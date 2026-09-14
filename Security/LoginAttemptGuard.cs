using System;
using System.Collections.Generic;

namespace RecepcionDocumental.Security
{
    public static class LoginAttemptGuard
    {
        private const int MaxFailures = 5;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, AttemptState> Attempts = new Dictionary<string, AttemptState>(StringComparer.Ordinal);

        public static bool CanAttempt(string clientKey, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            var key = NormalizeKey(clientKey);
            var now = DateTime.UtcNow;

            lock (Sync)
            {
                AttemptState state;
                if (!Attempts.TryGetValue(key, out state)) return true;

                if (state.BlockedUntilUtc > now)
                {
                    retryAfterSeconds = (int)Math.Ceiling((state.BlockedUntilUtc - now).TotalSeconds);
                    return false;
                }

                if (now - state.WindowStartUtc >= Window)
                {
                    Attempts.Remove(key);
                }

                return true;
            }
        }

        public static void ReportFailure(string clientKey)
        {
            var key = NormalizeKey(clientKey);
            var now = DateTime.UtcNow;

            lock (Sync)
            {
                AttemptState state;
                if (!Attempts.TryGetValue(key, out state) || now - state.WindowStartUtc >= Window)
                {
                    state = new AttemptState { WindowStartUtc = now };
                    Attempts[key] = state;
                }

                state.Failures++;
                if (state.Failures >= MaxFailures)
                {
                    state.BlockedUntilUtc = now.Add(BlockDuration);
                }
            }
        }

        public static void ReportSuccess(string clientKey)
        {
            lock (Sync)
            {
                Attempts.Remove(NormalizeKey(clientKey));
            }
        }

        private static string NormalizeKey(string clientKey)
        {
            return string.IsNullOrWhiteSpace(clientKey) ? "(sin-ip)" : clientKey.Trim();
        }

        private sealed class AttemptState
        {
            public int Failures { get; set; }
            public DateTime WindowStartUtc { get; set; }
            public DateTime BlockedUntilUtc { get; set; }
        }
    }
}
