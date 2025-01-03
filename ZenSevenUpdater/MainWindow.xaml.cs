using Microsoft.Win32;
using System;
using System.IO;

namespace ZenSevenUpdater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private bool _isActionRunning = false;

        public MainWindow()
        {
            InitializeComponent();

            DismHelper.SetLogAction(Log);
            IsoHelper.SetLogAction(Log);

            ButtonBrowseWorkingDirectory.Click += (s, e) => ExecuteSafe(() => SelectDirectory(path =>
            {
                long requiredBytes = 30L * 1024 * 1024 * 1024; // 30GB
                if (FileUtils.CheckDiskSpace(path, requiredBytes))
                {
                    Log("Sufficient disk space available.");
                    TextBoxWorkingDirectory.Text = path;
                }
                else
                {
                    Log("Insufficient disk space. At least 30GB is required.");
                }
            }));

            ButtonBrowse.Click += (s, e) => ExecuteSafe(() => SelectFile("ISO Files (*.iso)|*.iso", path =>
            {
                TextBoxIsoPath.Text = path;
                //DismHelper.CalculateChecksum(path, DismHelper.ChecksumAlgorithm.MD5);
            }));

            ButtonBrowseWin10.Click += (s, e) => ExecuteSafe(() => SelectFile("ISO Files (*.iso)|*.iso", path => TextBoxWin10IsoPath.Text = path));

            CommandQueue.OnQueueCompleted += () =>
            {
                _isActionRunning = false;
                SetButtonsEnabled(true);
            };

            ButtonStart.Click += (s, e) =>
            {
                if (_isActionRunning) return;

                _isActionRunning = true;
                SetButtonsEnabled(false);

                var isoPath = TextBoxIsoPath.Text;
                var workingDirectory = TextBoxWorkingDirectory.Text;
                var installWimPath = $"{workingDirectory}\\iso\\sources\\install.wim";

                CommandQueue.EnqueueCommand(ct => IsoHelper.ExtractIsoAsync(isoPath, $"{workingDirectory}\\iso", ct));

                CommandQueue.EnqueueCommand(ct => DismHelper.GetWimInfoAsync(installWimPath, ct));

                CommandQueue.EnqueueCommand(ct => DismHelper.DeleteImageAsync(installWimPath, 1, ct));
                CommandQueue.EnqueueCommand(ct => DismHelper.DeleteImageAsync(installWimPath, 1, ct));
                CommandQueue.EnqueueCommand(ct => DismHelper.DeleteImageAsync(installWimPath, 1, ct));

                CommandQueue.EnqueueCommand(ct => DismHelper.MountImageAsync(installWimPath, $"{workingDirectory}\\mount", "1", ct));
                CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync($"{workingDirectory}\\mount", true, ct));

                CommandQueue.EnqueueCommand(ct => IsoHelper.CreateBootableIsoFromDirectoryAsync($"{workingDirectory}\\iso", $"{workingDirectory}\\output.iso", ct));
            };

            ButtonCancel.Click += (s, e) =>
            {
                CommandQueue.CancelQueue();

                var workingDirectory = TextBoxWorkingDirectory.Text;
                if (Directory.Exists($"{workingDirectory}\\mount") && Directory.GetFiles($"{workingDirectory}\\mount").Length == 0)
                {
                    CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync($"{workingDirectory}\\mount", false, ct));
                }
            };

            Log("Ready.");

            /*
             * Button cancelQueueButton = new Button { Content = "Cancel Queue", Margin = new Thickness(10) };
    cancelQueueButton.Click += (s, e) => CommandQueue.CancelQueue();

    mountButton.Click += (s, e) => CommandQueue.EnqueueCommand(ct => DismHelper.MountImageAsync("C:\\path\\to\\image.wim", "C:\\mount\\directory", "1", ct));
    unmountButton.Click += (s, e) => CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync("C:\\mount\\directory", true, ct));
    deleteButton.Click += (s, e) => CommandQueue.EnqueueCommand(ct => DismHelper.DeleteImageAsync("C:\\path\\to\\image.wim", ct));
    addDriverButton.Click += (s, e) => CommandQueue.EnqueueCommand(ct => DismHelper.AddDriverAsync("C:\\mount\\directory", "C:\\path\\to\\driver", true, ct));

    buttonPanel.Children.Add(cancelQueueButton);

            */
        }
        private void SetButtonsEnabled(bool isEnabled)
        {
            ButtonBrowseWorkingDirectory.IsEnabled = isEnabled;
            ButtonBrowse.IsEnabled = isEnabled;
            ButtonBrowseWin10.IsEnabled = isEnabled;
            ButtonStart.IsEnabled = isEnabled;
        }

        private void SelectFile(string filter, Action<string> onFileSelected)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedFile = dialog.FileName;
                Log($"Selected File: {selectedFile}");
                onFileSelected?.Invoke(selectedFile);
            }
        }

        private void SelectDirectory(Action<string> onDirectorySelected)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = dialog.SelectedPath;
                Log($"Selected Directory: {selectedPath}");
                onDirectorySelected?.Invoke(selectedPath);
            }
        }

        private void ExecuteSafe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                TextBoxLog.AppendText($"[{timestamp}] {message}" + Environment.NewLine);
                TextBoxLog.ScrollToEnd();
            });
        }
    }
}
