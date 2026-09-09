using System;
using System.Linq;

namespace RecepcionDocumental.Services
{
    internal static class FamilyDocumentAiSafetyPolicy
    {
        private static readonly string[][] PayrollEvidence = {
            new[] { "SUELDO", "HABERES", "REMUNERATIVO", "REMUNERACION" },
            new[] { "EMPLEADOR", "TRABAJADOR" },
            new[] { "JUBILACION", "OBRA SOCIAL", "CARGAS SOCIALES", "ART" },
            new[] { "DESCUENTOS", "SUELDO NETO", "NETO A PAGAR", "SUELDO BASICO" },
            new[] { "LEGAJO", "ANTIGUEDAD", "CUIL" },
            new[] { "COMPOSICION SALARIAL", "LAPSO LIQUIDADO" }
        };

        private static readonly string[][] BankNoticeEvidence = {
            new[] { "COMISION", "COMISIONES", "CARGO", "CARGOS" },
            new[] { "BANCO", "BNA" },
            new[] { "CLIENTE", "CARTERA CONSUMO", "CARTERA COMERCIAL" },
            new[] { "CUENTA CORRIENTE", "CAJA DE AHORRO", "TARJETA DE CREDITO", "PAQUETE" },
            new[] { "ENTRARAN EN VIGOR", "ENTRARA EN VIGOR", "COMUNICAR", "COMUNICARTE", "COMUNICARLE" },
            new[] { "REGIMEN DE TRANSPARENCIA", "BCRA", "BANCO CENTRAL" }
        };

        public static bool CanDiscard(string family, string ocrText, InvoiceSelection ocrSelection, ArcaQrEvidence qr, out string reason)
        {
            reason = null;
            if (qr != null && qr.IsValid) { reason = "QR_ARCA_VALIDO"; return false; }
            if (ocrSelection != null && string.Equals(ocrSelection.Classification, "FACTURA", StringComparison.Ordinal)) { reason = "OCR_FACTURA"; return false; }
            if (ocrSelection != null && ocrSelection.Confidence.HasValue && ocrSelection.Confidence.Value >= 45)
            { reason = "OCR_EVIDENCIA_FISCAL_NO_CONCLUYENTE"; return false; }

            var normalized = InvoiceSelector.Normalize(ocrText);
            if (ContainsAny(normalized, "CAE", "CAEA", "PUNTO DE VENTA", "PTO VTA")) { reason = "ANCLA_FISCAL_FUERTE"; return false; }

            if (string.Equals(family, "RECIBO_HABERES", StringComparison.Ordinal))
            {
                var groups = CountGroups(normalized, PayrollEvidence);
                if (groups < 3) { reason = "EVIDENCIA_HABERES_INSUFICIENTE"; return false; }
                reason = "EVIDENCIA_HABERES_OK:" + groups;
                return true;
            }
            if (string.Equals(family, "NOTIFICACION_BANCARIA", StringComparison.Ordinal))
            {
                var hasPricing = ContainsAny(normalized, BankNoticeEvidence[0]);
                var hasBank = ContainsAny(normalized, BankNoticeEvidence[1]);
                var groups = CountGroups(normalized, BankNoticeEvidence);
                if (!hasPricing || !hasBank || groups < 4) { reason = "EVIDENCIA_NOTIFICACION_BANCARIA_INSUFICIENTE"; return false; }
                reason = "EVIDENCIA_NOTIFICACION_BANCARIA_OK:" + groups;
                return true;
            }
            reason = "FAMILIA_NO_AUTORIZADA";
            return false;
        }

        private static int CountGroups(string normalized, string[][] groups)
        { return groups.Count(group => ContainsAny(normalized, group)); }

        private static bool ContainsAny(string normalized, params string[] values)
        {
            var haystack = " " + (normalized ?? string.Empty) + " ";
            return values.Any(value => haystack.IndexOf(" " + value + " ", StringComparison.Ordinal) >= 0);
        }
    }
}
