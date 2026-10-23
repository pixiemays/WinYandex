using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class ExplorePage : Page
{
    public ExploreViewModel ViewModel { get; }

    private Storyboard? _currentTransition;
    private bool _isAnimating;

    public ExplorePage()
    {
        InitializeComponent();
        ViewModel = App.GetService<ExploreViewModel>();

        ApplyInitialViewState();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ExploreViewModel.IsDetailOpen))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.IsDetailOpen)
                    {
                        ResetDetailScroll();
                        if (DetailViewRoot.Visibility != Visibility.Visible || DetailViewRoot.Opacity < 0.9)
                        {
                            AnimateToDetail();
                        }
                    }
                    else
                    {
                        if (!_isAnimating && DetailViewRoot.Visibility == Visibility.Visible)
                        {
                            AnimateToBrowse();
                        }
                    }
                });
            }
            else if (e.PropertyName == nameof(ExploreViewModel.ActiveAlbum) ||
                     e.PropertyName == nameof(ExploreViewModel.ActivePlaylist))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.IsDetailOpen && !_isAnimating)
                    {
                        ResetDetailScroll();
                        AnimateDetailContentCrossfade();
                    }
                });
            }
        };
    }

    private void ApplyInitialViewState()
    {
        if (ViewModel.IsDetailOpen)
        {
            BrowseViewRoot.Visibility = Visibility.Collapsed;
            BrowseViewRoot.Opacity = 0.0;
            BrowseTranslateTransform.Y = -16.0;

            DetailViewRoot.Visibility = Visibility.Visible;
            DetailViewRoot.Opacity = 1.0;
            DetailTranslateTransform.Y = 0.0;
        }
        else
        {
            BrowseViewRoot.Visibility = Visibility.Visible;
            BrowseViewRoot.Opacity = 1.0;
            BrowseTranslateTransform.Y = 0.0;

            DetailViewRoot.Visibility = Visibility.Collapsed;
            DetailViewRoot.Opacity = 0.0;
            DetailTranslateTransform.Y = 28.0;
        }
    }

    public void AnimateToDetail()
    {
        _currentTransition?.Stop();
        _isAnimating = true;

        DetailViewRoot.Visibility = Visibility.Visible;
        BrowseViewRoot.Visibility = Visibility.Visible;

        var sb = new Storyboard();

        // 1. Detail Fade In (0 -> 1)
        var detailFade = new DoubleAnimation
        {
            From = DetailViewRoot.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(detailFade, DetailViewRoot);
        Storyboard.SetTargetProperty(detailFade, "Opacity");
        sb.Children.Add(detailFade);

        // 2. Detail Slide Up (from current Y or 28 -> 0)
        var detailSlide = new DoubleAnimation
        {
            From = DetailTranslateTransform.Y != 0 ? DetailTranslateTransform.Y : 28.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(detailSlide, DetailTranslateTransform);
        Storyboard.SetTargetProperty(detailSlide, "Y");
        sb.Children.Add(detailSlide);

        // 3. Browse Fade Out (1 -> 0)
        var browseFade = new DoubleAnimation
        {
            From = BrowseViewRoot.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(browseFade, BrowseViewRoot);
        Storyboard.SetTargetProperty(browseFade, "Opacity");
        sb.Children.Add(browseFade);

        // 4. Browse Slide slightly Up (0 -> -16)
        var browseSlide = new DoubleAnimation
        {
            From = BrowseTranslateTransform.Y,
            To = -16.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(browseSlide, BrowseTranslateTransform);
        Storyboard.SetTargetProperty(browseSlide, "Y");
        sb.Children.Add(browseSlide);

        sb.Completed += (s, e) =>
        {
            BrowseViewRoot.Visibility = Visibility.Collapsed;
            _isAnimating = false;
            _currentTransition = null;
        };

        _currentTransition = sb;
        sb.Begin();
    }

    public void AnimateDetailContentCrossfade()
    {
        _currentTransition?.Stop();

        var sb = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = 0.35,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, DetailViewRoot);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);

        var slide = new DoubleAnimation
        {
            From = 18.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, DetailTranslateTransform);
        Storyboard.SetTargetProperty(slide, "Y");
        sb.Children.Add(slide);

        sb.Completed += (s, e) =>
        {
            _currentTransition = null;
        };

        _currentTransition = sb;
        sb.Begin();
    }

    public void AnimateToBrowse(Action? onCompleted = null)
    {
        _currentTransition?.Stop();
        _isAnimating = true;

        BrowseViewRoot.Visibility = Visibility.Visible;
        DetailViewRoot.Visibility = Visibility.Visible;

        var sb = new Storyboard();

        // 1. Detail Fade Out (1 -> 0)
        var detailFade = new DoubleAnimation
        {
            From = DetailViewRoot.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(170),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(detailFade, DetailViewRoot);
        Storyboard.SetTargetProperty(detailFade, "Opacity");
        sb.Children.Add(detailFade);

        // 2. Detail Slide Down (0 -> 24)
        var detailSlide = new DoubleAnimation
        {
            From = DetailTranslateTransform.Y,
            To = 24.0,
            Duration = TimeSpan.FromMilliseconds(170),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(detailSlide, DetailTranslateTransform);
        Storyboard.SetTargetProperty(detailSlide, "Y");
        sb.Children.Add(detailSlide);

        // 3. Browse Fade In (0 -> 1)
        var browseFade = new DoubleAnimation
        {
            From = BrowseViewRoot.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(browseFade, BrowseViewRoot);
        Storyboard.SetTargetProperty(browseFade, "Opacity");
        sb.Children.Add(browseFade);

        // 4. Browse Slide Down from -16 to 0
        var browseSlide = new DoubleAnimation
        {
            From = BrowseTranslateTransform.Y != 0 ? BrowseTranslateTransform.Y : -16.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(browseSlide, BrowseTranslateTransform);
        Storyboard.SetTargetProperty(browseSlide, "Y");
        sb.Children.Add(browseSlide);

        sb.Completed += (s, e) =>
        {
            DetailViewRoot.Visibility = Visibility.Collapsed;
            _isAnimating = false;
            _currentTransition = null;
            onCompleted?.Invoke();
        };

        _currentTransition = sb;
        sb.Begin();
    }

    private void DetailBackButton_Click(object sender, RoutedEventArgs e)
    {
        AnimateToBrowse(() =>
        {
            ViewModel.CloseDetailCommand.Execute(null);
        });
    }

    public void ResetDetailScroll()
    {
        DetailTracksListView.ScrollToTop();
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

    private void TogglePlaylistLikeContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            _ = ViewModel.TogglePlaylistLikeCommand.ExecuteAsync(playlist);
        }
    }

    private void ToggleAlbumLikeContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            _ = ViewModel.ToggleAlbumLikeCommand.ExecuteAsync(album);
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

    private async void TrackArtist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor && anchor.Tag is Track track)
        {
            await YMusic.WinUI.Helpers.ArtistNavigationHelper.NavigateToTrackArtistAsync(anchor, track);
        }
    }
}
