# Technical Specification: Native Yandex Music Client (WinUI 3)

## 1. Project Overview
A lightweight, fully native Windows desktop client for Yandex Music built with **WinUI 3 (Windows App SDK)** and **.NET 8+**.
The goal is to replace the resource-heavy Electron/web wrapper with a native Fluent Design app that provides minimal memory footprint, hardware-accelerated UI, smooth playback, and deep Windows OS integration (SMTC).

---

## 2. Technology Stack & Core Dependencies

- **Platform / Framework:** .NET 8 / .NET 9 (`net8.0-windows10.0.19041.0` or higher)
- **UI Framework:** WinUI 3 (Windows App SDK)
- **Architecture Pattern:** MVVM (Model-View-ViewModel)
- **MVVM Library:** `CommunityToolkit.Mvvm` (Source generators for observable properties and commands)
- **Dependency Injection:** `Microsoft.Extensions.DependencyInjection`
- **Audio Playback:** `Windows.Media.Playback.MediaPlayer` + `Windows.Media.SystemMediaTransportControls` (SMTC)
- **Network & Serialization:** `HttpClient`, `System.Text.Json`, `System.Xml.Linq`
- **Secure Storage:** `Windows.Security.Credentials.PasswordVault` (for storing user OAuth tokens)

---

## 3. Architecture & Project Structure

The project must follow a clean layered MVVM architecture:

```
src/YMusic.WinUI/
├── Assets/
├── Models/
│   ├── Track.cs
│   ├── Album.cs
│   ├── Artist.cs
│   ├── Playlist.cs
│   └── UserProfile.cs
├── Services/
│   ├── Interfaces/
│   │   ├── IYandexMusicService.cs
│   │   ├── IAudioPlayerService.cs
│   │   └── ICredentialStorageService.cs
│   ├── Implementations/
│   │   ├── YandexMusicService.cs
│   │   ├── AudioPlayerService.cs
│   │   └── CredentialStorageService.cs
│   └── StreamSigner.cs
├── ViewModels/
│   ├── BaseViewModel.cs
│   ├── ShellViewModel.cs
│   ├── PlayerBarViewModel.cs
│   ├── LikedTracksViewModel.cs
│   ├── PlaylistsViewModel.cs
│   └── LoginViewModel.cs
├── Views/
│   ├── ShellWindow.xaml / .xaml.cs
│   ├── Controls/
│   │   └── PlayerBarControl.xaml / .xaml.cs
│   └── Pages/
│       ├── LikedTracksPage.xaml / .xaml.cs
│       ├── PlaylistsPage.xaml / .xaml.cs
│       └── LoginPage.xaml / .xaml.cs
├── App.xaml / .xaml.cs
└── Package.appxmanifest
```

---

## 4. Key Functional Requirements

### 4.1. Authentication & Security
- **OAuth Login:** Support authentication via Yandex OAuth token.
  - Option A: Embedded WebView2 modal dialog targeting Yandex OAuth endpoint to extract the access token from redirect URL.
  - Option B: Manual token input box for quick development/debugging.
- **Secure Token Persistence:** Store the token using `Windows.Security.Credentials.PasswordVault`. Never store plaintext tokens in configuration files or unencrypted storage.

### 4.2. Yandex Music API Communication
All requests must use:
- Base URL: `https://api.music.yandex.net/`
- Header: `Authorization: OAuth <token>`
- Header: `User-Agent: YandexMusicDesktop/WinUI`

#### Essential Endpoints to Implement:
1. `GET /users/{userId}/likes/tracks` — Retrieve IDs of liked tracks.
2. `POST /tracks` (with form-data or JSON body `track-ids: [id1, id2, ...]`) — Batch fetch track metadata.
3. `GET /users/{userId}/playlists/list` — Retrieve user's personal playlists.
4. `GET /tracks/{trackId}/download-info` — Retrieve download/streaming endpoints XML link.

