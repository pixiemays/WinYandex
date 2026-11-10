#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Services.Implementations;

public sealed class CustomThemeService : ICustomThemeService, IDisposable
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private const string CommunityThemesCatalogUrl = "https://raw.githubusercontent.com/themaymeow/winyandex-themes/main/community_themes.json";

    private readonly ISettingsService _settingsService;
    private readonly List<CustomTheme> _themes = new();
    private readonly object _lock = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    public string ThemesDirectory { get; }

    public IReadOnlyList<CustomTheme> Themes
    {
        get
        {
            lock (_lock)
            {
                return _themes.ToList();
            }
        }
    }

    public CustomTheme? ActiveCustomTheme
    {
        get
        {
            string themeKey = _settingsService.AppTheme;
            return GetThemeById(themeKey);
        }
    }

    public event EventHandler? ThemesChanged;
    public event EventHandler<CustomTheme>? ActiveThemeReloaded;

    public CustomThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        ThemesDirectory = Path.Combine(appData, "WinYandex", "Themes");
    }

    public void Initialize()
    {
        try
        {
            Directory.CreateDirectory(ThemesDirectory);

            EnsureDefaultTemplates();

            ReloadThemesInternal();

            StartDirectoryWatcher();

            Task.Run(RegisterProtocolAndAssociations);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] Initialize error: {ex.Message}");
        }
    }

    private void EnsureDefaultTemplates()
    {
        try
        {
            if (!Directory.Exists(ThemesDirectory))
            {
                Directory.CreateDirectory(ThemesDirectory);
            }

            // Readme helper
            string readmePath = Path.Combine(ThemesDirectory, "README.txt");
            if (!File.Exists(readmePath))
            {
                File.WriteAllText(readmePath,
                    "Папка пользовательских тем WinYandex.\r\n" +
                    "Вы можете добавлять темы в формате .json или архивы .wytheme (.zip).\r\n" +
                    "Изменения в файлах тем подхватываются на лету при сохранении (Ctrl+S)!\r\n");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] EnsureDefaultTemplates error: {ex.Message}");
        }
    }

    private static string? TryReadFileWithRetry(string filePath, int maxRetries = 6, int delayMs = 60)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                if (i == maxRetries - 1) return null;
                Thread.Sleep(delayMs);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private void ReloadThemesInternal()
    {
        lock (_lock)
        {
            _themes.Clear();

            if (!Directory.Exists(ThemesDirectory)) return;

            var jsonFiles = Directory.GetFiles(ThemesDirectory, "*.json", SearchOption.AllDirectories);
            foreach (var file in jsonFiles)
            {
                try
                {
                    string? content = TryReadFileWithRetry(file);
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    var theme = JsonSerializer.Deserialize<CustomTheme>(content);
                    if (theme != null)
                    {
                        theme.FilePath = file;
                        if (string.IsNullOrWhiteSpace(theme.Id))
                        {
                            theme.Id = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                        }
                        if (string.IsNullOrWhiteSpace(theme.Name))
                        {
                            theme.Name = Path.GetFileNameWithoutExtension(file);
                        }
                        _themes.Add(theme);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CustomThemeService] Failed to parse theme '{file}': {ex.Message}");
                }
            }
        }

        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            ThemesChanged?.Invoke(this, EventArgs.Empty);

            var active = ActiveCustomTheme;
            if (active != null)
            {
                ActiveThemeReloaded?.Invoke(this, active);
            }
        });
    }

    private void StartDirectoryWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(ThemesDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileSystemChanged;
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] FileSystemWatcher error: {ex.Message}");
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            ReloadThemesInternal();
        }, null, 250, Timeout.Infinite);
    }

    public CustomTheme? GetThemeById(string idOrKey)
    {
        if (string.IsNullOrWhiteSpace(idOrKey)) return null;

        string clean = idOrKey.Trim();
        if (clean.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("custom:".Length).Trim();
        }

        lock (_lock)
        {
            return _themes.FirstOrDefault(t =>
                t.Id.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(t.FilePath).Equals(clean, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void OpenThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(ThemesDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = ThemesDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] OpenThemesFolder error: {ex.Message}");
        }
    }

    public async Task<CustomTheme?> ImportThemeFileAsync(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
                string fileName = Path.GetFileNameWithoutExtension(sourceFilePath);

                if (ext is ".wytheme" or ".zip")
                {
                    string targetDir = Path.Combine(ThemesDirectory, SanitizeFileName(fileName));
                    if (Directory.Exists(targetDir))
                    {
                        Directory.Delete(targetDir, true);
                    }
                    Directory.CreateDirectory(targetDir);

                    ZipFile.ExtractToDirectory(sourceFilePath, targetDir);

                    ReloadThemesInternal();

                    // Find extracted theme
                    var jsonFiles = Directory.GetFiles(targetDir, "*.json", SearchOption.AllDirectories);
                    if (jsonFiles.Length > 0)
                    {
                        return GetThemeById(Path.GetFileNameWithoutExtension(jsonFiles[0])) ?? Themes.FirstOrDefault(t => t.FilePath == jsonFiles[0]);
                    }
                }
                else if (ext is ".json")
                {
                    string targetFile = Path.Combine(ThemesDirectory, Path.GetFileName(sourceFilePath));
                    File.Copy(sourceFilePath, targetFile, overwrite: true);

                    ReloadThemesInternal();
                    return GetThemeById(fileName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CustomThemeService] ImportThemeFile error: {ex.Message}");
            }
            return null;
        });
    }

    public async Task<string> ExportThemeAsync(CustomTheme theme, string targetZipPath)
    {
        return await Task.Run(() =>
        {
            if (File.Exists(targetZipPath))
            {
                File.Delete(targetZipPath);
            }

            if (!string.IsNullOrEmpty(theme.DirectoryPath) &&
                !theme.DirectoryPath.Equals(ThemesDirectory, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(theme.DirectoryPath))
            {
                ZipFile.CreateFromDirectory(theme.DirectoryPath, targetZipPath);
            }
            else
            {
                using var zip = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);
                if (File.Exists(theme.FilePath))
                {
                    zip.CreateEntryFromFile(theme.FilePath, "theme.json");
                }
                if (!string.IsNullOrEmpty(theme.ResolvedBackgroundImagePath) && File.Exists(theme.ResolvedBackgroundImagePath))
                {
                    zip.CreateEntryFromFile(theme.ResolvedBackgroundImagePath, Path.GetFileName(theme.ResolvedBackgroundImagePath));
                }
            }

            return targetZipPath;
        });
    }

    public async Task<CustomTheme?> InstallFromUrlAsync(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            byte[] data = await s_httpClient.GetByteArrayAsync(url);
            string tempFile = Path.Combine(Path.GetTempPath(), $"theme_{Guid.NewGuid():N}" + (url.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ".json" : ".wytheme"));
            await File.WriteAllBytesAsync(tempFile, data);

            var imported = await ImportThemeFileAsync(tempFile);
            try { File.Delete(tempFile); } catch { }

            return imported;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] InstallFromUrl error: {ex.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<CommunityThemeItem>> FetchCommunityThemesAsync()
    {
        var result = new List<CommunityThemeItem>();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await s_httpClient.GetAsync(CommunityThemesCatalogUrl, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(cts.Token);
                var items = JsonSerializer.Deserialize<List<CommunityThemeItem>>(json);
                if (items != null)
                {
                    result.AddRange(items);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] FetchCommunityThemes online error: {ex.Message}");
        }

        // Update installed status
        lock (_lock)
        {
            foreach (var item in result)
            {
                item.IsInstalled = _themes.Any(t => t.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            }
        }

        return result;
    }

    public async Task<bool> InstallCommunityThemeAsync(CommunityThemeItem item)
    {
        try
        {
            var existing = GetThemeById(item.Id);
            if (existing != null)
            {
                item.IsInstalled = true;
                App.MainDispatcherQueue?.TryEnqueue(() =>
                {
                    var themeService = App.GetService<IThemeService>();
                    themeService?.ApplyTheme($"custom:{existing.Id}");

                    var playerBar = App.GetService<PlayerBarViewModel>();
                    playerBar?.ShowToast($"Тема «{existing.Name}» применена!");
                });
                return true;
            }

            item.IsInstalling = true;

            CustomTheme? installed = null;
            if (!string.IsNullOrWhiteSpace(item.DownloadUrl))
            {
                installed = await InstallFromUrlAsync(item.DownloadUrl);
            }

            item.IsInstalling = false;
            item.IsInstalled = installed != null;

            if (installed != null)
            {
                App.MainDispatcherQueue?.TryEnqueue(() =>
                {
                    var themeService = App.GetService<IThemeService>();
                    themeService?.ApplyTheme($"custom:{installed.Id}");

                    var playerBar = App.GetService<PlayerBarViewModel>();
                    playerBar?.ShowToast($"Тема «{installed.Name}» успешно установлена и применена!");
                });
                return true;
            }
            else
            {
                App.MainDispatcherQueue?.TryEnqueue(() =>
                {
                    var playerBar = App.GetService<PlayerBarViewModel>();
                    playerBar?.ShowToast("Не удалось загрузить тему: ссылка недоступна или файл поврежден");
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] InstallCommunityTheme error: {ex.Message}");
            item.IsInstalling = false;
        }
        return false;
    }

    public void HandleActivation(string uriOrFilePath)
    {
        if (string.IsNullOrWhiteSpace(uriOrFilePath)) return;

        Task.Run(async () =>
        {
            try
            {
                string trimmed = uriOrFilePath.Trim().Trim('"', '\'');

                // 1. OAuth auto-login: winyandex://auth?token=... or winyandex://login?token=...
                if (trimmed.StartsWith("winyandex://auth", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("winyandex://login", StringComparison.OrdinalIgnoreCase))
                {
                    string? token = ExtractTokenFromUri(trimmed);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        App.MainDispatcherQueue?.TryEnqueue(async () =>
                        {
                            var loginVm = App.GetService<LoginViewModel>();
                            if (loginVm != null)
                            {
                                bool ok = await loginVm.LoginWithOAuthTokenAsync(token);
                                var playerBar = App.GetService<PlayerBarViewModel>();
                                if (ok)
                                {
                                    playerBar?.ShowToast("Вход выполнен успешно! Добро пожаловать.");
                                }
                                else
                                {
                                    playerBar?.ShowToast("Ошибка авторизации: неверный токен");
                                }
                            }
                        });
                        return;
                    }
                }

                // 2. Theme installation via protocol: winyandex://theme/...
                if (trimmed.StartsWith("winyandex://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(trimmed);
                    string query = uri.Query;
                    string? url = null;
                    string? id = null;

                    if (!string.IsNullOrEmpty(query))
                    {
                        var queryParams = System.Web.HttpUtility.ParseQueryString(query);
                        url = queryParams["url"];
                        id = queryParams["id"];
                    }

                    CustomTheme? theme = null;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        theme = await InstallFromUrlAsync(url);
                    }

                    if (theme != null)
                    {
                        App.MainDispatcherQueue?.TryEnqueue(() =>
                        {
                            App.GetService<IThemeService>()?.ApplyTheme($"custom:{theme.Id}");
                            App.GetService<PlayerBarViewModel>()?.ShowToast($"Тема «{theme.Name}» установлена и применена!");
                        });
                    }
                    else
                    {
                        App.MainDispatcherQueue?.TryEnqueue(() =>
                        {
                            App.GetService<PlayerBarViewModel>()?.ShowToast("Не удалось загрузить тему: файл не найден или ссылка недоступна");
                        });
                    }
                    return;
                }

                // 3. File installation: .wytheme or .json
                if (trimmed.EndsWith(".wytheme", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(trimmed))
                    {
                        var theme = await ImportThemeFileAsync(trimmed);
                        if (theme != null)
                        {
                            App.MainDispatcherQueue?.TryEnqueue(() =>
                            {
                                App.GetService<IThemeService>()?.ApplyTheme($"custom:{theme.Id}");
                                App.GetService<PlayerBarViewModel>()?.ShowToast($"Тема «{theme.Name}» установлена и применена!");
                            });
                        }
                        else
                        {
                            App.MainDispatcherQueue?.TryEnqueue(() =>
                            {
                                App.GetService<PlayerBarViewModel>()?.ShowToast("Ошибка при установке файла темы");
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CustomThemeService] HandleActivation error: {ex.Message}");
                App.MainDispatcherQueue?.TryEnqueue(() =>
                {
                    App.GetService<PlayerBarViewModel>()?.ShowToast($"Ошибка обработки ссылки: {ex.Message}");
                });
            }
        });
    }

    private static string? ExtractTokenFromUri(string uriString)
    {
        var match = System.Text.RegularExpressions.Regex.Match(uriString, @"(?:token|access_token)=([^&#]+)");
        if (match.Success)
        {
            return Uri.UnescapeDataString(match.Groups[1].Value);
        }
        return null;
    }

    private static string ExtractThemeIdFromUri(string uriString)
    {
        string clean = uriString.ToLowerInvariant();
        if (clean.Contains("catppuccin")) return "catppuccin-mocha";
        if (clean.Contains("tokyo")) return "tokyo-night";
        if (clean.Contains("nordic")) return "nordic-aurora";
        if (clean.Contains("sunset")) return "sunset-vibes";
        if (clean.Contains("cyberpunk")) return "cyberpunk-neon";
        return Path.GetFileNameWithoutExtension(uriString);
    }

    private static void RegisterProtocolAndAssociations()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            // 1. winyandex:// protocol
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\winyandex"))
            {
                key.SetValue("", "URL:WinYandex Protocol");
                key.SetValue("URL Protocol", "");
                using var cmdKey = key.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
            }

            // 2. .wytheme extension
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.wytheme"))
            {
                key.SetValue("", "WinYandex.Theme");
            }

            // 3. WinYandex.Theme ProgID
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\WinYandex.Theme"))
            {
                key.SetValue("", "WinYandex Theme Package");
                using (var iconKey = key.CreateSubKey("DefaultIcon"))
                {
                    iconKey.SetValue("", $"\"{exePath}\",0");
                }
                using (var cmdKey = key.CreateSubKey(@"shell\open\command"))
                {
                    cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomThemeService] RegisterProtocolAndAssociations error: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
