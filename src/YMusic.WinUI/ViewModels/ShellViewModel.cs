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

    public string AccountNavItemTitle => IsAuthenticated
        ? (!string.IsNullOrWhiteSpace(CurrentUser?.DisplayName) ? CurrentUser.DisplayName : "Profile & Settings")
        : "Account / Sign In";

    partial void OnCurrentUserChanged(UserProfile? value) => OnPropertyChanged(nameof(AccountNavItemTitle));
    partial void OnIsAuthenticatedChanged(bool value) => OnPropertyChanged(nameof(AccountNavItemTitle));

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
