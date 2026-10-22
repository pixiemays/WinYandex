#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class ShellViewModel : BaseViewModel
{
    private readonly ICredentialStorageService _credentialStorage;
    private readonly IYandexMusicService _musicService;

    [ObservableProperty]
    private PlayerBarViewModel _playerBar;

    [ObservableProperty]
    private UserProfile? _currentUser;

    [ObservableProperty]
    private bool _isAuthenticated;

    private bool IsRussian => Loc?.CurrentLanguage != "en";

    public string AccountNavItemTitle => IsAuthenticated
        ? (!string.IsNullOrWhiteSpace(CurrentUser?.DisplayName) ? CurrentUser.DisplayName : (IsRussian ? "Профиль и настройки" : "Profile & Settings"))
        : (IsRussian ? "Войти в аккаунт" : "Sign In");

    public string NavExploreText => IsRussian ? "Главное" : "Home";
    public string NavSearchText => IsRussian ? "Поиск" : "Search";
    public string NavWaveText => IsRussian ? "Моя волна" : "My Wave";
    public string NavLikesText => IsRussian ? "Любимые треки" : "Liked Tracks";
    public string NavPlaylistsText => IsRussian ? "Плейлисты и альбомы" : "Playlists & Albums";
    public string SearchPlaceholderText => IsRussian ? "Поиск треков, артистов, альбомов..." : "Search tracks, artists, albums...";

    partial void OnCurrentUserChanged(UserProfile? value) => OnPropertyChanged(nameof(AccountNavItemTitle));
    partial void OnIsAuthenticatedChanged(bool value) => OnPropertyChanged(nameof(AccountNavItemTitle));

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AccountNavItemTitle));
        OnPropertyChanged(nameof(NavExploreText));
        OnPropertyChanged(nameof(NavSearchText));
        OnPropertyChanged(nameof(NavWaveText));
        OnPropertyChanged(nameof(NavLikesText));
        OnPropertyChanged(nameof(NavPlaylistsText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
    }

    public ShellViewModel(
        PlayerBarViewModel playerBar,
        ICredentialStorageService credentialStorage,
        IYandexMusicService musicService)
    {
        _playerBar = playerBar;
        _credentialStorage = credentialStorage;
        _musicService = musicService;
        _isAuthenticated = _credentialStorage.HasToken();
    }

    public async Task InitializeAsync()
    {
        if (_credentialStorage.HasToken())
        {
            try
            {
                string? storedToken = _credentialStorage.GetToken();
                if (!string.IsNullOrWhiteSpace(storedToken))
                {
                    _musicService.SetOAuthToken(storedToken);
                }

                var profile = await _musicService.GetAccountStatusAsync();
                RunOnUi(() =>
                {
                    if (profile != null)
                    {
                        CurrentUser = profile;
                        IsAuthenticated = true;
                    }
                    else
                    {
                        // Maintain authentication if valid token is stored
                        IsAuthenticated = !string.IsNullOrWhiteSpace(_credentialStorage.GetToken());
                    }
                });

                if (profile != null)
                {
                    _ = _musicService.EnsureLikedTracksLoadedAsync();

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var profileVm = App.GetService<ProfileViewModel>();
                            await profileVm.InitializeAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ShellViewModel] Profile preloading error: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellViewModel Initialize warning] {ex.Message}");
                RunOnUi(() => IsAuthenticated = !string.IsNullOrWhiteSpace(_credentialStorage.GetToken()));
            }
        }
        else
        {
            RunOnUi(() => IsAuthenticated = false);
        }
    }
}
