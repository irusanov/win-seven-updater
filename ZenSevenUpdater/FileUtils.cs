using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using static ZenSevenUpdater.DismHelper;

namespace ZenSevenUpdater
{
    internal static class FileUtils
    {
        public static string CalculateChecksum(string filePath, ChecksumAlgorithm algorithm)
        {
            string algorithmName = algorithm.ToString();
            Log($"Calculating {algorithmName} checksum for file: {filePath}");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "certutil.exe",
                Arguments = $"-hashfile \"{filePath}\" {algorithmName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            string checksum = string.Empty;
            using (Process process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log(e.Data);
                        if (string.IsNullOrEmpty(checksum) && !e.Data.Contains("certutil") && !e.Data.StartsWith("\""))
                        {
                            checksum = e.Data;
                        }
                    }
                };
                process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[CERTUTIL ERROR]: {e.Data}"); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Checksum calculation failed with exit code {process.ExitCode}");
                }
            }
            Log("Checksum calculation completed\n");
            return checksum;
        }

        public static bool CheckDiskSpace(string path, long requiredBytes)
        {
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(path));
            return drive.AvailableFreeSpace >= requiredBytes;
        }

        public static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        public static bool IsDirectoryEmpty(string path)
        {
            return Directory.GetFileSystemEntries(path).Length == 0;
        }

        public static async Task CleanupWorkingDirectory(string path)
        {
            await Task.Run(() =>
            {

                DeleteDirectory(path);
            });
        }
    }
}
