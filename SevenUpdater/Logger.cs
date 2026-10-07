using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SevenUpdater
{
    public enum LogLevel
    {
        Info,
        Step,
        Success,
        Warning,
        Error
    }

    /// <summary>One log line, as shown in the log list.</summary>
    public sealed class LogEntry
    {
        public LogEntry(DateTime time, string message, LogLevel level)
        {
            Time = time;
            Message = message;
            Level = level;
        }

        public DateTime Time { get; }
        public string Message { get; }
        public LogLevel Level { get; }

        public string TimeText => Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>Message without the [ERROR]/[WARNING] prefix (the icon shows that).</summary>
        public string DisplayMessage
        {
            get
            {
                foreach (string prefix in new[] { "[ERROR] ", "[WARNING] " })
                {
                    if (Message.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return Message.Substring(prefix.Length);
                    }
                }
                return Message;
            }
        }

        /// <summary>Full line as written to output.log.</summary>
        public string Text => $"[{Time:yyyy-MM-dd HH:mm:ss}] {Message}";

        public override string ToString() => Text;
    }

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

        private static readonly Regex ErrorPattern = new Regex(@"\b(error|errors|failed|failure)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NoErrorsPattern = new Regex(@"\b(0|no) errors?\b|\berrors?\s*:\s*0\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WarningPattern = new Regex(@"\bwarnings?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SuccessPattern = new Regex(@"^(Completed|Finished):|\bcompleted(\s+successfully)?\.?$|\bsuccessfully\b|^Everything is Ok", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Raised for every logged line. May be raised from any thread.</summary>
        public static event Action<LogEntry> MessageLogged;

        /// <summary>Raised with 0-100, or null when progress is unknown. May be raised from any thread.</summary>
        public static event Action<double?> ProgressChanged;

        public static void Log(string message) => Log(message, null);

        /// <summary>Logs a message; when <paramref name="level"/> is null it is derived from the text.</summary>
        public static void Log(string message, LogLevel? level)
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

            var entry = new LogEntry(DateTime.Now, message, level ?? Classify(message));
            WriteToFile(entry.Text);
            MessageLogged?.Invoke(entry);
        }

        /// <summary>Picks an icon level for lines that were not logged with an explicit one (e.g. tool output).</summary>
        public static LogLevel Classify(string message)
        {
            if (message.StartsWith("[ERROR]", StringComparison.Ordinal))
            {
                return LogLevel.Error;
            }
            if (message.StartsWith("[WARNING]", StringComparison.Ordinal))
            {
                return LogLevel.Warning;
            }
            if (message.StartsWith("--- Step", StringComparison.Ordinal))
            {
                return LogLevel.Step;
            }
            if (ErrorPattern.IsMatch(message) && !NoErrorsPattern.IsMatch(message))
            {
                return LogLevel.Error;
            }
            if (WarningPattern.IsMatch(message))
            {
                return LogLevel.Warning;
            }

            // Tool prefixes like "[DISM] " should not hide "The operation completed successfully."
            string text = message;
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                int end = text.IndexOf("] ", StringComparison.Ordinal);
                if (end > 0)
                {
                    text = text.Substring(end + 2);
                }
            }
            return SuccessPattern.IsMatch(text.Trim()) ? LogLevel.Success : LogLevel.Info;
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

        public static void Warn(string message) => Log("[WARNING] " + message, LogLevel.Warning);

        public static void Error(string message) => Log("[ERROR] " + message, LogLevel.Error);

        public static void Success(string message) => Log(message, LogLevel.Success);

        public static void Step(string message) => Log(message, LogLevel.Step);

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
