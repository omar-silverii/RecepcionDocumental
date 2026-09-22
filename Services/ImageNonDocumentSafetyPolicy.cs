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
            if (string.Equals(current.DetectionMethod, "OCR_ERROR", StringComparison.Ordinal))
            {
                LogDecision(visual, "CONSERVAR", "OCR_ERROR");
                return current;
            }

            if (visual == null || !string.Equals(visual.Status, "OK", StringComparison.Ordinal)
                || !string.Equals(visual.Zone, "NO_FACTURA_FUERTE", StringComparison.Ordinal)
                || !visual.PFactura.HasValue || visual.PFactura.Value > VisualInvoiceShadowService.TNoFactura)
            {
                LogDecision(visual, "CONSERVAR", "VISION_NO_CONCLUYENTE");
                return current;
            }

            string evidence;
            if (InvoiceSelector.HasDocumentEvidenceForImageSafety(ocrText, out evidence))
            {
                LogDecision(visual, "CONSERVAR", "EVIDENCIA_DOCUMENTAL:" + Logs.SanitizarMensaje(evidence));
                return current;
            }

            var confidence = (byte)Math.Max(0, Math.Min(100, (int)Math.Round(
                visual.PNoFactura.GetValueOrDefault(1d - visual.PFactura.Value) * 100d, MidpointRounding.AwayFromZero)));
            LogDecision(visual, "DESCARTAR", "EVIDENCIA_OCR_NINGUNA");
            return new InvoiceSelection
            {
                Classification = "DESCARTAR",
                DetectionMethod = "IA_VISUAL+OCR_GATE",
                Confidence = confidence,
                Reason = "La imagen no contiene evidencia OCR documental o fiscal y el modelo visual la ubicó en NO_FACTURA_FUERTE."
            };
        }

        private static void LogDecision(VisualShadowResult visual, string decision, string reason)
        {
            Logs.LogProc("ImageNonDocumentGate | VisualAttempted=" + (visual != null && visual.Attempted ? "true" : "false")
                + " | Status=" + (visual == null ? "NO_EVALUADO" : visual.Status)
                + " | Zone=" + (visual == null || string.IsNullOrWhiteSpace(visual.Zone) ? "NINGUNA" : visual.Zone)
                + " | PFactura=" + (visual != null && visual.PFactura.HasValue ? visual.PFactura.Value.ToString("0.#########", CultureInfo.InvariantCulture) : "NULL")
                + " | Decision=" + decision
                + " | Motivo=" + reason);
        }
    }
}
