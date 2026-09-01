using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.VisualBasic.FileIO;

namespace PdfRasterProbe
{
    internal static class H1D9DVisualInferenceParityProbe
    {
        private const string DatasetHash = "AFECA7A2F995CE1B2DF6F9DAF3501A392CC7FB4DD1509900A19D1588E26794C2";
        private const string FrozenHash = "FADEA71A298125E8CE0EB65C31F6232EAAE72EB71F33141B912D23F4E59603E4";
        private const string FoldHash = "9E4A9ACC7DB4B042A96A28502745ADC32F78AF7866A45918000668C127D895D9";
        private const string OnnxHash = "A1DC24FE90C3C14303C3319EC0BD6D9EF95E91CC82C9B38E2FBAAEB0EF826811";
        private const string CheckpointHash = "F6F552CF5FAD856D7FB57352C63C4CD68C3E3E0F6C039C3C7623B030FD965F27";
        private const double TNoFactura = 0.1169927716255188;
        private const double TFactura = 0.7979831695556641;
        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

        internal static int Run(string[] args)
        {
            if (args.Length != 7)
            {
                Console.Error.WriteLine("Uso: --h1d9d-visual-parity <candidate.onnx> <python-reference.csv> <development-assets.csv> <holdout-assets.csv> <holdout-predictions.csv> <output>");
                return 2;
            }
            try { return Execute(args); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("H1D9D | Resultado=H1D9D NO APROBADO");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static int Execute(string[] args)
        {
            if (!Environment.Is64BitProcess) throw new BadImageFormatException("H1D9D requiere proceso x64.");
            var onnx = Path.GetFullPath(args[1]);
            var references = LoadReference(Path.GetFullPath(args[2]));
            var assets = LoadAssets(Path.GetFullPath(args[3]), "DEVELOPMENT")
                .Concat(LoadAssets(Path.GetFullPath(args[4]), "HOLDOUT")).ToDictionary(x => x.Sha256, StringComparer.OrdinalIgnoreCase);
            var priorHoldout = LoadHoldout(Path.GetFullPath(args[5]));
            var output = Path.GetFullPath(args[6]);
            Directory.CreateDirectory(output);
            if (!string.Equals(Sha256(onnx), OnnxHash, StringComparison.Ordinal)) throw new InvalidDataException("SHA-256 inesperado para candidate.onnx.");
            if (new FileInfo(onnx).Length != 16708744) throw new InvalidDataException("Tamaño inesperado para candidate.onnx.");
            if (references.Count != 80 || assets.Count != 80 || references.Keys.Any(x => !assets.ContainsKey(x)))
                throw new InvalidDataException("El universo no coincide con 80 SHA únicos.");

            var privateBefore = PrivateMemory();
            var results = new List<Result>();
            using (var options = new SessionOptions())
            using (var session = new InferenceSession(onnx, options))
            {
                ValidateMetadata(session);
                foreach (var reference in references.Values)
                {
                    var asset = assets[reference.Sha256];
                    if (!string.Equals(asset.Cohort, reference.Cohort, StringComparison.Ordinal) || !File.Exists(asset.Path))
                        throw new InvalidDataException("Asset/cohort inconsistente: " + reference.Sha256);
                    var total = Stopwatch.StartNew();
                    var preprocessing = Stopwatch.StartNew();
                    var tensor = Preprocess(asset.Path);
                    preprocessing.Stop();
                    var inference = Stopwatch.StartNew();
                    float pNo, pYes;
                    using (var input = OrtValue.CreateTensorValueFromMemory(tensor, new long[] { 1, 3, 224, 224 }))
                    using (var runOptions = new RunOptions())
                    using (var outputs = session.Run(runOptions, new[] { "image" }, new[] { input }, new[] { "probabilities" }))
                    {
                        var probabilities = outputs[0].GetTensorDataAsSpan<float>();
                        if (probabilities.Length != 2) throw new InvalidDataException("Salida ONNX no contiene dos probabilidades.");
                        pNo = probabilities[0]; pYes = probabilities[1];
                    }
                    inference.Stop(); total.Stop();
                    if (Math.Abs((pNo + pYes) - 1.0) > 0.00001) throw new InvalidDataException("Probabilidades no suman 1: " + reference.Sha256);
                    results.Add(new Result { Reference = reference, Asset = asset, PNo = pNo, PYes = pYes,
                        PreprocessMs = preprocessing.Elapsed.TotalMilliseconds, InferenceMs = inference.Elapsed.TotalMilliseconds, TotalMs = total.Elapsed.TotalMilliseconds });
                }
            }
            var privateAfter = PrivateMemory();

            var predEqual = results.Count(x => x.PredEqual);
            var zoneEqual = results.Count(x => x.ZoneEqual);
            var deltas = results.Select(x => x.Delta).OrderBy(x => x).ToList();
            var gateA = results.Count == 80 && results.Select(x => x.Reference.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 80;
            var gateB = predEqual == 80;
            var gateC = zoneEqual == 80;
            var gateD = Percentile(deltas, .95) <= .01 && deltas.Last() <= .025;
            var holdout = results.Where(x => x.Asset.Cohort == "HOLDOUT").ToList();
            var fileCorrect = holdout.Count(x => string.Equals(x.CSharpPred, x.Asset.Label, StringComparison.Ordinal));
            var groupCorrect = holdout.GroupBy(x => x.Asset.GroupId).Count(g =>
            {
                var score = g.Average(x => (double)x.PYes);
                return string.Equals(score >= .5 ? "FACTURA" : "NO_FACTURA", g.First().Asset.Label, StringComparison.Ordinal);
            });
            var dangerous = holdout.Count(x => (x.Asset.Label == "FACTURA" && x.CSharpZone == "NO_FACTURA_FUERTE") ||
                                                 (x.Asset.Label == "NO_FACTURA" && x.CSharpZone == "FACTURA_FUERTE"));
            var priorEqual = holdout.Count(x => priorHoldout.ContainsKey(x.Reference.Sha256) &&
                                                string.Equals(priorHoldout[x.Reference.Sha256], x.Reference.Pred, StringComparison.Ordinal));
            var gateE = holdout.Count == 10 && fileCorrect == 10 && groupCorrect == 5 && dangerous == 0 && priorEqual == 10;
            var approved = gateA && gateB && gateC && gateD && gateE;

            WriteComparison(Path.Combine(output, "csharp-vs-python.csv"), results);
            WriteMetrics(Path.Combine(output, "parity-metrics.md"), results, predEqual, zoneEqual, fileCorrect, groupCorrect, dangerous, gateA, gateB, gateC, gateD, gateE);
            WriteSummary(Path.Combine(output, "resumen.md"), results, approved, predEqual, zoneEqual, fileCorrect, groupCorrect, dangerous, gateA, gateB, gateC, gateD, gateE);
            WriteManifest(Path.Combine(output, "parity-manifest.json"), results, predEqual, zoneEqual, fileCorrect, groupCorrect,
                dangerous, gateA, gateB, gateC, gateD, gateE, privateBefore, privateAfter, approved);

            Console.WriteLine("H1D9D | Resultado=" + (approved ? "H1D9D APROBADO" : "H1D9D NO APROBADO") +
                              " | Processed=" + results.Count + " | PredEqual=" + predEqual + "/80 | ZoneEqual=" + zoneEqual +
                              "/80 | MeanDelta=" + F(deltas.Average()) + " | P95Delta=" + F(Percentile(deltas, .95)) + " | MaxDelta=" + F(deltas.Last()));
            return approved ? 0 : 1;
        }

        private static float[] Preprocess(string path)
        {
            using (var source = Image.FromFile(path))
            using (var canvas = new Bitmap(224, 224, PixelFormat.Format24bppRgb))
            {
                var scale = Math.Min(224.0 / source.Width, 224.0 / source.Height);
                var width = Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.ToEven));
                var height = Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.ToEven));
                var left = (224 - width) / 2;
                var top = (224 - height) / 2;
                using (var graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.White);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(source, new Rectangle(left, top, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                    }
                }
                var tensor = new float[3 * 224 * 224];
                var rectangle = new Rectangle(0, 0, 224, 224);
                var data = canvas.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var bytes = Math.Abs(data.Stride) * 224;
                    var pixels = new byte[bytes];
                    Marshal.Copy(data.Scan0, pixels, 0, bytes);
                    for (var y = 0; y < 224; y++)
                    for (var x = 0; x < 224; x++)
                    {
                        var sourceIndex = y * data.Stride + x * 3;
                        var destinationIndex = y * 224 + x;
                        tensor[destinationIndex] = ((pixels[sourceIndex + 2] / 255f) - Mean[0]) / Std[0];
                        tensor[224 * 224 + destinationIndex] = ((pixels[sourceIndex + 1] / 255f) - Mean[1]) / Std[1];
                        tensor[2 * 224 * 224 + destinationIndex] = ((pixels[sourceIndex] / 255f) - Mean[2]) / Std[2];
                    }
                }
                finally { canvas.UnlockBits(data); }
                return tensor;
            }
        }

        private static void ValidateMetadata(InferenceSession session)
        {
            NodeMetadata input, output;
            if (session.InputMetadata.Count != 1 || !session.InputMetadata.TryGetValue("image", out input) ||
                input.ElementType != typeof(float) || !input.Dimensions.SequenceEqual(new[] { 1, 3, 224, 224 }))
                throw new InvalidDataException("Metadata input inesperada.");
            if (session.OutputMetadata.Count != 1 || !session.OutputMetadata.TryGetValue("probabilities", out output) ||
                output.ElementType != typeof(float) || !output.Dimensions.SequenceEqual(new[] { 1, 2 }))
                throw new InvalidDataException("Metadata output inesperada.");
        }

        private static Dictionary<string, Reference> LoadReference(string path)
        {
            return ReadCsv(path).Select(r => new Reference { Sha256 = r["Sha256"], Cohort = r["Cohort"],
                PYes = double.Parse(r["PFactura"], CultureInfo.InvariantCulture), Pred = r["Pred050"], Zone = r["ZonaPreRegistrada"] })
                .ToDictionary(x => x.Sha256, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<Asset> LoadAssets(string path, string cohort)
        {
            return ReadCsv(path).Select(r => new Asset { Sha256 = r["Sha256"], Cohort = cohort, GroupId = r["GroupId"],
                Label = r["LabelBinario"], Path = r["VisualAssetPath"] });
        }

        private static Dictionary<string, string> LoadHoldout(string path)
        {
            return ReadCsv(path).ToDictionary(r => r["Sha256"], r => r["Pred050"], StringComparer.OrdinalIgnoreCase);
        }

        private static List<Dictionary<string, string>> ReadCsv(string path)
        {
            var rows = new List<Dictionary<string, string>>();
            using (var parser = new TextFieldParser(path, Encoding.UTF8))
            {
                parser.TextFieldType = FieldType.Delimited; parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
                var headers = parser.ReadFields();
                while (!parser.EndOfData)
                {
                    var fields = parser.ReadFields();
                    if (fields == null || fields.All(string.IsNullOrEmpty)) continue;
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < headers.Length; i++) row[headers[i]] = i < fields.Length ? fields[i] : string.Empty;
                    rows.Add(row);
                }
            }
            return rows;
        }

        private static void WriteComparison(string path, IEnumerable<Result> results)
        {
            var lines = new List<string> { "Sha256,Cohort,PythonPFactura,CSharpPFactura,AbsDelta,PythonPred050,CSharpPred050,PythonZona,CSharpZona,Pred050Equal,ZonaEqual" };
            lines.AddRange(results.Select(x => string.Join(",", x.Reference.Sha256, x.Reference.Cohort, F9(x.Reference.PYes), F9(x.PYes),
                F9(x.Delta), x.Reference.Pred, x.CSharpPred, x.Reference.Zone, x.CSharpZone, Lower(x.PredEqual), Lower(x.ZoneEqual))));
            WriteLf(path, string.Join("\n", lines) + "\n");
        }

        private static void WriteMetrics(string path, List<Result> results, int predEqual, int zoneEqual, int fileCorrect, int groupCorrect, int dangerous,
            bool a, bool b, bool c, bool d, bool e)
        {
            var deltas = results.Select(x => x.Delta).OrderBy(x => x).ToList();
            var worst = results.OrderByDescending(x => x.Delta).Take(5).ToList();
            var text = new StringBuilder();
            text.AppendLine("# H1D9D — Métricas de paridad Python ↔ C#").AppendLine();
            text.AppendLine("- Procesadas: 80/80; SHA únicos: 80.");
            text.AppendLine("- Pred050 iguales: " + predEqual + "/80.");
            text.AppendLine("- Zonas congeladas iguales: " + zoneEqual + "/80.");
            text.AppendLine("- Delta absoluto: media " + F(deltas.Average()) + ", mediana " + F(Percentile(deltas, .5)) + ", P95 " + F(Percentile(deltas, .95)) + ", máximo " + F(deltas.Last()) + ".");
            text.AppendLine("- C# preprocessing ms: " + Stats(results.Select(x => x.PreprocessMs)) + ".");
            text.AppendLine("- C# ONNX ms: " + Stats(results.Select(x => x.InferenceMs)) + ".");
            text.AppendLine("- C# total ms: " + Stats(results.Select(x => x.TotalMs)) + ".");
            text.AppendLine("- HOLDOUT C#: archivos " + fileCorrect + "/10, grupos " + groupCorrect + "/5, errores fuertes peligrosos " + dangerous + ".").AppendLine();
            text.AppendLine("## Peores cinco deltas").AppendLine();
            text.AppendLine("| SHA-256 | Cohort | Python | C# | Delta |").AppendLine("|---|---:|---:|---:|---:|");
            foreach (var x in worst) text.AppendLine("| " + x.Reference.Sha256 + " | " + x.Reference.Cohort + " | " + F9(x.Reference.PYes) + " | " + F9(x.PYes) + " | " + F9(x.Delta) + " |");
            text.AppendLine().AppendLine("## Gates").AppendLine();
            text.AppendLine("- Gate A integridad: " + Pass(a) + ".").AppendLine("- Gate B decisión 0.5: " + Pass(b) + ".")
                .AppendLine("- Gate C zonas congeladas: " + Pass(c) + ".").AppendLine("- Gate D deriva numérica: " + Pass(d) + ".")
                .AppendLine("- Gate E reproducción H1D9C: " + Pass(e) + ".");
            WriteLf(path, text.ToString());
        }

        private static void WriteSummary(string path, List<Result> results, bool approved, int predEqual, int zoneEqual, int fileCorrect, int groupCorrect, int dangerous,
            bool a, bool b, bool c, bool d, bool e)
        {
            var deltas = results.Select(x => x.Delta).OrderBy(x => x).ToList();
            var text = "# H1D9D — Resultado\n\n`" + (approved ? "H1D9D APROBADO" : "H1D9D NO APROBADO") + "`\n\n" +
                       "Se procesaron 80/80 assets canónicos con 80 SHA únicos usando una única sesión ONNX Runtime CPU en .NET Framework 4.8 x64. " +
                       "Pred050 coincidió en " + predEqual + "/80 y la zona congelada en " + zoneEqual + "/80. " +
                       "Delta absoluto medio " + F(deltas.Average()) + ", P95 " + F(Percentile(deltas, .95)) + " y máximo " + F(deltas.Last()) + ".\n\n" +
                       "HOLDOUT desde C#: " + fileCorrect + "/10 archivos correctos, " + groupCorrect + "/5 grupos correctos y " + dangerous + " errores fuertes peligrosos.\n\n" +
                       "Gates: A=" + Pass(a) + ", B=" + Pass(b) + ", C=" + Pass(c) + ", D=" + Pass(d) + ", E=" + Pass(e) + ".\n\n" +
                       "No hubo entrenamiento, ajuste de thresholds ni modificación del producto. TEST se usó exclusivamente para validar equivalencia técnica posterior a H1D9C.\n";
            WriteLf(path, text);
        }

        private static void WriteManifest(string path, List<Result> results, int predEqual, int zoneEqual, int fileCorrect, int groupCorrect, int dangerous,
            bool a, bool b, bool c, bool d, bool e, long memoryBefore, long memoryAfter, bool approved)
        {
            var deltas = results.Select(x => x.Delta).OrderBy(x => x).ToList();
            var ort = typeof(InferenceSession).Assembly.GetName().Version.ToString();
            var json = "{\n" +
                "  \"milestone\": \"H1D9D\",\n  \"status\": \"" + (approved ? "APROBADO" : "NO_APROBADO") + "\",\n" +
                "  \"frozen_hashes\": {\"dataset.csv\": \"" + DatasetHash + "\", \"frozen-test-groups.txt\": \"" + FrozenHash + "\", \"fold-manifest.csv\": \"" + FoldHash + "\", \"candidate.onnx\": \"" + OnnxHash + "\", \"candidate-checkpoint.pt\": \"" + CheckpointHash + "\"},\n" +
                "  \"runtime\": {\"onnx_runtime\": \"" + ort + "\", \"execution_provider\": \"CPUExecutionProvider\", \"framework\": \".NET Framework 4.8\", \"process_x64\": true},\n" +
                "  \"class_mapping\": {\"NO_FACTURA\": 0, \"FACTURA\": 1},\n" +
                "  \"thresholds\": {\"no_factura\": " + TNoFactura.ToString("R", CultureInfo.InvariantCulture) + ", \"factura\": " + TFactura.ToString("R", CultureInfo.InvariantCulture) + "},\n" +
                "  \"python_preprocessing\": \"Pillow BICUBIC; RGB; complete image; aspect-fit 224x224; Python round-to-even; centered white canvas; [0,1]; ImageNet mean/std; NCHW\",\n" +
                "  \"csharp_preprocessing\": \"System.Drawing HighQualityBicubic; RGB; complete image; aspect-fit 224x224; MidpointRounding.ToEven; centered white canvas; [0,1]; ImageNet mean/std; NCHW\",\n" +
                "  \"evaluated\": {\"total\": 80, \"development\": 70, \"holdout\": 10, \"unique_sha256\": 80},\n" +
                "  \"delta_abs\": {\"mean\": " + F9(deltas.Average()) + ", \"median\": " + F9(Percentile(deltas, .5)) + ", \"p95\": " + F9(Percentile(deltas, .95)) + ", \"max\": " + F9(deltas.Last()) + "},\n" +
                "  \"equalities\": {\"pred050\": " + predEqual + ", \"zones\": " + zoneEqual + "},\n" +
                "  \"csharp_timings_ms\": {\"preprocessing\": " + StatsJson(results.Select(x => x.PreprocessMs)) + ", \"onnx\": " + StatsJson(results.Select(x => x.InferenceMs)) + ", \"total\": " + StatsJson(results.Select(x => x.TotalMs)) + "},\n" +
                "  \"memory_private_bytes\": {\"before\": " + memoryBefore + ", \"after\": " + memoryAfter + ", \"delta\": " + (memoryAfter - memoryBefore) + "},\n" +
                "  \"holdout_csharp\": {\"file_predictions_correct\": " + fileCorrect + ", \"group_predictions_correct\": " + groupCorrect + ", \"dangerous_strong_errors\": " + dangerous + "},\n" +
                "  \"gates\": {\"A_integrity\": " + Lower(a) + ", \"B_pred050\": " + Lower(b) + ", \"C_zones\": " + Lower(c) + ", \"D_numeric_drift\": " + Lower(d) + ", \"E_holdout\": " + Lower(e) + "},\n" +
                "  \"training_performed\": false,\n  \"threshold_tuning_performed\": false,\n  \"product_modified\": false\n}\n";
            WriteLf(path, json);
        }

        private static string Stats(IEnumerable<double> values) { var x = values.OrderBy(v => v).ToList(); return "media " + F(x.Average()) + ", P50 " + F(Percentile(x, .5)) + ", P95 " + F(Percentile(x, .95)) + ", máximo " + F(x.Last()); }
        private static string StatsJson(IEnumerable<double> values) { var x = values.OrderBy(v => v).ToList(); return "{\"mean\": " + F(x.Average()) + ", \"p50\": " + F(Percentile(x, .5)) + ", \"p95\": " + F(Percentile(x, .95)) + ", \"max\": " + F(x.Last()) + "}"; }
        private static double Percentile(IReadOnlyList<double> sorted, double percentile) { var rank = (sorted.Count - 1) * percentile; var lower = (int)Math.Floor(rank); var upper = (int)Math.Ceiling(rank); return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower); }
        private static string Zone(double p) { return p <= TNoFactura ? "NO_FACTURA_FUERTE" : p >= TFactura ? "FACTURA_FUERTE" : "INCIERTO_VISUAL"; }
        private static string Sha256(string path) { using (var a = SHA256.Create()) using (var s = File.OpenRead(path)) return BitConverter.ToString(a.ComputeHash(s)).Replace("-", ""); }
        private static long PrivateMemory() { using (var p = Process.GetCurrentProcess()) { p.Refresh(); return p.PrivateMemorySize64; } }
        private static void WriteLf(string path, string text) { File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false)); }
        private static string F(double x) { return x.ToString("0.######", CultureInfo.InvariantCulture); }
        private static string F9(double x) { return x.ToString("0.000000000", CultureInfo.InvariantCulture); }
        private static string Lower(bool x) { return x ? "true" : "false"; }
        private static string Pass(bool x) { return x ? "PASS" : "FAIL"; }

        private sealed class Reference { public string Sha256; public string Cohort; public double PYes; public string Pred; public string Zone; }
        private sealed class Asset { public string Sha256; public string Cohort; public string GroupId; public string Label; public string Path; }
        private sealed class Result
        {
            public Reference Reference; public Asset Asset; public float PNo; public float PYes; public double PreprocessMs; public double InferenceMs; public double TotalMs;
            public double Delta { get { return Math.Abs(Reference.PYes - PYes); } }
            public string CSharpPred { get { return PYes >= .5f ? "FACTURA" : "NO_FACTURA"; } }
            public string CSharpZone { get { return H1D9DVisualInferenceParityProbe.Zone(PYes); } }
            public bool PredEqual { get { return string.Equals(Reference.Pred, CSharpPred, StringComparison.Ordinal); } }
            public bool ZoneEqual { get { return string.Equals(Reference.Zone, CSharpZone, StringComparison.Ordinal); } }
        }
    }
}
