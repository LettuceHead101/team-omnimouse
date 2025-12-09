namespace OmniMouse.Configuration
{
    using System;
    using System.IO;
    using System.Text.Json;

    public class AppSettings
    {
        public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniMouse");

        public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

        public string DefaultDownloadFolder { get; set; } = string.Empty;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        // Ensure not empty and exists
                        if (string.IsNullOrWhiteSpace(loaded.DefaultDownloadFolder))
                        {
                            loaded.DefaultDownloadFolder = GetDefaultDownloadsPath();
                        }

                        return loaded;
                    }
                }
            }
            catch
            {
                // ignore and fallback to defaults
            }

            return new AppSettings
            {
                DefaultDownloadFolder = GetDefaultDownloadsPath(),
            };
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // best-effort; ignore persistence errors
            }
        }

        private static string GetDefaultDownloadsPath()
        {
            // Prefer user's Downloads; fallback to Documents if missing
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloads = Path.Combine(userProfile, "Downloads");
            if (!string.IsNullOrWhiteSpace(downloads) && Directory.Exists(downloads))
            {
                return downloads;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents))
            {
                return documents;
            }

            // Last resort: app base Downloads
            var appDownloads = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
            Directory.CreateDirectory(appDownloads);
            return appDownloads;
        }
    }
}
