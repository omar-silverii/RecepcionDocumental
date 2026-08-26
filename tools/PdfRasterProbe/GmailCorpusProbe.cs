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
        private const int TargetMessages = 15;
        private const int MaxMessagesInspected = 2000;

        public static int Run(string[] args)
        {
            try
            {
                var root = FindRoot();
                var output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2f_NoDocumento_Revision");
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
                var seen = new HashSet<string>(corpusHashes, StringComparer.OrdinalIgnoreCase);
                var candidates = new List<Candidate>();
                var inspected = 0;
                var messagesWithCandidates = 0;
                var duplicateCorpus = 0;
                var duplicateBank = 0;
                var unsupported = 0;
                var excludedFlightAwareMessages = 0;

                using (var client = GmailOAuthService.CreateAuthorizedClient(settings, account.Email, token))
                {
                    string page = null;
                    do
                    {
                        var list = client.Service.Users.Messages.List("me");
                        list.Q = "newer_than:365d";
                        list.MaxResults = 100;
                        list.PageToken = page;
                        var response = list.Execute();
                        foreach (var stub in response.Messages ?? new List<Message>())
                        {
                            if (inspected++ >= MaxMessagesInspected || messagesWithCandidates >= TargetMessages) break;
                            var get = client.Service.Users.Messages.Get("me", stub.Id);
                            get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                            var message = get.Execute();
                            var subject = Header(message.Payload, "Subject");
                            if (subject.IndexOf("FlightAware", StringComparison.OrdinalIgnoreCase) >= 0) { excludedFlightAwareMessages++; continue; }
                            var parts = new List<MessagePart>();
                            CollectImages(message.Payload, parts);
                            var acceptedForMessage = 0;
                            foreach (var part in parts)
                            {
                                if (acceptedForMessage >= MaxPerMessage) break;
                                var bytes = Download(client.Service, message.Id, part);
                                if (bytes == null || bytes.Length == 0) continue;
                                var hash = Hash(bytes);
                                if (corpusHashes.Contains(hash)) { duplicateCorpus++; continue; }
                                if (!seen.Add(hash)) { duplicateBank++; continue; }
                                if (!CanRender(bytes)) { unsupported++; continue; }
                                var id = "G" + (candidates.Count + 1).ToString("D4", CultureInfo.InvariantCulture);
                                var filename = string.IsNullOrWhiteSpace(part.Filename) ? "inline-" + Safe(part.PartId) + Extension(part.MimeType) : Path.GetFileName(part.Filename);
                                var stored = Path.Combine(local, id + "_" + Safe(filename));
                                File.WriteAllBytes(stored, bytes);
                                candidates.Add(new Candidate(id, message.Id, filename, part.MimeType ?? "", hash, bytes.Length, stored, subject,
                                    "PartId=" + (part.PartId ?? "") + "; Disposition=" + Header(part, "Content-Disposition") + "; ContentId=" + Header(part, "Content-ID")));
                                acceptedForMessage++;
                            }
                            if (acceptedForMessage > 0) messagesWithCandidates++;
                        }
                        page = response.NextPageToken;
                    } while (!string.IsNullOrWhiteSpace(page) && inspected < MaxMessagesInspected && messagesWithCandidates < TargetMessages);
                }

                WriteCsv(Path.Combine(output, "candidatos.csv"), candidates);
                var sheets = WriteSheets(output, candidates);
                WriteSummary(Path.Combine(output, "resumen.md"), candidates, inspected, messagesWithCandidates, duplicateCorpus, duplicateBank, unsupported, excludedFlightAwareMessages, sheets);
                var zip = output + ".zip";
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(Path.Combine(output, "candidatos.csv"), "candidatos.csv", CompressionLevel.Optimal);
                    archive.CreateEntryFromFile(Path.Combine(output, "resumen.md"), "resumen.md", CompressionLevel.Optimal);
                    foreach (var sheet in sheets) archive.CreateEntryFromFile(sheet, Path.GetFileName(sheet), CompressionLevel.Optimal);
                }
                Console.WriteLine("GMAIL_CORPUS | MensajesInspeccionados=" + inspected + " | Origenes=" + messagesWithCandidates + " | Candidatos=" + candidates.Count + " | DuplicadosCorpus=" + duplicateCorpus + " | DuplicadosBanco=" + duplicateBank + " | FlightAwareExcluidos=" + excludedFlightAwareMessages + " | NoRenderizables=" + unsupported);
                Console.WriteLine("SALIDA | Carpeta=" + output + " | Zip=" + zip + " | ContactSheets=" + sheets.Count);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ERROR | " + ex.GetType().Name + ": " + ex.Message); return 1; }
        }

        private static void CollectImages(MessagePart part, IList<MessagePart> result)
        {
            if (part == null) return;
            if (!string.IsNullOrWhiteSpace(part.MimeType) && part.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && part.Body != null && (!string.IsNullOrWhiteSpace(part.Body.AttachmentId) || !string.IsNullOrWhiteSpace(part.Body.Data))) result.Add(part);
            foreach (var child in part.Parts ?? new List<MessagePart>()) CollectImages(child, result);
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

        private static bool CanRender(byte[] bytes)
        {
            try { using (var ms = new MemoryStream(bytes)) using (var image = Image.FromStream(ms, true, true)) return image.Width > 0 && image.Height > 0; }
            catch { return false; }
        }

        private static List<string> WriteSheets(string output, IList<Candidate> candidates)
        {
            var result = new List<string>();
            const int perSheet = 6;
            for (var offset = 0; offset < candidates.Count; offset += perSheet)
            {
                var path = Path.Combine(output, "contact-" + (result.Count + 1).ToString("D2", CultureInfo.InvariantCulture) + ".jpg");
                using (var canvas = new Bitmap(1800, 2100))
                using (var g = Graphics.FromImage(canvas))
                using (var title = new Font("Arial", 22, FontStyle.Bold))
                using (var detail = new Font("Arial", 15))
                {
                    g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    for (var i = 0; i < perSheet && offset + i < candidates.Count; i++)
                    {
                        var c = candidates[offset + i]; var col = i % 2; var row = i / 2; var x = 30 + col * 890; var y = 25 + row * 690;
                        g.DrawRectangle(Pens.DarkGray, x, y, 850, 650);
                        using (var image = Image.FromFile(c.LocalPath))
                        {
                            var box = new Rectangle(x + 15, y + 15, 820, 500); var scale = Math.Min((double)box.Width / image.Width, (double)box.Height / image.Height); var w = (int)(image.Width * scale); var h = (int)(image.Height * scale);
                            g.DrawImage(image, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                        }
                        g.DrawString(c.Id + " | MessageId=" + c.MessageId, title, Brushes.Black, x + 15, y + 525);
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
            var lines = new List<string> { "CandidateId,MessageId,OriginalFilename,MimeType,SHA256,SizeBytes,LocalPath,Subject,OriginRelation" };
            lines.AddRange(candidates.Select(c => string.Join(",", new[] { c.Id, c.MessageId, c.Filename, c.MimeType, c.Sha256, c.Size.ToString(CultureInfo.InvariantCulture), c.LocalPath, c.Subject, c.Relation }.Select(Csv))));
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static void WriteSummary(string path, IList<Candidate> c, int inspected, int origins, int corpusDupes, int bankDupes, int unsupported, int flightAwareExcluded, IList<string> sheets)
        {
            var types = string.Join(", ", c.GroupBy(x => x.MimeType).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Count()));
            var text = "# H1D3A2f — revisión NO_DOCUMENTO\n\n- Ventana Gmail consultada: 365 días\n- Mensajes inspeccionados: " + inspected + "\n- Mensajes/orígenes con candidatos: " + origins + "\n- Candidatos gráficos únicos: " + c.Count + "\n- Máximo aplicado: " + MaxPerMessage + " imágenes por MessageId\n- Mensajes FlightAware excluidos deliberadamente: " + flightAwareExcluded + "\n- Hashes excluidos por existir en dataset.csv: " + corpusDupes + "\n- Duplicados excluidos dentro del banco: " + bankDupes + "\n- Formatos no renderizables excluidos: " + unsupported + "\n- Contact sheets: " + sheets.Count + "\n- MIME: " + types + "\n\nLos candidatos no tienen Label ni GroupId. MessageId y metadatos MIME se conservan sólo como contexto para revisión humana. No se modificaron Gmail, SQL, dataset.csv ni el corpus.\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static HashSet<string> LoadCorpusHashes(string path)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var regex = new Regex("[A-Fa-f0-9]{64}");
            foreach (var line in File.ReadLines(path).Skip(1)) foreach (Match match in regex.Matches(line)) result.Add(match.Value.ToUpperInvariant());
            return result;
        }

        private static string Header(MessagePart part, string name) { var h = (part.Headers ?? new List<MessagePartHeader>()).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); return h == null ? "" : h.Value ?? ""; }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private static string Safe(string value) { var s = string.Concat((value ?? "sin-nombre").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)); return s.Length > 100 ? s.Substring(0, 100) : s; }
        private static string Extension(string mime) { if (string.Equals(mime, "image/png", StringComparison.OrdinalIgnoreCase)) return ".png"; if (string.Equals(mime, "image/gif", StringComparison.OrdinalIgnoreCase)) return ".gif"; return ".jpg"; }
        private static string Trim(string value, int max) { value = (value ?? "").Replace('\r', ' ').Replace('\n', ' '); return value.Length <= max ? value : value.Substring(0, max - 1) + "…"; }
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        private static string FindRoot() { var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); while (d != null) { if (File.Exists(Path.Combine(d.FullName, "RecepcionDocumental.ini"))) return d.FullName; d = d.Parent; } throw new DirectoryNotFoundException("No se encontró la raíz de RecepcionDocumental."); }

        private sealed class Candidate
        {
            internal Candidate(string id, string messageId, string filename, string mimeType, string sha256, long size, string localPath, string subject, string relation) { Id = id; MessageId = messageId; Filename = filename; MimeType = mimeType; Sha256 = sha256; Size = size; LocalPath = localPath; Subject = subject; Relation = relation; }
            internal string Id { get; private set; } internal string MessageId { get; private set; } internal string Filename { get; private set; } internal string MimeType { get; private set; } internal string Sha256 { get; private set; } internal long Size { get; private set; } internal string LocalPath { get; private set; } internal string Subject { get; private set; } internal string Relation { get; private set; }
        }
    }
}
