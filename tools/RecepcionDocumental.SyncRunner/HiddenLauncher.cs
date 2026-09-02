using System;
using System.Diagnostics;
using System.IO;

// GUI-subsystem entry point: Task Scheduler never allocates a console window.
internal static class HiddenLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || args.Length > 2) return 1;
            string root = Path.GetFullPath(args[0]);
            string mode = args.Length == 2 ? args[1] : "--sync";
            if (mode != "--sync" && mode != "--verify-config" &&
                mode != "--probe-lock" && mode != "--probe-lock-hold") return 1;
            string runner = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RecepcionDocumental.SyncRunner.exe");
            var start = new ProcessStartInfo(runner, "\"" + root.TrimEnd(Path.DirectorySeparatorChar) + "\" " + mode)
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var child = new Process { StartInfo = start })
            {
                // Drain both streams asynchronously; operational logging remains in SyncRunner.
                child.OutputDataReceived += (sender, e) => { };
                child.ErrorDataReceived += (sender, e) => { };
                if (!child.Start()) return 1;
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                child.WaitForExit();
                return child.ExitCode;
            }
        }
        catch { return 1; }
    }
}
