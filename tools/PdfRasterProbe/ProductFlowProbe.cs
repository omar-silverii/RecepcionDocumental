using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class ProductFlowProbe
    {
        internal static int Run(string[] args)
        {
            if (args.Length != 7)
            {
                Console.Error.WriteLine("Uso: --product <ini> <scan1> <scan2> <pdf-texto> <pdf-imagen-mdoc> <pdf-limite>");
                return 2;
            }
            var installed = ConfiguracionIni.Cargar(args[1]);
            var probeRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProductFlowData");
            var configuration = new ConfiguracionAplicacion(
                installed.NombreProyecto, Path.Combine(probeRoot, "Logs"), Path.Combine(probeRoot, "Trabajo"),
                Path.Combine(probeRoot, "Facturas"), Path.Combine(probeRoot, "Revisar"), installed.ZipMaxEntradas,
                installed.ZipMaxBytesPorArchivo, installed.ZipMaxBytesDescomprimidos, installed.ZipMaxProfundidad,
                installed.GmailRedirectUri);
            configuration.PrepararRutasOperativas();
            ConfiguracionSistema.Inicializar(configuration);
            Logs.Inicializar(configuration);

            var ok = true;
            ok &= Analyze(args[2], "FACTURA", "RasterFactura1");
            ok &= Analyze(args[3], "FACTURA", "RasterFactura2");
            ok &= Analyze(args[4], null, "TextoMdocSinRaster");
            ok &= VerifyMdocImagePath(args[5]);
            ok &= VerifyLimit(args[6]);
            ok &= VerifyControlledFailure();
            return ok ? 0 : 1;
        }

        private static bool Analyze(string path, string expected, string caseName)
        {
            string root;
            AttachmentAnalysis result;
            using (var workspace = new AttachmentWorkspace())
            {
                root = workspace.RootPath;
                result = DocumentAnalysisService.Analyze(File.ReadAllBytes(path), Path.GetFileName(path), "application/pdf", workspace);
            }
            var classification = result.Candidates.Count == 0 ? "DESCARTAR" : result.Candidates[0].Selection.Classification;
            var method = result.Candidates.Count == 0 ? string.Empty : result.Candidates[0].Selection.DetectionMethod;
            var clean = !Directory.Exists(root);
            var valid = (expected == null || string.Equals(classification, expected, StringComparison.Ordinal)) && clean;
            Console.WriteLine("PRODUCTO | Caso=" + caseName + " | Clasificacion=" + classification + " | Metodo=" + method + " | WorkspaceEliminado=" + clean + " | OK=" + valid);
            return valid;
        }

        private static bool VerifyMdocImagePath(string path)
        {
            var extracted = MdocPdfImageExtractor.Extract(path);
            string root;
            InvoiceSelection selection;
            using (var workspace = new AttachmentWorkspace())
            {
                root = workspace.RootPath;
                var method = typeof(DocumentAnalysisService).GetMethod("AnalyzePdfWithOcr", BindingFlags.NonPublic | BindingFlags.Static);
                selection = (InvoiceSelection)method.Invoke(null, new object[] { path, new PdfTextResult { HasUsefulText = false }, workspace });
            }
            var clean = !Directory.Exists(root);
            var valid = extracted.Images.Count > 0 && clean;
            Console.WriteLine("PRODUCTO | Caso=ImagenMdocSinRaster | ImagenesMdoc=" + extracted.Images.Count + " | Clasificacion=" + selection.Classification + " | Metodo=" + selection.DetectionMethod + " | WorkspaceEliminado=" + clean + " | OK=" + valid);
            return valid;
        }

        private static bool VerifyLimit(string path)
        {
            string root;
            InvoiceSelection selection;
            using (var workspace = new AttachmentWorkspace())
            {
                root = workspace.RootPath;
                var method = typeof(DocumentAnalysisService).GetMethod("AnalyzePdfWithOcr", BindingFlags.NonPublic | BindingFlags.Static);
                selection = (InvoiceSelection)method.Invoke(null, new object[] { path, new PdfTextResult { HasUsefulText = false }, workspace });
            }
            var clean = !Directory.Exists(root);
            var valid = string.Equals(selection.Classification, "REVISAR", StringComparison.Ordinal)
                && string.Equals(selection.DetectionMethod, "OCR_LIMITE", StringComparison.Ordinal)
                && clean;
            Console.WriteLine("PRODUCTO | Caso=LimitePaginas | Clasificacion=" + selection.Classification + " | Metodo=" + selection.DetectionMethod + " | WorkspaceEliminado=" + clean + " | OK=" + valid);
            return valid;
        }

        private static bool VerifyControlledFailure()
        {
            string root;
            AttachmentAnalysis result;
            using (var workspace = new AttachmentWorkspace())
            {
                root = workspace.RootPath;
                result = DocumentAnalysisService.Analyze(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }, "corrupto.pdf", "application/pdf", workspace);
            }
            var selection = result.Candidates.Count == 0 ? null : result.Candidates[0].Selection;
            var clean = !Directory.Exists(root);
            var valid = selection != null && string.Equals(selection.Classification, "REVISAR", StringComparison.Ordinal) && clean;
            Console.WriteLine("PRODUCTO | Caso=FalloControlado | Clasificacion=" + (selection == null ? "NINGUNA" : selection.Classification) + " | Metodo=" + (selection == null ? string.Empty : selection.DetectionMethod) + " | WorkspaceEliminado=" + clean + " | OK=" + valid);
            return valid;
        }
    }
}
