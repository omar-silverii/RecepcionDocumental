using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Security;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    public static class GmailCorpusProbe
    {
        private const int MaxPerMessage = 5;
        private const int TargetMessages = 30;
        private const int MaxMessagesInspected = 2000;
        private const string GmailQuery = "older_than:365d newer_than:5y";
        private const int ImagesOnlyTargetMessages = 15;
        private const int ImagesOnlyMaxMessagesInspected = 1500;

        public static int Run(string[] args)
        {
            try
            {
                var root = FindRoot();
                var output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2h_NoDocumento_Historico_Revision");
                var imagesOnly = args.Any(x => x.Equals("--images-only", StringComparison.OrdinalIgnoreCase));
                if (Directory.Exists(output) || File.Exists(output + ".zip")) throw new IOException("La salida ya existe; no se sobrescribe: " + output);
                var installed = ConfiguracionIni.Cargar(Path.Combine(root, ConfiguracionIni.NombreArchivo));
                try { var alreadyInitialized = ConfiguracionSistema.Actual; }
                catch (InvalidOperationException) { ConfiguracionSistema.Inicializar(installed); }
                GoogleOAuthSettings settings;
                string error;
                if (!GoogleOAuthSettings.TryLoad(out settings, out error)) throw new InvalidOperationException(error);
                var account = GmailSyncRepository.GetActiveAccount();
                if (account == null || account.ProtectedRefreshToken == null) throw new InvalidOperationException("No existe una cuenta Gmail activa autorizada.");
                var token = RefreshTokenProtector.Unprotect(account.ProtectedRefreshToken);
                Directory.CreateDirectory(output);
                var local = Path.Combine(output, "_candidatos_locales");
                Directory.CreateDirectory(local);
                var corpusHashes = LoadCorpusHashes(Path.Combine(root, "tools", "DocumentAiProbe", "dataset.csv"));
                var priorReviewHashes = LoadCorpusHashes(Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2f_NoDocumento_Revision", "candidatos.csv"));
                var historicalReviewHashes = LoadCorpusHashes(Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2h_NoDocumento_Historico_Revision", "candidatos.csv"));
                var seen = new HashSet<string>(corpusHashes, StringComparer.OrdinalIgnoreCase);
                seen.UnionWith(priorReviewHashes);
                seen.UnionWith(historicalReviewHashes);
                var candidates = new List<Candidate>();
                var windows = imagesOnly ? ImagesOnlyWindows() : new List<WindowSpec> { new WindowSpec("GLOBAL", GmailQuery, "más de 365 días y hasta 5 años") };
                var results = new List<WindowResult>();

                using (var client = GmailOAuthService.CreateAuthorizedClient(settings, account.Email, token))
                {
                    foreach (var window in windows)
                    {
                        var stats = new WindowResult(window, candidates.Count);
                        var targetMessages = imagesOnly ? ImagesOnlyTargetMessages : TargetMessages;
                        var maxInspected = imagesOnly ? ImagesOnlyMaxMessagesInspected : MaxMessagesInspected;
                        string page = null;
                        do
                        {
                            var list = client.Service.Users.Messages.List("me");
                            list.Q = window.Query;
                            list.MaxResults = 100;
                            list.PageToken = page;
                            var response = list.Execute();
                            foreach (var stub in response.Messages ?? new List<Message>())
                            {
                                if (stats.Inspected++ >= maxInspected || stats.MessagesWithCandidates >= targetMessages) break;
                                var get = client.Service.Users.Messages.Get("me", stub.Id);
                                get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                                var message = get.Execute();
                                var subject = Header(message.Payload, "Subject");
                                var sender = Header(message.Payload, "From");
                                if (subject.IndexOf("FlightAware", StringComparison.OrdinalIgnoreCase) >= 0 || sender.IndexOf("FlightAware", StringComparison.OrdinalIgnoreCase) >= 0) { stats.FlightAwareExcluded++; continue; }
                                var parts = new List<MessagePart>();
                                CollectDownloadableParts(message.Payload, parts);
                                var acceptedForMessage = 0;
                                foreach (var part in parts)
                                {
                                    if (acceptedForMessage >= MaxPerMessage) break;
                                    var bytes = Download(client.Service, message.Id, part);
                                    if (bytes == null || bytes.Length == 0) continue;
                                    stats.RawCandidates++;
                                    var hash = Hash(bytes);
                                    if (corpusHashes.Contains(hash)) { stats.DuplicateCorpus++; continue; }
                                    if (priorReviewHashes.Contains(hash)) { stats.DuplicatePriorReview++; continue; }
                                    if (historicalReviewHashes.Contains(hash)) { stats.DuplicateHistoricalReview++; continue; }
                                    if (!seen.Add(hash)) { stats.DuplicateBank++; continue; }
                                    var format = DetectFormat(bytes);
                                    if (format.Name == "PDF" && imagesOnly) { stats.PdfOmitted++; continue; }
                                    if (format.Name == "OTHER") { stats.UnsupportedFormat++; continue; }
                                    int width, height;
                                    if (format.Name == "PDF") { width = 0; height = 0; }
                                    else if (!CanRender(bytes, out width, out height)) { stats.NonRenderable++; continue; }
                                    var id = (imagesOnly ? "I" : "H") + (candidates.Count + 1).ToString("D4", CultureInfo.InvariantCulture);
                                    var filename = string.IsNullOrWhiteSpace(part.Filename) ? "inline-" + Safe(part.PartId) + Extension(part.MimeType) : Path.GetFileName(part.Filename);
                                    var storedName = id + "_" + Safe(Path.GetFileNameWithoutExtension(filename)) + format.Extension;
                                    var stored = Path.Combine(local, storedName);
                                    File.WriteAllBytes(stored, bytes);
                                    candidates.Add(new Candidate(id, window.Name, message.Id, MessageDate(message), sender, SenderDomain(sender), filename, part.MimeType ?? "", format.Name, width, height, hash, bytes.Length, stored, "_candidatos_locales/" + storedName, subject,
                                        "PartId=" + (part.PartId ?? "") + "; Disposition=" + Header(part, "Content-Disposition") + "; ContentId=" + Header(part, "Content-ID")));
                                    acceptedForMessage++;
                                }
                                if (acceptedForMessage > 0) stats.MessagesWithCandidates++;
                            }
                            page = response.NextPageToken;
                        } while (!string.IsNullOrWhiteSpace(page) && stats.Inspected < maxInspected && stats.MessagesWithCandidates < targetMessages);
                        stats.FinalCandidates = candidates.Count - stats.StartCandidateCount;
                        stats.Limit = Limit(stats.Inspected, stats.MessagesWithCandidates, maxInspected, targetMessages);
                        results.Add(stats);
                    }
                }

                WriteCsv(Path.Combine(output, "candidatos.csv"), candidates);
                var sheets = WriteSheets(output, candidates);
                WriteSummary(Path.Combine(output, "resumen.md"), candidates, results, imagesOnly, sheets);
                var zip = output + ".zip";
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(Path.Combine(output, "candidatos.csv"), "candidatos.csv", CompressionLevel.Optimal);
                    archive.CreateEntryFromFile(Path.Combine(output, "resumen.md"), "resumen.md", CompressionLevel.Optimal);
                    foreach (var sheet in sheets) archive.CreateEntryFromFile(sheet, Path.GetFileName(sheet), CompressionLevel.Optimal);
                    foreach (var candidate in candidates) archive.CreateEntryFromFile(candidate.LocalPath, candidate.PackagePath, CompressionLevel.Optimal);
                }
                foreach (var r in results) Console.WriteLine("VENTANA | Nombre=" + r.Window.Name + " | Query=" + r.Window.Query + " | MensajesInspeccionados=" + r.Inspected + " | MensajesConCandidatos=" + r.MessagesWithCandidates + " | Brutos=" + r.RawCandidates + " | Finales=" + r.FinalCandidates + " | DuplicadosCorpus=" + r.DuplicateCorpus + " | DuplicadosH1D3A2f=" + r.DuplicatePriorReview + " | DuplicadosH1D3A2h=" + r.DuplicateHistoricalReview + " | DuplicadosBanco=" + r.DuplicateBank + " | PDFOmitidos=" + r.PdfOmitted + " | OTHER=" + r.UnsupportedFormat + " | NoRenderizables=" + r.NonRenderable + " | FlightAware=" + r.FlightAwareExcluded + " | Limite=" + r.Limit);
                Console.WriteLine("GMAIL_CORPUS | ImagesOnly=" + imagesOnly + " | Ventanas=" + results.Count + " | MensajesInspeccionados=" + results.Sum(x => x.Inspected) + " | Origenes=" + results.Sum(x => x.MessagesWithCandidates) + " | CandidatosBrutos=" + results.Sum(x => x.RawCandidates) + " | Candidatos=" + candidates.Count);
                Console.WriteLine("SALIDA | Carpeta=" + output + " | Zip=" + zip + " | ContactSheets=" + sheets.Count);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ERROR | " + ex.GetType().Name + ": " + ex.Message); return 1; }
        }

        private static void CollectDownloadableParts(MessagePart part, IList<MessagePart> result)
        {
            if (part == null) return;
            var hasAttachment = part.Body != null && !string.IsNullOrWhiteSpace(part.Body.AttachmentId);
            var hasInlineData = part.Body != null && !string.IsNullOrWhiteSpace(part.Body.Data);
            var unnamedTextBody = !hasAttachment && string.IsNullOrWhiteSpace(part.Filename)
                && (string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(part.MimeType, "text/html", StringComparison.OrdinalIgnoreCase));
            if ((hasAttachment || hasInlineData) && !unnamedTextBody) result.Add(part);
            foreach (var child in part.Parts ?? new List<MessagePart>()) CollectDownloadableParts(child, result);
        }

        private static byte[] Download(GmailService service, string messageId, MessagePart part)
        {
            var data = part.Body.Data;
            if (!string.IsNullOrWhiteSpace(part.Body.AttachmentId)) data = service.Users.Messages.Attachments.Get("me", messageId, part.Body.AttachmentId).Execute().Data;
            return Decode(data);
        }

        private static byte[] Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        private static bool CanRender(byte[] bytes, out int width, out int height)
        {
            width = 0; height = 0;
            try { using (var ms = new MemoryStream(bytes)) using (var image = Image.FromStream(ms, true, true)) { width = image.Width; height = image.Height; return width > 0 && height > 0; } }
            catch { return false; }
        }

        private static List<string> WriteSheets(string output, IList<Candidate> candidates)
        {
            var result = new List<string>();
            var images = candidates.Where(c => c.DetectedFormat == "PNG" || c.DetectedFormat == "JPEG").ToList();
            const int perSheet = 6;
            for (var offset = 0; offset < images.Count; offset += perSheet)
            {
                var path = Path.Combine(output, "contact-" + (result.Count + 1).ToString("D2", CultureInfo.InvariantCulture) + ".jpg");
                using (var canvas = new Bitmap(1800, 2100))
                using (var g = Graphics.FromImage(canvas))
                using (var title = new Font("Arial", 22, FontStyle.Bold))
                using (var detail = new Font("Arial", 15))
                {
                    g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    for (var i = 0; i < perSheet && offset + i < images.Count; i++)
                    {
                        var c = images[offset + i]; var col = i % 2; var row = i / 2; var x = 30 + col * 890; var y = 25 + row * 690;
                        g.DrawRectangle(Pens.DarkGray, x, y, 850, 650);
                        using (var image = Image.FromFile(c.LocalPath))
                        {
                            var box = new Rectangle(x + 15, y + 15, 820, 500); var scale = Math.Min((double)box.Width / image.Width, (double)box.Height / image.Height); var w = (int)(image.Width * scale); var h = (int)(image.Height * scale);
                            g.DrawImage(image, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                        }
                        g.DrawString(c.Id + " | " + c.MessageDateUtc.Substring(0, Math.Min(10, c.MessageDateUtc.Length)), title, Brushes.Black, x + 15, y + 525);
                        g.DrawString(Trim(c.Filename, 72), detail, Brushes.Black, x + 15, y + 565);
                        g.DrawString(Trim(c.Subject, 82), detail, Brushes.DimGray, x + 15, y + 600);
                    }
                    canvas.Save(path, ImageFormat.Jpeg);
                }
                result.Add(path);
            }
            return result;
        }

        private static void WriteCsv(string path, IEnumerable<Candidate> candidates)
        {
            var lines = new List<string> { "CandidateId,Window,MessageId,MessageDateUtc,Sender,SenderDomain,OriginalFilename,MimeType,DetectedFormat,Width,Height,SHA256,SizeBytes,PackagePath,Subject,OriginRelation" };
            lines.AddRange(candidates.Select(c => string.Join(",", new[] { c.Id, c.Window, c.MessageId, c.MessageDateUtc, c.Sender, c.SenderDomain, c.Filename, c.MimeType, c.DetectedFormat, c.Width.ToString(CultureInfo.InvariantCulture), c.Height.ToString(CultureInfo.InvariantCulture), c.Sha256, c.Size.ToString(CultureInfo.InvariantCulture), c.PackagePath, c.Subject, c.Relation }.Select(Csv))));
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static void WriteSummary(string path, IList<Candidate> c, IList<WindowResult> results, bool imagesOnly, IList<string> sheets)
        {
            var formats = string.Join(", ", c.GroupBy(x => x.DetectedFormat).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Count()));
            var distinctOrigins = c.Select(x => x.SenderDomain).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var dates = c.Where(x => !string.IsNullOrWhiteSpace(x.MessageDateUtc)).Select(x => DateTime.Parse(x.MessageDateUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).ToList();
            var effective = dates.Count == 0 ? "sin fechas disponibles" : dates.Min().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " a " + dates.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var text = new StringBuilder("# H1D3A2i — imágenes históricas para revisión\n\n");
            text.AppendLine("- Modo images-only: " + (imagesOnly ? "Sí" : "No"));
            text.AppendLine("- Período efectivo de candidatos: " + effective);
            foreach (var r in results)
            {
                var windowCandidates = c.Where(x => x.Window == r.Window.Name).ToList();
                var domains = windowCandidates.Select(x => x.SenderDomain).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                text.AppendLine("\n## Ventana " + r.Window.Name);
                text.AppendLine("- Query: `" + r.Window.Query + "`");
                text.AppendLine("- Período: " + r.Window.Period);
                text.AppendLine("- Mensajes inspeccionados: " + r.Inspected);
                text.AppendLine("- Mensajes con candidatos: " + r.MessagesWithCandidates);
                text.AppendLine("- Candidatos brutos: " + r.RawCandidates);
                text.AppendLine("- Candidatos finales: " + r.FinalCandidates);
                text.AppendLine("- Dominios distintos: " + domains);
                text.AppendLine("- Duplicados corpus: " + r.DuplicateCorpus);
                text.AppendLine("- Duplicados H1D3A2f: " + r.DuplicatePriorReview);
                text.AppendLine("- Duplicados H1D3A2h: " + r.DuplicateHistoricalReview);
                text.AppendLine("- Duplicados internos: " + r.DuplicateBank);
                text.AppendLine("- PDF omitidos por images-only: " + r.PdfOmitted);
                text.AppendLine("- OTHER: " + r.UnsupportedFormat);
                text.AppendLine("- Imágenes no renderizables: " + r.NonRenderable);
                text.AppendLine("- FlightAware excluidos: " + r.FlightAwareExcluded);
                text.AppendLine("- Límite alcanzado: " + r.Limit);
            }
            text.AppendLine("\n## Totales");
            text.AppendLine("- Mensajes inspeccionados: " + results.Sum(x => x.Inspected));
            text.AppendLine("- Mensajes con candidatos: " + results.Sum(x => x.MessagesWithCandidates));
            text.AppendLine("- Candidatos brutos: " + results.Sum(x => x.RawCandidates));
            text.AppendLine("- Candidatos finales únicos: " + c.Count);
            text.AppendLine("- Dominios distintos: " + distinctOrigins);
            text.AppendLine("- Formatos físicos: " + formats);
            text.AppendLine("- PDF omitidos: " + results.Sum(x => x.PdfOmitted));
            text.AppendLine("- OTHER: " + results.Sum(x => x.UnsupportedFormat));
            text.AppendLine("- Imágenes no renderizables: " + results.Sum(x => x.NonRenderable));
            text.AppendLine("- Contact sheets: " + sheets.Count);
            text.AppendLine("\nLos candidatos no tienen Label ni GroupId. No se modificaron Gmail, SQL, dataset.csv ni el corpus.");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private static HashSet<string> LoadCorpusHashes(string path)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var regex = new Regex("[A-Fa-f0-9]{64}");
            foreach (var line in File.ReadLines(path).Skip(1)) foreach (Match match in regex.Matches(line)) result.Add(match.Value.ToUpperInvariant());
            return result;
        }

        private static string Header(MessagePart part, string name) { var h = (part.Headers ?? new List<MessagePartHeader>()).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); return h == null ? "" : h.Value ?? ""; }
        private static string MessageDate(Message message) { return message.InternalDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime.ToString("O", CultureInfo.InvariantCulture) : ""; }
        private static string SenderDomain(string sender) { var match = Regex.Match(sender ?? "", @"@(?<domain>[A-Za-z0-9.-]+)"); return match.Success ? match.Groups["domain"].Value.ToLowerInvariant() : ""; }
        private static PhysicalFormat DetectFormat(byte[] bytes) { if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return new PhysicalFormat("PNG", ".png"); if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return new PhysicalFormat("JPEG", ".jpg"); if (bytes.Length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D) return new PhysicalFormat("PDF", ".pdf"); return new PhysicalFormat("OTHER", ""); }
        private static string Limit(int inspected, int origins, int maxInspected, int targetMessages) { if (origins >= targetMessages) return "objetivo defensivo de " + targetMessages + " mensajes con candidatos"; if (inspected >= maxInspected) return "máximo defensivo de " + maxInspected + " mensajes inspeccionados"; return "fin de paginación Gmail"; }
        private static List<WindowSpec> ImagesOnlyWindows() { return new List<WindowSpec> { new WindowSpec("A", "after:2024/08/28 before:2025/08/28", "2024-08-28 a 2025-08-28"), new WindowSpec("B", "after:2023/08/28 before:2024/08/28", "2023-08-28 a 2024-08-28"), new WindowSpec("C", "after:2022/08/28 before:2023/08/28", "2022-08-28 a 2023-08-28"), new WindowSpec("D", "after:2021/08/28 before:2022/08/28", "2021-08-28 a 2022-08-28") }; }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private static string Safe(string value) { var s = string.Concat((value ?? "sin-nombre").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)); return s.Length > 100 ? s.Substring(0, 100) : s; }
        private static string Extension(string mime) { if (string.Equals(mime, "image/png", StringComparison.OrdinalIgnoreCase)) return ".png"; if (string.Equals(mime, "image/gif", StringComparison.OrdinalIgnoreCase)) return ".gif"; return ".jpg"; }
        private static string Trim(string value, int max) { value = (value ?? "").Replace('\r', ' ').Replace('\n', ' '); return value.Length <= max ? value : value.Substring(0, max - 1) + "…"; }
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        private static string FindRoot() { var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); while (d != null) { if (File.Exists(Path.Combine(d.FullName, "RecepcionDocumental.ini"))) return d.FullName; d = d.Parent; } throw new DirectoryNotFoundException("No se encontró la raíz de RecepcionDocumental."); }

        private sealed class Candidate
        {
            internal Candidate(string id, string window, string messageId, string messageDateUtc, string sender, string senderDomain, string filename, string mimeType, string detectedFormat, int width, int height, string sha256, long size, string localPath, string packagePath, string subject, string relation) { Id = id; Window = window; MessageId = messageId; MessageDateUtc = messageDateUtc; Sender = sender; SenderDomain = senderDomain; Filename = filename; MimeType = mimeType; DetectedFormat = detectedFormat; Width = width; Height = height; Sha256 = sha256; Size = size; LocalPath = localPath; PackagePath = packagePath; Subject = subject; Relation = relation; }
            internal string Id { get; private set; } internal string Window { get; private set; } internal string MessageId { get; private set; } internal string MessageDateUtc { get; private set; } internal string Sender { get; private set; } internal string SenderDomain { get; private set; } internal string Filename { get; private set; } internal string MimeType { get; private set; } internal string DetectedFormat { get; private set; } internal int Width { get; private set; } internal int Height { get; private set; } internal string Sha256 { get; private set; } internal long Size { get; private set; } internal string LocalPath { get; private set; } internal string PackagePath { get; private set; } internal string Subject { get; private set; } internal string Relation { get; private set; }
        }
        private sealed class WindowSpec { internal WindowSpec(string name, string query, string period) { Name = name; Query = query; Period = period; } internal string Name { get; private set; } internal string Query { get; private set; } internal string Period { get; private set; } }
        private sealed class WindowResult { internal WindowResult(WindowSpec window, int startCandidateCount) { Window = window; StartCandidateCount = startCandidateCount; } internal WindowSpec Window { get; private set; } internal int StartCandidateCount { get; private set; } internal int Inspected; internal int MessagesWithCandidates; internal int RawCandidates; internal int FinalCandidates; internal int DuplicateCorpus; internal int DuplicatePriorReview; internal int DuplicateHistoricalReview; internal int DuplicateBank; internal int PdfOmitted; internal int UnsupportedFormat; internal int NonRenderable; internal int FlightAwareExcluded; internal string Limit; }
        private sealed class PhysicalFormat { internal PhysicalFormat(string name, string extension) { Name = name; Extension = extension; } internal string Name { get; private set; } internal string Extension { get; private set; } }
    }
}
