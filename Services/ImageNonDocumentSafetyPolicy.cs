using System;
using System.Globalization;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    internal static class ImageNonDocumentSafetyPolicy
    {
        internal static InvoiceSelection Apply(InvoiceSelection current, string ocrText, VisualShadowResult visual)
        {
            if (current == null) throw new ArgumentNullException("current");
            if (!string.Equals(current.Classification, "REVISAR", StringComparison.Ordinal)) return current;
            if (string.Equals(current.DetectionMethod, "OCR_ERROR", StringComparison.Ordinal)) return current;

            if (visual == null || !string.Equals(visual.Status, "OK", StringComparison.Ordinal)
                || !string.Equals(visual.Zone, "NO_FACTURA_FUERTE", StringComparison.Ordinal)
                || !visual.PFactura.HasValue || visual.PFactura.Value > VisualInvoiceShadowService.TNoFactura)
            {
                Logs.LogProc("ImageNonDocumentGate | Decision=CONSERVAR | Motivo=VISION_NO_CONCLUYENTE");
                return current;
            }

            string evidence;
            if (InvoiceSelector.HasDocumentEvidenceForImageSafety(ocrText, out evidence))
            {
                Logs.LogProc("ImageNonDocumentGate | Decision=CONSERVAR | Motivo=EVIDENCIA_DOCUMENTAL | Evidencia=" + Logs.SanitizarMensaje(evidence)
                    + " | PFactura=" + visual.PFactura.Value.ToString("0.#########", CultureInfo.InvariantCulture));
                return current;
            }

            var confidence = (byte)Math.Max(0, Math.Min(100, (int)Math.Round(
                visual.PNoFactura.GetValueOrDefault(1d - visual.PFactura.Value) * 100d, MidpointRounding.AwayFromZero)));
            Logs.LogProc("ImageNonDocumentGate | Decision=DESCARTAR | Metodo=IA_VISUAL+OCR_GATE | Zona=" + visual.Zone
                + " | PFactura=" + visual.PFactura.Value.ToString("0.#########", CultureInfo.InvariantCulture)
                + " | EvidenciaOCR=NINGUNA");
            return new InvoiceSelection
            {
                Classification = "DESCARTAR",
                DetectionMethod = "IA_VISUAL+OCR_GATE",
                Confidence = confidence,
                Reason = "La imagen no contiene evidencia OCR documental o fiscal y el modelo visual la ubicó en NO_FACTURA_FUERTE."
            };
        }
    }
}
