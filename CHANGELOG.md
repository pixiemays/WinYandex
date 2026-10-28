# Changelog

All notable changes to the WinYandex project are documented in this file.

---

## [0.9.5] — 2026-10-28

### Pre-Save Albums (Upcoming Releases)
* Added full support for upcoming and unreleased albums across the entire application.
* **Pre-save album page**: dedicated view featuring cover art, artist credits, release date countdown, and one-click pre-save action.
* **Liked Albums integration**: user pre-saved albums appear directly at the top of "Liked Albums" with high priority placement, clear `PRE-SAVE` status badge, and instant state toggle.
* **Search integration**: upcoming and pre-saved albums can now be found directly via Search, including in the Albums section and as the Best Result.
* **Fluent Design styling**: translucent acrylic violet styling with subtle borders and badges (`#2A8B5CF6` / `#C4B5FD`) designed to blend seamlessly into the UI.

### Instant Live Search in Navigation Bar
* **Search on first keystroke**: typing even a single character in the sidebar search box immediately triggers a search without requiring Enter.
* **Seamless navigation & persistent focus**: automatically routes to the Search page without losing keyboard focus or interrupting typing.
* **Bidirectional synchronization**: search queries stay perfectly synced in real time between the sidebar and the Search page input.
* **Fast response**: reduced search debounce to 240ms with immediate cancellation of stale queries for snappy results.

### Playback & Visual Transitions
* Replaced abrupt strobe track switching in Now Playing drawer with a smooth crossfade and subtle zoom transition.
* Added IDE F5 debugging configuration in `.vscode` (`tasks.json` and `launch.json`).

---

## [0.9.4] — 2026-10-24

### Track Context Menu
* Added a comprehensive track context menu accessible via right-click and "···" button across all views (Playlists, Liked Tracks, Search, Artist Page, Explore, Player Bar, and Now Playing drawer).
* **"My Wave by track"**: launch a personalized My Wave stream tuned to any selected track. If the track is currently playing, playback continues smoothly without interruption while the upcoming queue seamlessly updates to the wave stream.
* **Queue management**: added "Play next" (insert immediately after the current track) and "Add to end of queue".
* **"Don't like" (Dislike)**: added full dislike support synchronized with Yandex Music cloud. Disliked tracks are visually dimmed (semi-transparent) across all lists, and disliking the currently playing track skips it automatically.
* **"Add to playlist"**: dynamic submenu listing all user playlists with instant track addition via API.
* **"Share"**: instant copying of web track links to clipboard.
* **"Download to file"**: direct download of audio tracks to the user's `Downloads` folder with clean tagging (`Artist - Title.m4a` / `mp3`).
* **"Track details"**: modal dialog displaying comprehensive metadata (title, artists, album, year, duration, bitrate, codec, and track ID).
* **FckCensor integration**: quick options to replace censored tracks with local files, remove existing replacements, or report censorship directly from any list.

### UI & Notifications
* Added a global floating toast notification pill at the bottom of the window to confirm user actions (track added to playlist, link copied, file downloaded, added to queue, etc.).
* Visual dimming for disliked tracks across all track lists.
* Complete localization (English and Russian) for all new context menu items, toasts, and dialogs.

---

## [0.9.3] — 2026-10-24

### UI & Update System
* Improved visual styling of the Update Available card in settings: eliminated low-contrast white text on accent background, redesigned as a clean Fluent Card with accent border and dynamic system theme brushes.
* Added smooth scrolling (`ScrollViewer`) for the entire changelog list directly within the update card.
* Fixed update navigation from the main player notification banner: clicking the update button now smoothly scrolls down to the update section in settings.
* Seamless changelog integration: GitHub releases and the in-app update service now publish formatted release notes directly from `CHANGELOG.md` instead of a generic compare link.
* Added automatic cleanup of downloaded update installer files after installation and on app startup to reclaim disk space.

---

## [0.9.2] — 2026-10-24

### System Tray & App Lifecycle (Single Instance)
* Fixed re-opening the application when "hide to tray on close" is enabled: clicking desktop shortcuts or pinned taskbar icons now restores and activates the existing tray instance instead of launching a duplicate process.
* Implemented cross-process activation redirection (Windows AppLifecycle Single Instance) with proper window foreground handoff (`AllowSetForegroundWindow` / `SwitchToThisWindow`).

### Audio Cache & Storage
* Added ability to select custom directories and any drive (e.g. D:, E:) for audio cache storage, removing the restriction to drive C:.
* Added custom directory picker button with native Windows Explorer dialog.
* Added safe automated migration of previously cached tracks to the new location (with user confirmation).
* Added quick reset button to restore the default cache path on the system drive.
* Dynamic cache size calculation now accurately accounts for storage across all connected drives.

---

## [0.9.1] — 2026-10-24

### UI & Animations
* Added smooth entry and exit animations for full-screen Now Playing mode (slide up, fade, and subtle album art scale).
* Eliminated layout shifts and content jumping in the main panel when toggling Now Playing mode.
* Added smooth opening and closing animations for albums and playlists.
* Added crossfade transition when switching between albums.
* Fixed track table flickering during data loading.
* Replaced playback icon near "My Vibe" header with a neutral audio wave icon.
* Increased navigation sidebar width to 250 px to prevent text clipping.
* Added ability to like and unlike (add/remove from favorites) albums and playlists in headers and context menus.
* Added artist picker context menu when clicking tracks with multiple artists (in player bar, Now Playing, and track lists).
* Hid back button when navigating to pinned albums or playlists directly from the sidebar.
* Prevented re-triggering animation and reloading when clicking already opened albums or playlists.
* Fixed back navigation from albums: now returns directly to the originating page (including artist page) without intermediate playlist library display.
* Fixed quality badges and text clipping in Now Playing mode.
* Fixed clipping of Mute, equalizer, and lyrics buttons in the player bar.

### Audio Controls
* Increased volume slider width and added smooth mouse wheel adjustment.
* Added manual volume percentage input by clicking the numeric value.
* Refined speaker icon state for low volume levels.

### Performance
* Added in-memory caching for albums and playlists, eliminating redundant network calls.

### FckCensor v2
* Support for `list_v2.json` database.
* Restored blocked tracks in albums (AlbumSpoof).
* Re-uploaded track injection into artist pages (ArtistInsertion).
* Priority for synchronized LRCLIB lyrics on replaced tracks.
* Automatic inheritance of album art for recovered tracks.
* Dynamic audio stream duration detection and seek bar unlocking for blocked tracks.
* Added attribution links to original FckCensor repository in settings and context menus.

### Authentication & Session
* Added external system browser authentication with clipboard token capture.
* Added SSL security indicator badge to embedded login window and improved text wrapping.
* Clean playback stop, player collapse, and pinned items concealment on account logout.

---

## [0.9.0] — 2026-10-23

### Release
* Native Yandex Music desktop client built with WinUI 3 and .NET 8.
* "My Vibe" personalized stream support with dynamic moods and filters.
* Playback for tracks, playlists, albums, and artist discographies.
* Built-in 10-band equalizer with audio presets.
* Real-time synchronized lyrics display.
* Local disk LRU cache with gapless track preloading.
* Integrated FckCensor module for censored track replacement.
* Full integration with Windows SMTC, system tray, and Discord Rich Presence.
* Light and dark themes with Mica material effects.
* Global hotkeys and media key control.
