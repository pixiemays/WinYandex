#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Implementations;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views;

namespace YMusic.WinUI;

public partial class App : Application
{
    public static ShellWindow? MainWindow { get; private set; }
    private Window? _window;

    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; set; }

    public static IServiceProvider Services { get; private set; } = null!;

    public static ILocalizationService Loc => Services.GetRequiredService<ILocalizationService>();

    public static T GetService<T>() where T : class
    {
        return Services.GetRequiredService<T>();
    }

    public App()
    {
        InitializeComponent();
        MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Services = ConfigureServices();

        UnhandledException += (sender, e) =>
        {
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CRITICAL UnhandledException]: {e.Message}\n{e.Exception}\nStackTrace:\n{e.Exception?.StackTrace}\n";
            System.Diagnostics.Debug.WriteLine(msg);
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = System.IO.Path.Combine(appData, "YMusic");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "app_debug.log"), msg);
            }
            catch { }
            e.Handled = true;
        };
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core services
        services.AddSingleton<ICredentialStorageService, CredentialStorageService>();
        services.AddSingleton<IPlaybackStateStorageService, PlaybackStateStorageService>();
        services.AddSingleton<IYandexMusicService, YandexMusicService>();
        services.AddSingleton<ILyricsOffsetService, LyricsOffsetService>();
        services.AddSingleton<IFckCensorService, FckCensorService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISloplessService, SloplessService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IAudioCacheService, AudioCacheService>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddSingleton<IEqualizerService, EqualizerService>();
        services.AddSingleton<ITrackPlaybackCoordinator, TrackPlaybackCoordinator>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IPinnedItemsService, PinnedItemsService>();
        services.AddSingleton<ITrayNotificationService, TrayNotificationService>();
        services.AddSingleton<IDiscordRpcService, DiscordRpcService>();
        services.AddSingleton<IObsOverlayService, ObsOverlayService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ISpotifyImportService, SpotifyImportService>();
        services.AddSingleton<IGameOverlayService, GameOverlayService>();
        services.AddSingleton<ICustomThemeService, CustomThemeService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // ViewModels
        services.AddSingleton<PlayerBarViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<WaveViewModel>();
        services.AddSingleton<ExploreViewModel>();
        services.AddSingleton<LikedTracksViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ArtistViewModel>();
        services.AddSingleton<ProfileViewModel>();

        var provider = services.BuildServiceProvider();
        var fckCensor = provider.GetRequiredService<IFckCensorService>();
        var audioCache = provider.GetRequiredService<IAudioCacheService>();
        var loc = provider.GetRequiredService<ILocalizationService>();
        var settings = provider.GetRequiredService<ISettingsService>();

        // Apply saved language setting
        if (!string.IsNullOrWhiteSpace(settings.Language))
        {
            loc.CurrentLanguage = settings.Language;
        }

        settings.SettingChanged += (s, propName) =>
        {
            if (propName == nameof(ISettingsService.Language) && loc.CurrentLanguage != settings.Language)
            {
                loc.CurrentLanguage = settings.Language;
            }
        };

        loc.LanguageChanged += (s, newLang) =>
        {
            if (settings.Language != newLang)
            {
                settings.Language = newLang;
            }
        };

        Track.IsTrackReplacedChecker = id => fckCensor.IsReplaced(id);
        Track.TrackSpoofGetter = id => fckCensor.GetTrackSpoof(id);
        Track.IsTrackCachedChecker = id => audioCache.IsTrackCached(id);
        Album.AlbumSpoofGetter = id => fckCensor.GetAlbumSpoof(id);

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            try
            {
                provider.GetService<IDiscordRpcService>()?.Dispose();
                provider.GetService<IObsOverlayService>()?.Dispose();
                provider.GetService<IGameOverlayService>()?.Dispose();
            }
            catch { }
        };

        return provider;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var customThemeService = GetService<ICustomThemeService>();
        customThemeService?.Initialize();

        var themeService = GetService<IThemeService>();
        themeService?.PreInitializeResources();

        MainWindow = new ShellWindow();
        _window = MainWindow;
        MainDispatcherQueue = _window.DispatcherQueue;
        themeService?.Initialize();
        _window.Activate();
        GetService<ITrayNotificationService>()?.InitializeThumbnailButtons();
        GetService<IDiscordRpcService>()?.Initialize();
        GetService<IObsOverlayService>()?.Initialize();
        GetService<IGameOverlayService>()?.Initialize();
        UpdateService.CleanOldUpdates();

        try
        {
            var appArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            HandleAppActivation(appArgs);
        }
        catch { }
    }

    public static void HandleAppActivation(Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        try
        {
            var customThemeService = GetService<ICustomThemeService>();

            if (args.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol)
            {
                if (args.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs proto)
                {
                    customThemeService?.HandleActivation(proto.Uri.AbsoluteUri);
                    return;
                }
            }
            else if (args.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File)
            {
                if (args.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs && fileArgs.Files.Count > 0)
                {
                    customThemeService?.HandleActivation(fileArgs.Files[0].Path);
                    return;
                }
            }
            else if (args.Kind.ToString() == "CommandLineLaunch")
            {
                if (args.Data != null)
                {
                    var opProp = args.Data.GetType().GetProperty("Operation");
                    var op = opProp?.GetValue(args.Data);
                    var argsProp = op?.GetType().GetProperty("Arguments");
                    string? rawArgs = argsProp?.GetValue(op) as string;
                    if (!string.IsNullOrWhiteSpace(rawArgs))
                    {
                        string[] parts = ParseCommandLine(rawArgs);
                        foreach (var part in parts)
                        {
                            if (part.StartsWith("winyandex://", StringComparison.OrdinalIgnoreCase) ||
                                part.EndsWith(".wytheme", StringComparison.OrdinalIgnoreCase) ||
                                part.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            {
                                customThemeService?.HandleActivation(part);
                                return;
                            }
                        }
                        customThemeService?.HandleActivation(rawArgs);
                        return;
                    }
                }
            }
            else if (args.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch)
            {
                if (args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs && !string.IsNullOrWhiteSpace(launchArgs.Arguments))
                {
                    customThemeService?.HandleActivation(launchArgs.Arguments);
                    return;
                }
            }

            // Fallback: check Environment.GetCommandLineArgs
            var envArgs = Environment.GetCommandLineArgs();
            for (int i = 1; i < envArgs.Length; i++)
            {
                string arg = envArgs[i];
                if (arg.StartsWith("winyandex://", StringComparison.OrdinalIgnoreCase) ||
                    arg.EndsWith(".wytheme", StringComparison.OrdinalIgnoreCase) ||
                    arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    customThemeService?.HandleActivation(arg);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] HandleAppActivation error: {ex.Message}");
        }
    }

    private static string[] ParseCommandLine(string cmdLine)
    {
        if (string.IsNullOrWhiteSpace(cmdLine)) return Array.Empty<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(cmdLine, @"[\""].+?[\""]|[^ ]+");
        var list = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            list.Add(m.Value.Trim('"'));
        }
        return list.ToArray();
    }
}
