#nullable enable

using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.ViewModels;

public sealed partial class ProfileViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly ISettingsService _settingsService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IEqualizerService _equalizerService;
    private readonly IPinnedItemsService _pinnedItemsService;
    private readonly INavigationService _navigationService;
    private readonly LoginViewModel _loginViewModel;
    private readonly SearchViewModel _searchViewModel;
    private readonly IAudioCacheService _audioCacheService;

    private readonly string _appDataDir;
    private bool _isInitializingSettings;

    [ObservableProperty]
    private UserProfile? _user;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private int _likedTracksCount;

    [ObservableProperty]
    private int _playlistsCount;

    [ObservableProperty]
    private int _pinnedItemsCount;

    [ObservableProperty]
    private string _cacheSizeText = "0 KB";

    [ObservableProperty]
    private string _cachedTracksCountText = "0 tracks cached";

    [ObservableProperty]
    private bool _isAudioCacheEnabled = true;

    [ObservableProperty]
    private int _selectedCacheLimitIndex = 1;

    [ObservableProperty]
    private string _appDataPath = string.Empty;

    [ObservableProperty]
    private string _appVersionText = "v0.9.0-preview";

    [ObservableProperty]
    private int _selectedQualityIndex;

    [ObservableProperty]
    private bool _isGaplessPreloadEnabled;

    [ObservableProperty]
    private bool _isEqualizerEnabled;

    [ObservableProperty]
    private ObservableCollection<string> _availablePresets = new();

    [ObservableProperty]
    private string _selectedEqualizerPreset = "Flat";

    [ObservableProperty]
    private bool _isDiscordRpcEnabled;

    [ObservableProperty]
    private bool _isMinimizeToTrayEnabled;

    [ObservableProperty]
    private bool _isTrackNotificationsEnabled;

    [ObservableProperty]
    private bool _isFckCensorEnabled;

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private string? _notificationMessage;

    [ObservableProperty]
    private bool _hasNotification;

    public ProfileViewModel(
        IYandexMusicService musicService,
        ICredentialStorageService credentialStorage,
        ISettingsService settingsService,
        IAudioPlayerService audioPlayer,
        IEqualizerService equalizerService,
        IPinnedItemsService pinnedItemsService,
        INavigationService navigationService,
        LoginViewModel loginViewModel,
        SearchViewModel searchViewModel,
        IAudioCacheService audioCacheService)
    {
        _musicService = musicService;
        _credentialStorage = credentialStorage;
        _settingsService = settingsService;
        _audioPlayer = audioPlayer;
        _equalizerService = equalizerService;
        _pinnedItemsService = pinnedItemsService;
        _navigationService = navigationService;
        _loginViewModel = loginViewModel;
        _searchViewModel = searchViewModel;
        _audioCacheService = audioCacheService;

        _appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic");
        AppDataPath = _appDataDir;

        if (_credentialStorage.HasToken())
        {
            _isLoggedIn = true;
            _user = _loginViewModel.CurrentUser;
        }
        _pinnedItemsCount = _pinnedItemsService.GetPinnedItems().Count;

        LoadSettingsState();
        _settingsService.SettingChanged += OnSettingChanged;
        _equalizerService.EqualizerChanged += OnEqualizerServiceChanged;
        _audioCacheService.CacheUpdated += (_, _) => RunOnUi(CalculateCacheSize);
    }

    public async Task InitializeAsync()
    {
        SetBusy(true);
        try
        {
            await RefreshProfileAsync();
            CalculateCacheSize();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LoadSettingsState()
    {
        _isInitializingSettings = true;
        try
        {
            SelectedQualityIndex = _settingsService.PreferredQuality switch
            {
                AudioQualityPreference.AacPreferred => 0,
                AudioQualityPreference.Mp3Preferred => 1,
                _ => 2
            };

            IsGaplessPreloadEnabled = _settingsService.GaplessPreloadEnabled;
            IsDiscordRpcEnabled = _settingsService.DiscordRpcEnabled;
            IsMinimizeToTrayEnabled = _settingsService.MinimizeToTrayOnClose;
            IsTrackNotificationsEnabled = _settingsService.TrackNotificationsEnabled;
            IsFckCensorEnabled = _settingsService.FckCensorEnabled;
            IsAudioCacheEnabled = _settingsService.AudioCacheEnabled;

            SelectedCacheLimitIndex = _settingsService.CacheSizeLimitMb switch
            {
                512 => 0,
                1024 => 1,
                2048 => 2,
                4096 => 3,
                8192 => 4,
                16384 => 5,
                0 => 6,
                _ => 1
            };

            SelectedThemeIndex = _settingsService.AppTheme switch
            {
                "Light" => 2,
                "Default" => 0,
                _ => 1 // Dark default
            };

            SelectedLanguageIndex = _settingsService.Language switch
            {
                "en" => 1,
                _ => 0 // "ru" default
            };

            IsEqualizerEnabled = _equalizerService.IsEnabled;

            AvailablePresets.Clear();
            foreach (var preset in _equalizerService.Presets)
            {
                AvailablePresets.Add(preset.Name);
            }
            SelectedEqualizerPreset = _equalizerService.CurrentPresetName;
        }
        finally
        {
            _isInitializingSettings = false;
        }
    }

    private void OnSettingChanged(object? sender, string propName)
    {
        if (_isInitializingSettings) return;

        RunOnUi(() =>
        {
            if (propName == nameof(ISettingsService.PreferredQuality))
            {
                int newIndex = _settingsService.PreferredQuality switch
                {
                    AudioQualityPreference.AacPreferred => 0,
                    AudioQualityPreference.Mp3Preferred => 1,
                    _ => 2
                };
                if (SelectedQualityIndex != newIndex) SelectedQualityIndex = newIndex;
            }
            else if (propName == nameof(ISettingsService.GaplessPreloadEnabled))
            {
                IsGaplessPreloadEnabled = _settingsService.GaplessPreloadEnabled;
            }
            else if (propName == nameof(ISettingsService.DiscordRpcEnabled))
            {
                IsDiscordRpcEnabled = _settingsService.DiscordRpcEnabled;
            }
            else if (propName == nameof(ISettingsService.MinimizeToTrayOnClose))
            {
                IsMinimizeToTrayEnabled = _settingsService.MinimizeToTrayOnClose;
            }
            else if (propName == nameof(ISettingsService.TrackNotificationsEnabled))
            {
                IsTrackNotificationsEnabled = _settingsService.TrackNotificationsEnabled;
            }
            else if (propName == nameof(ISettingsService.FckCensorEnabled))
            {
                IsFckCensorEnabled = _settingsService.FckCensorEnabled;
            }
            else if (propName == nameof(ISettingsService.AudioCacheEnabled))
            {
                IsAudioCacheEnabled = _settingsService.AudioCacheEnabled;
            }
            else if (propName == nameof(ISettingsService.CacheSizeLimitMb))
            {
                int newIdx = _settingsService.CacheSizeLimitMb switch
                {
                    512 => 0,
                    1024 => 1,
                    2048 => 2,
                    4096 => 3,
                    8192 => 4,
                    16384 => 5,
                    0 => 6,
                    _ => 1
                };
                if (SelectedCacheLimitIndex != newIdx) SelectedCacheLimitIndex = newIdx;
                CalculateCacheSize();
            }
            else if (propName == nameof(ISettingsService.Language))
            {
                int langIdx = _settingsService.Language == "en" ? 1 : 0;
                if (SelectedLanguageIndex != langIdx) SelectedLanguageIndex = langIdx;
            }
        });
    }

    private void OnEqualizerServiceChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            IsEqualizerEnabled = _equalizerService.IsEnabled;
            SelectedEqualizerPreset = _equalizerService.CurrentPresetName;
        });
    }

    partial void OnSelectedQualityIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        var pref = value switch
        {
            0 => AudioQualityPreference.AacPreferred,
            1 => AudioQualityPreference.Mp3Preferred,
            _ => AudioQualityPreference.HighestBitrate
        };
        _settingsService.PreferredQuality = pref;
        ShowNotification($"Audio quality updated to: {(value == 0 ? "AAC Preferred" : (value == 1 ? "MP3 320k" : "Highest Bitrate"))}");
    }

    partial void OnIsGaplessPreloadEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.GaplessPreloadEnabled = value;
    }

    partial void OnIsDiscordRpcEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.DiscordRpcEnabled = value;
    }

    partial void OnIsMinimizeToTrayEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.MinimizeToTrayOnClose = value;
    }

    partial void OnIsTrackNotificationsEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.TrackNotificationsEnabled = value;
        ShowNotification(value ? "Desktop notifications enabled for track changes." : "Desktop notifications disabled.");
    }

    partial void OnIsFckCensorEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.FckCensorEnabled = value;
        _ = _audioPlayer.ReloadCurrentTrackQualityAsync();
        ShowNotification(value ? "FckCensor enabled: Uncensored audio active." : "FckCensor disabled.");
    }

    partial void OnIsAudioCacheEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.AudioCacheEnabled = value;
        ShowNotification(value ? "Audio Disk Cache enabled." : "Audio Disk Cache disabled.");
    }

    partial void OnSelectedCacheLimitIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        int limit = value switch
        {
            0 => 512,
            1 => 1024,
            2 => 2048,
            3 => 4096,
            4 => 8192,
            5 => 16384,
            _ => 0 // Unlimited
        };
        _settingsService.CacheSizeLimitMb = limit;
        _audioCacheService.EnforceCacheLimit();
        CalculateCacheSize();
        ShowNotification(limit > 0 ? $"Audio cache limit set to {limit} MB." : "Audio cache limit set to Unlimited.");
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        string theme = value switch
        {
            2 => "Light",
            0 => "Default",
            _ => "Dark"
        };
        _settingsService.AppTheme = theme;

        if (App.MainWindow is ShellWindow shell)
        {
            shell.ApplyTheme(theme);
        }
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        string lang = value == 1 ? "en" : "ru";
        _settingsService.Language = lang;
        if (Loc != null && Loc.CurrentLanguage != lang)
        {
            Loc.CurrentLanguage = lang;
        }

        ShowNotification(Loc?["SettingsSavedNotification"] ?? "Настройки сохранены.");
    }

    partial void OnIsEqualizerEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _equalizerService.IsEnabled = value;
    }

    partial void OnSelectedEqualizerPresetChanged(string value)
    {
        if (_isInitializingSettings || string.IsNullOrEmpty(value)) return;
        _equalizerService.ApplyPreset(value);
    }

    [RelayCommand]
    public async Task RefreshProfileAsync()
    {
        string? token = _credentialStorage.GetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            RunOnUi(() =>
            {
                User = null;
                IsLoggedIn = false;
                LikedTracksCount = 0;
                PlaylistsCount = 0;
                PinnedItemsCount = _pinnedItemsService.GetPinnedItems().Count;
            });
            return;
        }

        try
        {
            _musicService.SetOAuthToken(token);
            var profile = await _musicService.GetAccountStatusAsync();
            if (profile == null && _loginViewModel.CurrentUser != null)
            {
                profile = _loginViewModel.CurrentUser;
            }

            int likedCount = 0;
            int playlistsCount = 0;

            if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
            {
                try
                {
                    var likedIds = await _musicService.GetLikedTrackIdsAsync(profile.UserId);
                    likedCount = likedIds?.Count ?? 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfileViewModel] Liked tracks count error: {ex.Message}");
                }

                try
                {
                    var playlists = await _musicService.GetUserPlaylistsAsync(profile.UserId);
                    playlistsCount = playlists?.Count ?? 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfileViewModel] Playlists count error: {ex.Message}");
                }
            }

            int pinnedCount = _pinnedItemsService.GetPinnedItems().Count;

            RunOnUi(() =>
            {
                User = profile;
                IsLoggedIn = true;
                LikedTracksCount = likedCount;
                PlaylistsCount = playlistsCount;
                PinnedItemsCount = pinnedCount;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to load profile stats: {ex.Message}");
            RunOnUi(() =>
            {
                IsLoggedIn = _credentialStorage.HasToken();
            });
        }
    }

    [RelayCommand]
    public void ResetEqualizer()
    {
        _equalizerService.ResetToFlat();
        SelectedEqualizerPreset = "Flat";
        ShowNotification("Equalizer reset to Flat.");
    }

    [RelayCommand]
    public void CopyUserId()
    {
        if (User == null || string.IsNullOrWhiteSpace(User.UserId)) return;

        try
        {
            var dp = new DataPackage();
            dp.SetText(User.UserId);
            Clipboard.SetContent(dp);
            ShowNotification($"User ID copied: {User.UserId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Clipboard copy failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void Logout()
    {
        _loginViewModel.LogoutCommand.Execute(null);
    }

    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        try
        {
            await _audioCacheService.ClearCacheAsync();

            string cacheFolder = Path.Combine(_appDataDir, "Cache");
            if (Directory.Exists(cacheFolder))
            {
                var files = Directory.GetFiles(cacheFolder);
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }

            CalculateCacheSize();
            ShowNotification("Audio and application cache successfully cleared.");
        }
        catch (Exception ex)
        {
            ShowNotification($"Failed to clear cache: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenCacheFolder()
    {
        try
        {
            string cacheFolder = Path.Combine(_appDataDir, "Cache", "Audio");
            Directory.CreateDirectory(cacheFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{cacheFolder}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowNotification($"Could not open cache folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearSearchHistory()
    {
        _searchViewModel.ClearRecentQueries();
        ShowNotification("Search history cleared.");
    }

    [RelayCommand]
    public void OpenAppDataFolder()
    {
        try
        {
            Directory.CreateDirectory(_appDataDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_appDataDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowNotification($"Could not open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/pixiemays/WinYandex",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Open GitHub failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void NavigateToLikes() => _navigationService.NavigateToTag("Likes");

    [RelayCommand]
    public void NavigateToPlaylists() => _navigationService.NavigateToTag("Playlists");

    [RelayCommand]
    public void NavigateToLogin() => _navigationService.NavigateToTag("Account");

    private void CalculateCacheSize()
    {
        try
        {
            long audioBytes = _audioCacheService.GetTotalCacheSizeBytes();
            int trackCount = _audioCacheService.GetCachedTrackCount();
            CachedTracksCountText = trackCount == 1 ? "1 track cached" : $"{trackCount} tracks cached";

            if (!Directory.Exists(_appDataDir))
            {
                CacheSizeText = "0 KB";
                return;
            }

            var dirInfo = new DirectoryInfo(_appDataDir);
            long totalBytes = 0;
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                totalBytes += file.Length;
            }

            string limitStr = _settingsService.CacheSizeLimitMb > 0
                ? $" / {_settingsService.CacheSizeLimitMb} MB limit"
                : " (Unlimited)";

            CacheSizeText = $"{FormatBytes(totalBytes)} ({FormatBytes(audioBytes)} audio{limitStr})";
        }
        catch
        {
            CacheSizeText = "< 1 MB";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private void ShowNotification(string message)
    {
        NotificationMessage = message;
        HasNotification = true;
    }

    [RelayCommand]
    public void DismissNotification()
    {
        HasNotification = false;
        NotificationMessage = null;
    }
}
