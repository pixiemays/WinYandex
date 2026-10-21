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
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Playlists.Count == 0)
        {
            await ViewModel.LoadPlaylistsAsync();
        }
    }

    private async void PlaylistListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView && listView.SelectedItem is Playlist playlist)
        {
            await ViewModel.SelectPlaylistCommand.ExecuteAsync(playlist);
        }
    }

    private async void TrackListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            await ViewModel.PlayTrackCommand.ExecuteAsync(track);
        }
    }
}
