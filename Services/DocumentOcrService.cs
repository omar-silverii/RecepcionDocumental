using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Hosting;
using Tesseract;

namespace RecepcionDocumental.Services
{
    public sealed class OcrImageData
    {
        public byte[] Bytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class OcrResult
    {
        public bool Success { get; set; }
        public bool HasUsefulText { get; set; }
        public bool SystemFailure { get; set; }
        public string Text { get; set; }
        public string FailureReason { get; set; }
        public int ImagesProcessed { get; set; }
        public int DurationMilliseconds { get; set; }
        public float MeanConfidence { get; set; }
    }

    public static class DocumentOcrService
    {
        private const int MaxFrames = 5;
        private const long MaxPixelsPerImage = 16000000;
        private const long MaxTotalPixels = 40000000;
        private const long MaxSourceBytes = 25L * 1024 * 1024;
        private const int MaxTextCharacters = 200000;

        public static OcrResult RecognizeImageFile(string path)
        {
            if (new FileInfo(path).Length > MaxSourceBytes)
                return Failure("La imagen supera el tamaño permitido para OCR.", false);
            try
            {
                var images = new List<OcrImageData>();
                using (var source = Image.FromFile(path))
                {
                    var dimension = new FrameDimension(source.FrameDimensionsList[0]);
                    var frames = source.GetFrameCount(dimension);
                    if (frames > MaxFrames) return Failure("La imagen multipágina supera el límite de OCR.", false);
                    long totalPixels = 0;
                    for (var frame = 0; frame < frames; frame++)
                    {
                        source.SelectActiveFrame(dimension, frame);
                        var pixels = (long)source.Width * source.Height;
                        totalPixels += pixels;
                        if (pixels <= 0 || pixels > MaxPixelsPerImage || totalPixels > MaxTotalPixels)
                            return Failure("La imagen supera el límite de píxeles para OCR.", false);
                        using (var stream = new MemoryStream())
                        {
                            source.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            images.Add(new OcrImageData { Bytes = stream.ToArray(), Width = source.Width, Height = source.Height });
                        }
                    }
                }
                return Recognize(images);
            }
            catch (Exception ex) when (IsImageException(ex))
            { return Failure("No se pudo abrir la imagen para OCR.", false); }
        }

        public static OcrResult Recognize(IEnumerable<OcrImageData> candidates)
        {
            var stopwatch = Stopwatch.StartNew();
            var output = new StringBuilder();
            var processed = 0;
            var confidence = 0f;
            try
            {
                var dataPath = GetDataPath();
                if (!File.Exists(Path.Combine(dataPath, "spa.traineddata")))
                    return TimedFailure("Falta el modelo OCR español.", true, stopwatch);
                using (var engine = new TesseractEngine(dataPath, "spa", EngineMode.LstmOnly))
                {
                    foreach (var candidate in candidates ?? Enumerable.Empty<OcrImageData>())
                    {
                        if (candidate == null || candidate.Bytes == null || candidate.Bytes.Length == 0) continue;
                        using (var pix = Pix.LoadFromMemory(candidate.Bytes))
                        using (var page = engine.Process(pix, PageSegMode.Auto))
                        {
                            var text = page.GetText() ?? string.Empty;
                            var remaining = MaxTextCharacters - output.Length;
                            if (remaining > 0) output.Append(text.Length <= remaining ? text : text.Substring(0, remaining)).Append(' ');
                            confidence += page.GetMeanConfidence();
                            processed++;
                        }
                    }
                }
                stopwatch.Stop();
                var combined = output.ToString();
                return new OcrResult {
                    Success = processed > 0,
                    HasUsefulText = HasUsefulText(combined),
                    Text = combined,
                    ImagesProcessed = processed,
                    DurationMilliseconds = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                    MeanConfidence = processed == 0 ? 0f : confidence / processed,
                    FailureReason = processed == 0 ? "No se encontraron imágenes procesables para OCR." : null
                };
            }
            catch (Exception ex) when (IsOcrException(ex))
            { return TimedFailure("El motor OCR no pudo procesar el documento.", IsSystemFailure(ex), stopwatch); }
        }

        private static bool HasUsefulText(string text)
        {
            var value = text ?? string.Empty;
            var alphanumeric = value.Count(char.IsLetterOrDigit);
            var letters = value.Count(char.IsLetter);
            var words = Regex.Matches(value, @"\p{L}[\p{L}\p{N}]{1,}").Count;
            return alphanumeric >= 30 && letters >= 15 && words >= 5;
        }

        private static string GetDataPath()
        {
            var mapped = HostingEnvironment.MapPath("~/App_Data/Tessdata");
            if (!string.IsNullOrWhiteSpace(mapped)) return mapped;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "Tessdata");
        }

        private static OcrResult Failure(string reason, bool systemFailure)
        { return new OcrResult { Text = string.Empty, FailureReason = reason, SystemFailure = systemFailure }; }

        private static OcrResult TimedFailure(string reason, bool systemFailure, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            var result = Failure(reason, systemFailure);
            result.DurationMilliseconds = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
            return result;
        }

        private static bool IsImageException(Exception ex)
        { return ex is ArgumentException || ex is ExternalException || ex is IOException || ex is OutOfMemoryException; }

        private static bool IsOcrException(Exception ex)
        { return ex is TesseractException || ex is DllNotFoundException || ex is BadImageFormatException || ex is IOException || ex is InvalidOperationException; }

        private static bool IsSystemFailure(Exception ex)
        { return ex is DllNotFoundException || ex is BadImageFormatException || ex is TesseractException; }
    }
}
