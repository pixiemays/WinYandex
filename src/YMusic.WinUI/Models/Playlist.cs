#nullable enable

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using YMusic.WinUI.Converters;

namespace YMusic.WinUI.Models;

public sealed class PlaylistOwner
{
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Uid { get; set; }

    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class PlaylistTrackItem
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumId")]
    [JsonConverter(typeof(NullableStringOrNumberConverter))]
    public string? AlbumId { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("track")]
    public Track? Track { get; set; }
}

public sealed partial class Playlist : ObservableObject
{
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isPinned;

    [JsonIgnore]
    public string PinKey => $"playlist:{Uid}:{Kind}";
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Uid { get; set; }

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("cover")]
    public Cover? Cover { get; set; }

    [JsonPropertyName("owner")]
    public PlaylistOwner? Owner { get; set; }

    [JsonPropertyName("tracks")]
    public List<PlaylistTrackItem> Tracks { get; set; } = new();

    [JsonIgnore]
    public string OwnerName => !string.IsNullOrWhiteSpace(Owner?.DisplayName)
        ? Owner.DisplayName
        : (!string.IsNullOrWhiteSpace(Owner?.Name)
            ? Owner.Name
            : (!string.IsNullOrWhiteSpace(Owner?.Login) ? Owner.Login : "Yandex Music"));

    [JsonIgnore]
    public List<Track> ResolvedTracks => Tracks
        .Where(t => t.Track != null)
        .Select(t => t.Track!)
        .ToList();

    [JsonIgnore]
    public string FormattedTrackCount => TrackCount == 1 ? "1 track" : $"{TrackCount} tracks";

    [JsonIgnore]
    public string? CoverUrl => GetCoverUrl(300);

    public string? GetCoverUrl(int size = 300)
    {
        return Cover?.GetFormattedUrl(size);
    }
}

