# Release Notes

## v1.3 (August 25, 2026)

### Reliability
- Hardened local qBittorrent hand-offs by confirming a stopped state before moving downloaded media.
- Added exclusive-access checks and retry handling for Windows file locks.
- Preserved downloaded media when removing the local torrent entry.
- Kept the automation queue moving when an individual item fails.

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
