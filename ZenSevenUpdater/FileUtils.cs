using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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

        public static void DeleteFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public static async Task DeleteFileAsync(string filePath)
        {
            Log($"Deleting file: {filePath}");
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    await Task.CompletedTask; // Simulate async operation
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to delete file: {filePath}", ex);
                }
            }
            Log("File deletion completed\n");
        }

        public static async Task CopyFileAsync(string sourceFilePath, string destinationFilePath)
        {
            Log($"Copying file: {sourceFilePath} to {destinationFilePath}");
            using (FileStream sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }
            }
            Log("File copy completed\n");
        }

        public static bool IsDirectoryEmpty(string path)
        {
            return !Directory.Exists(path) || Directory.GetFileSystemEntries(path).Length == 0;
        }

        public static async Task CleanupWorkingDirectory(string path)
        {
            Log($"Cleaning up working directory: {path}");
            await Task.Run(() =>
            {
                DeleteDirectory(path);
            });
            Log("Cleanup completed\n");
        }
        public static string[] GetArchiveFiles(string path)
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"The directory '{path}' does not exist.");
            }

            return Directory.GetFiles(path, "*.zip")
                .Concat(Directory.GetFiles(path, "*.rar"))
                .Concat(Directory.GetFiles(path, "*.zip"))
                .Concat(Directory.GetFiles(path, "*.rar"))
                .Concat(Directory.GetFiles(path, "*.7z"))
                .Concat(Directory.GetFiles(path, "*.7zip"))
                .ToArray();
        }

        public static async Task ExtractArchiveAsync(string archivePath, string destinationPath)
        {
            Log($"Extracting archive: {archivePath} to {destinationPath}");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "7za.exe",
                Arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[7ZIP ERROR]: {e.Data}"); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit());

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Extraction failed with exit code {process.ExitCode}");
                }
            }
            Log("Extraction completed\n");
        }
    }
}
