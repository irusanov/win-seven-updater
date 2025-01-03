using AdonisUI.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace ZenSevenUpdater
{
    public sealed class AppSettings
    {
        private const int VERSION_MAJOR = 1;
        private const int VERSION_MINOR = 0;

        private const string filename = "settings.xml";

        public AppSettings()
        {}

        public AppSettings Create()
        {
            //Version = $"{VERSION_MAJOR}.{VERSION_MINOR}";

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

        public string Windows7IsoPath { get; set; } = string.Empty;
        public string Windows10IsoPath { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = @"C:\AM5";
        public string IsoLabel { get; set; } = "BOOTABLEISO";
        public string Version { get; set; } = $"{VERSION_MAJOR}.{VERSION_MINOR}";
        public double Width { get; set; } = 0;
        public double Height { get; set; } = 0;
        public int WindowLeft { get; set; } = -1;
        public int WindowTop { get; set; } = -1;
    }
}
