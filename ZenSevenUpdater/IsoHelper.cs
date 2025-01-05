using DiscUtils.Iso9660;
using DiscUtils.Udf;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SevenUpdater
{
    internal static class IsoHelper
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

        public static async Task ExtractIsoAsync(string isoPath, string destinationPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(isoPath))
            {
                throw new FileNotFoundException("ISO file not found", isoPath);
            }

            Directory.CreateDirectory(destinationPath);

            Log($"Extracting ISO file: {isoPath} to {destinationPath}");

            using (FileStream isoStream = File.OpenRead(isoPath))
            {
                UdfReader udf = new UdfReader(isoStream);
                foreach (var file in udf.GetFiles(string.Empty, "*.*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string outputPath = Path.Combine(destinationPath, file.TrimStart('\\'));
                    string outputDirectory = Path.GetDirectoryName(outputPath);
                    Directory.CreateDirectory(outputDirectory);
                    using (Stream fileStream = File.Create(outputPath))
                    using (Stream udfFileStream = udf.OpenFile(file, FileMode.Open))
                    {
                        await udfFileStream.CopyToAsync(fileStream);
                    }
                }
            }

            Log("ISO extraction completed successfully.");
        }

        public static async Task CreateBootableIsoFromDirectoryAsync(string sourceDirectory, string outputIsoPath, string label, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
            }

            Log($"Creating bootable ISO from directory: {sourceDirectory} to {outputIsoPath}");

            using (var isoStream = File.Create(outputIsoPath))
            {
                CDBuilder builder = new CDBuilder
                {
                    UseJoliet = true,
                    VolumeIdentifier = $@"{label}"
                };

                await AddFilesToIsoAsync(builder, sourceDirectory, string.Empty, cancellationToken);
                builder.Build(isoStream);
            }

            Log("Bootable ISO creation completed.");
        }

        private static async Task AddFilesToIsoAsync(CDBuilder builder, string sourcePath, string targetPath, CancellationToken cancellationToken)
        {
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(file);
                builder.AddFile(Path.Combine(targetPath, fileName), file);
            }

            foreach (var directory in Directory.GetDirectories(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string dirName = Path.GetFileName(directory);
                await AddFilesToIsoAsync(builder, directory, Path.Combine(targetPath, dirName), cancellationToken);
            }
        }
    }
}
