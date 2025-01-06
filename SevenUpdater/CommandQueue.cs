using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SevenUpdater
{
    public static class CommandQueue
    {
        private static readonly Queue<Func<CancellationToken, Task>> _commands = new Queue<Func<CancellationToken, Task>>();
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private static bool _isProcessing = false;
        public static event Action OnQueueCompleted;

        public static void EnqueueCommand(Func<CancellationToken, Task> command)
        {
            lock (_commands)
            {
                _commands.Enqueue(command);
                if (!_isProcessing)
                {
                    _isProcessing = true;
                    _ = ProcessQueue();
                }
            }
        }

        private static async Task ProcessQueue()
        {
            while (true)
            {
                Func<CancellationToken, Task> command;
                lock (_commands)
                {
                    if (_commands.Count == 0)
                    {
                        _isProcessing = false;
                        OnQueueCompleted?.Invoke();
                        return;
                    }
                    command = _commands.Dequeue();
                }

                try
                {
                    if (_isProcessing)
                    {
                        await command(_cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    DismHelper.Log($"Error executing command: {ex.Message}");
                    CancelQueue();
                    return;
                }
            }
        }

        public static void CancelQueue()
        {
            if (_isProcessing)
            {
                _isProcessing = false;
                _cts.Cancel();
                _cts = new CancellationTokenSource();
                lock (_commands)
                {
                    _commands.Clear();
                }
                OnQueueCompleted?.Invoke();
                DismHelper.Log("Queue canceled.");
            }
        }
    }
}
