#nullable enable

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YMusic.WinUI.Models;

public sealed class Cover
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("itemsUri")]
    public string[]? ItemsUri { get; set; }

    public string? GetFormattedUrl(int size = 200)
    {
        string? rawUri = Uri;
        if (string.IsNullOrWhiteSpace(rawUri) && ItemsUri != null && ItemsUri.Length > 0)
        {
            rawUri = ItemsUri[0];
        }

        if (string.IsNullOrWhiteSpace(rawUri))
        {
            return null;
        }

        string formatted = rawUri.Replace("%%", $"{size}x{size}");
        if (!formatted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !formatted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            formatted = "https://" + formatted;
        }

        return formatted;
    }
}

public sealed partial class Album : ObservableObject
{
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isLiked;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isPinned;

    [JsonIgnore]
    public string PinKey => $"album:{Id}";

    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonIgnore]
    public DateTime? ParsedReleaseDate
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReleaseDate) && DateTime.TryParse(ReleaseDate, out var dt))
            {
                return dt;
            }
            if (Year.HasValue && Year.Value > 0)
            {
                return new DateTime(Year.Value, 1, 1);
            }
            return null;
        }
    }

    [JsonPropertyName("coverUri")]
    public string? CoverUri { get; set; }

    [JsonPropertyName("artists")]
    public List<Artist> Artists { get; set; } = new();

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("metaType")]
    public string? MetaType { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("volumes")]
    public List<List<Track>>? Volumes { get; set; }

    [JsonIgnore]
    public string ArtistNames => Artists.Count > 0
        ? string.Join(", ", Artists.Select(a => a.Name))
        : string.Empty;

    [JsonIgnore]
    public string FormattedTrackCount => string.Equals(Type, "single", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(MetaType, "single", StringComparison.OrdinalIgnoreCase)
        ? "Сингл"
        : TrackCount switch
        {
            1 => "1 трек",
            2 or 3 or 4 => $"{TrackCount} трека",
            _ => $"{TrackCount} треков"
        };

    [JsonIgnore]
    public string FormattedYearAndTracks => Year.HasValue ? $"{Year.Value} • {FormattedTrackCount}" : FormattedTrackCount;

    [JsonIgnore]
    public string? CoverUrl => GetCoverUrl(300);

    [JsonIgnore]
    public List<Track> AllTracks => Volumes != null
        ? Volumes.SelectMany(v => v).ToList()
        : new List<Track>();

    public string? GetCoverUrl(int size = 200)
    {
        if (string.IsNullOrWhiteSpace(CoverUri))
        {
            return null;
        }

        string formatted = CoverUri.Replace("%%", $"{size}x{size}");
        if (!formatted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !formatted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            formatted = "https://" + formatted;
        }

        return formatted;
    }
}

