#nullable enable

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Windows.Globalization;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class LocalizationService : ILocalizationService
{
    private readonly Dictionary<string, string> _ruStrings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _enStrings = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "ru";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? LanguageChanged;

    public LocalizationService()
    {
        LoadAllStrings();
    }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            string normalized = NormalizeLanguageCode(value);
            if (!string.Equals(_currentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _currentLanguage = normalized;
                ApplySystemLanguageOverride(normalized);
                NotifyLanguageChanged();
            }
        }
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var primary = _currentLanguage == "en" ? _enStrings : _ruStrings;
        var fallback = _currentLanguage == "en" ? _ruStrings : _enStrings;

        if (primary.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (fallback.TryGetValue(key, out var fallbackValue) && !string.IsNullOrEmpty(fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    public string GetString(string key, params object[] args)
    {
        string template = GetString(key);
        if (args == null || args.Length == 0)
        {
            return template;
        }

        try
        {
            var culture = _currentLanguage == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("ru-RU");
            return string.Format(culture, template, args);
        }
        catch (FormatException ex)
        {
            Debug.WriteLine($"[LocalizationService] Format error for key '{key}': {ex.Message}");
            return template;
        }
    }

    private void NotifyLanguageChanged()
    {
        LanguageChanged?.Invoke(this, _currentLanguage);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
    }

    private static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "ru";
        }

        code = code.Trim().ToLowerInvariant();
        if (code.StartsWith("en"))
        {
            return "en";
        }

        return "ru";
    }

    private void ApplySystemLanguageOverride(string lang)
    {
        try
        {
            string bcp47 = lang == "en" ? "en-US" : "ru-RU";
            ApplicationLanguages.PrimaryLanguageOverride = bcp47;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalizationService] PrimaryLanguageOverride failed: {ex.Message}");
        }
    }

    private void LoadAllStrings()
    {
        LoadLanguage("ru", "ru-RU", _ruStrings);
        LoadLanguage("en", "en-US", _enStrings);

        Debug.WriteLine($"[LocalizationService] Loaded {_ruStrings.Count} RU strings, {_enStrings.Count} EN strings.");
    }

    private void LoadLanguage(string shortCode, string cultureCode, Dictionary<string, string> targetDict)
    {
        // 1. Try file on disk (output dir or project root)
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Strings", cultureCode, "Resources.resw"),
            Path.Combine(AppContext.BaseDirectory, "AppX", "Strings", cultureCode, "Resources.resw"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Strings", cultureCode, "Resources.resw"),
            Path.Combine(Directory.GetCurrentDirectory(), "Strings", cultureCode, "Resources.resw")
        };

        foreach (var path in searchPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    ParseReswFile(path, targetDict);
                    if (targetDict.Count > 0)
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalizationService] Error loading resw from '{path}': {ex.Message}");
            }
        }

        // 2. Try embedded resource in assembly
        try
        {
            var assembly = typeof(LocalizationService).Assembly;
            var resNames = assembly.GetManifestResourceNames();
            var matched = resNames.FirstOrDefault(n =>
                (n.Contains(cultureCode, StringComparison.OrdinalIgnoreCase) ||
                 n.Contains(cultureCode.Replace('-', '_'), StringComparison.OrdinalIgnoreCase)) &&
                n.EndsWith(".resw", StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                using var stream = assembly.GetManifestResourceStream(matched);
                if (stream != null)
                {
                    ParseReswStream(stream, targetDict);
                    if (targetDict.Count > 0)
                    {
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalizationService] Error loading embedded resw for {cultureCode}: {ex.Message}");
        }

        // 3. Try Windows.ApplicationModel.Resources.ResourceManager
        try
        {
            var rm = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();
            var map = rm.MainResourceMap.GetSubtree("Resources");
            if (map != null)
            {
                var ctx = rm.CreateResourceContext();
                ctx.QualifierValues["Language"] = cultureCode;
                // Pre-populate known keys from the other dictionary if available
                var knownKeys = _ruStrings.Keys.Concat(_enStrings.Keys).Distinct().ToList();
                foreach (var k in knownKeys)
                {
                    var cand = map.TryGetValue(k, ctx);
                    if (cand != null && !string.IsNullOrEmpty(cand.ValueAsString))
                    {
                        targetDict[k] = cand.ValueAsString;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalizationService] ResourceManager lookup failed for {cultureCode}: {ex.Message}");
        }
    }

    private static void ParseReswFile(string filePath, Dictionary<string, string> target)
    {
        using var stream = File.OpenRead(filePath);
        ParseReswStream(stream, target);
    }

    private static void ParseReswStream(Stream stream, Dictionary<string, string> target)
    {
        var doc = XDocument.Load(stream);
        var dataElements = doc.Root?.Elements("data");
        if (dataElements == null)
        {
            return;
        }

        foreach (var elem in dataElements)
        {
            var name = elem.Attribute("name")?.Value;
            var val = elem.Element("value")?.Value;
            if (!string.IsNullOrEmpty(name) && val != null)
            {
                target[name] = val;
                if (name.Contains('_'))
                {
                    target[name.Replace("_", string.Empty)] = val;
                }
            }
        }
    }
}
