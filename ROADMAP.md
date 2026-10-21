# YMusic Windows Native Client — Feature Roadmap

| # | Feature | Status | Description |
|---|---|:---:|---|
| 1 | **AAC Audio & Quality Selection** | [x] Done | Support for AAC (192–256 kbps) and MP3 (320 kbps), quality badge pill, flyout selector |
| 2 | **Equalizer with Presets** | [x] Done | 10-band frequency equalizer, presets (Rock, Pop, Bass Boost, Vocal, Electronic, Classical, Jazz, Flat), persistence |
| 3 | **Gapless Playback & 80% Preloading** | [x] Done | Background stream resolution at 80% track progress to eliminate buffering delays between tracks |
| 4 | **«Моя волна» (My Wave)** | [x] Done | Endless personalized radio stream (`user:onyourwave`), mood/activity presets, interactive Up Next queue, isolated playback mode, like/dislike feedback & instant Liked Tracks sync |
| 5 | **Альбомы и плейлисты от яндекса** | [ ] Up Next | Реализовать функционал альбомов и плейлистов от яндекса (типо плейлист дня и т.п.) |
| 6 | **Now Playing Drawer & Karaoke Lyrics** | [ ] Planned | HD cover view, LRC timed lyrics with real-time synchronized karaoke highlight and auto-scroll |
| 7 | **Global Catalog Search** | [ ] Planned | Full Yandex Music catalog search with tabbed categories (Tracks, Albums, Artists, Playlists) and counters |
| 8 | **Audio Disk Cache (LRU)** | [ ] Planned | Local offline caching for played tracks in `%LocalAppData%/YMusic/Cache`, instant replay without network re-downloading |
| 9 | **Windows System Tray & Notifications** | [ ] Planned | Taskbar notification area tray icon, minimize-to-tray on close, background playback menu, Windows Toast notifications |
| 10 | **Discord Rich Presence** | [ ] Planned | Real-time Discord status showing current track title, artist, album art, and live elapsed/remaining progress |
| 11 | **Localization / Мультиязычность (RU / EN)** | [ ] Planned | Full Russian and English localization with runtime language switching, .resw resource files, and localized UI/player strings |
| 12 | **Интеграция FckCensor** | [ ] Planned | Короче надо засунуть аддон из pulsesync в прогу как опцию в настройках или тип того |
| 13 | **Profile** | [ ] Planned | ну както профиль сделать а то там полузаглушка щас |

---

### 💡 Мелкие доработки / UI Polish
- [ ] **Индикация текущего играющего трека в списках:** Сделать аккуратное и ненавязчивое, но заметное выделение трека, который сейчас играет, в списках (в «Liked Tracks», плейлистах и альбомах) — например, мягкая подсветка названия акцентным цветом / анимированная или статичная мини-иконка эквалайзера/play поверх обложки.