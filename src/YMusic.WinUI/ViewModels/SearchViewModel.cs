#nullable enable

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views.Controls;

namespace YMusic.WinUI.ViewModels;

public sealed partial class SearchViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly INavigationService _navigationService;
    private readonly ITrackPlaybackCoordinator _trackCoordinator;

    private DispatcherQueueTimer? _debounceTimer;
    private CancellationTokenSource? _searchCts;

    private int _tracksCurrentPage;
    private bool _hasMoreTracks;

    public ObservableCollection<Track> Tracks { get; } = new();
    public ObservableCollection<Track> TopTracks { get; } = new();
    public ObservableCollection<Artist> Artists { get; } = new();
    public ObservableCollection<Album> Albums { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<string> RecentQueries { get; } = new();
    public ObservableCollection<string> SuggestionChips { get; } = new()
    {
        "Rock", "Pop", "Hip-Hop", "Electronic", "Metal", "Indie", "Soundtracks", "Jazz", "Classical", "Lo-Fi", "Phonk", "Anime"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuggestions))]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    private string _query = string.Empty;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool ShowSuggestions => string.IsNullOrWhiteSpace(Query);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private bool _hasSearched;

    [ObservableProperty]
    private SearchBestResult? _bestResult;

    public ObservableCollection<SearchPreviewCoverItem> VisiblePreviewCovers { get; } = new();
    private readonly List<SearchPreviewCoverItem> _allBestResultPreviewCovers = new();
    private int _lastMaxIcons = 6;

    [ObservableProperty]
    private bool _hasBestResultPreviewCovers;

    [ObservableProperty]
    private int _selectedTabIndex; // 0: All, 1: Tracks, 2: Artists, 3: Albums, 4: Playlists

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TracksTabHeader))]
    private int _tracksTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArtistsTabHeader))]
    private int _artistsTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumsTabHeader))]
    private int _albumsTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaylistsTabHeader))]
    private int _playlistsTotal;

    [ObservableProperty]
    private bool _isLoadingMoreTracks;

    public string AllTabHeader => Loc?["Search_Tab_All"] ?? "Все";
    public string TracksTabHeader => TracksTotal > 0 ? $"{Loc?["Search_Tab_Tracks"] ?? "Треки"} ({TracksTotal:N0})" : (Loc?["Search_Tab_Tracks"] ?? "Треки");
    public string ArtistsTabHeader => ArtistsTotal > 0 ? $"{Loc?["Search_Tab_Artists"] ?? "Артисты"} ({ArtistsTotal:N0})" : (Loc?["Search_Tab_Artists"] ?? "Артисты");
    public string AlbumsTabHeader => AlbumsTotal > 0 ? $"{Loc?["Search_Tab_Albums"] ?? "Альбомы"} ({AlbumsTotal:N0})" : (Loc?["Search_Tab_Albums"] ?? "Альбомы");
    public string PlaylistsTabHeader => PlaylistsTotal > 0 ? $"{Loc?["Search_Tab_Playlists"] ?? "Плейлисты"} ({PlaylistsTotal:N0})" : (Loc?["Search_Tab_Playlists"] ?? "Плейлисты");

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AllTabHeader));
        OnPropertyChanged(nameof(TracksTabHeader));
        OnPropertyChanged(nameof(ArtistsTabHeader));
        OnPropertyChanged(nameof(AlbumsTabHeader));
        OnPropertyChanged(nameof(PlaylistsTabHeader));
    }

    public bool HasResults => BestResult != null || Tracks.Count > 0 || Artists.Count > 0 || Albums.Count > 0 || Playlists.Count > 0;
    public bool ShowNoResults => HasSearched && !IsSearching && !HasResults && !string.IsNullOrWhiteSpace(Query);

    public SearchViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        INavigationService navigationService,
        ITrackPlaybackCoordinator trackCoordinator)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _navigationService = navigationService;
        _trackCoordinator = trackCoordinator;

        _trackCoordinator.Register(Tracks);
        _trackCoordinator.Register(TopTracks);

        LoadRecentQueries();

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null)
        {
            _debounceTimer = dispatcher.CreateTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(320);
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                if (!string.IsNullOrWhiteSpace(Query))
                {
                    _ = PerformSearchAsync(Query.Trim(), isLiveDebounce: true);
                }
            };
        }
    }

    partial void OnQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _debounceTimer?.Stop();
            _searchCts?.Cancel();
            ResetResults();
            HasSearched = false;
            IsSearching = false;
            return;
        }

        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    public void SetQueryAndSearch(string newQuery)
    {
        _debounceTimer?.Stop();
        Query = newQuery;
        if (!string.IsNullOrWhiteSpace(newQuery))
        {
            _ = PerformSearchAsync(newQuery.Trim(), isLiveDebounce: false);
        }
    }

    [RelayCommand]
    public void ClearQuery()
    {
        Query = string.Empty;
    }

    [RelayCommand]
    public void SelectTab(int index)
    {
        if (SelectedTabIndex != index)
        {
            SelectedTabIndex = index;
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        _debounceTimer?.Stop();
        if (!string.IsNullOrWhiteSpace(Query))
        {
            await PerformSearchAsync(Query.Trim(), isLiveDebounce: false);
        }
    }

    [RelayCommand]
    public void UseSuggestionChip(string chip)
    {
        SetQueryAndSearch(chip);
    }

    [RelayCommand]
    public void UseRecentQuery(string recent)
    {
        SetQueryAndSearch(recent);
    }

    [RelayCommand]
    public void ClearRecentQueries()
    {
        RecentQueries.Clear();
        SaveRecentQueries();
    }

    public async Task PerformSearchAsync(string text, bool isLiveDebounce = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        IsSearching = true;

        try
        {
            var result = await _musicService.SearchAsync(text, "all", 0, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;

            RunOnUi(() =>
            {
                if (token.IsCancellationRequested) return;

                ResetResults();
                HasSearched = true;

                if (result != null)
                {
                    BestResult = result.Best;

                    TracksTotal = result.Tracks?.Total ?? 0;
                    ArtistsTotal = result.Artists?.Total ?? 0;
                    AlbumsTotal = result.Albums?.Total ?? 0;
                    PlaylistsTotal = result.Playlists?.Total ?? 0;

                    if (result.Tracks?.Results != null)
                    {
                        foreach (var track in result.Tracks.Results)
                        {
                            Tracks.Add(track);
                        }

                        foreach (var track in Tracks.Take(5))
                        {
                            TopTracks.Add(track);
                        }
                    }

                    if (result.Artists?.Results != null)
                    {
                        foreach (var artist in result.Artists.Results)
                        {
                            Artists.Add(artist);
                        }
                    }

                    if (result.Albums?.Results != null)
                    {
                        foreach (var album in result.Albums.Results)
                        {
                            Albums.Add(album);
                        }
                    }

                    if (result.Playlists?.Results != null)
                    {
                        foreach (var playlist in result.Playlists.Results)
                        {
                            Playlists.Add(playlist);
                        }
                    }

                    _tracksCurrentPage = 0;
                    _hasMoreTracks = Tracks.Count < TracksTotal;

                    _allBestResultPreviewCovers.Clear();
                    VisiblePreviewCovers.Clear();
                    HasBestResultPreviewCovers = false;

                    if (result.Best?.Type == "artist" && result.Best.Artist != null)
                    {
                        long artistId = result.Best.Artist.Id;
                        var seenCovers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (result.Albums?.Results != null)
                        {
                            foreach (var album in result.Albums.Results)
                            {
                                if (album.Artists.Any(a => a.Id == artistId) && !string.IsNullOrWhiteSpace(album.CoverUrl))
                                {
                                    if (seenCovers.Add(album.CoverUrl))
                                    {
                                        _allBestResultPreviewCovers.Add(new SearchPreviewCoverItem
                                        {
                                            CoverUrl = album.CoverUrl,
                                            Title = album.Title
                                        });
                                    }
                                }
                            }
                        }

                        if (result.Tracks?.Results != null)
                        {
                            foreach (var track in result.Tracks.Results)
                            {
                                if (track.Artists.Any(a => a.Id == artistId) && !string.IsNullOrWhiteSpace(track.CoverUrl))
                                {
                                    if (seenCovers.Add(track.CoverUrl))
                                    {
                                        _allBestResultPreviewCovers.Add(new SearchPreviewCoverItem
                                        {
                                            CoverUrl = track.CoverUrl,
                                            Title = track.FullTitle
                                        });
                                    }
                                }
                            }
                        }

                        UpdateVisiblePreviewCovers(_lastMaxIcons);

                        _ = LoadArtistBriefPreviewCoversAsync(artistId, token);
                    }
                }

                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(ShowNoResults));

                if (!isLiveDebounce)
                {
                    AddRecentQuery(text);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignored, user typed further
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search Error] {ex.Message}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                RunOnUi(() => IsSearching = false);
            }
        }
    }

    [RelayCommand]
    public async Task LoadMoreTracksAsync()
    {
        if (IsLoadingMoreTracks || !_hasMoreTracks || string.IsNullOrWhiteSpace(Query)) return;

        IsLoadingMoreTracks = true;
        try
        {
            int nextPage = _tracksCurrentPage + 1;
            var result = await _musicService.SearchAsync(Query.Trim(), "track", nextPage).ConfigureAwait(false);
            if (result?.Tracks?.Results != null && result.Tracks.Results.Count > 0)
            {
                RunOnUi(() =>
                {
                    foreach (var track in result.Tracks.Results)
                    {
                        Tracks.Add(track);
                    }
                    _tracksCurrentPage = nextPage;
                    _hasMoreTracks = Tracks.Count < TracksTotal;
                });
            }
            else
            {
                _hasMoreTracks = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadMoreTracks Warning] {ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsLoadingMoreTracks = false);
        }
    }

    [RelayCommand]
    public async Task PlayTrackAsync(Track? track)
    {
        if (track == null) return;
        await _audioPlayer.PlayTrackAsync(track, Tracks.ToList()).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task ToggleLikeTrackAsync(Track? track)
    {
        if (track == null) return;

        bool newLikedState = !track.IsLiked;
        track.IsLiked = newLikedState;

        try
        {
            if (newLikedState)
            {
                await _musicService.LikeTrackAsync(track.Id);
            }
            else
            {
                await _musicService.UnlikeTrackAsync(track.Id);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToggleLikeTrack Warning] {ex.Message}");
            track.IsLiked = !newLikedState; // rollback on failure
        }
    }

    [RelayCommand]
    public async Task OpenArtistAsync(Artist? artist)
    {
        if (artist == null) return;
        await _navigationService.NavigateToArtistAsync(artist);
    }

    [RelayCommand]
    public async Task OpenAlbumAsync(Album? album)
    {
        if (album == null) return;
        await _navigationService.NavigateToAlbumAsync(album);
    }

    [RelayCommand]
    public async Task OpenPlaylistAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        await _navigationService.NavigateToPlaylistAsync(playlist);
    }

    [RelayCommand]
    public async Task PlayBestResultAsync()
    {
        if (BestResult == null) return;

        switch (BestResult.Type)
        {
            case "track" when BestResult.Track != null:
                await PlayTrackAsync(BestResult.Track);
                break;
            case "artist" when BestResult.Artist != null:
                await _navigationService.NavigateToArtistAsync(BestResult.Artist);
                break;
            case "album" when BestResult.Album != null:
                await OpenAlbumAsync(BestResult.Album);
                break;
            case "playlist" when BestResult.Playlist != null:
                await OpenPlaylistAsync(BestResult.Playlist);
                break;
        }
    }

    private void ResetResults()
    {
        Tracks.Clear();
        TopTracks.Clear();
        Artists.Clear();
        Albums.Clear();
        Playlists.Clear();
        BestResult = null;
        _allBestResultPreviewCovers.Clear();
        VisiblePreviewCovers.Clear();
        HasBestResultPreviewCovers = false;
        TracksTotal = 0;
        ArtistsTotal = 0;
        AlbumsTotal = 0;
        PlaylistsTotal = 0;
        _tracksCurrentPage = 0;
        _hasMoreTracks = false;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    public void UpdateVisiblePreviewCovers(int maxIcons)
    {
        if (maxIcons <= 0) maxIcons = 1;
        _lastMaxIcons = maxIcons;

        var toShow = _allBestResultPreviewCovers.Take(maxIcons).ToList();
        if (!VisiblePreviewCovers.SequenceEqual(toShow))
        {
            VisiblePreviewCovers.Clear();
            foreach (var item in toShow)
            {
                VisiblePreviewCovers.Add(item);
            }
        }

        HasBestResultPreviewCovers = VisiblePreviewCovers.Count > 0;
    }

    private async Task LoadArtistBriefPreviewCoversAsync(long artistId, CancellationToken cancellationToken)
    {
        try
        {
            var brief = await _musicService.GetArtistBriefInfoAsync(artistId, cancellationToken).ConfigureAwait(false);
            if (brief == null || cancellationToken.IsCancellationRequested) return;

            RunOnUi(() =>
            {
                if (BestResult?.Type != "artist" || BestResult.Artist?.Id != artistId) return;

                var seenCovers = new HashSet<string>(_allBestResultPreviewCovers.Select(p => p.CoverUrl ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                bool addedNew = false;

                if (brief.PopularTracks != null)
                {
                    foreach (var track in brief.PopularTracks)
                    {
                        if (!string.IsNullOrWhiteSpace(track.CoverUrl) && seenCovers.Add(track.CoverUrl))
                        {
                            _allBestResultPreviewCovers.Add(new SearchPreviewCoverItem
                            {
                                CoverUrl = track.CoverUrl,
                                Title = track.FullTitle
                            });
                            addedNew = true;
                        }
                    }
                }

                if (brief.Albums != null)
                {
                    foreach (var album in brief.Albums)
                    {
                        if (!string.IsNullOrWhiteSpace(album.CoverUrl) && seenCovers.Add(album.CoverUrl))
                        {
                            _allBestResultPreviewCovers.Add(new SearchPreviewCoverItem
                            {
                                CoverUrl = album.CoverUrl,
                                Title = album.Title
                            });
                            addedNew = true;
                        }
                    }
                }

                if (addedNew)
                {
                    UpdateVisiblePreviewCovers(_lastMaxIcons);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchViewModel] LoadArtistBriefPreviewCovers warning: {ex.Message}");
        }
    }

    private void AddRecentQuery(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return;
        q = q.Trim();

        for (int i = RecentQueries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(RecentQueries[i], q, StringComparison.OrdinalIgnoreCase))
            {
                RecentQueries.RemoveAt(i);
            }
        }

        RecentQueries.Insert(0, q);
        while (RecentQueries.Count > 10)
        {
            RecentQueries.RemoveAt(RecentQueries.Count - 1);
        }

        SaveRecentQueries();
    }

    private void LoadRecentQueries()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string filePath = Path.Combine(appData, "YMusic", "recent_searches.json");
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    RecentQueries.Clear();
                    foreach (var item in list)
                    {
                        RecentQueries.Add(item);
                    }
                }
            }
        }
        catch { }
    }

    private void SaveRecentQueries()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "YMusic");
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "recent_searches.json");
            string json = JsonSerializer.Serialize(RecentQueries);
            File.WriteAllText(filePath, json);
        }
        catch { }
    }
}
