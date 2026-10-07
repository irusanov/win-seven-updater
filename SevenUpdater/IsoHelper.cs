using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SevenUpdater
{
    internal static class IsoHelper
    {
        /// <summary>Volume labels oscdimg accepts without quoting problems.</summary>
        public static readonly Regex ValidLabel = new Regex(@"^[A-Za-z0-9_\-]{1,32}$");

        public static Task ExtractIsoAsync(string isoPath, string destinationPath, CancellationToken cancellationToken)
        {
            // DiscUtils streams are synchronous; run on a worker thread so the UI stays responsive.
            return Task.Run(() => ExtractIso(isoPath, destinationPath, cancellationToken), cancellationToken);
        }

        private static void ExtractIso(string isoPath, string destinationPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(isoPath))
            {
                throw new FileNotFoundException($"ISO file not found: {isoPath}", isoPath);
            }

            Logger.Log($"Extracting ISO: {isoPath} to {destinationPath}");

            // Start clean so files from an earlier run (e.g. an already replaced install.wim) cannot survive.
            FileUtils.DeleteDirectory(destinationPath);
            Directory.CreateDirectory(destinationPath);

            using (FileStream isoStream = File.OpenRead(isoPath))
            using (DiscFileSystem fileSystem = OpenFileSystem(isoStream))
            {
                foreach (string directory in fileSystem.GetDirectories(string.Empty, "*.*", SearchOption.AllDirectories))
                {
                    Directory.CreateDirectory(Path.Combine(destinationPath, directory.TrimStart('\\')));
                }

                string[] files = fileSystem.GetFiles(string.Empty, "*.*", SearchOption.AllDirectories);
                long totalBytes = files.Sum(f => fileSystem.GetFileLength(f));
                long copiedBytes = 0;
                int lastPercent = -1;
                byte[] buffer = new byte[1024 * 1024];

                Logger.Log($"{files.Length} files, {totalBytes / (1024 * 1024)} MB");

                foreach (string file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string outputPath = Path.Combine(destinationPath, file.TrimStart('\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    using (Stream source = fileSystem.OpenFile(file, FileMode.Open, FileAccess.Read))
                    using (Stream target = File.Create(outputPath))
                    {
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            target.Write(buffer, 0, read);
                            copiedBytes += read;

                            int percent = totalBytes > 0 ? (int)(copiedBytes * 100 / totalBytes) : 100;
                            if (percent != lastPercent)
                            {
                                lastPercent = percent;
                                Logger.ReportProgress(percent);
                            }
                        }
                    }
                }
            }

            Logger.Log("ISO extraction completed");
        }

        private static DiscFileSystem OpenFileSystem(Stream isoStream)
        {
            if (UdfReader.Detect(isoStream))
            {
                isoStream.Position = 0;
                return new UdfReader(isoStream);
            }

            isoStream.Position = 0;
            if (CDReader.Detect(isoStream))
            {
                isoStream.Position = 0;
                return new CDReader(isoStream, true);
            }

            throw new InvalidDataException("The file is not a readable ISO image (no UDF or ISO 9660 file system found).");
        }

        public static async Task CreateIsoWithOscdimgAsync(string sourceDirectory, string outputIsoPath, string label, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
            }

            if (!File.Exists(AppPaths.OscdimgExe))
            {
                throw new FileNotFoundException($"oscdimg.exe not found: {AppPaths.OscdimgExe}", AppPaths.OscdimgExe);
            }

            string biosBoot = Path.Combine(sourceDirectory, "boot", "etfsboot.com");
            string uefiBoot = Path.Combine(sourceDirectory, "efi", "microsoft", "boot", "efisys.bin");
            if (!File.Exists(biosBoot) || !File.Exists(uefiBoot))
            {
                throw new FileNotFoundException("Boot files (boot\\etfsboot.com, efi\\microsoft\\boot\\efisys.bin) are missing. Is the second ISO a Windows 10 installation ISO?");
            }

            if (!ValidLabel.IsMatch(label ?? string.Empty))
            {
                throw new ArgumentException($"Invalid ISO label '{label}'. Use up to 32 letters, digits, '_' or '-'.");
            }

            string outputDirectory = Path.GetDirectoryName(outputIsoPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            FileUtils.DeleteFile(outputIsoPath);

            Logger.Log($"Creating ISO: {outputIsoPath}");

            string arguments = $"-bootdata:2#p0,e,b\"{biosBoot}\"#pEF,e,b\"{uefiBoot}\" " +
                               $"-o -m -u2 -udfver102 -l{label} \"{sourceDirectory}\" \"{outputIsoPath}\"";

            try
            {
                // oscdimg writes its progress to stderr; reading both streams asynchronously avoids the
                // deadlock the old ReadToEnd(stdout)-then-ReadToEnd(stderr) sequence could run into.
                await ProcessRunner.RunCheckedAsync(
                    AppPaths.OscdimgExe,
                    arguments,
                    "ISO creation (oscdimg)",
                    cancellationToken,
                    line => Logger.LogTool("[OSCDIMG]", line)).ConfigureAwait(false);
            }
            catch
            {
                FileUtils.TryDeleteFile(outputIsoPath);
                throw;
            }

            Logger.Log("ISO creation completed");
        }
    }
}
