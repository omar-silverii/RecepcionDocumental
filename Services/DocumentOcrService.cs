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
        private const int MaxFrames = OcrLimits.MaxImages;
        private const long MaxPixelsPerImage = OcrLimits.MaxPixelsPerImage;
        private const long MaxTotalPixels = OcrLimits.MaxTotalPixels;
        private const long MaxSourceBytes = OcrLimits.MaxSourceBytes;
        private const int MaxTextCharacters = OcrLimits.MaxTextCharacters;
        private const double HeaderLeft = 0.43;
        private const double HeaderTop = 0.05;
        private const double HeaderWidth = 0.52;
        private const double HeaderHeight = 0.12;

        public static OcrResult RecognizeImageFile(string path)
        { return RecognizeImageFile(path, false); }

        public static OcrResult RecognizeImageHeader(string path)
        { return RecognizeImageFile(path, true); }

        public static OcrResult RecognizeHeader(IEnumerable<OcrImageData> candidates)
        {
            try
            {
                var headers = new List<OcrImageData>();
                foreach (var candidate in candidates ?? Enumerable.Empty<OcrImageData>())
                {
                    if (candidate == null || candidate.Bytes == null || candidate.Bytes.Length == 0) continue;
                    using (var stream = new MemoryStream(candidate.Bytes, false))
                    using (var source = Image.FromStream(stream, false, true))
                        headers.Add(CropHeader(source));
                }
                return Recognize(headers);
            }
            catch (Exception ex) when (IsImageException(ex))
            { return Failure("No se pudo obtener el encabezado para OCR.", false); }
        }

        public static OcrResult Combine(OcrResult complete, OcrResult header)
        {
            if (complete == null) throw new ArgumentNullException("complete");
            if (header == null) throw new ArgumentNullException("header");
            var first = complete.Text ?? string.Empty;
            var second = header.Text ?? string.Empty;
            var separator = first.Length > 0 && second.Length > 0 ? Environment.NewLine : string.Empty;
            var combined = first + separator + second;
            if (combined.Length > MaxTextCharacters) combined = combined.Substring(0, MaxTextCharacters);
            return new OcrResult {
                Success = complete.Success,
                HasUsefulText = HasUsefulText(combined),
                Text = combined,
                ImagesProcessed = complete.ImagesProcessed + header.ImagesProcessed,
                DurationMilliseconds = complete.DurationMilliseconds + header.DurationMilliseconds,
                MeanConfidence = complete.MeanConfidence
            };
        }

        private static OcrResult RecognizeImageFile(string path, bool headerOnly)
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
                        if (headerOnly)
                        {
                            images.Add(CropHeader(source));
                        }
                        else
                        {
                            using (var stream = new MemoryStream())
                            {
                                source.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                                images.Add(new OcrImageData { Bytes = stream.ToArray(), Width = source.Width, Height = source.Height });
                            }
                        }
                    }
                }
                return Recognize(images);
            }
            catch (Exception ex) when (IsImageException(ex))
            { return Failure("No se pudo abrir la imagen para OCR.", false); }
        }

        private static OcrImageData CropHeader(Image source)
        {
            var left = (int)Math.Floor(source.Width * HeaderLeft);
            var top = (int)Math.Floor(source.Height * HeaderTop);
            var width = Math.Min(source.Width - left, Math.Max(1, (int)Math.Ceiling(source.Width * HeaderWidth)));
            var height = Math.Min(source.Height - top, Math.Max(1, (int)Math.Ceiling(source.Height * HeaderHeight)));
            var area = new Rectangle(left, top, width, height);
            using (var output = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(output))
            using (var stream = new MemoryStream())
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), area, GraphicsUnit.Pixel);
                output.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return new OcrImageData { Bytes = stream.ToArray(), Width = width, Height = height };
            }
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
