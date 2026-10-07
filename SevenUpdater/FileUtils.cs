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
        private const string AdministratorsSid = "*S-1-5-32-544";

        public static string CalculateChecksum(string filePath, ChecksumAlgorithm algorithm)
        {
            string algorithmName = algorithm.ToString();
            Logger.Log($"Calculating {algorithmName} checksum for file: {filePath}");

            string checksum = string.Empty;
            int exitCode = ProcessRunner.RunAsync(
                "certutil.exe",
                $"-hashfile \"{filePath}\" {algorithmName}",
                CancellationToken.None,
                line =>
                {
                    Logger.Log(line);
                    if (string.IsNullOrEmpty(checksum) && !line.Contains("certutil") && !line.Contains(":"))
                    {
                        checksum = line.Replace(" ", string.Empty).Trim();
                    }
                }).GetAwaiter().GetResult();

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Checksum calculation failed with exit code {exitCode}");
            }

            Logger.Log("Checksum calculation completed");
            return checksum;
        }

        public static bool CheckDiskSpace(string path, long requiredBytes)
        {
            long available = GetAvailableFreeSpace(path);
            return available < 0 || available >= requiredBytes;
        }

        /// <summary>Free bytes on the drive of <paramref name="path"/>, or -1 when it cannot be determined.</summary>
        public static long GetAvailableFreeSpace(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root))
                {
                    return -1;
                }
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Deletes a directory tree, including files marked read-only (Directory.Delete fails on those).</summary>
        public static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch
                {
                    // Directory.Delete below reports the real problem.
                }
            }

            Directory.Delete(path, true);
        }

        public static void DeleteFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
            }
        }

        public static Task DeleteFileAsync(string filePath)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                Logger.Log($"Deleting file: {filePath}");
                try
                {
                    DeleteFile(filePath);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to delete file: {filePath}", ex);
                }
            });
        }

        public static async Task CopyFileAsync(string sourceFilePath, string destinationDirectory, CancellationToken cancellationToken)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException($"File not found: {sourceFilePath}", sourceFilePath);
            }

            Directory.CreateDirectory(destinationDirectory);
            string destinationFilePath = Path.Combine(destinationDirectory, Path.GetFileName(sourceFilePath));
            Logger.Log($"Copying {sourceFilePath} to {destinationFilePath}");

            if (File.Exists(destinationFilePath))
            {
                DeleteFile(destinationFilePath);
            }

            long total = new FileInfo(sourceFilePath).Length;
            long copied = 0;
            int lastPercent = -1;
            byte[] buffer = new byte[1024 * 1024];

            try
            {
                using (var source = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                using (var destination = new FileStream(destinationFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        copied += read;
                        int percent = total > 0 ? (int)(copied * 100 / total) : 100;
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            Logger.ReportProgress(percent);
                        }
                    }
                }
            }
            catch
            {
                TryDeleteFile(destinationFilePath);
                throw;
            }

            Logger.Log("Copy completed");
        }

        /// <summary>
        /// Replaces a file inside the mounted offline image that is owned by TrustedInstaller.
        /// Ownership is taken only for this single file (the previous recursive "takeown /r /d y"
        /// failed on non-English Windows, where the confirmation letter is localized) and the
        /// original owner and permissions are restored afterwards.
        /// </summary>
        public static async Task CopyFileToProtectedFolderAsync(string sourceFilePath, string basePath, string folderNameStart, CancellationToken cancellationToken)
        {
            string folder = FindFolder(basePath, folderNameStart);
            if (folder == null)
            {
                throw new DirectoryNotFoundException($"No folder starting with '{folderNameStart}' was found in {basePath}.");
            }

            await CopyFileToProtectedFolderAsync(sourceFilePath, Path.Combine(folder, Path.GetFileName(sourceFilePath)), cancellationToken).ConfigureAwait(false);
        }

        public static async Task CopyFileToProtectedFolderAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken)
        {
            Logger.Log($"Replacing protected file: {destinationFilePath}");

            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException($"Source file does not exist: {sourceFilePath}", sourceFilePath);
            }

            string destinationDirectory = Path.GetDirectoryName(destinationFilePath);
            if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            {
                throw new DirectoryNotFoundException($"Destination folder does not exist: {destinationDirectory}");
            }

            bool existed = File.Exists(destinationFilePath);
            string target = existed ? destinationFilePath : destinationDirectory;

            await TakeOwnershipAsync(target, cancellationToken).ConfigureAwait(false);

            try
            {
                File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"Access denied while replacing {destinationFilePath}. Make sure the app runs as administrator.", ex);
            }

            await RestoreTrustedInstallerOwnershipAsync(destinationFilePath, cancellationToken).ConfigureAwait(false);
            if (!existed)
            {
                await RestoreTrustedInstallerOwnershipAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);
            }

            Logger.Log("Protected file replaced");
        }

        private static async Task TakeOwnershipAsync(string path, CancellationToken cancellationToken)
        {
            await ProcessRunner.RunCheckedAsync("takeown.exe", $"/F \"{path}\" /A", "Taking ownership of " + path, cancellationToken).ConfigureAwait(false);
            await ProcessRunner.RunCheckedAsync("icacls.exe", $"\"{path}\" /grant {AdministratorsSid}:F /C /Q", "Granting access to " + path, cancellationToken).ConfigureAwait(false);
        }

        private static async Task RestoreTrustedInstallerOwnershipAsync(string path, CancellationToken cancellationToken)
        {
            // Not fatal: the image works either way, it is only about leaving the ACLs as Windows expects them.
            int exitCode = await ProcessRunner.RunAsync("icacls.exe", $"\"{path}\" /setowner \"NT SERVICE\\TrustedInstaller\" /C /Q", cancellationToken).ConfigureAwait(false);
            exitCode |= await ProcessRunner.RunAsync("icacls.exe", $"\"{path}\" /grant:r {AdministratorsSid}:RX /C /Q", cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                Logger.Warn($"Could not fully restore the original permissions of {path}.");
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

            return Directory.GetDirectories(basePath)
                .FirstOrDefault(dir => Path.GetFileName(dir).StartsWith(folderStart, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsDirectoryEmpty(string path)
        {
            try
            {
                return !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();
            }
            catch (UnauthorizedAccessException)
            {
                // A mounted image root can deny listing; treat it as "in use".
                return false;
            }
        }

        public static string[] GetArchiveFiles(string path)
        {
            if (!Directory.Exists(path))
            {
                return new string[0];
            }

            string[] extensions = { ".zip", ".rar", ".7z", ".7zip" };
            return Directory.GetFiles(path)
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static async Task ExtractArchiveAsync(string archivePath, string destinationPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
            }

            if (!File.Exists(AppPaths.SevenZipExe))
            {
                throw new FileNotFoundException($"7-Zip not found: {AppPaths.SevenZipExe}", AppPaths.SevenZipExe);
            }

            Logger.Log($"Extracting archive: {archivePath} to {destinationPath}");

            // Start from an empty folder so files from a previously selected driver pack do not get mixed in.
            DeleteDirectory(destinationPath);
            Directory.CreateDirectory(destinationPath);

            int exitCode = await ProcessRunner.RunAsync(
                AppPaths.SevenZipExe,
                $"x \"{archivePath}\" -o\"{destinationPath}\" -y -bd",
                cancellationToken,
                line => Logger.LogTool("[7-Zip]", line)).ConfigureAwait(false);

            // 7-Zip: 0 = OK, 1 = warning (non fatal), 2+ = error.
            if (exitCode == 1)
            {
                Logger.Warn("7-Zip reported warnings while extracting.");
            }
            else if (exitCode != 0)
            {
                throw new InvalidOperationException($"Extraction of {Path.GetFileName(archivePath)} failed (7-Zip exit code {exitCode}).");
            }

            Logger.Log("Extraction completed");
        }

        public static Task MoveFileAsync(string sourceFilePath, string destinationFilePath)
        {
            return Task.Run(() =>
            {
                Logger.Log($"Moving file: {sourceFilePath} to {destinationFilePath}");
                if (!File.Exists(sourceFilePath))
                {
                    throw new FileNotFoundException("Source file does not exist.", sourceFilePath);
                }

                try
                {
                    DeleteFile(destinationFilePath);
                    File.Move(sourceFilePath, destinationFilePath);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to move file: {sourceFilePath} to {destinationFilePath}", ex);
                }
            });
        }

        public static void TryDeleteFile(string path)
        {
            try
            {
                DeleteFile(path);
            }
            catch
            {
                // Best effort.
            }
        }

        public static void OpenFolderAndSelect(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                else
                {
                    string folder = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        Process.Start("explorer.exe", $"\"{folder}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open Explorer: " + ex.Message);
            }
        }
    }
}
