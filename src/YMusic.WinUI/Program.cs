#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI;

public static class Program
{
    private const string InstanceKey = "YMusic_WinUI_SingleInstance_AppKey";

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int ASFW_ANY = -1;

    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        bool isRedirect = DecideRedirection();
        if (isRedirect)
        {
            return;
        }

        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    private static bool DecideRedirection()
    {
        try
        {
            AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();
            AppInstance mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

            if (mainInstance.IsCurrent)
            {
                mainInstance.Activated += OnAppActivated;
                return false;
            }

            // An instance is already running. Allow foreground activation and redirect.
            AllowSetForegroundWindow(ASFW_ANY);
            mainInstance.RedirectActivationToAsync(args).AsTask().Wait(3000);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Program] DecideRedirection error: {ex}");
            return false;
        }
    }

    private static void OnAppActivated(object? sender, AppActivationArguments args)
    {
        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                var trayService = App.GetService<ITrayNotificationService>();
                trayService?.RestoreWindow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Program] Failed to restore window on activation: {ex}");
            }
        });
    }
}
