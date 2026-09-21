using System;
using System.Diagnostics;
using System.IO;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    public sealed class GmailManualSyncLaunchResult
    {
        public bool Started { get; set; }
        public string Message { get; set; }
    }

    public static class GmailManualSyncLauncher
    {
        public static GmailManualSyncLaunchResult Start()
        {
            var runnerPath = ConfiguracionSistema.Actual.RunnerPath;
            if (string.IsNullOrWhiteSpace(runnerPath))
                return Failure("No está configurado Sync/RunnerPath en RecepcionDocumental.ini.");
            if (!File.Exists(runnerPath))
                return Failure("No se encontró el ejecutable configurado en Sync/RunnerPath.");

            int processId;
            try
            {
                var productRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                var startInfo = new ProcessStartInfo
                {
                    FileName = runnerPath,
                    Arguments = "\"" + productRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "\" --sync-web",
                    WorkingDirectory = productRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                using (var process = Process.Start(startInfo))
                {
                    if (process == null) throw new InvalidOperationException("No se pudo crear el proceso de sincronización.");
                    processId = process.Id;
                }
            }
            catch (Exception ex)
            {
                try { Logs.LogError("GmailManualSync | Estado=ERROR | Tipo=" + ex.GetType().Name); } catch { }
                return Failure("No fue posible iniciar el proceso externo de sincronización.");
            }
            try { Logs.LogProc("GmailManualSync | Estado=INICIADA | ProcessId=" + processId); } catch { }
            return new GmailManualSyncLaunchResult
            {
                Started = true,
                Message = "Búsqueda iniciada. El procesamiento continúa en segundo plano."
            };
        }

        private static GmailManualSyncLaunchResult Failure(string message)
        {
            return new GmailManualSyncLaunchResult { Started = false, Message = message };
        }
    }
}
