#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class ArtistCardDialog : ContentDialog
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly INavigationService _navigationService;
    private readonly IYandexMusicService _musicService;
    private List<Track>? _popularTracks;
    private Action? _onAlbumNavigating;

    public ArtistCardDialog(
        IAudioPlayerService audioPlayer,
        INavigationService navigationService,
        IYandexMusicService musicService)
    {
        InitializeComponent();
        _audioPlayer = audioPlayer;
        _navigationService = navigationService;
        _musicService = musicService;
    }

    public void SetBasicInfo(string artistName)
    {
        ArtistNameTextBlock.Text = artistName;
        ArtistPicture.DisplayName = artistName;
    }

    public void SetNavigationCallback(Action? onAlbumNavigating)
    {
        _onAlbumNavigating = onAlbumNavigating;
    }

    public async Task LoadArtistDataAsync(long artistId, CancellationToken cancellationToken = default)
    {
        try
        {
            var brief = await _musicService.GetArtistBriefInfoAsync(artistId, cancellationToken);
            if (brief?.Artist != null)
            {
                var artist = brief.Artist;
                ArtistNameTextBlock.Text = artist.Name;
                ArtistPicture.DisplayName = artist.Name;

                if (!string.IsNullOrWhiteSpace(artist.AvatarUrl))
                {
                    ArtistPicture.ProfilePicture = new BitmapImage(new Uri(artist.AvatarUrl));
                }

                if (brief.Stats?.LastMonthListeners != null)
                {
                    ArtistListenersTextBlock.Text = brief.Stats.FormattedListeners;
                    ArtistListenersTextBlock.Visibility = Visibility.Visible;
                }

                if (!string.IsNullOrWhiteSpace(artist.FormattedGenres))
                {
                    ArtistGenresTextBlock.Text = artist.FormattedGenres;
                    ArtistGenresTextBlock.Visibility = Visibility.Visible;
                }

                _popularTracks = brief.PopularTracks;
                if (_popularTracks != null && _popularTracks.Count > 0)
                {
                    TracksRepeater.ItemsSource = _popularTracks.Take(5).ToList();
                    PopularTracksSection.Visibility = Visibility.Visible;
                }
                else
                {
                    PopularTracksSection.Visibility = Visibility.Collapsed;
                }

                if (brief.Albums != null && brief.Albums.Count > 0)
                {
                    AlbumsRepeater.ItemsSource = brief.Albums.Take(12).ToList();
                    AlbumsSection.Visibility = Visibility.Visible;
                }
                else
                {
                    AlbumsSection.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistCardDialog Load Error]: {ex.Message}");
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentScrollViewer.Visibility = Visibility.Visible;
        }
    }

    private async void TrackRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Track track)
        {
            await _audioPlayer.PlayTrackAsync(track, _popularTracks);
        }
    }

    private async void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Album album)
        {
            Hide();
            _onAlbumNavigating?.Invoke();
            await _navigationService.NavigateToAlbumAsync(album);
        }
    }
}
