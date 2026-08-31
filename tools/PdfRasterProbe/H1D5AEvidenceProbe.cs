using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class H1D5AEvidenceProbe
    {
        private const string ExpectedDatasetHash = "AF1FCC230B279D7DA4ACD77F5641BB1835D7D42EA0E8A41AD524D473FEC48158";

        internal static int Run(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("Uso: --h1d5a-evidence <dataset.csv> <output>");
                return 2;
            }
            try
            {
                var dataset = Path.GetFullPath(args[1]);
                var output = Path.GetFullPath(args[2]);
                ValidateDataset(dataset);
                var splitPath = Path.Combine(Path.GetDirectoryName(dataset), "experiments", "H1D4A", "split-manifest.csv");
                var splits = LoadSplit(splitPath);
                var rows = LoadDataset(dataset);
                Directory.CreateDirectory(output);
                var runtime = Path.Combine(Path.GetTempPath(), "RecepcionDocumental-H1D5A-" + Guid.NewGuid().ToString("N"));
                try
                {
                    InitializeRuntime(runtime);
                    var audits = new List<Audit>();
                    foreach (var row in rows)
                    {
                        row.Split = splits[row.Sha256];
                        Console.WriteLine("H1D5A | " + (audits.Count + 1) + "/" + rows.Count + " | " + row.Sha256);
                        audits.Add(AuditFile(row));
                    }
                    WriteEvidence(Path.Combine(output, "evidence-audit.csv"), audits);
                    WriteGroups(Path.Combine(output, "group-summary.csv"), audits);
                    WriteProblems(Path.Combine(output, "problem-cases.csv"), audits);
                    WriteMetrics(Path.Combine(output, "metrics.md"), audits);
                    WriteSummary(Path.Combine(output, "resumen.md"), audits);
                    Console.WriteLine("H1D5A_COMPLETO | Filas=" + audits.Count + " | Hashes=" + audits.Select(x => x.Sha256).Distinct().Count() + " | Grupos=" + audits.Select(x => x.GroupId).Distinct().Count() + " | Output=" + output);
                }
                finally { if (Directory.Exists(runtime)) Directory.Delete(runtime, true); }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR H1D5A | " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static void InitializeRuntime(string root)
        {
            var configuration = new ConfiguracionAplicacion(
                "RecepcionDocumental", Path.Combine(root, "Logs"), Path.Combine(root, "Trabajo"),
                Path.Combine(root, "Facturas"), Path.Combine(root, "Revisar"),
                100, 25L * 1024 * 1024, 100L * 1024 * 1024, 3, "https://localhost/h1d5a");
            configuration.PrepararRutasOperativas();
            ConfiguracionSistema.Inicializar(configuration);
            Logs.Inicializar(configuration);
        }

        private static Audit AuditFile(Row row)
        {
            var audit = new Audit(row);
            if (row.PhysicalFormat == "PDF")
            {
                var mdoc = MdocPdfTextExtractor.Extract(row.Path);
                audit.MdocExecuted = true;
                audit.MdocHasUsefulText = mdoc.HasUsefulText;
                audit.MdocFailureReason = mdoc.FailureReason;
                audit.Mdoc = TextMetrics.Measure(mdoc.Text);
                audit.MdocSelection = InvoiceSelector.SelectPdf(mdoc.Text, mdoc.HasUsefulText);
                audit.Qr = MdocPdfQrDetector.Detect(row.Path);
                audit.QrMdocSelection = ArcaQrDecoder.Combine(audit.Qr, audit.MdocSelection);
                using (var workspace = new AttachmentWorkspace())
                {
                    var raster = PdfPageRasterizer.Rasterize(row.Path, workspace);
                    audit.RasterPageCount = raster.PageCount;
                    audit.RasterDurationMs = raster.DurationMilliseconds;
                    audit.OcrFailureReason = raster.FailureReason;
                    audit.OcrLimitExceeded = raster.LimitExceeded;
                    audit.OcrStructuralFailure = raster.StructuralFailure;
                    if (raster.Images.Count > 0) RunOcr(audit, raster.Images);
                }
                if (audit.OcrSelection == null)
                    audit.OcrSelection = InvoiceSelector.SelectOcrText(string.Empty, false);
                audit.QrOcrSelection = ArcaQrDecoder.Combine(audit.Qr, audit.OcrSelection);
            }
            else
            {
                audit.Qr = new ArcaQrEvidence();
                var ocr = DocumentOcrService.RecognizeImageFile(row.Path);
                ApplyOcr(audit, ocr, false, 0);
                var selection = InvoiceSelector.SelectOcrText(ocr.Text, ocr.HasUsefulText);
                if (selection.Classification == "REVISAR")
                {
                    var header = DocumentOcrService.RecognizeImageHeader(row.Path);
                    if (header.Success)
                    {
                        ocr = DocumentOcrService.Combine(ocr, header);
                        ApplyOcr(audit, ocr, true, header.DurationMilliseconds);
                        selection = InvoiceSelector.SelectOcrText(ocr.Text, ocr.HasUsefulText);
                    }
                }
                audit.OcrSelection = selection;
            }
            audit.DetermineProblems();
            return audit;
        }

        private static void RunOcr(Audit audit, IList<OcrImageData> images)
        {
            var ocr = DocumentOcrService.Recognize(images);
            ApplyOcr(audit, ocr, false, 0);
            var selection = InvoiceSelector.SelectOcrText(ocr.Text, ocr.HasUsefulText);
            if (selection.Classification == "REVISAR")
            {
                var header = DocumentOcrService.RecognizeHeader(images);
                if (header.Success)
                {
                    ocr = DocumentOcrService.Combine(ocr, header);
                    ApplyOcr(audit, ocr, true, header.DurationMilliseconds);
                    selection = InvoiceSelector.SelectOcrText(ocr.Text, ocr.HasUsefulText);
                }
            }
            audit.OcrSelection = selection;
        }

        private static void ApplyOcr(Audit audit, OcrResult ocr, bool headerUsed, int headerMs)
        {
            audit.OcrExecuted = true;
            audit.OcrSuccess = ocr.Success;
            audit.OcrHasUsefulText = ocr.HasUsefulText;
            audit.Ocr = TextMetrics.Measure(ocr.Text);
            audit.OcrMeanConfidence = ocr.MeanConfidence;
            audit.OcrImagesProcessed = ocr.ImagesProcessed;
            audit.OcrDurationMs = ocr.DurationMilliseconds;
            audit.OcrFailureReason = ocr.FailureReason;
            audit.OcrSystemFailure = ocr.SystemFailure;
            audit.OcrHeaderUsed = headerUsed;
            audit.OcrHeaderDurationMs = headerMs;
        }

        private static void ValidateDataset(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("No se encontró dataset.csv.", path);
            var hash = Hash(path);
            if (hash != ExpectedDatasetHash) throw new InvalidDataException("SHA-256 dataset.csv inesperado: " + hash);
            var rows = LoadDataset(path);
            if (rows.Count != 80 || rows.Select(x => x.Sha256).Distinct().Count() != 80 || rows.Select(x => x.GroupId).Distinct().Count() != 54)
                throw new InvalidDataException("Corpus inesperado: filas=" + rows.Count + ", hashes=" + rows.Select(x => x.Sha256).Distinct().Count() + ", grupos=" + rows.Select(x => x.GroupId).Distinct().Count());
            var labels = rows.GroupBy(x => x.Label).ToDictionary(x => x.Key, x => x.Count());
            var formats = rows.GroupBy(x => x.PhysicalFormat).ToDictionary(x => x.Key, x => x.Count());
            if (labels.Get("FACTURA") != 24 || labels.Get("OTRO_DOCUMENTO") != 26 || labels.Get("NO_DOCUMENTO") != 30 || formats.Get("PDF") != 36 || formats.Get("PNG") != 30 || formats.Get("JPEG") != 14)
                throw new InvalidDataException("Distribución del corpus inesperada.");
            foreach (var row in rows)
                if (!File.Exists(row.Path) || Hash(row.Path) != row.Sha256) throw new InvalidDataException("Archivo ausente o hash distinto: " + row.Sha256);
        }

        private static List<Row> LoadDataset(string path)
        {
            return ReadCsv(path, f =>
            {
                var stored = f("Path");
                var full = Path.IsPathRooted(stored) ? stored : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), stored));
                return new Row { Path = full, Label = f("Label"), GroupId = f("GroupId"), Sha256 = f("Sha256").ToUpperInvariant(), PhysicalFormat = DetectFormat(full) };
            });
        }

        private static Dictionary<string, string> LoadSplit(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("No se encontró split-manifest.csv.", path);
            var rows = ReadCsv(path, f => new KeyValuePair<string, string>(f("Sha256").ToUpperInvariant(), f("Split")));
            var result = rows.ToDictionary(x => x.Key, x => x.Value);
            if (result.Count != 80) throw new InvalidDataException("El split H1D4A no contiene 80 hashes únicos.");
            return result;
        }

        private static List<T> ReadCsv<T>(string path, Func<Func<string, string>, T> map)
        {
            var result = new List<T>();
            using (var parser = new TextFieldParser(path, Encoding.UTF8))
            {
                parser.TextFieldType = FieldType.Delimited; parser.HasFieldsEnclosedInQuotes = true; parser.SetDelimiters(",");
                var header = parser.ReadFields();
                var columns = header.Select((n, i) => new { n, i }).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase);
                while (!parser.EndOfData)
                {
                    var fields = parser.ReadFields(); if (fields == null) continue;
                    Func<string, string> get = n => columns.ContainsKey(n) && columns[n] < fields.Length ? fields[columns[n]] : string.Empty;
                    result.Add(map(get));
                }
            }
            return result;
        }

        private static string DetectFormat(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var b = new byte[8]; if (stream.Read(b, 0, b.Length) < 3) throw new InvalidDataException("Archivo demasiado corto.");
                if (b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46) return "PDF";
                if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4e && b[3] == 0x47) return "PNG";
                if (b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff) return "JPEG";
            }
            throw new InvalidDataException("Formato físico no soportado: " + Path.GetFileName(path));
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static void WriteEvidence(string path, IList<Audit> rows)
        {
            var header = new[] { "Sha256","Label","GroupId","Split","PhysicalFormat","MdocExecuted","MdocHasUsefulText","MdocTextLen","MdocAlphanumeric","MdocLetters","MdocUsefulWords","MdocNul","MdocControl","MdocReplacement","MdocPrintableRatio","MdocSpaceRatio","MdocTokens","MdocSingleCharTokens","MdocSingleCharTokenRatio","MdocLongTokenCount","MdocFailureReason","MdocClassification","MdocMethod","MdocConfidence","MdocReason","OcrExecuted","OcrSuccess","OcrHasUsefulText","OcrTextLen","OcrAlphanumeric","OcrLetters","OcrUsefulWords","OcrMeanConfidence","OcrImagesProcessed","RasterPageCount","RasterDurationMs","OcrDurationMs","OcrHeaderUsed","OcrHeaderDurationMs","OcrLimitExceeded","OcrStructuralFailure","OcrSystemFailure","OcrFailureReason","OcrClassification","OcrMethod","OcrConfidence","OcrReason","QrDetected","QrValid","TipoComprobanteArca","QrMdocClassification","QrMdocMethod","QrMdocConfidence","QrMdocReason","QrOcrClassification","QrOcrMethod","QrOcrConfidence","QrOcrReason","ProblemFlags" };
            var lines = new List<string> { string.Join(",", header) };
            lines.AddRange(rows.Select(a => string.Join(",", new[] { a.Sha256,a.Label,a.GroupId,a.Split,a.PhysicalFormat,B(a.MdocExecuted),B(a.MdocHasUsefulText),N(a.Mdoc.Length),N(a.Mdoc.Alphanumeric),N(a.Mdoc.Letters),N(a.Mdoc.UsefulWords),N(a.Mdoc.Nul),N(a.Mdoc.Control),N(a.Mdoc.Replacement),D(a.Mdoc.PrintableRatio),D(a.Mdoc.SpaceRatio),N(a.Mdoc.Tokens),N(a.Mdoc.SingleCharTokens),D(a.Mdoc.SingleCharTokenRatio),N(a.Mdoc.LongTokens),a.MdocFailureReason,S(a.MdocSelection,"C"),S(a.MdocSelection,"M"),S(a.MdocSelection,"F"),S(a.MdocSelection,"R"),B(a.OcrExecuted),B(a.OcrSuccess),B(a.OcrHasUsefulText),N(a.Ocr.Length),N(a.Ocr.Alphanumeric),N(a.Ocr.Letters),N(a.Ocr.UsefulWords),D(a.OcrMeanConfidence),N(a.OcrImagesProcessed),N(a.RasterPageCount),N(a.RasterDurationMs),N(a.OcrDurationMs),B(a.OcrHeaderUsed),N(a.OcrHeaderDurationMs),B(a.OcrLimitExceeded),B(a.OcrStructuralFailure),B(a.OcrSystemFailure),a.OcrFailureReason,S(a.OcrSelection,"C"),S(a.OcrSelection,"M"),S(a.OcrSelection,"F"),S(a.OcrSelection,"R"),B(a.Qr.QrDetected),B(a.Qr.IsValid),a.Qr.TipoComprobante.HasValue ? N(a.Qr.TipoComprobante.Value) : "",S(a.QrMdocSelection,"C"),S(a.QrMdocSelection,"M"),S(a.QrMdocSelection,"F"),S(a.QrMdocSelection,"R"),S(a.QrOcrSelection,"C"),S(a.QrOcrSelection,"M"),S(a.QrOcrSelection,"F"),S(a.QrOcrSelection,"R"),string.Join(";",a.Problems) }.Select(Csv))));
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static void WriteGroups(string path, IList<Audit> rows)
        {
            var lines = new List<string> { "GroupId,Files,Pdfs,Labels,Splits,MdocUseful,MdocDegraded,OcrAvailable,MdocOcrClassChanges,QrValid,FacturaToDiscardMdoc,FacturaToDiscardOcr" };
            foreach (var g in rows.GroupBy(x => x.GroupId).OrderBy(x => x.Key))
                lines.Add(string.Join(",", new[] { g.Key,N(g.Count()),N(g.Count(x=>x.PhysicalFormat=="PDF")),string.Join(";",g.Select(x=>x.Label).Distinct()),string.Join(";",g.Select(x=>x.Split).Distinct()),N(g.Count(x=>x.MdocHasUsefulText)),N(g.Count(x=>x.Problems.Contains("MDOC_DEGRADADO"))),N(g.Count(x=>x.OcrSuccess)),N(g.Count(x=>Changed(x))),N(g.Count(x=>x.Qr.IsValid)),N(g.Count(x=>x.Label=="FACTURA"&&S(x.MdocSelection,"C")=="DESCARTAR")),N(g.Count(x=>x.Label=="FACTURA"&&S(x.OcrSelection,"C")=="DESCARTAR")) }.Select(Csv)));
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static void WriteProblems(string path, IList<Audit> rows)
        {
            var lines = new List<string> { "Sha256,Label,GroupId,Split,PhysicalFormat,ProblemFlags,MdocClassification,OcrClassification,QrMdocClassification,QrOcrClassification,MdocNul,MdocControl,MdocSingleCharTokenRatio,OcrFailureReason" };
            foreach (var a in rows.Where(x=>x.Problems.Count>0)) lines.Add(string.Join(",", new[] { a.Sha256,a.Label,a.GroupId,a.Split,a.PhysicalFormat,string.Join(";",a.Problems),S(a.MdocSelection,"C"),S(a.OcrSelection,"C"),S(a.QrMdocSelection,"C"),S(a.QrOcrSelection,"C"),N(a.Mdoc.Nul),N(a.Mdoc.Control),D(a.Mdoc.SingleCharTokenRatio),a.OcrFailureReason }.Select(Csv)));
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static void WriteMetrics(string path, IList<Audit> rows)
        {
            var pdf = rows.Where(x=>x.PhysicalFormat=="PDF").ToList();
            var sb = new StringBuilder();
            sb.AppendLine("# H1D5A — Métricas de evidencia textual").AppendLine().AppendLine("> Los resultados sobre TEST son regresión/diagnóstico experimental, no certificación final independiente. No se seleccionaron thresholds productivos.").AppendLine();
            sb.AppendLine("## A. Calidad técnica de extracción").AppendLine();
            sb.AppendLine("- Archivos auditados: 80; PDF: 36; PNG: 30; JPEG: 14; GroupId: 54.");
            sb.AppendLine("- PDF que pasan `Mdoc.HasUsefulText`: " + pdf.Count(x=>x.MdocHasUsefulText) + ".");
            sb.AppendLine("- Mdoc útil con degradación estructural evidente: " + pdf.Count(x=>x.MdocHasUsefulText&&x.Problems.Contains("MDOC_DEGRADADO")) + ".");
            sb.AppendLine("- PDF con NUL: " + pdf.Count(x=>x.Mdoc.Nul>0) + "; con controles: " + pdf.Count(x=>x.Mdoc.Control>0) + "; fragmentación alta: " + pdf.Count(x=>x.Mdoc.SingleCharTokenRatio>=0.35&&x.Mdoc.Tokens>=10) + ".");
            sb.AppendLine("- OCR ejecutado/exitoso/no disponible: " + rows.Count(x=>x.OcrExecuted) + "/" + rows.Count(x=>x.OcrSuccess) + "/" + rows.Count(x=>!x.OcrSuccess) + ".");
            sb.AppendLine("- PDF con clasificación Mdoc/OCR distinta: " + pdf.Count(Changed) + ".").AppendLine();
            sb.AppendLine("## B. Evaluación contra Label").AppendLine();
            AppendMatrix(sb, "Mdoc + InvoiceSelector (sólo PDF)", pdf, x=>x.MdocSelection);
            AppendMatrix(sb, "OCR + InvoiceSelector", rows, x=>x.OcrSelection);
            AppendMatrix(sb, "QR + Mdoc (sólo PDF)", pdf, x=>x.QrMdocSelection);
            AppendMatrix(sb, "QR + OCR (sólo PDF)", pdf, x=>x.QrOcrSelection);
            sb.AppendLine("### Indicadores críticos").AppendLine();
            sb.AppendLine("- `FACTURA → DESCARTAR` con Mdoc: " + pdf.Count(x=>x.Label=="FACTURA"&&S(x.MdocSelection,"C")=="DESCARTAR") + ".");
            sb.AppendLine("- `FACTURA → DESCARTAR` con OCR: " + rows.Count(x=>x.Label=="FACTURA"&&S(x.OcrSelection,"C")=="DESCARTAR") + ".");
            sb.AppendLine("- Falsos FACTURA Mdoc/OCR: " + pdf.Count(x=>x.Label!="FACTURA"&&S(x.MdocSelection,"C")=="FACTURA") + "/" + rows.Count(x=>x.Label!="FACTURA"&&S(x.OcrSelection,"C")=="FACTURA") + ".");
            sb.AppendLine("- REVISAR Mdoc/OCR: " + pdf.Count(x=>S(x.MdocSelection,"C")=="REVISAR") + "/" + rows.Count(x=>S(x.OcrSelection,"C")=="REVISAR") + ".");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void AppendMatrix(StringBuilder sb, string title, IEnumerable<Audit> source, Func<Audit,InvoiceSelection> selector)
        {
            sb.AppendLine("### " + title).AppendLine().AppendLine("| Label | FACTURA | REVISAR | DESCARTAR |").AppendLine("|---|---:|---:|---:|");
            foreach (var label in new[]{"FACTURA","OTRO_DOCUMENTO","NO_DOCUMENTO"})
            { var rows=source.Where(x=>x.Label==label); sb.AppendLine("| "+label+" | "+rows.Count(x=>S(selector(x),"C")=="FACTURA")+" | "+rows.Count(x=>S(selector(x),"C")=="REVISAR")+" | "+rows.Count(x=>S(selector(x),"C")=="DESCARTAR")+" |"); }
            sb.AppendLine();
        }

        private static void WriteSummary(string path, IList<Audit> rows)
        {
            var pdf=rows.Where(x=>x.PhysicalFormat=="PDF").ToList(); var useful=pdf.Where(x=>x.MdocHasUsefulText).ToList();
            var invoiceBetter=pdf.Count(x=>x.Label=="FACTURA"&&Rank(S(x.OcrSelection,"C"),true)>Rank(S(x.MdocSelection,"C"),true));
            var otherAvoid=pdf.Count(x=>x.Label=="OTRO_DOCUMENTO"&&S(x.MdocSelection,"C")=="FACTURA"&&S(x.OcrSelection,"C")!="FACTURA");
            var otherGenerate=pdf.Count(x=>x.Label=="OTRO_DOCUMENTO"&&S(x.MdocSelection,"C")!="FACTURA"&&S(x.OcrSelection,"C")=="FACTURA");
            var gateEvidence=useful.Any(x=>x.Problems.Contains("MDOC_DEGRADADO")&&Changed(x));
            var sb=new StringBuilder(); sb.AppendLine("# H1D5A — Resumen ejecutivo").AppendLine().AppendLine("> Diagnóstico experimental; no define ni implementa un quality gate.").AppendLine();
            sb.AppendLine("1. **PDF que pasan hoy `Mdoc.HasUsefulText`:** "+useful.Count+" de 36.");
            sb.AppendLine("2. **Con degradación técnica evidente:** "+useful.Count(x=>x.Problems.Contains("MDOC_DEGRADADO"))+" de los que pasan.");
            sb.AppendLine("3. **Degradaciones observadas:** NUL en "+pdf.Count(x=>x.Mdoc.Nul>0)+", controles en "+pdf.Count(x=>x.Mdoc.Control>0)+", fragmentación alta en "+pdf.Count(x=>x.Mdoc.SingleCharTokenRatio>=0.35&&x.Mdoc.Tokens>=10)+" y tokens anormalmente largos en "+pdf.Count(x=>x.Mdoc.LongTokens>0)+" PDF.");
            sb.AppendLine("4. **Cambios de clasificación usando OCR:** "+pdf.Count(Changed)+" PDF.");
            sb.AppendLine("5. **FACTURA donde OCR aporta evidencia mejor:** "+invoiceBetter+".");
            sb.AppendLine("6. **OTRO_DOCUMENTO donde OCR evita/genera falso FACTURA:** "+otherAvoid+"/"+otherGenerate+".");
            sb.AppendLine("7. **Aporte QR ARCA:** "+pdf.Count(x=>x.Qr.IsValid)+" QR válidos; cambia Mdoc en "+pdf.Count(x=>S(x.QrMdocSelection,"C")!=S(x.MdocSelection,"C"))+" y OCR en "+pdf.Count(x=>S(x.QrOcrSelection,"C")!=S(x.OcrSelection,"C"))+" PDF.");
            sb.AppendLine("8. **Evidencia para futuro quality gate Mdoc→OCR:** "+(gateEvidence?"sí, existen casos degradados donde cambia el resultado; H1D5B deberá diseñarlo sin calibrar thresholds sobre TEST.":"no concluyente con este criterio descriptivo; revisar los casos técnicos antes de H1D5B.")+"");
            sb.AppendLine("9. **Casos que continúan en REVISAR:** "+pdf.Count(x=>S(x.QrOcrSelection,"C")=="REVISAR")+" PDF tras QR+OCR y "+rows.Count(x=>x.PhysicalFormat!="PDF"&&S(x.OcrSelection,"C")=="REVISAR")+" imágenes tras OCR.");
            File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));
        }

        private static bool Changed(Audit a) { return a.MdocSelection!=null && a.OcrSelection!=null && S(a.MdocSelection,"C")!=S(a.OcrSelection,"C"); }
        private static int Rank(string c,bool invoice) { return c=="FACTURA"?2:c=="REVISAR"?1:0; }
        private static string S(InvoiceSelection s,string p) { if(s==null)return ""; if(p=="C")return s.Classification; if(p=="M")return s.DetectionMethod; if(p=="F")return s.Confidence.HasValue?s.Confidence.Value.ToString(CultureInfo.InvariantCulture):""; return s.Reason; }
        private static string Csv(string s) { return "\""+(s??"").Replace("\"","\"\"")+"\""; }
        private static string B(bool v) { return v?"true":"false"; }
        private static string N(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string D(double v) { return v.ToString("0.######",CultureInfo.InvariantCulture); }

        private sealed class Row { internal string Path,Label,GroupId,Sha256,PhysicalFormat,Split; }
        private sealed class Audit
        {
            internal string Sha256,Label,GroupId,Split,PhysicalFormat,MdocFailureReason,OcrFailureReason; internal bool MdocExecuted,MdocHasUsefulText,OcrExecuted,OcrSuccess,OcrHasUsefulText,OcrLimitExceeded,OcrStructuralFailure,OcrSystemFailure,OcrHeaderUsed; internal TextMetrics Mdoc=new TextMetrics(),Ocr=new TextMetrics(); internal double OcrMeanConfidence; internal int OcrImagesProcessed,RasterPageCount,RasterDurationMs,OcrDurationMs,OcrHeaderDurationMs; internal InvoiceSelection MdocSelection,OcrSelection,QrMdocSelection,QrOcrSelection; internal ArcaQrEvidence Qr; internal List<string> Problems=new List<string>();
            internal Audit(Row r){Sha256=r.Sha256;Label=r.Label;GroupId=r.GroupId;Split=r.Split;PhysicalFormat=r.PhysicalFormat;}
            internal void DetermineProblems(){ if(PhysicalFormat=="PDF"&&MdocHasUsefulText&&(Mdoc.Nul>0||Mdoc.Control>0||(Mdoc.Tokens>=10&&Mdoc.SingleCharTokenRatio>=0.35)||Mdoc.PrintableRatio<0.85||Mdoc.LongTokens>0)){Problems.Add("MDOC_DEGRADADO");} if(Changed(this))Problems.Add("MDOC_OCR_DISCREPAN"); if(Label=="FACTURA"&&Rank(S(OcrSelection,"C"),true)>Rank(S(MdocSelection,"C"),true))Problems.Add("OCR_MEJORA_FACTURA"); if(Label=="FACTURA"&&Rank(S(OcrSelection,"C"),true)<Rank(S(MdocSelection,"C"),true))Problems.Add("OCR_EMPEORA_FACTURA"); if(Label!="FACTURA"&&S(MdocSelection,"C")=="FACTURA"&&S(OcrSelection,"C")!="FACTURA")Problems.Add("OCR_EVITA_FALSO_FACTURA"); if(Label!="FACTURA"&&S(MdocSelection,"C")!="FACTURA"&&S(OcrSelection,"C")=="FACTURA")Problems.Add("OCR_GENERA_FALSO_FACTURA"); if(Qr!=null&&Qr.IsValid&&(S(QrMdocSelection,"M")=="QR_TEXTO_CONFLICTO"||S(QrOcrSelection,"M")=="QR_TEXTO_CONFLICTO"))Problems.Add("QR_TEXTO_CONFLICTO"); if(!OcrSuccess)Problems.Add(OcrLimitExceeded?"OCR_LIMITE":"OCR_NO_DISPONIBLE"); }
        }
        private sealed class TextMetrics
        {
            internal int Length,Alphanumeric,Letters,UsefulWords,Nul,Control,Replacement,Tokens,SingleCharTokens,LongTokens; internal double PrintableRatio,SpaceRatio,SingleCharTokenRatio;
            internal static TextMetrics Measure(string value){value=value??"";var m=new TextMetrics{Length=value.Length,Alphanumeric=value.Count(char.IsLetterOrDigit),Letters=value.Count(char.IsLetter),Nul=value.Count(c=>c=='\0'),Control=value.Count(c=>char.IsControl(c)&&c!='\r'&&c!='\n'&&c!='\t'),Replacement=value.Count(c=>c=='\ufffd')};m.UsefulWords=Regex.Matches(value,@"\p{L}[\p{L}\p{N}]{1,}").Count;var tokens=Regex.Matches(value,@"\S+").Cast<Match>().Select(x=>x.Value).ToList();m.Tokens=tokens.Count;m.SingleCharTokens=tokens.Count(x=>x.Count(char.IsLetterOrDigit)==1);m.LongTokens=tokens.Count(x=>x.Length>=80);m.PrintableRatio=value.Length==0?0d:(double)value.Count(c=>!char.IsControl(c)||c=='\r'||c=='\n'||c=='\t')/value.Length;m.SpaceRatio=value.Length==0?0d:(double)value.Count(char.IsWhiteSpace)/value.Length;m.SingleCharTokenRatio=m.Tokens==0?0d:(double)m.SingleCharTokens/m.Tokens;return m;}
        }
    }
    internal static class H1D5ADictionaryExtensions { internal static int Get(this IDictionary<string,int> values,string key){int value;return values.TryGetValue(key,out value)?value:0;} }
}
