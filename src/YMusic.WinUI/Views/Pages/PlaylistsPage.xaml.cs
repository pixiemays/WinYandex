#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class PlaylistsPage : Page
{
    public PlaylistsViewModel ViewModel { get; }

    public PlaylistsPage()
    {
        ViewModel = App.GetService<PlaylistsViewModel>();
        InitializeComponent();

        TracksListView.VerticalOffsetChanged += (_, offset) =>
        {
            UpdatePlaylistHeaderCollapse(offset);
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Playlists.Count == 0 && ViewModel.LikedAlbums.Count == 0)
        {
            await ViewModel.LoadPlaylistsAsync();
        }
    }

    private void CategoryPlaylists_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectCategory("playlists");
    }

    private void CategoryAlbums_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectCategory("albums");
    }

    private void PlaylistHeroGrid_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(PlaylistHeroGrid).Properties.MouseWheelDelta;
        TracksListView.ScrollByDelta(delta);
        e.Handled = true;
    }

    private void UpdatePlaylistHeaderCollapse(double offset)
    {
        if (PlaylistHeroGrid == null || PlaylistCoverBorder == null || PlaylistTitleText == null) return;

        double progress = Math.Clamp(offset / 100.0, 0.0, 1.0);

        // 1. Cover dimensions: 140 down to 48
        double coverSize = Math.Round(140.0 - progress * (140.0 - 48.0));
        PlaylistCoverBorder.Width = coverSize;
        PlaylistCoverBorder.Height = coverSize;
        PlaylistCoverBorder.CornerRadius = new CornerRadius(Math.Round(10.0 - progress * 5.0));
        if (PlaylistCoverIcon != null)
        {
            PlaylistCoverIcon.FontSize = Math.Round(36.0 - progress * (36.0 - 18.0));
        }

        // 2. Title font size: 28 down to 18
        PlaylistTitleText.FontSize = Math.Round(28.0 - progress * (28.0 - 18.0));
        PlaylistTitleText.MaxLines = progress > 0.5 ? 1 : 2;

        // 3. Spacing and margins
        if (PlaylistMetaStack != null)
        {
            PlaylistMetaStack.Margin = new Thickness(Math.Round(20.0 - progress * 8.0), 0, 0, 0);
            PlaylistMetaStack.Spacing = Math.Round(8.0 - progress * 4.0);
        }

        PlaylistHeroGrid.Margin = new Thickness(0, 0, 0, Math.Round(24.0 - progress * 12.0));

        // 4. Fade extra info
        double fadeOut = Math.Clamp(1.0 - progress * 1.8, 0.0, 1.0);
        if (PlaylistExtraInfoPanel != null)
        {
            PlaylistExtraInfoPanel.Opacity = fadeOut;
            PlaylistExtraInfoPanel.Visibility = fadeOut <= 0.01 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void PlaylistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            UpdatePlaylistHeaderCollapse(0);
            TracksListView.ScrollToTop();
            await ViewModel.OpenPlaylistCommand.ExecuteAsync(playlist);
        }
    }

    private async void DirectPlayPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            await ViewModel.PlayPlaylistDirectCommand.ExecuteAsync(playlist);
        }
    }

    private async void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Album album)
        {
            UpdatePlaylistHeaderCollapse(0);
            TracksListView.ScrollToTop();
            await ViewModel.OpenAlbumCommand.ExecuteAsync(album);
        }
    }

    private async void DirectPlayAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Album album)
        {
            await ViewModel.PlayAlbumDirectCommand.ExecuteAsync(album);
        }
    }

    private async void TrackListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            await ViewModel.PlayTrackCommand.ExecuteAsync(track);
        }
    }

    private void TrackLikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Track track)
        {
            ViewModel.ToggleLikeTrackCommand.Execute(track);
        }
    }

    private void PinPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            ViewModel.TogglePlaylistPinCommand.Execute(playlist);
        }
    }

    private void TogglePlaylistPinContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            ViewModel.TogglePlaylistPinCommand.Execute(playlist);
        }
    }

    private void PinAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            ViewModel.ToggleAlbumPinCommand.Execute(album);
        }
    }

    private void ToggleAlbumPinContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            ViewModel.ToggleAlbumPinCommand.Execute(album);
        }
    }

    public void ResetScrollAndHeader()
    {
        UpdatePlaylistHeaderCollapse(0);
        TracksListView?.ScrollToTop();
    }

    private async void TrackAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton button && button.Tag is Track track)
        {
            var album = track.Albums?.FirstOrDefault();
            if (album != null && album.Id > 0)
            {
                await App.GetService<YMusic.WinUI.Services.Interfaces.INavigationService>().NavigateToAlbumAsync(album);
            }
            else if (!string.IsNullOrEmpty(track.Id) && track.Id.Contains(':'))
            {
                var parts = track.Id.Split(':');
                if (parts.Length > 1 && long.TryParse(parts[1], out long albumId) && albumId > 0)
                {
                    await App.GetService<YMusic.WinUI.Services.Interfaces.INavigationService>().NavigateToAlbumByIdAsync(albumId, track.AlbumTitle);
                }
            }
        }
    }
}
