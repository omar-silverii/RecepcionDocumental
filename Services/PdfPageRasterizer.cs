using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using PDFtoImage;

namespace RecepcionDocumental.Services
{
    public sealed class PdfPageRasterizationResult
    {
        public IList<OcrImageData> Images { get; set; } = new List<OcrImageData>();
        public bool LimitExceeded { get; set; }
        public bool StructuralFailure { get; set; }
        public string FailureReason { get; set; }
        public int PageCount { get; set; }
        public int DurationMilliseconds { get; set; }
    }

    public static class PdfPageRasterizer
    {
        public static PdfPageRasterizationResult RasterizeFirstPage(string path, AttachmentWorkspace workspace)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException("path");
            if (workspace == null) throw new ArgumentNullException("workspace");
            var stopwatch=Stopwatch.StartNew();var result=new PdfPageRasterizationResult();
            try
            {
                var source=new FileInfo(path);if(!source.Exists)return Failure(result,"No se encontró el PDF para rasterizar.",false,stopwatch);
                if(source.Length>OcrLimits.MaxSourceBytes)return Limited(result,"El PDF supera el tamaño de origen permitido para OCR.",stopwatch);
                var pdf=File.ReadAllBytes(path);result.PageCount=Conversion.GetPageCount(pdf);if(result.PageCount<=0)return Failure(result,"El PDF no contiene páginas rasterizables.",false,stopwatch);
                var output=workspace.CreatePath(".png");Conversion.SavePng(output,pdf,0,options:new RenderOptions(OcrLimits.PdfRasterDpi));
                int width,height;using(var image=Image.FromFile(output)){width=image.Width;height=image.Height;}var pixels=(long)width*height;
                if(width<=0||height<=0||pixels>OcrLimits.MaxTotalPixels)return Limited(result,"La primera página rasterizada supera el límite de seguridad visual.",stopwatch);
                result.Images.Add(new OcrImageData{Bytes=File.ReadAllBytes(output),Width=width,Height=height});Stop(result,stopwatch);return result;
            }
            catch(Exception ex){return Failure(result,IsStructuralFailure(ex)?"El renderer PDF no está disponible o es incompatible.":"No se pudo rasterizar la primera página del PDF.",IsStructuralFailure(ex),stopwatch);}
        }

        public static PdfPageRasterizationResult Rasterize(string path, AttachmentWorkspace workspace)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException("path");
            if (workspace == null) throw new ArgumentNullException("workspace");
            var stopwatch = Stopwatch.StartNew();
            var result = new PdfPageRasterizationResult();
            try
            {
                var source = new FileInfo(path);
                if (!source.Exists) return Failure(result, "No se encontró el PDF para rasterizar.", false, stopwatch);
                if (source.Length > OcrLimits.MaxSourceBytes)
                    return Limited(result, "El PDF supera el tamaño de origen permitido para OCR.", stopwatch);

                var pdf = File.ReadAllBytes(path);
                result.PageCount = Conversion.GetPageCount(pdf);
                if (result.PageCount <= 0) return Failure(result, "El PDF no contiene páginas rasterizables.", false, stopwatch);
                if (result.PageCount > OcrLimits.MaxImages)
                    return Limited(result, "El PDF supera el límite de páginas OCR.", stopwatch);

                long totalPixels = 0;
                var options = new RenderOptions(OcrLimits.PdfRasterDpi);
                for (var page = 0; page < result.PageCount; page++)
                {
                    var output = workspace.CreatePath(".png");
                    Conversion.SavePng(output, pdf, page, options: options);
                    int width;
                    int height;
                    using (var image = Image.FromFile(output)) { width = image.Width; height = image.Height; }
                    var pixels = (long)width * height;
                    totalPixels += pixels;
                    if (width <= 0 || height <= 0 || pixels > OcrLimits.MaxPixelsPerImage)
                        return Limited(result, "Una página rasterizada supera el límite de píxeles OCR.", stopwatch);
                    if (totalPixels > OcrLimits.MaxTotalPixels)
                        return Limited(result, "El PDF rasterizado supera el límite total de píxeles OCR.", stopwatch);
                    result.Images.Add(new OcrImageData { Bytes = File.ReadAllBytes(output), Width = width, Height = height });
                }
                Stop(result, stopwatch);
                return result;
            }
            catch (Exception ex)
            {
                return Failure(result,
                    IsStructuralFailure(ex) ? "El renderer PDF no está disponible o es incompatible." : "No se pudo rasterizar el PDF.",
                    IsStructuralFailure(ex), stopwatch);
            }
        }

        private static PdfPageRasterizationResult Limited(PdfPageRasterizationResult result, string reason, Stopwatch stopwatch)
        {
            result.Images.Clear();
            result.LimitExceeded = true;
            result.FailureReason = reason;
            Stop(result, stopwatch);
            return result;
        }

        private static PdfPageRasterizationResult Failure(PdfPageRasterizationResult result, string reason, bool structural, Stopwatch stopwatch)
        {
            result.Images.Clear();
            result.StructuralFailure = structural;
            result.FailureReason = reason;
            Stop(result, stopwatch);
            return result;
        }

        private static void Stop(PdfPageRasterizationResult result, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            result.DurationMilliseconds = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
        }

        private static bool IsStructuralFailure(Exception ex)
        {
            if (ex is DllNotFoundException || ex is BadImageFormatException || ex is EntryPointNotFoundException || ex is TypeLoadException)
                return true;
            return ex.InnerException != null && IsStructuralFailure(ex.InnerException);
        }
    }
}
