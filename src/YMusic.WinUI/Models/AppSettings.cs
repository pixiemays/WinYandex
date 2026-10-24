#nullable enable

using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class AppSettings
{
    [JsonPropertyName("preferredQuality")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AudioQualityPreference PreferredQuality { get; set; } = AudioQualityPreference.AacPreferred;

    [JsonPropertyName("gaplessPreloadEnabled")]
    public bool GaplessPreloadEnabled { get; set; } = true;

    [JsonPropertyName("discordRpcEnabled")]
    public bool DiscordRpcEnabled { get; set; } = true;

    [JsonPropertyName("minimizeToTrayOnClose")]
    public bool MinimizeToTrayOnClose { get; set; } = true;

    [JsonPropertyName("trackNotificationsEnabled")]
    public bool TrackNotificationsEnabled { get; set; } = true;

    [JsonPropertyName("fckCensorEnabled")]
    public bool FckCensorEnabled { get; set; } = true;

    [JsonPropertyName("appTheme")]
    public string AppTheme { get; set; } = "Dark"; // "Default", "Light", "Dark"

    [JsonPropertyName("appFont")]
    public string AppFont { get; set; } = "Inter"; // "Inter", "System", "YandexSans"

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru"; // "ru" or "en"

    [JsonPropertyName("audioCacheEnabled")]
    public bool AudioCacheEnabled { get; set; } = true;

    [JsonPropertyName("audioCacheLocation")]
    public string AudioCacheLocation { get; set; } = string.Empty;

    [JsonPropertyName("cacheSizeLimitMb")]
    public int CacheSizeLimitMb { get; set; } = 1024;
}
