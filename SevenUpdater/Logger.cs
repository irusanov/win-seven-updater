using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SevenUpdater
{
    /// <summary>
    /// Central, thread-safe logger. Replaces the three separate SetLogAction copies.
    /// Progress lines from DISM/oscdimg are turned into progress events instead of log spam.
    /// </summary>
    internal static class Logger
    {
        private const long MaxLogFileBytes = 5L * 1024 * 1024;

        private static readonly object FileLock = new object();

        // DISM:    [=====                      10.0%                          ]
        private static readonly Regex DismProgress = new Regex(@"^\s*\[[=\s]*(\d{1,3}(?:[.,]\d+)?)%[=\s]*\]\s*$", RegexOptions.Compiled);
        // oscdimg: 45% complete
        private static readonly Regex OscdimgProgress = new Regex(@"^\s*(\d{1,3}(?:[.,]\d+)?)%\s+complete\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Raised with a timestamped line. May be raised from any thread.</summary>
        public static event Action<string> MessageLogged;

        /// <summary>Raised with 0-100, or null when progress is unknown. May be raised from any thread.</summary>
        public static event Action<double?> ProgressChanged;

        public static void Log(string message)
        {
            if (message == null)
            {
                return;
            }

            message = message.TrimEnd('\r', '\n');
            if (message.Length == 0)
            {
                return;
            }

            double percent;
            if (TryParseProgress(message, out percent))
            {
                ReportProgress(percent);
                return;
            }

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            WriteToFile(line);
            MessageLogged?.Invoke(line);
        }

        /// <summary>Logs a line of tool output with a prefix; progress lines are turned into progress events.</summary>
        public static void LogTool(string prefix, string line)
        {
            double percent;
            if (line != null && TryParseProgress(line, out percent))
            {
                ReportProgress(percent);
                return;
            }
            Log(prefix + " " + (line ?? string.Empty).Trim());
        }

        public static void Warn(string message) => Log("[WARNING] " + message);

        public static void Error(string message) => Log("[ERROR] " + message);

        public static void ReportProgress(double? percent)
        {
            if (percent.HasValue)
            {
                percent = Math.Max(0, Math.Min(100, percent.Value));
            }
            ProgressChanged?.Invoke(percent);
        }

        public static bool TryParseProgress(string line, out double percent)
        {
            percent = 0;
            Match match = DismProgress.Match(line);
            if (!match.Success)
            {
                match = OscdimgProgress.Match(line);
            }
            if (!match.Success)
            {
                return false;
            }

            string value = match.Groups[1].Value.Replace(',', '.');
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
        }

        /// <summary>Message of an exception including inner exceptions, for user-facing errors.</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            string text = ex.Message;
            Exception inner = ex.InnerException;
            while (inner != null)
            {
                if (!text.Contains(inner.Message))
                {
                    text += Environment.NewLine + inner.Message;
                }
                inner = inner.InnerException;
            }
            return text;
        }

        private static void WriteToFile(string line)
        {
            // The log used to live inside the working directory, so "Clean" deleted it mid-session
            // and a failed write could throw into the middle of a DISM operation.
            try
            {
                lock (FileLock)
                {
                    string path = AppPaths.LogFile;
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxLogFileBytes)
                    {
                        string old = Path.ChangeExtension(path, ".old.log");
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never break the actual work.
            }
        }
    }
}
