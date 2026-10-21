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
                NavView.SelectedItem = NavItemLikes;
                NavigateToTag("Likes");
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

        InitializeNavigation();
    }

    private async void InitializeNavigation()
    {
        await ViewModel.InitializeAsync();

        if (ViewModel.IsAuthenticated)
        {
            NavView.SelectedItem = NavItemLikes;
            NavigateToTag("Likes");
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
                "My Wave" or "Моя волна" => "Wave",
                "Liked Tracks" => "Likes",
                "Playlists" => "Playlists",
                "Account / Sign In" => "Account",
                _ => null
            };
        }

        if (!string.IsNullOrEmpty(tag))
        {
            NavigateToTag(tag);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            NavigateToTag(item.Tag?.ToString());
        }
    }

    private void NavigateToTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        Type? targetPage = tag switch
        {
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
                ContentFrame.Navigate(targetPage);
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
}
