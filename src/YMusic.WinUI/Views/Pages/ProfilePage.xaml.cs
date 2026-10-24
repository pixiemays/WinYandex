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

        Loaded += (_, _) =>
        {
            if (ViewModel.ShouldScrollToUpdates)
            {
                ViewModel.ShouldScrollToUpdates = false;
                ScrollToUpdates();
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Bindings.Update();
        _ = ViewModel.InitializeAsync();

        if (ViewModel.ShouldScrollToUpdates || (e.Parameter is string s && s == "Updates"))
        {
            ViewModel.ShouldScrollToUpdates = false;
            ScrollToUpdates();
        }
    }

    public void ScrollToUpdates()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(150);
            PageScrollViewer?.ChangeView(null, PageScrollViewer.ScrollableHeight, null, false);
            AboutAndUpdatesSection?.StartBringIntoView(new Microsoft.UI.Xaml.BringIntoViewOptions { AnimationDesired = true });
        });
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