#### Stream Link Generation Algorithm:
1. Call `GET /tracks/{trackId}/download-info`. Parse JSON, locate the object where `codec == "mp3"` (preferred bitrate `320` or `192`).
2. Make a GET request to the retrieved `downloadInfoUrl`. Parse returned XML:
   - Extract `<host>`, `<path>`, `<ts>`, `<s>`.
3. Generate MD5 signature:
   - Secret Salt: `"XDXFnebAaaWaytaSession"`
   - Target String: `SecretSalt + path.Substring(1) + s`
   - Hash: Lowercase Hex representation of `MD5(UTF8(TargetString))`.
4. Construct final direct URL:
   - `https://{host}/get-mp3/{hash}/{ts}{path}`

### 4.3. Audio Engine & Native Windows Integration
- Use `Windows.Media.Playback.MediaPlayer` for stream handling.
- **System Media Transport Controls (SMTC):**
  - Integrate with Windows SMTC to support physical/keyboard media keys (Play, Pause, Next, Previous).
  - Update `MediaDisplayUpdater` with:
    - Current track title
    - Artist name
    - Album art (via `RandomAccessStreamReference.CreateFromUri`)
    - Playback status (Playing / Paused)
- **Queue Management:**
  - Maintain an in-memory playlist queue.
  - Support Shuffle and Repeat (Off / Repeat All / Repeat One) modes.
  - Automatic progression to next track on stream completion.

### 4.4. UI / UX Design Requirements
- **Windows 11 Fluent Design:** Use Mica backdrop material (`MicaController` or `Window.SystemBackdrop = new MicaBackdrop()`).
- **Layout:**
  - Left: `NavigationView` (Home, Liked Tracks, Playlists, Settings).
  - Center: Dynamic Frame hosting Pages.
  - Bottom: Fixed docked `PlayerBarControl`.
- **PlayerBar Components:**
  - Album art thumbnail (rounded corners, async image loading).
  - Track Title (bold, text-trimming), Artist (secondary color).
  - Playback controls: Previous, Play/Pause toggle, Next, Shuffle toggle, Repeat toggle.
  - Seek bar slider showing elapsed time and total duration.
  - Volume slider with mute button.

---

## 5. Non-Functional & Engineering Requirements

1. **Memory & Performance:**
   - Idle memory usage must remain under 150 MB RAM.
   - UI virtualization must be enabled on all track lists (`ListView` with `ItemsStackPanel`).
2. **Error Handling & Resilience:**
   - Handle token expiration (HTTP 401) with navigation to login.
   - Handle network disconnections during audio streaming gracefully (retry policy / UI notification).
3. **Coding Standards:**
   - Fully typed C# with `#nullable enable`.
   - Async/Await best practices (`ConfigureAwait(false)` in service layers).
   - Use `CommunityToolkit.Mvvm` attributes (`[ObservableProperty]`, `[RelayCommand]`) to minimize boilerplate.
   - Zero emojis in source code, variable names, and commit recommendations.
   - Provide complete, compilable files without placeholders (`// TODO: implement later`).

---

## 6. Phased Implementation Roadmap

- [ ] **Phase 1: Foundation & API Client**
  - Set up WinUI 3 unpacked or packaged project.
  - Implement `YandexMusicService` (token auth, fetch likes, track metadata, audio URL generation).
- [ ] **Phase 2: Audio Engine & SMTC**
  - Implement `AudioPlayerService` wrapping `MediaPlayer`.
  - Wire up SMTC media events and track position timers.
- [ ] **Phase 3: Shell & MVVM Navigation**
  - Create `ShellWindow` with `NavigationView` and Mica backdrop.
  - Implement `PlayerBarControl` and bind it to `PlayerBarViewModel`.
- [ ] **Phase 4: Views & Polish**
  - Implement `LikedTracksPage` with virtualized track list.
  - Implement basic search and playlist view.
  - Profile and optimize memory and CPU usage.