using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Xml.Serialization;

namespace SevenUpdater
{
    public class AppSettings : INotifyPropertyChanged
    {
        private const int VERSION_MAJOR = 1;
        private const int VERSION_MINOR = 0;

        private static string filename => AppPaths.SettingsFile;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string windows7IsoPath = string.Empty;
        public string Windows7IsoPath
        {
            get => windows7IsoPath;
            set
            {
                if (windows7IsoPath != value)
                {
                    windows7IsoPath = value;
                    OnPropertyChanged(nameof(Windows7IsoPath));
                }
            }
        }

        private string windows10IsoPath = string.Empty;
        public string Windows10IsoPath
        {
            get => windows10IsoPath;
            set
            {
                if (windows10IsoPath != value)
                {
                    windows10IsoPath = value;
                    OnPropertyChanged(nameof(Windows10IsoPath));
                }
            }
        }

        private string workingDirectory = @"C:\AM5";
        public string WorkingDirectory
        {
            get => workingDirectory;
            set
            {
                if (workingDirectory != value)
                {
                    workingDirectory = value;
                    OnPropertyChanged(nameof(WorkingDirectory));
                }
            }
        }



        private string isoLabel = "AMDSEVEN";
        public string IsoLabel
        {
            get => isoLabel;
            set
            {
                if (isoLabel != value)
                {
                    isoLabel = value;
                    OnPropertyChanged(nameof(IsoLabel));
                }
            }
        }

        private string version = $"{VERSION_MAJOR}.{VERSION_MINOR}";
        public string Version
        {
            get => version;
            set
            {
                if (version != value)
                {
                    version = value;
                    OnPropertyChanged(nameof(Version));
                }
            }
        }

        private double width = 0;
        public double Width
        {
            get => width;
            set
            {
                if (width != value)
                {
                    width = value;
                    OnPropertyChanged(nameof(Width));
                }
            }
        }

        private double height = 0;
        public double Height
        {
            get => height;
            set
            {
                if (height != value)
                {
                    height = value;
                    OnPropertyChanged(nameof(Height));
                }
            }
        }

        private double windowLeft = -1;
        public double WindowLeft
        {
            get => windowLeft;
            set
            {
                if (windowLeft != value)
                {
                    windowLeft = value;
                    OnPropertyChanged(nameof(WindowLeft));
                }
            }
        }

        private double windowTop = -1;
        public double WindowTop
        {
            get => windowTop;
            set
            {
                if (windowTop != value)
                {
                    windowTop = value;
                    OnPropertyChanged(nameof(WindowTop));
                }
            }
        }

        private string drivers = string.Empty;
        public string Drivers
        {
            get => drivers;
            set
            {
                if (drivers != value)
                {
                    drivers = value;
                    OnPropertyChanged(nameof(Drivers));
                }
            }
        }

        private bool checkForUpdaterPackUpdates = true;

        public bool CheckForUpdaterPackUpdates
        {
            get => checkForUpdaterPackUpdates;
            set
            {
                if (checkForUpdaterPackUpdates != value)
                {
                    checkForUpdaterPackUpdates = value;
                    OnPropertyChanged(nameof(CheckForUpdaterPackUpdates));
                }
            }
        }

        private bool includeModdedAcpi = false;
        public bool IncludeModdedAcpi
        {
            get => includeModdedAcpi;
            set
            {
                if (includeModdedAcpi != value)
                {
                    includeModdedAcpi = value;
                    OnPropertyChanged(nameof(IncludeModdedAcpi));
                }
            }
        }

        private string outputDirectory = "C:\\AM5";
        public string OutputDirectory
        {
            get => outputDirectory;
            set
            {
                if (outputDirectory != value)
                {
                    outputDirectory = value;
                    OnPropertyChanged(nameof(OutputDirectory));
                }
            }
        }

        private bool includeUpdates = true;
        public bool IncludeUpdates
        {
            get => includeUpdates;
            set
            {
                if (includeUpdates != value)
                {
                    includeUpdates = value;
                    OnPropertyChanged(nameof(IncludeUpdates));
                }
            }
        }



        private string selectedEdition = string.Empty;
        /// <summary>Last Windows 7 edition picked from install.wim; preselected next time.</summary>
        public string SelectedEdition
        {
            get => selectedEdition;
            set
            {
                if (selectedEdition != value)
                {
                    selectedEdition = value;
                    OnPropertyChanged(nameof(SelectedEdition));
                }
            }
        }

        public AppSettings() { }

        public AppSettings Create()
        {
            Save();
            return this;
        }

        public AppSettings Reset() => Create();

        public AppSettings Load()
        {
            if (!File.Exists(filename))
            {
                return Create();
            }

            AppSettings loaded = null;
            try
            {
                using (StreamReader sr = new StreamReader(filename))
                {
                    XmlSerializer xmls = new XmlSerializer(typeof(AppSettings));
                    loaded = xmls.Deserialize(sr) as AppSettings;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (loaded == null)
            {
                // The reader is closed at this point, so the defaults can be written back.
                ThemedMessageBox.Show(
                    "Invalid settings file!\nSettings will be reset to defaults.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return Create();
            }

            // Older or hand-edited files can contain empty elements.
            loaded.Drivers = loaded.Drivers ?? string.Empty;
            loaded.IsoLabel = string.IsNullOrWhiteSpace(loaded.IsoLabel) ? "AMDSEVEN" : loaded.IsoLabel.Trim();
            loaded.SelectedEdition = loaded.SelectedEdition ?? string.Empty;
            loaded.Version = $"{VERSION_MAJOR}.{VERSION_MINOR}";
            return loaded;
        }

        public void Save()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(filename))
                {
                    XmlSerializer xmls = new XmlSerializer(typeof(AppSettings));
                    xmls.Serialize(sw, this);
                }
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show(
                    "Could not save settings to file!\n" + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
