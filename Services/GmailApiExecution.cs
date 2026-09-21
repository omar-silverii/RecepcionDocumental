using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    internal static class GmailApiExecution
    {
        private const int MaxRetries = 6;
        private const int ExpensiveCallIntervalMilliseconds = 250;
        private static readonly SemaphoreSlim ExpensiveCallGate = new SemaphoreSlim(1, 1);
        private static readonly object RandomGate = new object();
        private static readonly Random Random = new Random();
        private static long _lastExpensiveCallTimestamp;

        public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, bool expensive, string gmailMessageId = null, string partId = null)
        {
            if (operation == null) throw new ArgumentNullException("operation");

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (expensive) await WaitForExpensiveCallSlotAsync().ConfigureAwait(false);
                    return await operation().ConfigureAwait(false);
                }
                catch (GoogleApiException ex)
                {
                    string reason;
                    if (!IsTransient(ex, out reason) || attempt >= MaxRetries) throw;

                    var delayMilliseconds = GetRetryDelayMilliseconds(attempt);
                    Logs.LogProc(BuildRetryLog(operationName, attempt + 1, ex, reason, delayMilliseconds, gmailMessageId, partId));
                    await Task.Delay(delayMilliseconds).ConfigureAwait(false);
                }
            }
        }

        internal static bool IsTransient(GoogleApiException exception, out string reason)
        {
            reason = GetReason(exception);
            if (exception == null) return false;

            var status = (int)exception.HttpStatusCode;
            if (status == 429 || status == 500 || status == 502 || status == 503 || status == 504) return true;
            if (status != 403) return false;

            if (string.Equals(reason, "rateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "userRateLimitExceeded", StringComparison.OrdinalIgnoreCase)) return true;

            var message = (exception.Message ?? string.Empty) + " " +
                          (exception.Error == null ? string.Empty : exception.Error.Message ?? string.Empty);
            return message.IndexOf("quota exceeded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetReason(GoogleApiException exception)
        {
            if (exception == null || exception.Error == null || exception.Error.Errors == null) return null;
            var errors = exception.Error.Errors.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Reason)).ToList();
            var error = errors.FirstOrDefault(item =>
                            string.Equals(item.Reason, "rateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.Reason, "userRateLimitExceeded", StringComparison.OrdinalIgnoreCase)) ??
                        errors.FirstOrDefault();
            return error == null ? null : error.Reason;
        }

        private static int GetRetryDelayMilliseconds(int zeroBasedRetry)
        {
            var baseMilliseconds = 1000 * (1 << zeroBasedRetry);
            int jitter;
            lock (RandomGate) jitter = Random.Next(0, 1001);
            return baseMilliseconds + jitter;
        }

        private static async Task WaitForExpensiveCallSlotAsync()
        {
            await ExpensiveCallGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var now = Stopwatch.GetTimestamp();
                if (_lastExpensiveCallTimestamp != 0)
                {
                    var elapsedMilliseconds = (now - _lastExpensiveCallTimestamp) * 1000L / Stopwatch.Frequency;
                    var remaining = ExpensiveCallIntervalMilliseconds - elapsedMilliseconds;
                    if (remaining > 0) await Task.Delay((int)remaining).ConfigureAwait(false);
                }
                _lastExpensiveCallTimestamp = Stopwatch.GetTimestamp();
            }
            finally
            {
                ExpensiveCallGate.Release();
            }
        }

        private static string BuildRetryLog(string operationName, int attempt, GoogleApiException exception, string reason, int delayMilliseconds, string gmailMessageId, string partId)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append("GmailApiRetry | Operacion=").Append(Logs.SanitizarMensaje(operationName));
            builder.Append(" | Intento=").Append(attempt.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | HttpStatus=").Append(((int)exception.HttpStatusCode).ToString(CultureInfo.InvariantCulture));
            builder.Append(" | Reason=").Append(Logs.SanitizarMensaje(string.IsNullOrWhiteSpace(reason) ? "N/D" : reason));
            builder.Append(" | EsperaMs=").Append(delayMilliseconds.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(gmailMessageId)) builder.Append(" | GmailMessageId=").Append(Logs.SanitizarMensaje(gmailMessageId));
            if (!string.IsNullOrWhiteSpace(partId)) builder.Append(" | PartId=").Append(Logs.SanitizarMensaje(partId));
            return builder.ToString();
        }
    }
}
