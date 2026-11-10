#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface ICustomThemeService
{
    string ThemesDirectory { get; }

    IReadOnlyList<CustomTheme> Themes { get; }

    CustomTheme? ActiveCustomTheme { get; }

    event EventHandler? ThemesChanged;

    event EventHandler<CustomTheme>? ActiveThemeReloaded;

    void Initialize();

    void OpenThemesFolder();

    Task<CustomTheme?> ImportThemeFileAsync(string sourceFilePath);

    Task<string> ExportThemeAsync(CustomTheme theme, string targetZipPath);

    Task<CustomTheme?> InstallFromUrlAsync(string url);

    Task<IReadOnlyList<CommunityThemeItem>> FetchCommunityThemesAsync();

    Task<bool> InstallCommunityThemeAsync(CommunityThemeItem item);

    CustomTheme? GetThemeById(string idOrKey);

    void HandleActivation(string uriOrFilePath);
}
