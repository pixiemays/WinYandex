using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services;

internal static class StreamFileInfo
{
    // The signing key and client header must match.
    internal const string Client = "YandexMusicDesktopAppWindows/5.95.0";
    private const string SignKey = "kzqU4XhfCaY6B6JTHODeq5";

    internal static string CreateQuery(string trackId, AudioQualityPreference preference, long timestamp)
    {
        const string quality = "hq";
        const string transport = "raw";
        string codecs = preference switch
        {
            AudioQualityPreference.AacPreferred => "aac",
            AudioQualityPreference.Mp3Preferred => "mp3",
            _ => "mp3,aac"
        };
        string ts = timestamp.ToString(CultureInfo.InvariantCulture);
        string message = ts + trackId + quality + codecs.Replace(",", "") + transport;
        byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SignKey), Encoding.UTF8.GetBytes(message));
        string sign = Convert.ToBase64String(hash).TrimEnd('=');
        return $"ts={ts}&trackId={Uri.EscapeDataString(trackId)}&quality={quality}" +
            $"&codecs={Uri.EscapeDataString(codecs)}&transports={transport}&sign={Uri.EscapeDataString(sign)}";
    }

    internal static (string? Url, StreamAudioQuality? Quality) Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement;
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("result", out var wrapped))
        {
            result = wrapped;
        }

        if (result.ValueKind != JsonValueKind.Object ||
            (!result.TryGetProperty("fileInfo", out var info) &&
             !result.TryGetProperty("downloadInfo", out info)) ||
            info.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        // MediaPlayer consumes raw files directly; encrypted streams need a separate decoder.
        if (info.TryGetProperty("transport", out var transport) &&
            (transport.ValueKind != JsonValueKind.String || transport.GetString() != "raw"))
        {
            return (null, null);
        }
        if (info.TryGetProperty("key", out var key) && key.ValueKind != JsonValueKind.Null &&
            (key.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(key.GetString())))
        {
            return (null, null);
        }

        string? url = info.TryGetProperty("url", out var direct) && direct.ValueKind == JsonValueKind.String
            ? direct.GetString() : null;
        if (!IsHttpUrl(url) && info.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array)
        {
            url = urls.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()).FirstOrDefault(IsHttpUrl);
        }
        if (!IsHttpUrl(url))
        {
            return (null, null);
        }

        int bitrate = 0;
        if (info.TryGetProperty("bitrate", out var value))
        {
            if (value.ValueKind == JsonValueKind.Number)
                value.TryGetInt32(out bitrate);
            else if (value.ValueKind == JsonValueKind.String)
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out bitrate);
        }
        return (url, new StreamAudioQuality
        {
            Codec = info.TryGetProperty("codec", out var codec) && codec.ValueKind == JsonValueKind.String
                ? codec.GetString() ?? "" : "",
            BitrateInKbps = bitrate
        });
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
