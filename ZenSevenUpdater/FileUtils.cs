using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SevenUpdater.DismHelper;

namespace SevenUpdater
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
                CreateNoWindow = true,
                Verb = "runas"
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
                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to delete file: {filePath}", ex);
                }
            }
            Log("File deletion completed\n");
        }

        public static async Task CopyFileAsync(string sourceFilePath, string basePath, string folderNameStart)
        {
            var folder = FindFolder(basePath, folderNameStart);
            var path = Path.Combine(folder, Path.GetFileName(sourceFilePath));

            await CopyFileAsync(sourceFilePath, path);
        }

        public static async Task CopyFileAsync(string sourceFilePath, string destinationFilePath)
        {
            Log($"Copying file: {sourceFilePath} to {destinationFilePath}");

            try
            {
                string destinationDirectory = Path.GetDirectoryName(destinationFilePath);
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    Log($"Created directory: {destinationDirectory}");
                }

                await Task.Run(() => File.Copy(sourceFilePath, destinationFilePath, overwrite: true));

                Log("File copy completed successfully.\n");
            }
            catch (UnauthorizedAccessException)
            {
                Log("[ERROR]: Access denied. Ensure the application has appropriate permissions.");
                throw;
            }
            catch (Exception ex)
            {
                Log($"[ERROR]: An error occurred during file copy: {ex.Message}");
                throw;
            }
        }

        public static async Task CopyFileToProtectedFolderAsync(string sourceFilePath, string basePath, string folderNameStart)
        {
            var folder = FindFolder(basePath, folderNameStart);
            var path = Path.Combine(folder, Path.GetFileName(sourceFilePath));

            await CopyFileToProtectedFolderAsync(sourceFilePath, path);
        }

        public static async Task CopyFileToProtectedFolderAsync(string sourceFilePath, string destinationFilePath)
        {
            Log($"Copying file: {sourceFilePath} to {destinationFilePath}");

            try
            {
                if (!File.Exists(sourceFilePath))
                    throw new FileNotFoundException("Source file does not exist.", sourceFilePath);

                string destinationDirectory = Path.GetDirectoryName(destinationFilePath);
                if (string.IsNullOrWhiteSpace(destinationDirectory))
                    throw new ArgumentException("Invalid destination file path.", nameof(destinationFilePath));

                await Task.Run(() => TakeOwnership(destinationDirectory));
                await Task.Run(() => TakeOwnership(destinationFilePath));

                File.Copy(sourceFilePath, destinationFilePath, overwrite: true);

                Log("File copy completed successfully.\n");
            }
            catch (UnauthorizedAccessException)
            {
                Log("[ERROR]: Access denied. Ensure the application is running with administrative privileges.");
                throw;
            }
            catch (Exception ex)
            {
                Log($"[ERROR]: An error occurred: {ex.Message}");
                throw;
            }
        }

        public static void TakeOwnership(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    Console.WriteLine("Invalid file path.");
                    return;
                }

                string takeownCommand = $"/c takeown /f \"{filePath}\" /r /d y";
                ExecuteCommand("cmd.exe", takeownCommand);

                string icaclsCommand = $"/c icacls \"{filePath}\" /grant *S-1-3-4:F /t /c /l /q";
                ExecuteCommand("cmd.exe", icaclsCommand);

                Console.WriteLine("Ownership successfully taken.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
            }
        }

        private static void ExecuteCommand(string command, string arguments)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "runas",
                CreateNoWindow = true
            };

            using (Process process = Process.Start(processStartInfo))
            {
                process?.WaitForExit();
            }
        }

        public static string FindFolder(string basePath, string folderStart)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));

            if (string.IsNullOrWhiteSpace(folderStart))
                throw new ArgumentException("Folder start string cannot be null or empty.", nameof(folderStart));

            if (!Directory.Exists(basePath))
                throw new DirectoryNotFoundException($"The directory '{basePath}' does not exist.");

            try
            {
                var directories = Directory.GetDirectories(basePath);

                var matchingFolder = directories
                    .FirstOrDefault(dir => Path.GetFileName(dir).StartsWith(folderStart, StringComparison.OrdinalIgnoreCase));

                return matchingFolder;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while searching for the folder: {ex.Message}");
                return null;
            }
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

        public static async Task ExtractArchiveAsync(string archivePath, string destinationPath, CancellationToken cancellationToken)
        {
            Log($"Extracting archive: {archivePath} to {destinationPath}");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "7za.exe",
                Arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[7ZIP ERROR]: {e.Data}"); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() =>
                {
                    while (!process.HasExited)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            process.Kill();
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        Thread.Sleep(100);
                    }
                }, cancellationToken);

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Extraction failed with exit code {process.ExitCode}");
                }
            }
            Log("Extraction completed\n");
        }

        public static async Task RenameFileAsync(string sourceFilePath, string destinationFilePath)
        {
            Log($"Renaming file: {sourceFilePath} to {destinationFilePath}");
            if (File.Exists(sourceFilePath))
            {
                try
                {
                    await Task.Run(() => File.Move(sourceFilePath, destinationFilePath));
                    Log("File rename completed successfully.\n");
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to rename file: {sourceFilePath} to {destinationFilePath}", ex);
                }
            }
            else
            {
                throw new FileNotFoundException("Source file does not exist.", sourceFilePath);
            }
        }

        public static async Task MoveFileAsync(string sourceFilePath, string destinationFilePath)
        {
            Log($"Moving file: {sourceFilePath} to {destinationFilePath}");
            if (File.Exists(sourceFilePath))
            {
                try
                {
                    await Task.Run(() => File.Move(sourceFilePath, destinationFilePath));
                    Log("File move completed successfully.\n");
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to move file: {sourceFilePath} to {destinationFilePath}", ex);
                }
            }
            else
            {
                throw new FileNotFoundException("Source file does not exist.", sourceFilePath);
            }
        }
    }
}
