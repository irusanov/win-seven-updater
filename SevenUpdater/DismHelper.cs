using AdonisUI.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SevenUpdater
{
    internal static class DismHelper
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

        public class DismImageInfo
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }

            public override string ToString() => $"{Index}: {Name}";
        }

        private static Task RunDismAsync(string arguments, string description, CancellationToken cancellationToken)
        {
            Logger.Log($"Starting: {description}");
            // /English keeps the output parseable and the log readable on localized Windows.
            return RunAndLogCompletion(
                ProcessRunner.RunCheckedAsync("dism.exe", "/English " + arguments, "DISM " + description, cancellationToken, line => Logger.LogTool("[DISM]", line)),
                description);
        }

        private static async Task RunAndLogCompletion(Task task, string description)
        {
            await task.ConfigureAwait(false);
            Logger.Log($"Completed: {description}");
        }

        public static async Task<List<DismImageInfo>> GetWimInfoAsync(string wimFilePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(wimFilePath))
            {
                throw new FileNotFoundException($"install.wim not found: {wimFilePath}. Is this a Windows 7 installation ISO?", wimFilePath);
            }

            Logger.Log($"Reading editions from: {wimFilePath}");
            var images = new List<DismImageInfo>();
            DismImageInfo current = null;

            Action<string> parse = line =>
            {
                string trimmed = line.Trim();
                string value;
                if (TryGetValue(trimmed, "Index", out value))
                {
                    int index;
                    current = int.TryParse(value, out index) ? new DismImageInfo { Index = index } : null;
                    if (current != null)
                    {
                        images.Add(current);
                    }
                }
                else if (current != null && TryGetValue(trimmed, "Name", out value))
                {
                    current.Name = value;
                }
                else if (current != null && TryGetValue(trimmed, "Description", out value))
                {
                    current.Description = value;
                }
            };

            await ProcessRunner.RunCheckedAsync(
                "dism.exe",
                $"/English /Get-WimInfo /WimFile:\"{wimFilePath}\"",
                "DISM Get-WimInfo",
                cancellationToken,
                parse).ConfigureAwait(false);

            foreach (DismImageInfo image in images)
            {
                if (string.IsNullOrEmpty(image.Name))
                {
                    image.Name = "Image " + image.Index;
                }
                Logger.Log($"  {image.Index}: {image.Name}");
            }

            return images;
        }

        private static bool TryGetValue(string line, string key, out string value)
        {
            value = null;
            int colon = line.IndexOf(':');
            if (colon <= 0 || !string.Equals(line.Substring(0, colon).Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            value = line.Substring(colon + 1).Trim();
            return true;
        }

        public static Task MountImageAsync(string imagePath, string mountPath, int index, bool optimize, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(mountPath);
            string optimizeOption = optimize ? " /Optimize" : string.Empty;
            return RunDismAsync(
                $"/Mount-Image /ImageFile:\"{imagePath}\" /Index:{index} /MountDir:\"{mountPath}\"{optimizeOption}",
                "Mount image",
                cancellationToken);
        }

        public static Task UnmountImageAsync(string mountPath, bool commitChanges, CancellationToken cancellationToken)
        {
            string commitOption = commitChanges ? "/Commit" : "/Discard";
            return RunDismAsync(
                $"/Unmount-Image /MountDir:\"{mountPath}\" {commitOption}",
                commitChanges ? "Unmount image (commit changes)" : "Unmount image (discard changes)",
                cancellationToken);
        }

        public static Task CleanupMountpointsAsync(CancellationToken cancellationToken)
        {
            return RunDismAsync("/Cleanup-Mountpoints", "Clean up stale mount points", cancellationToken);
        }

        public static Task AddDriverAsync(string imagePath, string driverPath, bool recurse, string logPath, CancellationToken cancellationToken)
        {
            string recurseOption = recurse ? " /Recurse" : string.Empty;
            string logOption = string.IsNullOrEmpty(logPath) ? string.Empty : $" /LogPath:\"{logPath}\"";
            return RunDismAsync(
                $"/Image:\"{imagePath}\" /Add-Driver /Driver:\"{driverPath}\"{recurseOption} /ForceUnsigned{logOption}",
                "Add drivers",
                cancellationToken);
        }

        public static async Task ExportImageAsync(string imagePath, string destinationPath, int index, CancellationToken cancellationToken)
        {
            // DISM appends to an existing destination WIM. A temp.wim left over from an aborted run
            // would make the wrong edition end up at index 1.
            FileUtils.DeleteFile(destinationPath);
            await RunDismAsync(
                $"/Export-Image /SourceImageFile:\"{imagePath}\" /SourceIndex:{index} /DestinationImageFile:\"{destinationPath}\"",
                "Export edition",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// If install.wim contains several editions, asks which one to keep and reduces the WIM to that
        /// edition (so it is always index 1 afterwards). Returns the name of the kept edition.
        /// </summary>
        public static async Task<string> SelectEditionAsync(string wimFilePath, string tempWimPath, string preferredEdition, CancellationToken cancellationToken)
        {
            List<DismImageInfo> images = await GetWimInfoAsync(wimFilePath, cancellationToken).ConfigureAwait(false);
            if (images.Count == 0)
            {
                throw new InvalidOperationException("No Windows editions were found in install.wim.");
            }

            if (images.Count == 1)
            {
                Logger.Log($"Single edition image: {images[0].Name}");
                return images[0].Name;
            }

            DismImageInfo selected = null;
            Application.Current.Dispatcher.Invoke(() => selected = ShowEditionDialog(images, preferredEdition));
            if (selected == null)
            {
                Logger.Warn("No edition selected.");
                throw new OperationCanceledException("Edition selection was canceled.");
            }

            Logger.Log($"Selected edition: {selected.Index}: {selected.Name}");
            await ExportImageAsync(wimFilePath, tempWimPath, selected.Index, cancellationToken).ConfigureAwait(false);
            FileUtils.DeleteFile(wimFilePath);
            await FileUtils.MoveFileAsync(tempWimPath, wimFilePath).ConfigureAwait(false);
            return selected.Name;
        }

        private static DismImageInfo ShowEditionDialog(List<DismImageInfo> images, string preferredEdition)
        {
            var window = new AdonisWindow
            {
                Title = "Select Windows edition",
                Width = 360,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.Height,
            };

            var label = new TextBlock
            {
                Text = "install.wim contains several editions. Choose the one to put on the new ISO:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 10, 10, 0)
            };

            var comboBox = new ComboBox
            {
                Margin = new Thickness(10),
                ItemsSource = images,
            };

            // Preselect the edition used last time, otherwise the last one (usually Ultimate).
            DismImageInfo preselected = images.FirstOrDefault(i => string.Equals(i.Name, preferredEdition, StringComparison.OrdinalIgnoreCase))
                ?? images.FirstOrDefault(i => i.Name.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? images.Last();
            comboBox.SelectedItem = preselected;

            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(5, 5, 5, 10), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 75, Margin = new Thickness(5, 5, 10, 10), IsCancel = true };
            okButton.Click += (sender, e) => window.DialogResult = true;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel();
            panel.Children.Add(label);
            panel.Children.Add(comboBox);
            panel.Children.Add(buttons);
            window.Content = panel;

            return window.ShowDialog() == true ? comboBox.SelectedItem as DismImageInfo : null;
        }
    }
}
