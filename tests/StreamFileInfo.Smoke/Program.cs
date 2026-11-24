using System.Text.Json;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}

var query = StreamFileInfo.CreateQuery("12345:678", AudioQualityPreference.Mp3Preferred, 1700000000);
// Independent HMAC-SHA256 vector generated with Node.js crypto.
Check(query == "ts=1700000000&trackId=12345%3A678&quality=hq&codecs=mp3&transports=raw&sign=tJ3Z93Uy67JSUvBlaCp7JYxlgVLfNQLX%2BXJ7H1EIWTg", "Signed query");
Check(StreamFileInfo.CreateQuery("1", AudioQualityPreference.AacPreferred, 1).Contains("codecs=aac&"), "AAC preference");
Check(StreamFileInfo.CreateQuery("1", AudioQualityPreference.HighestBitrate, 1).Contains("codecs=mp3%2Caac&"), "Highest bitrate codecs");

var modern = StreamFileInfo.Parse("""{"result":{"fileInfo":{"url":"https://cdn.example/song","codec":"mp3","bitrate":320,"transport":"raw"}}}""");
Check(modern.Url == "https://cdn.example/song" && modern.Quality?.BitrateInKbps == 320 && modern.Quality.Codec == "mp3", "V2 fileInfo");
var legacy = StreamFileInfo.Parse("""{"result":{"downloadInfo":{"urls":["https://cdn.example/old"],"codec":"aac","bitrate":"192","transport":"raw"}}}""");
Check(legacy.Url == "https://cdn.example/old" && legacy.Quality?.BitrateInKbps == 192, "Legacy downloadInfo");
Check(StreamFileInfo.Parse("""{"fileInfo":{"url":"https://cdn.example/direct"}}""").Url != null, "Unwrapped response");
Check(StreamFileInfo.Parse("""{"result":{"fileInfo":{"url":"invalid","urls":[null,"file:///local","https://cdn.example/fallback"]}}}""").Url == "https://cdn.example/fallback", "Alternate valid URL");
foreach (var json in new[]
{
    "{}", "null", "[]", "{\"result\":null}", "{\"result\":[]}",
    """{"result":{"fileInfo":null}}""",
    """{"result":{"fileInfo":{"url":"file:///local"}}}""",
    """{"result":{"fileInfo":{"url":"https://cdn.example/crypt","transport":"encraw"}}}""",
    """{"result":{"fileInfo":{"url":"https://cdn.example/crypt","key":"deadbeef"}}}""",
    """{"result":{"fileInfo":{"url":123}}}"""
})
{
    Check(StreamFileInfo.Parse(json).Url == null, "Unusable response: " + json);
}
bool malformedRejected = false;
try { StreamFileInfo.Parse("{"); }
catch (JsonException) { malformedRejected = true; }
Check(malformedRejected, "Malformed JSON");
Console.WriteLine($"Passed {checks} checks.");
