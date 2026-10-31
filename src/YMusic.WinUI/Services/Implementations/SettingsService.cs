#nullable enable

using System.Diagnostics;
using System.Text.Json;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly IYandexMusicService _musicService;
    private AppSettings _settings = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public event EventHandler<string>? SettingChanged;

    public SettingsService(IYandexMusicService musicService)
    {
        _musicService = musicService;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");

        Load();
        ApplyToServices();
    }

    public AudioQualityPreference PreferredQuality
    {
        get => _settings.PreferredQuality;
        set
        {
            if (_settings.PreferredQuality != value)
            {
                _settings.PreferredQuality = value;
                _musicService.PreferredQuality = value;
                Save();
                NotifyChanged(nameof(PreferredQuality));
            }
        }
    }

    public bool GaplessPreloadEnabled
    {
        get => _settings.GaplessPreloadEnabled;
        set
        {
            if (_settings.GaplessPreloadEnabled != value)
            {
                _settings.GaplessPreloadEnabled = value;
                Save();
                NotifyChanged(nameof(GaplessPreloadEnabled));
            }
        }
    }

    public bool DiscordRpcEnabled
    {
        get => _settings.DiscordRpcEnabled;
        set
        {
            if (_settings.DiscordRpcEnabled != value)
            {
                _settings.DiscordRpcEnabled = value;
                Save();
                NotifyChanged(nameof(DiscordRpcEnabled));
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _settings.MinimizeToTrayOnClose;
        set
        {
            if (_settings.MinimizeToTrayOnClose != value)
            {
                _settings.MinimizeToTrayOnClose = value;
                Save();
                NotifyChanged(nameof(MinimizeToTrayOnClose));
            }
        }
    }

    public bool TrackNotificationsEnabled
    {
        get => _settings.TrackNotificationsEnabled;
        set
        {
            if (_settings.TrackNotificationsEnabled != value)
            {
                _settings.TrackNotificationsEnabled = value;
                Save();
                NotifyChanged(nameof(TrackNotificationsEnabled));
            }
        }
    }

    public bool FckCensorEnabled
    {
        get => _settings.FckCensorEnabled;
        set
        {
            if (_settings.FckCensorEnabled != value)
            {
                _settings.FckCensorEnabled = value;
                Save();
                NotifyChanged(nameof(FckCensorEnabled));
            }
        }
    }

    public string AppTheme
    {
        get => _settings.AppTheme;
        set
        {
            if (_settings.AppTheme != value)
            {
                _settings.AppTheme = value;
                Save();
                NotifyChanged(nameof(AppTheme));
            }
        }
    }

    public string AppFont
    {
        get => _settings.AppFont;
        set
        {
            if (_settings.AppFont != value)
            {
                _settings.AppFont = value;
                Save();
                NotifyChanged(nameof(AppFont));
            }
        }
    }

    public string Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language != value)
            {
                _settings.Language = value;
                Save();
                NotifyChanged(nameof(Language));
            }
        }
    }

    public bool AudioCacheEnabled
    {
        get => _settings.AudioCacheEnabled;
        set
        {
            if (_settings.AudioCacheEnabled != value)
            {
                _settings.AudioCacheEnabled = value;
                Save();
                NotifyChanged(nameof(AudioCacheEnabled));
            }
        }
    }

    public string AudioCacheLocation
    {
        get => _settings.AudioCacheLocation;
        set
        {
            if (_settings.AudioCacheLocation != value)
            {
                _settings.AudioCacheLocation = value ?? string.Empty;
                Save();
                NotifyChanged(nameof(AudioCacheLocation));
            }
        }
    }

    public int CacheSizeLimitMb
    {
        get => _settings.CacheSizeLimitMb;
        set
        {
            if (_settings.CacheSizeLimitMb != value)
            {
                _settings.CacheSizeLimitMb = value;
                Save();
                NotifyChanged(nameof(CacheSizeLimitMb));
            }
        }
    }

    public bool ObsOverlayEnabled
    {
        get => _settings.ObsOverlayEnabled;
        set
        {
            if (_settings.ObsOverlayEnabled != value)
            {
                _settings.ObsOverlayEnabled = value;
                Save();
                NotifyChanged(nameof(ObsOverlayEnabled));
            }
        }
    }

    public int ObsOverlayPort
    {
        get => _settings.ObsOverlayPort;
        set
        {
            if (_settings.ObsOverlayPort != value)
            {
                _settings.ObsOverlayPort = value;
                Save();
                NotifyChanged(nameof(ObsOverlayPort));
            }
        }
    }

    public string ObsOverlayTheme
    {
        get => _settings.ObsOverlayTheme;
        set
        {
            if (_settings.ObsOverlayTheme != value)
            {
                _settings.ObsOverlayTheme = value;
                Save();
                NotifyChanged(nameof(ObsOverlayTheme));
            }
        }
    }

    public bool ObsOverlayHideOnPause
    {
        get => _settings.ObsOverlayHideOnPause;
        set
        {
            if (_settings.ObsOverlayHideOnPause != value)
            {
                _settings.ObsOverlayHideOnPause = value;
                Save();
                NotifyChanged(nameof(ObsOverlayHideOnPause));
            }
        }
    }

    public bool AutoplayWaveOnAlbumEnd
    {
        get => _settings.AutoplayWaveOnAlbumEnd;
        set
        {
            if (_settings.AutoplayWaveOnAlbumEnd != value)
            {
                _settings.AutoplayWaveOnAlbumEnd = value;
                Save();
                NotifyChanged(nameof(AutoplayWaveOnAlbumEnd));
            }
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        _settings = loaded;
                        if (string.IsNullOrEmpty(_settings.AppFont) || _settings.AppFont == "YandexSans")
                        {
                            _settings.AppFont = "Inter";
                            Save();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Failed to load settings: {ex.Message}");
            }
        }
    }

    private void ApplyToServices()
    {
        try
        {
            _musicService.PreferredQuality = _settings.PreferredQuality;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Failed to apply settings to services: {ex.Message}");
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Failed to save settings: {ex.Message}");
            }
        }
    }

    private void NotifyChanged(string propName)
    {
        SettingChanged?.Invoke(this, propName);
    }
}
