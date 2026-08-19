using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Mdoc.text.pdf;

namespace RecepcionDocumental.Services
{
    public sealed class PdfTextResult { public string Text { get; set; } public bool HasUsefulText { get; set; } public string FailureReason { get; set; } }

    public static class MdocPdfTextExtractor
    {
        private static readonly Regex Literal = new Regex(@"\((?<value>(?:\\.|[^\\)])*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Hex = new Regex(@"<(?<value>[0-9A-Fa-f\s]+)>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static PdfTextResult Extract(string path)
        {
            try
            {
                var output = new StringBuilder();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var reader = new PdfReader(stream);
                    try
                    {
                        for (var page = 1; page <= reader.NumberOfPages; page++)
                        {
                            var content = Encoding.GetEncoding(1252).GetString(reader.GetPageContent(page) ?? new byte[0]);
                            foreach (Match match in Literal.Matches(content)) output.Append(DecodeLiteral(match.Groups["value"].Value)).Append(' ');
                            foreach (Match match in Hex.Matches(content)) output.Append(DecodeHex(match.Groups["value"].Value)).Append(' ');
                        }
                    }
                    finally { reader.Close(); }
                }
                var text = output.ToString();
                var usefulCharacters = Regex.Replace(text, @"[^\p{L}\p{N}]", string.Empty).Length;
                return new PdfTextResult { Text = text, HasUsefulText = usefulCharacters >= 30 };
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex.GetType().Assembly.GetName().Name == "Mdoc")
            { return new PdfTextResult { Text = string.Empty, HasUsefulText = false, FailureReason = "Mdoc no pudo leer el PDF." }; }
        }

        private static string DecodeLiteral(string value)
        {
            return Regex.Replace(value, @"\\([nrtbf()\\]|[0-7]{1,3})", m =>
            {
                var token = m.Groups[1].Value;
                int octal; if (int.TryParse(token, System.Globalization.NumberStyles.None, null, out octal) && token.Length > 1) return ((char)Convert.ToInt32(token, 8)).ToString();
                switch (token) { case "n": return "\n"; case "r": return "\r"; case "t": return "\t"; case "b": return "\b"; case "f": return "\f"; default: return token; }
            });
        }

        private static string DecodeHex(string value)
        {
            var clean = Regex.Replace(value, @"\s", string.Empty); if (clean.Length % 2 != 0) clean += "0";
            var bytes = new byte[clean.Length / 2]; for (var i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
