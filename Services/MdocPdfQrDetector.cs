using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mdoc.text.pdf;
using ZXing;
using ZXing.Common;

namespace RecepcionDocumental.Services
{
    public static class MdocPdfQrDetector
    {
        private const int MaxPages = 20;
        private const int MaxImages = 100;
        private const int MaxFormDepth = 4;
        private const long MaxPixels = 16000000;

        public static ArcaQrEvidence Detect(string path)
        {
            var evidence = new ArcaQrEvidence();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var pdf = new PdfReader(stream);
                    try
                    {
                        var decoder = CreateDecoder();
                        var images = 0;
                        foreach (var page in PagesToScan(pdf.NumberOfPages))
                        {
                            if (images >= MaxImages) break;
                            var resources = pdf.GetPageN(page).GetAsDict(PdfName.RESOURCES);
                            var found = InspectResources(resources, decoder, 0, ref images, evidence);
                            if (found != null && found.IsValid) return found;
                        }
                    }
                    finally { pdf.Close(); }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex.GetType().Assembly.GetName().Name == "Mdoc")
            { return evidence; }
            return evidence;
        }

        private static BarcodeReaderGeneric CreateDecoder()
        {
            var decoder = new BarcodeReaderGeneric { AutoRotate = false };
            decoder.Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE } };
            return decoder;
        }

        private static ArcaQrEvidence InspectResources(PdfDictionary resources, BarcodeReaderGeneric decoder, int depth, ref int images, ArcaQrEvidence evidence)
        {
            if (resources == null || depth > MaxFormDepth || images >= MaxImages) return null;
            var xobjects = resources.GetAsDict(PdfName.XOBJECT);
            if (xobjects == null) return null;
            foreach (PdfName key in xobjects.Keys)
            {
                var stream = PdfReader.GetPdfObject(xobjects.Get(key)) as PRStream;
                if (stream == null) continue;
                var subtype = stream.GetAsName(PdfName.SUBTYPE);
                if (PdfName.FORM.Equals(subtype))
                {
                    var nested = InspectResources(stream.GetAsDict(PdfName.RESOURCES), decoder, depth + 1, ref images, evidence);
                    if (nested != null && nested.IsValid) return nested;
                    continue;
                }
                if (!PdfName.IMAGE.Equals(subtype) || ++images > MaxImages) continue;
                var result = DecodeImage(stream, decoder);
                if (result == null) continue;
                evidence.QrDetected = true;
                var arca = ArcaQrDecoder.Decode(result.Text);
                if (arca.IsValid) return arca;
            }
            return null;
        }

        private static Result DecodeImage(PRStream stream, BarcodeReaderGeneric decoder)
        {
            var widthValue = stream.GetAsNumber(PdfName.WIDTH);
            var heightValue = stream.GetAsNumber(PdfName.HEIGHT);
            var bitsValue = stream.GetAsNumber(PdfName.BITSPERCOMPONENT);
            var colorSpace = stream.GetAsName(PdfName.COLORSPACE);
            if (widthValue == null || heightValue == null || bitsValue == null) return null;
            var width = widthValue.IntValue;
            var height = heightValue.IntValue;
            var bits = bitsValue.IntValue;
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) return null;
            byte[] data;
            try { data = PdfReader.GetStreamBytes(stream); }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex.GetType().Assembly.GetName().Name == "Mdoc") { return null; }
            if (data == null) return null;
            if (PdfName.DEVICERGB.Equals(colorSpace) && bits == 8 && data.Length == width * height * 3)
                return decoder.Decode(data, width, height, RGBLuminanceSource.BitmapFormat.RGB24);
            if (PdfName.DEVICEGRAY.Equals(colorSpace) && bits == 8 && data.Length == width * height)
                return decoder.Decode(data, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
            if (PdfName.DEVICEGRAY.Equals(colorSpace) && bits == 1)
            {
                var rowLength = (width + 7) / 8;
                if (data.Length != rowLength * height) return null;
                var gray = new byte[width * height];
                var invert = IsInverted(stream.GetAsArray(PdfName.DECODE));
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var bit = (data[y * rowLength + x / 8] >> (7 - x % 8)) & 1;
                        gray[y * width + x] = (byte)((bit == (invert ? 1 : 0)) ? 0 : 255);
                    }
                return decoder.Decode(gray, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
            }
            return null;
        }

        private static bool IsInverted(PdfArray decode)
        {
            var first = decode == null ? null : decode.GetAsNumber(0);
            return first != null && first.IntValue == 1;
        }

        private static IEnumerable<int> PagesToScan(int numberOfPages)
        {
            // En comprobantes extensos se inspeccionan las primeras 19 páginas y la última,
            // donde normalmente se ubican los datos de autorización, sin procesamiento ilimitado.
            var sequential = Math.Min(numberOfPages, MaxPages);
            if (numberOfPages <= MaxPages)
            {
                for (var page = 1; page <= sequential; page++) yield return page;
                yield break;
            }
            for (var page = 1; page < MaxPages; page++) yield return page;
            yield return numberOfPages;
        }
    }
}
