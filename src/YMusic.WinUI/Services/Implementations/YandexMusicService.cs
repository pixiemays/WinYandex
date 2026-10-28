#nullable enable

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    private readonly HashSet<string> _dislikedTrackIds = new();
    private readonly HashSet<long> _likedAlbumIds = new();
    private readonly HashSet<long> _presavedAlbumIds = new();
    private readonly HashSet<string> _likedPlaylistKeys = new();
    private readonly HashSet<long> _likedArtistIds = new();
    private readonly ConcurrentDictionary<string, List<LyricLine>> _lyricsCache = new();
    private readonly ConcurrentDictionary<long, (Album Album, DateTime CachedAt)> _albumWithTracksCache = new();
    private readonly ConcurrentDictionary<string, (Playlist Playlist, DateTime CachedAt)> _playlistWithTracksCache = new();
    private readonly ConcurrentDictionary<long, Album> _knownUpcomingAlbums = new();
    private static readonly HttpClient _lrclibHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2.5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "WinYandex/1.0 (https://github.com/winyandex)" }
        }
    };
    private static readonly HttpClient _geniusHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(3.5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" }
        }
    };
    private string? _cachedUserId;
    private string? _token;

    public event EventHandler? UnauthorizedResponseReceived;
    public event EventHandler<(string TrackId, bool IsLiked)>? TrackLikeChanged;
    public event EventHandler<(string TrackId, bool IsDisliked)>? TrackDislikeChanged;
    public event EventHandler<(long AlbumId, bool IsLiked)>? AlbumLikeChanged;
    public event EventHandler<(long OwnerUid, int Kind, bool IsLiked)>? PlaylistLikeChanged;
    public event EventHandler<(long ArtistId, bool IsLiked)>? ArtistLikeChanged;
    public event EventHandler? LikedTracksLoaded;
    public event EventHandler? DislikedTracksLoaded;

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

        if (_credentialStorage != null)
        {
            string? storedToken = _credentialStorage.GetToken();
            if (!string.IsNullOrWhiteSpace(storedToken))
            {
                SetOAuthToken(storedToken);
            }
        }
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
            _albumWithTracksCache.Clear();
            _playlistWithTracksCache.Clear();
            _lyricsCache.Clear();
            lock (_likedTrackIds) { _likedTrackIds.Clear(); }
            lock (_likedAlbumIds) { _likedAlbumIds.Clear(); }
            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Clear(); }
            lock (_likedArtistIds) { _likedArtistIds.Clear(); }
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

            var profile = UserProfile.FromAccountStatus(apiResult.Result);

            string? token = _token;
            if (string.IsNullOrWhiteSpace(token) && _credentialStorage != null)
            {
                token = _credentialStorage.GetToken();
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    using var infoReq = new HttpRequestMessage(HttpMethod.Get, "https://login.yandex.ru/info?format=json");
                    infoReq.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
                    using var infoRes = await _httpClient.SendAsync(infoReq, cancellationToken).ConfigureAwait(false);
                    if (infoRes.IsSuccessStatusCode)
                    {
                        var infoJson = await infoRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var infoDoc = JsonDocument.Parse(infoJson);
                        if (infoDoc.RootElement.TryGetProperty("default_avatar_id", out var avatarElem))
                        {
                            string? avatarId = avatarElem.GetString();
                            if (!string.IsNullOrWhiteSpace(avatarId))
                            {
                                profile.AvatarUrl = $"https://avatars.yandex.net/get-yapic/{avatarId}/islands-200";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YandexMusicService] Failed to load custom avatar from login.yandex.ru: {ex.Message}");
                }
            }

            return (profile, null);
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

    public bool IsTrackDisliked(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        string cleanId = trackId.Split(':')[0];
        lock (_dislikedTrackIds)
        {
            return _dislikedTrackIds.Contains(trackId) || _dislikedTrackIds.Contains(cleanId);
        }
    }

    public async Task<IReadOnlyList<string>> GetDislikedTrackIdsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/dislikes/tracks", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return ids;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var resultElement))
            {
                return ids;
            }

            JsonElement tracksElement;
            if (resultElement.TryGetProperty("library", out var libElement) &&
                libElement.TryGetProperty("tracks", out tracksElement))
            {
            }
            else if (resultElement.TryGetProperty("tracks", out tracksElement))
            {
            }
            else
            {
                tracksElement = resultElement;
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

            lock (_dislikedTrackIds)
            {
                _dislikedTrackIds.Clear();
                foreach (var id in ids)
                {
                    _dislikedTrackIds.Add(id);
                    string cleanId = id.Split(':')[0];
                    if (!string.Equals(cleanId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        _dislikedTrackIds.Add(cleanId);
                    }
                }
            }
            DislikedTracksLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetDislikedTrackIds Error] {ex.Message}");
        }
        return ids;
    }

    public async Task EnsureDislikedTracksLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_dislikedTrackIds)
        {
            if (_dislikedTrackIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            await GetDislikedTrackIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> DislikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_dislikedTrackIds)
        {
            _dislikedTrackIds.Add(trackId);
            _dislikedTrackIds.Add(cleanId);
        }

        if (IsTrackLiked(trackId))
        {
            _ = UnlikeTrackAsync(trackId, cancellationToken);
        }

        TrackDislikeChanged?.Invoke(this, (trackId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/dislikes/tracks/add-multiple", content, cancellationToken).ConfigureAwait(false);

            return response != null && response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DislikeTrack Error] {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UndislikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_dislikedTrackIds)
        {
            _dislikedTrackIds.Remove(trackId);
            _dislikedTrackIds.Remove(cleanId);
        }
        TrackDislikeChanged?.Invoke(this, (trackId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/dislikes/tracks/remove", content, cancellationToken).ConfigureAwait(false);

            return response != null && response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UndislikeTrack Error] {ex.Message}");
            return false;
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
                        if (pl != null)
                        {
                            pl.IsLiked = true;
                            lock (_likedPlaylistKeys)
                            {
                                _likedPlaylistKeys.Add(pl.PlaylistKey);
                            }

                            var existing = allPlaylists.FirstOrDefault(e => e.Kind == pl.Kind && e.Uid == pl.Uid);
                            if (existing != null)
                            {
                                existing.IsLiked = true;
                            }
                            else
                            {
                                allPlaylists.Add(pl);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLikedPlaylists warning] {ex.Message}");
        }

        foreach (var playlist in allPlaylists)
        {
            playlist.IsLiked = IsPlaylistLiked(playlist.Uid, playlist.Kind);
        }

        return allPlaylists;
    }

    public async Task<bool> AddTrackToPlaylistAsync(
        long ownerUid,
        int kind,
        string trackId,
        string? albumId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var diffJson = albumId != null
                ? $"[{{\"op\":\"insert\",\"at\":0,\"tracks\":[{{\"id\":\"{trackId}\",\"albumId\":\"{albumId}\"}}]}}]"
                : $"[{{\"op\":\"insert\",\"at\":0,\"tracks\":[{{\"id\":\"{trackId}\"}}]}}]";

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("diff", diffJson)
            });

            using var response = await SendRequestAsync(
                HttpMethod.Post,
                $"users/{ownerUid}/playlists/{kind}/change-relative",
                content,
                cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                _playlistWithTracksCache.TryRemove($"{ownerUid}:{kind}", out _);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddTrackToPlaylist Error] {ex.Message}");
            return false;
        }
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

    public async Task<Album?> GetAlbumAsync(long albumId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"albums/{albumId}", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Album>>(json, _jsonOptions);
                if (apiResult?.Result != null && !apiResult.Result.IsNotFound && !string.IsNullOrWhiteSpace(apiResult.Result.Title))
                {
                    apiResult.Result.IsLiked = IsAlbumLiked(apiResult.Result.Id) || IsAlbumPresaved(apiResult.Result.Id);
                    return apiResult.Result;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetAlbum Warning] {ex.Message}");
        }

        var albums = await GetAlbumsAsync(new[] { albumId }, cancellationToken).ConfigureAwait(false);
        var valid = albums.FirstOrDefault(a => !a.IsNotFound && !string.IsNullOrWhiteSpace(a.Title));
        return valid;
    }

    public bool IsAlbumPresaved(long albumId)
    {
        lock (_presavedAlbumIds)
        {
            return _presavedAlbumIds.Contains(albumId);
        }
    }

    public IReadOnlyCollection<Album> GetKnownUpcomingAlbums() => _knownUpcomingAlbums.Values.ToList();

    public async Task<bool> AddAlbumPresaveAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_presavedAlbumIds)
        {
            _presavedAlbumIds.Add(albumId);
        }
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Add(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, true));

        bool success = false;
        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var formValues = new List<KeyValuePair<string, string>>
                {
                    new("album_id", albumId.ToString()),
                    new("likeAfterRelease", "true")
                };
                using var content = new FormUrlEncodedContent(formValues);
                using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/presaves/add", content, cancellationToken).ConfigureAwait(false);
                if (response != null && response.IsSuccessStatusCode)
                {
                    success = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddAlbumPresave Warning] {ex.Message}");
        }

        if (!success)
        {
            lock (_presavedAlbumIds) { _presavedAlbumIds.Remove(albumId); }
            lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, false));
            return false;
        }

        return true;
    }

    public async Task<bool> RemoveAlbumPresaveAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_presavedAlbumIds)
        {
            _presavedAlbumIds.Remove(albumId);
        }
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Remove(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, false));

        bool success = false;
        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var formValues = new List<KeyValuePair<string, string>>
                {
                    new("album_id", albumId.ToString())
                };
                using var content = new FormUrlEncodedContent(formValues);
                using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/presaves/remove", content, cancellationToken).ConfigureAwait(false);
                if (response != null && response.IsSuccessStatusCode)
                {
                    success = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoveAlbumPresave Warning] {ex.Message}");
        }

        if (!success)
        {
            lock (_presavedAlbumIds) { _presavedAlbumIds.Add(albumId); }
            lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, true));
            return false;
        }

        return true;
    }

    public async Task<PresavesResult?> GetPresavesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId)) return null;

            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/presaves?include_released=true&include_upcoming=true", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<PresavesResult>>(json, _jsonOptions);
                var res = apiResult?.Result;
                if (res != null)
                {
                    lock (_presavedAlbumIds)
                    {
                        _presavedAlbumIds.Clear();
                        if (res.UpcomingAlbums != null)
                        {
                            foreach (var alb in res.UpcomingAlbums)
                            {
                                alb.IsPreorder = true;
                                alb.IsLiked = true;
                                _presavedAlbumIds.Add(alb.Id);
                                _knownUpcomingAlbums[alb.Id] = alb;
                            }
                        }
                    }
                    return res;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetPresaves Warning] {ex.Message}");
        }
        return null;
    }

    public async Task<IReadOnlyList<Album>> GetLikedAlbumsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var albumIds = new List<long>();
        var presavedAlbums = new List<Album>();

        // 1. Load user's pre-saved upcoming albums
        try
        {
            var presaves = await GetPresavesAsync(cancellationToken).ConfigureAwait(false);
            if (presaves?.UpcomingAlbums != null)
            {
                foreach (var ua in presaves.UpcomingAlbums)
                {
                    ua.IsPreorder = true;
                    ua.IsLiked = true;
                    presavedAlbums.Add(ua);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLikedAlbums Presaves Warning] {ex.Message}");
        }

        // 2. Load standard released liked albums
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
            foreach (var pa in presavedAlbums)
            {
                _likedAlbumIds.Add(pa.Id);
            }
            foreach (var id in albumIds)
            {
                _likedAlbumIds.Add(id);
            }
        }

        var result = new List<Album>();

        // Pre-saved upcoming albums are shown at the very top of Liked Albums
        foreach (var pa in presavedAlbums)
        {
            result.Add(pa);
        }

        if (albumIds.Count > 0)
        {
            var fullAlbums = await GetAlbumsAsync(albumIds, cancellationToken).ConfigureAwait(false);
            var albumDict = fullAlbums.ToDictionary(a => a.Id);

            foreach (var id in albumIds)
            {
                if (albumDict.TryGetValue(id, out var album))
                {
                    if (result.All(x => x.Id != album.Id))
                    {
                        album.IsLiked = true;
                        result.Add(album);
                    }
                }
            }
        }

        return result;
    }

    public bool IsAlbumLiked(long albumId)
    {
        lock (_likedAlbumIds)
        {
            if (_likedAlbumIds.Contains(albumId)) return true;
        }
        lock (_presavedAlbumIds)
        {
            return _presavedAlbumIds.Contains(albumId);
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

    public async Task EnsureLikedAlbumsLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedAlbumIds)
        {
            if (_likedAlbumIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/albums", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    lock (_likedAlbumIds)
                    {
                        foreach (var item in resEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("album", out var albEl) && albEl.TryGetProperty("id", out var subId) && subId.TryGetInt64(out long sid))
                            {
                                _likedAlbumIds.Add(sid);
                            }
                            else if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long id))
                            {
                                _likedAlbumIds.Add(id);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureLikedAlbumsLoaded warning] {ex.Message}");
        }

        try
        {
            await GetPresavesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsurePresavesLoaded warning] {ex.Message}");
        }
    }

    public bool IsPlaylistLiked(long ownerUid, int kind)
    {
        lock (_likedPlaylistKeys)
        {
            return _likedPlaylistKeys.Contains($"{ownerUid}:{kind}");
        }
    }

    public async Task EnsureLikedPlaylistsLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedPlaylistKeys)
        {
            if (_likedPlaylistKeys.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            using var likedResponse = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/playlists", null, cancellationToken).ConfigureAwait(false);
            if (likedResponse != null && likedResponse.IsSuccessStatusCode)
            {
                var json = await likedResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    lock (_likedPlaylistKeys)
                    {
                        foreach (var item in resEl.EnumerateArray())
                        {
                            long pUid = 0;
                            int pKind = 0;

                            JsonElement target = item;
                            if (item.TryGetProperty("playlist", out var plEl))
                            {
                                target = plEl;
                            }

                            if (target.TryGetProperty("uid", out var uidEl) && uidEl.TryGetInt64(out long uid))
                            {
                                pUid = uid;
                            }
                            else if (target.TryGetProperty("owner", out var ownerEl) && ownerEl.TryGetProperty("uid", out var oUidEl) && oUidEl.TryGetInt64(out long oUid))
                            {
                                pUid = oUid;
                            }

                            if (target.TryGetProperty("kind", out var kindEl) && kindEl.TryGetInt32(out int kind))
                            {
                                pKind = kind;
                            }

                            if (pUid > 0 && pKind > 0)
                            {
                                _likedPlaylistKeys.Add($"{pUid}:{pKind}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureLikedPlaylistsLoaded warning] {ex.Message}");
        }
    }

    public async Task<bool> LikePlaylistAsync(long ownerUid, int kind, CancellationToken cancellationToken = default)
    {
        string key = $"{ownerUid}:{kind}";
        lock (_likedPlaylistKeys)
        {
            _likedPlaylistKeys.Add(key);
        }
        PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedPlaylistKeys) { _likedPlaylistKeys.Remove(key); }
                PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("playlist-id", key),
                new("playlist-ids", key)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/add-multiple", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackContent = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("playlist-id", key) });
            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/add", fallbackContent, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Remove(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));
            return false;
        }
        catch
        {
            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Remove(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));
            throw;
        }
    }

    public async Task<bool> UnlikePlaylistAsync(long ownerUid, int kind, CancellationToken cancellationToken = default)
    {
        string key = $"{ownerUid}:{kind}";
        lock (_likedPlaylistKeys)
        {
            _likedPlaylistKeys.Remove(key);
        }
        PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedPlaylistKeys) { _likedPlaylistKeys.Add(key); }
                PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("playlist-id", key),
                new("playlist-ids", key)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/{kind}/remove", null, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Add(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));
            return false;
        }
        catch
        {
            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Add(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));
            throw;
        }
    }

    public bool IsArtistLiked(long artistId)
    {
        lock (_likedArtistIds)
        {
            return _likedArtistIds.Contains(artistId);
        }
    }

    public async Task EnsureLikedArtistsLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedArtistIds)
        {
            if (_likedArtistIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/artists", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    lock (_likedArtistIds)
                    {
                        foreach (var item in resEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("artist", out var artEl) && artEl.TryGetProperty("id", out var subId) && subId.TryGetInt64(out long sid))
                            {
                                _likedArtistIds.Add(sid);
                            }
                            else if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long id))
                            {
                                _likedArtistIds.Add(id);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureLikedArtistsLoaded warning] {ex.Message}");
        }
    }

    public async Task<bool> LikeArtistAsync(long artistId, CancellationToken cancellationToken = default)
    {
        lock (_likedArtistIds)
        {
            _likedArtistIds.Add(artistId);
        }
        ArtistLikeChanged?.Invoke(this, (artistId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedArtistIds) { _likedArtistIds.Remove(artistId); }
                ArtistLikeChanged?.Invoke(this, (artistId, false));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("artist-id", artistId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/artists/add", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedArtistIds) { _likedArtistIds.Remove(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, false));
            return false;
        }
        catch
        {
            lock (_likedArtistIds) { _likedArtistIds.Remove(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, false));
            throw;
        }
    }

    public async Task<bool> UnlikeArtistAsync(long artistId, CancellationToken cancellationToken = default)
    {
        lock (_likedArtistIds)
        {
            _likedArtistIds.Remove(artistId);
        }
        ArtistLikeChanged?.Invoke(this, (artistId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedArtistIds) { _likedArtistIds.Add(artistId); }
                ArtistLikeChanged?.Invoke(this, (artistId, true));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("artist-id", artistId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/artists/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedArtistIds) { _likedArtistIds.Add(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, true));
            return false;
        }
        catch
        {
            lock (_likedArtistIds) { _likedArtistIds.Add(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, true));
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

    public async Task<List<LyricLine>> GetTrackLyricsAsync(
        string trackId,
        string? title = null,
        string? artist = null,
        double durationSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return new List<LyricLine>();
        }

        string cleanTrackId = trackId.Contains(':') ? trackId.Split(':')[0] : trackId;

        // 0. Cache hit: instant return (0 ms)
        if (_lyricsCache.TryGetValue(cleanTrackId, out var cached) && cached != null && cached.Count > 0)
        {
            return cached;
        }

        // FckCensor feature: prefer clean, uncensored lyrics from LRCLIB for censored tracks
        bool isReplaced = Track.IsTrackReplacedChecker?.Invoke(cleanTrackId) ?? false;
        if (isReplaced && !string.IsNullOrWhiteSpace(title))
        {
            try
            {
                var (fckSynced, fckPlain) = await FetchFromLrclibAsync(title, artist, durationSeconds, cancellationToken).ConfigureAwait(false);
                var fckLines = fckSynced ?? fckPlain;
                if (fckLines != null && fckLines.Count > 0)
                {
                    _lyricsCache[cleanTrackId] = fckLines;
                    return fckLines;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LRCLIB Priority Warning] {ex.Message}");
            }
        }

        // 1. Fetch Yandex Signed LRC and Yandex Supplement in parallel (very fast, ~100-150ms)
        var signedTask = FetchYandexSignedLyricsAsync(cleanTrackId, durationSeconds, cancellationToken);
        var supplementTask = FetchYandexSupplementLyricsAsync(cleanTrackId, durationSeconds, cancellationToken);

        await Task.WhenAll(signedTask, supplementTask).ConfigureAwait(false);

        var signedLyrics = await signedTask.ConfigureAwait(false);
        var supplementLyrics = await supplementTask.ConfigureAwait(false);

        // Check if Yandex already gave synchronized lyrics (with timestamps)
        if (signedLyrics != null && signedLyrics.Count > 0 && signedLyrics.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
        {
            _lyricsCache[cleanTrackId] = signedLyrics;
            return signedLyrics;
        }

        if (supplementLyrics != null && supplementLyrics.Count > 0 && supplementLyrics.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
        {
            _lyricsCache[cleanTrackId] = supplementLyrics;
            return supplementLyrics;
        }

        // Keep best plain lyrics from Yandex if available
        var fallbackPlainLyrics = (signedLyrics != null && signedLyrics.Count > 0)
            ? signedLyrics
            : supplementLyrics;

        // 2. Query LRCLIB for synchronized (or fallback plain) lyrics
        List<LyricLine>? lrclibPlainFallback = null;
        if (!string.IsNullOrWhiteSpace(title))
        {
            try
            {
                var (lrclibSynced, lrclibPlain) = await FetchFromLrclibAsync(title, artist, durationSeconds, cancellationToken).ConfigureAwait(false);
                if (lrclibSynced != null && lrclibSynced.Count > 0)
                {
                    _lyricsCache[cleanTrackId] = lrclibSynced;
                    return lrclibSynced;
                }
                lrclibPlainFallback = lrclibPlain;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LRCLIB Warning] {ex.Message}");
            }
        }

        // 3. Fallback to plain text lyrics: Yandex first, then LRCLIB plain
        if (fallbackPlainLyrics != null && fallbackPlainLyrics.Count > 0)
        {
            _lyricsCache[cleanTrackId] = fallbackPlainLyrics;
            return fallbackPlainLyrics;
        }

        if (lrclibPlainFallback != null && lrclibPlainFallback.Count > 0)
        {
            _lyricsCache[cleanTrackId] = lrclibPlainFallback;
            return lrclibPlainFallback;
        }

        // 4. Fallback to Genius if neither Yandex nor LRCLIB had lyrics
        if (!string.IsNullOrWhiteSpace(title))
        {
            try
            {
                var geniusLines = await FetchFromGeniusAsync(title, artist, durationSeconds, cancellationToken).ConfigureAwait(false);
                if (geniusLines != null && geniusLines.Count > 0)
                {
                    _lyricsCache[cleanTrackId] = geniusLines;
                    return geniusLines;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Genius Warning] {ex.Message}");
            }
        }

        return new List<LyricLine>();
    }

    private async Task<List<LyricLine>?> FetchYandexSignedLyricsAsync(
        string cleanTrackId,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string msg = $"{cleanTrackId}{ts}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("kzqU4XhfCaY6B6JTHODeq5"));
            string sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(msg)));
            string signedUrl = $"tracks/{Uri.EscapeDataString(cleanTrackId)}/lyrics?format=LRC&timeStamp={ts}&sign={Uri.EscapeDataString(sign)}";

            using var response = await SendRequestAsync(HttpMethod.Get, signedUrl, null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<TrackLyricsResult>>(json, _jsonOptions);
                if (apiResult?.Result != null)
                {
                    if (!string.IsNullOrWhiteSpace(apiResult.Result.DownloadUrl))
                    {
                        try
                        {
                            var lrcContent = await _httpClient.GetStringAsync(apiResult.Result.DownloadUrl, cancellationToken).ConfigureAwait(false);
                            var parsed = LyricLine.ParseLrc(lrcContent, durationSeconds);
                            if (parsed.Count > 0) return parsed;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(apiResult.Result.FullLyrics))
                    {
                        var parsed = LyricLine.ParseLrc(apiResult.Result.FullLyrics, durationSeconds);
                        if (parsed.Count > 0) return parsed;
                    }

                    if (!string.IsNullOrWhiteSpace(apiResult.Result.Lyrics))
                    {
                        var parsed = LyricLine.ParseLrc(apiResult.Result.Lyrics, durationSeconds);
                        if (parsed.Count > 0) return parsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Yandex Signed Lyrics Warning] {ex.Message}");
        }

        return null;
    }

    private async Task<List<LyricLine>?> FetchYandexSupplementLyricsAsync(
        string cleanTrackId,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"tracks/{Uri.EscapeDataString(cleanTrackId)}/supplement", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<TrackSupplementResult>>(json, _jsonOptions);
                var lyrics = apiResult?.Result?.Lyrics;
                if (lyrics != null)
                {
                    string? rawLyrics = null;
                    if (!string.IsNullOrWhiteSpace(lyrics.DownloadUrl))
                    {
                        try
                        {
                            rawLyrics = await _httpClient.GetStringAsync(lyrics.DownloadUrl, cancellationToken).ConfigureAwait(false);
                        }
                        catch { }
                    }

                    if (string.IsNullOrWhiteSpace(rawLyrics)) rawLyrics = lyrics.FullLyrics;
                    if (string.IsNullOrWhiteSpace(rawLyrics)) rawLyrics = lyrics.Lyrics;

                    if (!string.IsNullOrWhiteSpace(rawLyrics))
                    {
                        var parsed = LyricLine.ParseLrc(rawLyrics, durationSeconds);
                        if (parsed.Count > 0) return parsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Supplement API Warning] {ex.Message}");
        }

        return null;
    }

    private async Task<(List<LyricLine>? Synced, List<LyricLine>? Plain)> FetchFromLrclibAsync(
        string title,
        string? artist,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        string rawTitle = title.Trim();
        string rawArtist = (artist ?? string.Empty).Trim();

        string cleanTitle = CleanTitleForLyrics(rawTitle);
        string primaryArtist = GetPrimaryArtist(rawArtist);

        List<LyricLine>? fallbackPlain = null;

        // 1. Exact query: /api/get?track_name=...&artist_name=...&duration=...
        var (exactSynced, exactPlain) = await TryLrclibGetAsync(rawTitle, rawArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
        if (exactSynced != null) return (exactSynced, exactPlain);
        if (exactPlain != null && fallbackPlain == null) fallbackPlain = exactPlain;

        // If title or artist had extra info, try /api/get with cleaned title & primary artist
        if (!string.Equals(cleanTitle, rawTitle, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(primaryArtist, rawArtist, StringComparison.OrdinalIgnoreCase))
        {
            var (cleanSynced, cleanPlain) = await TryLrclibGetAsync(cleanTitle, primaryArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
            if (cleanSynced != null) return (cleanSynced, cleanPlain);
            if (cleanPlain != null && fallbackPlain == null) fallbackPlain = cleanPlain;
        }

        // 2. Search query: /api/search?track_name=...&artist_name=...
        // First search with clean title and primary artist
        var (searchSynced, searchPlain) = await TryLrclibSearchAsync(cleanTitle, primaryArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
        if (searchSynced != null) return (searchSynced, searchPlain);
        if (searchPlain != null && fallbackPlain == null) fallbackPlain = searchPlain;

        // If clean search failed, also try raw title search
        if (!string.Equals(cleanTitle, rawTitle, StringComparison.OrdinalIgnoreCase))
        {
            var (rawSearchSynced, rawSearchPlain) = await TryLrclibSearchAsync(rawTitle, primaryArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
            if (rawSearchSynced != null) return (rawSearchSynced, rawSearchPlain);
            if (rawSearchPlain != null && fallbackPlain == null) fallbackPlain = rawSearchPlain;
        }

        return (null, fallbackPlain);
    }

    private async Task<(List<LyricLine>? Synced, List<LyricLine>? Plain)> TryLrclibGetAsync(
        string trackName,
        string artistName,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackName)) return (null, null);

        try
        {
            var queryBuilder = new StringBuilder("https://lrclib.net/api/get?");
            queryBuilder.Append($"track_name={Uri.EscapeDataString(trackName)}");
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                queryBuilder.Append($"&artist_name={Uri.EscapeDataString(artistName)}");
            }
            if (durationSeconds > 0)
            {
                queryBuilder.Append($"&duration={(int)Math.Round(durationSeconds)}");
            }

            using var resp = await _lrclibHttpClient.GetAsync(queryBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var lrclibItem = JsonSerializer.Deserialize<LrclibResponse>(json, _jsonOptions);
                if (lrclibItem != null)
                {
                    // Strict duration check: if duration differs by > 4.5 seconds, reject (different cut/version)
                    if (durationSeconds > 0 && lrclibItem.Duration > 0 && Math.Abs(lrclibItem.Duration - durationSeconds) > 4.5)
                    {
                        return (null, null);
                    }

                    List<LyricLine>? synced = null;
                    if (!string.IsNullOrWhiteSpace(lrclibItem.SyncedLyrics))
                    {
                        var lines = LyricLine.ParseLrc(lrclibItem.SyncedLyrics, durationSeconds);
                        if (lines.Count > 0 && lines.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
                        {
                            synced = lines;
                        }
                    }

                    List<LyricLine>? plain = null;
                    if (!string.IsNullOrWhiteSpace(lrclibItem.PlainLyrics))
                    {
                        var lines = LyricLine.ParseLrc(lrclibItem.PlainLyrics, durationSeconds);
                        if (lines.Count > 0)
                        {
                            plain = lines;
                        }
                    }

                    return (synced, plain);
                }
            }
        }
        catch { }

        return (null, null);
    }

    private async Task<(List<LyricLine>? Synced, List<LyricLine>? Plain)> TryLrclibSearchAsync(
        string trackName,
        string artistName,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackName)) return (null, null);

        try
        {
            string searchUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(trackName)}&artist_name={Uri.EscapeDataString(artistName)}";
            using var resp = await _lrclibHttpClient.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var items = JsonSerializer.Deserialize<List<LrclibResponse>>(json, _jsonOptions);
                if (items != null && items.Count > 0)
                {
                    // 1. Filter candidates that actually have SyncedLyrics
                    List<LyricLine>? bestSynced = null;
                    var syncedCandidates = items.Where(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics)).ToList();
                    if (syncedCandidates.Count > 0)
                    {
                        var compatible = syncedCandidates
                            .Where(x => durationSeconds <= 0 || x.Duration <= 0 || Math.Abs(x.Duration - durationSeconds) <= 4.5)
                            .OrderBy(x => durationSeconds > 0 && x.Duration > 0 ? Math.Abs(x.Duration - durationSeconds) : 0.0)
                            .ThenBy(x =>
                            {
                                bool requestedLive = trackName.Contains("live", StringComparison.OrdinalIgnoreCase);
                                bool isLive = (x.TrackName?.Contains("live", StringComparison.OrdinalIgnoreCase) == true) ||
                                              (x.AlbumName?.Contains("live", StringComparison.OrdinalIgnoreCase) == true);
                                return (!requestedLive && isLive) ? 1 : 0;
                            })
                            .ThenBy(x =>
                            {
                                bool requestedAcoustic = trackName.Contains("acoustic", StringComparison.OrdinalIgnoreCase);
                                bool isAcoustic = (x.TrackName?.Contains("acoustic", StringComparison.OrdinalIgnoreCase) == true) ||
                                                  (x.AlbumName?.Contains("acoustic", StringComparison.OrdinalIgnoreCase) == true);
                                return (!requestedAcoustic && isAcoustic) ? 1 : 0;
                            })
                            .ToList();

                        foreach (var best in compatible)
                        {
                            if (string.IsNullOrWhiteSpace(best.SyncedLyrics)) continue;
                            var lines = LyricLine.ParseLrc(best.SyncedLyrics, durationSeconds);
                            if (lines.Count > 0 && lines.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
                            {
                                bestSynced = lines;
                                break;
                            }
                        }
                    }

                    // 2. Filter candidates that have PlainLyrics as fallback
                    List<LyricLine>? bestPlain = null;
                    var plainCandidates = items.Where(x => !string.IsNullOrWhiteSpace(x.PlainLyrics)).ToList();
                    if (plainCandidates.Count > 0)
                    {
                        var compatiblePlain = plainCandidates
                            .Where(x => durationSeconds <= 0 || x.Duration <= 0 || Math.Abs(x.Duration - durationSeconds) <= 6.0)
                            .OrderBy(x => durationSeconds > 0 && x.Duration > 0 ? Math.Abs(x.Duration - durationSeconds) : 0.0)
                            .ToList();

                        var chosen = compatiblePlain.Count > 0 ? compatiblePlain[0] : plainCandidates[0];
                        if (!string.IsNullOrWhiteSpace(chosen.PlainLyrics))
                        {
                            var lines = LyricLine.ParseLrc(chosen.PlainLyrics, durationSeconds);
                            if (lines.Count > 0)
                            {
                                bestPlain = lines;
                            }
                        }
                    }

                    return (bestSynced, bestPlain);
                }
            }
        }
        catch { }

        return (null, null);
    }

    private async Task<List<LyricLine>?> FetchFromGeniusAsync(
        string title,
        string? artist,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        string rawTitle = title.Trim();
        string rawArtist = (artist ?? string.Empty).Trim();
        string cleanTitle = CleanTitleForLyrics(rawTitle);
        string primaryArtist = GetPrimaryArtist(rawArtist);

        // Try primary artist + clean title
        var lines = await TryGeniusSearchAndExtractAsync(cleanTitle, primaryArtist, cancellationToken).ConfigureAwait(false);
        if (lines != null && lines.Count > 0) return lines;

        // Try primary artist + raw title if clean was different
        if (!string.Equals(cleanTitle, rawTitle, StringComparison.OrdinalIgnoreCase))
        {
            lines = await TryGeniusSearchAndExtractAsync(rawTitle, primaryArtist, cancellationToken).ConfigureAwait(false);
            if (lines != null && lines.Count > 0) return lines;
        }

        // Try full artist name + clean title if composite artist
        if (!string.IsNullOrWhiteSpace(primaryArtist) && !string.Equals(primaryArtist, rawArtist, StringComparison.OrdinalIgnoreCase))
        {
            lines = await TryGeniusSearchAndExtractAsync(cleanTitle, rawArtist, cancellationToken).ConfigureAwait(false);
            if (lines != null && lines.Count > 0) return lines;
        }

        // Try clean title only
        if (!string.IsNullOrWhiteSpace(primaryArtist))
        {
            lines = await TryGeniusSearchAndExtractAsync(cleanTitle, string.Empty, cancellationToken).ConfigureAwait(false);
            if (lines != null && lines.Count > 0) return lines;
        }

        return null;
    }

    private async Task<List<LyricLine>?> TryGeniusSearchAndExtractAsync(
        string trackName,
        string artistName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackName)) return null;

        try
        {
            string query = string.IsNullOrWhiteSpace(artistName) ? trackName : $"{artistName} {trackName}";
            string searchUrl = $"https://genius.com/api/search/multi?q={Uri.EscapeDataString(query)}";

            using var searchResp = await _geniusHttpClient.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            if (!searchResp.IsSuccessStatusCode) return null;

            var json = await searchResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("response", out var respProp) ||
                !respProp.TryGetProperty("sections", out var sectionsProp))
            {
                return null;
            }

            string? targetUrl = null;

            // Search priority: check "song" section, then "top_hit"
            foreach (var section in sectionsProp.EnumerateArray())
            {
                string secType = section.TryGetProperty("type", out var st) ? st.GetString() ?? "" : "";
                if (secType != "song" && secType != "top_hit") continue;

                if (!section.TryGetProperty("hits", out var hitsProp)) continue;

                foreach (var hit in hitsProp.EnumerateArray())
                {
                    if (!hit.TryGetProperty("result", out var resultProp)) continue;

                    string hitType = resultProp.TryGetProperty("_type", out var tp) ? tp.GetString() ?? "" : "";
                    if (hitType != "song") continue;

                    if (!resultProp.TryGetProperty("url", out var urlProp)) continue;
                    string? url = urlProp.GetString();
                    if (string.IsNullOrWhiteSpace(url) || !url.Contains("-lyrics")) continue;

                    string hitTitle = resultProp.TryGetProperty("title", out var tit) ? tit.GetString() ?? "" : "";
                    string hitArtist = resultProp.TryGetProperty("artist_names", out var art) ? art.GetString() ?? "" : "";

                    // Filter out Romanized or foreign translations if original is available
                    if (hitTitle.Contains("(Romanized)", StringComparison.OrdinalIgnoreCase) ||
                        hitArtist.Contains("Genius Romanizations", StringComparison.OrdinalIgnoreCase) ||
                        hitArtist.Contains("Genius Translations", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Check reasonable artist / title matching
                    bool artistMatches = string.IsNullOrWhiteSpace(artistName) ||
                        hitArtist.Contains(artistName, StringComparison.OrdinalIgnoreCase) ||
                        artistName.Contains(hitArtist, StringComparison.OrdinalIgnoreCase);

                    bool titleMatches = hitTitle.Contains(trackName, StringComparison.OrdinalIgnoreCase) ||
                        trackName.Contains(hitTitle, StringComparison.OrdinalIgnoreCase);

                    if (artistMatches || titleMatches)
                    {
                        targetUrl = url;
                        break;
                    }
                }

                if (targetUrl != null) break;
            }

            if (string.IsNullOrWhiteSpace(targetUrl)) return null;

            // Fetch the lyrics webpage from Genius
            using var pageResp = await _geniusHttpClient.GetAsync(targetUrl, cancellationToken).ConfigureAwait(false);
            if (!pageResp.IsSuccessStatusCode) return null;

            var html = await pageResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // Container regex: <div data-lyrics-container="true"...>(.*?)</div>
            var containerRegex = new Regex(@"<div[^>]*data-lyrics-container=[""']true[""'][^>]*>(.*?)</div>(?=(?:<div[^>]*data-lyrics-container|$))", RegexOptions.Singleline);
            var matches = containerRegex.Matches(html);
            if (matches.Count == 0) return null;

            var sb = new StringBuilder();
            foreach (Match match in matches)
            {
                string content = match.Groups[1].Value;
                content = Regex.Replace(content, @"<br\s*/?>", "\n");
                content = Regex.Replace(content, @"<.*?>", "");
                content = System.Net.WebUtility.HtmlDecode(content);
                sb.AppendLine(content);
            }

            string rawLyrics = sb.ToString().Trim();
            rawLyrics = CleanGeniusLyrics(rawLyrics);

            if (string.IsNullOrWhiteSpace(rawLyrics)) return null;

            var lines = LyricLine.ParseLrc(rawLyrics, 0);
            return lines.Count > 0 ? lines : null;
        }
        catch { }

        return null;
    }

    private static string CleanGeniusLyrics(string lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return string.Empty;

        // 1. Remove contributor header, e.g. "23 Contributors... Lyrics" or "4 Contributors... Текст песни"
        lyrics = Regex.Replace(lyrics, @"^\d+\s*Contributors.*?(?:Lyrics|Текст песни)", "", RegexOptions.IgnoreCase | RegexOptions.Singleline).Trim();

        // 2. Remove "Read More" preamble if bio text leaked into the container
        int readMoreIdx = lyrics.IndexOf("Read More", StringComparison.OrdinalIgnoreCase);
        if (readMoreIdx >= 0 && readMoreIdx < 400)
        {
            lyrics = lyrics.Substring(readMoreIdx + "Read More".Length).Trim();
        }

        // 3. Remove trailing Genius metadata tags like "123Embed" or "Embed"
        lyrics = Regex.Replace(lyrics, @"\d*\s*Embed$", "", RegexOptions.IgnoreCase).Trim();
        lyrics = Regex.Replace(lyrics, @"You might also like.*$", "", RegexOptions.IgnoreCase).Trim();

        return lyrics;
    }

    private static string CleanTitleForLyrics(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        // Strip parenthetical/bracketed suffixes like "(Remastered 2021)", "[Deluxe Edition]", "(feat. Drake)", etc.
        string cleaned = Regex.Replace(title, @"\s*[\(\[](?:remastered|remaster|deluxe|bonus|anniversary|feat\.?|featuring|version|mono|stereo)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);

        // Strip trailing " - Remastered..." etc.
        cleaned = Regex.Replace(cleaned, @"\s+-\s+(?:remastered|remaster|deluxe|mono|stereo|bonus track).*$", "", RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    private static string GetPrimaryArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        int commaIndex = artist.IndexOfAny(new[] { ',', '&', '/' });
        if (commaIndex > 0)
        {
            return artist.Substring(0, commaIndex).Trim();
        }
        return artist.Trim();
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
        try
        {
            // 1. Try full dedicated new-releases endpoint which returns array of IDs (up to 100)
            using var fullResponse = await SendRequestAsync(HttpMethod.Get, "landing3/new-releases", null, cancellationToken).ConfigureAwait(false);
            if (fullResponse != null && fullResponse.IsSuccessStatusCode)
            {
                var fullJson = await fullResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var fullDoc = JsonDocument.Parse(fullJson);
                if (fullDoc.RootElement.TryGetProperty("result", out var resEl) &&
                    resEl.TryGetProperty("newReleases", out var nrEl) &&
                    nrEl.ValueKind == JsonValueKind.Array)
                {
                    var idList = new List<long>();
                    foreach (var item in nrEl.EnumerateArray())
                    {
                        if (item.TryGetInt64(out long albumId))
                        {
                            idList.Add(albumId);
                        }
                    }

                    if (idList.Count > 0)
                    {
                        // Load top 40 new releases to display a rich, smooth horizontal scroll list
                        var loadedAlbums = await GetAlbumsAsync(idList.Take(40), cancellationToken).ConfigureAwait(false);
                        if (loadedAlbums.Count > 0)
                        {
                            return loadedAlbums;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetNewReleases Full Warning] {ex.Message}");
        }

        // 2. Fallback to basic landing3?blocks=new-releases preview block
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
        string cacheKey = $"{ownerUid}_{kind}";
        if (_playlistWithTracksCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 10)
        {
            return cached.Playlist;
        }

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

                _playlistWithTracksCache[cacheKey] = (playlist, DateTime.UtcNow);
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
        if (_albumWithTracksCache.TryGetValue(albumId, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 15)
        {
            return cached.Album;
        }

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"albums/{albumId}/with-tracks", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Album>>(json, _jsonOptions);
                if (apiResult?.Result != null && !apiResult.Result.IsNotFound && !string.IsNullOrWhiteSpace(apiResult.Result.Title))
                {
                    _albumWithTracksCache[albumId] = (apiResult.Result, DateTime.UtcNow);
                    return apiResult.Result;
                }
            }

            // Fallback: When with-tracks returns 404 or fails (unreleased / preorder albums), fetch album metadata directly
            var fallbackAlbum = await GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
            if (fallbackAlbum != null)
            {
                _albumWithTracksCache[albumId] = (fallbackAlbum, DateTime.UtcNow);
                return fallbackAlbum;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetAlbumWithTracks Warning] {ex.Message}");
            var fallbackAlbum = await GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
            if (fallbackAlbum != null)
            {
                _albumWithTracksCache[albumId] = (fallbackAlbum, DateTime.UtcNow);
                return fallbackAlbum;
            }
            return null;
        }
    }

    public async Task<ArtistBriefInfoResult?> GetArtistBriefInfoAsync(long artistId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"artists/{artistId}/brief-info", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<ArtistBriefInfoResult>>(json, _jsonOptions);
            var res = apiResult?.Result;
            if (res != null)
            {
                if (res.UpcomingAlbum != null)
                {
                    res.UpcomingAlbum.IsPreorder = true;
                    res.UpcomingAlbum.IsLiked = IsAlbumLiked(res.UpcomingAlbum.Id);
                    _knownUpcomingAlbums[res.UpcomingAlbum.Id] = res.UpcomingAlbum;
                }
                if (res.UpcomingAlbums != null)
                {
                    foreach (var ua in res.UpcomingAlbums)
                    {
                        ua.IsPreorder = true;
                        ua.IsLiked = IsAlbumLiked(ua.Id);
                        _knownUpcomingAlbums[ua.Id] = ua;
                    }
                }
            }
            return res;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetArtistBriefInfo Warning] {ex.Message}");
            return null;
        }
    }

    public async Task<List<Album>> GetArtistDirectAlbumsAsync(long artistId, int page = 0, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var allAlbums = new List<Album>();
            int currentPage = page;
            int total = int.MaxValue;

            while (allAlbums.Count < total && currentPage < 10)
            {
                using var response = await SendRequestAsync(HttpMethod.Get, $"artists/{artistId}/direct-albums?page={currentPage}&page-size={pageSize}", null, cancellationToken).ConfigureAwait(false);
                if (response == null || !response.IsSuccessStatusCode)
                {
                    break;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<ArtistAlbumsResponse>>(json, _jsonOptions);
                var result = apiResult?.Result;
                if (result?.Albums == null || result.Albums.Count == 0)
                {
                    break;
                }

                foreach (var album in result.Albums)
                {
                    album.IsLiked = IsAlbumLiked(album.Id);
                    allAlbums.Add(album);
                }

                if (result.Pager != null)
                {
                    total = result.Pager.Total;
                }
                else
                {
                    break;
                }

                if (result.Albums.Count < pageSize)
                {
                    break;
                }

                currentPage++;
            }

            return allAlbums;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetArtistDirectAlbums Warning] {ex.Message}");
            return new List<Album>();
        }
    }

    public async Task<List<Album>> GetArtistAlsoAlbumsAsync(long artistId, int page = 0, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"artists/{artistId}/also-albums?page={page}&page-size={pageSize}", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return new List<Album>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<ArtistAlbumsResponse>>(json, _jsonOptions);
            var albums = apiResult?.Result?.Albums ?? new List<Album>();
            foreach (var album in albums)
            {
                album.IsLiked = IsAlbumLiked(album.Id);
            }
            return albums;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetArtistAlsoAlbums Warning] {ex.Message}");
            return new List<Album>();
        }
    }

    public async Task<SearchResult?> SearchAsync(
        string query,
        string type = "all",
        int page = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        try
        {
            string endpoint = $"search?text={Uri.EscapeDataString(query.Trim())}&type={type}&page={page}&nocorrect=false";
            using var response = await SendRequestAsync(HttpMethod.Get, endpoint, null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<SearchResult>>(json, _jsonOptions);
            var result = apiResult?.Result;

            if (result != null)
            {
                if (result.Best != null && result.Best.ResultElement.ValueKind == JsonValueKind.Object)
                {
                    var rawText = result.Best.ResultElement.GetRawText();
                    switch (result.Best.Type)
                    {
                        case "artist":
                            result.Best.Artist = JsonSerializer.Deserialize<Artist>(rawText, _jsonOptions);
                            break;
                        case "track":
                            result.Best.Track = JsonSerializer.Deserialize<Track>(rawText, _jsonOptions);
                            break;
                        case "album":
                            result.Best.Album = JsonSerializer.Deserialize<Album>(rawText, _jsonOptions);
                            break;
                        case "playlist":
                            result.Best.Playlist = JsonSerializer.Deserialize<Playlist>(rawText, _jsonOptions);
                            break;
                    }
                }

                if (result.Tracks?.Results != null)
                {
                    foreach (var track in result.Tracks.Results)
                    {
                        track.IsLiked = IsTrackLiked(track.Id);
                    }
                }

                if (result.Best?.Track != null)
                {
                    result.Best.Track.IsLiked = IsTrackLiked(result.Best.Track.Id);
                }

                if (result.Albums?.Results != null)
                {
                    foreach (var album in result.Albums.Results)
                    {
                        album.IsLiked = IsAlbumLiked(album.Id);
                    }
                }

                if (result.Best?.Album != null)
                {
                    result.Best.Album.IsLiked = IsAlbumLiked(result.Best.Album.Id);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API Search Warning] {ex.Message}");
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
