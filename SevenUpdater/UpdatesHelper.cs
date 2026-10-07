using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SevenUpdater
{
    public static class UpdatesHelper
    {
        private const string UpdatePackPattern = "UpdatePack7R2-*.exe";
        private static readonly Regex UpdatePackVersion = new Regex(@"^UpdatePack7R2-(\d+(?:\.\d+)+)\.exe$", RegexOptions.IgnoreCase);

        public static string[] GetUpdatePackFiles()
        {
            if (!Directory.Exists(AppPaths.UpdatesDirectory))
            {
                return new string[0];
            }
            return Directory.GetFiles(AppPaths.UpdatesDirectory, UpdatePackPattern);
        }

        /// <summary>
        /// Newest UpdatePack7R2-x.y.z.exe by version number. Sorting by name picked the wrong file
        /// as soon as a month had two digits (26.9.x sorts after 26.10.x).
        /// </summary>
        public static string FindLatestUpdatePack()
        {
            return GetUpdatePackFiles()
                .Select(path => new { Path = path, Version = ParseVersion(path) })
                .OrderBy(x => x.Version)
                .ThenBy(x => File.GetLastWriteTimeUtc(x.Path))
                .Select(x => x.Path)
                .LastOrDefault();
        }

        private static Version ParseVersion(string path)
        {
            Match match = UpdatePackVersion.Match(Path.GetFileName(path));
            Version version;
            if (match.Success && Version.TryParse(match.Groups[1].Value, out version))
            {
                return version;
            }
            return new Version(0, 0);
        }

        /// <summary>Runs UpdatePack7R2+.exe, which downloads the latest UpdatePack7R2 into the updates folder.</summary>
        public static async Task RunUpdatePackCheckAsync(CancellationToken cancellationToken)
        {
            string checker = AppPaths.UpdatePackCheckerExe;
            if (!File.Exists(checker))
            {
                throw new FileNotFoundException($"{Path.GetFileName(checker)} not found in {AppPaths.UpdatesDirectory}.", checker);
            }

            // Remember what was there before, so cancelling only removes the partial download.
            // (Previously cancelling deleted every UpdatePack7R2-*.exe, including good 800+ MB packs.)
            var existingFiles = new HashSet<string>(Directory.GetFiles(AppPaths.UpdatesDirectory), StringComparer.OrdinalIgnoreCase);

            Logger.Log($"Starting: {Path.GetFileName(checker)}");
            try
            {
                int exitCode = await ProcessRunner.RunAsync(
                    checker,
                    string.Empty,
                    cancellationToken,
                    line => Logger.LogTool("[UpdatePack+]", line),
                    AppPaths.UpdatesDirectory).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    Logger.Warn($"{Path.GetFileName(checker)} exited with code {exitCode}. Continuing with the update packs already available.");
                }
            }
            catch (OperationCanceledException)
            {
                RemoveNewFiles(existingFiles);
                throw;
            }

            string latest = FindLatestUpdatePack();
            Logger.Log(latest == null ? "No UpdatePack7R2 is available." : $"Latest update pack: {Path.GetFileName(latest)}");
        }

        private static void RemoveNewFiles(HashSet<string> existingFiles)
        {
            // Give the killed downloader a moment to release its file handles.
            Thread.Sleep(1000);
            foreach (string file in Directory.GetFiles(AppPaths.UpdatesDirectory))
            {
                if (existingFiles.Contains(file))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    Logger.Log($"Removed incomplete download: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not remove {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        /// <summary>Integrates the newest UpdatePack7R2 into the (unmounted) WIM.</summary>
        public static async Task RunUpdatePackAsync(string wimFilePath, string tempDirectory, int index, bool optimize, CancellationToken cancellationToken)
        {
            string updatePack = FindLatestUpdatePack();
            if (updatePack == null)
            {
                throw new FileNotFoundException($"No UpdatePack7R2-*.exe found in {AppPaths.UpdatesDirectory}. Enable \"Check for UpdatePack updates\" or download it manually.");
            }

            Directory.CreateDirectory(tempDirectory);

            // Runs straight from the updates folder; copying the 800+ MB pack into the working folder first was unnecessary.
            string arguments = $"/WimFile=\"{wimFilePath}\" /Index={index} /Temp=\"{tempDirectory}\"{(optimize ? " /Optimize" : string.Empty)}";
            Logger.Log($"Starting: {Path.GetFileName(updatePack)} {arguments}");
            Logger.Log("Integrating updates usually takes 30-90 minutes. Progress is only shown when the update pack reports it.");

            int exitCode = await ProcessRunner.RunAsync(
                updatePack,
                arguments,
                cancellationToken,
                line => Logger.LogTool("[UpdatePack]", line),
                AppPaths.UpdatesDirectory).ConfigureAwait(false);

            if (exitCode != 0)
            {
                Logger.Warn($"{Path.GetFileName(updatePack)} exited with code {exitCode}. Check the log above to verify the updates were integrated.");
            }
            else
            {
                Logger.Log($"Finished: {Path.GetFileName(updatePack)}");
            }
        }
    }
}
