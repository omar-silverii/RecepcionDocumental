using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Mdoc.text.pdf;

namespace RecepcionDocumental.Services
{
    public sealed class PdfImageExtractionResult
    {
        public IList<OcrImageData> Images { get; set; } = new List<OcrImageData>();
        public bool LimitExceeded { get; set; }
        public string FailureReason { get; set; }
    }

    public static class MdocPdfImageExtractor
    {
        private const int MaxPages = 10;
        private const int MaxImages = 20;
        private const int MaxFormDepth = 4;
        private const long MaxPixelsPerImage = OcrLimits.MaxPixelsPerImage;
        private const long MaxTotalPixels = OcrLimits.MaxTotalPixels;

        public static PdfImageExtractionResult Extract(string path)
        {
            var result = new PdfImageExtractionResult();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var reader = new PdfReader(stream);
                    try
                    {
                        if (reader.NumberOfPages > MaxPages) return Limited("El PDF supera el límite de páginas OCR.");
                        var images = 0;
                        long totalPixels = 0;
                        for (var page = 1; page <= reader.NumberOfPages; page++)
                        {
                            InspectResources(reader.GetPageN(page).GetAsDict(PdfName.RESOURCES), 0, ref images, ref totalPixels, result);
                            if (result.LimitExceeded) return result;
                        }
                    }
                    finally { reader.Close(); }
                }
            }
            catch (Exception ex) when (IsMdocException(ex))
            { result.FailureReason = "Mdoc no pudo extraer imágenes del PDF."; }
            return result;
        }

        private static void InspectResources(PdfDictionary resources, int depth, ref int images, ref long totalPixels, PdfImageExtractionResult result)
        {
            if (resources == null || result.LimitExceeded || depth > MaxFormDepth) return;
            var xobjects = resources.GetAsDict(PdfName.XOBJECT);
            if (xobjects == null) return;
            foreach (PdfName key in xobjects.Keys)
            {
                var stream = PdfReader.GetPdfObject(xobjects.Get(key)) as PRStream;
                if (stream == null) continue;
                var subtype = stream.GetAsName(PdfName.SUBTYPE);
                if (PdfName.FORM.Equals(subtype))
                {
                    InspectResources(stream.GetAsDict(PdfName.RESOURCES), depth + 1, ref images, ref totalPixels, result);
                    if (result.LimitExceeded) return;
                    continue;
                }
                if (!PdfName.IMAGE.Equals(subtype)) continue;
                if (++images > MaxImages) { SetLimited(result, "El PDF supera el límite de imágenes OCR."); return; }
                var widthValue = stream.GetAsNumber(PdfName.WIDTH);
                var heightValue = stream.GetAsNumber(PdfName.HEIGHT);
                if (widthValue == null || heightValue == null) continue;
                var width = widthValue.IntValue;
                var height = heightValue.IntValue;
                var pixels = (long)width * height;
                totalPixels += pixels;
                if (width <= 0 || height <= 0 || pixels > MaxPixelsPerImage || totalPixels > MaxTotalPixels)
                { SetLimited(result, "El PDF supera el límite de píxeles OCR."); return; }
                var converted = ConvertImage(stream, width, height);
                if (converted != null) result.Images.Add(converted);
            }
        }

        private static OcrImageData ConvertImage(PRStream stream, int width, int height)
        {
            var bitsValue = stream.GetAsNumber(PdfName.BITSPERCOMPONENT);
            var colorSpace = stream.GetAsName(PdfName.COLORSPACE);
            if (bitsValue == null) return null;
            byte[] source;
            try { source = PdfReader.GetStreamBytes(stream); }
            catch (Exception ex) when (IsMdocException(ex)) { return null; }
            if (source == null) return null;
            var bits = bitsValue.IntValue;
            byte[] rgb = null;
            if (PdfName.DEVICERGB.Equals(colorSpace) && bits == 8 && source.Length == width * height * 3)
                rgb = source;
            else if (PdfName.DEVICEGRAY.Equals(colorSpace) && bits == 8 && source.Length == width * height)
                rgb = ExpandGray(source, width, height);
            else if (PdfName.DEVICEGRAY.Equals(colorSpace) && bits == 1)
                rgb = ExpandMonochrome(source, width, height, IsInverted(stream.GetAsArray(PdfName.DECODE)));
            if (rgb == null) return null;
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                var area = new Rectangle(0, 0, width, height);
                var data = bitmap.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var destination = new byte[data.Stride * height];
                    for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            var sourceOffset = (y * width + x) * 3;
                            var targetOffset = y * data.Stride + x * 3;
                            destination[targetOffset] = rgb[sourceOffset + 2];
                            destination[targetOffset + 1] = rgb[sourceOffset + 1];
                            destination[targetOffset + 2] = rgb[sourceOffset];
                        }
                    Marshal.Copy(destination, 0, data.Scan0, destination.Length);
                }
                finally { bitmap.UnlockBits(data); }
                using (var output = new MemoryStream())
                {
                    bitmap.Save(output, ImageFormat.Png);
                    return new OcrImageData { Bytes = output.ToArray(), Width = width, Height = height };
                }
            }
        }

        private static byte[] ExpandGray(byte[] source, int width, int height)
        {
            var rgb = new byte[width * height * 3];
            for (var i = 0; i < source.Length; i++) rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = source[i];
            return rgb;
        }

        private static byte[] ExpandMonochrome(byte[] source, int width, int height, bool invert)
        {
            var rowLength = (width + 7) / 8;
            if (source.Length != rowLength * height) return null;
            var rgb = new byte[width * height * 3];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var bit = (source[y * rowLength + x / 8] >> (7 - x % 8)) & 1;
                    var value = (byte)((bit == (invert ? 1 : 0)) ? 0 : 255);
                    var offset = (y * width + x) * 3;
                    rgb[offset] = rgb[offset + 1] = rgb[offset + 2] = value;
                }
            return rgb;
        }

        private static bool IsInverted(PdfArray decode)
        { var first = decode == null ? null : decode.GetAsNumber(0); return first != null && first.IntValue == 1; }

        private static PdfImageExtractionResult Limited(string reason)
        { return new PdfImageExtractionResult { LimitExceeded = true, FailureReason = reason }; }

        private static void SetLimited(PdfImageExtractionResult result, string reason)
        { result.LimitExceeded = true; result.FailureReason = reason; result.Images.Clear(); }

        private static bool IsMdocException(Exception ex)
        { return ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex.GetType().Assembly.GetName().Name == "Mdoc"; }
    }
}
