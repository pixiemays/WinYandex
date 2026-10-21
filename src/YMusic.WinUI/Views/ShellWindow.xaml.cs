using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views.Pages;

namespace YMusic.WinUI.Views;

public sealed partial class ShellWindow : Window
{
    public ShellViewModel ViewModel { get; }
    private readonly IPinnedItemsService _pinnedItemsService;
    private readonly List<object> _pinnedNavItems = new();

    public ShellWindow()
    {
        InitializeComponent();

        ViewModel = App.GetService<ShellViewModel>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));

        if (Content is FrameworkElement rootElement)
        {
            rootElement.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    if (ViewModel.PlayerBar.IsNowPlayingDrawerOpen)
                    {
                        ViewModel.PlayerBar.IsNowPlayingDrawerOpen = false;
                        e.Handled = true;
                    }
                }
                else if (e.Key == Windows.System.VirtualKey.L && rootElement.XamlRoot != null)
                {
                    var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(rootElement.XamlRoot);
                    if (focused is not TextBox && focused is not PasswordBox)
                    {
                        if (ViewModel.PlayerBar.HasTrack)
                        {
                            ViewModel.PlayerBar.ToggleNowPlayingDrawerCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                }
                else if ((e.Key == Windows.System.VirtualKey.F || e.Key == Windows.System.VirtualKey.K) &&
                         Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    NavigateToTag("Search");
                    NavAutoSuggestBox?.Focus(FocusState.Programmatic);
                    e.Handled = true;
                }
            };
        }

        UpdateTitleBarButtonColors(false);

        ViewModel.PlayerBar.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerBarViewModel.IsNowPlayingDrawerOpen))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    bool isOpen = ViewModel.PlayerBar.IsNowPlayingDrawerOpen;
                    BottomPlayerBar.Visibility = isOpen
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                    AppTitleBar.Visibility = isOpen
                        ? Visibility.Collapsed
                        : Visibility.Visible;

                    if (isOpen)
                    {
                        SetTitleBar(NowPlayingDrawer.TitleBarDragRegion);
                    }
                    else
                    {
                        SetTitleBar(AppTitleBar);
                    }
                    UpdateTitleBarButtonColors(isOpen);
                });
            }
        };

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

        _pinnedItemsService = App.GetService<IPinnedItemsService>();
        _pinnedItemsService.PinStateChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshPinnedMenuItems);
        NavView.PaneOpened += (_, _) => RefreshPinnedMenuItems();
        NavView.PaneClosed += (_, _) => RefreshPinnedMenuItems();
        RefreshPinnedMenuItems();

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
                "Search" or "Поиск" => "Search",
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

    private void NavAutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = args.QueryText?.Trim() ?? string.Empty;
        NavigateToTag("Search");
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchVm = App.GetService<SearchViewModel>();
            searchVm.SetQueryAndSearch(query);
        }
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

        if (tag.StartsWith("playlist:"))
        {
            var parts = tag.Split(':');
            if (parts.Length == 3 && long.TryParse(parts[1], out long uid) && int.TryParse(parts[2], out int kind))
            {
                var pinned = _pinnedItemsService.GetPinnedItems().FirstOrDefault(p => p.Key == tag);
                var playlist = new Playlist
                {
                    Uid = uid,
                    Kind = kind,
                    Title = pinned?.Title ?? "Playlist",
                    Cover = pinned?.CoverUrl != null ? new Cover { Uri = pinned.CoverUrl } : null
                };
                _ = App.GetService<INavigationService>().NavigateToPlaylistAsync(playlist);
                return;
            }
        }
        else if (tag.StartsWith("album:"))
        {
            var parts = tag.Split(':');
            if (parts.Length == 2 && long.TryParse(parts[1], out long albumId))
            {
                var pinned = _pinnedItemsService.GetPinnedItems().FirstOrDefault(p => p.Key == tag);
                _ = App.GetService<INavigationService>().NavigateToAlbumByIdAsync(albumId, pinned?.Title);
                return;
            }
        }

        Type? targetPage = tag switch
        {
            "Explore" => typeof(ExplorePage),
            "Search" => typeof(SearchPage),
            "Wave" => typeof(WavePage),
            "Likes" => typeof(LikedTracksPage),
            "Playlists" => typeof(PlaylistsPage),
            "Account" => typeof(LoginPage),
            "Artist" => typeof(ArtistPage),
            _ => null
        };

        if (targetPage != null)
        {
            try
            {
                NavigationViewItem? navItem = tag switch
                {
                    "Explore" => NavItemExplore,
                    "Search" => NavItemSearch,
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
                else if (navItem == null)
                {
                    NavView.SelectedItem = null;
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
        Type t when t == typeof(SearchPage) => "Search",
        Type t when t == typeof(WavePage) => "Wave",
        Type t when t == typeof(LikedTracksPage) => "Likes",
        Type t when t == typeof(PlaylistsPage) => "Playlists",
        Type t when t == typeof(LoginPage) => "Account",
        _ => null
    };

    public async Task NavigateToArtistAsync(Artist artist)
    {
        App.GetService<PlayerBarViewModel>().IsNowPlayingDrawerOpen = false;

        var artistVm = App.GetService<ArtistViewModel>();
        string? currentTag = CurrentPageTag;
        if (!string.IsNullOrEmpty(currentTag))
        {
            artistVm.PreviousPageTag = currentTag;
        }

        NavView.SelectedItem = null;
        if (ContentFrame.CurrentSourcePageType != typeof(ArtistPage))
        {
            ContentFrame.Navigate(typeof(ArtistPage));
        }
        (ContentFrame.Content as ArtistPage)?.ResetScroll();

        await artistVm.LoadArtistAsync(artist);
    }

    public async Task NavigateToArtistByIdAsync(long artistId, string? artistName = null)
    {
        App.GetService<PlayerBarViewModel>().IsNowPlayingDrawerOpen = false;

        var artistVm = App.GetService<ArtistViewModel>();
        string? currentTag = CurrentPageTag;
        if (!string.IsNullOrEmpty(currentTag))
        {
            artistVm.PreviousPageTag = currentTag;
        }

        NavView.SelectedItem = null;
        if (ContentFrame.CurrentSourcePageType != typeof(ArtistPage))
        {
            ContentFrame.Navigate(typeof(ArtistPage));
        }
        (ContentFrame.Content as ArtistPage)?.ResetScroll();

        await artistVm.LoadArtistByIdAsync(artistId, artistName);
    }

    public void SelectPlaylistsTab()
    {
        NavView.SelectedItem = NavItemPlaylists;
        NavigateToTag("Playlists");
    }

    public PlaylistsPage? CurrentPlaylistsPage => ContentFrame.Content as PlaylistsPage;

    private void RefreshPinnedMenuItems()
    {
        foreach (var item in _pinnedNavItems)
        {
            NavView.MenuItems.Remove(item);
        }
        _pinnedNavItems.Clear();

        var pinned = _pinnedItemsService.GetPinnedItems();
        if (pinned.Count == 0) return;

        var separator = new NavigationViewItemSeparator();
        var header = new NavigationViewItemHeader
        {
            Content = "Pinned",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 11,
            Opacity = 0.7
        };

        NavView.MenuItems.Add(separator);
        _pinnedNavItems.Add(separator);

        NavView.MenuItems.Add(header);
        _pinnedNavItems.Add(header);

        bool isExpanded = NavView.IsPaneOpen;

        foreach (var record in pinned)
        {
            var navItem = new NavigationViewItem
            {
                Tag = record.Key,
                MinHeight = 44
            };

            UpdatePinnedNavItemVisual(navItem, record, isExpanded);

            ToolTipService.SetToolTip(navItem, record.Title);

            var flyout = new MenuFlyout();
            var unpinItem = new MenuFlyoutItem
            {
                Text = "Unpin",
                Icon = new FontIcon { Glyph = "\uE77A" },
                Tag = record
            };
            unpinItem.Click += (s, e) =>
            {
                if (s is FrameworkElement fe && fe.Tag is PinnedItemRecord r)
                {
                    if (r.Type == "album")
                        _pinnedItemsService.UnpinAlbum(r.AlbumId);
                    else
                        _pinnedItemsService.UnpinPlaylist(r.Uid, r.Kind);
                }
            };
            flyout.Items.Add(unpinItem);
            navItem.ContextFlyout = flyout;

            NavView.MenuItems.Add(navItem);
            _pinnedNavItems.Add(navItem);
        }
    }

    private void UpdatePinnedNavItemVisual(NavigationViewItem navItem, PinnedItemRecord record, bool isExpanded)
    {
        if (isExpanded)
        {
            navItem.Icon = null;

            var grid = new Grid
            {
                ColumnSpacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 36
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var coverBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(5),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrWhiteSpace(record.CoverUrl) && Uri.TryCreate(record.CoverUrl, UriKind.Absolute, out var uri))
            {
                coverBorder.Child = new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri)
                    {
                        DecodePixelWidth = 64,
                        DecodePixelHeight = 64
                    },
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                };
            }
            else
            {
                coverBorder.Child = new FontIcon
                {
                    Glyph = record.Type == "album" ? "\uE93C" : "\uE142",
                    FontSize = 14,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var textBlock = new TextBlock
            {
                Text = record.Title,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13
            };

            Grid.SetColumn(coverBorder, 0);
            Grid.SetColumn(textBlock, 1);
            grid.Children.Add(coverBorder);
            grid.Children.Add(textBlock);

            navItem.Content = grid;
        }
        else
        {
            navItem.Content = null;
            if (!string.IsNullOrWhiteSpace(record.CoverUrl) && Uri.TryCreate(record.CoverUrl, UriKind.Absolute, out var uri))
            {
                navItem.Icon = new ImageIcon
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri)
                    {
                        DecodePixelWidth = 64,
                        DecodePixelHeight = 64
                    }
                };
            }
            else
            {
                navItem.Icon = new FontIcon
                {
                    Glyph = record.Type == "album" ? "\uE93C" : "\uE142",
                    FontSize = 14
                };
            }
        }
    }

    public bool IsFullScreen => AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen;

    public void ToggleFullScreen()
    {
        if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
        }
        else
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        }
    }

    private void UpdateTitleBarButtonColors(bool isLyricsMode)
    {
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            if (isLyricsMode)
            {
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 235, 235, 235);
                titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(35, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(55, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            }
            else
            {
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonPressedForegroundColor = null;
            }
        }
    }
}
