using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class ImageNonDocumentGateCorpusProbe
    {
        private const string DatasetHash = "AFECA7A2F995CE1B2DF6F9DAF3501A392CC7FB4DD1509900A19D1588E26794C2";

        internal static int Run(string[] args)
        {
            if (args.Length != 4) { Console.Error.WriteLine("Uso: --image-nondocument-corpus <dataset.csv> <baseline.csv> <output>"); return 2; }
            try
            {
                var dataset = Path.GetFullPath(args[1]); var baseline = Path.GetFullPath(args[2]); var output = Path.GetFullPath(args[3]);
                VerifyDataset(dataset); var rows = LoadDataset(dataset); var old = LoadBaseline(baseline);
                if (rows.Count != 80 || old.Count != 80) throw new InvalidDataException("Se esperaban 80 documentos.");
                Directory.CreateDirectory(output); Initialize(Path.Combine(output, "runtime"));
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i]; row.Before = old[row.Sha];
                    Console.WriteLine("IMAGE_GATE_CORPUS | " + (i + 1) + "/" + rows.Count + " | " + row.Sha);
                    Analyze(row);
                }
                Write(output, rows);
                var facturaDiscard = rows.Count(x => x.Label == "FACTURA" && x.After == "DESCARTAR");
                var changed = rows.Where(x => x.IsImage && x.Before == "REVISAR" && x.After == "DESCARTAR").ToList();
                var gate = facturaDiscard == 0 && changed.All(x => x.Method == "IA_VISUAL+OCR_GATE");
                Console.WriteLine("IMAGE_GATE_CORPUS | Filas=80 | ImagenesRevisarADescartar=" + changed.Count + " | FACTURA_A_DESCARTAR=" + facturaDiscard + " | Gate=" + gate);
                foreach (var row in changed) Console.WriteLine("CAMBIO | " + row.Sha + " | Label=" + row.Label + " | PFactura=" + Number(row.PFactura) + " | Zona=" + row.Zone + " | EvidenciaOCR=" + row.OcrEvidence + " | Motivo=" + row.Reason);
                return gate ? 0 : 1;
            }
            catch (Exception ex) { Console.Error.WriteLine("ERROR IMAGE_GATE_CORPUS | " + ex.GetType().Name + ": " + ex.Message); return 1; }
        }

        private static void Analyze(Row row)
        {
            using (var workspace = new AttachmentWorkspace())
            {
                var analysis = DocumentAnalysisService.Analyze(File.ReadAllBytes(row.Path), Path.GetFileName(row.Path), Mime(row.Path), workspace);
                var candidate = analysis.Candidates.SingleOrDefault(); var ai = analysis.AiDiscards.SingleOrDefault(); var deterministic = analysis.DeterministicDiscards.SingleOrDefault();
                var selection = candidate != null ? candidate.Selection : ai != null ? ai.Selection : deterministic == null ? null : deterministic.Selection;
                var visual = candidate != null ? candidate.VisualShadow : ai == null ? null : ai.VisualShadow;
                if (selection == null) throw new InvalidDataException("Sin selección para " + row.Sha);
                row.After = selection.Classification; row.Method = selection.DetectionMethod; row.Reason = selection.Reason;
                row.PFactura = visual == null ? null : visual.PFactura; row.Zone = visual == null ? null : visual.Zone;
                row.OcrEvidence = row.Method == "IA_VISUAL+OCR_GATE" ? "NINGUNA" : "NO_APLICA";
            }
        }

        private static void Initialize(string root)
        {
            var configuration = new ConfiguracionAplicacion("RecepcionDocumental", Path.Combine(root, "Logs"), Path.Combine(root, "Trabajo"), Path.Combine(root, "Facturas"), Path.Combine(root, "Revisar"), 200, 52428800, 262144000, 3, "https://localhost/image-gate", true, "H1D9B-CANDIDATE-001");
            configuration.PrepararRutasOperativas(); ConfiguracionSistema.Inicializar(configuration); Logs.Inicializar(configuration);
        }

        private static List<Row> LoadDataset(string path)
        {
            return ReadCsv(path, field => { var value = field("Path"); value = Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), value)); return new Row { Path = value, Sha = field("Sha256").ToUpperInvariant(), Label = field("Label"), IsImage = !string.Equals(Path.GetExtension(value), ".pdf", StringComparison.OrdinalIgnoreCase) }; });
        }

        private static Dictionary<string, string> LoadBaseline(string path)
        { return ReadCsv(path, field => new { Sha = field("Sha256"), Value = field("H1D8B") }).ToDictionary(x => x.Sha, x => x.Value, StringComparer.OrdinalIgnoreCase); }

        private static List<T> ReadCsv<T>(string path, Func<Func<string, string>, T> map)
        {
            var rows = new List<T>(); using (var parser = new TextFieldParser(path, Encoding.UTF8))
            {
                parser.TextFieldType = FieldType.Delimited; parser.HasFieldsEnclosedInQuotes = true; parser.SetDelimiters(",");
                var header = parser.ReadFields(); var columns = header.Select((name, index) => new { name, index }).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
                while (!parser.EndOfData) { var values = parser.ReadFields(); if (values != null) rows.Add(map(name => columns.ContainsKey(name) && columns[name] < values.Length ? values[columns[name]] : string.Empty)); }
            }
            return rows;
        }

        private static void Write(string output, IList<Row> rows)
        {
            var lines = new List<string> { "Sha256,Label,IsImage,Before,After,Method,PFactura,Zone,OcrEvidence,Reason" };
            lines.AddRange(rows.Select(x => Csv(x.Sha, x.Label, x.IsImage ? "true" : "false", x.Before, x.After, x.Method, Number(x.PFactura), x.Zone, x.OcrEvidence, x.Reason)));
            File.WriteAllLines(Path.Combine(output, "image-nondocument-corpus.csv"), lines, new UTF8Encoding(false));
            var changed = rows.Where(x => x.IsImage && x.Before == "REVISAR" && x.After == "DESCARTAR").ToList();
            var report = "# Image non-document gate\n\n- Procesados: " + rows.Count + "/80.\n- Imágenes REVISAR → DESCARTAR: " + changed.Count + ".\n- FACTURA → DESCARTAR: " + rows.Count(x => x.Label == "FACTURA" && x.After == "DESCARTAR") + ".\n\n" + string.Join("\n", changed.Select(x => "- " + x.Sha + "; label=" + x.Label + "; PFactura=" + Number(x.PFactura) + "; zona=" + x.Zone + "; evidencia OCR=" + x.OcrEvidence + "; motivo=" + x.Reason)) + "\n";
            File.WriteAllText(Path.Combine(output, "summary.md"), report, new UTF8Encoding(false));
        }

        private static string Mime(string path) { var extension = Path.GetExtension(path).ToLowerInvariant(); return extension == ".pdf" ? "application/pdf" : extension == ".png" ? "image/png" : "image/jpeg"; }
        private static string Number(double? value) { return value.HasValue ? value.Value.ToString("0.#########", CultureInfo.InvariantCulture) : string.Empty; }
        private static string Csv(params string[] values) { return string.Join(",", values.Select(x => "\"" + (x ?? string.Empty).Replace("\"", "\"\"") + "\"")); }
        private static void VerifyDataset(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) if (!string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty), DatasetHash, StringComparison.Ordinal)) throw new InvalidDataException("SHA dataset inesperado."); }

        private sealed class Row
        { internal string Path, Sha, Label, Before, After, Method, Zone, OcrEvidence, Reason; internal bool IsImage; internal double? PFactura; }
    }
}
