#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class UpdateService : IUpdateService
{
    private const string RepoOwner = "pixiemays";
    private const string RepoName = "WinYandex";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WinYandex-App/1.0");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIdx = infoVersion.IndexOf('+');
                return plusIdx > 0 ? infoVersion.Substring(0, plusIdx) : infoVersion;
            }
            var ver = assembly.GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.9.0";
        }
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        string currentVer = CurrentVersion;
        var info = new UpdateInfo
        {
            CurrentVersion = currentVer,
            IsUpdateAvailable = false
        };

        try
        {
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return info;
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string cleanTag = tagName.TrimStart('v', 'V');
            info.LatestVersion = cleanTag;

            if (root.TryGetProperty("name", out var nameProp))
            {
                info.ReleaseName = nameProp.GetString() ?? tagName;
            }

            if (root.TryGetProperty("body", out var bodyProp))
            {
                info.ReleaseNotes = bodyProp.GetString() ?? "";
            }

            if (root.TryGetProperty("html_url", out var htmlProp))
            {
                info.HtmlUrl = htmlProp.GetString() ?? info.HtmlUrl;
            }

            if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var pubDate))
            {
                info.PublishedAt = pubDate;
            }

            // Find best asset (prefer .exe installer, fallback to .zip)
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                JsonElement? bestAsset = null;
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAsset = asset;
                        break;
                    }
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && bestAsset == null)
                    {
                        bestAsset = asset;
                    }
                }

                if (bestAsset.HasValue)
                {
                    var a = bestAsset.Value;
                    info.AssetName = a.TryGetProperty("name", out var an) ? an.GetString() : null;
                    info.DownloadUrl = a.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
                    info.FileSizeBytes = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                }
            }

            info.IsUpdateAvailable = IsNewerVersion(cleanTag, currentVer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] CheckForUpdates error: {ex.Message}");
        }

        return info;
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<(double Percentage, long DownloadedBytes, long TotalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("No download URL available for update.");
        }

        string updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic", "Updates");
        Directory.CreateDirectory(updatesDir);

        string fileName = update.AssetName ?? $"WinYandex-v{update.LatestVersion}-Setup.exe";
        string filePath = Path.Combine(updatesDir, fileName);

        using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? update.FileSizeBytes;

        using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
            downloaded += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                double pct = Math.Clamp((double)downloaded / totalBytes * 100.0, 0.0, 100.0);
                progress.Report((pct, downloaded, totalBytes));
            }
        }

        return filePath;
    }

    public void InstallAndRestart(string installerFilePath)
    {
        if (!File.Exists(installerFilePath))
        {
            throw new FileNotFoundException("Installer file was not found", installerFilePath);
        }

        try
        {
            if (installerFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // Run Inno Setup installer
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerFilePath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Portable ZIP - reveal in explorer
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{installerFilePath}\"",
                    UseShellExecute = true
                });
            }

            // Clean shutdown of current app to allow overwriting files
            if (Application.Current != null)
            {
                Application.Current.Exit();
            }
            else
            {
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] InstallAndRestart failed: {ex.Message}");
            throw;
        }
    }

    private static bool IsNewerVersion(string latestStr, string currentStr)
    {
        if (string.IsNullOrWhiteSpace(latestStr)) return false;

        // Try standard Version parse
        if (Version.TryParse(NormalizeVersionString(latestStr), out var latestVer) &&
            Version.TryParse(NormalizeVersionString(currentStr), out var currentVer))
        {
            return latestVer > currentVer;
        }

        // Fallback string comparison
        return !string.Equals(latestStr, currentStr, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersionString(string ver)
    {
        string clean = ver.Trim().TrimStart('v', 'V');
        int dashIdx = clean.IndexOf('-');
        if (dashIdx > 0) clean = clean.Substring(0, dashIdx);

        var parts = clean.Split('.');
        if (parts.Length == 1) return $"{parts[0]}.0.0";
        if (parts.Length == 2) return $"{parts[0]}.{parts[1]}.0";
        return clean;
    }
}
