using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            if (args.Length != 6)
            {
                Console.Error.WriteLine("Uso: --current-cases <temporal> <factura.jpg> <logo.jpg> <nota-credito.pdf> <nota-credito-conflictiva.pdf>");
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
            CheckSyntheticSelectors(failures);
            CheckDocument(args[2], "image/jpeg", "FACTURA", null, failures, "Factura_real");
            CheckDocument(args[3], "image/jpeg", "DESCARTAR", "GROUND_TRUTH_NO_DOCUMENTO", failures, "Logo_real");
            CheckDocument(args[4], "application/pdf", "REVISAR", "MDOC_OCR_CONFLICTO", failures, "Nota_credito_real");
            CheckDocument(args[5], "application/pdf", "REVISAR", null, failures, "Nota_credito_conflictiva_corpus");
            CheckUiStates(failures);

            Console.WriteLine("CURRENT_CASES_REGRESSION | " + (failures.Count == 0 ? "APROBADO" : "NO_APROBADO") + " | Fallas=" + failures.Count);
            foreach (var failure in failures) Console.WriteLine("FAIL | " + failure);
            return failures.Count == 0 ? 0 : 1;
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

        private static void CheckDocument(string path, string mime, string expectedClass, string expectedMethod, ICollection<string> failures, string name)
        {
            using (var workspace = new AttachmentWorkspace())
            {
                var analysis = DocumentAnalysisService.Analyze(File.ReadAllBytes(path), Path.GetFileName(path), mime, workspace);
                var candidate = analysis.Candidates.SingleOrDefault();
                var selection = candidate == null
                    ? analysis.DeterministicDiscards.Select(x => x.Selection).SingleOrDefault()
                    : candidate.Selection;
                Check(selection != null, name + " selección presente", failures);
                if (selection == null) return;
                Check(string.Equals(selection.Classification, expectedClass, StringComparison.Ordinal), name + " Expected=" + expectedClass + " Actual=" + selection.Classification, failures);
                if (expectedMethod != null)
                    Check(string.Equals(selection.DetectionMethod, expectedMethod, StringComparison.Ordinal), name + " Method=" + selection.DetectionMethod, failures);
                Console.WriteLine("PASS | " + name + " | " + selection.Classification + " | " + selection.DetectionMethod
                    + " | Confianza=" + (selection.Confidence.HasValue ? selection.Confidence.Value.ToString() : "NULL")
                    + " | Motivo=" + selection.Reason);
            }
        }

        private static void CheckUiStates(ICollection<string> failures)
        {
            var initial = Gmail_Bandeja.BuildSyncStatus(null);
            var running = Gmail_Bandeja.BuildSyncStatus(Audit(10, "EJECUTANDO", 0, 0));
            var completed = Gmail_Bandeja.BuildSyncStatus(Audit(10, "COMPLETADA", 3, 0));
            var withErrors = Gmail_Bandeja.BuildSyncStatus(Audit(10, "COMPLETADA_CON_ERRORES", 3, 1));
            var failed = Gmail_Bandeja.BuildSyncStatus(Audit(10, "FALLIDA", 0, 1));
            var omitted = Gmail_Bandeja.BuildSyncStatus(Audit(10, "OMITIDA_YA_EN_EJECUCION", 0, 0));
            Check(!initial.EnEjecucion, "UI_INICIAL", failures);
            Check(running.EnEjecucion && running.Texto.StartsWith("BUSCANDO / EN EJECUCIÓN", StringComparison.Ordinal), "UI_EJECUTANDO", failures);
            Check(!completed.EnEjecucion && completed.Texto.StartsWith("Búsqueda completada", StringComparison.Ordinal), "UI_COMPLETADA", failures);
            Check(!withErrors.EnEjecucion && withErrors.Texto.StartsWith("Búsqueda completada con errores", StringComparison.Ordinal), "UI_COMPLETADA_CON_ERRORES", failures);
            Check(!failed.EnEjecucion && failed.Texto.StartsWith("Búsqueda fallida", StringComparison.Ordinal), "UI_FALLIDA", failures);
            Check(!omitted.EnEjecucion && omitted.Texto.StartsWith("Búsqueda omitida", StringComparison.Ordinal), "UI_OMITIDA", failures);
        }

        private static GmailSyncAuditInfo Audit(long id, string state, int messages, int errors)
        {
            return new GmailSyncAuditInfo { Id = id, Estado = state, Origen = "WEB", Inicio = DateTime.UtcNow, Mensajes = messages, Errores = errors };
        }

        private static void Check(bool pass, string message, ICollection<string> failures)
        {
            if (pass) Console.WriteLine("PASS | " + message);
            else failures.Add(message);
        }
    }
}
