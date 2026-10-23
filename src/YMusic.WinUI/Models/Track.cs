#nullable enable

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using YMusic.WinUI.Converters;

namespace YMusic.WinUI.Models;

public sealed partial class Track : ObservableObject
{
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isCurrent;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isPlaying;

    [JsonIgnore]
    public bool IsCurrentTrack => IsCurrent;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isLiked;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isReplaced;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isCached;

    [JsonIgnore]
    public static Func<string, bool>? IsTrackReplacedChecker { get; set; }

    [JsonIgnore]
    public static Func<string, YMusic.WinUI.Services.Interfaces.TrackSpoof?>? TrackSpoofGetter { get; set; }

    [JsonIgnore]
    public static Func<string, bool>? IsTrackCachedChecker { get; set; }

    private string _id = string.Empty;

    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string Id
    {
        get => _id;
        set
        {
            if (_id != value)
            {
                _id = value;
                RefreshReplacedStatus();
                RefreshCachedStatus();
            }
        }
    }

    public void RefreshReplacedStatus()
    {
        if (IsTrackReplacedChecker != null && !string.IsNullOrWhiteSpace(_id))
        {
            IsReplaced = IsTrackReplacedChecker(_id);
        }

        if (TrackSpoofGetter != null && !string.IsNullOrWhiteSpace(_id))
        {
            var spoof = TrackSpoofGetter(_id);
            if (spoof != null)
            {
                if (!string.IsNullOrWhiteSpace(spoof.Title) && Title != spoof.Title)
                {
                    Title = spoof.Title;
                }
                if (!string.IsNullOrWhiteSpace(spoof.CoverUri) && CoverUri != spoof.CoverUri)
                {
                    CoverUri = spoof.CoverUri;
                }
            }
        }
    }

    public void RefreshCachedStatus()
    {
        if (IsTrackCachedChecker != null && !string.IsNullOrWhiteSpace(_id))
        {
            IsCached = IsTrackCachedChecker(_id);
        }
    }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("available")]
    public bool Available
    {
        get => _available || IsReplaced;
        set => _available = value;
    }
    private bool _available = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration))]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    [property: JsonPropertyName("durationMs")]
    private long _durationMs;

    [JsonPropertyName("artists")]
    public List<Artist> Artists { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumTitle))]
    [NotifyPropertyChangedFor(nameof(CoverUrl))]
    [NotifyPropertyChangedFor(nameof(HdCoverUrl))]
    [NotifyPropertyChangedFor(nameof(ThumbnailUrl))]
    [property: JsonPropertyName("albums")]
    private List<Album> _albums = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverUrl))]
    [NotifyPropertyChangedFor(nameof(HdCoverUrl))]
    [NotifyPropertyChangedFor(nameof(ThumbnailUrl))]
    [property: JsonPropertyName("coverUri")]
    private string? _coverUri;

    [JsonPropertyName("ogImage")]
    public string? OgImage { get; set; }

    [JsonIgnore]
    public string FullTitle => string.IsNullOrWhiteSpace(Version) ? Title : $"{Title} ({Version})";

    [JsonIgnore]
    public string ArtistNames => Artists.Count > 0
        ? string.Join(", ", Artists.Select(a => a.Name))
        : "Unknown Artist";

    [JsonIgnore]
    public string AlbumTitle => Albums.Count > 0
        ? Albums[0].Title
        : string.Empty;

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

    [JsonIgnore]
    public string FormattedDuration => Duration.Hours > 0
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    [JsonIgnore]
    public string FckBadgeTooltip => App.Loc?.CurrentLanguage == "en"
        ? "Explicit version restored by FckCensor"
        : "Трек подменен FckCensor (Uncensored)";

    [JsonIgnore]
    public string? CoverUrl => GetCoverUrl(200);

    [JsonIgnore]
    public string? HdCoverUrl => GetCoverUrl(800);

    [JsonIgnore]
    public string? ThumbnailUrl => GetCoverUrl(100);

    public string? GetCoverUrl(int size = 200)
    {
        string? raw = CoverUri;
        if (string.IsNullOrWhiteSpace(raw) && Albums.Count > 0)
        {
            raw = Albums[0].CoverUri;
            if (string.IsNullOrWhiteSpace(raw))
            {
                var spoof = Album.AlbumSpoofGetter?.Invoke(Albums[0].Id);
                if (spoof != null && !string.IsNullOrWhiteSpace(spoof.CoverUri))
                {
                    raw = spoof.CoverUri;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(OgImage))
        {
            raw = OgImage;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string formatted = raw.Replace("%%", $"{size}x{size}");
        if (!formatted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !formatted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            formatted = "https://" + formatted;
        }

        return formatted;
    }

    public static bool IsSameTrackId(string? id1, string? id2)
    {
        if (string.IsNullOrWhiteSpace(id1) || string.IsNullOrWhiteSpace(id2))
            return false;

        if (string.Equals(id1, id2, StringComparison.OrdinalIgnoreCase))
            return true;

        string clean1 = id1.Split(':')[0];
        string clean2 = id2.Split(':')[0];
        return string.Equals(clean1, clean2, StringComparison.OrdinalIgnoreCase);
    }
}

