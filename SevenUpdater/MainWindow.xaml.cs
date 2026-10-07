using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace SevenUpdater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private const long RecommendedFreeSpaceBytes = 30L * 1024 * 1024 * 1024; // 30 GB
        private const string AppTitle = "Seven Updater";

        private readonly AppSettings _appSettings = new AppSettings().Load();
        private string _baseTitle;
        private bool _closeRequested;
        private bool _logHasText;
        private int _stepNumber;
        private int _stepCount;
        private string _stepName;

        private class CustomComboBoxItem
        {
            public string Label { get; set; }
            public string FullPath { get; set; }
        }

        /// <summary>Everything a build needs, validated and captured before it starts.</summary>
        private sealed class BuildOptions
        {
            public string Win7IsoPath;
            public string Win10IsoPath;
            public WorkingPaths Paths;
            public string OutputIsoPath;
            public string Label;
            public string DriversArchive;
            public bool IncludeUpdates;
            public bool CheckForUpdates;
            public bool IncludeModdedAcpi;
            public string PreferredEdition;
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                _baseTitle = Title;

                DataContext = _appSettings;
                TaskbarItemInfo = new TaskbarItemInfo();

                Logger.MessageLogged += OnMessageLogged;
                Logger.ProgressChanged += OnProgressChanged;
                CommandQueue.StepStarted += OnStepStarted;

                ButtonBrowseWorkingDirectory.Click += (s, e) => ExecuteSafe(() => SelectDirectory(_appSettings.WorkingDirectory, path =>
                {
                    long free = FileUtils.GetAvailableFreeSpace(path);
                    if (free >= 0 && free < RecommendedFreeSpaceBytes)
                    {
                        MessageBoxResult answer = ThemedMessageBox.Show(
                            $"Only {FormatSize(free)} is free on this drive. About {FormatSize(RecommendedFreeSpaceBytes)} is recommended for the working directory.\n\nUse this folder anyway?",
                            AppTitle,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (answer != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }
                    _appSettings.WorkingDirectory = path;
                }));

                ButtonBrowse.Click += (s, e) => ExecuteSafe(() => SelectFile(_appSettings.Windows7IsoPath, path => _appSettings.Windows7IsoPath = path));
                ButtonBrowseWin10.Click += (s, e) => ExecuteSafe(() => SelectFile(_appSettings.Windows10IsoPath, path => _appSettings.Windows10IsoPath = path));
                ButtonBrowseOutputDirectory.Click += (s, e) => ExecuteSafe(() => SelectDirectory(_appSettings.OutputDirectory, path => _appSettings.OutputDirectory = path));

                ButtonStart.Click += ButtonStart_Click;
                ButtonCancel.Click += (s, e) => CommandQueue.Cancel();
                ButtonCleanup.Click += ButtonCleanup_Click;
                ButtonOpenOutput.Click += (s, e) => ExecuteSafe(() =>
                {
                    string folder = GetOutputDirectory();
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    {
                        ShowError("The output directory does not exist yet.");
                        return;
                    }
                    FileUtils.OpenFolderAndSelect(folder);
                });

                LoadDrivers();
                SetBusy(false);
                Logger.Log("Ready.");
            }
            catch (Exception ex)
            {
                ShowError(Logger.Describe(ex));
                ExitApplication();
            }
        }

        #region Build

        private async void ButtonStart_Click(object sender, RoutedEventArgs e)
        {
            if (CommandQueue.IsRunning)
            {
                return;
            }

            try
            {
                BuildOptions options = ValidateAndCollectOptions();
                if (options == null)
                {
                    return;
                }

                _appSettings.Save();

                Logger.Log("========== Build started ==========");
                Logger.Log($"Windows 7 ISO:  {options.Win7IsoPath}");
                Logger.Log($"Windows 10 ISO: {options.Win10IsoPath}");
                Logger.Log($"Working dir:    {options.Paths.Root}");
                Logger.Log($"Output ISO:     {options.OutputIsoPath}");
                Logger.Log($"Drivers: {(options.DriversArchive == null ? "none" : Path.GetFileName(options.DriversArchive))}, " +
                           $"updates: {(options.IncludeUpdates ? "yes" : "no")}, modded acpi.sys: {(options.IncludeModdedAcpi ? "yes" : "no")}");

                List<PipelineStep> steps = BuildSteps(options);
                WorkingPaths paths = options.Paths;
                var stopwatch = Stopwatch.StartNew();

                PipelineResult result;
                SetBusy(true);
                try
                {
                    result = await CommandQueue.RunAsync(steps, () => UnmountAfterAbortAsync(paths));
                }
                finally
                {
                    SetBusy(false);
                }

                Logger.Log($"========== Build {result.ToString().ToLowerInvariant()} after {FormatDuration(stopwatch.Elapsed)} ==========");
                ShowResult(result, options.OutputIsoPath);
            }
            catch (Exception ex)
            {
                Logger.Error(Logger.Describe(ex));
                ShowError(Logger.Describe(ex));
            }
        }

        private BuildOptions ValidateAndCollectOptions()
        {
            var options = new BuildOptions
            {
                Win7IsoPath = (_appSettings.Windows7IsoPath ?? string.Empty).Trim().Trim('"'),
                Win10IsoPath = (_appSettings.Windows10IsoPath ?? string.Empty).Trim().Trim('"'),
                Label = (_appSettings.IsoLabel ?? string.Empty).Trim(),
                IncludeUpdates = _appSettings.IncludeUpdates,
                CheckForUpdates = _appSettings.IncludeUpdates && _appSettings.CheckForUpdaterPackUpdates,
                IncludeModdedAcpi = _appSettings.IncludeModdedAcpi,
                PreferredEdition = _appSettings.SelectedEdition,
            };

            if (!ValidateIso(options.Win7IsoPath, "Windows 7") || !ValidateIso(options.Win10IsoPath, "Windows 10"))
            {
                return null;
            }

            if (string.Equals(Path.GetFullPath(options.Win7IsoPath), Path.GetFullPath(options.Win10IsoPath), StringComparison.OrdinalIgnoreCase))
            {
                ShowError("The Windows 7 and Windows 10 ISO paths point to the same file.");
                return null;
            }

            string workingDirectory;
            if (!TryGetFullDirectoryPath(_appSettings.WorkingDirectory, "working directory", out workingDirectory))
            {
                return null;
            }

            string outputDirectory = GetOutputDirectory();
            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = workingDirectory;
            }
            if (!TryGetFullDirectoryPath(outputDirectory, "output directory", out outputDirectory))
            {
                return null;
            }

            if (!IsoHelper.ValidLabel.IsMatch(options.Label))
            {
                ShowError("The ISO label may only contain letters, digits, '_' and '-' (no spaces) and must be 1-32 characters long.");
                TextBoxIsoLabel.Focus();
                return null;
            }

            var selectedDrivers = ComboBoxDriversDirectory.SelectedItem as CustomComboBoxItem;
            if (selectedDrivers != null && !string.IsNullOrEmpty(selectedDrivers.FullPath))
            {
                if (!File.Exists(selectedDrivers.FullPath))
                {
                    ShowError($"The driver pack was not found:\n{selectedDrivers.FullPath}");
                    return null;
                }
                options.DriversArchive = selectedDrivers.FullPath;
            }

            var missingTools = new List<string>();
            if (!File.Exists(AppPaths.OscdimgExe))
            {
                missingTools.Add(AppPaths.OscdimgExe);
            }
            if ((options.DriversArchive != null || options.IncludeModdedAcpi) && !File.Exists(AppPaths.SevenZipExe))
            {
                missingTools.Add(AppPaths.SevenZipExe);
            }
            if (options.IncludeModdedAcpi && !File.Exists(AppPaths.AcpiArchive))
            {
                missingTools.Add(AppPaths.AcpiArchive);
            }
            if (options.CheckForUpdates && !File.Exists(AppPaths.UpdatePackCheckerExe))
            {
                missingTools.Add(AppPaths.UpdatePackCheckerExe);
            }
            if (missingTools.Count > 0)
            {
                ShowError("Required files are missing:\n\n" + string.Join("\n", missingTools));
                return null;
            }

            if (options.IncludeUpdates && !options.CheckForUpdates && UpdatesHelper.FindLatestUpdatePack() == null)
            {
                ShowError($"\"Include updates\" is enabled, but no UpdatePack7R2-*.exe was found in:\n{AppPaths.UpdatesDirectory}\n\nEnable \"Check for UpdatePack updates\" to download it, or place it there manually.");
                return null;
            }

            long free = FileUtils.GetAvailableFreeSpace(workingDirectory);
            if (free >= 0 && free < RecommendedFreeSpaceBytes)
            {
                if (!Confirm($"Only {FormatSize(free)} is free on the working directory drive; about {FormatSize(RecommendedFreeSpaceBytes)} is recommended.\n\nStart anyway?"))
                {
                    return null;
                }
            }

            options.Paths = new WorkingPaths(workingDirectory);
            options.OutputIsoPath = Path.Combine(outputDirectory, options.Label + ".iso");

            if (File.Exists(options.OutputIsoPath) &&
                !Confirm($"{options.OutputIsoPath}\nalready exists. Overwrite it?"))
            {
                return null;
            }

            return options;
        }

        private List<PipelineStep> BuildSteps(BuildOptions o)
        {
            WorkingPaths p = o.Paths;
            var steps = new List<PipelineStep>
            {
                new PipelineStep("Preparing working directory", ct => PrepareWorkingDirectoryAsync(p, ct))
            };

            if (o.CheckForUpdates)
            {
                steps.Add(new PipelineStep("Checking for UpdatePack7R2 updates", ct => UpdatesHelper.RunUpdatePackCheckAsync(ct)));
            }

            steps.Add(new PipelineStep("Extracting Windows 7 ISO", ct => IsoHelper.ExtractIsoAsync(o.Win7IsoPath, p.Win7, ct)));

            steps.Add(new PipelineStep("Selecting Windows 7 edition", async ct =>
            {
                string edition = await DismHelper.SelectEditionAsync(p.InstallWim, p.TempWim, o.PreferredEdition, ct);
                if (!string.IsNullOrEmpty(edition))
                {
                    Dispatcher.Invoke(() => _appSettings.SelectedEdition = edition);
                }
            }));

            // Updates are integrated before acpi.sys is replaced: update packs can ship a newer acpi.sys,
            // which would silently overwrite the modded one if they ran afterwards.
            if (o.IncludeUpdates)
            {
                steps.Add(new PipelineStep("Integrating updates (UpdatePack7R2) - this takes a while", ct =>
                    UpdatesHelper.RunUpdatePackAsync(p.InstallWim, p.Temp, 1, true, ct)));
            }

            if (o.IncludeModdedAcpi || o.DriversArchive != null)
            {
                steps.Add(new PipelineStep("Mounting install.wim", ct => DismHelper.MountImageAsync(p.InstallWim, p.Mount, 1, false, ct)));

                if (o.IncludeModdedAcpi)
                {
                    steps.Add(new PipelineStep("Extracting modded acpi.sys", ct => FileUtils.ExtractArchiveAsync(AppPaths.AcpiArchive, p.Acpi, ct)));
                    steps.Add(new PipelineStep("Replacing acpi.sys", ct => ReplaceAcpiAsync(p, ct)));
                }

                if (o.DriversArchive != null)
                {
                    steps.Add(new PipelineStep("Extracting drivers", ct => FileUtils.ExtractArchiveAsync(o.DriversArchive, p.Drivers, ct)));
                    steps.Add(new PipelineStep("Adding drivers to the image", ct => DismHelper.AddDriverAsync(p.Mount, p.Drivers, true, p.DriversLog, ct)));
                }

                steps.Add(new PipelineStep("Unmounting install.wim (saving changes)", ct => DismHelper.UnmountImageAsync(p.Mount, true, ct)));
            }

            steps.Add(new PipelineStep("Extracting Windows 10 ISO", ct => IsoHelper.ExtractIsoAsync(o.Win10IsoPath, p.Win10, ct)));
            steps.Add(new PipelineStep("Replacing the Windows 10 install image", ct => ReplaceInstallImageAsync(p)));
            steps.Add(new PipelineStep("Creating ISO", ct => IsoHelper.CreateIsoWithOscdimgAsync(p.Win10, o.OutputIsoPath, o.Label, ct)));
            steps.Add(new PipelineStep("Removing temporary files", ct => RemoveTemporaryFilesAsync(p)));

            return steps;
        }

        private static async Task PrepareWorkingDirectoryAsync(WorkingPaths p, CancellationToken ct)
        {
            Directory.CreateDirectory(p.Root);

            if (!FileUtils.IsDirectoryEmpty(p.Mount))
            {
                Logger.Warn($"{p.Mount} is not empty - an image from a previous run is probably still mounted. Discarding it.");
                await DiscardMountAsync(p, ct);
                if (!FileUtils.IsDirectoryEmpty(p.Mount))
                {
                    throw new InvalidOperationException($"{p.Mount} is still in use. Close any Explorer windows showing it and press Clean, or restart Windows and try again.");
                }
            }

            await RemoveTemporaryFilesAsync(p);
        }

        private static async Task DiscardMountAsync(WorkingPaths p, CancellationToken ct)
        {
            try
            {
                await DismHelper.UnmountImageAsync(p.Mount, false, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn("Unmount failed: " + Logger.Describe(ex));
            }

            try
            {
                await DismHelper.CleanupMountpointsAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn("Mount point clean-up failed: " + Logger.Describe(ex));
            }
        }

        /// <summary>Runs after a failed or cancelled build: never leave an image mounted.</summary>
        private static async Task UnmountAfterAbortAsync(WorkingPaths p)
        {
            if (FileUtils.IsDirectoryEmpty(p.Mount))
            {
                return;
            }

            Logger.Log("Unmounting the image and discarding changes...");
            await DiscardMountAsync(p, CancellationToken.None);
        }

        private static Task RemoveTemporaryFilesAsync(WorkingPaths p)
        {
            return Task.Run(() =>
            {
                foreach (string directory in p.IntermediateDirectories)
                {
                    if (Directory.Exists(directory))
                    {
                        Logger.Log($"Deleting {directory}");
                        FileUtils.DeleteDirectory(directory);
                    }
                }

                FileUtils.DeleteFile(p.TempWim);

                // Never delete the mount directory while something is mounted in it:
                // that would delete files inside the mounted image.
                if (Directory.Exists(p.Mount) && FileUtils.IsDirectoryEmpty(p.Mount))
                {
                    Directory.Delete(p.Mount);
                }
            });
        }

        private static async Task ReplaceAcpiAsync(WorkingPaths p, CancellationToken ct)
        {
            string acpiSys = Directory.Exists(p.Acpi)
                ? Directory.GetFiles(p.Acpi, "acpi.sys", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (acpiSys == null)
            {
                throw new FileNotFoundException($"acpi.sys was not found in {Path.GetFileName(AppPaths.AcpiArchive)}.");
            }

            string windows = Path.Combine(p.Mount, "Windows", "System32");
            await FileUtils.CopyFileToProtectedFolderAsync(acpiSys, Path.Combine(windows, "drivers", "acpi.sys"), ct);

            string fileRepository = Path.Combine(windows, "DriverStore", "FileRepository");
            string[] driverStoreFolders = Directory.Exists(fileRepository)
                ? Directory.GetDirectories(fileRepository, "acpi.inf_amd64_*")
                : new string[0];

            if (driverStoreFolders.Length == 0)
            {
                Logger.Warn("No acpi.inf folder found in the driver store; only System32\\drivers\\acpi.sys was replaced.");
            }

            foreach (string folder in driverStoreFolders)
            {
                await FileUtils.CopyFileToProtectedFolderAsync(acpiSys, Path.Combine(folder, "acpi.sys"), ct);
            }
        }

        private static async Task ReplaceInstallImageAsync(WorkingPaths p)
        {
            string sources = Path.Combine(p.Win10, "sources");
            if (!Directory.Exists(sources))
            {
                throw new DirectoryNotFoundException("The Windows 10 ISO has no 'sources' folder. Is it a Windows installation ISO?");
            }

            foreach (string pattern in new[] { "install.esd", "install.wim", "install*.swm" })
            {
                foreach (string file in Directory.GetFiles(sources, pattern))
                {
                    await FileUtils.DeleteFileAsync(file);
                }
            }

            // Move instead of copy: same drive, instant, and saves several GB of free space.
            await FileUtils.MoveFileAsync(p.InstallWim, Path.Combine(sources, "install.wim"));
        }

        private void ShowResult(PipelineResult result, string outputIsoPath)
        {
            if (_closeRequested)
            {
                Dispatcher.InvokeAsync(Close);
                return;
            }

            switch (result)
            {
                case PipelineResult.Completed:
                    TextBlockStatus.Text = "Done: " + outputIsoPath;
                    if (ThemedMessageBox.Show(
                            $"The ISO was created successfully:\n{outputIsoPath}\n\nOpen the containing folder?",
                            AppTitle,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        FileUtils.OpenFolderAndSelect(outputIsoPath);
                    }
                    break;

                case PipelineResult.Canceled:
                    TextBlockStatus.Text = "Canceled";
                    break;

                case PipelineResult.Failed:
                    string message = Logger.Describe(CommandQueue.LastError);
                    TextBlockStatus.Text = "Failed: " + message;
                    ShowError($"The build failed during \"{_stepName}\":\n\n{message}\n\nSee the log for details.");
                    break;
            }
        }

        #endregion

        #region Clean

        private async void ButtonCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (CommandQueue.IsRunning)
            {
                return;
            }

            string workingDirectory;
            if (!TryGetFullDirectoryPath(_appSettings.WorkingDirectory, "working directory", out workingDirectory))
            {
                return;
            }

            var paths = new WorkingPaths(workingDirectory);
            if (!Confirm($"Delete the temporary folders (win7, win10, offline, drivers, acpi, temp) in\n{workingDirectory}?\n\nA leftover mounted image will be unmounted without saving. Other files, including created ISOs, are kept."))
            {
                return;
            }

            var steps = new List<PipelineStep>();
            if (!FileUtils.IsDirectoryEmpty(paths.Mount))
            {
                steps.Add(new PipelineStep("Unmounting leftover image", ct => DiscardMountAsync(paths, ct)));
            }
            steps.Add(new PipelineStep("Deleting temporary files", ct => RemoveTemporaryFilesAsync(paths)));

            try
            {
                PipelineResult result;
                SetBusy(true);
                try
                {
                    result = await CommandQueue.RunAsync(steps);
                }
                finally
                {
                    SetBusy(false);
                }

                if (_closeRequested)
                {
                    Dispatcher.InvokeAsync(Close);
                    return;
                }

                TextBlockStatus.Text = result == PipelineResult.Completed ? "Clean-up finished" : "Clean-up " + result.ToString().ToLowerInvariant();
                if (result == PipelineResult.Failed)
                {
                    ShowError("Clean-up failed:\n\n" + Logger.Describe(CommandQueue.LastError));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(Logger.Describe(ex));
                ShowError(Logger.Describe(ex));
            }
        }

        #endregion

        #region UI state and progress

        private void SetBusy(bool isBusy)
        {
            PanelSettings.IsEnabled = !isBusy;
            ButtonStart.IsEnabled = !isBusy;
            ButtonCleanup.IsEnabled = !isBusy;
            ButtonCancel.IsEnabled = isBusy;

            ProgressBarStep.IsIndeterminate = isBusy;
            ProgressBarStep.Value = 0;
            TaskbarItemInfo.ProgressState = isBusy ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.None;

            if (isBusy)
            {
                TextBlockStatus.Text = "Starting...";
            }
            else
            {
                Title = _baseTitle;
            }
        }

        private void OnStepStarted(int stepNumber, int stepCount, string stepName)
        {
            _stepNumber = stepNumber;
            _stepCount = stepCount;
            _stepName = stepName;
            UpdateProgress(null);
        }

        private void OnProgressChanged(double? percent)
        {
            Dispatcher.InvokeAsync(() => UpdateProgress(percent));
        }

        private void UpdateProgress(double? percent)
        {
            if (!CommandQueue.IsRunning)
            {
                ProgressBarStep.IsIndeterminate = false;
                ProgressBarStep.Value = 0;
                return;
            }

            string status = $"Step {_stepNumber}/{_stepCount}: {_stepName}";
            Title = $"[{_stepNumber}/{_stepCount}] {_baseTitle}";

            if (percent.HasValue)
            {
                status += $" - {percent.Value:0}%";
                ProgressBarStep.IsIndeterminate = false;
                ProgressBarStep.Value = percent.Value;
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
            }
            else
            {
                ProgressBarStep.IsIndeterminate = true;
            }

            if (_stepCount > 0)
            {
                double overall = (_stepNumber - 1 + (percent ?? 0) / 100.0) / _stepCount;
                TaskbarItemInfo.ProgressValue = Math.Max(0, Math.Min(1, overall));
                if (!percent.HasValue)
                {
                    TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                }
            }

            TextBlockStatus.Text = status;
        }

        private void OnMessageLogged(string line)
        {
            // BeginInvoke-style: tool output threads must never wait for the UI.
            Dispatcher.InvokeAsync(() =>
            {
                TextBoxLog.AppendText(_logHasText ? Environment.NewLine + line : line);
                _logHasText = true;
                TextBoxLog.ScrollToEnd();
            });
        }

        #endregion

        #region Helpers

        private void LoadDrivers()
        {
            ComboBoxDriversDirectory.DisplayMemberPath = "Label";
            ComboBoxDriversDirectory.Items.Add(new CustomComboBoxItem { Label = "None", FullPath = "" });

            foreach (string driver in FileUtils.GetArchiveFiles(AppPaths.DriversDirectory))
            {
                string driverLabel = Path.GetFileNameWithoutExtension(driver);
                ComboBoxDriversDirectory.Items.Add(new CustomComboBoxItem { Label = driverLabel, FullPath = driver });
                if (string.Equals(_appSettings.Drivers, driverLabel, StringComparison.OrdinalIgnoreCase))
                {
                    ComboBoxDriversDirectory.SelectedIndex = ComboBoxDriversDirectory.Items.Count - 1;
                }
            }

            if (ComboBoxDriversDirectory.SelectedIndex == -1)
            {
                ComboBoxDriversDirectory.SelectedIndex = 0;
            }

            if (ComboBoxDriversDirectory.Items.Count == 1)
            {
                Logger.Log($"No driver packs found in {AppPaths.DriversDirectory}.");
            }
        }

        private string GetOutputDirectory()
        {
            string output = (_appSettings.OutputDirectory ?? string.Empty).Trim().Trim('"');
            return string.IsNullOrEmpty(output) ? (_appSettings.WorkingDirectory ?? string.Empty).Trim().Trim('"') : output;
        }

        private bool ValidateIso(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowError($"Select the {name} ISO first.");
                return false;
            }

            if (!File.Exists(path))
            {
                ShowError($"The {name} ISO was not found:\n{path}");
                return false;
            }

            return true;
        }

        private bool TryGetFullDirectoryPath(string path, string name, out string fullPath)
        {
            fullPath = null;
            path = (path ?? string.Empty).Trim().Trim('"');

            if (string.IsNullOrEmpty(path))
            {
                ShowError($"Select a {name} first.");
                return false;
            }

            try
            {
                if (!Path.IsPathRooted(path))
                {
                    throw new ArgumentException("not an absolute path");
                }
                fullPath = Path.GetFullPath(path).TrimEnd('\\');
                if (fullPath.EndsWith(":"))
                {
                    fullPath += "\\";
                }
                return true;
            }
            catch (Exception ex)
            {
                ShowError($"The {name} \"{path}\" is not valid ({ex.Message}). Use a full path such as C:\\AM5.");
                return false;
            }
        }

        private void SelectFile(string currentPath, Action<string> onFileSelected)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ISO Files (*.iso)|*.iso|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true,
            };

            string currentDirectory = SafeGetDirectoryName(currentPath);
            if (!string.IsNullOrEmpty(currentDirectory) && Directory.Exists(currentDirectory))
            {
                dialog.InitialDirectory = currentDirectory;
            }

            if (dialog.ShowDialog(this) == true)
            {
                Logger.Log($"Selected file: {dialog.FileName}");
                onFileSelected?.Invoke(dialog.FileName);
            }
        }

        private void SelectDirectory(string currentPath, Action<string> onDirectorySelected)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = true })
            {
                if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
                {
                    dialog.SelectedPath = currentPath;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    Logger.Log($"Selected directory: {dialog.SelectedPath}");
                    onDirectorySelected?.Invoke(dialog.SelectedPath);
                }
            }
        }

        private static string SafeGetDirectoryName(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path.Trim().Trim('"'));
            }
            catch
            {
                return null;
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
                Logger.Error(ex.Message);
                ShowError(ex.Message);
            }
        }

        private static string FormatSize(long bytes)
        {
            return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
                : $"{duration.Minutes} min {duration.Seconds} s";
        }

        private static bool Confirm(string message)
        {
            return ThemedMessageBox.Show(message, AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private static void ShowError(string message, string title = AppTitle)
        {
            ThemedMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ExitApplication()
        {
            _appSettings?.Save();
            Application.Current.Shutdown();
        }

        #endregion

        #region Window events

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (CommandQueue.IsRunning)
            {
                e.Cancel = true;
                if (_closeRequested)
                {
                    return;
                }

                if (Confirm("An operation is still running. Cancel it and exit?\n\nIf an image is mounted it will be unmounted first, which can take a minute."))
                {
                    _closeRequested = true;
                    TextBlockStatus.Text = "Canceling, the window closes when clean-up has finished...";
                    CommandQueue.Cancel();
                }
                return;
            }

            if (WindowState == WindowState.Normal)
            {
                _appSettings.WindowLeft = Left;
                _appSettings.WindowTop = Top;
            }
            _appSettings?.Save();

            Logger.MessageLogged -= OnMessageLogged;
            Logger.ProgressChanged -= OnProgressChanged;
            CommandQueue.StepStarted -= OnStepStarted;
        }

        private void ComboBoxDriversDirectory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ComboBoxDriversDirectory?.SelectedItem as CustomComboBoxItem;
            if (selectedItem != null)
            {
                _appSettings.Drivers = selectedItem.Label;
            }
        }

        private void Window_Initialized(object sender, EventArgs e)
        {
            if (_appSettings.WindowLeft == -1 || _appSettings.WindowTop == -1)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            // The old check used the window handle before it existed, so it always compared against the
            // primary screen (and mixed pixels with DPI-independent units). Use the whole virtual desktop.
            double left = SystemParameters.VirtualScreenLeft;
            double top = SystemParameters.VirtualScreenTop;
            double right = left + SystemParameters.VirtualScreenWidth;
            double bottom = top + SystemParameters.VirtualScreenHeight;

            bool visible = _appSettings.WindowLeft >= left && _appSettings.WindowLeft + 100 <= right &&
                           _appSettings.WindowTop >= top && _appSettings.WindowTop + 50 <= bottom;

            if (visible)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _appSettings.WindowLeft;
                Top = _appSettings.WindowTop;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        #endregion
    }
}
