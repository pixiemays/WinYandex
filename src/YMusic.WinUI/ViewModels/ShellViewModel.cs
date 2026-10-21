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
                        IsAuthenticated = false;
                    }
                });

                if (profile != null)
                {
                    _ = _musicService.EnsureLikedTracksLoadedAsync();
                }
            }
            catch
            {
                RunOnUi(() => IsAuthenticated = false);
            }
        }
        else
        {
            RunOnUi(() => IsAuthenticated = false);
        }
    }
}
