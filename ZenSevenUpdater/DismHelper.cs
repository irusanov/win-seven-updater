using AdonisUI.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MessageBox = AdonisUI.Controls.MessageBox;

namespace SevenUpdater
{
    internal class DismHelper
    {
        public enum ChecksumAlgorithm
        {
            MD2,
            MD4,
            MD5,
            SHA1,
            SHA256,
            SHA384,
            SHA512
        }

        private static Action<string> _logAction;

        public static void SetLogAction(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public static void Log(string message)
        {
            _logAction?.Invoke(message);
        }

        private static void ExecuteDismCommand(string arguments, string description)
        {
            Log($"Starting: {description}");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) => LogOutput(e.Data, "[DISM]");
                process.ErrorDataReceived += (sender, e) => LogOutput(e.Data, "[DISM ERROR]");

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"DISM command failed with exit code {process.ExitCode}: {arguments}");
                }
            }
            Log($"Completed: {description}\n");
        }

        private static void LogOutput(string data, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(data))
            {
                Log($"{prefix}: {data}");
            }
        }

        public class DismImageInfo
        {
            public string Index { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
        }

        public static List<DismImageInfo> GetWimInfo(string wimFilePath)
        {
            Log($"Retrieving WIM info for: {wimFilePath}");
            var imageInfoList = new List<DismImageInfo>();

            var startInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = $"/Get-WimInfo /WimFile:\"{wimFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                string index = null, name = null, description = null;

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log(e.Data);

                        if (e.Data.StartsWith("Index :"))
                        {
                            if (index != null && name != null && description != null)
                            {
                                imageInfoList.Add(new DismImageInfo { Index = index, Name = name, Description = description });
                            }

                            index = ExtractValue(e.Data, "Index :");
                            name = null;
                            description = null;
                        }
                        else if (e.Data.StartsWith("Name :"))
                        {
                            name = ExtractValue(e.Data, "Name :");
                        }
                        else if (e.Data.StartsWith("Description :"))
                        {
                            description = ExtractValue(e.Data, "Description :");
                        }
                    }
                };

                process.ErrorDataReceived += (sender, e) => LogOutput(e.Data, "[DISM ERROR]");

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (index != null && name != null && description != null)
                {
                    imageInfoList.Add(new DismImageInfo { Index = index, Name = name, Description = description });
                }

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Failed to retrieve WIM info. Exit code: {process.ExitCode}");
                }
            }

            Log($"Retrieved {imageInfoList.Count} images from WIM file.");
            return imageInfoList;
        }

        private static string ExtractValue(string input, string key)
        {
            if (input.StartsWith(key))
            {
                return input.Substring(key.Length).Trim();
            }
            return null;
        }

        public static void MountImage(string imagePath, string mountPath, string index, bool optimize)
        {
            if (!Directory.Exists(mountPath))
            {
                Directory.CreateDirectory(mountPath);
            }

            string optimizeOption = optimize ? "/Optimize" : string.Empty;
            string arguments = $"/Mount-Image /ImageFile:\"{imagePath}\" /Index:{index} /MountDir:\"{mountPath}\" {optimizeOption}";
            ExecuteDismCommand(arguments, "Mount Image");
        }

        public static void UnmountImage(string mountPath, bool commitChanges)
        {
            string commitOption = commitChanges ? "/Commit" : "/Discard";
            string arguments = $"/Unmount-Image /MountDir:\"{mountPath}\" {commitOption}";
            ExecuteDismCommand(arguments, "Unmount Image");
        }

        public static void DeleteImage(string imagePath, int index)
        {
            string arguments = $"/Delete-Image /ImageFile:\"{imagePath}\" /Index:{index} /CheckIntegrity";
            ExecuteDismCommand(arguments, "Delete Image");
        }

        public static void AddDriver(string imagePath, string driverPath, bool recurse = false)
        {
            string recurseOption = recurse ? "/Recurse" : string.Empty;
            string arguments = $"/Image:\"{imagePath}\" /Add-Driver /Driver:\"{driverPath}\" {recurseOption} /forceunsigned";
            ExecuteDismCommand(arguments, "Add Driver");
        }

        public static async Task MountImageAsync(string imagePath, string mountPath, string index, bool optimize, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    MountImage(imagePath, mountPath, index, optimize);
                }
            }, cancellationToken);
        }

        public static async Task UnmountImageAsync(string mountPath, bool commitChanges, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    UnmountImage(mountPath, commitChanges);
                }
            }, cancellationToken);
        }

        public static async Task DeleteImageAsync(string imagePath, int index, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    DeleteImage(imagePath, index);
                }
            }, cancellationToken);
        }

        public static async Task AddDriverAsync(string imagePath, string driverPath, bool recurse, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    AddDriver(imagePath, driverPath, recurse);
                }
            }, cancellationToken);
        }

        public static async Task<List<DismImageInfo>> GetWimInfoAsync(string wimFilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested) return null;
                return GetWimInfo(wimFilePath);
            }, cancellationToken);
        }

        public static async Task ShowWimInfoDialogAsync(string wimFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            var wimInfoList = await GetWimInfoAsync(wimFilePath, cancellationToken);
            if (wimInfoList == null || wimInfoList.Count == 0)
            {
                MessageBox.Show("No WIM images found.", "Error", AdonisUI.Controls.MessageBoxButton.OK, AdonisUI.Controls.MessageBoxImage.Error);
                return;
            }

            if (wimInfoList.Count == 1)
            {
                return;
            }

            var window = new AdonisWindow
            {
                Title = "Select WIM Image",
                Width = 300,
                Height = 200,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                SizeToContent = SizeToContent.Height,
            };

            var comboBox = new ComboBox
            {
                Margin = new Thickness(10),
                DisplayMemberPath = "Name",
                SelectedValuePath = "Index",
                ItemsSource = wimInfoList,
                SelectedIndex = 0
            };

            var button = new Button
            {
                Content = "OK",
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Width = 75
            };

            button.Click += (sender, e) => window.DialogResult = true;

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(comboBox);
            stackPanel.Children.Add(button);

            window.Content = stackPanel;

            if (window.ShowDialog() == true)
            {
                var selectedIndex = comboBox.SelectedValue?.ToString();
                if (!string.IsNullOrEmpty(selectedIndex))
                {
                    var index = int.Parse(selectedIndex);
                    var tempFilePath = Path.Combine(workingDirectory, "temp.wim");
                    await ExtractImageAsync(wimFilePath, tempFilePath, index, cancellationToken);
                    await FileUtils.DeleteFileAsync(wimFilePath);
                    await FileUtils.MoveFileAsync(tempFilePath, wimFilePath);
                }
            }
        }
        public static void ExtractImage(string imagePath, string destinationPath, int index)
        {
            string arguments = $"/Export-Image /SourceImageFile:\"{imagePath}\" /SourceIndex:{index} /DestinationImageFile:\"{destinationPath}\"";
            ExecuteDismCommand(arguments, "Extract Image");
        }

        public static async Task ExtractImageAsync(string imagePath, string destinationPath, int index, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    ExtractImage(imagePath, destinationPath, index);
                }
            }, cancellationToken);
        }
    }
}
