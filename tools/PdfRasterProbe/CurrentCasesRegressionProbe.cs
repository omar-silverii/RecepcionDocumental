using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using RecepcionDocumental;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class CurrentCasesRegressionProbe
    {
        internal static int Run(string[] args)
        {
            if (args.Length != 7)
            {
                Console.Error.WriteLine("Uso: --current-cases <temporal> <factura.jpg> <logo.jpg> <nota-credito.pdf> <nota-credito-conflictiva.pdf> <factura-dificil.jpg>");
                return 2;
            }

            var root = Path.GetFullPath(args[1]);
            var configuration = new ConfiguracionAplicacion(
                "RecepcionDocumental", Path.Combine(root, "Logs"), Path.Combine(root, "Trabajo"),
                Path.Combine(root, "Facturas"), Path.Combine(root, "Revisar"),
                200, 52428800, 262144000, 3, "https://localhost/current-cases");
            configuration.PrepararRutasOperativas();
            ConfiguracionSistema.Inicializar(configuration);
            Logs.Inicializar(configuration);

            var failures = new List<string>();
            Check(!configuration.VisionShadowEnabled, "CONFIG_PRODUCTIVA VisionShadowEnabled=false", failures);
            CheckProductiveModelAssets(failures);
            CheckSyntheticSelectors(failures);
            CheckImageSafetyPolicy(failures);
            CheckDocument(args[2], "image/jpeg", "FACTURA", null, failures, "Factura_real");
            CheckDocument(args[3], "image/jpeg", "DESCARTAR", "IA_VISUAL+OCR_GATE", failures, "Logo_real");
            CheckDocument(args[4], "application/pdf", "REVISAR", "MDOC_OCR_CONFLICTO", failures, "Nota_credito_real");
            CheckDocument(args[5], "application/pdf", "REVISAR", null, failures, "Nota_credito_conflictiva_corpus");
            CheckDocument(args[6], "image/jpeg", null, null, failures, "Factura_imagen_dificil", true);
            CheckThreeAttachmentIntegration(args[2], args[3], args[4], failures);
            CheckGeneralizationVariants(root, args[2], args[3], failures);
            CheckUiStates(failures);
            CheckUiMarkup(failures);

            Console.WriteLine("CURRENT_CASES_REGRESSION | " + (failures.Count == 0 ? "APROBADO" : "NO_APROBADO") + " | Fallas=" + failures.Count);
            foreach (var failure in failures) Console.WriteLine("FAIL | " + failure);
            return failures.Count == 0 ? 0 : 1;
        }

        private static void CheckProductiveModelAssets(ICollection<string> failures)
        {
            var modelRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "DocumentAi", "Models", "H1D9B-CANDIDATE-001");
            Check(File.Exists(Path.Combine(modelRoot, "candidate.onnx")), "Modelo productivo candidate.onnx disponible", failures);
            Check(File.Exists(Path.Combine(modelRoot, "runtime-manifest.json")), "Modelo productivo runtime-manifest.json disponible", failures);
        }

        private static void CheckThreeAttachmentIntegration(string invoice, string logo, string note, ICollection<string> failures)
        {
            var selections = new[] { AnalyzeSelection(invoice, "image/jpeg"), AnalyzeSelection(note, "application/pdf"), AnalyzeSelection(logo, "image/jpeg") };
            Check(selections.Count(x => x != null && x.Classification == "FACTURA") == 1, "Integración tres adjuntos: 1 FACTURA", failures);
            Check(selections.Count(x => x != null && x.Classification == "REVISAR") == 1, "Integración tres adjuntos: 1 REVISAR", failures);
            Check(selections.Count(x => x != null && x.Classification == "DESCARTAR") == 1, "Integración tres adjuntos: 1 DESCARTAR", failures);
            Check(selections.Single(x => x.Classification == "DESCARTAR").DetectionMethod == "IA_VISUAL+OCR_GATE", "Integración tres adjuntos: descarte por gate visual", failures);
        }

        private static InvoiceSelection AnalyzeSelection(string path, string mime)
        {
            using (var workspace = new AttachmentWorkspace())
            {
                var analysis = DocumentAnalysisService.Analyze(File.ReadAllBytes(path), Path.GetFileName(path), mime, workspace);
                var candidate = analysis.Candidates.SingleOrDefault(); var ai = analysis.AiDiscards.SingleOrDefault(); var deterministic = analysis.DeterministicDiscards.SingleOrDefault();
                return candidate != null ? candidate.Selection : ai != null ? ai.Selection : deterministic == null ? null : deterministic.Selection;
            }
        }

        private static void CheckSyntheticSelectors(ICollection<string> failures)
        {
            CheckSelector("Factura_lineas_separadas", "FACTURA\nPUNTO DE VENTA: 00004\nC COMP. NRO: 00000002", "FACTURA", failures);
            CheckSelector("Factura_variante_abreviada", "FACTURA C\nPTO. VTA. NRO: 4\nCOMPROBANTE NUMERO: 12345678", "FACTURA", failures);
            CheckSelector("NC_no_promueve", "NOTA DE CREDITO A\nPUNTO DE VENTA: 00004\nCOMP. NRO: 00000002", "DESCARTAR", failures);
            CheckSelector("Recibo_no_promueve", "RECIBO\nPUNTO DE VENTA: 00004\nCOMP. NRO: 00000002", "DESCARTAR", failures);
            CheckSelector("Pago_factura_no_promueve", "PAGO DE FACTURA\nPUNTO DE VENTA: 00004\nCOMP. NRO: 00000002", "REVISAR", failures);
            CheckSelector("PV_sin_comprobante_no_promueve", "FACTURA\nPUNTO DE VENTA: 00004", "REVISAR", failures);
            CheckSelector("Comprobante_sin_PV_no_promueve", "FACTURA\nCOMP. NRO: 00000002", "REVISAR", failures);
            CheckSelector("Factura_sin_letra_en_comp_conserva_revision", "FACTURA\nPUNTO DE VENTA: 00004\nCOMP. NRO: 00000002", "REVISAR", failures);
            CheckSelector("Header_baja_confianza_no_promueve", "FACTURA\nPUNTO DE VENTA: 00004\nCOMP. NRO: 00000002", "REVISAR", failures, 0.60f);
            var conflict = InvoiceSelector.SelectOcrText(
                "FACTURA A CUIT 30-54010330-1 CAE 75011258003742 PUNTO DE VENTA 00005 COMP NRO 00000073",
                true,
                "NOTA DA CRBDILO\nNRO 00005-00000073\nFACTURA DE CREDITO",
                0.90f);
            Check(conflict.Classification == "REVISAR" && conflict.DetectionMethod == "OCR_TITULO_CONFLICTO", "OCR_titulo_conflictivo_conserva_revision", failures);
        }

        private static void CheckSelector(string name, string header, string expected, ICollection<string> failures, float confidence = 0.90f)
        {
            var selection = InvoiceSelector.SelectOcrText(header, true, header, confidence);
            Check(string.Equals(selection.Classification, expected, StringComparison.Ordinal), name + " Expected=" + expected + " Actual=" + selection.Classification, failures);
        }

        private static void CheckImageSafetyPolicy(ICollection<string> failures)
        {
            foreach (var signal in new[] { "FACTURA", "CUIT", "CAE", "NOTA DE CREDITO", "COMPROBANTE DE PAGO", "RESPONSABLE INSCRIPTO", "INGRESOS BRUTOS", "TOTAL A PAGAR", "ORIGINAL" })
            {
                string evidence;
                Check(InvoiceSelector.HasDocumentEvidenceForImageSafety(signal, out evidence), "ImageSafety protege " + signal, failures);
            }
            var visual = new VisualShadowResult { Status = "OK", Zone = "NO_FACTURA_FUERTE", PFactura = 0.01, PNoFactura = 0.99 };
            var review = InvoiceSelector.Review("OCR_NO_CONCLUYENTE", "Prueba", null);
            Check(ImageNonDocumentSafetyPolicy.Apply(review, string.Empty, visual).DetectionMethod == "IA_VISUAL+OCR_GATE", "ImageSafety descarta sin evidencia", failures);
            Check(ImageNonDocumentSafetyPolicy.Apply(review, "RESPONSABLE INSCRIPTO", visual).Classification == "REVISAR", "ImageSafety conserva evidencia documental", failures);
            Check(ImageNonDocumentSafetyPolicy.Apply(review, string.Empty, new VisualShadowResult { Status = "ERROR" }).Classification == "REVISAR", "ImageSafety falla abierto", failures);
            Check(ImageNonDocumentSafetyPolicy.Apply(InvoiceSelector.Review("OCR_ERROR", "Prueba", null), string.Empty, visual).Classification == "REVISAR", "ImageSafety conserva OCR_ERROR", failures);
        }

        private static void CheckDocument(string path, string mime, string expectedClass, string expectedMethod, ICollection<string> failures, string name, bool mustNotDiscard = false)
        {
            using (var workspace = new AttachmentWorkspace())
            {
                var analysis = DocumentAnalysisService.Analyze(File.ReadAllBytes(path), Path.GetFileName(path), mime, workspace);
                var candidate = analysis.Candidates.SingleOrDefault();
                var aiDiscard = analysis.AiDiscards.SingleOrDefault();
                var deterministicDiscard = analysis.DeterministicDiscards.SingleOrDefault();
                var selection = candidate != null ? candidate.Selection : aiDiscard != null ? aiDiscard.Selection : deterministicDiscard == null ? null : deterministicDiscard.Selection;
                var visual = candidate != null ? candidate.VisualShadow : aiDiscard == null ? null : aiDiscard.VisualShadow;
                Check(selection != null, name + " selección presente", failures);
                if (selection == null) return;
                if (expectedClass != null) Check(string.Equals(selection.Classification, expectedClass, StringComparison.Ordinal), name + " Expected=" + expectedClass + " Actual=" + selection.Classification, failures);
                if (mustNotDiscard) Check(!string.Equals(selection.Classification, "DESCARTAR", StringComparison.Ordinal), name + " no debe descartarse", failures);
                if (expectedMethod != null)
                    Check(string.Equals(selection.DetectionMethod, expectedMethod, StringComparison.Ordinal), name + " Method=" + selection.DetectionMethod, failures);
                Console.WriteLine("PASS | " + name + " | " + selection.Classification + " | " + selection.DetectionMethod
                    + " | Confianza=" + (selection.Confidence.HasValue ? selection.Confidence.Value.ToString() : "NULL")
                    + " | PFactura=" + (visual != null && visual.PFactura.HasValue ? visual.PFactura.Value.ToString("0.#########") : "NULL")
                    + " | Zona=" + (visual == null ? "NULL" : visual.Zone)
                    + " | Motivo=" + selection.Reason);
            }
        }

        private static void CheckGeneralizationVariants(string root, string invoicePath, string logoPath, ICollection<string> failures)
        {
            var variants = Path.Combine(root, "Variants");
            Directory.CreateDirectory(variants);
            var logoVariants = CreateVariants(logoPath, variants, "logo", true);
            var invoiceVariants = CreateVariants(invoicePath, variants, "invoice", false);
            var logoHash = Hash(logoPath);
            foreach (var path in logoVariants)
            {
                Check(!string.Equals(logoHash, Hash(path), StringComparison.OrdinalIgnoreCase), "Logo variante SHA distinto " + Path.GetFileName(path), failures);
                CheckDocument(path, Mime(path), "DESCARTAR", "IA_VISUAL+OCR_GATE", failures, "Logo_variante_" + Path.GetFileNameWithoutExtension(path));
            }
            foreach (var path in invoiceVariants)
                CheckDocument(path, Mime(path), null, null, failures, "Factura_variante_" + Path.GetFileNameWithoutExtension(path), true);
        }

        private static IList<string> CreateVariants(string source, string directory, string prefix, bool includeExtra)
        {
            var files = new List<string>();
            using (var image = Image.FromFile(source))
            {
                var reencoded = Path.Combine(directory, prefix + "-reencoded.jpg");
                SaveJpeg(image, reencoded, 82L); files.Add(reencoded);

                var resized = Path.Combine(directory, prefix + "-resized.jpg");
                using (var bitmap = Resize(image, Math.Max(1, image.Width * 3 / 4), Math.Max(1, image.Height * 3 / 4))) SaveJpeg(bitmap, resized, 90L);
                files.Add(resized);

                var png = Path.Combine(directory, prefix + "-png.png");
                image.Save(png, ImageFormat.Png); files.Add(png);

                if (includeExtra)
                {
                    var margin = Path.Combine(directory, prefix + "-margin.jpg");
                    using (var bitmap = new Bitmap(image.Width + 40, image.Height + 40))
                    using (var graphics = Graphics.FromImage(bitmap))
                    { graphics.Clear(Color.White); graphics.DrawImage(image, 20, 20, image.Width, image.Height); SaveJpeg(bitmap, margin, 90L); }
                    files.Add(margin);

                    var metadata = Path.Combine(directory, prefix + "-metadata.jpg");
                    using (var bitmap = new Bitmap(image)) { bitmap.SetResolution(72f, 72f); SaveJpeg(bitmap, metadata, 91L); }
                    files.Add(metadata);
                }
            }
            return files;
        }

        private static Bitmap Resize(Image image, int width, int height)
        {
            var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            { graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.DrawImage(image, 0, 0, width, height); }
            return bitmap;
        }

        private static void SaveJpeg(Image image, string path, long quality)
        {
            var codec = ImageCodecInfo.GetImageEncoders().Single(x => x.FormatID == ImageFormat.Jpeg.Guid);
            using (var parameters = new EncoderParameters(1))
            { parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality); image.Save(path, codec, parameters); }
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string Mime(string path)
        { return string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg"; }

        private static void CheckUiStates(ICollection<string> failures)
        {
            var initial = Gmail_Bandeja.BuildSyncStatus(null);
            var running = Gmail_Bandeja.BuildSyncStatus(Audit(10, "EJECUTANDO", 0, 0));
            var completed = Gmail_Bandeja.BuildSyncStatus(Audit(10, "COMPLETADA", 3, 0));
            var withErrors = Gmail_Bandeja.BuildSyncStatus(Audit(10, "COMPLETADA_CON_ERRORES", 3, 1));
            var failed = Gmail_Bandeja.BuildSyncStatus(Audit(10, "FALLIDA", 0, 1));
            var omitted = Gmail_Bandeja.BuildSyncStatus(Audit(10, "OMITIDA_YA_EN_EJECUCION", 0, 0));
            Check(!initial.EnEjecucion && !initial.EsTerminal, "UI_INICIAL overlay oculto", failures);
            Check(running.EnEjecucion && running.BloqueaUi && !running.EsTerminal && running.Texto.StartsWith("BUSCANDO / EN EJECUCIÓN", StringComparison.Ordinal), "UI_EJECUTANDO_WEB overlay bloqueante", failures);
            var scheduler = Audit(11, "EJECUTANDO", 0, 0); scheduler.Origen = "SCHEDULER";
            var schedulerStatus = Gmail_Bandeja.BuildSyncStatus(scheduler);
            Check(schedulerStatus.EnEjecucion && !schedulerStatus.BloqueaUi && !schedulerStatus.EsTerminal, "UI_EJECUTANDO_SCHEDULER no bloquea UI", failures);
            Check(!completed.EnEjecucion && completed.EsTerminal && completed.Texto.StartsWith("Búsqueda completada", StringComparison.Ordinal), "UI_COMPLETADA recarga final", failures);
            Check(!withErrors.EnEjecucion && withErrors.EsTerminal && withErrors.Texto.StartsWith("Búsqueda completada con errores", StringComparison.Ordinal), "UI_COMPLETADA_CON_ERRORES recarga final", failures);
            Check(!failed.EnEjecucion && failed.EsTerminal && failed.Texto.StartsWith("Búsqueda fallida", StringComparison.Ordinal), "UI_FALLIDA libera tras confirmar", failures);
            Check(!omitted.EnEjecucion && omitted.EsTerminal && omitted.Texto.StartsWith("Búsqueda omitida", StringComparison.Ordinal), "UI_OMITIDA terminal", failures);
        }

        private static GmailSyncAuditInfo Audit(long id, string state, int messages, int errors)
        {
            return new GmailSyncAuditInfo { Id = id, Estado = state, Origen = "WEB", Inicio = DateTime.UtcNow, Mensajes = messages, Errores = errors };
        }

        private static void CheckUiMarkup(ICollection<string> failures)
        {
            var path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Gmail_Bandeja.aspx"));
            var markup = File.ReadAllText(path);
            Check(markup.Contains("position: fixed") && markup.Contains("z-index: 2147483647"), "UI overlay cubre viewport y navbar", failures);
            Check(markup.Contains("OnClientClick=\"beginGmailSync();\""), "UI click muestra overlay sin cancelar postback", failures);
            Check(markup.Contains("GmailSyncStatus.ashx") && markup.Contains("request.timeout = 10000"), "UI polling usa endpoint estable y timeout", failures);
            Check(markup.Contains("pollingFailures >= 3") && markup.Contains("window.location.replace"), "UI polling se recupera por GET sin reenviar POST", failures);
            Check(markup.Contains("if (!payload.EsTerminal)") && !markup.Contains("window.location.reload()"), "UI estado terminal no reenvía POST", failures);
            Check(markup.Contains("event.key === 'Tab'") && markup.Contains("overlay.focus()"), "UI bloquea navegación por teclado", failures);
        }

        private static int Count(string value, string fragment)
        {
            var count = 0; var start = 0;
            while ((start = value.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0) { count++; start += fragment.Length; }
            return count;
        }

        private static void Check(bool pass, string message, ICollection<string> failures)
        {
            if (pass) Console.WriteLine("PASS | " + message);
            else failures.Add(message);
        }
    }
}
