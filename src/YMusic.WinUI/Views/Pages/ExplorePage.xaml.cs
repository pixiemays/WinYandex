#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class ExplorePage : Page
{
    public ExploreViewModel ViewModel { get; }

    public ExplorePage()
    {
        InitializeComponent();
        ViewModel = App.GetService<ExploreViewModel>();
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ExploreViewModel.IsDetailOpen) && ViewModel.IsDetailOpen)
            {
                ResetDetailScroll();
            }
        };

        DetailTracksListView.VerticalOffsetChanged += (_, offset) =>
        {
            UpdateDetailHeaderCollapse(offset);
        };
    }

    private void DetailHeroGrid_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(DetailHeroGrid).Properties.MouseWheelDelta;
        DetailTracksListView.ScrollByDelta(delta);
        e.Handled = true;
    }

    public void ResetDetailScroll()
    {
        UpdateDetailHeaderCollapse(0);
        DetailTracksListView.ScrollToTop();
    }

    private void UpdateDetailHeaderCollapse(double offset)
    {
        if (DetailHeroGrid == null || DetailCoverBorder == null || DetailTitleText == null) return;

        double progress = Math.Clamp(offset / 100.0, 0.0, 1.0);

        // 1. Cover dimensions: 180 down to 56
        double coverSize = Math.Round(180.0 - progress * (180.0 - 56.0));
        DetailCoverBorder.Width = coverSize;
        DetailCoverBorder.Height = coverSize;
        DetailCoverBorder.CornerRadius = new CornerRadius(Math.Round(10.0 - progress * 4.0));
        if (DetailCoverIcon != null)
        {
            DetailCoverIcon.FontSize = Math.Round(36.0 - progress * (36.0 - 18.0));
        }

        // 2. Title font size: 30 down to 18
        DetailTitleText.FontSize = Math.Round(30.0 - progress * (30.0 - 18.0));
        DetailTitleText.MaxLines = progress > 0.5 ? 1 : 2;

        // 3. Spacing and margins
        if (DetailMetaStack != null)
        {
            DetailMetaStack.Margin = new Thickness(Math.Round(24.0 - progress * 12.0), 0, 0, 0);
            DetailMetaStack.Spacing = Math.Round(8.0 - progress * 4.0);
        }

        DetailHeroGrid.Margin = new Thickness(0, 0, 0, Math.Round(24.0 - progress * 16.0));

        // 4. Fade extra details (badge, subtitle, description, meta)
        double fadeOut = Math.Clamp(1.0 - progress * 1.6, 0.0, 1.0);
        if (DetailBadgeBorder != null)
        {
            DetailBadgeBorder.Opacity = fadeOut;
            DetailBadgeBorder.Visibility = fadeOut <= 0.01 ? Visibility.Collapsed : Visibility.Visible;
        }
        if (DetailExtraInfoPanel != null)
        {
            DetailExtraInfoPanel.Opacity = fadeOut;
            DetailExtraInfoPanel.Visibility = fadeOut <= 0.01 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasLoaded)
        {
            await ViewModel.LoadExploreDataCommand.ExecuteAsync(null);
        }
    }

    private void PersonalPlaylistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            ViewModel.OpenPlaylistCommand.Execute(playlist);
        }
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Album album)
        {
            ViewModel.OpenAlbumCommand.Execute(album);
        }
    }

    private void EditorialPlaylistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            ViewModel.OpenPlaylistCommand.Execute(playlist);
        }
    }

    private void DirectPlayPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            ViewModel.PlayPlaylistDirectCommand.Execute(playlist);
        }
    }

    private void DirectPlayAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Album album)
        {
            ViewModel.PlayAlbumDirectCommand.Execute(album);
        }
    }

    private void ChartTrackRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Track track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void DetailTrack_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void DetailLikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Track track)
        {
            ViewModel.ToggleLikeTrackCommand.Execute(track);
        }
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
