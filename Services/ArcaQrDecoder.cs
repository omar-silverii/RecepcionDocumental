using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RecepcionDocumental.Services
{
    public sealed class ArcaQrEvidence
    {
        public bool QrDetected { get; set; }
        public bool IsValid { get; set; }
        public int? TipoComprobante { get; set; }
    }

    public static class ArcaQrDecoder
    {
        private static readonly HashSet<int> InvoiceTypes = new HashSet<int> { 1, 6, 11, 19, 51, 201, 206, 211 };
        private static readonly HashSet<int> NonInvoiceTypes = new HashSet<int> {
            2, 3, 4, 7, 8, 9, 12, 13, 15, 20, 21, 52, 53, 54,
            202, 203, 207, 208, 212, 213
        };
        private static readonly HashSet<string> OfficialHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "arca.gob.ar", "www.arca.gob.ar", "afip.gob.ar", "www.afip.gob.ar" };
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool IsInvoiceType(int type) { return InvoiceTypes.Contains(type); }
        public static bool IsKnownNonInvoiceType(int type) { return NonInvoiceTypes.Contains(type); }

        public static ArcaQrEvidence Decode(string value)
        {
            var evidence = new ArcaQrEvidence { QrDetected = true };
            Uri uri;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 12000 || !Uri.TryCreate(value, UriKind.Absolute, out uri)) return evidence;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !OfficialHosts.Contains(uri.Host)) return evidence;
            if (!string.Equals(uri.AbsolutePath, "/fe/qr/", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.Fragment)) return evidence;
            string encoded;
            if (!TryGetPayload(uri.Query, out encoded) || encoded.Length > 10000) return evidence;
            byte[] jsonBytes;
            try { jsonBytes = Convert.FromBase64String(PadBase64(encoded)); }
            catch (FormatException) { return evidence; }
            if (jsonBytes.Length == 0 || jsonBytes.Length > 8192) return evidence;
            JObject payload;
            try
            {
                using (var text = new StringReader(StrictUtf8.GetString(jsonBytes)))
                using (var json = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                    payload = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException) { return evidence; }

            int version, pointOfSale, type, number;
            decimal amount, exchangeRate;
            if (!TryInteger(payload["ver"], 1, 1, out version)
                || !TryDate(payload["fecha"])
                || !Digits(payload["cuit"], 11, 11)
                || !TryInteger(payload["ptoVta"], 1, 99999, out pointOfSale)
                || !TryInteger(payload["tipoCmp"], 1, 999, out type)
                || !TryInteger(payload["nroCmp"], 1, 99999999, out number)
                || !TryDecimal(payload["importe"], 0m, out amount)
                || !Currency(payload["moneda"])
                || !TryDecimal(payload["ctz"], 0.0000000001m, out exchangeRate)
                || !AuthorizationType(payload["tipoCodAut"])
                || !Digits(payload["codAut"], 14, 14)
                || !ValidOptionalReceiver(payload)) return evidence;

            evidence.IsValid = true;
            evidence.TipoComprobante = type;
            return evidence;
        }

        public static InvoiceSelection Combine(ArcaQrEvidence qr, InvoiceSelection textSelection)
        {
            if (textSelection == null) throw new ArgumentNullException("textSelection");
            if (qr == null || !qr.IsValid || !qr.TipoComprobante.HasValue) return textSelection;
            var type = qr.TipoComprobante.Value;
            if (IsInvoiceType(type))
            {
                if (string.Equals(textSelection.Classification, "DESCARTAR", StringComparison.Ordinal))
                    return InvoiceSelector.Review("QR_TEXTO_CONFLICTO", "El QR ARCA indica factura, pero el texto identifica inequívocamente otro tipo documental.", 70);
                var combinedMethod = string.Equals(textSelection.Classification, "FACTURA", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(textSelection.DetectionMethod)
                    ? "QR_ARCA+" + textSelection.DetectionMethod
                    : "QR_ARCA";
                return new InvoiceSelection {
                    Classification = "FACTURA",
                    DetectionMethod = combinedMethod,
                    Confidence = 98,
                    Reason = "QR ARCA estructuralmente válido con tipo de comprobante factura. No constituye validación online de autenticidad."
                };
            }
            if (IsKnownNonInvoiceType(type))
            {
                if (string.Equals(textSelection.Classification, "FACTURA", StringComparison.Ordinal))
                    return InvoiceSelector.Review("QR_TEXTO_CONFLICTO", "El QR ARCA indica un comprobante no factura, pero el texto identifica factura.", 70);
                return new InvoiceSelection { Classification = "DESCARTAR", DetectionMethod = "QR_ARCA", Confidence = 98, Reason = "El tipo de comprobante informado por el QR ARCA no corresponde a una factura." };
            }
            return textSelection;
        }

        private static bool TryGetPayload(string query, out string payload)
        {
            payload = null;
            var parts = (query ?? string.Empty).TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var separator = part.IndexOf('=');
                if (separator <= 0) return false;
                var key = Uri.UnescapeDataString(part.Substring(0, separator));
                if (!string.Equals(key, "p", StringComparison.Ordinal)) return false;
                if (payload != null) return false;
                payload = Uri.UnescapeDataString(part.Substring(separator + 1));
            }
            return !string.IsNullOrWhiteSpace(payload);
        }

        private static string PadBase64(string value)
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var remainder = normalized.Length % 4;
            return remainder == 0 ? normalized : normalized + new string('=', 4 - remainder);
        }

        private static bool TryInteger(JToken token, int minimum, int maximum, out int value)
        {
            return int.TryParse(TokenText(token), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= minimum && value <= maximum;
        }

        private static bool TryDecimal(JToken token, decimal minimum, out decimal value)
        {
            return decimal.TryParse(TokenText(token), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && value >= minimum;
        }

        private static bool TryDate(JToken token)
        {
            DateTime value;
            return DateTime.TryParseExact(TokenText(token), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        private static bool Digits(JToken token, int minimumLength, int maximumLength)
        {
            var value = TokenText(token);
            return value.Length >= minimumLength && value.Length <= maximumLength && value.All(char.IsDigit);
        }

        private static bool Currency(JToken token)
        {
            var value = TokenText(token);
            return value.Length == 3 && value.All(c => c >= 'A' && c <= 'Z');
        }

        private static bool AuthorizationType(JToken token)
        {
            var value = TokenText(token);
            return string.Equals(value, "A", StringComparison.Ordinal) || string.Equals(value, "E", StringComparison.Ordinal);
        }

        private static bool ValidOptionalReceiver(JObject payload)
        {
            var type = payload["tipoDocRec"];
            var number = payload["nroDocRec"];
            if (type == null && number == null) return true;
            int parsed;
            return type != null && number != null && TryInteger(type, 0, 99, out parsed) && Digits(number, 1, 20);
        }

        private static string TokenText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array) return string.Empty;
            return token.ToString(Formatting.None).Trim('"');
        }
    }
}
