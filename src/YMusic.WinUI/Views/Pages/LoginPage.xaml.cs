#nullable enable

using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using YMusic.WinUI.Services;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class LoginPage : Page
{
    private static string OAuthUrl =>
        $"https://oauth.yandex.ru/authorize?response_type=token&client_id={EnvironmentConfig.YandexClientId}";

    private bool _isLoggingIn;

    public LoginViewModel ViewModel { get; }

    public LoginPage()
    {
        ViewModel = App.GetService<LoginViewModel>();
        InitializeComponent();
    }

    private async void SignInWithYandexButton_Click(object sender, RoutedEventArgs e)
    {
        _isLoggingIn = false;
        OAuthOverlay.Visibility = Visibility.Visible;
        WebViewProgressRing.IsActive = true;

        try
        {
            await OAuthWebView.EnsureCoreWebView2Async();
            OAuthWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            OAuthWebView.CoreWebView2.SourceChanged -= OnWebViewSourceChanged;
            OAuthWebView.CoreWebView2.SourceChanged += OnWebViewSourceChanged;
            OAuthWebView.Source = new Uri(OAuthUrl);
        }
        catch (Exception ex)
        {
            OAuthOverlay.Visibility = Visibility.Collapsed;
            ViewModel.SetError($"Failed to initialize browser: {ex.Message}");
        }
    }

    private void CancelOAuthButton_Click(object sender, RoutedEventArgs e)
    {
        OAuthOverlay.Visibility = Visibility.Collapsed;
        try
        {
            OAuthWebView.CoreWebView2?.Stop();
        }
        catch
        {
            // Ignore clean up errors
        }
    }

    private void OAuthWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        CheckForTokenAndLogin(args.Uri, () => args.Cancel = true);
    }

    private void OAuthWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        WebViewProgressRing.IsActive = false;
    }

    private void OnWebViewSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        WebViewProgressRing.IsActive = false;
        CheckForTokenAndLogin(sender.Source, null);
    }

    private void CheckForTokenAndLogin(string uriString, Action? cancelAction)
    {
        if (_isLoggingIn || string.IsNullOrWhiteSpace(uriString) || !uriString.Contains("access_token="))
        {
            return;
        }

        var match = Regex.Match(uriString, @"[#&?]access_token=([^&]+)");
        if (match.Success)
        {
            _isLoggingIn = true;
            cancelAction?.Invoke();

            string token = Uri.UnescapeDataString(match.Groups[1].Value);
            System.Diagnostics.Debug.WriteLine($"[OAuth Redirect] Matched URI: {uriString}");
            System.Diagnostics.Debug.WriteLine($"[OAuth Token Extracted] Length: {token.Length}");

            DispatcherQueue.TryEnqueue(async () =>
            {
                OAuthOverlay.Visibility = Visibility.Collapsed;
                try
                {
                    OAuthWebView.CoreWebView2?.Stop();
                }
                catch
                {
                    // Ignore
                }

                bool success = await ViewModel.LoginWithOAuthTokenAsync(token);
                if (!success)
                {
                    _isLoggingIn = false;
                }
            });
        }
    }
}
