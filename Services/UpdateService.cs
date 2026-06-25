using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace BlexAutoClicker.Services
{
    public class UpdateService
    {
        private static readonly string RepoOwner = "blexifyz";
        private static readonly string RepoName = "BlexAuto";
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public async void CheckForUpdate()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version == null) return;
                var current = new Version(version.Major, version.Minor, version.Build);

                string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("BlexAuto-updater/1.0");
                var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);
                if (release == null || release.TagName == null) return;

                var tag = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(tag, out var latest)) return;

                if (latest > current)
                {
                    var result = MessageBox.Show(
                        $"Update v{latest} available (current v{current}). Download?",
                        "Blex Auto Update",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        var asset = release.Assets?.Find(a => a.Name == "BlexAuto.exe");
                        if (asset == null)
                        {
                            MessageBox.Show("No BlexAuto.exe found in release.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        if (asset.BrowserDownloadUrl != null)
                            DownloadAndInstall(asset.BrowserDownloadUrl);
                    }
                }
                else
                {
                    MessageBox.Show($"You're up to date (v{current}).", "Blex Auto Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show($"Update check failed: {ex.Message}\n\nMake sure you have internet access.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update check error: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void DownloadAndInstall(string downloadUrl)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "BlexAutoUpdate");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string newExePath = Path.Combine(tempDir, "BlexAuto.exe");
                var resp = await _http.GetAsync(downloadUrl);
                resp.EnsureSuccessStatusCode();
                using (var fs = new FileStream(newExePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await resp.Content.CopyToAsync(fs);

                if (!File.Exists(newExePath))
                {
                    MessageBox.Show("Update file not found after download.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlexAuto.exe");
                string currentDir = Path.GetDirectoryName(currentExe) ?? ".";
                string oldName = "BlexAuto.old.exe";
                string newName = "BlexAuto.new.exe";
                string oldExe = Path.Combine(currentDir, oldName);
                string newLocalExe = Path.Combine(currentDir, newName);
                string logFile = Path.Combine(currentDir, "update.log");
                string updaterPath = Path.Combine(currentDir, "updater.bat");

                // Copy downloaded exe next to current exe
                File.Copy(newExePath, newLocalExe, true);

                // Write a bat file with logging (every step logged to update.log)
                File.WriteAllText(updaterPath,
                    $"@echo off\r\n" +
                    $"echo [%date% %time%] Updater started >> \"{logFile}\"\r\n" +
                    $"echo CurrentExe={currentExe} >> \"{logFile}\"\r\n" +
                    $"echo OldExe={oldExe} >> \"{logFile}\"\r\n" +
                    $"echo NewExe={newLocalExe} >> \"{logFile}\"\r\n" +
                    $"ping -n 3 127.0.0.1 > nul\r\n" +
                    $"echo [%date% %time%] Renaming running exe to .old... >> \"{logFile}\"\r\n" +
                    $"ren \"{currentExe}\" \"{oldName}\" >> \"{logFile}\" 2>&1\r\n" +
                    $"echo [%date% %time%] Renaming .new to exe... >> \"{logFile}\"\r\n" +
                    $"ren \"{newLocalExe}\" \"BlexAuto.exe\" >> \"{logFile}\" 2>&1\r\n" +
                    $"echo [%date% %time%] Starting new exe... >> \"{logFile}\"\r\n" +
                    $"start \"\" \"{currentExe}\" >> \"{logFile}\" 2>&1\r\n" +
                    $"echo [%date% %time%] Cleaning up old exe... >> \"{logFile}\"\r\n" +
                    $":wait\r\n" +
                    $"ping -n 2 127.0.0.1 > nul\r\n" +
                    $"del \"{oldExe}\" >> \"{logFile}\" 2>&1\r\n" +
                    $"if exist \"{oldExe}\" (echo Old exe still locked, retrying... >> \"{logFile}\" & goto wait)\r\n" +
                    $"echo [%date% %time%] Done, deleting self >> \"{logFile}\"\r\n" +
                    $"del \"%~f0\"\r\n");

                var psi = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };
                Process.Start(psi);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }
            [JsonPropertyName("assets")]
            public List<GitHubAsset>? Assets { get; set; }
        }

        private class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
