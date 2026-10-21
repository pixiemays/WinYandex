#nullable enable

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class FckCensorService : IFckCensorService
{
    private const string RemoteListUrl = "https://raw.githubusercontent.com/Hazzz895/FckCensorData/refs/heads/main/list.json";
    private const string SupabaseApiUrl = "https://pzomqvgckpgkshxhpite.supabase.co/rest/v1/reported_tracks";
    private const string SupabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InB6b21xdmdja3Bna3NoeGhwaXRlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzUwNTgzNDEsImV4cCI6MjA5MDYzNDM0MX0.ggCxM-ver3gDWUBWyhSBfy3n7rpdW8jtlxRQVCXkhNg";

    private readonly HttpClient _httpClient = new();
    private readonly string _baseDir;
    private readonly string _tracksDir;
    private readonly string _exceptionsFile;
    private readonly string _reportedFile;
    private readonly string _localTracksFile;
    private readonly string _remoteCacheFile;

    private readonly object _lock = new();

    private Dictionary<string, string> _remoteTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _localTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _remoteExceptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedTracks = new(StringComparer.OrdinalIgnoreCase);

    private sealed class RemoteListModel
    {
        [JsonPropertyName("tracks")]
        public Dictionary<string, string>? Tracks { get; set; }
    }

    private sealed class SupabaseReportPayload
    {
        [JsonPropertyName("track_id")]
        public long TrackId { get; set; }

        [JsonPropertyName("replaced")]
        public bool Replaced { get; set; }
    }

    public event EventHandler<string>? TrackReplacementChanged;

    public FckCensorService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _baseDir = Path.Combine(localAppData, "YMusic", "FckCensor");
        _tracksDir = Path.Combine(_baseDir, "tracks");
        _exceptionsFile = Path.Combine(_baseDir, "exceptions.json");
        _reportedFile = Path.Combine(_baseDir, "reported.json");
        _localTracksFile = Path.Combine(_baseDir, "local_tracks.json");
        _remoteCacheFile = Path.Combine(_baseDir, "remote_cache.json");

        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_tracksDir);

        LoadLocalState();
        _ = InitializeAsync();
    }

    private void LoadLocalState()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_localTracksFile))
                {
                    string json = File.ReadAllText(_localTracksFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            if (File.Exists(kvp.Value))
                            {
                                _localTracks[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading local_tracks: {ex.Message}");
            }

            try
            {
                if (File.Exists(_exceptionsFile))
                {
                    string json = File.ReadAllText(_exceptionsFile);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var id in list) _remoteExceptions.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading exceptions: {ex.Message}");
            }

            try
            {
                if (File.Exists(_reportedFile))
                {
                    string json = File.ReadAllText(_reportedFile);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var id in list) _reportedTracks.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading reported: {ex.Message}");
            }

            try
            {
                if (File.Exists(_remoteCacheFile))
                {
                    string json = File.ReadAllText(_remoteCacheFile);
                    var model = JsonSerializer.Deserialize<RemoteListModel>(json);
                    if (model?.Tracks != null)
                    {
                        _remoteTracks = new Dictionary<string, string>(model.Tracks, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading cached remote tracks: {ex.Message}");
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.GetAsync(RemoteListUrl, cts.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var model = JsonSerializer.Deserialize<RemoteListModel>(json);
                if (model?.Tracks != null)
                {
                    lock (_lock)
                    {
                        _remoteTracks = new Dictionary<string, string>(model.Tracks, StringComparer.OrdinalIgnoreCase);
                    }

                    try
                    {
                        File.WriteAllText(_remoteCacheFile, json);
                    }
                    catch { }

                    Debug.WriteLine($"[FckCensor] Successfully loaded {_remoteTracks.Count} tracks from remote repository.");
                    TrackReplacementChanged?.Invoke(this, string.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Failed to fetch remote track list: {ex.Message}");
        }
    }

    public bool IsReplaced(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        lock (_lock)
        {
            if (_localTracks.ContainsKey(trackId))
            {
                return true;
            }

            if (_remoteExceptions.Contains(trackId))
            {
                return false;
            }

            return _remoteTracks.ContainsKey(trackId);
        }
    }

    public string? GetReplacementUrl(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return null;

        lock (_lock)
        {
            if (_localTracks.TryGetValue(trackId, out var localPath))
            {
                if (File.Exists(localPath))
                {
                    return localPath;
                }
            }

            if (_remoteExceptions.Contains(trackId))
            {
                return null;
            }

            if (_remoteTracks.TryGetValue(trackId, out var remoteUrl))
            {
                return remoteUrl;
            }

            return null;
        }
    }

    public async Task<bool> ReplaceTrackWithFileAsync(string trackId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(trackId) || !File.Exists(sourceFilePath))
        {
            return false;
        }

        try
        {
            string ext = Path.GetExtension(sourceFilePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";

            string destFileName = $"{trackId}{ext}";
            string destFilePath = Path.Combine(_tracksDir, destFileName);

            File.Copy(sourceFilePath, destFilePath, true);

            lock (_lock)
            {
                _localTracks[trackId] = destFilePath;
                _remoteExceptions.Remove(trackId);
                SaveLocalTracks();
                SaveExceptions();
            }

            _ = ReportCensorshipAsync(trackId, replaced: true);

            TrackReplacementChanged?.Invoke(this, trackId);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] ReplaceTrackWithFileAsync error: {ex.Message}");
            return false;
        }
    }

    public Task<bool> RemoveReplacementAsync(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return Task.FromResult(false);

        lock (_lock)
        {
            bool hadLocal = _localTracks.TryGetValue(trackId, out var path);
            if (hadLocal)
            {
                _localTracks.Remove(trackId);
                SaveLocalTracks();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }
            }
            else if (_remoteTracks.ContainsKey(trackId))
            {
                _remoteExceptions.Add(trackId);
                SaveExceptions();
            }
        }

        TrackReplacementChanged?.Invoke(this, trackId);
        return Task.FromResult(true);
    }

    public async Task<bool> ReportCensorshipAsync(string trackId, bool replaced = false)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        if (!long.TryParse(trackId, out long numericId)) return false;

        lock (_lock)
        {
            if (_reportedTracks.Contains(trackId))
            {
                return true;
            }
        }

        try
        {
            var payload = new SupabaseReportPayload
            {
                TrackId = numericId,
                Replaced = replaced
            };

            string json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, SupabaseApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("apikey", SupabaseKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseKey);

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                lock (_lock)
                {
                    _reportedTracks.Add(trackId);
                    SaveReported();
                }
                Debug.WriteLine($"[FckCensor] Successfully reported track {trackId} (replaced: {replaced})");
                return true;
            }
            else
            {
                Debug.WriteLine($"[FckCensor] Report failed with HTTP {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Report error: {ex.Message}");
            return false;
        }
    }

    public bool IsReported(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        lock (_lock)
        {
            return _reportedTracks.Contains(trackId);
        }
    }

    private void SaveLocalTracks()
    {
        try
        {
            string json = JsonSerializer.Serialize(_localTracks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_localTracksFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Save local_tracks error: {ex.Message}");
        }
    }

    private void SaveExceptions()
    {
        try
        {
            string json = JsonSerializer.Serialize(_remoteExceptions.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_exceptionsFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Save exceptions error: {ex.Message}");
        }
    }

    private void SaveReported()
    {
        try
        {
            string json = JsonSerializer.Serialize(_reportedTracks.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_reportedFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Save reported error: {ex.Message}");
        }
    }
}
