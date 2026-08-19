using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RecepcionDocumental.Services
{
    public sealed class InvoiceSelection
    {
        public string Classification { get; set; }
        public string DetectionMethod { get; set; }
        public byte? Confidence { get; set; }
        public string Reason { get; set; }
    }

    public static class InvoiceSelector
    {
        private static readonly string[] ExplicitInvoices = { "FACTURA A", "FACTURA B", "FACTURA C", "FACTURA M", "FACTURA E", "FACTURA DE CREDITO ELECTRONICA" };
        private static readonly string[][] FiscalSignals = {
            new[] { "CUIT" }, new[] { "CAE", "CAEA" }, new[] { "PUNTO DE VENTA", "PTO VTA" },
            new[] { "COMP NRO", "COMPROBANTE", "NRO COMPROBANTE" }, new[] { "IMPORTE TOTAL", "TOTAL" },
            new[] { "IVA" }, new[] { "FECHA DE EMISION" }
        };
        private static readonly string[] NegativeSignals = { "REMITO", "NOTA DE CREDITO", "NOTA DE DEBITO", "ORDEN DE COMPRA", "PRESUPUESTO", "RECIBO", "NOTA DE PEDIDO" };

        public static InvoiceSelection SelectPdf(string text, bool hasUsefulText)
        {
            if (!hasUsefulText) return Review("PDF_SIN_TEXTO", "Requiere OCR futuro.", null);
            var normalized = Normalize(text);
            var explicitInvoice = ExplicitInvoices.Any(x => ContainsPhrase(normalized, x));
            var fiscalCount = FiscalSignals.Count(group => group.Any(x => ContainsPhrase(normalized, x)));
            var negative = NegativeSignals.FirstOrDefault(x => ContainsPhrase(normalized, x));
            if (explicitInvoice && fiscalCount >= 3) return new InvoiceSelection { Classification = "FACTURA", DetectionMethod = "PDF_TEXTO", Confidence = (byte)Math.Min(95, 70 + fiscalCount * 4), Reason = "Factura explícita y " + fiscalCount + " señales fiscales." };
            if (explicitInvoice) return Review("PDF_TEXTO", "Factura explícita con señales fiscales insuficientes.", 55);
            if (fiscalCount >= 3) return Review("PDF_TEXTO", "Se detectaron " + fiscalCount + " señales fiscales sin tipo de factura explícito.", 45);
            if (!string.IsNullOrEmpty(negative)) return Discard("PDF_TEXTO", "Documento identificado como " + negative + ".");
            return Review("PDF_TEXTO_NO_CONCLUYENTE", "El texto obtenido no permite clasificar el documento con seguridad.", null);
        }

        public static InvoiceSelection SelectNonPdf(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            if (new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" }.Contains(extension))
                return Review("IMAGEN_SIN_OCR", "Imagen pendiente de OCR.", null);
            return Normalize(System.IO.Path.GetFileNameWithoutExtension(fileName)).Contains("FACTURA")
                ? Review("NOMBRE_ARCHIVO", "El nombre sugiere una factura; requiere revisión.", 30)
                : Discard("TIPO_NO_ADMITIDO", "Formato sin evidencia de factura.");
        }

        public static InvoiceSelection Review(string method, string reason, byte? confidence)
        { return new InvoiceSelection { Classification = "REVISAR", DetectionMethod = method, Confidence = confidence, Reason = reason }; }

        private static InvoiceSelection Discard(string method, string reason)
        { return new InvoiceSelection { Classification = "DESCARTAR", DetectionMethod = method, Confidence = null, Reason = reason }; }

        internal static string Normalize(string value)
        {
            var decomposed = (value ?? string.Empty).ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
            return string.Join(" ", builder.ToString().Normalize(NormalizationForm.FormC).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool ContainsPhrase(string normalizedText, string phrase)
        { return (" " + normalizedText + " ").IndexOf(" " + phrase + " ", StringComparison.Ordinal) >= 0; }
    }
}
