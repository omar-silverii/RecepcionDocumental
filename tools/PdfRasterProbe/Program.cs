using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using PDFtoImage;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class Program
    {
        private const int Dpi = 300;
        private const int MaxPages = 5;
        private const long MaxPixelsPerImage = 16000000;
        private const long MaxTotalPixels = 40000000;
        private const long MaxSourceBytes = 25L * 1024 * 1024;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--mime", StringComparison.OrdinalIgnoreCase))
                return GmailMimeProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--product", StringComparison.OrdinalIgnoreCase))
                return ProductFlowProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--gmail-corpus", StringComparison.OrdinalIgnoreCase))
                return GmailCorpusProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--productive-corpus", StringComparison.OrdinalIgnoreCase))
                return ProductiveCorpusProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d4a-assets", StringComparison.OrdinalIgnoreCase))
                return H1D4AAssetProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d5a-evidence", StringComparison.OrdinalIgnoreCase))
                return H1D5AEvidenceProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d5b-fusion", StringComparison.OrdinalIgnoreCase))
                return H1D5BFusionProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d5c-product-validation", StringComparison.OrdinalIgnoreCase))
                return H1D5CProductValidationProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d5c1-ocr-source-benchmark", StringComparison.OrdinalIgnoreCase))
                return H1D5C1OcrSourceBenchmarkProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d5c2-product-validation", StringComparison.OrdinalIgnoreCase))
                return H1D5C2ProductValidationProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d6a-gmail-operational", StringComparison.OrdinalIgnoreCase))
                return H1D6AOperationalGmailProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d6a-gmail-operational-inner", StringComparison.OrdinalIgnoreCase))
                return H1D6AOperationalGmailProbe.RunInner(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d7a-review", StringComparison.OrdinalIgnoreCase))
                return H1D7AReviewValidationProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d7a-review-inner", StringComparison.OrdinalIgnoreCase))
                return H1D7AReviewValidationProbe.RunInner(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d7b-visual-shadow", StringComparison.OrdinalIgnoreCase))
                return H1D7BVisualShadowProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d7b2-visual-shadow", StringComparison.OrdinalIgnoreCase))
                return H1D7B2VisualShadowProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d8a-fiscal-evidence", StringComparison.OrdinalIgnoreCase))
                return H1D8AFiscalEvidenceProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d8b-product-regression", StringComparison.OrdinalIgnoreCase))
                return H1D8BProductRegressionProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d9a-onnx-runtime", StringComparison.OrdinalIgnoreCase))
                return H1D9AOnnxRuntimeProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d9b-export-visual-assets", StringComparison.OrdinalIgnoreCase))
                return H1D9BVisualAssetProbe.Run(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d9c-export-test-assets", StringComparison.OrdinalIgnoreCase))
                return H1D9BVisualAssetProbe.RunTest(args);
            if (args.Length > 0 && string.Equals(args[0], "--h1d9d-visual-parity", StringComparison.OrdinalIgnoreCase))
                return H1D9DVisualInferenceParityProbe.Run(args);
            if (args.Length != 2) { Console.Error.WriteLine("Uso: PdfRasterProbe <pdf1> <pdf2>"); return 2; }
            var failed = false;
            foreach (var path in args)
            {
                try { Probe(path); }
                catch (Exception ex) { failed = true; Console.WriteLine("ERROR | Archivo=" + Path.GetFileName(path) + " | " + ex.GetType().Name + ": " + ex.Message); }
            }
            return failed ? 1 : 0;
        }

        private static void Probe(string path)
        {
            var source = new FileInfo(path);
            if (!source.Exists) throw new FileNotFoundException("No se encontró el PDF.", path);
            if (source.Length > MaxSourceBytes) throw new InvalidOperationException("El PDF supera el límite de origen de 25 MB.");

            var pdf = File.ReadAllBytes(path);
            var pages = Conversion.GetPageCount(pdf);
            Console.WriteLine("PDF | Archivo=" + source.Name + " | Bytes=" + source.Length + " | Paginas=" + pages + " | DPI=" + Dpi);
            if (pages > MaxPages) throw new InvalidOperationException("El PDF supera el límite de 5 páginas OCR.");

            var temporary = Path.Combine(Path.GetTempPath(), "RecepcionDocumental-PdfRasterProbe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            var images = new List<OcrImageData>();
            long totalPixels = 0;
            var renderTotal = Stopwatch.StartNew();
            try
            {
                for (var page = 0; page < pages; page++)
                {
                    var output = Path.Combine(temporary, "page-" + (page + 1).ToString(CultureInfo.InvariantCulture) + ".png");
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    var managedBefore = GC.GetTotalMemory(false);
                    var privateBefore = Process.GetCurrentProcess().PrivateMemorySize64;
                    var watch = Stopwatch.StartNew();
                    Conversion.SavePng(output, pdf, page, options: new RenderOptions { Dpi = Dpi });
                    watch.Stop();
                    var managedAfter = GC.GetTotalMemory(false);
                    var privateAfter = Process.GetCurrentProcess().PrivateMemorySize64;
                    int width, height;
                    using (var image = Image.FromFile(output)) { width = image.Width; height = image.Height; }
                    var pixels = (long)width * height;
                    totalPixels += pixels;
                    if (pixels <= 0 || pixels > MaxPixelsPerImage) throw new InvalidOperationException("La página " + (page + 1) + " supera 16.000.000 píxeles.");
                    if (totalPixels > MaxTotalPixels) throw new InvalidOperationException("El PDF supera 40.000.000 píxeles acumulados.");
                    var bytes = File.ReadAllBytes(output);
                    images.Add(new OcrImageData { Bytes = bytes, Width = width, Height = height });
                    Console.WriteLine("PAGINA | Numero=" + (page + 1) + " | Ancho=" + width + " | Alto=" + height + " | Pixeles=" + pixels + " | PNGBytes=" + bytes.Length + " | RenderMs=" + watch.ElapsedMilliseconds + " | ManagedDeltaBytes=" + Math.Max(0, managedAfter - managedBefore) + " | PrivateDeltaBytes=" + Math.Max(0, privateAfter - privateBefore));
                }
                renderTotal.Stop();
                var ocr = DocumentOcrService.Recognize(images);
                var selection = InvoiceSelector.SelectOcrText(ocr.Text, ocr.HasUsefulText);
                var headerUsed = false;
                var headerMilliseconds = 0;
                if (string.Equals(selection.Classification, "REVISAR", StringComparison.Ordinal))
                {
                    var header = DocumentOcrService.RecognizeHeader(images);
                    headerMilliseconds = header.DurationMilliseconds;
                    if (header.Success)
                    {
                        headerUsed = true;
                        ocr = DocumentOcrService.Combine(ocr, header);
                        selection = InvoiceSelector.SelectOcrText(ocr.Text, ocr.HasUsefulText);
                    }
                }
                Console.WriteLine("RESULTADO | Archivo=" + source.Name + " | RenderTotalMs=" + renderTotal.ElapsedMilliseconds + " | PixelesTotales=" + totalPixels + " | OCRSuccess=" + ocr.Success + " | OCRImagenes=" + ocr.ImagesProcessed + " | OCRMs=" + ocr.DurationMilliseconds + " | OCRCaracteres=" + (ocr.Text ?? string.Empty).Length + " | Confianza=" + ocr.MeanConfidence.ToString("0.000", CultureInfo.InvariantCulture) + " | SegundoPaseEncabezado=" + headerUsed + " | EncabezadoMs=" + headerMilliseconds + " | Clasificacion=" + selection.Classification + " | Metodo=" + selection.DetectionMethod + " | Razon=" + selection.Reason);
            }
            finally
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                Console.WriteLine("TEMP | Eliminado=" + (!Directory.Exists(temporary)));
            }
        }
    }
}
