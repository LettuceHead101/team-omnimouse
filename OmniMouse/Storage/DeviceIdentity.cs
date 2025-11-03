using System;
using System.IO;
using System.Text.Json;

namespace OmniMouse.Storage
{
    public static class DeviceIdentity
    {
        private sealed class IdentityModel { public Guid Id { get; set; } public string? Name { get; set; } }
        private static readonly string s_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniMouse");
        private static readonly string s_file = Path.Combine(s_dir, "identity.json");
        private static IdentityModel? s_cached;

        public static Guid Id => Ensure().Id;
        public static string Name
        {
            get => Ensure().Name ?? Environment.MachineName;
            set
            {
                var m = Ensure(); m.Name = value;
                Save(m);
            }
        }

        private static IdentityModel Ensure()
        {
            if (s_cached != null) return s_cached;
            try
            {
                Directory.CreateDirectory(s_dir);
                if (File.Exists(s_file))
                {
                    var json = File.ReadAllText(s_file);
                    s_cached = JsonSerializer.Deserialize<IdentityModel>(json) ?? new IdentityModel { Id = Guid.NewGuid(), Name = Environment.MachineName };
                }
                else
                {
                    s_cached = new IdentityModel { Id = Guid.NewGuid(), Name = Environment.MachineName };
                    Save(s_cached);
                }
            }
            catch
            {
                s_cached = new IdentityModel { Id = Guid.NewGuid(), Name = Environment.MachineName };
            }
            return s_cached!;
        }

        private static void Save(IdentityModel model)
        {
            try
            {
                Directory.CreateDirectory(s_dir);
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(s_file, json);
            }
            catch { /* best-effort */ }
        }
    }
}