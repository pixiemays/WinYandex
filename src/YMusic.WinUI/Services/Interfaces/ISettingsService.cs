#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface ISettingsService
{
    AudioQualityPreference PreferredQuality { get; set; }
    bool GaplessPreloadEnabled { get; set; }
    bool DiscordRpcEnabled { get; set; }
    bool MinimizeToTrayOnClose { get; set; }
    bool TrackNotificationsEnabled { get; set; }
    bool FckCensorEnabled { get; set; }
    string AppTheme { get; set; }
    bool AudioCacheEnabled { get; set; }
    int CacheSizeLimitMb { get; set; }

    event EventHandler<string>? SettingChanged;

    void Save();
}
