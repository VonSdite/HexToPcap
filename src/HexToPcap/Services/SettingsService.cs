using System;
using System.IO;
using HexToPcap.Core.Models;

namespace HexToPcap.Services
{
    public sealed class SettingsService : ISettingsService
    {
        public AppSettings Load()
        {
            var outputDirectory = UserSettingsStore.Default.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "HexToPcap");
            }

            return new AppSettings
            {
                OutputDirectory = outputDirectory,
                WiresharkPath = UserSettingsStore.Default.WiresharkPath
            };
        }

        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            UserSettingsStore.Default.OutputDirectory = (settings.OutputDirectory ?? string.Empty).Trim();
            UserSettingsStore.Default.WiresharkPath = (settings.WiresharkPath ?? string.Empty).Trim();
            UserSettingsStore.Default.Save();
        }
    }
}

