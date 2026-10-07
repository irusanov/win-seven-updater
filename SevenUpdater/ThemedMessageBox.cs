using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SevenUpdater
{
    /// <summary>
    /// Themed replacement for AdonisUI's MessageBox. Same call shape as System.Windows.MessageBox.
    /// </summary>
    public static class ThemedMessageBox
    {
        public static MessageBoxResult Show(string text, string caption = null, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            Application app = Application.Current;
            if (app == null)
            {
                return MessageBox.Show(text ?? string.Empty, caption ?? string.Empty, buttons, icon);
            }

            if (!app.Dispatcher.CheckAccess())
            {
                return app.Dispatcher.Invoke(() => Show(text, caption, buttons, icon));
            }

            try
            {
                return ShowThemed(app, text ?? string.Empty, caption ?? string.Empty, buttons, icon);
            }
            catch (Exception)
            {
                // Never lose an error message because of a styling problem.
                return MessageBox.Show(text ?? string.Empty, caption ?? string.Empty, buttons, icon);
            }
        }

        private static MessageBoxResult ShowThemed(Application app, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            Window owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
                           ?? (app.MainWindow != null && app.MainWindow.IsVisible ? app.MainWindow : null);

            var window = new ThemedWindow
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = owner == null,
                MinWidth = 320,
                MaxWidth = 560,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            if (owner != null)
            {
                window.Owner = owner;
            }

            MessageBoxResult result = DefaultCloseResult(buttons);

            var body = new Grid { Margin = new Thickness(16, 16, 16, 8) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            PathIcon iconElement = CreateIcon(icon);
            if (iconElement != null)
            {
                body.Children.Add(iconElement);
            }

            var message = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(iconElement != null ? 12 : 0, 0, 0, 0),
            };
            message.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");
            Grid.SetColumn(message, 1);
            body.Children.Add(message);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 8, 11, 16),
            };

            Action<string, MessageBoxResult, bool, bool> addButton = (label, value, isDefault, isCancel) =>
            {
                var button = new Button
                {
                    Content = label,
                    MinWidth = 75,
                    Margin = new Thickness(5, 0, 5, 0),
                    IsDefault = isDefault,
                    IsCancel = isCancel,
                };
                if (isDefault)
                {
                    button.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
                }
                button.Click += (s, e) =>
                {
                    result = value;
                    window.Close();
                };
                buttonPanel.Children.Add(button);
            };

            switch (buttons)
            {
                case MessageBoxButton.OKCancel:
                    addButton("OK", MessageBoxResult.OK, true, false);
                    addButton("Cancel", MessageBoxResult.Cancel, false, true);
                    break;
                case MessageBoxButton.YesNo:
                    addButton("Yes", MessageBoxResult.Yes, true, false);
                    addButton("No", MessageBoxResult.No, false, true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    addButton("Yes", MessageBoxResult.Yes, true, false);
                    addButton("No", MessageBoxResult.No, false, false);
                    addButton("Cancel", MessageBoxResult.Cancel, false, true);
                    break;
                default:
                    addButton("OK", MessageBoxResult.OK, true, true);
                    break;
            }

            var root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            root.Children.Add(buttonPanel);
            root.Children.Add(body);
            window.Content = root;

            if (icon == MessageBoxImage.Error || icon == MessageBoxImage.Warning)
            {
                System.Media.SystemSounds.Exclamation.Play();
            }

            window.ShowDialog();
            return result;
        }

        private static MessageBoxResult DefaultCloseResult(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.YesNo:
                    return MessageBoxResult.No;
                case MessageBoxButton.OKCancel:
                case MessageBoxButton.YesNoCancel:
                    return MessageBoxResult.Cancel;
                default:
                    return MessageBoxResult.OK;
            }
        }

        private static PathIcon CreateIcon(MessageBoxImage image)
        {
            string iconKey;
            string brushKey;
            switch (image)
            {
                case MessageBoxImage.Error:
                    iconKey = "IconCloseCircle";
                    brushKey = "ErrorBrush";
                    break;
                case MessageBoxImage.Warning:
                    iconKey = "IconAlert";
                    brushKey = "AlertBrush";
                    break;
                case MessageBoxImage.Question:
                    iconKey = "IconHelpCircle";
                    brushKey = "AccentBrush";
                    break;
                case MessageBoxImage.Information:
                    iconKey = "IconInformation";
                    brushKey = "AccentBrush";
                    break;
                default:
                    return null;
            }

            var icon = new PathIcon
            {
                Width = 32,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Top,
            };
            icon.SetResourceReference(PathIcon.DataProperty, iconKey);
            icon.SetResourceReference(PathIcon.ForegroundProperty, brushKey);
            return icon;
        }
    }
}
