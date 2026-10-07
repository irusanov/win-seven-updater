using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SevenUpdater
{
    internal sealed class PipelineStep
    {
        public PipelineStep(string name, Func<CancellationToken, Task> action)
        {
            Name = name;
            Action = action;
        }

        public string Name { get; }
        public Func<CancellationToken, Task> Action { get; }
    }

    internal enum PipelineResult
    {
        Completed,
        Canceled,
        Failed
    }

    /// <summary>
    /// Runs a list of steps one after another.
    /// The previous queue could start a second processing loop while a cancelled command was still
    /// running (e.g. "unmount" started during a mount), raised completion twice, and never stopped
    /// the running DISM/UpdatePack process. Now a run is a single awaited task: cancelling kills the
    /// running tool, and an optional clean-up callback runs once the step has actually stopped.
    /// </summary>
    internal static class CommandQueue
    {
        private static CancellationTokenSource _cts;

        public static bool IsRunning => _cts != null;

        public static Exception LastError { get; private set; }

        /// <summary>Raised on the calling (UI) thread: step number (1-based), step count, step name.</summary>
        public static event Action<int, int, string> StepStarted;

        public static async Task<PipelineResult> RunAsync(IList<PipelineStep> steps, Func<Task> cleanupOnAbort = null)
        {
            if (_cts != null)
            {
                throw new InvalidOperationException("Another operation is already running.");
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            LastError = null;
            PipelineResult result = PipelineResult.Completed;

            try
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    PipelineStep step = steps[i];
                    StepStarted?.Invoke(i + 1, steps.Count, step.Name);
                    Logger.Log($"--- Step {i + 1}/{steps.Count}: {step.Name} ---");
                    Logger.ReportProgress(null);

                    // Always run off the UI thread: ISO extraction and file copies are synchronous
                    // under the hood and used to freeze the window.
                    await Task.Run(() => step.Action(token), token);
                }
            }
            catch (OperationCanceledException)
            {
                result = PipelineResult.Canceled;
                Logger.Warn("Operation canceled.");
            }
            catch (Exception ex)
            {
                result = PipelineResult.Failed;
                LastError = ex;
                Logger.Error(Logger.Describe(ex));
            }

            if (result != PipelineResult.Completed && cleanupOnAbort != null)
            {
                try
                {
                    await Task.Run(cleanupOnAbort);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Clean-up after abort failed: " + Logger.Describe(ex));
                }
            }

            _cts.Dispose();
            _cts = null;
            Logger.ReportProgress(null);
            return result;
        }

        public static void Cancel()
        {
            CancellationTokenSource cts = _cts;
            if (cts == null || cts.IsCancellationRequested)
            {
                return;
            }

            Logger.Log("Cancel requested, stopping the current step...");
            // Cancellation callbacks kill processes and may block for a moment; keep the UI responsive.
            Task.Run(() =>
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Run finished in the meantime.
                }
            });
        }
    }
}
