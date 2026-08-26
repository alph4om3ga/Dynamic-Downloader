# Release Notes

## v1.3 (August 25, 2026)

### Reliability
- Hardened local qBittorrent hand-offs by confirming a stopped state before moving downloaded media.
- Added exclusive-access checks and retry handling for Windows file locks.
- Preserved downloaded media when removing the local torrent entry.
- Kept the automation queue moving when an individual item fails.

### Nyaa Session Management
- Added 28-day Nyaa session-cookie tracking with persisted refresh timestamps.
- Added visible session states for missing, legacy/untracked, fresh, expiring, and expired cookies.
- Added a one-day expiration warning that is shown only once per cookie refresh period.
- Added automatic timer refresh when a new cookie is entered.
- Added a confirmed **Reset 28-day timer** control for freshly renewed Nyaa session information.

### RSS Release Detection
- Fixed release grabbing for uploader-specific Nyaa feeds such as Varyg1001.
- Treats an RSS feed's explicit `u=` uploader filter as authoritative, so valid releases are not rejected by an unrelated saved source-group value.
- Preserved title-based source-group filtering for broad RSS feeds without an uploader filter.

### Regression Coverage
- Added regression checks for Nyaa session expiration states and one-time warning behavior.
- Added regression coverage for persisted cookie-session timestamps and warning state.
- Added Windows regression coverage for queue cleanup and release processing behavior.

## v1.2.1 (July 29, 2026)

### Bug Fixes
- Fixed episode-number detection order so unambiguous patterns such as `S01E06` are checked before generic numbers in a title.
- Prevented show titles containing numbers, such as `Level 999`, from being mistaken for the episode number.

### Maintenance
- Updated the application and in-app version display to `v1.2.1`.

## v1.2.0 (July 28, 2026)

### Automated Encoding Pipeline
- Added end-to-end automation from RSS detection through download, encoding, muxing, screenshot capture, torrent creation, FTP upload, seedbox seeding, and Nyaa posting.
- Added Nyaa RSS monitoring with configurable source-group filtering and episode detection.
- Added qBittorrent download monitoring with progress reporting.
- Added PowerShell/VapourSynth x265 encoding support and a five-minute FFmpeg quick-encode test mode.
- Added automatic audio and subtitle track analysis, language tagging, and Judas track naming conventions.
- Added smart muxing with English subtitle prioritization and dynamic audio/subtitle tags.
- Added screenshot capture and ImgBB upload support.
- Added torrent creation and FTP upload support for the completed release package.
- Added Nyaa posting with generated descriptions and hidden/public posting modes.

### Show and Queue Management
- Added weekly show management for adding, editing, and removing shows.
- Added per-show RSS feeds, source groups, release schedules, expected episode counts, custom episode patterns, episode offsets, and uncensored-release settings.
- Added episode history and version-aware handling for repacks such as `v2` and `v3`.
- Added queue controls for monitoring, pausing, resuming, retrying, cancelling, and processing releases.

### Testing and Notifications
- Added Simulate, Quick Encode, and Full Encode pipeline modes.
- Added hidden/public release controls for safely testing Nyaa posts.
- Added Discord notifications for episode discovery, download completion, encoding, and torrent posting.
- Added machine-name identification in notifications for multi-encoder setups.

### User Interface and Configuration
- Added the WPF application interface with eight light and dark color schemes.
- Added embedded local and seedbox qBittorrent WebUI views.
- Added a real-time, color-coded activity log.
- Added JSON-based application settings and per-show configuration.
- Added standalone Windows build scripts and bundled application assets.

## v1.0.0 (January 29, 2025)

🎉 **Initial Release**

The first stable release of JudasEncodingManager - a complete automation solution for the Judas anime encoding pipeline.

### Features

#### Core Pipeline
- **RSS Feed Monitoring** - Automatically detects new episodes from configured Nyaa RSS feeds
- **qBittorrent Integration** - Downloads via local qBittorrent with progress tracking
- **PowerShell Encoding** - Executes existing encoding scripts with x265/Opus
- **Track Analysis** - Detects and tags audio/subtitle languages automatically
- **Smart Muxing** - Remuxes output with proper Judas track naming conventions
- **Screenshot Capture** - Takes 3 screenshots and uploads to ImgBB
- **FTP Upload** - Reliable uploads to seedbox using FluentFTP with progress tracking
- **Nyaa Posting** - Automated posting with dynamic descriptions and proper tagging

#### Test Run System
Four-mode test system for safe pipeline validation:
- **Simulate** - UI-only testing with fake delays
- **Quick Encode** - 5-minute ffmpeg encode for fast pipeline testing
- **Full Encode** - Production-quality PowerShell encoding
- **Hidden/Public Toggle** - Post as hidden for testing or public for release

#### Version Support
- Correctly parses version suffixes (v2, v3) from filenames
- Generates proper output names: `[Judas] Show - S01E03v2.mkv`
- Torrent display names include version when applicable

#### Discord Notifications
Notifications at key pipeline stages with machine name identification:
- 📥 Episode Grabbed
- ✅ Download Complete  
- 🎬 Encoding Started (Quick/Full)
- ✅ Encoding Complete (with duration and file size)
- 🎉 Torrent Posted (with URL and visibility)

#### Track Handling
- **Audio tracks** - Named by language only (Japanese, English)
- **Subtitle tracks** - Ordered by priority (English first) with proper naming
- **Video track** - Named `[Judas] x265 10b`

#### Automatic Cleanup
- Deletes source files after successful encode
- Removes LWI index files from encoding folder
- Cleans up intermediate muxing files

### User Interface
- 8 color schemes (4 dark, 4 light variants)
- Embedded qBittorrent WebUI (local and seedbox)
- Real-time activity log with color-coded severity
- Queue management with pause/resume/cancel controls
- Show management with episode tracking

### Configuration
- JSON-based settings (appSettings.json)
- Per-show configuration (RSS feed, source group, schedule)
- Flexible folder paths
- Nyaa cookie authentication

### Known Limitations
- Windows only (WPF application)
- Requires external encoding tools (FFmpeg, VapourSynth, x265, mkvmerge)
- Nyaa cookies need periodic refresh

---

### Upgrade Notes

This is the initial release - no upgrade path required.

### Dependencies

- .NET 8.0 (bundled in standalone build)
- WebView2 Runtime (usually pre-installed on Windows 10/11)
- FluentFTP 49.0.2
- Newtonsoft.Json 13.0.3
- BencodeNET 4.0.0

### File Checksums

```
JudasEncodingManager.exe - SHA256: [To be generated at release]
```

---

**Full Changelog**: Initial Release
