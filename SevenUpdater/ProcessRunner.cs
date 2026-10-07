using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SevenUpdater
{
    /// <summary>
    /// Runs console tools with both output streams read asynchronously (no pipe-buffer deadlocks),
    /// in the OEM code page (so localized output is readable), and kills the whole process tree
    /// when the operation is cancelled.
    /// </summary>
    internal static class ProcessRunner
    {
        public static Encoding OemEncoding
        {
            get
            {
                try
                {
                    return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                }
                catch
                {
                    return Encoding.Default;
                }
            }
        }

        /// <summary>
        /// Starts <paramref name="fileName"/> and waits for it to exit.
        /// Every non-empty output line is passed to <paramref name="onOutput"/> (or the logger, when null).
        /// Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        public static async Task<int> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken,
            Action<string> onOutput = null,
            string workingDirectory = null,
            string errorPrefix = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Action<string> output = onOutput ?? Logger.Log;
            Encoding encoding = OemEncoding;

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        output(e.Data);
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        output(string.IsNullOrEmpty(errorPrefix) ? e.Data : errorPrefix + " " + e.Data);
                    }
                };
                process.Exited += (s, e) => exited.TrySetResult(true);

                try
                {
                    process.Start();
                }
                catch (Win32Exception ex)
                {
                    throw new InvalidOperationException($"Could not start '{fileName}': {ex.Message}", ex);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() =>
                {
                    cancelled.TrySetResult(true);
                    KillProcessTree(process);
                }))
                {
                    await Task.WhenAny(exited.Task, cancelled.Task).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    // Do not wait for the output pipes here: an orphaned child process can keep them
                    // open for a long time. Give the killed process a moment to go away, then return.
                    await Task.WhenAny(exited.Task, Task.Delay(10000)).ConfigureAwait(false);
                    throw new OperationCanceledException(cancellationToken);
                }

                // Parameterless WaitForExit also waits until the redirected streams are drained.
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        /// <summary>Runs a tool and throws if it returns a non-zero exit code.</summary>
        public static async Task RunCheckedAsync(
            string fileName,
            string arguments,
            string description,
            CancellationToken cancellationToken,
            Action<string> onOutput = null,
            string workingDirectory = null,
            string errorPrefix = null)
        {
            int exitCode = await RunAsync(fileName, arguments, cancellationToken, onOutput, workingDirectory, errorPrefix).ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"{description} failed (exit code {exitCode}).");
            }
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }

                // taskkill /T also stops children such as DismHost.exe or wget launched by UpdatePack.
                var startInfo = new ProcessStartInfo("taskkill.exe", $"/PID {process.Id} /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (Process killer = Process.Start(startInfo))
                {
                    killer?.WaitForExit(15000);
                }
            }
            catch
            {
                // Fall through to Kill() below.
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Process already gone.
            }
        }
    }
}
