#nullable enable

using System.Text.Json.Serialization;

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

public sealed class Album
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("coverUri")]
    public string? CoverUri { get; set; }

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
