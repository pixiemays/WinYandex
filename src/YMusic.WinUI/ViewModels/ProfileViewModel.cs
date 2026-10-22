#nullable enable

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.ViewModels;

public sealed class ProfileCacheData
{
    public UserProfile? User { get; set; }
    public int LikedTracksCount { get; set; }
    public int PlaylistsCount { get; set; }
    public int PinnedItemsCount { get; set; }
}

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
    private readonly string _profileCachePath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _hasCachedProfile;
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
    private int _selectedFontIndex;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private string? _notificationMessage;

    [ObservableProperty]
    private bool _hasNotification;

    public ObservableCollection<string> QualityOptions { get; } = new();
    public ObservableCollection<string> ThemeOptions { get; } = new();
    public ObservableCollection<string> FontOptions { get; } = new();
    public ObservableCollection<string> CacheLimitOptions { get; } = new();

    private bool IsRussian => Loc?.CurrentLanguage != "en";

    public string TitleText => Loc?["Profile_Title"] ?? (IsRussian ? "Профиль и настройки" : "Profile & Settings");
    public string SubtitleText => IsRussian ? "Управление аккаунтом Яндекса, воспроизведением и настройками приложения" : "Manage your Yandex account, playback preferences, and application options";
    public string NotificationTitle => IsRussian ? "Настройки" : "Settings";
    public string CopyUserIdTooltip => IsRussian ? "Скопировать User ID" : "Copy User ID";
    public string RefreshButtonText => Loc?["Profile_Refresh"] ?? (IsRussian ? "Обновить" : "Refresh");
    public string SignOutButtonText => Loc?["Profile_Logout"] ?? (IsRussian ? "Выйти" : "Sign Out");
    public string LikedTracksStatText => Loc?["Profile_LikedTracks"] ?? (IsRussian ? "Любимые треки" : "Liked Tracks");
    public string PlaylistsStatText => Loc?["Profile_Playlists"] ?? (IsRussian ? "Плейлисты" : "Playlists");
    public string PinnedItemsStatText => Loc?["Playlists_Pinned"] ?? (IsRussian ? "Закрепленные" : "Pinned Items");

    public string SignedOutTitleText => IsRussian ? "Вы не авторизованы" : "You are currently signed out";
    public string SignedOutDescText => IsRussian ? "Войдите с Яндекс ID для синхронизации вашей коллекции, Моей волны, лайков и плейлистов." : "Sign in with your Yandex ID to sync your collection, My Wave recommendations, liked tracks and playlists.";
    public string SignInButtonText => IsRussian ? "Войти с Яндекс ID" : "Sign In with Yandex ID";

    public string AudioSectionTitle => Loc?["Profile_Section_Audio"] ?? (IsRussian ? "Аудио и воспроизведение" : "Audio & Playback");
    public string StreamQualityTitle => Loc?["Settings_PreferredQuality"] ?? (IsRussian ? "Предпочитаемое качество" : "Default Streaming Quality");
    public string StreamQualityDesc => IsRussian ? "Кодек и битрейт для потокового воспроизведения треков" : "Preferred audio codec and bitrate for streaming tracks";

    public string GaplessTitle => Loc?["Settings_Gapless"] ?? (IsRussian ? "Бесшовное воспроизведение и предзагрузка" : "Gapless Playback & Background Preloading");
    public string GaplessDesc => Loc?["Settings_GaplessDesc"] ?? (IsRussian ? "Предзагрузка следующего трека и текста на 80% для устранения пауз и буферизации" : "Preloads the next track stream and lyrics at 80% progress to eliminate buffering pauses");

    public string EqualizerTitle => Loc?["Settings_Equalizer"] ?? (IsRussian ? "Аудио эквалайзер" : "Audio Equalizer");
    public string EqualizerDesc => Loc?["Settings_EqualizerDesc"] ?? (IsRussian ? "10-полосный эквалайзер частот звука и акустические профили" : "10-band audio frequency equalizer and acoustic profiles");
    public string EqualizerResetText => IsRussian ? "Сброс" : "Reset";

    public string SystemSectionTitle => Loc?["Profile_Section_System"] ?? (IsRussian ? "Система и интеграции" : "System & Integrations");
    public string DiscordRpcTitle => Loc?["Settings_DiscordRpc"] ?? "Discord Rich Presence";
    public string DiscordRpcDesc => Loc?["Settings_DiscordRpcDesc"] ?? (IsRussian ? "Отображение названия трека, исполнителя и обложки в профиле Discord" : "Show current playing track title, artist name, and album artwork on your Discord profile");

    public string FckCensorTitle => Loc?["Settings_FckCensor"] ?? "FckCensor (разблокировка цензуры)";
    public string FckCensorDesc => Loc?["Settings_FckCensorDesc"] ?? (IsRussian ? "Автоматически предпочитать оригинальные треки без цензуры вместо заглушенных версий" : "Automatically prefer original explicit and uncensored versions of tracks over sanitized edits");

    public string MinimizeToTrayTitle => Loc?["Settings_MinimizeToTray"] ?? (IsRussian ? "Сворачивать в системный трей Windows" : "Minimize to Windows System Tray");
    public string MinimizeToTrayDesc => Loc?["Settings_MinimizeToTrayDesc"] ?? (IsRussian ? "При нажатии крестика плеер продолжит играть в фоне и свернется в трей" : "Keep music playing in background and minimize to the taskbar notification tray when window is closed");

    public string TrackNotificationsTitle => Loc?["Settings_TrackNotifications"] ?? (IsRussian ? "Уведомления Windows Toast" : "Windows Toast Notifications");
    public string TrackNotificationsDesc => Loc?["Settings_TrackNotificationsDesc"] ?? (IsRussian ? "Показывать всплывающие уведомления на рабочем столе при переключении трека" : "Show native desktop banner notifications when track changes while playing in background");

    public string ThemeTitle => Loc?["Settings_Theme"] ?? (IsRussian ? "Тема оформления" : "Application Theme");
    public string ThemeDesc => IsRussian ? "Выберите визуальное оформление приложения" : "Select your visual appearance preference";

    public string FontTitle => IsRussian ? "Шрифт интерфейса" : "UI Typography Font";
    public string FontDesc => IsRussian ? "Фирменный Yandex Sans, современный Inter или системный Segoe UI" : "Signature Yandex Sans, modern Inter, or system Segoe UI";

    public string LanguageTitle => "Language / Язык";
    public string LanguageDesc => IsRussian ? "Мгновенное переключение языка интерфейса без перезапуска" : "Switch interface language instantly / Смена языка интерфейса без перезапуска";

    public string StorageSectionTitle => Loc?["Profile_Section_Storage"] ?? (IsRussian ? "Хранилище и данные" : "Storage & Data");
    public string AudioCacheTitle => Loc?["Settings_AudioCache"] ?? (IsRussian ? "Дисковый аудио-кэш (LRU)" : "Audio Disk Cache (LRU)");
    public string InstantReplayText => IsRussian ? "Мгновенный старт" : "Instant Replay";
    public string AudioCacheDesc => Loc?["Settings_AudioCacheDesc"] ?? (IsRussian ? "Сохранение треков в %LocalAppData%/YMusic/Cache для мгновенного запуска (0 мс) без повторного скачивания" : "Save played tracks offline in %LocalAppData%/YMusic/Cache for instant 0 ms replay without network re-downloading");

    public string CacheLimitTitle => Loc?["Settings_CacheLimit"] ?? (IsRussian ? "Лимит дискового кэша" : "Cache Storage Limit");
    public string CacheLimitDesc => IsRussian ? "Автоматически удаляет наименее прослушиваемые треки (LRU) при превышении квоты" : "Automatically evicts least recently played tracks (LRU) when local cache exceeds this quota";

    public string LocalStorageTitle => IsRussian ? "Локальный аудио и метаданные кэш" : "Local Audio & Metadata Cache";
    public string StorageOccupiedText => IsRussian ? "Занято места:" : "Storage occupied:";
    public string CachedItemsText => IsRussian ? "Кэшировано треков:" : "Cached items:";
    public string OpenCacheTooltip => IsRussian ? "Открыть папку кэша аудио в Проводнике" : "Open Cache/Audio folder in Explorer";
    public string OpenCacheButtonText => IsRussian ? "Открыть кэш" : "Open Cache";
    public string ClearCacheButtonText => Loc?["Settings_ClearCache"] ?? (IsRussian ? "Очистить кэш" : "Clear Cache");

    public string AppDataFolderTitle => Loc?["Settings_OpenAppData"] ?? (IsRussian ? "Папка данных приложения" : "Application Data Folder");
    public string OpenInExplorerButtonText => IsRussian ? "Открыть в проводнике" : "Open in Explorer";

    public string SearchHistoryTitle => Loc?["Search_History"] ?? (IsRussian ? "История поиска" : "Search History");
    public string SearchHistoryDesc => IsRussian ? "Очистить недавние поисковые запросы, сохранённые на этом устройстве" : "Clear recent search queries saved on this device";
    public string ClearHistoryButtonText => Loc?["Search_ClearHistory"] ?? (IsRussian ? "Очистить историю" : "Clear History");

    public string AboutDescText => IsRussian ? "Современный настольный клиент Яндекс Музыки на WinUI 3, Windows App SDK и .NET 8." : "Modern desktop client for Yandex Music built with WinUI 3, Windows App SDK & .NET 8.";
    public string GitHubRepoButtonText => IsRussian ? "Репозиторий GitHub" : "GitHub Repository";

    public string SwitchOnText => IsRussian ? "Вкл" : "On";
    public string SwitchOffText => IsRussian ? "Выкл" : "Off";
    public string SwitchEnabledText => IsRussian ? "Включено" : "Enabled";
    public string SwitchDisabledText => IsRussian ? "Отключено" : "Disabled";

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
        _profileCachePath = Path.Combine(_appDataDir, "profile_cache.json");

        LoadCachedProfile();

        if (_user == null && _credentialStorage.HasToken())
        {
            _isLoggedIn = true;
            _user = _loginViewModel.CurrentUser;
        }
        _pinnedItemsCount = _pinnedItemsService.GetPinnedItems().Count;

        LoadSettingsState();
        UpdateOptionCollections();
        _settingsService.SettingChanged += OnSettingChanged;
        _equalizerService.EqualizerChanged += OnEqualizerServiceChanged;
        _audioCacheService.CacheUpdated += (_, _) => RunOnUi(CalculateCacheSize);
    }

    public async Task InitializeAsync()
    {
        bool shouldSetBusy = User == null && !_hasCachedProfile;
        if (shouldSetBusy)
        {
            SetBusy(true);
        }

        try
        {
            await RefreshProfileAsync();
            CalculateCacheSize();
        }
        finally
        {
            if (shouldSetBusy)
            {
                SetBusy(false);
            }
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

            SelectedFontIndex = _settingsService.AppFont switch
            {
                "Inter" => 1,
                "System" => 2,
                _ => 0 // YandexSans default
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
        string qualityName = value == 0
            ? (IsRussian ? "AAC (Рекомендуется)" : "AAC Preferred")
            : (value == 1 ? "MP3 320k" : (IsRussian ? "Наивысший битрейт" : "Highest Bitrate"));
        ShowNotification(IsRussian ? $"Качество аудио обновлено: {qualityName}" : $"Audio quality updated to: {qualityName}");
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
        ShowNotification(value
            ? (IsRussian ? "Уведомления на рабочем столе о смене трека включены." : "Desktop notifications enabled for track changes.")
            : (IsRussian ? "Уведомления на рабочем столе отключены." : "Desktop notifications disabled."));
    }

    partial void OnIsFckCensorEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.FckCensorEnabled = value;
        _ = _audioPlayer.ReloadCurrentTrackQualityAsync();
        ShowNotification(value
            ? (IsRussian ? "FckCensor включен: воспроизводятся нецензурированные версии." : "FckCensor enabled: Uncensored audio active.")
            : (IsRussian ? "FckCensor выключен." : "FckCensor disabled."));
    }

    partial void OnIsAudioCacheEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.AudioCacheEnabled = value;
        ShowNotification(value
            ? (IsRussian ? "Дисковый аудио-кэш включен." : "Audio Disk Cache enabled.")
            : (IsRussian ? "Дисковый аудио-кэш выключен." : "Audio Disk Cache disabled."));
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
        ShowNotification(limit > 0
            ? (IsRussian ? $"Лимит дискового кэша установлен на {limit} МБ." : $"Audio cache limit set to {limit} MB.")
            : (IsRussian ? "Лимит дискового кэша отключен (без ограничений)." : "Audio cache limit set to Unlimited."));
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

    partial void OnSelectedFontIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        string font = value switch
        {
            1 => "Inter",
            2 => "System",
            _ => "YandexSans"
        };
        _settingsService.AppFont = font;

        if (App.MainWindow is ShellWindow shell)
        {
            shell.ApplyFont(font);
        }

        string fontName = value switch
        {
            1 => "Inter",
            2 => "Segoe UI",
            _ => "Yandex Sans"
        };
        ShowNotification(IsRussian ? $"Шрифт интерфейса изменён: {fontName}." : $"UI font changed to: {fontName}.");
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

        ShowNotification(IsRussian ? "Настройки сохранены." : "Settings saved.");
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
        if (!await _refreshLock.WaitAsync(0))
        {
            // Already refreshing in background, avoid duplicate parallel calls
            return;
        }

        try
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
                ClearCachedProfile();
                return;
            }

            _musicService.SetOAuthToken(token);
            var profile = await _musicService.GetAccountStatusAsync();
            if (profile == null && _loginViewModel.CurrentUser != null)
            {
                profile = _loginViewModel.CurrentUser;
            }
            if (profile == null && User != null)
            {
                profile = User;
            }

            int likedCount = LikedTracksCount;
            int playlistsCount = PlaylistsCount;

            if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
            {
                try
                {
                    var likedIds = await _musicService.GetLikedTrackIdsAsync(profile.UserId);
                    if (likedIds != null)
                    {
                        likedCount = likedIds.Count;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfileViewModel] Liked tracks count error: {ex.Message}");
                }

                try
                {
                    var playlists = await _musicService.GetUserPlaylistsAsync(profile.UserId);
                    if (playlists != null)
                    {
                        playlistsCount = playlists.Count;
                    }
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

            SaveCachedProfile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to load profile stats: {ex.Message}");
            RunOnUi(() =>
            {
                IsLoggedIn = _credentialStorage.HasToken();
            });
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void LoadCachedProfile()
    {
        try
        {
            if (File.Exists(_profileCachePath) && _credentialStorage.HasToken())
            {
                string json = File.ReadAllText(_profileCachePath);
                var cached = JsonSerializer.Deserialize<ProfileCacheData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (cached != null && cached.User != null)
                {
                    User = cached.User;
                    IsLoggedIn = true;
                    LikedTracksCount = cached.LikedTracksCount;
                    PlaylistsCount = cached.PlaylistsCount;
                    PinnedItemsCount = cached.PinnedItemsCount > 0 ? cached.PinnedItemsCount : _pinnedItemsService.GetPinnedItems().Count;
                    _hasCachedProfile = true;
                    Debug.WriteLine($"[ProfileViewModel] Loaded cached profile for {User.DisplayName}: likes={LikedTracksCount}, playlists={PlaylistsCount}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to load cached profile: {ex.Message}");
        }
    }

    private void SaveCachedProfile()
    {
        try
        {
            if (User != null && IsLoggedIn)
            {
                var data = new ProfileCacheData
                {
                    User = User,
                    LikedTracksCount = LikedTracksCount,
                    PlaylistsCount = PlaylistsCount,
                    PinnedItemsCount = PinnedItemsCount
                };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(_appDataDir);
                File.WriteAllText(_profileCachePath, json);
                _hasCachedProfile = true;
                Debug.WriteLine("[ProfileViewModel] Successfully cached profile data to disk.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to save cached profile: {ex.Message}");
        }
    }

    private void ClearCachedProfile()
    {
        try
        {
            if (File.Exists(_profileCachePath))
            {
                File.Delete(_profileCachePath);
            }
            _hasCachedProfile = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to delete profile cache: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ResetEqualizer()
    {
        _equalizerService.ResetToFlat();
        SelectedEqualizerPreset = "Flat";
        ShowNotification(IsRussian ? "Эквалайзер сброшен в Flat." : "Equalizer reset to Flat.");
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
            ShowNotification(IsRussian ? $"ID пользователя скопирован: {User.UserId}" : $"User ID copied: {User.UserId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Clipboard copy failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void Logout()
    {
        ClearCachedProfile();
        User = null;
        IsLoggedIn = false;
        LikedTracksCount = 0;
        PlaylistsCount = 0;
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
            ShowNotification(IsRussian ? "Аудио и системный кэш успешно очищены." : "Audio and application cache successfully cleared.");
        }
        catch (Exception ex)
        {
            ShowNotification(IsRussian ? $"Не удалось очистить кэш: {ex.Message}" : $"Failed to clear cache: {ex.Message}");
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
            ShowNotification(IsRussian ? $"Не удалось открыть папку кэша: {ex.Message}" : $"Could not open cache folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearSearchHistory()
    {
        _searchViewModel.ClearRecentQueries();
        ShowNotification(IsRussian ? "История поиска очищена." : "Search history cleared.");
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
            ShowNotification(IsRussian ? $"Не удалось открыть папку: {ex.Message}" : $"Could not open folder: {ex.Message}");
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

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        UpdateOptionCollections();

        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(NotificationTitle));
        OnPropertyChanged(nameof(CopyUserIdTooltip));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(SignOutButtonText));
        OnPropertyChanged(nameof(LikedTracksStatText));
        OnPropertyChanged(nameof(PlaylistsStatText));
        OnPropertyChanged(nameof(PinnedItemsStatText));
        OnPropertyChanged(nameof(SignedOutTitleText));
        OnPropertyChanged(nameof(SignedOutDescText));
        OnPropertyChanged(nameof(SignInButtonText));
        OnPropertyChanged(nameof(AudioSectionTitle));
        OnPropertyChanged(nameof(StreamQualityTitle));
        OnPropertyChanged(nameof(StreamQualityDesc));
        OnPropertyChanged(nameof(GaplessTitle));
        OnPropertyChanged(nameof(GaplessDesc));
        OnPropertyChanged(nameof(EqualizerTitle));
        OnPropertyChanged(nameof(EqualizerDesc));
        OnPropertyChanged(nameof(EqualizerResetText));
        OnPropertyChanged(nameof(SystemSectionTitle));
        OnPropertyChanged(nameof(DiscordRpcTitle));
        OnPropertyChanged(nameof(DiscordRpcDesc));
        OnPropertyChanged(nameof(FckCensorTitle));
        OnPropertyChanged(nameof(FckCensorDesc));
        OnPropertyChanged(nameof(MinimizeToTrayTitle));
        OnPropertyChanged(nameof(MinimizeToTrayDesc));
        OnPropertyChanged(nameof(TrackNotificationsTitle));
        OnPropertyChanged(nameof(TrackNotificationsDesc));
        OnPropertyChanged(nameof(ThemeTitle));
        OnPropertyChanged(nameof(ThemeDesc));
        OnPropertyChanged(nameof(FontTitle));
        OnPropertyChanged(nameof(FontDesc));
        OnPropertyChanged(nameof(LanguageTitle));
        OnPropertyChanged(nameof(LanguageDesc));
        OnPropertyChanged(nameof(StorageSectionTitle));
        OnPropertyChanged(nameof(AudioCacheTitle));
        OnPropertyChanged(nameof(InstantReplayText));
        OnPropertyChanged(nameof(AudioCacheDesc));
        OnPropertyChanged(nameof(CacheLimitTitle));
        OnPropertyChanged(nameof(CacheLimitDesc));
        OnPropertyChanged(nameof(LocalStorageTitle));
        OnPropertyChanged(nameof(StorageOccupiedText));
        OnPropertyChanged(nameof(CachedItemsText));
        OnPropertyChanged(nameof(OpenCacheTooltip));
        OnPropertyChanged(nameof(OpenCacheButtonText));
        OnPropertyChanged(nameof(ClearCacheButtonText));
        OnPropertyChanged(nameof(AppDataFolderTitle));
        OnPropertyChanged(nameof(OpenInExplorerButtonText));
        OnPropertyChanged(nameof(SearchHistoryTitle));
        OnPropertyChanged(nameof(SearchHistoryDesc));
        OnPropertyChanged(nameof(ClearHistoryButtonText));
        OnPropertyChanged(nameof(AboutDescText));
        OnPropertyChanged(nameof(GitHubRepoButtonText));
        OnPropertyChanged(nameof(SwitchOnText));
        OnPropertyChanged(nameof(SwitchOffText));
        OnPropertyChanged(nameof(SwitchEnabledText));
        OnPropertyChanged(nameof(SwitchDisabledText));

        CalculateCacheSize();
    }

    private void UpdateOptionCollections()
    {
        bool wasInitializing = _isInitializingSettings;
        _isInitializingSettings = true;
        try
        {
            int qIdx = SelectedQualityIndex;
            QualityOptions.Clear();
            if (IsRussian)
            {
                QualityOptions.Add("AAC (192–256 kbps) • Рекомендуется");
                QualityOptions.Add("MP3 (320 kbps) • Постоянный битрейт");
                QualityOptions.Add("Наивысший доступный битрейт");
            }
            else
            {
                QualityOptions.Add("AAC (192–256 kbps) • Recommended");
                QualityOptions.Add("MP3 (320 kbps) • Constant Bitrate");
                QualityOptions.Add("Highest Bitrate Available");
            }
            SelectedQualityIndex = qIdx;

            int tIdx = SelectedThemeIndex;
            ThemeOptions.Clear();
            if (IsRussian)
            {
                ThemeOptions.Add("Как в системе");
                ThemeOptions.Add("Тёмная тема");
                ThemeOptions.Add("Светлая тема");
            }
            else
            {
                ThemeOptions.Add("System Default");
                ThemeOptions.Add("Dark Mode");
                ThemeOptions.Add("Light Mode");
            }
            SelectedThemeIndex = tIdx;

            int fIdx = SelectedFontIndex;
            FontOptions.Clear();
            if (IsRussian)
            {
                FontOptions.Add("Yandex Sans (Фирменный)");
                FontOptions.Add("Inter (Современный гротеск)");
                FontOptions.Add("Segoe UI (Системный)");
            }
            else
            {
                FontOptions.Add("Yandex Sans (Signature)");
                FontOptions.Add("Inter (Modern Grotesque)");
                FontOptions.Add("Segoe UI (System Default)");
            }
            SelectedFontIndex = fIdx;

            int cIdx = SelectedCacheLimitIndex;
            CacheLimitOptions.Clear();
            if (IsRussian)
            {
                CacheLimitOptions.Add("512 МБ");
                CacheLimitOptions.Add("1 ГБ (Рекомендуется)");
                CacheLimitOptions.Add("2 ГБ");
                CacheLimitOptions.Add("4 ГБ");
                CacheLimitOptions.Add("8 ГБ");
                CacheLimitOptions.Add("16 ГБ");
                CacheLimitOptions.Add("Без ограничений");
            }
            else
            {
                CacheLimitOptions.Add("512 MB");
                CacheLimitOptions.Add("1 GB (Recommended)");
                CacheLimitOptions.Add("2 GB");
                CacheLimitOptions.Add("4 GB");
                CacheLimitOptions.Add("8 GB");
                CacheLimitOptions.Add("16 GB");
                CacheLimitOptions.Add("Unlimited");
            }
            SelectedCacheLimitIndex = cIdx;
        }
        finally
        {
            _isInitializingSettings = wasInitializing;
        }
    }

    private void CalculateCacheSize()
    {
        try
        {
            long audioBytes = _audioCacheService.GetTotalCacheSizeBytes();
            int trackCount = _audioCacheService.GetCachedTrackCount();
            CachedTracksCountText = IsRussian
                ? $"{trackCount} трек(ов) в кэше"
                : (trackCount == 1 ? "1 track cached" : $"{trackCount} tracks cached");

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
                ? (IsRussian ? $" / лимит {_settingsService.CacheSizeLimitMb} МБ" : $" / {_settingsService.CacheSizeLimitMb} MB limit")
                : (IsRussian ? " (без лимита)" : " (Unlimited)");

            CacheSizeText = $"{FormatBytes(totalBytes)} ({FormatBytes(audioBytes)} {(IsRussian ? "аудио" : "audio")}{limitStr})";
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
