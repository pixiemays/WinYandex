#nullable enable

using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views;
using YMusic.WinUI.Views.Pages;

namespace YMusic.WinUI.Services.Implementations;

public sealed class NavigationService : INavigationService
{
    private ShellWindow? _shellWindow;

    public void Initialize(ShellWindow shellWindow)
    {
        _shellWindow = shellWindow;
    }

    public void NavigateToTag(string tag)
    {
        void Execute()
        {
            _shellWindow?.NavigateToTag(tag);
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }
    }

    public async Task NavigateToAlbumAsync(Album album)
    {
        if (album == null || _shellWindow == null) return;

        var tcs = new TaskCompletionSource();

        void Execute()
        {
            try
            {
                var playlistsVm = App.GetService<PlaylistsViewModel>();

                string? currentTag = _shellWindow.CurrentPageTag;
                if (!string.IsNullOrEmpty(currentTag) && currentTag != "Playlists")
                {
                    playlistsVm.PreviousPageTag = currentTag;
                }

                _shellWindow.SelectPlaylistsTab();
                playlistsVm.SelectedCategoryIndex = 1; // Albums tab

                _ = playlistsVm.OpenAlbumAsync(album).ContinueWith(_ =>
                {
                    App.MainDispatcherQueue?.TryEnqueue(() =>
                    {
                        _shellWindow.CurrentPlaylistsPage?.ResetScrollAndHeader();
                        tcs.TrySetResult();
                    });
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }

        await tcs.Task;
    }

    public async Task NavigateToAlbumByIdAsync(long albumId, string? albumTitle = null)
    {
        var album = new Album
        {
            Id = albumId,
            Title = albumTitle ?? string.Empty
        };
        await NavigateToAlbumAsync(album);
    }

    public async Task NavigateToPlaylistAsync(Playlist playlist)
    {
        if (playlist == null || _shellWindow == null) return;

        var tcs = new TaskCompletionSource();

        void Execute()
        {
            try
            {
                var playlistsVm = App.GetService<PlaylistsViewModel>();

                string? currentTag = _shellWindow.CurrentPageTag;
                if (!string.IsNullOrEmpty(currentTag) && currentTag != "Playlists")
                {
                    playlistsVm.PreviousPageTag = currentTag;
                }

                _shellWindow.SelectPlaylistsTab();
                playlistsVm.SelectedCategoryIndex = 0; // Playlists tab

                _ = playlistsVm.OpenPlaylistAsync(playlist).ContinueWith(_ =>
                {
                    App.MainDispatcherQueue?.TryEnqueue(() =>
                    {
                        _shellWindow.CurrentPlaylistsPage?.ResetScrollAndHeader();
                        tcs.TrySetResult();
                    });
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }

        await tcs.Task;
    }
}
