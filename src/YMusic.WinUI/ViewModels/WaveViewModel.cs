#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class WaveViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;

    private string? _currentBatchId;
    private bool _isWaveActive;
    private bool _isReplenishing;

    public ObservableCollection<WavePreset> Presets { get; } = new();
    public ObservableCollection<Track> UpcomingTracks { get; } = new();

    [ObservableProperty]
    private WavePreset _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph))]
    [NotifyPropertyChangedFor(nameof(LikeTooltip))]
    private bool _isCurrentTrackLiked;

    public string LikeGlyph => IsCurrentTrackLiked ? "\uEB52" : "\uEB51";

    public string LikeTooltip => IsCurrentTrackLiked ? "Unlike" : "Like (Train My Wave)";

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    public bool IsWavePlaying => _isWaveActive && IsPlaying && _audioPlayer.CurrentPlaybackMode == PlaybackMode.Wave;

    public string PlayPauseGlyph => IsWavePlaying ? "\uE769" : "\uE768";

    [ObservableProperty]
    private bool _isWaveActiveState;

    [ObservableProperty]
    private string _statusText = "Press Play to start your infinite personalized stream";

    public WaveViewModel(IYandexMusicService musicService, IAudioPlayerService audioPlayer)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;

        InitializePresets();
        _selectedPreset = Presets[0];

        _currentTrack = _audioPlayer.CurrentTrack;
        _isCurrentTrackLiked = _currentTrack != null && _musicService.IsTrackLiked(_currentTrack.Id);
        _isPlaying = _audioPlayer.IsPlaying;

        _audioPlayer.CurrentTrackChanged += OnAudioPlayerCurrentTrackChanged;
        _audioPlayer.IsPlayingChanged += (_, playing) => RunOnUi(() =>
        {
            IsPlaying = playing;
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));
        });

        _audioPlayer.PlaybackModeChanged += (_, mode) =>
        {
            if (mode != PlaybackMode.Wave)
            {
                DeactivateWave();
            }
        };

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

    public void DeactivateWave()
    {
        _isWaveActive = false;
        RunOnUi(() =>
        {
            IsWaveActiveState = false;
            UpcomingTracks.Clear();
            StatusText = "Press Play to start your infinite personalized stream";
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));
        });
    }

    public void SyncPlayerState()
    {
        CurrentTrack = _audioPlayer.CurrentTrack;
        IsPlaying = _audioPlayer.IsPlaying;
        IsCurrentTrackLiked = CurrentTrack != null && _musicService.IsTrackLiked(CurrentTrack.Id);

        if (_audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            _isWaveActive = false;
            IsWaveActiveState = false;
            UpcomingTracks.Clear();
            StatusText = "Press Play to start your infinite personalized stream";
        }
        else
        {
            _isWaveActive = true;
            IsWaveActiveState = true;
            UpcomingTracks.Clear();
            int curIndex = _audioPlayer.CurrentIndex;
            if (curIndex >= 0 && curIndex < _audioPlayer.Queue.Count)
            {
                foreach (var t in _audioPlayer.Queue.Skip(curIndex + 1))
                {
                    UpcomingTracks.Add(t);
                }
            }
            StatusText = $"\"{SelectedPreset.Title}\" wave active";
        }

        OnPropertyChanged(nameof(IsWavePlaying));
        OnPropertyChanged(nameof(PlayPauseGlyph));
    }

    private void InitializePresets()
    {
        Presets.Add(new WavePreset
        {
            Title = "My Wave",
            Description = "Personal stream tailored to your music taste",
            StationType = "user",
            StationTag = "onyourwave",
            Glyph = "\uE768",
            GradientStart = "#FF0844",
            GradientEnd = "#FFB199"
        });

        Presets.Add(new WavePreset
        {
            Title = "Energetic",
            Description = "Upbeat drive and high energy rhythms",
            StationType = "mood",
            StationTag = "energetic",
            Glyph = "\uE735",
            GradientStart = "#F857A6",
            GradientEnd = "#FF5858"
        });

        Presets.Add(new WavePreset
        {
            Title = "Calm",
            Description = "Relaxing chill, soft ambient and melodic vibes",
            StationType = "mood",
            StationTag = "calm",
            Glyph = "\uE945",
            GradientStart = "#00C9FF",
            GradientEnd = "#92FE9D"
        });

        Presets.Add(new WavePreset
        {
            Title = "Focus",
            Description = "Deep focus, concentration and flow without lyrics",
            StationType = "activity",
            StationTag = "work",
            Glyph = "\uE82D",
            GradientStart = "#654EA3",
            GradientEnd = "#EAAFC8"
        });

        Presets.Add(new WavePreset
        {
            Title = "Rock",
            Description = "Driving guitar riffs, alt and rock classics",
            StationType = "genre",
            StationTag = "rock",
            Glyph = "\uE8D6",
            GradientStart = "#232526",
            GradientEnd = "#414345"
        });

        Presets.Add(new WavePreset
        {
            Title = "Electronic",
            Description = "Synthesizers, punchy bass and electronic vibes",
            StationType = "genre",
            StationTag = "electronic",
            Glyph = "\uE8EC",
            GradientStart = "#00B4DB",
            GradientEnd = "#0083B0"
        });

        Presets.Add(new WavePreset
        {
            Title = "Hip-Hop",
            Description = "Groovy beats, punchy flow and top hip-hop",
            StationType = "genre",
            StationTag = "hiphop",
            Glyph = "\uE720",
            GradientStart = "#F12711",
            GradientEnd = "#F5AF19"
        });

        Presets.Add(new WavePreset
        {
            Title = "Indie",
            Description = "Atmospheric indie-pop, indie-rock and fresh releases",
            StationType = "genre",
            StationTag = "indie",
            Glyph = "\uE790",
            GradientStart = "#7F00FF",
            GradientEnd = "#E100FF"
        });
    }

    [RelayCommand]
    public async Task StartWaveAsync(WavePreset? preset = null)
    {
        var targetPreset = preset ?? SelectedPreset;
        SelectedPreset = targetPreset;

        SetBusy(true);
        ClearError();
        StatusText = $"Tuning tracks for \"{targetPreset.Title}\"...";

        try
        {
            var stationResult = await _musicService.GetRotorStationTracksAsync(
                targetPreset.StationType,
                targetPreset.StationTag).ConfigureAwait(false);

            if (stationResult?.Sequence == null || stationResult.Sequence.Count == 0)
            {
                SetError("Could not retrieve tracks from station. Please try again.");
                return;
            }

            _currentBatchId = stationResult.BatchId;
            _isWaveActive = true;
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Wave;

            var tracks = stationResult.Sequence
                .Where(item => item.Track != null)
                .Select(item => item.Track!)
                .ToList();

            if (tracks.Count == 0)
            {
                SetError("No playable tracks received in station response.");
                return;
            }

            RunOnUi(() =>
            {
                UpcomingTracks.Clear();
                foreach (var t in tracks.Skip(1))
                {
                    UpcomingTracks.Add(t);
                }
                IsWaveActiveState = true;
                StatusText = $"\"{targetPreset.Title}\" wave active";
                OnPropertyChanged(nameof(IsWavePlaying));
                OnPropertyChanged(nameof(PlayPauseGlyph));
            });

            await _audioPlayer.PlayQueueAsync(tracks, 0).ConfigureAwait(false);

            // Send radioStarted and trackStarted telemetry
            var firstTrack = tracks[0];
            _ = _musicService.SendRotorFeedbackAsync(
                "radioStarted",
                targetPreset.StationType,
                targetPreset.StationTag,
                firstTrack.Id,
                _currentBatchId);

            _ = _musicService.SendRotorFeedbackAsync(
                "trackStarted",
                targetPreset.StationType,
                targetPreset.StationTag,
                firstTrack.Id,
                _currentBatchId);
        }
        catch (Exception ex)
        {
            SetError($"Error starting My Wave: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (!_isWaveActive || _audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            _ = StartWaveAsync();
            return;
        }

        _audioPlayer.TogglePlayPause();
    }

    [RelayCommand]
    public async Task NextWaveTrackAsync()
    {
        if (!_isWaveActive || _audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            await _audioPlayer.NextAsync().ConfigureAwait(false);
            return;
        }

        if (_audioPlayer.CurrentTrack != null)
        {
            _ = _musicService.SendRotorFeedbackAsync(
                "skip",
                SelectedPreset.StationType,
                SelectedPreset.StationTag,
                _audioPlayer.CurrentTrack.Id,
                _currentBatchId);
        }

        await _audioPlayer.NextAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task ToggleLikeAsync()
    {
        if (_audioPlayer.CurrentTrack == null)
        {
            return;
        }

        string trackId = _audioPlayer.CurrentTrack.Id;
        bool currentlyLiked = IsCurrentTrackLiked;
        IsCurrentTrackLiked = !currentlyLiked;

        try
        {
            if (!currentlyLiked)
            {
                if (_isWaveActive && _audioPlayer.CurrentPlaybackMode == PlaybackMode.Wave)
                {
                    _ = _musicService.SendRotorFeedbackAsync(
                        "like",
                        SelectedPreset.StationType,
                        SelectedPreset.StationTag,
                        trackId,
                        _currentBatchId);
                }

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
    public async Task PlayUpcomingTrackAsync(Track? track)
    {
        if (track == null) return;

        int index = UpcomingTracks.IndexOf(track);
        if (index < 0) return;

        // Tracks starting from the clicked track
        var tracksToPlay = UpcomingTracks.Skip(index).ToList();

        // Send skip feedback for the track currently playing
        if (_audioPlayer.CurrentTrack != null && _isWaveActive && _audioPlayer.CurrentPlaybackMode == PlaybackMode.Wave)
        {
            _ = _musicService.SendRotorFeedbackAsync(
                "skip",
                SelectedPreset.StationType,
                SelectedPreset.StationTag,
                _audioPlayer.CurrentTrack.Id,
                _currentBatchId);
        }

        // Remove tracks up to and including the clicked one from UpcomingTracks
        RunOnUi(() =>
        {
            for (int i = 0; i <= index; i++)
            {
                UpcomingTracks.RemoveAt(0);
            }
        });

        _isWaveActive = true;
        _audioPlayer.CurrentPlaybackMode = PlaybackMode.Wave;
        RunOnUi(() =>
        {
            IsWaveActiveState = true;
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));
        });

        // Play the remaining queue starting with the clicked track
        await _audioPlayer.PlayQueueAsync(tracksToPlay, 0).ConfigureAwait(false);

        // Send trackStarted feedback
        _ = _musicService.SendRotorFeedbackAsync(
            "trackStarted",
            SelectedPreset.StationType,
            SelectedPreset.StationTag,
            track.Id,
            _currentBatchId);

        // Check if queue needs replenishment
        CheckReplenishWave(track.Id);
    }

    [RelayCommand]
    public async Task SendDislikeAndSkipAsync()
    {
        if (!_isWaveActive || _audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            return;
        }

        if (_audioPlayer.CurrentTrack == null)
        {
            return;
        }

        var track = _audioPlayer.CurrentTrack;
        _ = _musicService.SendRotorFeedbackAsync(
            "dislike",
            SelectedPreset.StationType,
            SelectedPreset.StationTag,
            track.Id,
            _currentBatchId);

        await _audioPlayer.NextAsync().ConfigureAwait(false);
    }

    private void OnAudioPlayerCurrentTrackChanged(object? sender, Track? track)
    {
        if (_audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            if (_isWaveActive)
            {
                DeactivateWave();
            }
            RunOnUi(() =>
            {
                CurrentTrack = track;
                IsCurrentTrackLiked = track != null && _musicService.IsTrackLiked(track.Id);
                OnPropertyChanged(nameof(IsWavePlaying));
                OnPropertyChanged(nameof(PlayPauseGlyph));
            });
            return;
        }

        RunOnUi(() =>
        {
            CurrentTrack = track;
            IsCurrentTrackLiked = track != null && _musicService.IsTrackLiked(track.Id);
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));

            if (_isWaveActive && track != null)
            {
                // Remove passed track and all preceding ones from UpcomingTracks
                var existing = UpcomingTracks.FirstOrDefault(t => t.Id == track.Id);
                if (existing != null)
                {
                    int idx = UpcomingTracks.IndexOf(existing);
                    for (int i = 0; i <= idx; i++)
                    {
                        UpcomingTracks.RemoveAt(0);
                    }
                }
            }
        });

        if (!_isWaveActive || track == null)
        {
            return;
        }

        // Send trackStarted feedback
        _ = _musicService.SendRotorFeedbackAsync(
            "trackStarted",
            SelectedPreset.StationType,
            SelectedPreset.StationTag,
            track.Id,
            _currentBatchId);

        // Check if we need to replenish the queue
        CheckReplenishWave(track.Id);
    }

    private void CheckReplenishWave(string currentTrackId)
    {
        if (_isReplenishing)
        {
            return;
        }

        int currentIndex = _audioPlayer.CurrentIndex;
        int queueCount = _audioPlayer.Queue.Count;

        // If 3 or fewer tracks remain in the queue, load the next batch
        if (queueCount - currentIndex <= 3)
        {
            _isReplenishing = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _musicService.GetRotorStationTracksAsync(
                        SelectedPreset.StationType,
                        SelectedPreset.StationTag,
                        currentTrackId).ConfigureAwait(false);

                    if (result?.Sequence != null && result.Sequence.Count > 0)
                    {
                        var newTracks = result.Sequence
                            .Where(i => i.Track != null)
                            .Select(i => i.Track!)
                            .ToList();

                        _audioPlayer.AppendToQueue(newTracks);

                        RunOnUi(() =>
                        {
                            foreach (var nt in newTracks)
                            {
                                UpcomingTracks.Add(nt);
                            }
                        });
                    }
                }
                catch
                {
                    // Non-fatal, will retry on next track change
                }
                finally
                {
                    _isReplenishing = false;
                }
            });
        }
    }
}
