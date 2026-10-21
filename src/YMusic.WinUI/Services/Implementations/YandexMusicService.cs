#nullable enable

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class YandexMusicService : IYandexMusicService
{
    private const string ApiBaseUrl = "https://api.music.yandex.net/";
    private const string UserAgentHeader = "YandexMusicDesktopAppWindows/5.29.1";
    private const string ClientHeader = "YandexMusicDesktopAppWindows/5.29.1";

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ICredentialStorageService? _credentialStorage;
    private readonly HashSet<string> _likedTrackIds = new();
    private readonly HashSet<long> _likedAlbumIds = new();
    private string? _cachedUserId;
    private string? _token;

    public event EventHandler? UnauthorizedResponseReceived;
    public event EventHandler<(string TrackId, bool IsLiked)>? TrackLikeChanged;
    public event EventHandler<(long AlbumId, bool IsLiked)>? AlbumLikeChanged;
    public event EventHandler? LikedTracksLoaded;

    public string? CurrentToken => _token;

    public YandexMusicService(ICredentialStorageService? credentialStorage = null, HttpClient? httpClient = null)
    {
        _credentialStorage = credentialStorage;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(ApiBaseUrl);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentHeader);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("X-Yandex-Music-Client", ClientHeader);
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "ru");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
    }

    public void SetOAuthToken(string token)
    {
        _token = token?.Trim();
        if (!string.IsNullOrWhiteSpace(_token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", _token);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<UserProfile?> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        var (profile, _) = await GetAccountStatusDetailedAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<(UserProfile? Profile, string? Error)> GetAccountStatusDetailedAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync(HttpMethod.Get, "account/status", null, cancellationToken).ConfigureAwait(false);
        if (response == null)
        {
            return (null, "No response from server.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[API account/status] HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {json}");

        if (!response.IsSuccessStatusCode)
        {
            return (null, $"HTTP {(int)response.StatusCode}: {json}");
        }

        try
        {
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<AccountStatusResult>>(json, _jsonOptions);
            if (apiResult?.Result == null)
            {
                return (null, $"Missing 'result' field in response: {json}");
            }

            return (UserProfile.FromAccountStatus(apiResult.Result), null);
        }
        catch (Exception ex)
        {
            return (null, $"JSON parse error: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> GetLikedTrackIdsAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/tracks", null, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return Array.Empty<string>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("result", out var resultElement))
        {
            return Array.Empty<string>();
        }

        var ids = new List<string>();

        // Scenario 1: result is an object containing library.tracks
        if (resultElement.ValueKind == JsonValueKind.Object)
        {
            JsonElement tracksElement;
            if (resultElement.TryGetProperty("library", out var libElement) && libElement.TryGetProperty("tracks", out var innerTracks))
            {
                tracksElement = innerTracks;
            }
            else if (resultElement.TryGetProperty("tracks", out var directTracks))
            {
                tracksElement = directTracks;
            }
            else
            {
                tracksElement = default;
            }

            if (tracksElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tracksElement.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        var idStr = idProp.ValueKind == JsonValueKind.String
                            ? idProp.GetString()
                            : (idProp.ValueKind == JsonValueKind.Number ? idProp.GetRawText() : null);
                        if (!string.IsNullOrWhiteSpace(idStr))
                        {
                            ids.Add(idStr);
                        }
                    }
                }
            }
        }
        // Scenario 2: result is directly an array of tracks
        else if (resultElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    var idStr = idProp.ValueKind == JsonValueKind.String
                        ? idProp.GetString()
                        : (idProp.ValueKind == JsonValueKind.Number ? idProp.GetRawText() : null);
                    if (!string.IsNullOrWhiteSpace(idStr))
                    {
                        ids.Add(idStr);
                    }
                }
            }
        }

        lock (_likedTrackIds)
        {
            _likedTrackIds.Clear();
            foreach (var id in ids)
            {
                _likedTrackIds.Add(id);
                string cleanId = id.Split(':')[0];
                if (!string.Equals(cleanId, id, StringComparison.OrdinalIgnoreCase))
                {
                    _likedTrackIds.Add(cleanId);
                }
            }
        }
        LikedTracksLoaded?.Invoke(this, EventArgs.Empty);

        return ids;
    }

    public bool IsTrackLiked(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        string cleanId = trackId.Split(':')[0];
        lock (_likedTrackIds)
        {
            return _likedTrackIds.Contains(trackId) || _likedTrackIds.Contains(cleanId);
        }
    }

    public async Task<bool> LikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_likedTrackIds)
        {
            _likedTrackIds.Add(trackId);
            _likedTrackIds.Add(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                RollbackLike(trackId, cleanId);
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/tracks/add-multiple", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            RollbackLike(trackId, cleanId);
            return false;
        }
        catch
        {
            RollbackLike(trackId, cleanId);
            throw;
        }
    }

    private void RollbackLike(string trackId, string cleanId)
    {
        lock (_likedTrackIds)
        {
            _likedTrackIds.Remove(trackId);
            _likedTrackIds.Remove(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, false));
    }

    public async Task<bool> UnlikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_likedTrackIds)
        {
            _likedTrackIds.Remove(trackId);
            _likedTrackIds.Remove(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                RollbackUnlike(trackId, cleanId);
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/tracks/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            RollbackUnlike(trackId, cleanId);
            return false;
        }
        catch
        {
            RollbackUnlike(trackId, cleanId);
            throw;
        }
    }

    private void RollbackUnlike(string trackId, string cleanId)
    {
        lock (_likedTrackIds)
        {
            _likedTrackIds.Add(trackId);
            _likedTrackIds.Add(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, true));
    }

    public async Task EnsureLikedTracksLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedTrackIds)
        {
            if (_likedTrackIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            await GetLikedTrackIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedUserId))
        {
            return _cachedUserId;
        }

        string? stored = _credentialStorage?.GetUserId();
        if (!string.IsNullOrWhiteSpace(stored))
        {
            _cachedUserId = stored;
            return _cachedUserId;
        }

        var profile = await GetAccountStatusAsync(cancellationToken).ConfigureAwait(false);
        if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
        {
            _cachedUserId = profile.UserId;
            _credentialStorage?.SaveUserId(profile.UserId);
            return _cachedUserId;
        }

        return null;
    }

    public async Task<IReadOnlyList<Track>> GetTracksAsync(IEnumerable<string> trackIds, CancellationToken cancellationToken = default)
    {
        var idList = trackIds.ToList();
        if (idList.Count == 0)
        {
            return Array.Empty<Track>();
        }

        var allTracks = new List<Track>();
        const int batchSize = 100;

        for (int i = 0; i < idList.Count; i += batchSize)
        {
            var batch = idList.Skip(i).Take(batchSize).ToList();
            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", string.Join(",", batch))
            };

            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, "tracks", content, cancellationToken).ConfigureAwait(false);

            if (response == null || !response.IsSuccessStatusCode)
            {
                continue;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<Track>>>(json, _jsonOptions);

            if (apiResult?.Result != null)
            {
                allTracks.AddRange(apiResult.Result);
            }
        }

        return allTracks;
    }

    public async Task<IReadOnlyList<Playlist>> GetUserPlaylistsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var allPlaylists = new List<Playlist>();

        // 1. User's created playlists
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/playlists/list", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<Playlist>>>(json, _jsonOptions);
                if (apiResult?.Result != null)
                {
                    allPlaylists.AddRange(apiResult.Result);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetUserPlaylists warning] {ex.Message}");
        }

        // 2. User's liked / saved playlists
        try
        {
            using var likedResponse = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/playlists", null, cancellationToken).ConfigureAwait(false);
            if (likedResponse != null && likedResponse.IsSuccessStatusCode)
            {
                var json = await likedResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in resEl.EnumerateArray())
                    {
                        JsonElement target = item;
                        if (item.TryGetProperty("playlist", out var plEl))
                        {
                            target = plEl;
                        }

                        var pl = JsonSerializer.Deserialize<Playlist>(target.GetRawText(), _jsonOptions);
                        if (pl != null && !allPlaylists.Any(existing => existing.Kind == pl.Kind && existing.Uid == pl.Uid))
                        {
                            allPlaylists.Add(pl);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLikedPlaylists warning] {ex.Message}");
        }

        return allPlaylists;
    }

    public async Task<IReadOnlyList<Album>> GetAlbumsAsync(IEnumerable<long> albumIds, CancellationToken cancellationToken = default)
    {
        var idList = albumIds.Distinct().ToList();
        if (idList.Count == 0) return Array.Empty<Album>();

        var result = new List<Album>();
        const int chunkSize = 50;

        for (int i = 0; i < idList.Count; i += chunkSize)
        {
            var chunk = idList.Skip(i).Take(chunkSize);
            string idsStr = string.Join(",", chunk);
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("album-ids", idsStr)
            });

            try
            {
                using var response = await SendRequestAsync(HttpMethod.Post, "albums", content, cancellationToken).ConfigureAwait(false);
                if (response != null && response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<Album>>>(json, _jsonOptions);
                    if (apiResult?.Result != null)
                    {
                        result.AddRange(apiResult.Result);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetAlbums Warning] {ex.Message}");
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<Album>> GetLikedAlbumsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var albumIds = new List<long>();

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/albums", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in resEl.EnumerateArray())
                    {
                        if (item.TryGetProperty("album", out var albEl) && albEl.TryGetProperty("id", out var subId) && subId.TryGetInt64(out long sid))
                        {
                            albumIds.Add(sid);
                        }
                        else if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long id))
                        {
                            albumIds.Add(id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLikedAlbums warning] {ex.Message}");
        }

        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Clear();
            foreach (var id in albumIds)
            {
                _likedAlbumIds.Add(id);
            }
        }

        if (albumIds.Count == 0)
        {
            return Array.Empty<Album>();
        }

        var fullAlbums = await GetAlbumsAsync(albumIds, cancellationToken).ConfigureAwait(false);
        var albumDict = fullAlbums.ToDictionary(a => a.Id);

        var result = new List<Album>();
        foreach (var id in albumIds)
        {
            if (albumDict.TryGetValue(id, out var album))
            {
                album.IsLiked = true;
                result.Add(album);
            }
        }

        return result;
    }

    public bool IsAlbumLiked(long albumId)
    {
        lock (_likedAlbumIds)
        {
            return _likedAlbumIds.Contains(albumId);
        }
    }

    public async Task<bool> LikeAlbumAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Add(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
                AlbumLikeChanged?.Invoke(this, (albumId, false));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("album-id", albumId.ToString()),
                new("album-ids", albumId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/add-multiple", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackContent = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("album-id", albumId.ToString()) });
            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/add", fallbackContent, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, false));
            return false;
        }
        catch
        {
            lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, false));
            throw;
        }
    }

    public async Task<bool> UnlikeAlbumAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Remove(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
                AlbumLikeChanged?.Invoke(this, (albumId, true));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("album-id", albumId.ToString()),
                new("album-ids", albumId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/{albumId}/remove", null, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, true));
            return false;
        }
        catch
        {
            lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, true));
            throw;
        }
    }


    public AudioQualityPreference PreferredQuality { get; set; } = AudioQualityPreference.AacPreferred;

    public async Task<string?> GetTrackDownloadUrlAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var (url, _) = await GetTrackStreamInfoAsync(trackId, cancellationToken).ConfigureAwait(false);
        return url;
    }

    public async Task<(string? Url, StreamAudioQuality? Quality)> GetTrackStreamInfoAsync(string trackId, CancellationToken cancellationToken = default)
    {
        using var downloadInfoResponse = await SendRequestAsync(HttpMethod.Get, $"tracks/{trackId}/download-info", null, cancellationToken).ConfigureAwait(false);
        if (downloadInfoResponse == null || !downloadInfoResponse.IsSuccessStatusCode)
        {
            return (null, null);
        }

        var json = await downloadInfoResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<DownloadInfo>>>(json, _jsonOptions);

        if (apiResult?.Result == null || apiResult.Result.Count == 0)
        {
            return (null, null);
        }

        // Filter out 30-second preview tracks if full tracks exist
        var candidates = apiResult.Result.Where(x => !x.Preview).ToList();
        if (candidates.Count == 0)
        {
            candidates = apiResult.Result;
        }

        DownloadInfo? selectedInfo = null;

        switch (PreferredQuality)
        {
            case AudioQualityPreference.AacPreferred:
                selectedInfo = candidates
                    .OrderByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps >= 192)
                    .ThenByDescending(x => string.Equals(x.Codec, "mp3", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps == 320)
                    .ThenByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.BitrateInKbps)
                    .FirstOrDefault();
                break;

            case AudioQualityPreference.Mp3Preferred:
                selectedInfo = candidates
                    .OrderByDescending(x => string.Equals(x.Codec, "mp3", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps == 320)
                    .ThenByDescending(x => string.Equals(x.Codec, "mp3", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps == 192)
                    .ThenByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps >= 192)
                    .ThenByDescending(x => x.BitrateInKbps)
                    .FirstOrDefault();
                break;

            case AudioQualityPreference.HighestBitrate:
            default:
                selectedInfo = candidates
                    .OrderByDescending(x => x.BitrateInKbps)
                    .ThenByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                break;
        }

        if (selectedInfo == null || string.IsNullOrWhiteSpace(selectedInfo.DownloadInfoUrl))
        {
            return (null, null);
        }

        // Download the XML info
        using var xmlResponse = await _httpClient.GetAsync(selectedInfo.DownloadInfoUrl, cancellationToken).ConfigureAwait(false);
        if (!xmlResponse.IsSuccessStatusCode)
        {
            return (null, null);
        }

        var xmlContent = await xmlResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Sign and build final direct stream link
        string? directUrl = StreamSigner.SignFromXml(xmlContent);

        var quality = new StreamAudioQuality
        {
            Codec = selectedInfo.Codec,
            BitrateInKbps = selectedInfo.BitrateInKbps
        };

        return (directUrl, quality);
    }

    public async Task<RotorStationResult?> GetRotorStationTracksAsync(
        string stationType = "user",
        string stationTag = "onyourwave",
        string? queueTrackId = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"rotor/station/{Uri.EscapeDataString(stationType)}:{Uri.EscapeDataString(stationTag)}/tracks";
        if (!string.IsNullOrWhiteSpace(queueTrackId))
        {
            endpoint += $"?queue={Uri.EscapeDataString(queueTrackId)}";
        }

        using var response = await SendRequestAsync(HttpMethod.Get, endpoint, null, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<RotorStationResult>>(json, _jsonOptions);
            return apiResult?.Result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Rotor API Error] {ex.Message}");
            return null;
        }
    }

    public async Task SendRotorFeedbackAsync(
        string eventType,
        string stationType,
        string stationTag,
        string trackId,
        string? batchId = null,
        double? totalPlayedSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"rotor/station/{Uri.EscapeDataString(stationType)}:{Uri.EscapeDataString(stationTag)}/feedback";
        var payload = new RotorFeedbackRequest
        {
            Type = eventType,
            Timestamp = DateTime.UtcNow.ToString("o"),
            TrackId = trackId,
            BatchId = batchId,
            TotalPlayedSeconds = totalPlayedSeconds
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await SendRequestAsync(HttpMethod.Post, endpoint, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<LyricLine>> GetTrackLyricsAsync(string trackId, CancellationToken cancellationToken = default)
    {
        // 1. Try tracks/{trackId}/lyrics?format=LRC
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"tracks/{Uri.EscapeDataString(trackId)}/lyrics?format=LRC", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<TrackLyricsResult>>(json, _jsonOptions);
                if (apiResult?.Result != null)
                {
                    if (!string.IsNullOrWhiteSpace(apiResult.Result.DownloadUrl))
                    {
                        var lrcContent = await _httpClient.GetStringAsync(apiResult.Result.DownloadUrl, cancellationToken).ConfigureAwait(false);
                        var parsed = LyricLine.ParseLrc(lrcContent);
                        if (parsed.Count > 0)
                        {
                            return parsed;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(apiResult.Result.FullLyrics))
                    {
                        var parsed = LyricLine.ParseLrc(apiResult.Result.FullLyrics);
                        if (parsed.Count > 0)
                        {
                            return parsed;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics API Warning] {ex.Message}");
        }

        // 2. Fallback to tracks/{trackId}/supplement
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"tracks/{Uri.EscapeDataString(trackId)}/supplement", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<TrackSupplementResult>>(json, _jsonOptions);
                var lyrics = apiResult?.Result?.Lyrics;
                if (lyrics != null)
                {
                    if (!string.IsNullOrWhiteSpace(lyrics.DownloadUrl))
                    {
                        var lrcContent = await _httpClient.GetStringAsync(lyrics.DownloadUrl, cancellationToken).ConfigureAwait(false);
                        var parsed = LyricLine.ParseLrc(lrcContent);
                        if (parsed.Count > 0)
                        {
                            return parsed;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(lyrics.FullLyrics))
                    {
                        var parsed = LyricLine.ParseLrc(lyrics.FullLyrics);
                        if (parsed.Count > 0)
                        {
                            return parsed;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Supplement API Warning] {ex.Message}");
        }

        return new List<LyricLine>();
    }

    public async Task<IReadOnlyList<Playlist>> GetPersonalPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var playlists = new List<Playlist>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "feed", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("generatedPlaylists", out var genEl) &&
                    genEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in genEl.EnumerateArray())
                    {
                        if (item.TryGetProperty("data", out var dataEl))
                        {
                            var playlist = JsonSerializer.Deserialize<Playlist>(dataEl.GetRawText(), _jsonOptions);
                            if (playlist != null)
                            {
                                playlists.Add(playlist);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetPersonalPlaylists Warning] {ex.Message}");
        }

        if (playlists.Count == 0)
        {
            try
            {
                using var landingResponse = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=personal-playlists", null, cancellationToken).ConfigureAwait(false);
                if (landingResponse != null && landingResponse.IsSuccessStatusCode)
                {
                    var json = await landingResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("result", out var resEl) &&
                        resEl.TryGetProperty("blocks", out var blocksEl) &&
                        blocksEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in blocksEl.EnumerateArray())
                        {
                            if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var entity in entitiesEl.EnumerateArray())
                                {
                                    if (entity.TryGetProperty("data", out var dEl))
                                    {
                                        JsonElement target = dEl;
                                        if (dEl.TryGetProperty("data", out var innerEl))
                                        {
                                            target = innerEl;
                                        }
                                        var pl = JsonSerializer.Deserialize<Playlist>(target.GetRawText(), _jsonOptions);
                                        if (pl != null)
                                        {
                                            playlists.Add(pl);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return playlists;
    }

    public async Task<IReadOnlyList<Playlist>> GetEditorialPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var playlists = new List<Playlist>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=new-playlists", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("blocks", out var blocksEl) &&
                    blocksEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in blocksEl.EnumerateArray())
                    {
                        if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var entity in entitiesEl.EnumerateArray())
                            {
                                if (entity.TryGetProperty("data", out var dataEl))
                                {
                                    var pl = JsonSerializer.Deserialize<Playlist>(dataEl.GetRawText(), _jsonOptions);
                                    if (pl != null)
                                    {
                                        playlists.Add(pl);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetEditorialPlaylists Warning] {ex.Message}");
        }

        return playlists;
    }

    public async Task<IReadOnlyList<Album>> GetNewReleasesAsync(CancellationToken cancellationToken = default)
    {
        var albums = new List<Album>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=new-releases", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("blocks", out var blocksEl) &&
                    blocksEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in blocksEl.EnumerateArray())
                    {
                        if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var entity in entitiesEl.EnumerateArray())
                            {
                                if (entity.TryGetProperty("data", out var dataEl))
                                {
                                    var album = JsonSerializer.Deserialize<Album>(dataEl.GetRawText(), _jsonOptions);
                                    if (album != null)
                                    {
                                        albums.Add(album);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetNewReleases Warning] {ex.Message}");
        }

        return albums;
    }

    public async Task<IReadOnlyList<Track>> GetChartTracksAsync(CancellationToken cancellationToken = default)
    {
        var tracks = new List<Track>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=chart", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("blocks", out var blocksEl) &&
                    blocksEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in blocksEl.EnumerateArray())
                    {
                        if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var entity in entitiesEl.EnumerateArray())
                            {
                                if (entity.TryGetProperty("data", out var dataEl) &&
                                    dataEl.TryGetProperty("track", out var trackEl))
                                {
                                    var track = JsonSerializer.Deserialize<Track>(trackEl.GetRawText(), _jsonOptions);
                                    if (track != null)
                                    {
                                        tracks.Add(track);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetChartTracks Warning] {ex.Message}");
        }

        return tracks;
    }

    public async Task<Playlist?> GetPlaylistWithTracksAsync(long ownerUid, int kind, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{ownerUid}/playlists/{kind}", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Playlist>>(json, _jsonOptions);
            var playlist = apiResult?.Result;

            if (playlist != null)
            {
                var missingIds = playlist.Tracks
                    .Where(t => t.Track == null && !string.IsNullOrWhiteSpace(t.Id))
                    .Select(t => t.Id)
                    .ToList();

                if (missingIds.Count > 0)
                {
                    var resolved = await GetTracksAsync(missingIds, cancellationToken).ConfigureAwait(false);
                    var resolvedMap = resolved.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in playlist.Tracks)
                    {
                        if (item.Track == null && resolvedMap.TryGetValue(item.Id, out var found))
                        {
                            item.Track = found;
                        }
                    }
                }
            }

            return playlist;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetPlaylistWithTracks Warning] {ex.Message}");
            return null;
        }
    }

    public async Task<Album?> GetAlbumWithTracksAsync(long albumId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"albums/{albumId}/with-tracks", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Album>>(json, _jsonOptions);
            return apiResult?.Result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetAlbumWithTracks Warning] {ex.Message}");
            return null;
        }
    }

    private async Task<HttpResponseMessage?> SendRequestAsync(
        HttpMethod method,
        string endpoint,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, endpoint);
        if (content != null)
        {
            request.Content = content;
        }

        if (!string.IsNullOrWhiteSpace(_token) && request.Headers.Authorization == null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _token);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            UnauthorizedResponseReceived?.Invoke(this, EventArgs.Empty);
        }

        return response;
    }
}
