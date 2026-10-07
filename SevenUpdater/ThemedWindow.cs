using System;
using System.Windows;
using System.Windows.Input;

namespace SevenUpdater
{
    /// <summary>
    /// Window with the themed title bar (replaces AdonisUI's AdonisWindow).
    /// The look is defined by the ThemedWindow style in Themes/Controls.xaml (WindowChrome + template).
    /// </summary>
    public class ThemedWindow : Window
    {
        public ThemedWindow()
        {
            // Implicit styles are looked up by the concrete type (e.g. MainWindow), so point at the base style explicitly.
            SetResourceReference(StyleProperty, typeof(ThemedWindow));

            CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (s, e) => SystemCommands.CloseWindow(this)));
            CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (s, e) => SystemCommands.MinimizeWindow(this)));
            CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (s, e) => SystemCommands.MaximizeWindow(this)));
            CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (s, e) => SystemCommands.RestoreWindow(this)));
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // WindowChrome + SizeToContent can leave an unpainted strip after the first layout pass.
            if (SizeToContent != SizeToContent.Manual)
            {
                InvalidateMeasure();
            }
        }
    }
}
