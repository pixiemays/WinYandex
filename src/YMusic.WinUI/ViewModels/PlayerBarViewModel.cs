#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views.Controls;

namespace YMusic.WinUI.ViewModels;

public sealed partial class PlayerBarViewModel : BaseViewModel
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IYandexMusicService _musicService;
    private readonly IEqualizerService _equalizer;
    private readonly INavigationService _navigationService;
    private bool _isUserSeeking;

    public IEqualizerService Equalizer => _equalizer;
    public IReadOnlyList<EqualizerBand> EqualizerBands => _equalizer.Bands;
    public IReadOnlyList<EqualizerPreset> EqualizerPresets => _equalizer.Presets;
    public IReadOnlyList<string> EqualizerPresetNames { get; } = DefaultEqualizerPresets.Presets
        .Select(p => p.Name)
        .ToList();

    [ObservableProperty]
    private bool _isEqualizerEnabled;

    [ObservableProperty]
    private string _currentEqualizerPresetName = "Flat";

    partial void OnCurrentEqualizerPresetNameChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && value != "Custom")
        {
            if (!string.Equals(_equalizer.CurrentPresetName, value, StringComparison.OrdinalIgnoreCase))
            {
                SelectEqualizerPreset(value);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackTitle))]
    [NotifyPropertyChangedFor(nameof(ArtistNames))]
    [NotifyPropertyChangedFor(nameof(HasTrack))]
    private Track? _currentTrack;

    public string TrackTitle => !string.IsNullOrWhiteSpace(CurrentTrack?.Title)
        ? CurrentTrack.FullTitle
        : (Loc?.CurrentLanguage == "en" ? "No track playing" : "Трек не выбран");

    public string ArtistNames => !string.IsNullOrWhiteSpace(CurrentTrack?.ArtistNames)
        ? CurrentTrack.ArtistNames
        : (Loc?.CurrentLanguage == "en" ? "Select a track to start" : "Выберите трек для воспроизведения");

    public bool HasTrack => CurrentTrack != null;

    [ObservableProperty]
    private string? _coverUrl;

    [ObservableProperty]
    private string? _hdCoverUrl;

    [ObservableProperty]
    private bool _isNowPlayingDrawerOpen;

    [ObservableProperty]
    private bool _isAutoScrollEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSyncLyricsButton))]
    private bool _isUserScrolledAway;

    public event EventHandler<LyricLine?>? ActiveLyricChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph))]
    [NotifyPropertyChangedFor(nameof(LikeTooltip))]
    private bool _isCurrentTrackLiked;

    public string LikeGlyph => IsCurrentTrackLiked ? "\uEB52" : "\uEB51";

    public string LikeTooltip => IsCurrentTrackLiked 
        ? (Loc?.CurrentLanguage == "en" ? "Unlike" : "Удалить из избранного") 
        : (Loc?.CurrentLanguage == "en" ? "Like" : "В избранное");

    public string NowPlayingDrawerHeader => Loc?.CurrentLanguage == "en" ? "NOW PLAYING" : "СЕЙЧАС ИГРАЕТ";
    public string SyncToCurrentLineText => Loc?.CurrentLanguage == "en" ? "Sync to current line" : "К текущей строке";
    public string LoadingLyricsTitle => Loc?.CurrentLanguage == "en" ? "Loading lyrics..." : "Загрузка текста...";
    public string LoadingLyricsSubtitle => Loc?.CurrentLanguage == "en" ? "Looking for synchronized timecodes" : "Ищем синхронизированные таймкоды";
    public string EnjoyTheMusicText => Loc?.CurrentLanguage == "en" ? "Enjoy the music" : "Наслаждайтесь музыкой";

    public string AudioEqualizerTooltip => Loc?.CurrentLanguage == "en" ? "Audio Equalizer" : "Эквалайзер";
    public string EqualizerTitle => Loc?.CurrentLanguage == "en" ? "Equalizer" : "Эквалайзер";
    public string EqualizerResetText => Loc?.CurrentLanguage == "en" ? "Reset" : "Сброс";
    public string EqualizerResetTooltip => Loc?.CurrentLanguage == "en" ? "Reset all bands to 0 dB" : "Сбросить все полосы в 0 дБ";
    public string EqualizerPresetLabel => Loc?.CurrentLanguage == "en" ? "Preset:" : "Пресет:";
    public string ToggleOnContent => Loc?.CurrentLanguage == "en" ? "On" : "Вкл";
    public string ToggleOffContent => Loc?.CurrentLanguage == "en" ? "Off" : "Выкл";
    public string BestBitrateMenuText => Loc?.CurrentLanguage == "en" ? "Best Available Bitrate" : "Максимальный битрейт";

    public string ShuffleTooltip => Loc?.CurrentLanguage == "en" ? "Shuffle" : "Перемешать";
    public string PreviousTooltip => Loc?.CurrentLanguage == "en" ? "Previous" : "Предыдущий трек";
    public string PlayPauseTooltip => Loc?.CurrentLanguage == "en" ? "Play / Pause" : "Воспроизведение / Пауза";
    public string NextTooltip => Loc?.CurrentLanguage == "en" ? "Next" : "Следующий трек";
    public string RepeatTooltip => Loc?.CurrentLanguage == "en" ? "Repeat" : "Повтор";
    public string LyricsDrawerTooltip => Loc?.CurrentLanguage == "en" ? "Lyrics & Now Playing (Esc to close)" : "Текст песни и плеер (Esc для закрытия)";
    public string CollapseDrawerTooltip => Loc?.CurrentLanguage == "en" ? "Collapse Now Playing (Esc)" : "Свернуть плеер (Esc)";
    public string RecenterLyricsTooltip => Loc?.CurrentLanguage == "en" ? "Re-center lyrics on playing line" : "Вернуться к играющей строке";
    public string ViewAlbumTooltip => Loc?.CurrentLanguage == "en" ? "View Album" : "Перейти к альбому";
    public string FullscreenTooltip => Loc?.CurrentLanguage == "en" ? "Toggle Fullscreen" : "Полноэкранный режим";
    public string ArtistInfoTooltip
    {
        get
        {
            bool isEn = Loc?.CurrentLanguage == "en";
            if (CurrentTrack?.Artists?.Count > 1)
            {
                return isEn ? "Artists (click to choose)" : "Исполнители (нажмите для выбора)";
            }
            return isEn ? "About Artist" : "Об исполнителе";
        }
    }
    public string VolumeInputTooltip => Loc?.CurrentLanguage == "en" ? "Click to enter volume percentage" : "Нажмите, чтобы ввести процент громкости";
    public string FckCensorRepoMenuText => Loc?.CurrentLanguage == "en" ? "Original FckCensor project (GitHub)" : "Оригинальный проект FckCensor (GitHub)";

    public System.Collections.ObjectModel.ObservableCollection<LyricLine> Lyrics { get; } = new();

    [ObservableProperty]
    private LyricLine? _activeLyricLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoLyricsPlaceholder))]
    private bool _hasLyrics;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSyncLyricsButton))]
    [NotifyPropertyChangedFor(nameof(LyricsBadgeText))]
    [NotifyPropertyChangedFor(nameof(LyricsBadgeGlyph))]
    private bool _hasSyncedLyrics;

    public string LyricsBadgeText => HasSyncedLyrics
        ? (Loc?.CurrentLanguage == "en" ? "Synced Lyrics" : "Синхр. текст")
        : (Loc?.CurrentLanguage == "en" ? "Plain Lyrics" : "Текст");
    public string LyricsBadgeGlyph => HasSyncedLyrics ? "\uEC4F" : "\uE8D2";
    public bool ShowSyncLyricsButton => HasSyncedLyrics && IsUserScrolledAway;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoLyricsPlaceholder))]
    private bool _isLoadingLyrics;

    public bool ShowNoLyricsPlaceholder => !IsLoadingLyrics && !HasLyrics;

    [ObservableProperty]
    private string _lyricsStatusText = "Loading lyrics...";

    [ObservableProperty]
    private StreamAudioQuality? _currentQuality;

    [ObservableProperty]
    private string _qualityBadge = "AAC";

    [ObservableProperty]
    private string _qualityTooltip = "Stream Quality: AAC (High Quality)";

    [ObservableProperty]
    private AudioQualityPreference _selectedQualityPreference = AudioQualityPreference.AacPreferred;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedPosition))]
    private TimeSpan _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    [NotifyPropertyChangedFor(nameof(VolumePercentageText))]
    [NotifyPropertyChangedFor(nameof(VolumeTooltip))]
    private double _volume = 80;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    [NotifyPropertyChangedFor(nameof(VolumePercentageText))]
    [NotifyPropertyChangedFor(nameof(VolumeTooltip))]
    [NotifyPropertyChangedFor(nameof(MuteTooltip))]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatGlyph))]
    private RepeatMode _repeatMode = RepeatMode.None;

    public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";

    public string VolumeGlyph => IsMuted || Volume == 0
        ? "\uE74F" // Mute
        : (Volume < 33 ? "\uE993" : (Volume < 66 ? "\uE994" : "\uE767"));

    private bool IsRussian => Loc?.CurrentLanguage != "en";

    public string VolumePercentageText => $"{Math.Round(Volume):0}%";

    public string VolumeTooltip => IsMuted
        ? (IsRussian ? "Без звука" : "Muted")
        : (IsRussian ? $"Громкость: {Math.Round(Volume):0}%" : $"Volume: {Math.Round(Volume):0}%");

    public string MuteTooltip => IsMuted
        ? (IsRussian ? "Включить звук" : "Unmute")
        : (IsRussian ? "Выключить звук" : "Mute");

    public string RepeatGlyph => RepeatMode switch
    {
        RepeatMode.One => "\uE8ED", // Repeat One (solid Segoe Fluent)
        RepeatMode.All => "\uE8EE", // Repeat All (solid Segoe Fluent)
        _ => "\uE8EE"
    };

    public string FormattedPosition => Position.Hours > 0
        ? Position.ToString(@"h\:mm\:ss")
        : Position.ToString(@"m\:ss");

    public string FormattedDuration => Duration.Hours > 0
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    [ObservableProperty]
    private bool _isCurrentTrackReplaced;

    [ObservableProperty]
    private bool _canReportCurrentTrack;

    [ObservableProperty]
    private string? _toastMessage;

    [ObservableProperty]
    private bool _isToastOpen;

    private readonly ISettingsService _settingsService;
    private readonly IFckCensorService _fckCensorService;

    public PlayerBarViewModel(
        IAudioPlayerService audioPlayer,
        IYandexMusicService musicService,
        IEqualizerService equalizer,
        INavigationService navigationService,
        ISettingsService settingsService,
        IFckCensorService fckCensorService)
    {
        _audioPlayer = audioPlayer;
        _musicService = musicService;
        _equalizer = equalizer;
        _navigationService = navigationService;
        _settingsService = settingsService;
        _fckCensorService = fckCensorService;

        _volume = _audioPlayer.Volume;
        _isMuted = _audioPlayer.IsMuted;
        _repeatMode = _audioPlayer.RepeatMode;
        _isShuffle = _audioPlayer.IsShuffle;
        _selectedQualityPreference = _settingsService.PreferredQuality;
        _qualityBadge = _selectedQualityPreference switch
        {
            AudioQualityPreference.AacPreferred => "AAC",
            AudioQualityPreference.Mp3Preferred => "320k",
            _ => "HQ"
        };
        _isEqualizerEnabled = _equalizer.IsEnabled;
        _currentEqualizerPresetName = _equalizer.CurrentPresetName;

        _currentTrack = _audioPlayer.CurrentTrack;
        _isCurrentTrackLiked = _currentTrack != null && _musicService.IsTrackLiked(_currentTrack.Id);
        _coverUrl = _currentTrack?.CoverUrl;
        _hdCoverUrl = _currentTrack?.HdCoverUrl ?? _currentTrack?.CoverUrl;
        _duration = _currentTrack?.Duration ?? TimeSpan.Zero;
        _durationSeconds = _duration.TotalSeconds;
        _position = _audioPlayer.Position;
        _positionSeconds = _position.TotalSeconds;
        _isPlaying = _audioPlayer.IsPlaying;
        if (_currentTrack != null)
        {
            _ = LoadLyricsForTrackAsync(_currentTrack);
        }

        _settingsService.SettingChanged += (s, prop) =>
        {
            if (prop == nameof(ISettingsService.PreferredQuality))
            {
                RunOnUi(() =>
                {
                    SelectedQualityPreference = _settingsService.PreferredQuality;
                    QualityBadge = SelectedQualityPreference switch
                    {
                        AudioQualityPreference.AacPreferred => "AAC",
                        AudioQualityPreference.Mp3Preferred => "320k",
                        _ => "HQ"
                    };
                });
            }
        };

        _fckCensorService.TrackReplacementChanged += (s, id) =>
        {
            if (CurrentTrack != null && Track.IsSameTrackId(CurrentTrack.Id, id))
            {
                UpdateFckCensorState();
            }
        };
        UpdateFckCensorState();

        _audioPlayer.CurrentTrackChanged += OnCurrentTrackChanged;
        _audioPlayer.CurrentQualityChanged += OnCurrentQualityChanged;
        _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
        _audioPlayer.PositionChanged += OnPositionChanged;
        _audioPlayer.DurationChanged += OnDurationChanged;
        _audioPlayer.VolumeChanged += OnVolumeChanged;
        _audioPlayer.MuteChanged += OnMuteChanged;
        _audioPlayer.RepeatModeChanged += OnRepeatModeChanged;
        _audioPlayer.IsShuffleChanged += OnIsShuffleChanged;
        _equalizer.EqualizerChanged += OnEqualizerChanged;

        _musicService.TrackLikeChanged += (_, e) =>
        {
            RunOnUi(() =>
            {
                if (Track.IsSameTrackId(CurrentTrack?.Id, e.TrackId))
                {
                    IsCurrentTrackLiked = e.IsLiked;
                }
            });
        };

        _musicService.LikedTracksLoaded += (_, _) =>
        {
            RunOnUi(() =>
            {
                if (CurrentTrack != null)
                {
                    IsCurrentTrackLiked = _musicService.IsTrackLiked(CurrentTrack.Id);
                }
            });
        };
    }

    private void OnEqualizerChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            IsEqualizerEnabled = _equalizer.IsEnabled;
            CurrentEqualizerPresetName = _equalizer.CurrentPresetName;
        });
    }

    private void OnCurrentQualityChanged(object? sender, StreamAudioQuality? quality)
    {
        RunOnUi(() =>
        {
            CurrentQuality = quality;
            if (quality != null)
            {
                QualityBadge = quality.BadgeText;
                string cacheStatus = quality.IsCached ? " • Local Disk Cache (0 ms)" : string.Empty;
                QualityTooltip = $"Preferred: {GetPreferenceDisplayName(SelectedQualityPreference)} • Active: {quality.DisplayText}{cacheStatus}";
            }
        });
    }

    private static string GetPreferenceDisplayName(AudioQualityPreference pref) => pref switch
    {
        AudioQualityPreference.AacPreferred => "AAC",
        AudioQualityPreference.Mp3Preferred => "MP3",
        _ => "Best Available"
    };

    private void OnCurrentTrackChanged(object? sender, Track? track)
    {
        RunOnUi(() =>
        {
            CurrentTrack = track;
            CoverUrl = track?.CoverUrl;
            HdCoverUrl = track?.HdCoverUrl ?? track?.CoverUrl;
            Duration = track?.Duration ?? TimeSpan.Zero;
            DurationSeconds = Duration.TotalSeconds;
            Position = TimeSpan.Zero;
            PositionSeconds = 0;
            Lyrics.Clear();
            ActiveLyricLine = null;
            HasLyrics = false;
            HasSyncedLyrics = false;
            IsUserScrolledAway = false;
            IsCurrentTrackLiked = track != null && _musicService.IsTrackLiked(track.Id);
            if (track == null)
            {
                IsNowPlayingDrawerOpen = false;
                IsPlaying = false;
            }
        });

        UpdateFckCensorState();

        if (track != null)
        {
            _ = LoadLyricsForTrackAsync(track);
        }
    }

    private CancellationTokenSource? _lyricsCts;

    private async Task LoadLyricsForTrackAsync(Track track)
    {
        _lyricsCts?.Cancel();
        _lyricsCts?.Dispose();
        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        var token = cts.Token;

        RunOnUi(() =>
        {
            IsLoadingLyrics = true;
            LyricsStatusText = "Loading lyrics...";
        });

        try
        {
            double durationSec = track.DurationMs > 0 ? track.DurationMs / 1000.0 : DurationSeconds;
            var lines = await _musicService.GetTrackLyricsAsync(track.Id, track.Title, track.ArtistNames, durationSec, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;

            RunOnUi(() =>
            {
                if (token.IsCancellationRequested) return;
                Lyrics.Clear();
                if (lines.Count > 0)
                {
                    foreach (var line in lines)
                    {
                        Lyrics.Add(line);
                    }
                    HasLyrics = true;
                    HasSyncedLyrics = lines.Any(l => l.HasTimestamp && l.Timestamp >= TimeSpan.Zero);
                    LyricsStatusText = string.Empty;
                    UpdateActiveLyric(Position);
                }
                else
                {
                    HasLyrics = false;
                    HasSyncedLyrics = false;
                    LyricsStatusText = "No lyrics available for this track";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignored, track changed
        }
        catch
        {
            if (!token.IsCancellationRequested)
            {
                RunOnUi(() =>
                {
                    HasLyrics = false;
                    HasSyncedLyrics = false;
                    LyricsStatusText = "Failed to load lyrics";
                });
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                RunOnUi(() => IsLoadingLyrics = false);
            }
        }
    }

    private void OnIsPlayingChanged(object? sender, bool isPlaying)
    {
        RunOnUi(() => IsPlaying = isPlaying);
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_isUserSeeking)
        {
            return;
        }

        RunOnUi(() =>
        {
            Position = position;
            PositionSeconds = position.TotalSeconds;
            UpdateActiveLyric(position);
        });
    }

    private void UpdateActiveLyric(TimeSpan position)
    {
        if (Lyrics.Count == 0 || !HasSyncedLyrics) return;

        LyricLine? currentActive = null;
        int activeIndex = -1;

        for (int i = 0; i < Lyrics.Count; i++)
        {
            var line = Lyrics[i];
            var nextTimestamp = (i + 1 < Lyrics.Count) ? Lyrics[i + 1].Timestamp : TimeSpan.MaxValue;

            if (position >= line.Timestamp && position < nextTimestamp)
            {
                currentActive = line;
                activeIndex = i;
                break;
            }
        }

        // Update IsPast for all lines
        for (int i = 0; i < Lyrics.Count; i++)
        {
            var line = Lyrics[i];
            bool isPast = activeIndex >= 0 && i < activeIndex;
            if (line.IsPast != isPast)
            {
                line.IsPast = isPast;
            }
        }

        if (currentActive == null)
        {
            if (ActiveLyricLine != null)
            {
                ActiveLyricLine.IsActive = false;
                ActiveLyricLine = null;
                ActiveLyricChanged?.Invoke(this, null);
            }
        }
        else if (currentActive != ActiveLyricLine)
        {
            if (ActiveLyricLine != null)
            {
                ActiveLyricLine.IsActive = false;
            }
            currentActive.IsActive = true;
            ActiveLyricLine = currentActive;
            ActiveLyricChanged?.Invoke(this, currentActive);
        }
    }

    [RelayCommand]
    private void SeekToLyric(LyricLine line)
    {
        if (line != null && line.HasTimestamp && line.Timestamp >= TimeSpan.Zero)
        {
            IsUserScrolledAway = false;
            _audioPlayer.Seek(line.Timestamp);
            Position = line.Timestamp;
            PositionSeconds = line.Timestamp.TotalSeconds;
            UpdateActiveLyric(line.Timestamp);
        }
    }

    private void OnDurationChanged(object? sender, TimeSpan duration)
    {
        RunOnUi(() =>
        {
            Duration = duration;
            DurationSeconds = duration.TotalSeconds;
        });
    }

    private void OnVolumeChanged(object? sender, double volume)
    {
        RunOnUi(() => Volume = volume);
    }

    private void OnMuteChanged(object? sender, bool isMuted)
    {
        RunOnUi(() =>
        {
            IsMuted = isMuted;
            OnPropertyChanged(nameof(VolumePercentageText));
            OnPropertyChanged(nameof(VolumeTooltip));
            OnPropertyChanged(nameof(MuteTooltip));
        });
    }

    private void OnRepeatModeChanged(object? sender, RepeatMode repeatMode)
    {
        RunOnUi(() => RepeatMode = repeatMode);
    }

    private void OnIsShuffleChanged(object? sender, bool isShuffle)
    {
        RunOnUi(() => IsShuffle = isShuffle);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        _audioPlayer.TogglePlayPause();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        await _audioPlayer.NextAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        await _audioPlayer.PreviousAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _audioPlayer.IsShuffle = !_audioPlayer.IsShuffle;
    }

    [RelayCommand]
    private void CycleRepeatMode()
    {
        _audioPlayer.RepeatMode = _audioPlayer.RepeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.None
        };
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _audioPlayer.IsMuted = !_audioPlayer.IsMuted;
    }

    [RelayCommand]
    private async Task SetAudioQualityPreference(string preferenceString)
    {
        if (Enum.TryParse<AudioQualityPreference>(preferenceString, true, out var preference))
        {
            SelectedQualityPreference = preference;
            _settingsService.PreferredQuality = preference;
            QualityBadge = preference switch
            {
                AudioQualityPreference.AacPreferred => "AAC",
                AudioQualityPreference.Mp3Preferred => "320k",
                _ => "HQ"
            };
            QualityTooltip = preference switch
            {
                AudioQualityPreference.AacPreferred => "Stream Quality: AAC Preferred (High Quality)",
                AudioQualityPreference.Mp3Preferred => "Stream Quality: MP3 Preferred (320 kbps)",
                _ => "Stream Quality: Best Available Bitrate"
            };

            if (CurrentTrack != null)
            {
                await _audioPlayer.ReloadCurrentTrackQualityAsync().ConfigureAwait(false);
            }
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (Math.Abs(_audioPlayer.Volume - value) > 0.5)
        {
            _audioPlayer.Volume = value;
        }
        OnPropertyChanged(nameof(VolumePercentageText));
        OnPropertyChanged(nameof(VolumeTooltip));
    }

    public void ChangeVolume(double delta)
    {
        if (IsMuted && delta > 0)
        {
            IsMuted = false;
            _audioPlayer.IsMuted = false;
        }
        Volume = Math.Clamp(Math.Round(Volume + delta), 0.0, 100.0);
    }

    public void SetVolumeFromInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            OnPropertyChanged(nameof(VolumePercentageText));
            return;
        }

        string cleaned = input.Replace("%", "").Trim();
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ||
            double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out parsed))
        {
            if (IsMuted && parsed > 0)
            {
                IsMuted = false;
                _audioPlayer.IsMuted = false;
            }
            Volume = Math.Clamp(Math.Round(parsed), 0.0, 100.0);
        }
        OnPropertyChanged(nameof(VolumePercentageText));
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(TrackTitle));
        OnPropertyChanged(nameof(ArtistNames));
        OnPropertyChanged(nameof(LikeTooltip));
        OnPropertyChanged(nameof(VolumeTooltip));
        OnPropertyChanged(nameof(MuteTooltip));
        OnPropertyChanged(nameof(NowPlayingDrawerHeader));
        OnPropertyChanged(nameof(SyncToCurrentLineText));
        OnPropertyChanged(nameof(LoadingLyricsTitle));
        OnPropertyChanged(nameof(LoadingLyricsSubtitle));
        OnPropertyChanged(nameof(EnjoyTheMusicText));
        OnPropertyChanged(nameof(AudioEqualizerTooltip));
        OnPropertyChanged(nameof(EqualizerTitle));
        OnPropertyChanged(nameof(EqualizerResetText));
        OnPropertyChanged(nameof(EqualizerResetTooltip));
        OnPropertyChanged(nameof(EqualizerPresetLabel));
        OnPropertyChanged(nameof(ToggleOnContent));
        OnPropertyChanged(nameof(ToggleOffContent));
        OnPropertyChanged(nameof(BestBitrateMenuText));
        OnPropertyChanged(nameof(ShuffleTooltip));
        OnPropertyChanged(nameof(PreviousTooltip));
        OnPropertyChanged(nameof(PlayPauseTooltip));
        OnPropertyChanged(nameof(NextTooltip));
        OnPropertyChanged(nameof(RepeatTooltip));
        OnPropertyChanged(nameof(LyricsDrawerTooltip));
        OnPropertyChanged(nameof(CollapseDrawerTooltip));
        OnPropertyChanged(nameof(RecenterLyricsTooltip));
        OnPropertyChanged(nameof(ViewAlbumTooltip));
        OnPropertyChanged(nameof(FullscreenTooltip));
        OnPropertyChanged(nameof(ArtistInfoTooltip));
        OnPropertyChanged(nameof(VolumeInputTooltip));
        OnPropertyChanged(nameof(FckCensorRepoMenuText));
    }

    [RelayCommand]
    private void ToggleEqualizer()
    {
        IsEqualizerEnabled = !IsEqualizerEnabled;
        _equalizer.IsEnabled = IsEqualizerEnabled;
    }

    [RelayCommand]
    private void SelectEqualizerPreset(string presetName)
    {
        _equalizer.ApplyPreset(presetName);
        if (!_equalizer.IsEnabled)
        {
            _equalizer.IsEnabled = true;
            IsEqualizerEnabled = true;
        }
        CurrentEqualizerPresetName = _equalizer.CurrentPresetName;
    }

    [RelayCommand]
    private void ResetEqualizer()
    {
        _equalizer.ResetToFlat();
        CurrentEqualizerPresetName = _equalizer.CurrentPresetName;
    }

    partial void OnIsEqualizerEnabledChanged(bool value)
    {
        if (_equalizer.IsEnabled != value)
        {
            _equalizer.IsEnabled = value;
        }
    }

    private double _lastSoughtSeconds = -1;

    public void StartSeeking()
    {
        _isUserSeeking = true;
        _lastSoughtSeconds = -1;
    }

    public void FinishSeeking(double seconds)
    {
        _isUserSeeking = false;
        if (Math.Abs(_lastSoughtSeconds - seconds) < 0.25)
        {
            return;
        }

        _lastSoughtSeconds = seconds;
        var target = TimeSpan.FromSeconds(seconds);
        _audioPlayer.Seek(target);
        Position = target;
        PositionSeconds = seconds;
    }

    [RelayCommand]
    private async Task ToggleLikeAsync()
    {
        if (CurrentTrack == null) return;

        string trackId = CurrentTrack.Id;
        bool currentlyLiked = IsCurrentTrackLiked;
        IsCurrentTrackLiked = !currentlyLiked;

        try
        {
            if (!currentlyLiked)
            {
                bool ok = await _musicService.LikeTrackAsync(trackId).ConfigureAwait(false);
                if (!ok)
                {
                    RunOnUi(() => IsCurrentTrackLiked = false);
                }
            }
            else
            {
                bool ok = await _musicService.UnlikeTrackAsync(trackId).ConfigureAwait(false);
                if (!ok)
                {
                    RunOnUi(() => IsCurrentTrackLiked = true);
                }
            }
        }
        catch
        {
            RunOnUi(() => IsCurrentTrackLiked = currentlyLiked);
        }
    }

    [RelayCommand]
    public async Task OpenCurrentAlbumAsync()
    {
        if (CurrentTrack == null) return;
        IsNowPlayingDrawerOpen = false;

        Album? targetAlbum = CurrentTrack.Albums?.FirstOrDefault();
        if (targetAlbum != null && targetAlbum.Id > 0)
        {
            await _navigationService.NavigateToAlbumAsync(targetAlbum);
            return;
        }

        if (!string.IsNullOrEmpty(CurrentTrack.Id) && CurrentTrack.Id.Contains(':'))
        {
            string[] parts = CurrentTrack.Id.Split(':');
            if (parts.Length > 1 && long.TryParse(parts[1], out long albumId) && albumId > 0)
            {
                await _navigationService.NavigateToAlbumByIdAsync(albumId, CurrentTrack.AlbumTitle);
                return;
            }
        }

        if (!string.IsNullOrEmpty(CurrentTrack.AlbumTitle) && targetAlbum != null)
        {
            await _navigationService.NavigateToAlbumAsync(targetAlbum);
        }
    }

    [RelayCommand]
    public async Task OpenCurrentArtistAsync(object? parameter = null)
    {
        if (CurrentTrack == null) return;
        var anchor = parameter as FrameworkElement;
        await ArtistNavigationHelper.NavigateToTrackArtistAsync(anchor, CurrentTrack, () => IsNowPlayingDrawerOpen = false);
    }

    [RelayCommand]
    private void ToggleNowPlayingDrawer()
    {
        IsNowPlayingDrawerOpen = !IsNowPlayingDrawerOpen;
    }

    [RelayCommand]
    private void OpenNowPlayingDrawer()
    {
        IsNowPlayingDrawerOpen = true;
    }

    [RelayCommand]
    private void CloseNowPlayingDrawer()
    {
        IsNowPlayingDrawerOpen = false;
    }

    [RelayCommand]
    private void ResumeAutoScroll()
    {
        IsUserScrolledAway = false;
        if (ActiveLyricLine != null)
        {
            ActiveLyricChanged?.Invoke(this, ActiveLyricLine);
        }
    }

    private void UpdateFckCensorState()
    {
        RunOnUi(() =>
        {
            if (CurrentTrack == null)
            {
                IsCurrentTrackReplaced = false;
                CanReportCurrentTrack = false;
                return;
            }

            IsCurrentTrackReplaced = _fckCensorService.IsReplaced(CurrentTrack.Id);
            CanReportCurrentTrack = !IsCurrentTrackReplaced && !_fckCensorService.IsReported(CurrentTrack.Id);
        });
    }

    [RelayCommand]
    public async Task ReplaceCurrentTrackAsync()
    {
        if (CurrentTrack == null) return;

        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");

            if (App.MainWindow != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                bool ok = await _fckCensorService.ReplaceTrackWithFileAsync(CurrentTrack.Id, file.Path);
                if (ok)
                {
                    UpdateFckCensorState();
                    ShowToast("Трек успешно подменён (FckCensor)");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReplaceCurrentTrackAsync] {ex.Message}");
            ShowToast("Ошибка при выборе аудиофайла");
        }
    }

    [RelayCommand]
    public async Task RemoveCurrentTrackReplacementAsync()
    {
        if (CurrentTrack == null) return;

        bool ok = await _fckCensorService.RemoveReplacementAsync(CurrentTrack.Id);
        if (ok)
        {
            UpdateFckCensorState();
            ShowToast("Трек восстановлен к оригиналу");
        }
    }

    [RelayCommand]
    public async Task ReportCurrentTrackCensorshipAsync()
    {
        if (CurrentTrack == null) return;

        bool ok = await _fckCensorService.ReportCensorshipAsync(CurrentTrack.Id, replaced: false);
        if (ok)
        {
            UpdateFckCensorState();
            ShowToast("Спасибо! Трек скоро будет добавлен в базу.");
        }
    }

    [RelayCommand]
    public async Task OpenFckCensorRepoAsync()
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Hazzz895/FckCensor"));
        }
        catch { }
    }

    public void ShowToast(string message)
    {
        RunOnUi(() =>
        {
            ToastMessage = message;
            IsToastOpen = true;
        });

        _ = Task.Delay(3000).ContinueWith(_ =>
        {
            RunOnUi(() => IsToastOpen = false);
        });
    }
}
