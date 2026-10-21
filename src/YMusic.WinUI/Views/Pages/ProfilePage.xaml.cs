#nullable enable

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class ProfilePage : Page
{
    public ProfileViewModel ViewModel { get; }

    public ProfilePage()
    {
        ViewModel = App.GetService<ProfileViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Bindings.Update();
        _ = ViewModel.InitializeAsync();
    }

    public string FormatLogin(string? login)
    {
        return string.IsNullOrWhiteSpace(login) ? string.Empty : $"@{login}";
    }

    public string FormatUid(string? uid)
    {
        return string.IsNullOrWhiteSpace(uid) ? string.Empty : $"UID: {uid}";
    }

    public Microsoft.UI.Xaml.Visibility HasEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void NotificationInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.DismissNotification();
    }
}
