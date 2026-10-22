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
