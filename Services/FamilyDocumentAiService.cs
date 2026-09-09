using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using RecepcionDocumental.Configuration;

namespace RecepcionDocumental.Services
{
    public sealed class FamilyDocumentAiResult
    {
        public bool Attempted { get; set; }
        public string Status { get; set; }
        public string ModelVersion { get; set; }
        public string Family { get; set; }
        public double? Confidence { get; set; }
        public bool AboveDecisionThreshold { get; set; }
        public int RecognizedFeatures { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorReason { get; set; }
    }

    // H1D10D5: learned residual family classifier. It is intentionally not a replacement
    // for the deterministic resolver: callers may only use a high-confidence family result
    // after their own safety policy has accepted it.
    public static class FamilyDocumentAiService
    {
        public const string ExpectedModelVersion = "H1D10D5-FAMILY-001";
        public const string ExpectedModelSha256 = "F8EB7568FAC1613F13FB91ACB4A06A86D827A9A142C29544515F9195642C2A8B";
        private const string ExpectedModelType = "TFIDF_WORD_1_2_SGD_LOGLOSS_OVR";
        private const string ExpectedPreprocessingVersion = "D5_SANITIZE_003";
        private const double ExpectedDecisionThreshold = 0.80;
        private static readonly Regex RemoveExternal = new Regex(@"https?://\S+|www\.\S+|\b\S+@\S+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RemoveNumbers = new Regex(@"\b\d[\d.,:/\-]*\b", RegexOptions.Compiled);
        private static readonly Regex RemoveSeparators = new Regex(@"[_|]+", RegexOptions.Compiled);
        private static readonly string[] ExpectedDropWords = { "agil", "argentina", "black", "bna", "caba", "dario", "edi", "escalada", "estilo", "faecys", "insignia", "nacion", "omar", "osecac", "platinum", "regional", "remedios", "roca", "signature", "silveri", "silverii", "tucuman" };
        private static readonly Regex RemoveDeidentifiedTokens = new Regex(@"(?<![a-z0-9])(?:agil|argentina|black|bna|caba|dario|edi|escalada|estilo|faecys|insignia|nacion|omar|osecac|platinum|regional|remedios|roca|signature|silveri|silverii|tucuman)(?![a-z0-9])", RegexOptions.Compiled);
        private static readonly Regex CollapseWhitespace = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex TokenRegex = new Regex(@"[a-z0-9]{2,}", RegexOptions.Compiled);
        private static readonly Lazy<RuntimeState> Runtime = new Lazy<RuntimeState>(LoadRuntime, true);

        public static FamilyDocumentAiResult Evaluate(string ocrText)
        {
            var version = ConfiguracionSistema.Actual.FamilyAiModelVersion;
            var result = new FamilyDocumentAiResult { Attempted = true, Status = "ERROR", ModelVersion = version };
            try
            {
                ValidateVersion(version);
                var runtime = Runtime.Value;
                var input = Preprocess(ocrText, runtime.Contract.skip_chars, runtime.Contract.max_chars);
                if (string.IsNullOrWhiteSpace(input))
                {
                    result.Status = "OK";
                    result.Family = "OTRO_BASE";
                    result.Confidence = 0d;
                    result.AboveDecisionThreshold = false;
                    return result;
                }

                var tokens = TokenRegex.Matches(input).Cast<Match>().Select(x => x.Value).ToList();
                var counts = new Dictionary<int, int>();
                for (var i = 0; i < tokens.Count; i++)
                {
                    AddFeature(runtime, tokens[i], counts);
                    if (i + 1 < tokens.Count) AddFeature(runtime, tokens[i] + " " + tokens[i + 1], counts);
                }

                result.RecognizedFeatures = counts.Count;
                if (counts.Count == 0)
                {
                    result.Status = "OK";
                    result.Family = "OTRO_BASE";
                    result.Confidence = 0d;
                    result.AboveDecisionThreshold = false;
                    return result;
                }

                var weighted = new Dictionary<int, double>();
                var normSquared = 0d;
                foreach (var item in counts)
                {
                    var tf = 1d + Math.Log(item.Value);
                    var value = tf * runtime.Contract.idf[item.Key];
                    weighted[item.Key] = value;
                    normSquared += value * value;
                }
                if (normSquared <= 0d) throw new InvalidDataException("El vector TF-IDF no tiene norma positiva.");
                var norm = Math.Sqrt(normSquared);

                var raw = new double[runtime.Contract.classes.Length];
                var probability = new double[runtime.Contract.classes.Length];
                var probabilitySum = 0d;
                for (var c = 0; c < runtime.Contract.classes.Length; c++)
                {
                    var score = runtime.Contract.intercept[c];
                    foreach (var item in weighted)
                        score += runtime.Contract.coef[c][item.Key] * (item.Value / norm);
                    raw[c] = score;
                    probability[c] = Sigmoid(score);
                    probabilitySum += probability[c];
                }
                if (probabilitySum <= 0d || double.IsNaN(probabilitySum) || double.IsInfinity(probabilitySum))
                    throw new InvalidDataException("Las probabilidades del modelo no son válidas.");
                for (var c = 0; c < probability.Length; c++) probability[c] /= probabilitySum;

                var best = 0;
                for (var c = 1; c < probability.Length; c++) if (probability[c] > probability[best]) best = c;
                result.Status = "OK";
                result.Family = runtime.Contract.classes[best];
                result.Confidence = probability[best];
                result.AboveDecisionThreshold = !string.Equals(result.Family, "OTRO_BASE", StringComparison.Ordinal) && probability[best] >= runtime.Contract.decision_threshold;
                return result;
            }
            catch (Exception ex) when (IsExpectedRuntimeException(ex))
            {
                result.ErrorCode = ErrorCode(ex);
                result.ErrorReason = SafeReason(ex.Message);
                return result;
            }
        }

        private static void AddFeature(RuntimeState runtime, string term, IDictionary<int, int> counts)
        {
            int index;
            if (!runtime.Vocabulary.TryGetValue(term, out index)) return;
            int count;
            counts[index] = counts.TryGetValue(index, out count) ? count + 1 : 1;
        }

        private static RuntimeState LoadRuntime()
        {
            var version = ConfiguracionSistema.Actual.FamilyAiModelVersion;
            ValidateVersion(version);
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "DocumentAi", "FamilyModels", version);
            var modelPath = Path.Combine(directory, "family-model.json");
            if (!File.Exists(modelPath)) throw new FileNotFoundException("No se encontró el modelo de familias documentales.", modelPath);
            var actualHash = Sha256(modelPath);
            if (!string.Equals(actualHash, ExpectedModelSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El SHA-256 del modelo de familias documentales no coincide con la versión aprobada.");

            ModelContract contract;
            using (var stream = File.OpenRead(modelPath))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                contract = JsonConvert.DeserializeObject<ModelContract>(reader.ReadToEnd());
            ValidateContract(contract);

            var vocabulary = new Dictionary<string, int>(contract.vocabulary.Length, StringComparer.Ordinal);
            for (var i = 0; i < contract.vocabulary.Length; i++)
            {
                var term = contract.vocabulary[i];
                if (string.IsNullOrWhiteSpace(term) || vocabulary.ContainsKey(term)) throw new InvalidDataException("El vocabulario del modelo de familias es inválido.");
                vocabulary.Add(term, i);
            }
            return new RuntimeState { Contract = contract, Vocabulary = vocabulary };
        }

        private static void ValidateContract(ModelContract contract)
        {
            if (contract == null || contract.model_version != ExpectedModelVersion || contract.model_type != ExpectedModelType || contract.status != "ACTIVE" ||
                contract.preprocessing_version != ExpectedPreprocessingVersion || Math.Abs(contract.decision_threshold - ExpectedDecisionThreshold) > 0.000000001d)
                throw new InvalidDataException("El contrato del modelo de familias documentales no es el esperado.");
            if (contract.skip_chars < 0 || contract.max_chars <= 0 || contract.vocabulary == null || contract.vocabulary.Length == 0 ||
                contract.idf == null || contract.idf.Length != contract.vocabulary.Length || contract.classes == null || contract.classes.Length != 3 ||
                contract.drop_words == null || contract.drop_words.Length != ExpectedDropWords.Length ||
                contract.coef == null || contract.coef.Length != contract.classes.Length || contract.intercept == null || contract.intercept.Length != contract.classes.Length)
                throw new InvalidDataException("Las dimensiones del modelo de familias documentales no son válidas.");
            if (!contract.classes.Contains("RECIBO_HABERES") || !contract.classes.Contains("NOTIFICACION_BANCARIA") || !contract.classes.Contains("OTRO_BASE"))
                throw new InvalidDataException("Las clases del modelo de familias documentales no son las aprobadas.");
            if (!new HashSet<string>(contract.drop_words, StringComparer.Ordinal).SetEquals(ExpectedDropWords))
                throw new InvalidDataException("La política de desidentificación del modelo de familias no es la aprobada.");
            for (var c = 0; c < contract.coef.Length; c++)
                if (contract.coef[c] == null || contract.coef[c].Length != contract.vocabulary.Length)
                    throw new InvalidDataException("Los coeficientes del modelo de familias documentales no son válidos.");
        }

        internal static string PreprocessForValidation(string text, int skipChars, int maxChars)
        { return Preprocess(text, skipChars, maxChars); }

        private static string Preprocess(string text, int skipChars, int maxChars)
        {
            var source = text ?? string.Empty;
            if (skipChars >= source.Length) return string.Empty;
            source = source.Substring(skipChars, Math.Min(maxChars, source.Length - skipChars));
            var decomposed = source.Normalize(NormalizationForm.FormKD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c <= 127) builder.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c)) builder.Append(' ');
            }
            var normalized = RemoveExternal.Replace(builder.ToString(), " ");
            normalized = RemoveNumbers.Replace(normalized, " ");
            normalized = RemoveSeparators.Replace(normalized, " ");
            normalized = RemoveDeidentifiedTokens.Replace(normalized, " ");
            return CollapseWhitespace.Replace(normalized, " ").Trim();
        }

