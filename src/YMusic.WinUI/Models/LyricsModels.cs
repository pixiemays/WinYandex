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

public sealed partial class LyricLine : ObservableObject
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    public static List<LyricLine> ParseLrc(string rawLrc)
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
        return result;
    }
}
