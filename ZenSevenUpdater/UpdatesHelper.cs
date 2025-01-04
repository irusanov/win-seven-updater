

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZenSevenUpdater
{
    public static class UpdatesHelper
    {
        private static Action<string> _logAction;

        public static void SetLogAction(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public static void Log(string message)
        {
            _logAction?.Invoke(message);
        }

        private static void RemoveUpdatePackFiles(string directory)
        {
            var files = Directory.GetFiles(directory, "UpdatePack7R2-*");
            foreach (var file in files)
            {
                File.Delete(file);
                Log($"Removed file: {file}");
            }
        }

        public static void RunUpdatePackCheck(string path, CancellationToken cancellationToken)
        {
            if (File.Exists(path))
            {
                Log($"Starting: {Path.GetFileName(path)}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    while (!process.HasExited)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            process.Kill();
                            KillSFXWgetProcess();
                            RemoveUpdatePackFiles(Path.GetDirectoryName(path));
                            Log($"Process {Path.GetFileName(path)} was cancelled.");
                            return;
                        }
                    }
                }
                Log($"Finished: {Path.GetFileName(path)}");
            }
            else
            {
                throw new FileNotFoundException($"{Path.GetFileName(path)} not found.");
            }
        }

        private static void KillSFXWgetProcess()
        {
            var processes = Process.GetProcessesByName("wget");
            foreach (var proc in processes)
            {
                proc.Kill();
                Log($"Killed process: {proc.ProcessName}");
            }

            processes = Process.GetProcessesByName("SFXWget");
            foreach (var proc in processes)
            {
                proc.Kill();
                Log($"Killed process: {proc.ProcessName}");
            }
        }

        public static async Task RunUpdatePackCheckAsync(string updatePackFullPath, CancellationToken cancellationToken)
        {
            await Task.Run(() => RunUpdatePackCheck(updatePackFullPath, cancellationToken), cancellationToken);
        }

        public static void RunUpdatePack(string path, string wimFilePath, int index, bool optimize)
        {
            if (File.Exists(path))
            {
                Log($"Starting: {System.IO.Path.GetFileName(path)} with parameters.");


                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = $"/WimFile=\"{wimFilePath}\" /Index={index} {(optimize ? "/Optimize" : "")}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    process.WaitForExit();
                    Log($"Finished: {Path.GetFileName(path)}");
                }
            }
            else
            {
                throw new FileNotFoundException($"{Path.GetFileName(path)} not found.");
            }
        }

        public static async Task RunUpdatePackAsync(string path, string wimFilePath, int index, bool optimize, CancellationToken cancellationToken)
        {
            await Task.Run(() => RunUpdatePack(path, wimFilePath, index, optimize), cancellationToken);
        }
    }
}
