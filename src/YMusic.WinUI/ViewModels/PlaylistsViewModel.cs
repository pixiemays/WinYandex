#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class PlaylistsViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ICredentialStorageService _credentialStorage;

    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<Track> SelectedPlaylistTracks { get; } = new();

    [ObservableProperty]
    private Playlist? _selectedPlaylist;

    public PlaylistsViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        ICredentialStorageService credentialStorage)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _credentialStorage = credentialStorage;
    }

    [RelayCommand]
    public async Task LoadPlaylistsAsync()
    {
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
            var playlists = await _musicService.GetUserPlaylistsAsync(userId);

            RunOnUi(() =>
            {
                Playlists.Clear();
                foreach (var playlist in playlists)
                {
                    Playlists.Add(playlist);
                }
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load playlists: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public async Task SelectPlaylistAsync(Playlist? playlist)
    {
        RunOnUi(() => SelectedPlaylist = playlist);

        if (playlist == null)
        {
            RunOnUi(() => SelectedPlaylistTracks.Clear());
            return;
        }

        SetBusy(true);
        ClearError();

        try
        {
            var trackIds = playlist.Tracks.Select(t => t.Id).ToList();
            var tracks = trackIds.Count > 0
                ? await _musicService.GetTracksAsync(trackIds)
                : Array.Empty<Track>();

            RunOnUi(() =>
            {
                SelectedPlaylistTracks.Clear();
                foreach (var t in tracks)
                {
                    SelectedPlaylistTracks.Add(t);
                }
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
    private async Task PlayPlaylistAsync(Playlist? playlist)
    {
        if (playlist == null)
        {
            return;
        }

        await SelectPlaylistAsync(playlist);

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

        _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
        await _audioPlayer.PlayTrackAsync(track, SelectedPlaylistTracks.ToList());
    }
}
