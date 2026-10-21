#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class LikedTracksPage : Page
{
    public LikedTracksViewModel ViewModel { get; }

    public LikedTracksPage()
    {
        ViewModel = App.GetService<LikedTracksViewModel>();
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasLoaded && !ViewModel.IsBusy)
        {
            await ViewModel.LoadTracksAsync();
        }
    }

    private async void TrackListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            await ViewModel.PlayTrackCommand.ExecuteAsync(track);
        }
    }

    private async void UnlikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Track track)
        {
            await ViewModel.UnlikeTrackCommand.ExecuteAsync(track);
        }
    }
}
