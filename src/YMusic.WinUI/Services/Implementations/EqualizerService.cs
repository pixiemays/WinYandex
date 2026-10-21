#nullable enable

using System.Text.Json;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class EqualizerService : IEqualizerService
{
    private readonly List<EqualizerBand> _bands = new();
    private bool _isEnabled;
    private string _currentPresetName = "Flat";
    private readonly string _settingsFilePath;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                EqualizerChanged?.Invoke(this, EventArgs.Empty);
                SaveSettings();
            }
        }
    }

    public IReadOnlyList<EqualizerBand> Bands => _bands;
    public IReadOnlyList<EqualizerPreset> Presets => DefaultEqualizerPresets.Presets;
    public string CurrentPresetName => _currentPresetName;

    public event EventHandler? EqualizerChanged;

    public EqualizerService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "equalizer_settings.json");

        // Initialize 10 standard bands
        for (int i = 0; i < DefaultEqualizerPresets.Standard10Bands.Count; i++)
        {
            var def = DefaultEqualizerPresets.Standard10Bands[i];
            var band = new EqualizerBand
            {
                Index = i,
                Label = def.Label,
                Frequency = def.Freq,
                Gain = 0.0
            };
            band.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EqualizerBand.Gain))
                {
                    CheckIfPresetMatches();
                    EqualizerChanged?.Invoke(this, EventArgs.Empty);
                    SaveSettings();
                }
            };
            _bands.Add(band);
        }

        LoadSettings();
    }

    public void ApplyPreset(string presetName)
    {
        var preset = Presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (preset == null) return;

        _currentPresetName = preset.Name;

        for (int i = 0; i < _bands.Count && i < preset.Gains.Count; i++)
        {
            _bands[i].Gain = preset.Gains[i];
        }

        EqualizerChanged?.Invoke(this, EventArgs.Empty);
        SaveSettings();
    }

    public void SetBandGain(int index, double gainDb)
    {
        if (index >= 0 && index < _bands.Count)
        {
            _bands[index].Gain = Math.Clamp(gainDb, -12.0, 12.0);
        }
    }

    public void ResetToFlat()
    {
        ApplyPreset("Flat");
    }

    private void CheckIfPresetMatches()
    {
        foreach (var preset in Presets)
        {
            if (preset.Name == "Custom") continue;

            bool matches = true;
            for (int i = 0; i < _bands.Count && i < preset.Gains.Count; i++)
            {
                if (Math.Abs(_bands[i].Gain - preset.Gains[i]) > 0.05)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                _currentPresetName = preset.Name;
                return;
            }
        }

        _currentPresetName = "Custom";
    }

    private void SaveSettings()
    {
        try
        {
            var data = new EqualizerSaveData
            {
                IsEnabled = _isEnabled,
                PresetName = _currentPresetName,
                Gains = _bands.Select(b => b.Gain).ToList()
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Silently ignore storage IO errors
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath)) return;

            string json = File.ReadAllText(_settingsFilePath);
            var data = JsonSerializer.Deserialize<EqualizerSaveData>(json);
            if (data == null) return;

            _isEnabled = data.IsEnabled;
            _currentPresetName = data.PresetName ?? "Flat";

            if (data.Gains != null && data.Gains.Count == _bands.Count)
            {
                for (int i = 0; i < _bands.Count; i++)
                {
                    _bands[i].Gain = data.Gains[i];
                }
            }
            else
            {
                ApplyPreset(_currentPresetName);
            }
        }
        catch
        {
            // Fallback to default
        }
    }

    private sealed class EqualizerSaveData
    {
        public bool IsEnabled { get; set; }
        public string? PresetName { get; set; }
        public List<double>? Gains { get; set; }
    }
}
