using Microsoft.Win32;
using SevenUpdater.Properties;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace SevenUpdater
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
                UpdatesHelper.SetLogAction(Log);

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

                ButtonBrowseOutputDirectory.Click += (s, e) => ExecuteSafe(() => SelectDirectory(path => _appSettings.OutputDirectory = path));

                CommandQueue.OnQueueCompleted += () =>
                {
                    _isActionRunning = false;
                    SetButtonsEnabled(true);
                };

                ButtonStart.Click += (s, e) =>
                {
                    try
                    {
                        if (_isActionRunning) return;

                        _isActionRunning = true;
                        SetButtonsEnabled(false);

                        var win7IsoPath = _appSettings.Windows7IsoPath;
                        var win10IsoPath = _appSettings.Windows10IsoPath;
                        var workingDirectory = _appSettings.WorkingDirectory;
                        var outputDirectory = String.IsNullOrEmpty(_appSettings.OutputDirectory) ? workingDirectory : _appSettings.OutputDirectory;
                        var win7WorkingDirectory = $"{workingDirectory}\\win7";
                        var win10WorkingDirectory = $"{workingDirectory}\\win10";
                        var installWimPath = $"{win7WorkingDirectory}\\sources\\install.wim";
                        var mountDirectory = $"{workingDirectory}\\offline";
                        var isoLabel = _appSettings.IsoLabel;
                        bool addDrivers = ComboBoxDriversDirectory.SelectedIndex > 0;

                        var driversPath = (ComboBoxDriversDirectory.SelectedItem as CustomComboBoxItem)?.FullPath ?? "";

                        if (!File.Exists(win7IsoPath))
                        {
                            throw new FileNotFoundException($"ISO file not found: {win7IsoPath}", win7IsoPath);
                        }

                        if (!File.Exists(win10IsoPath))
                        {
                            throw new FileNotFoundException($"ISO file not found: {win10IsoPath}", win10IsoPath);
                        }

                        if (_appSettings.CheckForUpdaterPackUpdates)
                        {
                            CommandQueue.EnqueueCommand(ct => UpdatesHelper.RunUpdatePackCheckAsync("updates\\UpdatePack7R2+.exe", ct));
                        }

                        CommandQueue.EnqueueCommand(ct => IsoHelper.ExtractIsoAsync(win7IsoPath, win7WorkingDirectory, ct));

                        CommandQueue.EnqueueCommand(ct => DismHelper.ShowWimInfoDialogAsync(installWimPath, workingDirectory, ct));

                        /*
                        if (_appSettings.IncludeUpdates)
                        {
                            CommandQueue.EnqueueCommand(ct => UpdatesHelper.RunUpdatePackAsync($"{win7WorkingDirectory}\\sources\\UpdatePack7R2.exe", installWimPath, 1, true, ct));
                        }
                        */
                        CommandQueue.EnqueueCommand(ct => DismHelper.MountImageAsync(installWimPath, mountDirectory, "1", false, ct));

                        if (_appSettings.IncludeModdedAcpi)
                        {
                            CommandQueue.EnqueueCommand(ct => FileUtils.ExtractArchiveAsync("acpi\\WIN7_A5_FIX_ACPI.7z", $"{workingDirectory}\\acpi", ct));
                            CommandQueue.EnqueueCommand(ct => FileUtils.CopyFileAsync($"{workingDirectory}\\acpi\\acpi.sys", $"{mountDirectory}\\Windows\\System32\\drivers"));
                            CommandQueue.EnqueueCommand(ct => FileUtils.CopyFileToProtectedFolderAsync(
                                $"{workingDirectory}\\acpi\\acpi.sys",
                                $"{mountDirectory}\\Windows\\System32\\DriverStore\\FileRepository",
                                "acpi.inf_amd64_neutral_"));
                        }

                        if (addDrivers)
                        {
                            CommandQueue.EnqueueCommand(ct => FileUtils.ExtractArchiveAsync(driversPath, $"{workingDirectory}\\drivers", ct));
                            CommandQueue.EnqueueCommand(ct => DismHelper.AddDriverAsync(mountDirectory, $"{workingDirectory}\\drivers", true, ct));
                        }

                        CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync(mountDirectory, true, ct));

                        if (_appSettings.IncludeUpdates)
                        {
                            CommandQueue.EnqueueCommand(ct => UpdatesHelper.RunUpdatePackAsync($"{win7WorkingDirectory}\\sources", installWimPath, $"{workingDirectory}\\temp", 1, true, ct));
                        }

                        CommandQueue.EnqueueCommand(ct => IsoHelper.ExtractIsoAsync(win10IsoPath, win10WorkingDirectory, ct));
                        CommandQueue.EnqueueCommand(ct => FileUtils.DeleteFileAsync($"{win10WorkingDirectory}\\sources\\install.esd"));
                        CommandQueue.EnqueueCommand(ct => FileUtils.DeleteFileAsync($"{win10WorkingDirectory}\\sources\\install.wim"));

                        CommandQueue.EnqueueCommand(ct => FileUtils.CopyFileAsync(installWimPath, $"{win10WorkingDirectory}\\sources"));
                        //CommandQueue.EnqueueCommand(ct => FileUtils.CopyFileAsync($"{win7WorkingDirectory}\\sources\\*.clg", $"{win10WorkingDirectory}\\sources"));
                        CommandQueue.EnqueueCommand(ct => IsoHelper.CreateIsoWithOcdimgAsync(win10WorkingDirectory, $"{outputDirectory}\\output.iso", isoLabel ?? "AMDSEVEN", ct));
                        CommandQueue.EnqueueCommand(ct => FileUtils.CleanupWorkingDirectory(win7WorkingDirectory));
                    }
                    catch (Exception ex)
                    {
                        Log(ex.Message);
                        HandleError(ex.Message);
                        _isActionRunning = false;
                    }
                };

                ButtonCancel.Click += (s, e) =>
                {
                    CommandQueue.CancelQueue();

                    var workingDirectory = _appSettings.WorkingDirectory;
                    var mountDirectory = $"{workingDirectory}\\offline";
                    if (!FileUtils.IsDirectoryEmpty(mountDirectory))
                    {
                        CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync(mountDirectory, false, ct));
                    }
                };

                ButtonCleanup.Click += (s, e) =>
                {
                    SetButtonsEnabled(false);

                    var workingDirectory = _appSettings.WorkingDirectory;
                    var mountDirectory = $"{workingDirectory}\\offline";
                    if (!FileUtils.IsDirectoryEmpty(mountDirectory))
                    {
                        CommandQueue.EnqueueCommand(ct => DismHelper.UnmountImageAsync(mountDirectory, false, ct));
                    }
                    CommandQueue.EnqueueCommand(ct => FileUtils.CleanupWorkingDirectory(workingDirectory));
                };

                // Drivers combobox
                var drivers = FileUtils.GetArchiveFiles(Path.Combine(Directory.GetCurrentDirectory(), "drivers"));
                ComboBoxDriversDirectory.DisplayMemberPath = "Label";
                ComboBoxDriversDirectory.Items.Add(new CustomComboBoxItem { Label = "None", FullPath = "" });
                foreach (string driver in drivers)
                {
                    var driverLabel = Path.GetFileNameWithoutExtension(driver);
                    ComboBoxDriversDirectory.Items.Add(new CustomComboBoxItem { Label = driverLabel, FullPath = driver });
                    if (_appSettings.Drivers.Equals(driverLabel))
                    {
                        ComboBoxDriversDirectory.SelectedIndex = ComboBoxDriversDirectory.Items.Count - 1;
                    }
                }
                if (ComboBoxDriversDirectory.SelectedIndex == -1)
                {
                    ComboBoxDriversDirectory.SelectedIndex = 0;
                }

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
            ButtonBrowseWorkingDirectory.IsEnabled = isEnabled;
            ButtonBrowseOutputDirectory.IsEnabled = isEnabled;
            TextBoxIsoLabel.IsEnabled = isEnabled;
            ComboBoxDriversDirectory.IsEnabled = isEnabled;
            TextBoxIsoLabel.IsEnabled = isEnabled;
            TextBoxWindows7IsoPath.IsEnabled = isEnabled;
            TextBoxWindows10IsoPath.IsEnabled = isEnabled;
            TextBoxWorkingDirectory.IsEnabled = isEnabled;
            TextBoxOutputDirectory.IsEnabled = isEnabled;
            CheckBoxCheckForUpdates.IsEnabled = isEnabled;
            CheckBoxIncludeModdedAcpi.IsEnabled = isEnabled;
            CheckBoxIncludeUpdates.IsEnabled = isEnabled;
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

                if (message.Contains("%"))
                {
                    var percentageMatch = Regex.Match(message, @"(\d+\.\d+%)");
                    if (percentageMatch.Success)
                    {
                        string progress = percentageMatch.Value;

                        string currentText = TextBoxLog.Text;
                        string[] lines = currentText.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                        // Check if the last line is a progress line
                        if (lines.Length > 0 && lines[lines.Length - 1].Contains("[DISM]: ["))
                        {
                            // Replace only the last line
                            lines[lines.Length - 1] = $"[{timestamp}] [DISM]: [{progress}]";
                        }
                        else
                        {
                            // Append new progress line
                            var newLine = $"[{timestamp}] [DISM]: [{progress}]";
                            lines = lines.Concat(new[] { newLine }).ToArray();
                        }

                        TextBoxLog.Text = string.Join(Environment.NewLine, lines);
                        TextBoxLog.ScrollToEnd();
                    }
                }
                else
                {
                    string text = $"{Environment.NewLine}[{timestamp}] {message}";
                    TextBoxLog.AppendText(text);
                    TextBoxLog.ScrollToEnd();

                    var workingDirectory = _appSettings.WorkingDirectory;
                    if (!Directory.Exists(workingDirectory))
                    {
                        workingDirectory = Directory.GetCurrentDirectory();
                    }
                    var logFilePath = Path.Combine(workingDirectory, "output.log");
                    File.AppendAllText(logFilePath, text);
                }
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
            _appSettings.WindowLeft = Left;
            _appSettings.WindowTop = Top;
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

        private void AdonisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //TextBoxLog.Height = TextBoxLog.ActualHeight + 15;
        }

        private void AdonisWindow_Initialized(object sender, EventArgs e)
        {
            if (_appSettings.WindowLeft == -1 || _appSettings.WindowTop == -1)
            {
                return;
            }

            WindowStartupLocation = WindowStartupLocation.Manual;

            System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            System.Drawing.Rectangle screenBounds = screen.Bounds;

            if (_appSettings.WindowLeft < screenBounds.Left || _appSettings.WindowLeft + Width > screenBounds.Right ||
                _appSettings.WindowTop < screenBounds.Top || _appSettings.WindowTop + Height > screenBounds.Bottom)
            {
                Left = (screenBounds.Width - Width) / 2 + screenBounds.Left;
                Top = (screenBounds.Height - Height) / 2 + screenBounds.Top;
            }
            else
            {
                Left = _appSettings.WindowLeft;
                Top = _appSettings.WindowTop;
            }
        }
    }
}
