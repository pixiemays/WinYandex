#nullable enable

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YMusic.WinUI.Models;

public sealed class TrackLyricsResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("lyrics")]
    public string? Lyrics { get; set; }

    [JsonPropertyName("fullLyrics")]
    public string? FullLyrics { get; set; }

    [JsonPropertyName("hasRights")]
    public bool HasRights { get; set; }

    [JsonPropertyName("showTranslation")]
    public bool ShowTranslation { get; set; }

    [JsonPropertyName("textLanguage")]
    public string? TextLanguage { get; set; }
}

public sealed class TrackSupplementResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("lyrics")]
    public TrackLyricsResult? Lyrics { get; set; }
}

public sealed class LrclibResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("trackName")]
    public string? TrackName { get; set; }

    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("albumName")]
    public string? AlbumName { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("instrumental")]
    public bool Instrumental { get; set; }

    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }
}

public sealed partial class LyricLine : ObservableObject
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineOpacity))]
    [NotifyPropertyChangedFor(nameof(LineFontSize))]
    [NotifyPropertyChangedFor(nameof(LineFontWeight))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineOpacity))]
    private bool _isPast;

    public bool HasTimestamp => Timestamp >= TimeSpan.Zero;

    public string FormattedTimestamp => Timestamp >= TimeSpan.Zero
        ? (Timestamp.Hours > 0 ? Timestamp.ToString(@"h\:mm\:ss") : Timestamp.ToString(@"m\:ss"))
        : string.Empty;

    public double LineOpacity => IsActive ? 1.0 : (IsPast ? 0.35 : 0.65);

    public double LineFontSize => IsActive ? 32.0 : 24.0;

    public Windows.UI.Text.FontWeight LineFontWeight => IsActive
        ? Microsoft.UI.Text.FontWeights.ExtraBold
        : Microsoft.UI.Text.FontWeights.SemiBold;

    public static List<LyricLine> ParseLrc(string rawLrc, double durationSeconds = 0)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return result;
        }

        // Standard LRC regex: [mm:ss.xx] or [mm:ss] or [mm:ss.xxx]
        var tagRegex = new Regex(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

        var lines = rawLrc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var matches = tagRegex.Matches(line);
            if (matches.Count == 0)
            {
                // Line without timestamp, add as sequential or omit metadata tags like [ar:Artist]
                if (!line.StartsWith("[") || !line.Contains("]"))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        result.Add(new LyricLine { Timestamp = TimeSpan.Zero, Text = line.Trim() });
                    }
                }
                continue;
            }

            // Extract the actual lyric text by stripping all timestamps
            var text = tagRegex.Replace(line, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                int minutes = int.Parse(match.Groups[1].Value);
                int seconds = int.Parse(match.Groups[2].Value);
                int millis = 0;
                if (match.Groups[3].Success)
                {
                    string msStr = match.Groups[3].Value;
                    if (msStr.Length == 1) millis = int.Parse(msStr) * 100;
                    else if (msStr.Length == 2) millis = int.Parse(msStr) * 10;
                    else millis = int.Parse(msStr.Substring(0, 3));
                }

                var timestamp = new TimeSpan(0, 0, minutes, seconds, millis);
                result.Add(new LyricLine
                {
                    Timestamp = timestamp,
                    Text = text
                });
            }
        }

        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // If lyrics only have plain text without timestamps, distribute timestamps proportionally across duration
        bool hasTimestamps = result.Any(l => l.Timestamp > TimeSpan.Zero);
        if (!hasTimestamps && result.Count > 0 && durationSeconds > 10)
        {
            double introSec = Math.Min(12.0, durationSeconds * 0.08);
            double outroSec = Math.Min(10.0, durationSeconds * 0.06);
            double singDuration = Math.Max(durationSeconds - introSec - outroSec, durationSeconds * 0.7);
            double step = singDuration / Math.Max(1, result.Count);

            for (int i = 0; i < result.Count; i++)
            {
                result[i].Timestamp = TimeSpan.FromSeconds(introSec + (i * step));
            }
        }

        return result;
    }
}
