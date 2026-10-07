using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SevenUpdater
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Elevated processes can start in C:\Windows\System32; make sure anything relative resolves next to the exe.
            Directory.SetCurrentDirectory(AppPaths.BaseDirectory);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Logger.Error("Unhandled exception: " + (args.ExceptionObject as Exception)?.ToString());
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Logger.Error("Unobserved task exception: " + Logger.Describe(args.Exception));
                args.SetObserved();
            };

            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("Unexpected error: " + e.Exception);
            MessageBox.Show(
                "An unexpected error occurred:\n" + Logger.Describe(e.Exception) + "\n\nDetails were written to output.log.",
                "Seven Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