        private static double Sigmoid(double value)
        {
            if (value >= 0d) { var z = Math.Exp(-value); return 1d / (1d + z); }
            var e = Math.Exp(value); return e / (1d + e);
        }

        private static void ValidateVersion(string version)
        {
            if (!string.Equals(version, ExpectedModelVersion, StringComparison.Ordinal))
                throw new InvalidOperationException("La versión del modelo de familias documentales no está aprobada.");
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static bool IsExpectedRuntimeException(Exception ex)
        { return ex is FileNotFoundException || ex is InvalidDataException || ex is InvalidOperationException || ex is IOException || ex is DecoderFallbackException || ex is JsonException; }

        private static string ErrorCode(Exception ex)
        {
            if (ex is FileNotFoundException) return "MODEL_MISSING";
            if (ex is DecoderFallbackException || ex is JsonException) return "MODEL_INVALID_JSON";
            if (ex is InvalidOperationException) return "MODEL_VERSION_UNSUPPORTED";
            if (ex is InvalidDataException && ex.Message.IndexOf("SHA-256", StringComparison.OrdinalIgnoreCase) >= 0) return "MODEL_HASH_INVALID";
            if (ex is InvalidDataException) return "MODEL_CONTRACT_INVALID";
            return "MODEL_IO_ERROR";
        }

        private static string SafeReason(string value)
        {
            var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= 240 ? text : text.Substring(0, 240);
        }

        private sealed class RuntimeState
        {
            public ModelContract Contract;
            public Dictionary<string, int> Vocabulary;
        }

        private sealed class ModelContract
        {
            public string model_version { get; set; }
            public string model_type { get; set; }
            public string status { get; set; }
            public string preprocessing_version { get; set; }
            public int skip_chars { get; set; }
            public int max_chars { get; set; }
            public double decision_threshold { get; set; }
            public string[] drop_words { get; set; }
            public string[] classes { get; set; }
            public string[] vocabulary { get; set; }
            public double[] idf { get; set; }
            public double[][] coef { get; set; }
            public double[] intercept { get; set; }
        }
    }
}
