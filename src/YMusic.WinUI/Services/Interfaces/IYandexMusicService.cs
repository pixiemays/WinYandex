#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IYandexMusicService
{
    string? CurrentToken { get; }
    void SetOAuthToken(string token);

    event EventHandler? UnauthorizedResponseReceived;

    Task<UserProfile?> GetAccountStatusAsync(CancellationToken cancellationToken = default);
    Task<(UserProfile? Profile, string? Error)> GetAccountStatusDetailedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetLikedTrackIdsAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> LikeTrackAsync(string trackId, CancellationToken cancellationToken = default);
    Task<bool> UnlikeTrackAsync(string trackId, CancellationToken cancellationToken = default);
    bool IsTrackLiked(string trackId);
    Task EnsureLikedTracksLoadedAsync(CancellationToken cancellationToken = default);
    event EventHandler<(string TrackId, bool IsLiked)>? TrackLikeChanged;
    event EventHandler? LikedTracksLoaded;

    Task<IReadOnlyList<Track>> GetTracksAsync(IEnumerable<string> trackIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Playlist>> GetUserPlaylistsAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetLikedAlbumsAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetAlbumsAsync(IEnumerable<long> albumIds, CancellationToken cancellationToken = default);
    Task<bool> LikeAlbumAsync(long albumId, CancellationToken cancellationToken = default);
    Task<bool> UnlikeAlbumAsync(long albumId, CancellationToken cancellationToken = default);
    bool IsAlbumLiked(long albumId);
    event EventHandler<(long AlbumId, bool IsLiked)>? AlbumLikeChanged;

    AudioQualityPreference PreferredQuality { get; set; }

    Task<string?> GetTrackDownloadUrlAsync(string trackId, CancellationToken cancellationToken = default);
    Task<(string? Url, StreamAudioQuality? Quality)> GetTrackStreamInfoAsync(string trackId, CancellationToken cancellationToken = default);

    Task<RotorStationResult?> GetRotorStationTracksAsync(string stationType = "user", string stationTag = "onyourwave", string? queueTrackId = null, CancellationToken cancellationToken = default);
    Task SendRotorFeedbackAsync(string eventType, string stationType, string stationTag, string trackId, string? batchId = null, double? totalPlayedSeconds = null, CancellationToken cancellationToken = default);

    Task<List<LyricLine>> GetTrackLyricsAsync(string trackId, string? title = null, string? artist = null, double durationSeconds = 0, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Playlist>> GetPersonalPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Playlist>> GetEditorialPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetNewReleasesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Track>> GetChartTracksAsync(CancellationToken cancellationToken = default);
    Task<Playlist?> GetPlaylistWithTracksAsync(long ownerUid, int kind, CancellationToken cancellationToken = default);
    Task<Album?> GetAlbumWithTracksAsync(long albumId, CancellationToken cancellationToken = default);
}

