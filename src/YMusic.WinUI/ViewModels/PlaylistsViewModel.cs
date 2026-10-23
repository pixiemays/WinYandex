#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class PlaylistsViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly ITrackPlaybackCoordinator _playbackCoordinator;
    private readonly IPinnedItemsService _pinnedItemsService;

    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<Album> LikedAlbums { get; } = new();
    public ObservableCollection<Track> SelectedPlaylistTracks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailTitle))]
    [NotifyPropertyChangedFor(nameof(DetailSubtitle))]
    [NotifyPropertyChangedFor(nameof(DetailCoverUrl))]
    [NotifyPropertyChangedFor(nameof(DetailDescription))]
    [NotifyPropertyChangedFor(nameof(DetailTypeBadge))]
    [NotifyPropertyChangedFor(nameof(DetailPlayButtonText))]
    [NotifyPropertyChangedFor(nameof(IsDetailPinned))]
    [NotifyPropertyChangedFor(nameof(IsDetailLiked))]
    private Playlist? _selectedPlaylist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailTitle))]
    [NotifyPropertyChangedFor(nameof(DetailSubtitle))]
    [NotifyPropertyChangedFor(nameof(DetailCoverUrl))]
    [NotifyPropertyChangedFor(nameof(DetailDescription))]
    [NotifyPropertyChangedFor(nameof(DetailTypeBadge))]
    [NotifyPropertyChangedFor(nameof(DetailPlayButtonText))]
    [NotifyPropertyChangedFor(nameof(IsDetailPinned))]
    [NotifyPropertyChangedFor(nameof(IsDetailLiked))]
    private Album? _selectedAlbum;

    public bool IsDetailPinned =>
        SelectedPlaylist != null
            ? _pinnedItemsService.IsPlaylistPinned(SelectedPlaylist.Uid, SelectedPlaylist.Kind)
            : SelectedAlbum != null
                ? _pinnedItemsService.IsAlbumPinned(SelectedAlbum.Id)
                : false;

    public bool IsDetailLiked =>
        SelectedPlaylist != null
            ? SelectedPlaylist.IsLiked
            : SelectedAlbum != null
                ? SelectedAlbum.IsLiked
                : false;

    partial void OnSelectedPlaylistChanged(Playlist? value)
    {
        if (value != null)
        {
            value.IsPinned = _pinnedItemsService.IsPlaylistPinned(value.Uid, value.Kind);
            value.IsLiked = _musicService.IsPlaylistLiked(value.Uid, value.Kind);
        }
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));
    }

    partial void OnSelectedAlbumChanged(Album? value)
    {
        if (value != null)
        {
            value.IsPinned = _pinnedItemsService.IsAlbumPinned(value.Id);
            value.IsLiked = _musicService.IsAlbumLiked(value.Id);
        }
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));
    }

    public string DetailTitle => SelectedPlaylist?.Title ?? SelectedAlbum?.Title ?? string.Empty;
    public string DetailSubtitle
    {
        get
        {
            if (SelectedPlaylist != null) return SelectedPlaylist.OwnerName;
            if (SelectedAlbum != null)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(SelectedAlbum.ArtistNames))
                    parts.Add(SelectedAlbum.ArtistNames);
                if (SelectedAlbum.Year.HasValue)
                    parts.Add(SelectedAlbum.Year.Value.ToString());
                if (!string.IsNullOrWhiteSpace(SelectedAlbum.Genre))
                    parts.Add(SelectedAlbum.Genre);
                return string.Join(" • ", parts);
            }
            return string.Empty;
        }
    }
    public string? DetailCoverUrl => SelectedPlaylist?.CoverUrl ?? SelectedAlbum?.CoverUrl;
    public string? DetailDescription => SelectedPlaylist?.Description ?? SelectedAlbum?.Description;
    public string DetailTypeBadge => Loc?.CurrentLanguage == "en"
        ? (SelectedPlaylist != null ? "PLAYLIST" : "ALBUM")
        : (SelectedPlaylist != null ? "ПЛЕЙЛИСТ" : "АЛЬБОМ");
    public string DetailPlayButtonText => Loc?.CurrentLanguage == "en"
        ? (SelectedPlaylist != null ? "Play Playlist" : "Play Album")
        : (SelectedPlaylist != null ? "Слушать плейлист" : "Слушать альбом");

    public string PageTitle => Loc?.CurrentLanguage == "en" ? "Library" : (Loc?.GetString("Nav_Playlists") ?? "Плейлисты и альбомы");
    public string PlaylistsTabText => Loc?.CurrentLanguage == "en" ? "Playlists" : "Плейлисты";
    public string LikedAlbumsTabText => Loc?.CurrentLanguage == "en" ? "Liked Albums" : "Любимые альбомы";
    public string RefreshTooltip => Loc?.CurrentLanguage == "en" ? "Refresh library" : "Обновить библиотеку";
    public string NoPlaylistsText => Loc?.CurrentLanguage == "en" ? "No playlists found" : "Плейлисты не найдены";
    public string NoLikedAlbumsText => Loc?.CurrentLanguage == "en" ? "No liked albums yet" : "Любимых альбомов пока нет";
    public string NoLikedAlbumsSubtext => Loc?.CurrentLanguage == "en" ? "Albums you like on Yandex Music will appear here." : "Альбомы, которые вы лайкнете в Яндекс Музыке, появятся здесь.";
    public string PinnedBadgeText => Loc?.CurrentLanguage == "en" ? "Pinned" : "Закреплено";
    public string PlayMenuText => Loc?.CurrentLanguage == "en" ? "Play" : "Слушать";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackButtonText))]
    private string? _previousPageTag;

    public string BackButtonText
    {
        get
        {
            bool isEn = Loc?.CurrentLanguage == "en";
            return PreviousPageTag switch
            {
                "Likes" => isEn ? "Back to Liked Tracks" : "Назад к Любимым трекам",
                "Explore" => isEn ? "Back to Explore" : "Назад на Главную",
                "Wave" => isEn ? "Back to My Wave" : "Назад в Мою волну",
                "Account" => isEn ? "Back" : "Назад",
                _ => (SelectedCategoryIndex == 1
                    ? (isEn ? "Back to Liked Albums" : "Назад к Альбомам")
                    : (isEn ? "Back to Playlists" : "Назад к Плейлистам"))
            };
        }
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PlaylistsTabText));
        OnPropertyChanged(nameof(LikedAlbumsTabText));
        OnPropertyChanged(nameof(RefreshTooltip));
        OnPropertyChanged(nameof(NoPlaylistsText));
        OnPropertyChanged(nameof(NoLikedAlbumsText));
        OnPropertyChanged(nameof(NoLikedAlbumsSubtext));
        OnPropertyChanged(nameof(PinnedBadgeText));
        OnPropertyChanged(nameof(PlayMenuText));
        OnPropertyChanged(nameof(DetailTypeBadge));
        OnPropertyChanged(nameof(DetailPlayButtonText));
        OnPropertyChanged(nameof(BackButtonText));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseVisible))]
    private bool _isDetailOpen;

    public bool IsBrowseVisible => !IsDetailOpen;

    [ObservableProperty]
    private bool _isBackButtonVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsTab))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsTab))]
    [NotifyPropertyChangedFor(nameof(BackButtonText))]
    private int _selectedCategoryIndex; // 0 = Playlists, 1 = Liked Albums

    public bool IsPlaylistsTab => SelectedCategoryIndex == 0;
    public bool IsAlbumsTab => SelectedCategoryIndex == 1;

    [ObservableProperty]
    private string _formattedTrackCount = string.Empty;

    [ObservableProperty]
    private string _formattedDuration = string.Empty;

    public PlaylistsViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        ICredentialStorageService credentialStorage,
        ITrackPlaybackCoordinator playbackCoordinator,
        IPinnedItemsService pinnedItemsService)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _credentialStorage = credentialStorage;
        _playbackCoordinator = playbackCoordinator;
        _pinnedItemsService = pinnedItemsService;

        _pinnedItemsService.PinStateChanged += OnPinStateChanged;
        _musicService.AlbumLikeChanged += OnAlbumLikeChanged;
        _musicService.PlaylistLikeChanged += OnPlaylistLikeChanged;
        _playbackCoordinator.Register(SelectedPlaylistTracks);
    }

    [RelayCommand]
    public void SelectCategory(string? category)
    {
        PreviousPageTag = null;
        SelectedCategoryIndex = category == "albums" ? 1 : 0;
    }

    [RelayCommand]
    public async Task LoadPlaylistsAsync()
    {
        if (string.IsNullOrWhiteSpace(_musicService.CurrentToken))
        {
            var token = _credentialStorage.GetToken();
            if (!string.IsNullOrWhiteSpace(token))
            {
                _musicService.SetOAuthToken(token);
            }
        }

        string? userId = _credentialStorage.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            var profile = await _musicService.GetAccountStatusAsync();
            if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
            {
                userId = profile.UserId;
                _credentialStorage.SaveUserId(userId);
            }
            else
            {
                SetError("User ID not available. Please log in.");
                return;
            }
        }

        SetBusy(true);
        ClearError();

        try
        {
            var playlistsTask = _musicService.GetUserPlaylistsAsync(userId);
            var albumsTask = _musicService.GetLikedAlbumsAsync(userId);

            await Task.WhenAll(playlistsTask, albumsTask).ConfigureAwait(false);

            var playlists = await playlistsTask.ConfigureAwait(false);
            var albums = await albumsTask.ConfigureAwait(false);

            RunOnUi(() =>
            {
                foreach (var playlist in playlists)
                {
                    playlist.IsPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
                    playlist.IsLiked = _musicService.IsPlaylistLiked(playlist.Uid, playlist.Kind);
                    if (playlist.IsPinned)
                    {
                        _pinnedItemsService.PinPlaylist(playlist.Uid, playlist.Kind, playlist.Title, playlist.CoverUrl);
                    }
                }
                var sortedPlaylists = playlists
                    .OrderByDescending(p => p.IsPinned)
                    .ThenBy(p => _pinnedItemsService.GetPinOrder(p.PinKey))
                    .ToList();

                Playlists.Clear();
                foreach (var playlist in sortedPlaylists)
                {
                    Playlists.Add(playlist);
                }

                foreach (var album in albums)
                {
                    album.IsPinned = _pinnedItemsService.IsAlbumPinned(album.Id);
                    album.IsLiked = true;
                    if (album.IsPinned)
                    {
                        _pinnedItemsService.PinAlbum(album.Id, album.Title, album.CoverUrl);
                    }
                }
                var sortedAlbums = albums
                    .OrderByDescending(a => a.IsPinned)
                    .ThenBy(a => _pinnedItemsService.GetPinOrder(a.PinKey))
                    .ToList();

                LikedAlbums.Clear();
                foreach (var album in sortedAlbums)
                {
                    LikedAlbums.Add(album);
                }
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load library: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public async Task SelectPlaylistAsync(Playlist? playlist)
    {
        if (playlist != null)
        {
            playlist.IsPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
        }

        RunOnUi(() =>
        {
            SelectedPlaylist = playlist;
            OnPropertyChanged(nameof(IsDetailPinned));
        });

        if (playlist == null)
        {
            RunOnUi(() => SelectedPlaylistTracks.Clear());
            return;
        }

        SetBusy(true);
        ClearError();

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            await _musicService.EnsureLikedPlaylistsLoadedAsync().ConfigureAwait(false);
            var fullPlaylist = await _musicService.GetPlaylistWithTracksAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            if (fullPlaylist != null)
            {
                fullPlaylist.IsPinned = _pinnedItemsService.IsPlaylistPinned(fullPlaylist.Uid, fullPlaylist.Kind);
                fullPlaylist.IsLiked = _musicService.IsPlaylistLiked(fullPlaylist.Uid, fullPlaylist.Kind);
            }
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
                if (fullPlaylist != null)
                {
                    SelectedPlaylist = fullPlaylist;
                    FormattedTrackCount = fullPlaylist.FormattedTrackCount;
                    OnPropertyChanged(nameof(IsDetailPinned));
                    OnPropertyChanged(nameof(IsDetailLiked));
                }
                SelectedPlaylistTracks.Clear();
                foreach (var t in tracks)
                {
                    SelectedPlaylistTracks.Add(t);
                }
                _playbackCoordinator.Sync(SelectedPlaylistTracks);
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load playlist tracks: {ex.Message}");
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

        if (IsDetailOpen && SelectedPlaylist != null && SelectedPlaylist.Uid == playlist.Uid && SelectedPlaylist.Kind == playlist.Kind)
        {
            return;
        }

        playlist.IsPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
        playlist.IsLiked = _musicService.IsPlaylistLiked(playlist.Uid, playlist.Kind);
        SelectedPlaylist = playlist;
        SelectedAlbum = null;
        IsDetailOpen = true;
        FormattedTrackCount = playlist.FormattedTrackCount;
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));

        await SelectPlaylistAsync(playlist);

        long totalMs = SelectedPlaylistTracks.Sum(t => t.DurationMs);
        var totalSpan = TimeSpan.FromMilliseconds(totalMs);
        FormattedDuration = PluralizationHelper.FormatDuration(totalSpan);
    }

    [RelayCommand]
    public async Task OpenAlbumAsync(Album? album)
    {
        if (album == null) return;

        if (IsDetailOpen && SelectedAlbum != null && SelectedAlbum.Id == album.Id)
        {
            return;
        }

        album.IsPinned = _pinnedItemsService.IsAlbumPinned(album.Id);
        album.IsLiked = _musicService.IsAlbumLiked(album.Id);
        SelectedAlbum = album;
        SelectedPlaylist = null;
        IsDetailOpen = true;
        FormattedTrackCount = album.FormattedTrackCount;
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));

        SetBusy(true);
        ClearError();

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            await _musicService.EnsureLikedAlbumsLoadedAsync().ConfigureAwait(false);
            var fullAlbum = await _musicService.GetAlbumWithTracksAsync(album.Id).ConfigureAwait(false);
            var tracks = fullAlbum?.AllTracks != null ? new List<Track>(fullAlbum.AllTracks) : new List<Track>();

            // FckCensor: Restore censored/deleted album tracks if present in album spoofs
            var fckCensor = App.GetService<YMusic.WinUI.Services.Interfaces.IFckCensorService>();
            var albumSpoof = fckCensor?.GetAlbumSpoof(album.Id);
            if (albumSpoof != null && albumSpoof.Volumes.Count > 0)
            {
                var existingTrackIds = new HashSet<string>(tracks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
                var missingIds = new List<string>();
                foreach (var vol in albumSpoof.Volumes)
                {
                    foreach (var tid in vol)
                    {
                        string sid = tid.ToString();
                        if (!existingTrackIds.Contains(sid))
                        {
                            missingIds.Add(sid);
                        }
                    }
                }

                if (missingIds.Count > 0)
                {
                    try
                    {
                        var fetchedTracks = await _musicService.GetTracksAsync(missingIds).ConfigureAwait(false);
                        var fetchedDict = fetchedTracks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

                        var restoredTracks = new List<Track>();
                        foreach (var vol in albumSpoof.Volumes)
                        {
                            foreach (var tid in vol)
                            {
                                string sid = tid.ToString();
                                var found = tracks.FirstOrDefault(t => t.Id == sid) ?? (fetchedDict.TryGetValue(sid, out var ft) ? ft : null);
                                if (found != null)
                                {
                                    restoredTracks.Add(found);
                                }
                            }
                        }
                        if (restoredTracks.Count > 0)
                        {
                            tracks = restoredTracks;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FckCensor Album Restore Warning] {ex.Message}");
                    }
                }
            }

            string? albumCoverUri = fullAlbum?.CoverUri ?? album.CoverUri ?? albumSpoof?.CoverUri;
            var albumRef = fullAlbum ?? album;

            foreach (var t in tracks)
            {
                if (t.Albums == null || t.Albums.Count == 0)
                {
                    t.Albums = new List<Album> { albumRef };
                }
                else if (string.IsNullOrEmpty(t.Albums[0].CoverUri) && !string.IsNullOrEmpty(albumCoverUri))
                {
                    t.Albums[0].CoverUri = albumCoverUri;
                }

                if (string.IsNullOrEmpty(t.CoverUri) && !string.IsNullOrEmpty(albumCoverUri))
                {
                    t.CoverUri = albumCoverUri;
                }
            }

            RunOnUi(() =>
            {
                if (fullAlbum != null)
                {
                    fullAlbum.IsLiked = _musicService.IsAlbumLiked(fullAlbum.Id);
                    fullAlbum.IsPinned = _pinnedItemsService.IsAlbumPinned(fullAlbum.Id);
                    SelectedAlbum = fullAlbum;
                    FormattedTrackCount = fullAlbum.FormattedTrackCount;
                    OnPropertyChanged(nameof(IsDetailPinned));
                    OnPropertyChanged(nameof(IsDetailLiked));
                }

                SelectedPlaylistTracks.Clear();
                long totalMs = 0;
                foreach (var t in tracks)
                {
                    SelectedPlaylistTracks.Add(t);
                    totalMs += t.DurationMs;
                }

                var totalSpan = TimeSpan.FromMilliseconds(totalMs);
                FormattedDuration = PluralizationHelper.FormatDuration(totalSpan);

                _playbackCoordinator.Sync(SelectedPlaylistTracks);

                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailSubtitle));
                OnPropertyChanged(nameof(DetailCoverUrl));
                OnPropertyChanged(nameof(DetailDescription));
                OnPropertyChanged(nameof(DetailTypeBadge));
                OnPropertyChanged(nameof(DetailPlayButtonText));
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load album tracks: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        string? returnTag = PreviousPageTag;

        IsDetailOpen = false;
        PreviousPageTag = null;
        IsBackButtonVisible = true;
        SelectedPlaylist = null;
        SelectedAlbum = null;
        SelectedPlaylistTracks.Clear();

        if (!string.IsNullOrEmpty(returnTag))
        {
            App.GetService<YMusic.WinUI.Services.Interfaces.INavigationService>().NavigateToTag(returnTag);
        }
    }

    [RelayCommand]
    public async Task PlayPlaylistDirectAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        await OpenPlaylistAsync(playlist);
        if (SelectedPlaylistTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
            await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
        }
    }

    [RelayCommand]
    public async Task PlayAlbumDirectAsync(Album? album)
    {
        if (album == null) return;
        await OpenAlbumAsync(album);
        if (SelectedPlaylistTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Album;
            await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
        }
    }

    [RelayCommand]
    public async Task PlayCurrentCollectionAsync()
    {
        if (SelectedPlaylist != null)
        {
            if (SelectedPlaylistTracks.Count == 0)
            {
                await SelectPlaylistAsync(SelectedPlaylist);
            }
            if (SelectedPlaylistTracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
                await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
            }
        }
        else if (SelectedAlbum != null)
        {
            if (SelectedPlaylistTracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Album;
                await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
            }
        }
    }

    [RelayCommand]
    private async Task PlayPlaylistAsync(Playlist? playlist)
    {
        playlist ??= SelectedPlaylist;
        if (playlist == null) return;

        if (SelectedPlaylistTracks.Count == 0)
        {
            await SelectPlaylistAsync(playlist);
        }

        if (SelectedPlaylistTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
            await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
        }
    }

    [RelayCommand]
    private async Task PlayTrackAsync(Track? track)
    {
        if (track == null || SelectedPlaylistTracks.Count == 0)
        {
            return;
        }

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

        _audioPlayer.CurrentPlaybackMode = SelectedAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
        await _audioPlayer.PlayTrackAsync(track, SelectedPlaylistTracks.ToList());
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
    public void TogglePlaylistPin(Playlist? playlist)
    {
        if (playlist == null) return;
        bool isPinned = _pinnedItemsService.TogglePlaylistPin(playlist.Uid, playlist.Kind, playlist.Title, playlist.CoverUrl);
        playlist.IsPinned = isPinned;
        if (SelectedPlaylist != null && string.Equals(SelectedPlaylist.PinKey, playlist.PinKey, StringComparison.OrdinalIgnoreCase))
        {
            SelectedPlaylist.IsPinned = isPinned;
        }
        ReorderPlaylists();
        OnPropertyChanged(nameof(IsDetailPinned));
    }

    [RelayCommand]
    public void ToggleAlbumPin(Album? album)
    {
        if (album == null) return;
        bool isPinned = _pinnedItemsService.ToggleAlbumPin(album.Id, album.Title, album.CoverUrl);
        album.IsPinned = isPinned;
        if (SelectedAlbum != null && string.Equals(SelectedAlbum.PinKey, album.PinKey, StringComparison.OrdinalIgnoreCase))
        {
            SelectedAlbum.IsPinned = isPinned;
        }
        ReorderAlbums();
        OnPropertyChanged(nameof(IsDetailPinned));
    }

    [RelayCommand]
    public void ToggleCurrentDetailPin()
    {
        if (SelectedPlaylist != null)
        {
            TogglePlaylistPin(SelectedPlaylist);
        }
        else if (SelectedAlbum != null)
        {
            ToggleAlbumPin(SelectedAlbum);
        }
    }

    [RelayCommand]
    public async Task TogglePlaylistLikeAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        bool willLike = !playlist.IsLiked;
        playlist.IsLiked = willLike;
        OnPropertyChanged(nameof(IsDetailLiked));

        try
        {
            if (willLike)
            {
                await _musicService.LikePlaylistAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            }
            else
            {
                await _musicService.UnlikePlaylistAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TogglePlaylistLike Error] {ex.Message}");
            playlist.IsLiked = !willLike;
            OnPropertyChanged(nameof(IsDetailLiked));
        }
    }

    [RelayCommand]
    public async Task ToggleAlbumLikeAsync(Album? album)
    {
        if (album == null) return;
        bool willLike = !album.IsLiked;
        album.IsLiked = willLike;
        OnPropertyChanged(nameof(IsDetailLiked));

        try
        {
            if (willLike)
            {
                await _musicService.LikeAlbumAsync(album.Id).ConfigureAwait(false);
            }
            else
            {
                await _musicService.UnlikeAlbumAsync(album.Id).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToggleAlbumLike Error] {ex.Message}");
            album.IsLiked = !willLike;
            OnPropertyChanged(nameof(IsDetailLiked));
        }
    }

    [RelayCommand]
    public async Task ToggleCurrentDetailLikeAsync()
    {
        if (SelectedPlaylist != null)
        {
            await TogglePlaylistLikeAsync(SelectedPlaylist).ConfigureAwait(false);
        }
        else if (SelectedAlbum != null)
        {
            await ToggleAlbumLikeAsync(SelectedAlbum).ConfigureAwait(false);
        }
    }

    private void OnAlbumLikeChanged(object? sender, (long AlbumId, bool IsLiked) e)
    {
        RunOnUi(() =>
        {
            if (SelectedAlbum?.Id == e.AlbumId)
            {
                SelectedAlbum.IsLiked = e.IsLiked;
                OnPropertyChanged(nameof(IsDetailLiked));
            }
            var inList = LikedAlbums.FirstOrDefault(a => a.Id == e.AlbumId);
            if (inList != null)
            {
                inList.IsLiked = e.IsLiked;
                if (!e.IsLiked)
                {
                    LikedAlbums.Remove(inList);
                }
            }
            else if (e.IsLiked && SelectedAlbum?.Id == e.AlbumId)
            {
                LikedAlbums.Insert(0, SelectedAlbum);
            }
        });
    }

    private void OnPlaylistLikeChanged(object? sender, (long OwnerUid, int Kind, bool IsLiked) e)
    {
        RunOnUi(() =>
        {
            if (SelectedPlaylist != null && SelectedPlaylist.Uid == e.OwnerUid && SelectedPlaylist.Kind == e.Kind)
            {
                SelectedPlaylist.IsLiked = e.IsLiked;
                OnPropertyChanged(nameof(IsDetailLiked));
            }
            var inList = Playlists.FirstOrDefault(p => p.Uid == e.OwnerUid && p.Kind == e.Kind);
            if (inList != null)
            {
                inList.IsLiked = e.IsLiked;
            }
            else if (e.IsLiked && SelectedPlaylist != null && SelectedPlaylist.Uid == e.OwnerUid && SelectedPlaylist.Kind == e.Kind)
            {
                Playlists.Insert(0, SelectedPlaylist);
            }
        });
    }

    private void OnPinStateChanged(object? sender, string key)
    {
        RunOnUi(() =>
        {
            if (SelectedPlaylist != null)
            {
                SelectedPlaylist.IsPinned = _pinnedItemsService.IsPlaylistPinned(SelectedPlaylist.Uid, SelectedPlaylist.Kind);
            }
            if (SelectedAlbum != null)
            {
                SelectedAlbum.IsPinned = _pinnedItemsService.IsAlbumPinned(SelectedAlbum.Id);
            }

            foreach (var p in Playlists)
            {
                if (string.Equals(p.PinKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    p.IsPinned = _pinnedItemsService.IsPinned(key);
                    break;
                }
            }
            foreach (var a in LikedAlbums)
            {
                if (string.Equals(a.PinKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    a.IsPinned = _pinnedItemsService.IsPinned(key);
                    break;
                }
            }
            ReorderPlaylists();
            ReorderAlbums();
            OnPropertyChanged(nameof(IsDetailPinned));
        });
    }

    private void ReorderPlaylists()
    {
        var ordered = Playlists
            .OrderByDescending(p => p.IsPinned)
            .ThenBy(p => _pinnedItemsService.GetPinOrder(p.PinKey))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            int oldIndex = Playlists.IndexOf(ordered[i]);
            if (oldIndex >= 0 && oldIndex != i)
            {
                Playlists.Move(oldIndex, i);
            }
        }
    }

    private void ReorderAlbums()
    {
        var ordered = LikedAlbums
            .OrderByDescending(a => a.IsPinned)
            .ThenBy(a => _pinnedItemsService.GetPinOrder(a.PinKey))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            int oldIndex = LikedAlbums.IndexOf(ordered[i]);
            if (oldIndex >= 0 && oldIndex != i)
            {
                LikedAlbums.Move(oldIndex, i);
            }
        }
    }
}
