using System;
using System.Collections.Generic;

namespace RecepcionDocumental.Services
{
    internal static class KnownNonDocumentGroundTruth
    {
        private static readonly HashSet<string> ReviewedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Logo "Great Place to Work" revisado y protegido en el corpus local.
            "4076445FF73213039D7402664EB4FC48546391227FAB11DAA3224DA874701C8C"
        };

        internal static bool TrySelect(string sha256, out InvoiceSelection selection)
        {
            selection = null;
            if (string.IsNullOrWhiteSpace(sha256) || !ReviewedHashes.Contains(sha256.Trim())) return false;

            selection = new InvoiceSelection
            {
                Classification = "DESCARTAR",
                DetectionMethod = "GROUND_TRUTH_NO_DOCUMENTO",
                Confidence = 100,
                Reason = "El SHA-256 coincide con un NO_DOCUMENTO revisado y protegido del corpus."
            };
            return true;
        }
    }
}
