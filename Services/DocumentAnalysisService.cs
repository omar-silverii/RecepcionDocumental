using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    public sealed class DocumentCandidate
    {
        public string SourcePath { get; set; }
        public string OriginalName { get; set; }
        public string MimeType { get; set; }
        public string OriginType { get; set; }
        public string InternalContainerPath { get; set; }
        public string OriginHash { get; set; }
        public long SizeBytes { get; set; }
        public InvoiceSelection Selection { get; set; }
    }

    public sealed class AttachmentAnalysis
    {
        public IList<DocumentCandidate> Candidates { get; set; } = new List<DocumentCandidate>();
        public int ContainersZip { get; set; }
        public int ZipFilesAnalyzed { get; set; }
        public int Discarded { get; set; }
    }

    internal sealed class ZipBudget { public int Entries; public long TotalBytes; }

    public static class DocumentAnalysisService
    {
        public static AttachmentAnalysis Analyze(byte[] bytes, string fileName, string mimeType, AttachmentWorkspace workspace)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            Logs.LogProc("DocumentAnalysis | Inicio análisis attachment | NombreOmitido=true");
            var extension = Path.GetExtension(fileName ?? string.Empty);
            var rootPath = workspace.CreatePath(extension); File.WriteAllBytes(rootPath, bytes);
            var result = new AttachmentAnalysis();
            if (IsZip(fileName, mimeType, bytes))
            {
                Logs.LogProc("DocumentAnalysis | ZIP detectado | Profundidad=1");
                AnalyzeZip(rootPath, fileName, mimeType, fileName, 1, workspace, new ZipBudget(), result, true);
            }
            else AnalyzeDocument(rootPath, fileName, mimeType, "DIRECTO", null, HashFile(rootPath), result);
            return result;
        }

        private static void AnalyzeZip(string zipPath, string zipName, string mimeType, string chain, int depth, AttachmentWorkspace workspace, ZipBudget budget, AttachmentAnalysis result, bool root)
        {
            result.ContainersZip++;
            var candidateStart = result.Candidates.Count;
            var discardedStart = result.Discarded;
            var filesStart = result.ZipFilesAnalyzed;
            var config = ConfiguracionSistema.Actual;
            if (depth > config.ZipMaxProfundidad) { AddUnanalyzableZip(zipPath, zipName, mimeType, chain, root, "ZIP anidado supera la profundidad permitida.", result); return; }
            try
            {
                using (var input = File.OpenRead(zipPath))
                using (var zip = new ZipInputStream(input))
                {
                    zip.IsStreamOwner = false; ZipEntry entry;
                    var extractionRoot = Path.Combine(workspace.RootPath, "zip-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(extractionRoot);
                    var extractionPrefix = Path.GetFullPath(extractionRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    while ((entry = zip.GetNextEntry()) != null)
                    {
                        if (++budget.Entries > config.ZipMaxEntradas) throw new InvalidDataException("El ZIP supera MaxEntradas.");
                        if (entry.IsDirectory) continue;
                        if (!entry.IsFile || entry.IsCrypted || !entry.CanDecompress) throw new InvalidDataException("Entrada ZIP no soportada o protegida.");
                        var entryName = (entry.Name ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
                        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName) || entryName.IndexOf(':') >= 0) throw new InvalidDataException("Ruta absoluta o inválida dentro del ZIP.");
                        var destination = Path.GetFullPath(Path.Combine(extractionRoot, entryName));
                        if (!destination.StartsWith(extractionPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Se bloqueó una entrada Zip Slip.");
                        if (File.Exists(destination)) throw new InvalidDataException("El ZIP contiene rutas en colisión.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        long entryBytes = 0; var buffer = new byte[81920]; int read;
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            while ((read = zip.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                entryBytes += read; budget.TotalBytes += read;
                                if (entryBytes > config.ZipMaxBytesPorArchivo) throw new InvalidDataException("Una entrada supera MaxBytesPorArchivo.");
                                if (budget.TotalBytes > config.ZipMaxBytesDescomprimidos) throw new InvalidDataException("La expansión supera MaxBytesDescomprimidos.");
                                output.Write(buffer, 0, read);
                            }
                            output.Flush(true);
                        }
                        result.ZipFilesAnalyzed++;
                        var nestedChain = chain + "!/" + entry.Name;
                        if (nestedChain.Length > 2000) throw new InvalidDataException("La cadena de origen supera la longitud permitida.");
                        if (IsZipFile(destination, entry.Name)) AnalyzeZip(destination, entry.Name, "application/zip", nestedChain, depth + 1, workspace, budget, result, false);
                        else AnalyzeDocument(destination, Path.GetFileName(entry.Name), GuessMime(entry.Name), "ZIP", nestedChain, StableZipOriginHash(nestedChain, destination), result);
                    }
                }
                Logs.LogProc("DocumentAnalysis | ZIP entradas analizadas | Entradas=" + budget.Entries + " | Bytes=" + budget.TotalBytes);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ZipException || ex is ArgumentException)
            {
                while (result.Candidates.Count > candidateStart) result.Candidates.RemoveAt(result.Candidates.Count - 1);
                result.Discarded = discardedStart; result.ZipFilesAnalyzed = filesStart;
                AddUnanalyzableZip(zipPath, zipName, mimeType, chain, root, ex.Message, result);
            }
        }

        private static void AnalyzeDocument(string path, string name, string mime, string originType, string internalPath, string originHash, AttachmentAnalysis result)
        {
            InvoiceSelection selection;
            if (string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdf = MdocPdfTextExtractor.Extract(path); selection = InvoiceSelector.SelectPdf(pdf.Text, pdf.HasUsefulText);
                if (!string.IsNullOrEmpty(pdf.FailureReason)) selection.Reason = pdf.FailureReason + " Requiere OCR futuro.";
            }
            else selection = InvoiceSelector.SelectNonPdf(name);
            Logs.LogProc("DocumentAnalysis | Documento clasificado | Clasificacion=" + selection.Classification + " | Metodo=" + selection.DetectionMethod);
            if (selection.Classification == "DESCARTAR") { result.Discarded++; Logs.LogProc("DocumentAnalysis | Documento descartado | Metodo=" + selection.DetectionMethod); return; }
            result.Candidates.Add(new DocumentCandidate { SourcePath = path, OriginalName = SafeOriginalName(name), MimeType = mime, OriginType = originType, InternalContainerPath = internalPath, OriginHash = originHash, SizeBytes = new FileInfo(path).Length, Selection = selection });
        }

        private static void AddUnanalyzableZip(string path, string name, string mime, string chain, bool root, string reason, AttachmentAnalysis result)
        {
            result.Candidates.Add(new DocumentCandidate { SourcePath = path, OriginalName = SafeOriginalName(name), MimeType = mime ?? "application/zip", OriginType = root ? "DIRECTO" : "ZIP", InternalContainerPath = root ? null : chain, OriginHash = root ? HashFile(path) : StableZipOriginHash(chain, path), SizeBytes = new FileInfo(path).Length, Selection = InvoiceSelector.Review("ZIP_NO_ANALIZABLE", "ZIP no analizable: " + SafeReason(reason), null) });
            Logs.LogProc("DocumentAnalysis | Documento clasificado | Clasificacion=REVISAR | Metodo=ZIP_NO_ANALIZABLE");
        }

        private static bool IsZip(string name, string mime, byte[] bytes)
        {
            if (string.Equals(Path.GetExtension(name ?? string.Empty), ".zip", StringComparison.OrdinalIgnoreCase) || string.Equals(mime, "application/zip", StringComparison.OrdinalIgnoreCase)) return true;
            return bytes != null && bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4b && (bytes[2] == 3 || bytes[2] == 5 || bytes[2] == 7) && (bytes[3] == 4 || bytes[3] == 6 || bytes[3] == 8);
        }
        private static bool IsZipFile(string path, string name)
        {
            if (IsZip(name, null, null)) return true;
            using (var stream = File.OpenRead(path)) { var header = new byte[4]; return stream.Read(header, 0, 4) == 4 && IsZip(name, null, header); }
        }
        private static string GuessMime(string name) { return string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : null; }
        private static string StableZipOriginHash(string chain, string path) { return HashBytes(Encoding.UTF8.GetBytes((chain ?? string.Empty) + "|" + HashFile(path))); }
        internal static string HashFile(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(stream)); }
        private static string HashBytes(byte[] bytes) { using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(bytes)); }
        private static string ToHex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant(); }
        private static string SafeReason(string value) { var safe = (value ?? "Error de ZIP").Replace('\r', ' ').Replace('\n', ' '); return safe.Length > 300 ? safe.Substring(0, 300) : safe; }
        private static string SafeOriginalName(string value) { var name = string.IsNullOrWhiteSpace(value) ? "documento" : value; return name.Length > 500 ? name.Substring(0, 500) : name; }
    }
}
