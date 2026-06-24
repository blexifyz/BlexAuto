using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
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
            }
            catch { }
        }

        private async void DownloadAndInstall(string downloadUrl)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "BlexAutoUpdate");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string zipPath = Path.Combine(tempDir, "update.zip");
                var resp = await _http.GetAsync(downloadUrl);
                resp.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await resp.Content.CopyToAsync(fs);

                ZipFile.ExtractToDirectory(zipPath, tempDir);
                string exePath = Path.Combine(tempDir, "BlexAuto.exe");
                if (!File.Exists(exePath))
                {
                    MessageBox.Show("Update file not found after extraction.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string updaterPath = Path.Combine(tempDir, "updater.bat");
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(exeDir, "BlexAuto.exe");

                File.WriteAllText(updaterPath,
                    $"@echo off\r\n" +
                    $"timeout /t 1 /nobreak > nul\r\n" +
                    $"copy /y \"{exePath}\" \"{currentExe}\" > nul\r\n" +
                    $"start \"\" \"{currentExe}\"\r\n" +
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
            public string? TagName { get; set; }
            public List<GitHubAsset>? Assets { get; set; }
        }

        private class GitHubAsset
        {
            public string? Name { get; set; }
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
