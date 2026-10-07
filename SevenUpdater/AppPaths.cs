using System;
using System.IO;

namespace SevenUpdater
{
    /// <summary>
    /// All paths of bundled tools and data, resolved relative to the executable.
    /// Relative paths broke whenever the elevated process started with a different
    /// current directory (e.g. C:\Windows\System32 when launched from some shells).
    /// </summary>
    internal static class AppPaths
    {
        public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        public static string BinDirectory => Path.Combine(BaseDirectory, "bin");
        public static string UpdatesDirectory => Path.Combine(BaseDirectory, "updates");
        public static string DriversDirectory => Path.Combine(BaseDirectory, "drivers");
        public static string AcpiDirectory => Path.Combine(BaseDirectory, "acpi");

        public static string SevenZipExe => Path.Combine(BinDirectory, "7za.exe");
        public static string OscdimgExe => Path.Combine(BinDirectory, "oscdimg.exe");
        public static string UpdatePackCheckerExe => Path.Combine(UpdatesDirectory, "UpdatePack7R2+.exe");
        public static string AcpiArchive => Path.Combine(AcpiDirectory, "WIN7_A5_FIX_ACPI.7z");

        public static string SettingsFile => Path.Combine(BaseDirectory, "settings.xml");
        public static string LogFile => Path.Combine(BaseDirectory, "output.log");
    }

    /// <summary>
    /// Layout of the working directory. Only these sub-items are ever deleted by the app,
    /// so pointing the working directory at a folder with other content is safe.
    /// </summary>
    internal sealed class WorkingPaths
    {
        public WorkingPaths(string workingDirectory)
        {
            Root = workingDirectory;
        }

        public string Root { get; }
        public string Win7 => Path.Combine(Root, "win7");
        public string Win10 => Path.Combine(Root, "win10");
        public string Mount => Path.Combine(Root, "offline");
        public string Drivers => Path.Combine(Root, "drivers");
        public string Acpi => Path.Combine(Root, "acpi");
        public string Temp => Path.Combine(Root, "temp");
        public string TempWim => Path.Combine(Root, "temp.wim");
        public string InstallWim => Path.Combine(Win7, "sources", "install.wim");
        public string DriversLog => Path.Combine(Root, "dism-drivers.log");

        /// <summary>Intermediate directories that can be removed safely (mount dir is handled separately).</summary>
        public string[] IntermediateDirectories => new[] { Win7, Win10, Drivers, Acpi, Temp };
    }
}
