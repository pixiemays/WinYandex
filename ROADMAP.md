# YMusic Windows Native Client — Feature Roadmap

| # | Feature | Status | Description |
|---|---|:---:|---|
| 1 | **AAC Audio & Quality Selection** | [x] Done | Support for AAC (192–256 kbps) and MP3 (320 kbps), quality badge pill, flyout selector |
| 2 | **Equalizer with Presets** | [x] Done | 10-band frequency equalizer, presets (Rock, Pop, Bass Boost, Vocal, Electronic, Classical, Jazz, Flat), persistence |
| 3 | **Gapless Playback & 80% Preloading** | [x] Done | Background stream resolution at 80% track progress to eliminate buffering delays between tracks |
| 4 | **«Моя волна» (My Wave)** | [x] Done | Endless personalized radio stream (`user:onyourwave`), mood/activity presets, interactive Up Next queue, isolated playback mode, like/dislike feedback & instant Liked Tracks sync |
| 5 | **Альбомы и плейлисты от яндекса** | [x] Done | Реализован раздел «Explore» («Главное») с витринами «Плейлист дня / Собрано для вас», популярными редакционными плейлистами, новыми альбомами/релизами, топ-чартом и полноэкранным просмотром плейлистов/альбомов с треклистами и воспроизведением |
| 6 | **Now Playing Drawer & Karaoke Lyrics** | [x] Done | HD cover view (800x800), ambient blurred backdrop, LRC timed lyrics with real-time synchronized karaoke highlight, smooth auto-scroll with manual scroll detection & sync button, clickable lyric seeking, fullscreen mode & hotkeys (L / Esc) |
| 7 | **Global Catalog Search** | [ ] Planned | Full Yandex Music catalog search with tabbed categories (Tracks, Albums, Artists, Playlists) and counters |
| 8 | **Audio Disk Cache (LRU)** | [ ] Planned | Local offline caching for played tracks in `%LocalAppData%/YMusic/Cache`, instant replay without network re-downloading |
| 9 | **Windows System Tray & Notifications** | [ ] Planned | Taskbar notification area tray icon, minimize-to-tray on close, background playback menu, Windows Toast notifications |
| 10 | **Discord Rich Presence** | [ ] Planned | Real-time Discord status showing current track title, artist, album art, and live elapsed/remaining progress |
| 11 | **Localization / Мультиязычность (RU / EN)** | [ ] Planned | Full Russian and English localization with runtime language switching, .resw resource files, and localized UI/player strings |
| 12 | **Интеграция FckCensor** | [ ] Planned | Короче надо засунуть аддон из pulsesync в прогу как опцию в настройках или тип того |
| 13 | **Profile** | [ ] Planned | ну както профиль сделать а то там полузаглушка щас |
| 14 | **Закрепление плейлистов и альбомов (Pinned Items)** | [x] Done | Закрепление избранных плейлистов и альбомов (Pin to top) с автосортировкой наверх, визуальным бейджем «Pinned» и кнопкой пина на карточках, в контекстном меню (ПКМ) и в шапке детального просмотра, с локальным сохранением в `pinned_items.json` |

---

### 💡 Мелкие доработки / UI Polish
- [x] **Индикация текущего играющего трека в списках:** Сделано ненавязчивое и стильное Fluent-выделение активного трека во всех списках («Liked Tracks», плейлисты, альбомы, очередь Моей волны, чарт) — мягкая акцентная подсветка строки, левый индикатор-пил, акцентный цвет названия трека и живая анимированная мини-иконка эквалайзера (MiniEqualizerControl) поверх обложки трека, реагирующая на play/pause.