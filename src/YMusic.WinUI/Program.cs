#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
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

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int ASFW_ANY = -1;

    [STAThread]
    static void Main(string[] args)
    {
        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic", "startup.log");
        void Log(string msg) { try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [PID:{Process.GetCurrentProcess().Id}] {msg}\n"); } catch { } }

        Log("Main entered.");

        try
        {
            SetCurrentProcessExplicitAppUserModelID("WinYandex");
        }
        catch (Exception ex) { Log($"SetAppID failed: {ex.Message}"); }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        bool isRedirect = DecideRedirection();
        Log($"DecideRedirection returned: {isRedirect}");
        if (isRedirect)
        {
            Log("Exiting due to isRedirect == true.");
            return;
        }

        Log("Calling Application.Start...");
        try
        {
            Application.Start((p) =>
            {
                Log("Application.Start callback invoked.");
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            Log("Application.Start returned.");
        }
        catch (Exception ex)
        {
            Log($"Application.Start exception: {ex}");
        }
    }

    private static bool DecideRedirection()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var existingProcesses = Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(p => p.Id != currentProcess.Id)
                .ToList();

            // 1. Clean up any dead/hung zombie processes if they are not responding
            foreach (var proc in existingProcesses)
            {
                try
                {
                    if (!proc.Responding)
                    {
                        Debug.WriteLine($"[Program] Killing hung zombie process {proc.Id}...");
                        proc.Kill();
                        proc.WaitForExit(1000);
                    }
                }
                catch { }
            }

            existingProcesses = Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(p => p.Id != currentProcess.Id)
                .ToList();

            // If no other process of this app is running, any existing AppInstance registration is stale.
            // Reset and become the main instance immediately without attempting a false redirect.
            if (existingProcesses.Count == 0)
            {
                try
                {
                    AppInstance.GetCurrent().UnregisterKey();
                }
                catch { }

                var reg = AppInstance.FindOrRegisterForKey(InstanceKey);
                if (reg.IsCurrent)
                {
                    reg.Activated += OnAppActivated;
                }
                return false;
            }

            AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();
            AppInstance mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

            if (mainInstance.IsCurrent)
            {
                mainInstance.Activated += OnAppActivated;
                return false;
            }

            // An instance is already running. Allow foreground activation and redirect.
            AllowSetForegroundWindow(ASFW_ANY);
            var redirectTask = mainInstance.RedirectActivationToAsync(args).AsTask();
            bool completed = redirectTask.Wait(2500);

            if (completed)
            {
                return true;
            }

            Debug.WriteLine("[Program] RedirectActivation timed out after 2500ms. Main instance is unresponsive.");
            // Main instance did not respond to activation, kill hung processes and continue as main instance
            foreach (var proc in existingProcesses)
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(1000);
                }
                catch { }
            }

            try
            {
                AppInstance.GetCurrent().UnregisterKey();
                mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
                if (mainInstance.IsCurrent)
                {
                    mainInstance.Activated += OnAppActivated;
                }
            }
            catch { }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Program] DecideRedirection error: {ex}");
            return false;
        }
    }

    private static void OnAppActivated(object? sender, AppActivationArguments args)
    {
        Task.Run(async () =>
        {
            for (int i = 0; i < 50 && App.MainDispatcherQueue == null; i++)
            {
                await Task.Delay(100);
            }

            App.MainDispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    var trayService = App.GetService<ITrayNotificationService>();
                    trayService?.RestoreWindow();
                    App.HandleAppActivation(args);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Program] Failed to restore window on activation: {ex}");
                }
            });
        });
    }
}
