using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        private static readonly string[] NegativeSignals = { "REMITO", "NOTA DE CREDITO", "NOTA DE DEBITO", "ORDEN DE COMPRA", "PRESUPUESTO", "RECIBO", "NOTA DE PEDIDO", "CREDENCIAL DE PAGO", "EXTRACTO DE CUENTAS", "RESUMEN DE OPERACIONES" };
        private static readonly string[] DeterministicNonInvoiceSignals = { "COMPROBANTE DE PAGO", "FONDO DE CESE LABORAL" };
        private static readonly string[] InvoiceLetters = { "A", "B", "C", "M", "E" };

        public static InvoiceSelection SelectPdf(string text, bool hasUsefulText)
        {
            if (!hasUsefulText) return Review("PDF_SIN_TEXTO", "Requiere OCR futuro.", null);
            var normalized = Normalize(text);
            var compact = Compact(normalized);
            var explicitInvoice = ExplicitInvoices.Any(x => ContainsSignal(normalized, compact, x))
                || (ContainsPhrase(normalized, "FACTURA") && InvoiceLetters.Any(x => ContainsPhrase(normalized, x)));
            var fiscalCount = FiscalSignals.Count(group => group.Any(x => ContainsFiscalSignal(text, normalized, compact, x)));

            // H1D10D7C: la precedencia documental se decide por título/estructura, no
            // por una palabra negativa encontrada en cualquier parte del documento.
            // Una NC/ND/RECIBO real conserva prioridad; una FACTURA fuerte puede
            // convivir con referencias administrativas dentro del cuerpo.
            var specificNonInvoiceTitle = FindStrongSpecificNonInvoiceTitle(text);
            if (!string.IsNullOrEmpty(specificNonInvoiceTitle))
                return Discard("PDF_TEXTO", "Documento identificado por título como " + specificNonInvoiceTitle + ".");

            string strongFacturaEvidence;
            var strongFacturaTitle = HasStrongFacturaNativeText(text, out strongFacturaEvidence);
            if (strongFacturaTitle && fiscalCount >= 3)
                return new InvoiceSelection
                {
                    Classification = "FACTURA",
                    DetectionMethod = "PDF_TEXTO",
                    Confidence = (byte)Math.Min(95, 78 + fiscalCount * 3),
                    Reason = "Título FACTURA validado por posición/estructura y " + fiscalCount + " señales fiscales."
                };

            var negative = NegativeSignals.FirstOrDefault(x => ContainsSignal(normalized, compact, x));
            if (!string.IsNullOrEmpty(negative))
            {
                if (explicitInvoice && string.Equals(negative, "REMITO", StringComparison.Ordinal))
                {
                    // REMITO se conserva como referencia administrativa compatible con
                    // una factura explícita, igual que en la política histórica.
                }
                else if (explicitInvoice)
                {
                    // H1D10D7D: Mdoc entrega un flujo textual esencialmente aplanado.
                    // En ese formato una mención de NC/ND/OC/RECIBO dentro del cuerpo no
                    // puede considerarse un título documental con seguridad. El conflicto
                    // obliga a OCR de encabezado, que sí conserva evidencia posicional.
                    return Review("PDF_TEXTO_CONFLICTO", "El texto contiene evidencia explícita de FACTURA y también una referencia a " + negative + ". Se requiere OCR de encabezado para resolver el tipo documental.", null);
                }
                else
                {
                    return Discard("PDF_TEXTO", "Documento identificado como " + negative + ".");
                }
            }
            var deterministicNonInvoice = DeterministicNonInvoiceSignals.FirstOrDefault(x => ContainsSignal(normalized, compact, x));
            if (!explicitInvoice && !string.IsNullOrEmpty(deterministicNonInvoice))
                return Discard("PDF_TEXTO", "Documento identificado como " + deterministicNonInvoice + ".");
            if (explicitInvoice && fiscalCount >= 3) return new InvoiceSelection { Classification = "FACTURA", DetectionMethod = "PDF_TEXTO", Confidence = (byte)Math.Min(95, 70 + fiscalCount * 4), Reason = "Factura explícita y " + fiscalCount + " señales fiscales." };
            if (explicitInvoice) return Review("PDF_TEXTO", "Factura explícita con señales fiscales insuficientes.", 55);
            if (fiscalCount >= 3) return Review("PDF_TEXTO", "Se detectaron " + fiscalCount + " señales fiscales sin tipo de factura explícito.", 45);
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

        public static InvoiceSelection SelectOcrText(string text, bool hasUsefulText)
        {
            if (!hasUsefulText) return Review("OCR_NO_CONCLUYENTE", "El OCR no produjo texto utilizable para clasificar el documento.", null);
            var selection = SelectPdf(text, true);
            selection.DetectionMethod = "OCR";
            return selection;
        }

        // H1D10D1c/D3: promoción controlada de evidencia de encabezado ya validada
        // experimentalmente. Nunca degrada una FACTURA ya detectada por el OCR completo;
        // sólo puede resolver un REVISAR/DESCARTAR cuando el encabezado demuestra una
        // estructura documental FACTURA fuerte.
        public static InvoiceSelection SelectOcrText(string text, bool hasUsefulText, string headerText, float headerConfidence)
        {
            var baseline = SelectOcrText(text, hasUsefulText);
            if (string.Equals(baseline.Classification, "FACTURA", StringComparison.Ordinal)) return baseline;
            if (!hasUsefulText || string.IsNullOrWhiteSpace(headerText)) return baseline;

            var normalizedHeader = Normalize(headerText);
            var compactHeader = Compact(normalizedHeader);
            if (IsEmailLikeHeader(compactHeader)) return baseline;
            if (headerConfidence >= 0.80f && HasSpecificNonInvoiceHeaderTitle(compactHeader)) return baseline;

            string evidence;
            if (headerConfidence >= 0.80f && HasStrongFacturaHeader(headerText, out evidence))
            {
                var anchors = CountFiscalAnchors((headerText ?? string.Empty) + "\n" + (text ?? string.Empty));
                return new InvoiceSelection
                {
                    Classification = "FACTURA",
                    DetectionMethod = "OCR",
                    Confidence = (byte)Math.Min(95, 84 + Math.Min(anchors, 5) * 2),
                    Reason = "Título FACTURA validado por posición/estructura de encabezado" + (anchors > 0 ? " y " + anchors + " anclas fiscales." : ".")
                };
            }

            if (HasD3FacturaStructure(headerText, text, out evidence))
            {
                var anchors = CountFiscalAnchors((headerText ?? string.Empty) + "\n" + (text ?? string.Empty));
                return new InvoiceSelection
                {
                    Classification = "FACTURA",
                    DetectionMethod = "OCR",
                    Confidence = (byte)Math.Min(95, 82 + Math.Min(anchors, 6) * 2),
                    Reason = "Estructura fiscal FACTURA validada por encabezado y " + anchors + " anclas fiscales."
                };
            }

            return baseline;
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


        private static bool ContainsFiscalSignal(string originalText, string normalizedText, string compactText, string phrase)
        {
            if (ContainsSignal(normalizedText, compactText, phrase)) return true;
            if (phrase.IndexOf(' ') >= 0) return false;
            return ContainsStandaloneOcrToken(originalText, phrase);
        }

        private static bool ContainsStandaloneOcrToken(string value, string token)
        {
            var normalized = Normalize(value);
            var pieces = (token ?? string.Empty).Select(c => Regex.Escape(c.ToString())).ToArray();
            if (pieces.Length == 0) return false;
            var pattern = @"(?<![A-Z0-9])" + string.Join(@"[^A-Z0-9]*", pieces) + @"(?![A-Z0-9])";
            return Regex.IsMatch(normalized, pattern, RegexOptions.CultureInvariant);
        }

        private static bool IsEmailLikeHeader(string compactHeader)
        {
            if (string.IsNullOrEmpty(compactHeader)) return false;
            var markers = 0;
            if (compactHeader.IndexOf("ASUNTO", StringComparison.Ordinal) >= 0) markers++;
            if (compactHeader.IndexOf("DATOSADJUNTOS", StringComparison.Ordinal) >= 0) markers++;
            if (compactHeader.IndexOf("ENVIADOEL", StringComparison.Ordinal) >= 0) markers++;
            if (compactHeader.IndexOf("PARA", StringComparison.Ordinal) >= 0) markers++;
            return markers >= 2;
        }

        private static bool HasSpecificNonInvoiceHeaderTitle(string compactHeader)
        {
            if (string.IsNullOrEmpty(compactHeader)) return false;
            return compactHeader.IndexOf("NOTADECREDITO", StringComparison.Ordinal) >= 0
                || compactHeader.IndexOf("NOTACREDITO", StringComparison.Ordinal) >= 0
                || compactHeader.IndexOf("NOTADEDEBITO", StringComparison.Ordinal) >= 0
                || compactHeader.IndexOf("NOTADEBITO", StringComparison.Ordinal) >= 0
                || compactHeader.IndexOf("RECIBODEPAGO", StringComparison.Ordinal) >= 0
                || compactHeader.IndexOf("RECIBODECOBRO", StringComparison.Ordinal) >= 0
                || compactHeader.IndexOf("RECIBODESUELDO", StringComparison.Ordinal) >= 0
                || compactHeader.IndexOf("RECIBO", StringComparison.Ordinal) >= 0;
        }

        private static string FindStrongSpecificNonInvoiceTitle(string text)
        {
            var lines = (text ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var nonEmpty = 0;
            foreach (var rawLine in lines)
            {
                var line = (rawLine ?? string.Empty).Trim();
                if (line.Length == 0) continue;
                nonEmpty++;
                if (nonEmpty > 40) break;

                var compact = Compact(Normalize(line));
                if (compact.Length == 0) continue;

                var hit = FindSpecificTitleAlias(compact, "NOTADECREDITO", "NOTACREDITO");
                if (hit >= 0 && IsStrongSpecificTitlePrefix(compact.Substring(0, hit)))
                    return "NOTA DE CREDITO";

                hit = FindSpecificTitleAlias(compact, "NOTADEDEBITO", "NOTADEBITO");
                if (hit >= 0 && IsStrongSpecificTitlePrefix(compact.Substring(0, hit)))
                    return "NOTA DE DEBITO";

                hit = FindSpecificTitleAlias(compact, "RECIBODEPAGO", "RECIBODECOBRO", "RECIBODESUELDO", "RECIBO");
                if (hit >= 0 && IsStrongSpecificTitlePrefix(compact.Substring(0, hit)))
                    return "RECIBO";
            }
            return null;
        }

        private static int FindSpecificTitleAlias(string compact, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var index = compact.IndexOf(alias, StringComparison.Ordinal);
                if (index >= 0) return index;
            }
            return -1;
        }

        private static bool IsStrongSpecificTitlePrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || InvoiceLetters.Contains(prefix) || string.Equals(prefix, "ORIGINAL", StringComparison.Ordinal))
                return true;
            return Regex.IsMatch(prefix, @"^(?:COD|CODIGO)?\d{1,3}$", RegexOptions.CultureInvariant);
        }

        private static bool HasStrongFacturaNativeText(string text, out string evidence)
        {
            evidence = null;
            var allLines = (text ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var lines = allLines.Where(x => !string.IsNullOrWhiteSpace(x)).Take(30).ToArray();

            for (var i = 0; i < lines.Length; i++)
            {
                var line = (lines[i] ?? string.Empty).Trim();
                var compact = Compact(Normalize(line));
                if (compact.Length == 0) continue;

                var alias = FindFacturaAlias(compact);
                if (alias == null) continue;
                var position = compact.IndexOf(alias, StringComparison.Ordinal);
                if (ContainsSecondaryFacturaMarker(compact)) continue;

                var prefix = compact.Substring(0, position);
                var tail = compact.Substring(position + alias.Length);
                var prefixAllowed = prefix.Length == 0 || InvoiceLetters.Contains(prefix) || string.Equals(prefix, "ORIGINAL", StringComparison.Ordinal);
                var adjacentIdentifier = HasNearbyNativeIdentifier(lines, i + 1);

                if (!prefixAllowed)
                {
                    if (i < 2 && tail.Length == 0 && adjacentIdentifier) { evidence = line; return true; }
                    continue;
                }

                if ((!string.Equals(alias, "FACTURA", StringComparison.Ordinal) && (tail.Length == 0 || InvoiceLetters.Contains(tail)))
                    || HasSameLineIdentifier(tail)
                    || IsFacturaDescriptorTail(tail)
                    || (tail.Length == 0 && adjacentIdentifier))
                {
                    evidence = line;
                    return true;
                }
            }
            return false;
        }

        private static bool HasNearbyNativeIdentifier(string[] lines, int start)
        {
            var found = 0;
            string previous = null;
            for (var i = start; i < lines.Length && found < 4; i++)
            {
                var compact = Compact(Normalize((lines[i] ?? string.Empty).Trim()));
                if (compact.Length == 0) continue;
                found++;

                if (Regex.IsMatch(compact, @"^(?:[ABCEM])?(?:N|NRO|NUMERO|COD|CODIGO)?[0-9]{2,}", RegexOptions.CultureInvariant))
                    return true;
                if (Regex.IsMatch(compact, @"^PUNTODEVENTA[0-9]+COMP(?:ROBANTE)?NRO[0-9]+", RegexOptions.CultureInvariant))
                    return true;
                if (Regex.IsMatch(compact, @"^NUMERO[ABCEM]?[0-9]+", RegexOptions.CultureInvariant))
                    return true;

                if (!string.IsNullOrEmpty(previous)
                    && (previous == "N" || previous == "NRO" || previous == "NUMERO" || previous == "COMPNRO" || previous == "COMPROBANTE" || previous == "NROCOMPROBANTE")
                    && Regex.IsMatch(compact, @"^[0-9]{4,}$", RegexOptions.CultureInvariant))
                    return true;

                previous = compact;
            }
            return false;
        }

        private static bool HasStrongFacturaHeader(string headerText, out string evidence)
        {
            evidence = null;
            var lines = (headerText ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = (lines[i] ?? string.Empty).Trim();
                if (line.Length == 0) continue;
                var compact = Compact(Normalize(line));
                var alias = FindFacturaAlias(compact);
                if (alias == null) continue;
                var position = compact.IndexOf(alias, StringComparison.Ordinal);
                if (ContainsSecondaryFacturaMarker(compact)) continue;

                var prefix = compact.Substring(0, position);
                var tail = compact.Substring(position + alias.Length);
                var prefixAllowed = prefix.Length == 0 || InvoiceLetters.Contains(prefix) || string.Equals(prefix, "ORIGINAL", StringComparison.Ordinal);
                var adjacentIdentifier = HasNearbyIdentifier(lines, i + 1);

                if (!prefixAllowed)
                {
                    if (i < 2 && tail.Length == 0 && adjacentIdentifier) { evidence = line; return true; }
                    continue;
                }

                if ((!string.Equals(alias, "FACTURA", StringComparison.Ordinal) && (tail.Length == 0 || InvoiceLetters.Contains(tail)))
                    || HasSameLineIdentifier(tail)
                    || IsFacturaDescriptorTail(tail)
                    || (tail.Length == 0 && adjacentIdentifier))
                {
                    evidence = line;
                    return true;
                }
            }
            return false;
        }

        private static string FindFacturaAlias(string compact)
        {
            var aliases = new[] { "FACTURADECREDITOELECTRONICAMIPYMES", "FACTURADECREDITOELECTRONICA", "FACTURAELECTRONICA", "FACTURA" };
            return aliases.FirstOrDefault(x => compact.IndexOf(x, StringComparison.Ordinal) >= 0);
        }

        private static bool ContainsSecondaryFacturaMarker(string compact)
        {
            var markers = new[] { "ASUNTO", "PAGODEFACTURA", "REFERENCIAFACTURA", "FACTURAASOCIADA", "FECHAFACTURA", "SEGUNDAFACTURA", "GENERARFACTURA" };
            return markers.Any(x => compact.IndexOf(x, StringComparison.Ordinal) >= 0);
        }

        private static bool HasSameLineIdentifier(string tail)
        {
            return Regex.IsMatch(tail ?? string.Empty, @"^(?:[ABCEM])?(?:NRO|NUMERO|N[A-Z]?|CODIGO|COD)?[0-9]{2,}", RegexOptions.CultureInvariant);
        }

        private static bool HasNearbyIdentifier(string[] lines, int start)
        {
            var found = 0;
            for (var i = start; i < lines.Length && found < 2; i++)
            {
                var compact = Compact(Normalize((lines[i] ?? string.Empty).Trim()));
                if (compact.Length == 0) continue;
                found++;
                if (Regex.IsMatch(compact, @"^(?:[ABCEM])?(?:N|NRO|NUMERO|COD|CODIGO)?[0-9]{2,}", RegexOptions.CultureInvariant)) return true;
                if (Regex.IsMatch(compact, @"^PUNTODEVENTA[0-9]+COMP(?:ROBANTE)?NRO[0-9]+", RegexOptions.CultureInvariant)) return true;
                if (Regex.IsMatch(compact, @"^NUMERO[ABCEM]?[0-9]+", RegexOptions.CultureInvariant)) return true;
            }
            return false;
        }

        private static bool IsFacturaDescriptorTail(string tail)
        {
            var descriptors = new[] { "ELECTRONICA", "ELECTRONICAA", "ELECTRONICAB", "ELECTRONICAC", "ELECTRONICAM", "CREDITOELECTRONICA", "CREDITOELECTRONICAA", "CREDITOELECTRONICAB", "CREDITOELECTRONICAC", "DECREDITOELECTRONICA", "DECREDITOELECTRONICAA", "MIPYMES", "MIPYMESA", "A", "B", "C", "E", "M" };
            return descriptors.Contains(tail ?? string.Empty);
        }

        private static bool HasD3FacturaStructure(string headerText, string text, out string evidence)
        {
            evidence = null;
            var full = (headerText ?? string.Empty) + "\n" + (text ?? string.Empty);
            var anchors = CountFiscalAnchors(full);
            var lines = (headerText ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < lines.Length; i++)
            {
                var compact = Compact(Normalize(lines[i]));
                if (compact.StartsWith("FACTURADECREDITO", StringComparison.Ordinal) || Regex.IsMatch(compact, @"^.{0,2}ORIGINALFACTURADECREDITOELECTRONICA", RegexOptions.CultureInvariant))
                {
                    var nearby = string.Join(" ", lines.Skip(i).Take(7));
                    if (HasInvoiceNumber(nearby) && (anchors >= 2 || compact.IndexOf("ELECTRONICA", StringComparison.Ordinal) >= 0 || Compact(Normalize(headerText)).IndexOf("MIPYME", StringComparison.Ordinal) >= 0))
                    {
                        evidence = lines[i];
                        return true;
                    }
                }

                if (Regex.IsMatch(Normalize(lines[i]), @"\bFACTURA\b.{0,12}(?:[ABCEM]\s*)?\d{4,5}\s*[-–—]\s*\d{5,8}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    && anchors >= 4
                    && Compact(Normalize(full)).IndexOf("ORIGINAL", StringComparison.Ordinal) >= 0
                    && (Compact(Normalize(full)).IndexOf("FECHADEEMISION", StringComparison.Ordinal) >= 0 || Compact(Normalize(full)).IndexOf("INICIODEACTIVIDADES", StringComparison.Ordinal) >= 0))
                {
                    evidence = lines[i];
                    return true;
                }
            }
            return false;
        }

        private static bool HasInvoiceNumber(string value)
        {
            return Regex.IsMatch(Normalize(value), @"(?:\b[ABCEM]\s*)?\d{4,5}\s*[-–—]\s*\d{5,8}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static int CountFiscalAnchors(string value)
        {
            var normalized = Normalize(value);
            var compact = Compact(normalized);
            var count = 0;
            if (compact.IndexOf("CUIT", StringComparison.Ordinal) >= 0) count++;
            if (compact.IndexOf("IVA", StringComparison.Ordinal) >= 0 || compact.IndexOf("RESPONSABLEINSCRIPTO", StringComparison.Ordinal) >= 0) count++;
            if (compact.IndexOf("INGBRUTOS", StringComparison.Ordinal) >= 0 || compact.IndexOf("INGRESOSBRUTOS", StringComparison.Ordinal) >= 0) count++;
            if (compact.IndexOf("INICIODEACTIVIDADES", StringComparison.Ordinal) >= 0) count++;
            if (compact.IndexOf("FECHADEEMISION", StringComparison.Ordinal) >= 0 || compact.IndexOf("FECHAEMISION", StringComparison.Ordinal) >= 0) count++;
            if (compact.IndexOf("PUNTODEVENTA", StringComparison.Ordinal) >= 0 && (compact.IndexOf("COMPNRO", StringComparison.Ordinal) >= 0 || compact.IndexOf("COMPROBANTE", StringComparison.Ordinal) >= 0 || compact.IndexOf("NRO", StringComparison.Ordinal) >= 0)) count++;
            if (Regex.IsMatch(normalized, @"\bC\s*\.?\s*A\s*\.?\s*E\s*\.?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) count++;
            if (compact.IndexOf("TOTALAPAGAR", StringComparison.Ordinal) >= 0 || compact.IndexOf("IMPORTETOTAL", StringComparison.Ordinal) >= 0 || compact.IndexOf("SUBTOTAL", StringComparison.Ordinal) >= 0) count++;
            if (ContainsPhrase(normalized, "ORIGINAL")) count++;
            return count;
        }

        private static bool ContainsPhrase(string normalizedText, string phrase)
        { return (" " + normalizedText + " ").IndexOf(" " + phrase + " ", StringComparison.Ordinal) >= 0; }

        private static bool ContainsSignal(string normalizedText, string compactText, string phrase)
        {
            if (ContainsPhrase(normalizedText, phrase)) return true;
            return phrase.IndexOf(' ') >= 0 && compactText.IndexOf(Compact(phrase), StringComparison.Ordinal) >= 0;
        }

        private static string Compact(string value)
        {
            var builder = new StringBuilder((value ?? string.Empty).Length);
            foreach (var c in value ?? string.Empty)
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            return builder.ToString();
        }
    }
}
