using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using RecepcionDocumental.Configuration;

namespace RecepcionDocumental.Services
{
    public sealed class DocumentStoredFile { public string FullPath { get; set; } public string HashSha256 { get; set; } public long Size { get; set; } public bool CreatedByThisCall { get; set; } }

    public static class DocumentStorage
    {
        public static DocumentStoredFile Save(string sourcePath, string classification, DateTime messageDateUtc, string gmailMessageId, string originalName, string originHash)
        {
            var root = classification == "FACTURA" ? ConfiguracionSistema.Actual.RutaFacturas : ConfiguracionSistema.Actual.RutaRevisar;
            var directory = Path.Combine(root, messageDateUtc.ToString("yyyy"), messageDateUtc.ToString("MM"), messageDateUtc.ToString("dd"), SafeSegment(gmailMessageId));
            Directory.CreateDirectory(directory);
            var extension = SafeExtension(Path.GetExtension(originalName));
            var baseName = SafeSegment(Path.GetFileNameWithoutExtension(originalName)); if (string.IsNullOrWhiteSpace(baseName)) baseName = "documento";
            var finalPath = Path.Combine(directory, baseName + "_" + originHash.Substring(0, 12) + extension);
            if (File.Exists(finalPath)) return Inspect(finalPath,false);
            var temporary = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            var created=false;
            try
            {
                using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                try { File.Move(temporary, finalPath); created=true; }
                catch (IOException) when (File.Exists(finalPath)) { File.Delete(temporary); }
                return Inspect(finalPath,created);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static DocumentStoredFile Inspect(string path,bool createdByThisCall)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return new DocumentStoredFile { FullPath = path, Size = stream.Length, HashSha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant(), CreatedByThisCall=createdByThisCall };
        }

        private static string SafeSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars(); var safe = new string((value ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(safe, StringComparer.OrdinalIgnoreCase)) safe = "_" + safe; return safe.Length > 120 ? safe.Substring(0, 120) : safe;
        }
        private static string SafeExtension(string value) { return string.IsNullOrWhiteSpace(value) || value.Length > 20 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? string.Empty : value.ToLowerInvariant(); }
    }
}
