#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class ExploreViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ITrackPlaybackCoordinator _playbackCoordinator;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly IPinnedItemsService _pinnedItemsService;

    public ObservableCollection<Playlist> PersonalPlaylists { get; } = new();
    public ObservableCollection<Playlist> EditorialPlaylists { get; } = new();
    public ObservableCollection<Album> NewReleases { get; } = new();
    public ObservableCollection<Track> ChartTracks { get; } = new();
    public ObservableCollection<Track> DetailTracks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseVisible))]
    private bool _isDetailOpen;

    public bool IsBrowseVisible => !IsDetailOpen;

    private string GetLoc(string key, string fallback)
    {
        var val = Loc?[key];
        return (!string.IsNullOrEmpty(val) && val != key) ? val : fallback;
    }

    public string HeaderTitle => GetLoc("Nav_Explore", "Главное");
    public string HeaderSubtitle => GetLoc("Explore_Subtitle", Loc?.CurrentLanguage == "en" ? "Personal mixes, curated playlists, fresh albums & charts from Yandex" : "Персональные миксы, плейлисты редакции, свежие релизы и чарты Яндекса");
    public string PersonalPlaylistsTitle => GetLoc("Explore_PickedForYou", Loc?.CurrentLanguage == "en" ? "Made For You" : "Собрано для вас");
    public string NewReleasesTitle => GetLoc("Explore_NewReleases", Loc?.CurrentLanguage == "en" ? "New Releases & Albums" : "Новые релизы и альбомы");
    public string EditorialPlaylistsTitle => GetLoc("Explore_PopularPlaylists", Loc?.CurrentLanguage == "en" ? "Popular Playlists" : "Популярные плейлисты");
    public string ChartTitle => GetLoc("Explore_TopChart", Loc?.CurrentLanguage == "en" ? "Top Chart" : "Чарт");

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(PersonalPlaylistsTitle));
        OnPropertyChanged(nameof(NewReleasesTitle));
        OnPropertyChanged(nameof(EditorialPlaylistsTitle));
        OnPropertyChanged(nameof(ChartTitle));
    }

    [ObservableProperty]
    private string _detailTitle = string.Empty;

    [ObservableProperty]
    private string _detailSubtitle = string.Empty;

    [ObservableProperty]
    private string _detailDescription = string.Empty;

    [ObservableProperty]
    private string? _detailCoverUrl;

    [ObservableProperty]
    private string _detailMeta = string.Empty;

    [ObservableProperty]
    private string _detailTypeBadge = "PLAYLIST";

    [ObservableProperty]
    private Playlist? _activePlaylist;

    [ObservableProperty]
    private Album? _activeAlbum;

    [ObservableProperty]
    private bool _isLoadingDetail;

    [ObservableProperty]
    private bool _isDetailPinned;

    public bool HasLoaded { get; private set; }

    public ExploreViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        ITrackPlaybackCoordinator playbackCoordinator,
        ICredentialStorageService credentialStorage,
        IPinnedItemsService pinnedItemsService)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _playbackCoordinator = playbackCoordinator;
        _credentialStorage = credentialStorage;
        _pinnedItemsService = pinnedItemsService;

        _pinnedItemsService.PinStateChanged += OnPinStateChanged;
        _playbackCoordinator.Register(DetailTracks);
        _playbackCoordinator.Register(ChartTracks);
    }

    [RelayCommand]
    public async Task LoadExploreDataAsync()
    {
        SetBusy(true);
        ClearError();

        try
        {
            if (string.IsNullOrWhiteSpace(_musicService.CurrentToken))
            {
                var token = _credentialStorage.GetToken();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _musicService.SetOAuthToken(token);
                }
            }

            var personalTask = _musicService.GetPersonalPlaylistsAsync();
            var editorialTask = _musicService.GetEditorialPlaylistsAsync();
            var albumsTask = _musicService.GetNewReleasesAsync();
            var chartTask = _musicService.GetChartTracksAsync();

            await Task.WhenAll(personalTask, editorialTask, albumsTask, chartTask).ConfigureAwait(false);

            var personal = await personalTask.ConfigureAwait(false);
            var editorial = await editorialTask.ConfigureAwait(false);
            var albums = await albumsTask.ConfigureAwait(false);
            var chart = await chartTask.ConfigureAwait(false);

            RunOnUi(() =>
            {
                PersonalPlaylists.Clear();
                foreach (var p in personal)
                {
                    PersonalPlaylists.Add(p);
                }

                EditorialPlaylists.Clear();
                foreach (var p in editorial)
                {
                    EditorialPlaylists.Add(p);
                }

                NewReleases.Clear();
                foreach (var a in albums)
                {
                    NewReleases.Add(a);
                }

                ChartTracks.Clear();
                foreach (var t in chart)
                {
                    ChartTracks.Add(t);
                }

                _playbackCoordinator.Sync(ChartTracks);
                HasLoaded = true;
                System.Diagnostics.Debug.WriteLine($"[Explore] Personal: {PersonalPlaylists.Count}, Editorial: {EditorialPlaylists.Count}, Albums: {NewReleases.Count}, Chart: {ChartTracks.Count}");
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load Explore content: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Explore Error] {ex}");
        }

        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public async Task OpenPlaylistAsync(Playlist? playlist)
    {
        if (playlist == null) return;

        ActivePlaylist = playlist;
        ActiveAlbum = null;
        DetailTitle = playlist.Title;
        DetailSubtitle = playlist.OwnerName;
        DetailDescription = playlist.Description ?? string.Empty;
        DetailCoverUrl = playlist.CoverUrl;
        DetailTypeBadge = playlist.Owner?.Login?.Contains("daily") == true ? "DAILY MIX" : "PLAYLIST";
        DetailMeta = $"{playlist.TrackCount} tracks";
        DetailTracks.Clear();
        IsDetailPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
        playlist.IsPinned = IsDetailPinned;
        IsDetailOpen = true;
        IsLoadingDetail = true;

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            var fullPlaylist = await _musicService.GetPlaylistWithTracksAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            var tracks = fullPlaylist?.ResolvedTracks ?? new List<Track>();

            if (tracks.Count == 0 && fullPlaylist?.Tracks != null && fullPlaylist.Tracks.Count > 0)
            {
                var trackIds = fullPlaylist.Tracks.Select(t => t.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                if (trackIds.Count > 0)
                {
                    var fetched = await _musicService.GetTracksAsync(trackIds).ConfigureAwait(false);
                    tracks = fetched.ToList();
                }
            }

            RunOnUi(() =>
            {
                DetailTracks.Clear();
                long totalMs = 0;
                foreach (var t in tracks)
                {
                    DetailTracks.Add(t);
                    totalMs += t.DurationMs;
                }

                var totalSpan = TimeSpan.FromMilliseconds(totalMs);
                string durStr = totalSpan.Hours > 0
                    ? $"{totalSpan.Hours} hr {totalSpan.Minutes} min"
                    : $"{totalSpan.Minutes} min";
                DetailMeta = $"{DetailTracks.Count} tracks • {durStr}";

                _playbackCoordinator.Sync(DetailTracks);
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load playlist: {ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsLoadingDetail = false);
        }
    }

    [RelayCommand]
    public async Task OpenAlbumAsync(Album? album)
    {
        if (album == null) return;

        ActiveAlbum = album;
        ActivePlaylist = null;
        DetailTitle = album.Title;
        DetailSubtitle = album.ArtistNames;
        DetailDescription = !string.IsNullOrWhiteSpace(album.Description) ? album.Description : (!string.IsNullOrWhiteSpace(album.Genre) ? $"Genre: {album.Genre}" : string.Empty);
        DetailCoverUrl = album.CoverUrl;
        DetailTypeBadge = "ALBUM";
        DetailMeta = album.Year.HasValue ? $"{album.Year.Value} • {album.TrackCount} tracks" : $"{album.TrackCount} tracks";
        DetailTracks.Clear();
        IsDetailPinned = _pinnedItemsService.IsAlbumPinned(album.Id);
        album.IsPinned = IsDetailPinned;
        IsDetailOpen = true;
        IsLoadingDetail = true;

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            var fullAlbum = await _musicService.GetAlbumWithTracksAsync(album.Id).ConfigureAwait(false);
            var tracks = fullAlbum?.AllTracks ?? new List<Track>();

            RunOnUi(() =>
            {
                DetailTracks.Clear();
                long totalMs = 0;
                foreach (var t in tracks)
                {
                    DetailTracks.Add(t);
                    totalMs += t.DurationMs;
                }

                var totalSpan = TimeSpan.FromMilliseconds(totalMs);
                string durStr = totalSpan.Hours > 0
                    ? $"{totalSpan.Hours} hr {totalSpan.Minutes} min"
                    : $"{totalSpan.Minutes} min";
                string yearStr = album.Year.HasValue ? $"{album.Year.Value} • " : string.Empty;
                DetailMeta = $"{yearStr}{DetailTracks.Count} tracks • {durStr}";

                _playbackCoordinator.Sync(DetailTracks);
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load album: {ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsLoadingDetail = false);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        IsDetailOpen = false;
        ActivePlaylist = null;
        ActiveAlbum = null;
        DetailTracks.Clear();
    }

    [RelayCommand]
    public async Task PlayPlaylistDirectAsync(Playlist? playlist)
    {
        if (playlist == null) return;

        try
        {
            var full = await _musicService.GetPlaylistWithTracksAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            var tracks = full?.ResolvedTracks ?? new List<Track>();
            if (tracks.Count == 0 && full?.Tracks != null && full.Tracks.Count > 0)
            {
                var trackIds = full.Tracks.Select(t => t.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                if (trackIds.Count > 0)
                {
                    var fetched = await _musicService.GetTracksAsync(trackIds).ConfigureAwait(false);
                    tracks = fetched.ToList();
                }
            }

            if (tracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
                await _audioPlayer.PlayQueueAsync(tracks, 0).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to play playlist: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task PlayAlbumDirectAsync(Album? album)
    {
        if (album == null) return;

        try
        {
            var full = await _musicService.GetAlbumWithTracksAsync(album.Id).ConfigureAwait(false);
            var tracks = full?.AllTracks ?? new List<Track>();
            if (tracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Album;
                await _audioPlayer.PlayQueueAsync(tracks, 0).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to play album: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task PlayDetailAllAsync()
    {
        if (DetailTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = ActiveAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
            await _audioPlayer.PlayQueueAsync(DetailTracks.ToList(), 0).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task ShufflePlayDetailAsync()
    {
        if (DetailTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = ActiveAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
            _audioPlayer.IsShuffle = true;
            await _audioPlayer.PlayQueueAsync(DetailTracks.ToList(), 0).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task PlayTrackAsync(Track? track)
    {
        if (track == null) return;

        if (Track.IsSameTrackId(track.Id, _audioPlayer.CurrentTrack?.Id))
        {
            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Pause();
            }
            else
            {
                _audioPlayer.Play();
            }
            return;
        }

        var sourceList = DetailTracks.Contains(track) ? DetailTracks.ToList() : (ChartTracks.Contains(track) ? ChartTracks.ToList() : new List<Track> { track });
        _audioPlayer.CurrentPlaybackMode = ActiveAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
        await _audioPlayer.PlayTrackAsync(track, sourceList).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task ToggleLikeTrackAsync(Track? track)
    {
        if (track == null) return;

        bool targetLiked = !(track.IsLiked || _musicService.IsTrackLiked(track.Id));
        track.IsLiked = targetLiked;

        if (targetLiked)
        {
            await _musicService.LikeTrackAsync(track.Id).ConfigureAwait(false);
        }
        else
        {
            await _musicService.UnlikeTrackAsync(track.Id).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public void ToggleDetailPin()
    {
        if (ActivePlaylist != null)
        {
            IsDetailPinned = _pinnedItemsService.TogglePlaylistPin(ActivePlaylist.Uid, ActivePlaylist.Kind, ActivePlaylist.Title, ActivePlaylist.CoverUrl);
            ActivePlaylist.IsPinned = IsDetailPinned;
        }
        else if (ActiveAlbum != null)
        {
            IsDetailPinned = _pinnedItemsService.ToggleAlbumPin(ActiveAlbum.Id, ActiveAlbum.Title, ActiveAlbum.CoverUrl);
            ActiveAlbum.IsPinned = IsDetailPinned;
        }
    }

    private void OnPinStateChanged(object? sender, string key)
    {
        RunOnUi(() =>
        {
            if (ActivePlaylist != null && string.Equals(ActivePlaylist.PinKey, key, StringComparison.OrdinalIgnoreCase))
            {
                IsDetailPinned = _pinnedItemsService.IsPinned(key);
                ActivePlaylist.IsPinned = IsDetailPinned;
            }
            else if (ActiveAlbum != null && string.Equals(ActiveAlbum.PinKey, key, StringComparison.OrdinalIgnoreCase))
            {
                IsDetailPinned = _pinnedItemsService.IsPinned(key);
                ActiveAlbum.IsPinned = IsDetailPinned;
            }
        });
    }
}
