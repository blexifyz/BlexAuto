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
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

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

                // Start the NEW exe with the old exe path + old PID
                // The new exe will wait for this process to exit, then copy itself over the old exe
                string currentExe = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(currentExe))
                {
                    MessageBox.Show("Cannot determine current exe path.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                int currentPid = Environment.ProcessId;
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = newExePath,
                    Arguments = $"--apply-update \"{currentExe}\" {currentPid}",
                    UseShellExecute = true
                });
                if (proc == null)
                {
                    MessageBox.Show("Update process failed to start. The download may have been blocked by antivirus or SmartScreen.\n\nTry downloading manually from the releases page.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
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
