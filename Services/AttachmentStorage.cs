using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RecepcionDocumental.Services
{
    public sealed class StoredAttachment
    {
        public string FullPath { get; set; }
        public string HashSha256 { get; set; }
        public long Size { get; set; }
    }

    public static class AttachmentStorage
    {
        public static StoredAttachment Save(byte[] bytes, DateTime messageDateUtc, string gmailMessageId, string originalName, string identity)
        {
            if (bytes == null) throw new InvalidDataException("El adjunto no contiene datos válidos.");
            var root = ConfigurationManager.AppSettings["AdjuntosRootPath"];
            if (string.IsNullOrWhiteSpace(root)) throw new ConfigurationErrorsException("Falta configurar AdjuntosRootPath.");

            root = Path.GetFullPath(root.Trim());
            Directory.CreateDirectory(root);
            EnsureWritable(root);

            var directory = Path.Combine(root, messageDateUtc.ToString("yyyy"), messageDateUtc.ToString("MM"), messageDateUtc.ToString("dd"), SanitizeSegment(gmailMessageId, "mensaje"));
            Directory.CreateDirectory(directory);
            var hash = ComputeSha256(bytes);
            var safeName = BuildSafeFileName(originalName, identity);
            var destination = Path.Combine(directory, safeName);
            var temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(destination))
                {
                    if (string.Equals(ComputeSha256(File.ReadAllBytes(destination)), hash, StringComparison.OrdinalIgnoreCase)) File.Delete(temp);
                    else
                    {
                        destination = Path.Combine(directory, Path.GetFileNameWithoutExtension(safeName) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(safeName));
                        File.Move(temp, destination);
                    }
                }
                else File.Move(temp, destination);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }

            return new StoredAttachment { FullPath = destination, HashSha256 = hash, Size = bytes.LongLength };
        }

        private static void EnsureWritable(string root)
        {
            var probe = Path.Combine(root, ".write-test-" + Guid.NewGuid().ToString("N"));
            try { File.WriteAllBytes(probe, new byte[0]); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }

        private static string BuildSafeFileName(string originalName, string identity)
        {
            var name = Path.GetFileName(originalName ?? string.Empty);
            var extension = SanitizeExtension(Path.GetExtension(name));
            var stem = SanitizeSegment(Path.GetFileNameWithoutExtension(name), "adjunto");
            if (stem.Length > 100) stem = stem.Substring(0, 100);
            var identityHash = ComputeSha256(Encoding.UTF8.GetBytes(identity ?? Guid.NewGuid().ToString("N"))).Substring(0, 12);
            return stem + "_" + identityHash + extension;
        }

        private static string SanitizeSegment(string value, string fallback)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string((value ?? string.Empty).Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray()).Trim().Trim('.');
            if (string.IsNullOrWhiteSpace(clean) || IsReserved(clean)) clean = fallback;
            return clean.Length > 120 ? clean.Substring(0, 120) : clean;
        }

        private static string SanitizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 16) return string.Empty;
            return new string(extension.Where(c => char.IsLetterOrDigit(c) || c == '.').ToArray());
        }

        private static bool IsReserved(string name)
        {
            var stem = name.Split('.')[0].ToUpperInvariant();
            return stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem.StartsWith("COM") && stem.Length == 4 && char.IsDigit(stem[3]) || stem.StartsWith("LPT") && stem.Length == 4 && char.IsDigit(stem[3]);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
