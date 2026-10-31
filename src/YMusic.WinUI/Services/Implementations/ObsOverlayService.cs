#nullable enable

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class ObsOverlayService : IObsOverlayService
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ISettingsService _settingsService;
    private readonly object _syncLock = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<HttpListenerResponse> _sseClients = new();
    private readonly object _sseLock = new();

    private bool _isInitialized;
    private bool _disposed;
    private string? _lastTrackId;
    private bool _lastIsPlaying;

    public bool IsRunning => _listener != null && _listener.IsListening;
    public int Port => _settingsService.ObsOverlayPort > 0 ? _settingsService.ObsOverlayPort : 21337;
    public string WidgetUrl => $"http://localhost:{Port}/widget";

    public ObsOverlayService(IAudioPlayerService audioPlayer, ISettingsService settingsService)
    {
        _audioPlayer = audioPlayer;
        _settingsService = settingsService;
    }

    public void Initialize()
    {
        lock (_syncLock)
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }

        try
        {
            Log("Initializing ObsOverlayService...");
            _audioPlayer.CurrentTrackChanged += OnPlaybackChanged;
            _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
            _audioPlayer.PositionChanged += OnPositionChanged;
            _settingsService.SettingChanged += OnSettingChanged;

            if (_settingsService.ObsOverlayEnabled)
            {
                Start();
            }
        }
        catch (Exception ex)
        {
            Log($"Initialization failed: {ex.Message}");
        }
    }

    public void Start()
    {
        lock (_syncLock)
        {
            if (IsRunning) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();

                Log($"OBS Overlay server started on http://localhost:{Port}/");
                _ = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
            }
            catch (Exception ex)
            {
                Log($"Failed to start HTTP listener on port {Port}: {ex.Message}");
                _listener = null;
            }
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (!IsRunning) return;

            try
            {
                Log("Stopping OBS Overlay server...");
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                lock (_sseLock)
                {
                    foreach (var client in _sseClients)
                    {
                        try { client.Close(); } catch { }
                    }
                    _sseClients.Clear();
                }

                _listener?.Stop();
                _listener?.Close();
                _listener = null;
            }
            catch (Exception ex)
            {
                Log($"Error stopping listener: {ex.Message}");
            }
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private void OnSettingChanged(object? sender, string settingName)
    {
        if (settingName == nameof(ISettingsService.ObsOverlayEnabled))
        {
            if (_settingsService.ObsOverlayEnabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
        else if (settingName == nameof(ISettingsService.ObsOverlayPort))
        {
            if (_settingsService.ObsOverlayEnabled)
            {
                Restart();
            }
        }
    }

    private void OnPlaybackChanged(object? sender, Track? track)
    {
        string? currentId = track?.Id;
        if (currentId != _lastTrackId)
        {
            _lastTrackId = currentId;
            BroadcastState();
        }
    }

    private void OnIsPlayingChanged(object? sender, bool isPlaying)
    {
        if (isPlaying != _lastIsPlaying)
        {
            _lastIsPlaying = isPlaying;
            BroadcastState();
        }
    }

    private DateTime _lastBroadcastTime = DateTime.MinValue;
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        // Throttle position updates to SSE to twice per second to reduce traffic
        if ((DateTime.UtcNow - _lastBroadcastTime).TotalMilliseconds >= 500)
        {
            _lastBroadcastTime = DateTime.UtcNow;
            BroadcastState();
        }
    }

    private string BuildCurrentStateJson()
    {
        var track = _audioPlayer.CurrentTrack;
        bool isPlaying = _audioPlayer.IsPlaying;
        var position = _audioPlayer.Position;
        var duration = _audioPlayer.Duration;

        var state = new
        {
            hasTrack = track != null,
            trackId = track?.Id ?? string.Empty,
            title = track?.FullTitle ?? string.Empty,
            rawTitle = track?.Title ?? string.Empty,
            artists = track?.ArtistNames ?? string.Empty,
            album = track?.AlbumTitle ?? string.Empty,
            coverUrl = track?.GetCoverUrl(400) ?? track?.GetCoverUrl(200) ?? string.Empty,
            isPlaying = isPlaying,
            positionMs = (long)position.TotalMilliseconds,
            durationMs = track?.DurationMs > 0 ? track.DurationMs : (long)duration.TotalMilliseconds,
            formattedPosition = position.ToString(@"m\:ss"),
            formattedDuration = duration.ToString(@"m\:ss"),
            isExplicit = track?.IsDimmed == false && track.Title.Contains("explicit", StringComparison.OrdinalIgnoreCase),
            isFckCensor = track?.IsReplaced == true
        };

        return JsonSerializer.Serialize(state);
    }

    private void BroadcastState()
    {
        string json = BuildCurrentStateJson();
        string sseData = $"data: {json}\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(sseData);

        lock (_sseLock)
        {
            for (int i = _sseClients.Count - 1; i >= 0; i--)
            {
                var response = _sseClients[i];
                try
                {
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    response.OutputStream.Flush();
                }
                catch
                {
                    try { response.Close(); } catch { }
                    _sseClients.RemoveAt(i);
                }
            }
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (HttpListenerException) when (!listener.IsListening || ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Listener error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers for all responses
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        string path = request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(path)) path = "/";

        try
        {
            switch (path)
            {
                case "":
                case "/":
                    // Interactive preview & settings page
                    await ServeHtmlAsync(response, GetDashboardHtml());
                    break;

                case "/widget":
                    // Standalone transparent widget for OBS Studio
                    await ServeHtmlAsync(response, GetWidgetHtml());
                    break;

                case "/api/track":
                    // JSON state
                    string json = BuildCurrentStateJson();
                    await ServeJsonAsync(response, json);
                    break;

                case "/events":
                    // Realtime Server-Sent Events (SSE)
                    HandleSseConnection(response);
                    break;

                case "/track.txt":
                    // Plain text "Artist - Title" for classic OBS Text sources
                    var t = _audioPlayer.CurrentTrack;
                    string plainText = t != null ? $"{t.ArtistNames} - {t.FullTitle}" : "";
                    await ServeTextAsync(response, plainText);
                    break;

                case "/cover":
                    // Direct redirect to current cover image
                    string? coverUrl = _audioPlayer.CurrentTrack?.GetCoverUrl(400);
                    if (!string.IsNullOrWhiteSpace(coverUrl))
                    {
                        response.Redirect(coverUrl);
                        response.Close();
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Close();
                    }
                    break;

                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Request handle error ({path}): {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    private void HandleSseConnection(HttpListenerResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        // Send initial state immediately
        string initialData = $"data: {BuildCurrentStateJson()}\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(initialData);
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Flush();

        lock (_sseLock)
        {
            _sseClients.Add(response);
        }
    }

    private static async Task ServeHtmlAsync(HttpListenerResponse response, string html)
    {
        response.ContentType = "text/html; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    private static async Task ServeJsonAsync(HttpListenerResponse response, string json)
    {
        response.ContentType = "application/json; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    private static async Task ServeTextAsync(HttpListenerResponse response, string text)
    {
        response.ContentType = "text/plain; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Log("Disposing ObsOverlayService...");
                _audioPlayer.CurrentTrackChanged -= OnPlaybackChanged;
                _audioPlayer.IsPlayingChanged -= OnIsPlayingChanged;
                _audioPlayer.PositionChanged -= OnPositionChanged;
                _settingsService.SettingChanged -= OnSettingChanged;
                Stop();
            }
            catch (Exception ex)
            {
                Log($"Dispose error: {ex.Message}");
            }
        }
    }

    private static void Log(string message)
    {
        string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ObsOverlay] {message}";
        Debug.WriteLine(formatted);

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "YMusic");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "obs_overlay.log"), formatted + Environment.NewLine);
        }
        catch { }
    }

    #region Embedded HTML/CSS/JS Widget & Dashboard

    private string GetWidgetHtml()
    {
        return """
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>WinYandex OBS Stream Widget</title>
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Montserrat:wght@400;500;600;700;800&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
    <style>
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
            user-select: none;
        }

        body {
            background-color: transparent;
            overflow: hidden;
            font-family: 'Montserrat', 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            align-items: flex-start;
            justify-content: flex-start;
            width: 100vw;
            height: 100vh;
            padding: 16px;
        }

        /* Root Container with smooth transitions */
        #widget-root {
            position: relative;
            transition: opacity 0.45s cubic-bezier(0.16, 1, 0.3, 1), transform 0.45s cubic-bezier(0.16, 1, 0.3, 1);
        }

        #widget-root.hidden-pause {
            opacity: 0 !important;
            transform: translateY(20px) scale(0.95) !important;
            pointer-events: none;
        }

        /* ================= THEME 1: GLASSMORPHISM CARD ================= */
        .theme-glass {
            display: flex;
            align-items: center;
            width: 500px;
            height: 140px;
            padding: 16px;
            border-radius: 20px;
            background: rgba(18, 18, 24, 0.78);
            backdrop-filter: blur(28px) saturate(180%);
            -webkit-backdrop-filter: blur(28px) saturate(180%);
            border: 1px solid rgba(255, 255, 255, 0.12);
            box-shadow: 0 16px 45px rgba(0, 0, 0, 0.55), 0 0 40px var(--ambient-glow, rgba(255, 204, 0, 0.15));
            color: #ffffff;
            position: relative;
            overflow: hidden;
        }

        .theme-glass .ambient-bg {
            position: absolute;
            top: -40px;
            left: -40px;
            width: 220px;
            height: 220px;
            border-radius: 50%;
            background: var(--ambient-glow, rgba(255, 204, 0, 0.35));
            filter: blur(65px);
            opacity: 0.45;
            pointer-events: none;
            transition: background 0.8s ease;
        }

        .theme-glass .cover-container {
            position: relative;
            width: 108px;
            height: 108px;
            min-width: 108px;
            border-radius: 14px;
            overflow: hidden;
            box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
            background: #24242a;
        }

        .theme-glass .cover-img {
            width: 100%;
            height: 100%;
            object-fit: cover;
            display: block;
            transition: transform 0.6s cubic-bezier(0.16, 1, 0.3, 1);
        }

        .theme-glass .info-container {
            margin-left: 18px;
            flex: 1;
            min-width: 0;
            display: flex;
            flex-direction: column;
            justify-content: center;
            height: 100%;
            position: relative;
            z-index: 2;
        }

        .theme-glass .top-meta-row {
            display: flex;
            align-items: center;
            gap: 8px;
            margin-bottom: 4px;
        }

        .theme-glass .title-container {
            overflow: hidden;
            white-space: nowrap;
            position: relative;
            margin-bottom: 3px;
        }

        .theme-glass .title {
            font-size: 19px;
            font-weight: 700;
            color: #ffffff;
            display: inline-block;
            letter-spacing: -0.2px;
            text-shadow: 0 2px 8px rgba(0, 0, 0, 0.5);
        }

        .theme-glass .artist {
            font-size: 14px;
            font-weight: 500;
            color: rgba(255, 255, 255, 0.72);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            margin-bottom: 12px;
        }

        .theme-glass .progress-wrapper {
            display: flex;
            align-items: center;
            gap: 10px;
            width: 100%;
        }

        .theme-glass .progress-track {
            flex: 1;
            height: 5px;
            background: rgba(255, 255, 255, 0.14);
            border-radius: 999px;
            overflow: hidden;
            position: relative;
        }

        .theme-glass .progress-fill {
            height: 100%;
            background: linear-gradient(90deg, #ffcc00, #ff5500);
            border-radius: 999px;
            width: 0%;
            box-shadow: 0 0 10px rgba(255, 120, 0, 0.6);
            transition: width 0.3s linear;
        }

        .theme-glass .time-text {
            font-size: 11px;
            font-weight: 600;
            color: rgba(255, 255, 255, 0.6);
            font-variant-numeric: tabular-nums;
            min-width: 32px;
        }

        /* ================= THEME 2: COMPACT PILL ================= */
        .theme-pill {
            display: flex;
            align-items: center;
            height: 64px;
            width: 440px;
            padding: 8px 18px 8px 8px;
            border-radius: 9999px;
            background: rgba(16, 16, 22, 0.86);
            backdrop-filter: blur(24px) saturate(180%);
            -webkit-backdrop-filter: blur(24px) saturate(180%);
            border: 1px solid rgba(255, 255, 255, 0.12);
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5), 0 0 24px var(--ambient-glow, rgba(255, 204, 0, 0.2));
            color: #ffffff;
            position: relative;
            overflow: hidden;
        }

        .theme-pill .cover-container {
            width: 48px;
            height: 48px;
            min-width: 48px;
            border-radius: 50%;
            overflow: hidden;
            box-shadow: 0 4px 14px rgba(0, 0, 0, 0.4);
            position: relative;
        }

        .theme-pill .cover-img {
            width: 100%;
            height: 100%;
            object-fit: cover;
            border-radius: 50%;
        }

        .theme-pill.playing .cover-img {
            animation: spin 12s linear infinite;
        }

        .theme-pill .info-container {
            margin-left: 14px;
            flex: 1;
            min-width: 0;
            display: flex;
            flex-direction: column;
            justify-content: center;
        }

        .theme-pill .title {
            font-size: 15px;
            font-weight: 700;
            color: #ffffff;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .theme-pill .artist {
            font-size: 12px;
            font-weight: 500;
            color: rgba(255, 255, 255, 0.65);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            margin-top: 1px;
        }

        .theme-pill .progress-bar-edge {
            position: absolute;
            bottom: 0;
            left: 0;
            height: 3px;
            background: linear-gradient(90deg, #ffcc00, #ff3b30);
            width: 0%;
            transition: width 0.3s linear;
        }

        /* ================= THEME 3: VINYL TURNTABLE ================= */
        .theme-vinyl {
            display: flex;
            align-items: center;
            width: 530px;
            height: 160px;
            position: relative;
            color: #ffffff;
        }

        .theme-vinyl .jacket-wrap {
            position: relative;
            width: 130px;
            height: 130px;
            z-index: 3;
        }

        .theme-vinyl .jacket-cover {
            width: 100%;
            height: 100%;
            border-radius: 10px;
            object-fit: cover;
            box-shadow: 0 10px 28px rgba(0, 0, 0, 0.6);
            border: 1px solid rgba(255, 255, 255, 0.1);
            position: relative;
            z-index: 3;
            background: #1c1c24;
        }

        .theme-vinyl .vinyl-disk {
            position: absolute;
            top: 5px;
            left: 55px;
            width: 120px;
            height: 120px;
            border-radius: 50%;
            background: radial-gradient(circle, #242424 0%, #111111 40%, #000000 70%, #151515 100%);
            box-shadow: 0 8px 24px rgba(0, 0, 0, 0.65);
            border: 2px solid #222;
            z-index: 2;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: transform 0.6s ease;
        }

        .theme-vinyl .vinyl-grooves {
            position: absolute;
            width: 90%;
            height: 90%;
            border-radius: 50%;
            border: 1px dashed rgba(255, 255, 255, 0.08);
            box-shadow: inset 0 0 10px rgba(255, 255, 255, 0.05);
        }

        .theme-vinyl .vinyl-center {
            width: 44px;
            height: 44px;
            border-radius: 50%;
            overflow: hidden;
            border: 2px solid #333;
            position: relative;
            z-index: 3;
        }

        .theme-vinyl .vinyl-center img {
            width: 100%;
            height: 100%;
            object-fit: cover;
        }

        .theme-vinyl.playing .vinyl-disk {
            animation: spin 8s linear infinite;
        }

        .theme-vinyl .info-card {
            margin-left: 65px;
            flex: 1;
            padding: 16px 20px;
            border-radius: 18px;
            background: rgba(18, 18, 24, 0.82);
            backdrop-filter: blur(24px);
            border: 1px solid rgba(255, 255, 255, 0.1);
            box-shadow: 0 12px 35px rgba(0, 0, 0, 0.5);
            display: flex;
            flex-direction: column;
            justify-content: center;
            z-index: 1;
            position: relative;
        }

        /* ================= SHARED ELEMENTS & BADGES ================= */
        .badge {
            font-size: 9px;
            font-weight: 800;
            padding: 2px 6px;
            border-radius: 4px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            display: inline-flex;
            align-items: center;
            line-height: 1;
        }

        .badge-fck {
            background: linear-gradient(135deg, #ff2d55, #ff375f);
            color: #ffffff;
            box-shadow: 0 2px 8px rgba(255, 45, 85, 0.4);
        }

        .badge-explicit {
            background: rgba(255, 255, 255, 0.18);
            color: #ddd;
        }

        /* Animated Audio Wave Equalizer */
        .equalizer {
            display: inline-flex;
            align-items: flex-end;
            gap: 2.5px;
            height: 12px;
            margin-right: 2px;
        }

        .eq-bar {
            width: 2.5px;
            background: #ffcc00;
            border-radius: 2px;
            height: 3px;
            transition: height 0.2s ease;
        }

        .playing .eq-bar:nth-child(1) { animation: bounce 0.9s ease-in-out infinite alternate; }
        .playing .eq-bar:nth-child(2) { animation: bounce 1.1s ease-in-out infinite 0.2s alternate; }
        .playing .eq-bar:nth-child(3) { animation: bounce 0.8s ease-in-out infinite 0.4s alternate; }
        .playing .eq-bar:nth-child(4) { animation: bounce 1.2s ease-in-out infinite 0.1s alternate; }

        @keyframes bounce {
            0% { height: 2px; }
            100% { height: 12px; }
        }

        @keyframes spin {
            from { transform: rotate(0deg); }
            to { transform: rotate(360deg); }
        }

        /* ================= SMOOTH TRACK TRANSITION ANIMATIONS ================= */
        .anim-exit {
            opacity: 0 !important;
            transform: translateY(-16px) scale(0.97) !important;
            transition: opacity 0.28s ease, transform 0.28s cubic-bezier(0.4, 0, 1, 1) !important;
        }

        .anim-enter {
            opacity: 0;
            transform: translateY(16px) scale(0.97);
            animation: slideInTrack 0.42s cubic-bezier(0.16, 1, 0.3, 1) forwards;
        }

        @keyframes slideInTrack {
            0% {
                opacity: 0;
                transform: translateY(16px) scale(0.97);
            }
            100% {
                opacity: 1;
                transform: translateY(0) scale(1);
            }
        }

        /* Marquee horizontal text scrolling when title is long */
        .marquee-active {
            display: inline-block;
            animation: marqueeScroll 9s linear infinite;
        }

        @keyframes marqueeScroll {
            0% { transform: translateX(0); }
            20% { transform: translateX(0); }
            80% { transform: translateX(calc(-100% + 280px)); }
            100% { transform: translateX(0); }
        }
    </style>
</head>
<body>

    <div id="widget-root" class="theme-glass">
        <!-- Ambient Color Glow -->
        <div class="ambient-bg" id="ambient-glow"></div>

        <!-- Cover Artwork -->
        <div class="cover-container" id="cover-wrap">
            <img id="cover-art" class="cover-img" src="" alt="Album Art">
        </div>

        <!-- Vinyl Disc (Only rendered in vinyl theme) -->
        <div id="vinyl-disk" class="vinyl-disk" style="display: none;">
            <div class="vinyl-grooves"></div>
            <div class="vinyl-center">
                <img id="vinyl-cover" src="" alt="Label">
            </div>
        </div>

        <!-- Details Info -->
        <div class="info-container" id="info-box">
            <div class="top-meta-row" id="top-meta">
                <div class="equalizer">
                    <div class="eq-bar"></div>
                    <div class="eq-bar"></div>
                    <div class="eq-bar"></div>
                    <div class="eq-bar"></div>
                </div>
                <span id="badge-fck" class="badge badge-fck" style="display: none;">FCK CENSOR</span>
                <span id="badge-explicit" class="badge badge-explicit" style="display: none;">E</span>
            </div>

            <div class="title-container" id="title-box">
                <span id="track-title" class="title">WinYandex</span>
            </div>

            <div id="track-artist" class="artist">Ожидание трека...</div>

            <div class="progress-wrapper" id="progress-box">
                <span id="time-pos" class="time-text">0:00</span>
                <div class="progress-track">
                    <div id="progress-fill" class="progress-fill"></div>
                </div>
                <span id="time-dur" class="time-text">0:00</span>
            </div>
        </div>

        <!-- Edge progress bar for pill theme -->
        <div id="progress-edge" class="progress-bar-edge" style="display: none;"></div>
    </div>

    <script>
        // Parse URL params for theme customization: ?theme=glass|pill|vinyl, ?hideOnPause=true, ?scale=1.2
        const urlParams = new URLSearchParams(window.location.search);
        const selectedTheme = urlParams.get('theme') || 'glass';
        const hideOnPause = urlParams.get('hideOnPause') === 'true' || urlParams.get('hideOnPause') === '1';
        const scale = parseFloat(urlParams.get('scale') || '1.0');

        const root = document.getElementById('widget-root');
        const coverArt = document.getElementById('cover-art');
        const vinylCover = document.getElementById('vinyl-cover');
        const vinylDisk = document.getElementById('vinyl-disk');
        const titleEl = document.getElementById('track-title');
        const artistEl = document.getElementById('track-artist');
        const badgeFck = document.getElementById('badge-fck');
        const badgeExplicit = document.getElementById('badge-explicit');
        const progressFill = document.getElementById('progress-fill');
        const progressEdge = document.getElementById('progress-edge');
        const timePos = document.getElementById('time-pos');
        const timeDur = document.getElementById('time-dur');
        const ambientGlow = document.getElementById('ambient-glow');

        // Apply scale
        if (scale && scale !== 1.0) {
            root.style.transformOrigin = 'top left';
            root.style.transform = `scale(${scale})`;
        }

        // Apply theme classes
        root.className = '';
        if (selectedTheme === 'pill') {
            root.classList.add('theme-pill');
            if (progressEdge) progressEdge.style.display = 'block';
            const progressBox = document.getElementById('progress-box');
            if (progressBox) progressBox.style.display = 'none';
        } else if (selectedTheme === 'vinyl') {
            root.classList.add('theme-vinyl');
            if (vinylDisk) vinylDisk.style.display = 'flex';
        } else {
            root.classList.add('theme-glass');
        }

        let currentTrackId = null;
        let isTransitioning = false;
        let clientPositionMs = 0;
        let clientDurationMs = 0;
        let isPlaying = false;
        let lastServerUpdate = Date.now();

        // 60 FPS smooth progress interpolator
        setInterval(() => {
            if (isPlaying && clientDurationMs > 0) {
                const elapsed = Date.now() - lastServerUpdate;
                const estimatedPos = Math.min(clientPositionMs + elapsed, clientDurationMs);
                const percent = Math.min((estimatedPos / clientDurationMs) * 100, 100);

                if (progressFill) progressFill.style.width = percent + '%';
                if (progressEdge) progressEdge.style.width = percent + '%';
                if (timePos) timePos.textContent = formatTime(estimatedPos);
            }
        }, 100);

        function formatTime(ms) {
            const totalSec = Math.floor(ms / 1000);
            const m = Math.floor(totalSec / 60);
            const s = totalSec % 60;
            return `${m}:${s < 10 ? '0' : ''}${s}`;
        }

        function updateState(data) {
            if (!data) return;

            isPlaying = !!data.isPlaying;
            clientPositionMs = data.positionMs || 0;
            clientDurationMs = data.durationMs || 0;
            lastServerUpdate = Date.now();

            if (isPlaying) {
                root.classList.add('playing');
                root.classList.remove('hidden-pause');
            } else {
                root.classList.remove('playing');
                if (hideOnPause) {
                    root.classList.add('hidden-pause');
                }
            }

            // Check if track changed (trigger smooth broadcast-grade animation)
            if (data.trackId && data.trackId !== currentTrackId) {
                triggerTrackTransition(data);
            } else {
                // Same track: just update progress and duration
                if (timeDur) timeDur.textContent = data.formattedDuration || formatTime(data.durationMs);
            }
        }

        function triggerTrackTransition(data) {
            if (isTransitioning) return;
            isTransitioning = true;
            currentTrackId = data.trackId;

            // Step 1: Smooth Exit Animation
            root.classList.add('anim-exit');

            // Step 2: Preload new album cover image in background to avoid any visual flicker
            const tempImg = new Image();
            tempImg.crossOrigin = 'anonymous';
            tempImg.onload = tempImg.onerror = () => {
                setTimeout(() => {
                    // Update content while hidden
                    applyTrackData(data);

                    // Step 3: Enter Animation
                    root.classList.remove('anim-exit');
                    root.classList.add('anim-enter');

                    setTimeout(() => {
                        root.classList.remove('anim-enter');
                        isTransitioning = false;
                    }, 450);
                }, 220);
            };
            tempImg.src = data.coverUrl || '';
        }

        function applyTrackData(data) {
            titleEl.textContent = data.title || 'Без названия';
            artistEl.textContent = data.artists || 'Неизвестный исполнитель';

            if (data.coverUrl) {
                coverArt.src = data.coverUrl;
                if (vinylCover) vinylCover.src = data.coverUrl;
            } else {
                coverArt.src = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" fill="%23333"><rect width="100" height="100"/></svg>';
            }

            if (badgeFck) badgeFck.style.display = data.isFckCensor ? 'inline-flex' : 'none';
            if (badgeExplicit) badgeExplicit.style.display = data.isExplicit ? 'inline-flex' : 'none';

            if (timeDur) timeDur.textContent = data.formattedDuration || formatTime(data.durationMs);

            // Auto-detect marquee if title overflows
            const titleBox = document.getElementById('title-box');
            if (titleBox && titleEl.scrollWidth > titleBox.clientWidth + 10) {
                titleEl.classList.add('marquee-active');
            } else {
                titleEl.classList.remove('marquee-active');
            }
        }

        // Realtime EventSource (SSE) connection
        function connectSse() {
            const evtSource = new EventSource('/events');

            evtSource.onmessage = (e) => {
                try {
                    const data = JSON.parse(e.data);
                    updateState(data);
                } catch (err) { }
            };

            evtSource.onerror = () => {
                evtSource.close();
                // Reconnect with backoff
                setTimeout(connectSse, 2000);
            };
        }

        // Initial fetch + fallback poll
        function fetchTrack() {
            fetch('/api/track')
                .then(res => res.json())
                .then(data => updateState(data))
                .catch(() => {});
        }

        fetchTrack();
        connectSse();
        setInterval(fetchTrack, 2500);
    </script>
</body>
</html>
""";
    }

    private string GetDashboardHtml()
    {
        return $$"""
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>WinYandex OBS Studio Overlay Center</title>
    <link href="https://fonts.googleapis.com/css2?family=Montserrat:wght@400;600;700;800&family=Inter:wght@400;500;600&display=swap" rel="stylesheet">
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            background: #0d0d12;
            color: #f0f0f5;
            font-family: 'Inter', -apple-system, sans-serif;
            padding: 36px 24px;
            display: flex;
            flex-direction: column;
            align-items: center;
            min-height: 100vh;
        }
        .header {
            text-align: center;
            margin-bottom: 32px;
        }
        .header h1 {
            font-family: 'Montserrat', sans-serif;
            font-size: 32px;
            font-weight: 800;
            background: linear-gradient(135deg, #ffcc00, #ff453a);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            margin-bottom: 8px;
        }
        .header p {
            color: #8e8e99;
            font-size: 15px;
        }
        .preview-card {
            background: rgba(22, 22, 30, 0.7);
            border: 1px solid rgba(255, 255, 255, 0.1);
            border-radius: 20px;
            padding: 24px;
            width: 100%;
            max-width: 820px;
            box-shadow: 0 20px 50px rgba(0, 0, 0, 0.6);
            margin-bottom: 30px;
        }
        .preview-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 16px;
        }
        .preview-header h2 {
            font-size: 18px;
            font-weight: 700;
        }
        .theme-buttons {
            display: flex;
            gap: 8px;
        }
        .theme-btn {
            background: #1c1c24;
            color: #ccc;
            border: 1px solid #333;
            padding: 8px 14px;
            border-radius: 10px;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
        }
        .theme-btn:hover { background: #282834; color: #fff; }
        .theme-btn.active {
            background: #ffcc00;
            color: #000;
            border-color: #ffcc00;
        }
        .iframe-container {
            width: 100%;
            height: 220px;
            background: repeating-conic-gradient(#14141c 0% 25%, #181822 0% 50%) 50% / 20px 20px;
            border-radius: 14px;
            overflow: hidden;
            display: flex;
            align-items: center;
            justify-content: center;
            border: 1px solid rgba(255, 255, 255, 0.08);
        }
        iframe {
            width: 100%;
            height: 100%;
            border: none;
        }
        .cards-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 16px;
            width: 100%;
            max-width: 820px;
        }
        .theme-info-card {
            background: #15151c;
            border: 1px solid rgba(255, 255, 255, 0.08);
            border-radius: 16px;
            padding: 20px;
            display: flex;
            flex-direction: column;
            justify-content: space-between;
        }
        .theme-info-card h3 {
            font-size: 16px;
            font-weight: 700;
            margin-bottom: 6px;
            color: #ffcc00;
        }
        .theme-info-card p {
            font-size: 13px;
            color: #888;
            margin-bottom: 14px;
            line-height: 1.4;
        }
        .url-box {
            background: #09090d;
            padding: 8px 12px;
            border-radius: 8px;
            font-family: monospace;
            font-size: 12px;
            color: #bbb;
            word-break: break-all;
            margin-bottom: 12px;
            border: 1px solid #222;
        }
        .copy-btn {
            background: #ffcc00;
            color: #000;
            border: none;
            padding: 10px;
            border-radius: 10px;
            font-weight: 700;
            font-size: 13px;
            cursor: pointer;
            transition: opacity 0.2s;
        }
        .copy-btn:hover { opacity: 0.88; }
        .instructions {
            margin-top: 30px;
            max-width: 820px;
            width: 100%;
            background: rgba(255, 255, 255, 0.03);
            border-radius: 16px;
            padding: 20px;
            font-size: 13px;
            line-height: 1.6;
            color: #aaa;
        }
        .instructions ol {
            margin-left: 20px;
            margin-top: 10px;
        }
    </style>
</head>
<body>

    <div class="header">
        <h1>WinYandex OBS Studio Overlay</h1>
        <p>Интерактивный виджет текущего играющего трека для стримеров (OBS Studio Browser Source)</p>
    </div>

    <div class="preview-card">
        <div class="preview-header">
            <h2>Живой предпросмотр</h2>
            <div class="theme-buttons">
                <button class="theme-btn active" onclick="setTheme('glass')">Glassmorphism Card</button>
                <button class="theme-btn" onclick="setTheme('pill')">Compact Pill</button>
                <button class="theme-btn" onclick="setTheme('vinyl')">Vinyl Record</button>
            </div>
        </div>

        <div class="iframe-container">
            <iframe id="preview-frame" src="/widget?theme=glass"></iframe>
        </div>
    </div>

    <div class="cards-grid">
        <div class="theme-info-card">
            <div>
                <h3>1. Glassmorphism Card</h3>
                <p>Полупрозрачная стеклянная карточка с размытием, цветной подсветкой в цвет обложки, бейджами и прогрессом. Размер в OBS: 520x160.</p>
                <div class="url-box" id="url-glass">http://localhost:{{Port}}/widget?theme=glass</div>
            </div>
            <button class="copy-btn" onclick="copyUrl('url-glass')">Копировать ссылку для OBS</button>
        </div>

        <div class="theme-info-card">
            <div>
                <h3>2. Compact Pill</h3>
                <p>Ультракомпактная капсула с вращающимся диском обложки и тонкой полоской прогресса. Идеально под веб-камеру. Размер в OBS: 460x80.</p>
                <div class="url-box" id="url-pill">http://localhost:{{Port}}/widget?theme=pill</div>
            </div>
            <button class="copy-btn" onclick="copyUrl('url-pill')">Копировать ссылку для OBS</button>
        </div>

        <div class="theme-info-card">
            <div>
                <h3>3. Vinyl Record</h3>
                <p>Ретро-виниловая пластинка, выезжающая из конверта альбома и плавно вращающаяся при игре. Размер в OBS: 560x180.</p>
                <div class="url-box" id="url-vinyl">http://localhost:{{Port}}/widget?theme=vinyl</div>
            </div>
            <button class="copy-btn" onclick="copyUrl('url-vinyl')">Копировать ссылку для OBS</button>
        </div>
    </div>

    <div class="instructions">
        <h4 style="color: #fff; margin-bottom: 8px;">Как добавить в OBS Studio:</h4>
        <ol>
            <li>В OBS нажмите кнопку <b>«+»</b> в панели «Источники» (Sources).</li>
            <li>Выберите <b>«Браузер»</b> (Browser Source).</li>
            <li>Вставьте скопированный URL (например, <code>http://localhost:{{Port}}/widget?theme=glass</code>).</li>
            <li>Укажите ширину и высоту (рекомендуется: <b>Ширина 540, Высота 180</b>).</li>
            <li>Фон автоматически прозрачный. Наслаждайтесь стримом!</li>
        </ol>
    </div>

    <script>
        function setTheme(theme) {
            document.querySelectorAll('.theme-btn').forEach(b => b.classList.remove('active'));
            event.target.classList.add('active');
            document.getElementById('preview-frame').src = '/widget?theme=' + theme;
        }

        function copyUrl(elementId) {
            const text = document.getElementById(elementId).textContent;
            navigator.clipboard.writeText(text).then(() => {
                alert('Ссылка скопирована в буфер обмена!\n\n' + text);
            });
        }
    </script>
</body>
</html>
""";
    }

    #endregion
}

