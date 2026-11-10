#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YMusic.WinUI.Models;

public sealed class CustomTheme
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("baseTheme")]
    public string BaseTheme { get; set; } = "Dark"; // "Dark", "Light", "Default"

    [JsonPropertyName("backdrop")]
    public string Backdrop { get; set; } = "Mica"; // "Mica", "MicaAlt", "Acrylic", "Solid"

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#FFEAB308";

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#FF121215";

    [JsonPropertyName("cardColor")]
    public string CardColor { get; set; } = "#FF1C1C21";

    [JsonPropertyName("secondaryCardColor")]
    public string SecondaryCardColor { get; set; } = "#FF26262D";

    [JsonPropertyName("backgroundImage")]
    public string? BackgroundImage { get; set; }

    [JsonPropertyName("backgroundOpacity")]
    public double BackgroundOpacity { get; set; } = 0.25;

    [JsonPropertyName("overrides")]
    public Dictionary<string, string>? Overrides { get; set; }

    // Runtime metadata
    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string DirectoryPath => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetDirectoryName(FilePath) ?? string.Empty;

    [JsonIgnore]
    public string? ResolvedBackgroundImagePath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BackgroundImage)) return null;
            if (Path.IsPathRooted(BackgroundImage) && File.Exists(BackgroundImage))
            {
                return BackgroundImage;
            }
            if (!string.IsNullOrEmpty(DirectoryPath))
            {
                string combined = Path.Combine(DirectoryPath, BackgroundImage);
                if (File.Exists(combined)) return combined;
            }
            return null;
        }
    }

    [JsonIgnore]
    public string? ResolvedPreviewImagePath
    {
        get
        {
            if (string.IsNullOrEmpty(DirectoryPath)) return null;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            {
                string previewPath = Path.Combine(DirectoryPath, $"preview{ext}");
                if (File.Exists(previewPath)) return previewPath;
            }
            return ResolvedBackgroundImagePath;
        }
    }

    [JsonIgnore]
    public bool IsCustom => true;
}

public sealed partial class CommunityThemeItem : ObservableObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("previewUrl")]
    public string PreviewUrl { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#FFEAB308";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyPropertyChangedFor(nameof(ActionButtonGlyph))]
    [NotifyPropertyChangedFor(nameof(ActionButtonOpacity))]
    [NotifyPropertyChangedFor(nameof(CanAction))]
    [JsonIgnore]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyPropertyChangedFor(nameof(ActionButtonGlyph))]
    [NotifyPropertyChangedFor(nameof(ActionButtonOpacity))]
    [NotifyPropertyChangedFor(nameof(CanAction))]
    [JsonIgnore]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAction))]
    [JsonIgnore]
    private bool _isInstalling;

    [JsonIgnore]
    public string ActionButtonText => IsActive ? "Активна" : IsInstalled ? "Применить" : "Скачать";

    [JsonIgnore]
    public string ActionButtonGlyph => IsActive ? "\uE73E" : IsInstalled ? "\uE768" : "\uE896";

    [JsonIgnore]
    public double ActionButtonOpacity => IsActive ? 0.6 : 1.0;

    [JsonIgnore]
    public bool CanAction => !IsActive && !IsInstalling;
}
