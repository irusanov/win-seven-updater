using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using ZenSevenUpdater.Properties;

namespace ZenSevenUpdater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private bool _isActionRunning = false;

        private readonly AppSettings _appSettings = new AppSettings().Load();

        private class CustomComboBoxItem
        {
            public string Label { get; set; }
            public string FullPath { get; set; }
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                DataContext = _appSettings;

                DismHelper.SetLogAction(Log);
                IsoHelper.SetLogAction(Log);

                ButtonBrowseWorkingDirectory.Click += (s, e) => ExecuteSafe(() => SelectDirectory(path =>
                {
                    long requiredBytes = 30L * 1024 * 1024 * 1024; // 30GB
                    if (FileUtils.CheckDiskSpace(path, requiredBytes))
                    {
                        Log("Sufficient disk space available.");
                        _appSettings.WorkingDirectory = path;
                    }
                    else
                    {
                        Log("Insufficient disk space. At least 30GB is required.");
                    }
                }));

                ButtonBrowse.Click += (s, e) => ExecuteSafe(() => SelectFile("ISO Files (*.iso)|*.iso", path =>
                {
                    _appSettings.Windows7IsoPath = path;
                    //DismHelper.CalculateChecksum(path, DismHelper.ChecksumAlgorithm.MD5);
                }));

                ButtonBrowseWin10.Click += (s, e) => ExecuteSafe(() => SelectFile("ISO Files (*.iso)|*.iso", path => _appSettings.Windows10IsoPath = path));

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

                    var win7IsoPath = _appSettings.Windows7IsoPath;
                    var win10IsoPath = _appSettings.Windows10IsoPath;
                    var workingDirectory = _appSettings.WorkingDirectory;
                    var win7WorkingDirectory = $"{workingDirectory}\\win7";
                    var win10WorkingDirectory = $"{workingDirectory}\\win10";
                    var installWimPath = $"{win7WorkingDirectory}\\sources\\install.wim";
                    var mountDirectory = $"{workingDirectory}\\mount";
                    var isoLabel = _appSettings.IsoLabel;

                    var driversPath = (ComboBoxDriversDirectory.SelectedItem as CustomComboBoxItem)?.FullPath ?? "";

                    CommandQueue.EnqueueCommand(ct => FileUtils.ExtractArchiveAsync(driversPath, $"{workingDirectory}\\drivers"));
                    CommandQueue.EnqueueCommand(ct => IsoHelper.ExtractIsoAsync(win7IsoPath, win7WorkingDirectory, ct));
                    CommandQueue.EnqueueCommand(ct => IsoHelper.ExtractIsoAsync(win10IsoPath, win10WorkingDirectory, ct));

                    CommandQueue.EnqueueCommand(ct => FileUtils.DeleteFileAsync($"{win10WorkingDirectory}\\sources\\install.esd"));
                    CommandQueue.EnqueueCommand(ct => FileUtils.DeleteFileAsync($"{win10WorkingDirectory}\\sources\\install.wim"));

                    CommandQueue.EnqueueCommand(ct => DismHelper.GetWimInfoAsync(installWimPath, ct));

                    CommandQueue.EnqueueCommand(ct => DismHelper.DeleteImageAsync(installWimPath, 1, ct));
                    CommandQueue.EnqueueCommand(ct => DismHelper.DeleteImageAsync(installWimPath, 1, ct));
                    CommandQueue.EnqueueCommand(ct => DismHelper.DeleteImageAsync(installWimPath, 1, ct));

                    CommandQueue.EnqueueCommand(ct => DismHelper.MountImageAsync(installWimPath, mountDirectory, "1", true, ct));
                    CommandQueue.EnqueueCommand(ct => DismHelper.AddDriverAsync(mountDirectory, $"{workingDirectory}\\drivers", true, ct));
                    CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync(mountDirectory, true, ct));

                    CommandQueue.EnqueueCommand(ct => FileUtils.CopyFileAsync(installWimPath, $"{win10WorkingDirectory}\\sources\\install.wim"));

                    CommandQueue.EnqueueCommand(ct => FileUtils.CleanupWorkingDirectory(win7WorkingDirectory));

                    CommandQueue.EnqueueCommand(ct => IsoHelper.CreateBootableIsoFromDirectoryAsync(win10WorkingDirectory, $"{workingDirectory}\\output.iso", isoLabel ?? "BOOTABLEISO", ct));
                };

                ButtonCancel.Click += (s, e) =>
                {
                    CommandQueue.CancelQueue();

                    var workingDirectory = _appSettings.WorkingDirectory;
                    var mountDirectory = $"{workingDirectory}\\mount";
                    if (!FileUtils.IsDirectoryEmpty(mountDirectory))
                    {
                        CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync(mountDirectory, false, ct));
                    }
                };

                ButtonCleanup.Click += (s, e) =>
                {
                    SetButtonsEnabled(false);

                    var workingDirectory = _appSettings.WorkingDirectory;
                    var mountDirectory = $"{workingDirectory}\\mount";
                    if (!FileUtils.IsDirectoryEmpty(mountDirectory))
                    {
                        CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync(mountDirectory, false, ct));
                    }
                    CommandQueue.EnqueueCommand(ct => FileUtils.CleanupWorkingDirectory(workingDirectory));
                };

                // Drivers combobox
                var drivers = FileUtils.GetArchiveFiles(Path.Combine(Directory.GetCurrentDirectory(), "drivers"));
                foreach (string driver in drivers)
                {
                    var driverLabel = Path.GetFileNameWithoutExtension(driver);
                    ComboBoxDriversDirectory.Items.Add(new CustomComboBoxItem { Label = driverLabel, FullPath = driver });
                    if (_appSettings.Drivers.Equals(driverLabel))
                    {
                        ComboBoxDriversDirectory.SelectedIndex = ComboBoxDriversDirectory.Items.Count - 1;
                    }
                }
                ComboBoxDriversDirectory.DisplayMemberPath = "Label";

                Log("Ready.");
            }
            catch (Exception ex)
            {
                HandleError(ex.Message);
                ExitApplication();
            }
        }
        private void SetButtonsEnabled(bool isEnabled)
        {
            ButtonBrowseWorkingDirectory.IsEnabled = isEnabled;
            ButtonBrowse.IsEnabled = isEnabled;
            ButtonBrowseWin10.IsEnabled = isEnabled;
            ButtonStart.IsEnabled = isEnabled;
            ButtonCleanup.IsEnabled = isEnabled;
            TextBoxIsoLabel.IsEnabled = isEnabled;
            ComboBoxDriversDirectory.IsEnabled = isEnabled;
            TextBoxIsoLabel.IsEnabled = isEnabled;
            TextBoxWindows7IsoPath.IsEnabled = isEnabled;
            TextBoxWindows10IsoPath.IsEnabled = isEnabled;
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
                string text = $"[{timestamp}] {message}{Environment.NewLine}";
                TextBoxLog.AppendText(text);
                TextBoxLog.ScrollToEnd();

                // Log to output.log file in the working directory
                var workingDirectory = _appSettings.WorkingDirectory;
                if (!Directory.Exists(workingDirectory))
                {
                    workingDirectory = Directory.GetCurrentDirectory();
                }
                var logFilePath = Path.Combine(workingDirectory, "output.log");
                File.AppendAllText(logFilePath, text);
            });
        }

        private void ExitApplication()
        {
            _appSettings?.Save();
            Application.Current.Shutdown();
        }

        private static void HandleError(string message, string title = "Error")
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        private void AdonisWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _appSettings?.Save();
        }

        private void ComboBoxDriversDirectory_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selectedItem = ComboBoxDriversDirectory?.SelectedItem;
            if (selectedItem is CustomComboBoxItem)
            {
                _appSettings.Drivers = (selectedItem as CustomComboBoxItem).Label;
            }
        }

        private void TextBoxLog_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}
