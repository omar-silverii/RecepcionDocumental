namespace RecepcionDocumental.Services
{
    internal static class OcrLimits
    {
        internal const int MaxImages = 5;
        internal const long MaxPixelsPerImage = 16000000;
        internal const long MaxTotalPixels = 40000000;
        internal const long MaxSourceBytes = 25L * 1024 * 1024;
        internal const int MaxTextCharacters = 200000;
        internal const int PdfRasterDpi = 300;
    }
}
