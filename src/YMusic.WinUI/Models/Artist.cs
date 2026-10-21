#nullable enable

using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class Artist
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("cover")]
    public Cover? Cover { get; set; }
}
