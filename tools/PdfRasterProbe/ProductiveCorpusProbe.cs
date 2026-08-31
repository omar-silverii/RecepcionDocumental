using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
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
using PDFtoImage;

namespace PdfRasterProbe
{
    public static class ProductiveCorpusProbe
    {
        public static int Run(string[] args)
        {
            try
            {
                var root = FindRoot();
                var output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2j_Productivos_Revision");
                if (Directory.Exists(output) || File.Exists(output + ".zip")) throw new IOException("La salida ya existe; no se sobrescribe: " + output);
                var corpus = Hashes(Path.Combine(root, "tools", "DocumentAiProbe", "dataset.csv"));
                var f = Hashes(Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2f_NoDocumento_Revision", "candidatos.csv"));
                var h = Hashes(Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2h_NoDocumento_Historico_Revision", "candidatos.csv"));
                var i = Hashes(Path.Combine(root, "tools", "DocumentAiProbe", "H1D3A2i_Imagenes_Historicas_Revision", "candidatos.csv"));
                var other = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in Directory.GetFiles(Path.Combine(root, "tools", "DocumentAiProbe"), "reviewed-decisions*.csv")) other.UnionWith(Hashes(path));
                var rows = LoadRows();
                Directory.CreateDirectory(output);
                var originals = Path.Combine(output, "_candidatos_locales"); Directory.CreateDirectory(originals);
                var renders = Path.Combine(output, "renders"); Directory.CreateDirectory(renders);
                var candidates = new List<Candidate>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var missing = 0; var unsupported = 0; var nonRenderable = 0; var dc = 0; var df = 0; var dh = 0; var di = 0; var dother = 0; var internalDupes = 0;
                foreach (var row in rows)
                {
                    if (!File.Exists(row.Path)) { missing++; continue; }
                    var bytes = File.ReadAllBytes(row.Path); var hash = Hash(bytes); var format = Format(bytes);
                    if (corpus.Contains(hash)) { dc++; continue; }
                    if (f.Contains(hash)) { df++; continue; }
                    if (h.Contains(hash)) { dh++; continue; }
                    if (i.Contains(hash)) { di++; continue; }
                    if (other.Contains(hash)) { dother++; continue; }
                    if (!seen.Add(hash)) { internalDupes++; continue; }
                    if (format.Name == "OTHER") { unsupported++; continue; }
                    var id = "J" + (candidates.Count + 1).ToString("D4", CultureInfo.InvariantCulture);
                    var storedName = id + "_" + Safe(Path.GetFileNameWithoutExtension(row.Name)) + format.Extension;
                    var stored = Path.Combine(originals, storedName); File.WriteAllBytes(stored, bytes);
                    string preview; int pages; int width; int height;
                    if (!Preview(row.Path, bytes, format.Name, renders, id, out preview, out pages, out width, out height)) { File.Delete(stored); nonRenderable++; continue; }
                    candidates.Add(new Candidate(id, row, format.Name, hash, bytes.Length, pages, width, height, stored, "_candidatos_locales/" + storedName, preview, string.IsNullOrWhiteSpace(preview) ? "" : "renders/" + Path.GetFileName(preview)));
                }
                WriteCsv(Path.Combine(output, "candidatos.csv"), candidates);
                var sheets = Sheets(output, candidates);
                WriteSummary(Path.Combine(output, "resumen.md"), rows, candidates, dc, df, dh, di, dother, internalDupes, missing, unsupported, nonRenderable, sheets.Count);
                using (var zip = ZipFile.Open(output + ".zip", ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(Path.Combine(output, "candidatos.csv"), "candidatos.csv", CompressionLevel.Optimal);
                    zip.CreateEntryFromFile(Path.Combine(output, "resumen.md"), "resumen.md", CompressionLevel.Optimal);
                    foreach (var c in candidates) { zip.CreateEntryFromFile(c.LocalPath, c.PackagePath, CompressionLevel.Optimal); zip.CreateEntryFromFile(c.PreviewPath, c.PreviewPackagePath, CompressionLevel.Optimal); }
                    foreach (var s in sheets) zip.CreateEntryFromFile(s, Path.GetFileName(s), CompressionLevel.Optimal);
                }
                Console.WriteLine("PRODUCTIVOS | FACTURA=" + rows.Count(x => x.Classification == "FACTURA") + " | REVISAR=" + rows.Count(x => x.Classification == "REVISAR") + " | Candidatos=" + candidates.Count + " | Corpus=" + dc + " | H1D3A2f=" + df + " | H1D3A2h=" + dh + " | H1D3A2i=" + di + " | Otros=" + dother + " | Internos=" + internalDupes + " | NoAccesibles=" + missing + " | NoSoportados=" + unsupported + " | NoRenderizables=" + nonRenderable);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ERROR | " + ex.GetType().Name + ": " + ex.Message); return 1; }
        }

        private static List<Row> LoadRows()
        {
            var result = new List<Row>(); var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"];
            if (cs == null) throw new ConfigurationErrorsException("Falta DefaultConnection.");
            const string sql = @"SELECT d.Id,d.Clasificacion,d.NombreOriginal,d.RutaLocal,d.MimeType,d.FechaAltaUtc,m.FechaMensajeUtc,m.Remitente,d.OrigenTipo,d.MetodoDeteccion FROM dbo.DocumentoRecepcion d INNER JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId ORDER BY CASE d.Clasificacion WHEN N'FACTURA' THEN 0 ELSE 1 END,d.Id;";
            using (var cn = new SqlConnection(cs.ConnectionString)) using (var cmd = new SqlCommand(sql, cn)) { cn.Open(); using (var r = cmd.ExecuteReader()) while (r.Read()) result.Add(new Row(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? "" : r.GetString(4), r.GetDateTime(5), r.GetDateTime(6), Domain(r.GetString(7)), r.GetString(8), r.GetString(9))); }
            return result;
        }
        private static bool Preview(string path, byte[] bytes, string format, string renders, string id, out string preview, out int pages, out int width, out int height)
        {
            preview = Path.Combine(renders, id + "-preview.png"); pages = 1; width = 0; height = 0;
            try
            {
                if (format == "PDF") { pages = Conversion.GetPageCount(bytes); if (pages <= 0) return false; Conversion.SavePng(preview, bytes, 0, options: new RenderOptions { Dpi = 120 }); }
                else { using (var source = Image.FromFile(path)) { width = source.Width; height = source.Height; source.Save(preview, ImageFormat.Png); } }
                using (var image = Image.FromFile(preview)) { width = image.Width; height = image.Height; }
                return width > 0 && height > 0;
            }
            catch { if (File.Exists(preview)) File.Delete(preview); return false; }
        }
        private static List<string> Sheets(string output, IList<Candidate> candidates)
        {
            var result = new List<string>(); const int per = 6;
            for (var offset = 0; offset < candidates.Count; offset += per) { var path = Path.Combine(output, "contact-" + (result.Count + 1).ToString("D2") + ".jpg"); using (var canvas = new Bitmap(1800, 2100)) using (var g = Graphics.FromImage(canvas)) using (var title = new Font("Arial", 20, FontStyle.Bold)) using (var detail = new Font("Arial", 14)) { g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic; for (var n = 0; n < per && offset + n < candidates.Count; n++) { var c = candidates[offset + n]; var x = 30 + (n % 2) * 890; var y = 25 + (n / 2) * 690; g.DrawRectangle(Pens.Gray, x, y, 850, 650); using (var image = Image.FromFile(c.PreviewPath)) { var scale = Math.Min(820d / image.Width, 500d / image.Height); var w = (int)(image.Width * scale); var h = (int)(image.Height * scale); g.DrawImage(image, x + 15 + (820 - w) / 2, y + 15 + (500 - h) / 2, w, h); } g.DrawString(c.Id + " | Documento=" + c.Row.Id + " | " + c.Row.Classification, title, Brushes.Black, x + 15, y + 525); g.DrawString(Trim(c.Row.Name, 75), detail, Brushes.Black, x + 15, y + 570); g.DrawString(c.Row.MessageDate.ToString("yyyy-MM-dd") + " | " + c.Row.SenderDomain, detail, Brushes.DimGray, x + 15, y + 605); } canvas.Save(path, ImageFormat.Jpeg); } result.Add(path); }
            return result;
        }
        private static void WriteCsv(string path, IEnumerable<Candidate> c) { var lines = new List<string> { "CandidateId,DocumentoRecepcionId,ProductiveClassification,OriginalFilename,SafeOriginPath,DetectedFormat,SHA256,SizeBytes,PageCount,Width,Height,ReceivedUtc,MessageDateUtc,SenderDomain,OriginType,DetectionMethod,PackagePath,PreviewPath,TechnicalNotes" }; lines.AddRange(c.Select(x => string.Join(",", new[] { x.Id, x.Row.Id.ToString(), x.Row.Classification, x.Row.Name, x.Row.Path, x.Format, x.Hash, x.Size.ToString(), x.Pages.ToString(), x.Width.ToString(), x.Height.ToString(), x.Row.Received.ToString("O"), x.Row.MessageDate.ToString("O"), x.Row.SenderDomain, x.Row.OriginType, x.Row.DetectionMethod, x.PackagePath, x.PreviewPackagePath, "Original preservado; preview auxiliar de revisión." }.Select(Csv)))); File.WriteAllLines(path, lines, new UTF8Encoding(false)); }
        private static void WriteSummary(string path, IList<Row> rows, IList<Candidate> c, int dc, int df, int dh, int di, int other, int dup, int missing, int unsupported, int nonrender, int sheets) { var text = "# H1D3A2j — documentos productivos para revisión\n\n- FACTURA inspeccionados: " + rows.Count(x => x.Classification == "FACTURA") + "\n- REVISAR inspeccionados: " + rows.Count(x => x.Classification == "REVISAR") + "\n- Candidatos brutos: " + rows.Count + "\n- Candidatos finales: " + c.Count + "\n- Excluidos por corpus: " + dc + "\n- Excluidos por H1D3A2f: " + df + "\n- Excluidos por H1D3A2h: " + dh + "\n- Excluidos por H1D3A2i: " + di + "\n- Excluidos por otros bancos: " + other + "\n- Duplicados internos: " + dup + "\n- No accesibles: " + missing + "\n- Formato no soportado: " + unsupported + "\n- No renderizables: " + nonrender + "\n- Formatos: " + string.Join(", ", c.GroupBy(x => x.Format).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Count())) + "\n- Desde FACTURA: " + c.Count(x => x.Row.Classification == "FACTURA") + "\n- Desde REVISAR: " + c.Count(x => x.Row.Classification == "REVISAR") + "\n- Contact sheets: " + sheets + "\n\nLa clasificación productiva es sólo contexto. El banco no contiene Label de entrenamiento ni GroupId.\n"; File.WriteAllText(path, text, new UTF8Encoding(false)); }
        private static HashSet<string> Hashes(string path) { var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); if (!File.Exists(path)) return set; var rx = new Regex("[A-Fa-f0-9]{64}"); foreach (var line in File.ReadLines(path).Skip(1)) foreach (Match m in rx.Matches(line)) set.Add(m.Value.ToUpperInvariant()); return set; }
        private static string Hash(byte[] b) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", ""); }
        private static Physical Format(byte[] b) { if (b.Length >= 5 && Encoding.ASCII.GetString(b, 0, 5) == "%PDF-") return new Physical("PDF", ".pdf"); if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4e && b[3] == 0x47) return new Physical("PNG", ".png"); if (b.Length >= 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff) return new Physical("JPEG", ".jpg"); return new Physical("OTHER", ""); }
        private static string Domain(string sender) { var m = Regex.Match(sender ?? "", "@(?<d>[A-Za-z0-9.-]+)"); return m.Success ? m.Groups["d"].Value.ToLowerInvariant() : ""; }
        private static string Safe(string s) { var v = string.Concat((s ?? "archivo").Select(x => Path.GetInvalidFileNameChars().Contains(x) ? '_' : x)); return v.Length > 100 ? v.Substring(0, 100) : v; }
        private static string Trim(string s, int n) { s = (s ?? "").Replace('\r', ' ').Replace('\n', ' '); return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }
        private static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
        private static string FindRoot() { var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); while (d != null) { if (File.Exists(Path.Combine(d.FullName, "RecepcionDocumental.ini"))) return d.FullName; d = d.Parent; } throw new DirectoryNotFoundException("No se encontró la raíz."); }
        private sealed class Row { internal Row(long id, string classification, string name, string path, string mime, DateTime received, DateTime messageDate, string senderDomain, string originType, string detectionMethod) { Id = id; Classification = classification; Name = name; Path = path; Mime = mime; Received = received; MessageDate = messageDate; SenderDomain = senderDomain; OriginType = originType; DetectionMethod = detectionMethod; } internal long Id; internal string Classification; internal string Name; internal string Path; internal string Mime; internal DateTime Received; internal DateTime MessageDate; internal string SenderDomain; internal string OriginType; internal string DetectionMethod; }
        private sealed class Candidate { internal Candidate(string id, Row row, string format, string hash, long size, int pages, int width, int height, string local, string package, string preview, string previewPackage) { Id = id; Row = row; Format = format; Hash = hash; Size = size; Pages = pages; Width = width; Height = height; LocalPath = local; PackagePath = package; PreviewPath = preview; PreviewPackagePath = previewPackage; } internal string Id; internal Row Row; internal string Format; internal string Hash; internal long Size; internal int Pages; internal int Width; internal int Height; internal string LocalPath; internal string PackagePath; internal string PreviewPath; internal string PreviewPackagePath; }
        private sealed class Physical { internal Physical(string name, string extension) { Name = name; Extension = extension; } internal string Name; internal string Extension; }
    }
}
