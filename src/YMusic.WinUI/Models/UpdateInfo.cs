#nullable enable

using System;

namespace YMusic.WinUI.Models;

public sealed class UpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "0.9.3";
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string? DownloadUrl { get; set; }
    public string? AssetName { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string HtmlUrl { get; set; } = "https://github.com/pixiemays/WinYandex/releases/latest";
}
