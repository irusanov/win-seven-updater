using AdonisUI.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace ZenSevenUpdater
{
    public class AppSettings : INotifyPropertyChanged
    {
        private const int VERSION_MAJOR = 1;
        private const int VERSION_MINOR = 0;

        private const string filename = "settings.xml";

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



        private string isoLabel = "BOOTABLEISO";
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



        public AppSettings() { }

        public AppSettings Create()
        {
            Save();
            return this;
        }

        public AppSettings Reset() => Create();

        public AppSettings Load()
        {
            if (File.Exists(filename))
            {
                using (StreamReader sr = new StreamReader(filename))
                {
                    try
                    {
                        XmlSerializer xmls = new XmlSerializer(typeof(AppSettings));
                        return xmls.Deserialize(sr) as AppSettings;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        sr.Close();
                        MessageBox.Show(
                            "Invalid settings file!\nSettings will be reset to defaults.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return Create();
                    }
                }
            }
            else
            {
                return Create();
            }
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
                MessageBox.Show(
                    "Could not save settings to file!",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
