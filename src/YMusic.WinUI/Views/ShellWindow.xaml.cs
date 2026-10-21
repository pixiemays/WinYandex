#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views.Pages;

namespace YMusic.WinUI.Views;

public sealed partial class ShellWindow : Window
{
    public ShellViewModel ViewModel { get; }

    public ShellWindow()
    {
        InitializeComponent();

        ViewModel = App.GetService<ShellViewModel>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));

        var loginVm = App.GetService<LoginViewModel>();
        loginVm.LoginSucceeded += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.IsAuthenticated = true;
                NavView.SelectedItem = NavItemExplore;
                NavigateToTag("Explore");
            });
        };
        loginVm.LoggedOut += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.IsAuthenticated = false;
                NavView.SelectedItem = NavItemAccount;
                NavigateToTag("Account");
            });
        };

        Closed += (_, _) =>
        {
            App.GetService<IAudioPlayerService>()?.SaveCurrentState();
        };

        AppWindow.Closing += (_, _) =>
        {
            App.GetService<IAudioPlayerService>()?.SaveCurrentState();
        };

        App.GetService<INavigationService>().Initialize(this);

        InitializeNavigation();
    }

    private async void InitializeNavigation()
    {
        await ViewModel.InitializeAsync();

        if (ViewModel.IsAuthenticated)
        {
            NavView.SelectedItem = NavItemExplore;
            NavigateToTag("Explore");
        }
        else
        {
            NavView.SelectedItem = NavItemAccount;
            NavigateToTag("Account");
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        string? tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString();
        if (string.IsNullOrEmpty(tag) && args.InvokedItem != null)
        {
            string itemStr = args.InvokedItem.ToString() ?? string.Empty;
            tag = itemStr switch
            {
                "Explore" or "Главное" => "Explore",
                "My Wave" or "Моя волна" => "Wave",
                "Liked Tracks" => "Likes",
                "Playlists" or "Playlists & Albums" => "Playlists",
                "Account / Sign In" => "Account",
                _ => null
            };
        }

        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        if (tag == "Explore" && ContentFrame.Content is ExplorePage explorePage && explorePage.ViewModel.IsDetailOpen)
        {
            explorePage.ViewModel.CloseDetail();
            return;
        }

        if (tag == "Playlists" && ContentFrame.Content is PlaylistsPage playlistsPage && playlistsPage.ViewModel.IsDetailOpen)
        {
            playlistsPage.ViewModel.CloseDetail();
            return;
        }

        NavigateToTag(tag);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            NavigateToTag(item.Tag?.ToString());
        }
    }

    public void NavigateToTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        Type? targetPage = tag switch
        {
            "Explore" => typeof(ExplorePage),
            "Wave" => typeof(WavePage),
            "Likes" => typeof(LikedTracksPage),
            "Playlists" => typeof(PlaylistsPage),
            "Account" => typeof(LoginPage),
            _ => null
        };

        if (targetPage != null)
        {
            try
            {
                NavigationViewItem? navItem = tag switch
                {
                    "Explore" => NavItemExplore,
                    "Wave" => NavItemWave,
                    "Likes" => NavItemLikes,
                    "Playlists" => NavItemPlaylists,
                    "Account" => NavItemAccount,
                    _ => null
                };
                if (navItem != null && !ReferenceEquals(NavView.SelectedItem, navItem))
                {
                    NavView.SelectedItem = navItem;
                }

                if (ContentFrame.CurrentSourcePageType != targetPage)
                {
                    ContentFrame.Navigate(targetPage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation Error] {ex}");
                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string dir = Path.Combine(appData, "YMusic");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "app_debug.log"), $"[NavigateToTag Exception]: {ex}\n");
                }
                catch { }
            }
        }
    }

    public string? CurrentPageTag => ContentFrame.CurrentSourcePageType switch
    {
        Type t when t == typeof(ExplorePage) => "Explore",
        Type t when t == typeof(WavePage) => "Wave",
        Type t when t == typeof(LikedTracksPage) => "Likes",
        Type t when t == typeof(PlaylistsPage) => "Playlists",
        Type t when t == typeof(LoginPage) => "Account",
        _ => null
    };

    public void SelectPlaylistsTab()
    {
        NavView.SelectedItem = NavItemPlaylists;
        NavigateToTag("Playlists");
    }

    public PlaylistsPage? CurrentPlaylistsPage => ContentFrame.Content as PlaylistsPage;
}
