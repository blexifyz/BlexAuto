using System.IO;
using System.Text.Json;

namespace BlexAutoClicker.Services
{
    public class FastFlagService
    {
        private readonly string _settingsPath;

        public FastFlagService()
        {
            _settingsPath = FindSettingsPath();
        }

        private static string? FindRobloxVersionFolder()
        {
            var versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
            if (!Directory.Exists(versions)) return null;

            var dirs = Directory.GetDirectories(versions, "version-*");
            if (dirs.Length == 0) return null;

            return dirs.OrderByDescending(d =>
            {
                var fi = new FileInfo(Path.Combine(d, "RobloxPlayerBeta.exe"));
                return fi.Exists ? fi.LastWriteTime : DateTime.MinValue;
            }).FirstOrDefault(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")));
        }

        private static string FindSettingsPath()
        {
            var folder = FindRobloxVersionFolder();
            if (folder == null) return "";
            var cs = Path.Combine(folder, "ClientSettings");
            if (!Directory.Exists(cs)) Directory.CreateDirectory(cs);
            return Path.Combine(cs, "ClientAppSettings.json");
        }

        public bool IsRobloxFound => !string.IsNullOrEmpty(_settingsPath) && Directory.Exists(Path.GetDirectoryName(_settingsPath));

        private Dictionary<string, JsonElement>? LoadRaw()
        {
            if (!File.Exists(_settingsPath)) return null;
            try
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            }
            catch { return null; }
        }

        private void SaveRaw(Dictionary<string, object> dict)
        {
            try
            {
                if (string.IsNullOrEmpty(_settingsPath)) return;
                var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        public int GetFpsUnlock()
        {
            var raw = LoadRaw();
            if (raw == null || !raw.TryGetValue("DFIntMaxFPS", out var je)) return 0;
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var v)) return v;
            return 0;
        }

        public void SetFpsUnlock(int fps)
        {
            var raw = new Dictionary<string, object>();
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(_settingsPath));
                    if (existing != null) raw = existing;
                }
                catch { }
            }
            if (fps <= 0) raw.Remove("DFIntMaxFPS");
            else raw["DFIntMaxFPS"] = fps;
            SaveRaw(raw);
        }

        private bool GetBoolFlag(string key)
        {
            var raw = LoadRaw();
            if (raw == null || !raw.TryGetValue(key, out var je)) return false;
            return je.ValueKind == JsonValueKind.True;
        }

        private void SetBoolFlag(string key, bool enable)
        {
            var raw = new Dictionary<string, object>();
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(_settingsPath));
                    if (existing != null) raw = existing;
                }
                catch { }
            }
            if (enable) raw[key] = true;
            else raw.Remove(key);
            SaveRaw(raw);
        }

        private bool GetIntFlagAsBool(string key)
        {
            var raw = LoadRaw();
            if (raw == null || !raw.TryGetValue(key, out var je)) return false;
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var v))
                return v == 0;
            return false;
        }

        private void SetIntFlagAsBool(string key, bool enable)
        {
            var raw = new Dictionary<string, object>();
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(_settingsPath));
                    if (existing != null) raw = existing;
                }
                catch { }
            }
            if (enable) raw[key] = 0;
            else raw.Remove(key);
            SaveRaw(raw);
        }

        public bool DisablePostFx { get => GetBoolFlag("FFlagDisablePostFx"); set => SetBoolFlag("FFlagDisablePostFx", value); }
        public bool DisableShadows { get => GetIntFlagAsBool("FIntRenderShadowIntensity"); set => SetIntFlagAsBool("FIntRenderShadowIntensity", value); }
        public bool DisableClouds { get => GetBoolFlag("FFlagDisableClouds"); set => SetBoolFlag("FFlagDisableClouds", value); }
        public bool DisableWaterReflection { get => GetBoolFlag("FFlagDebugDisableWaterReflection"); set => SetBoolFlag("FFlagDebugDisableWaterReflection", value); }
    }
}
