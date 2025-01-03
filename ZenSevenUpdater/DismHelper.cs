using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZenSevenUpdater
{
    internal class DismHelper
    {
        public enum ChecksumAlgorithm
        {
            MD2,
            MD4,
            MD5,
            SHA1,
            SHA256,
            SHA384,
            SHA512
        }

        private static Action<string> _logAction;

        public static void SetLogAction(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public static void Log(string message)
        {
            _logAction?.Invoke(message);
        }

        private static void ExecuteDismCommand(string arguments, string description)
        {
            Log($"Starting: {description}");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) => LogOutput(e.Data, "[DISM]");
                process.ErrorDataReceived += (sender, e) => LogOutput(e.Data, "[DISM ERROR]");

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"DISM command failed with exit code {process.ExitCode}: {arguments}");
                }
            }
            Log($"Completed: {description}\n");
        }

        private static void LogOutput(string data, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(data))
            {
                Log($"{prefix}: {data}");
            }
        }

        public static List<(string Index, string Name, string Description)> GetWimInfo(string wimFilePath)
        {
            Log($"Retrieving WIM info for: {wimFilePath}");
            var imageInfoList = new List<(string Index, string Name, string Description)>();

            var startInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = $"/Get-WimInfo /WimFile:\"{wimFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log(e.Data);
                        ParseWimInfo(e.Data, imageInfoList);
                    }
                };

                process.ErrorDataReceived += (sender, e) => LogOutput(e.Data, "[DISM ERROR]");

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Failed to retrieve WIM info. Exit code: {process.ExitCode}");
                }
            }

            Log($"Retrieved {imageInfoList.Count} images from WIM file.");
            return imageInfoList;
        }

        private static void ParseWimInfo(string data, List<(string Index, string Name, string Description)> imageInfoList)
        {
            string index = ExtractValue(data, "Index :");
            string name = ExtractValue(data, "Name :");
            string description = ExtractValue(data, "Description :");

            if (!string.IsNullOrEmpty(index))
            {
                imageInfoList.Add((index, name, description));
            }
        }

        public static void MountImage(string imagePath, string mountPath, string index)
        {
            if (!Directory.Exists(mountPath))
            {
                Directory.CreateDirectory(mountPath);
            }
            string arguments = $"/Mount-Image /ImageFile:\"{imagePath}\" /Index:{index} /MountDir:\"{mountPath}\"";
            ExecuteDismCommand(arguments, "Mount Image");
        }

        public static void UnmountImage(string mountPath, bool commitChanges)
        {
            string commitOption = commitChanges ? "/Commit" : "/Discard";
            string arguments = $"/Unmount-Image /MountDir:\"{mountPath}\" {commitOption}";
            ExecuteDismCommand(arguments, "Unmount Image");
        }

        public static void DeleteImage(string imagePath, int index)
        {
            string arguments = $"/Delete-Image /ImageFile:\"{imagePath}\" /Index:{index} /CheckIntegrity";
            ExecuteDismCommand(arguments, "Delete Image");
        }

        public static void AddDriver(string imagePath, string driverPath, bool recurse = false)
        {
            string recurseOption = recurse ? "/Recurse" : string.Empty;
            string arguments = $"/Add-Driver /Image:\"{imagePath}\" /Driver:\"{driverPath}\" {recurseOption}";
            ExecuteDismCommand(arguments, "Add Driver");
        }

        public static async Task MountImageAsync(string imagePath, string mountPath, string index, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    MountImage(imagePath, mountPath, index);
                }
            }, cancellationToken);
        }

        public static async Task UnmountImageAsync(string mountPath, bool commitChanges, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    UnmountImage(mountPath, commitChanges);
                }
            }, cancellationToken);
        }

        public static async Task DeleteImageAsync(string imagePath, int index, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    DeleteImage(imagePath, index);
                }
            }, cancellationToken);
        }

        public static async Task AddDriverAsync(string imagePath, string driverPath, bool recurse, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    AddDriver(imagePath, driverPath, recurse);
                }
            }, cancellationToken);
        }

        public static async Task<List<(string Index, string Name, string Description)>> GetWimInfoAsync(string wimFilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested) return null;
                return GetWimInfo(wimFilePath);
            }, cancellationToken);
        }

        private static string ExtractValue(string input, string key)
        {
            if (input.StartsWith(key))
            {
                return input.Substring(key.Length).Trim();
            }
            return null;
        }
    }
}
