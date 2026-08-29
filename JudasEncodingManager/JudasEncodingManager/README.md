# Judas Encoding Manager v1.3.2

A Windows WPF application for automating the Judas anime encoding and distribution pipeline.

## Overview

JudasEncodingManager automates the entire workflow from detecting new episodes via RSS feeds to posting finished encodes on Nyaa. It handles downloading, encoding with x265/Opus, muxing with proper track naming, screenshot capture, FTP upload to seedbox, and torrent posting.

## Features

### 🎬 Automated Encoding Pipeline
- **RSS Monitoring** - Watches Nyaa RSS feeds for new episodes from configured source groups
- **Automated Downloads** - Adds torrents to qBittorrent and monitors until complete
- **Full x265 Encoding** - Runs PowerShell encoding scripts with VapourSynth
- **Track Analysis** - Detects audio/subtitle languages for proper tagging
- **Smart Muxing** - Remuxes with correct track names and ordering (English subs first)
- **Screenshot Capture** - Takes 3 screenshots from random positions and uploads to ImgBB
- **Torrent Creation** - Generates .torrent files for distribution
- **FTP Upload** - Uploads to seedbox with real-time progress tracking
- **Nyaa Posting** - Posts releases with full descriptions, supports hidden/public visibility

### 🧪 Test Run System
- **Simulate Mode** - UI testing with fake delays, no actual processing
- **Quick Encode** - 5-minute ffmpeg test for rapid pipeline verification
- **Full Encode** - Production-quality PowerShell encoding
- **Hidden/Public Toggle** - Safe testing with hidden Nyaa posts before going public

### 📺 Show Management
- Add, edit, and remove weekly shows
- Configure RSS feeds and source group filters
- Set release schedules (day/time)
- Track episode history with version support (v2, v3 for repacks)
- Custom episode regex for unusual filename patterns
- Episode offset for split-cour shows

### 🔔 Discord Notifications
All notifications include your machine name for multi-encoder setups:
- **Episode Grabbed** - When a new episode is detected and download starts
- **Download Complete** - When the source file is ready
- **Encoding Started** - When encoding begins (shows Quick/Full type)
- **Encoding Complete** - When encoding finishes (includes duration and file size)
- **Torrent Posted** - When released to Nyaa (includes URL and visibility)

### 🎨 User Interface
- **8 Color Schemes** - Dark Blue, Dark Purple, Dark Green, Dark Red, Light Blue, Light Purple, Light Green, Light
- **Embedded qBittorrent WebUI** - View local and seedbox instances directly in the app
- **Activity Log** - Real-time color-coded logging of all operations
- **Queue Management** - View, pause, resume, retry, and cancel operations

## Requirements

### For Running
- Windows 10/11 (x64)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually pre-installed)
- qBittorrent with WebUI enabled (local and/or seedbox)
- FTP access to seedbox
- Nyaa account with session cookies
- ImgBB API key for screenshots

### For Building
- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)

### External Tools (for encoding)
- FFmpeg (for quick test encodes)
- VapourSynth with required plugins
- x265 encoder
- mkvmerge (MKVToolNix)
- PowerShell encoding scripts

## Quick Start

### Pre-built Executable
1. Download and extract the release
2. Double-click `JudasEncodingManager.exe`
3. Configure settings in the ⚙️ Settings tab
4. Add shows in the 📺 Weekly Shows tab
5. Start monitoring in the 🤖 Automation tab

### Building from Source
```powershell
# Clone the repository
git clone https://github.com/your-repo/JudasEncodingManager.git
cd JudasEncodingManager

# Build standalone executable
.\Build.bat
# Or with PowerShell
.\Build.ps1 -Release

# Output: .\publish\JudasEncodingManager.exe
```

## Configuration

### appSettings.json Structure

```json
{
  "machine_name": "Encoder-1",
  "imgbb_api_key": "your-api-key",
  "test_mode": false,
  "color_scheme": "Dark Blue",
  "folders": {
    "temp_folder": "C:\\JudasEncodingManager\\Temp",
    "log_folder": "C:\\JudasEncodingManager\\Logs",
    "encoding_folder": "C:\\JudasEncodingManager\\Encoding",
    "screenshots_folder": "C:\\JudasEncodingManager\\Screenshots",
    "seeding_folder": "C:\\JudasEncodingManager\\Seeding"
  },
  "qbittorrent_webui": {
    "local_url": "http://localhost:8080",
    "local_username": "admin",
    "local_password": "password",
    "seedbox_url": "https://seedbox.example.com:8080",
    "seedbox_username": "user",
    "seedbox_password": "password"
  },
  "ftp": {
    "host": "seedbox.example.com",
    "port": 21,
    "username": "user",
    "password": "password",
    "remote_path": "/torrents/judas"
  },
  "discord": {
    "webhook_url": "https://discord.com/api/webhooks/...",
    "server_invite_link": "https://discord.gg/..."
  },
  "auto_posting": {
    "nyaa_ddlg_cookie": "...",
    "nyaa_session_cookie": "..."
  },
  "weekly_shows": []
}
```

### Show Configuration

```json
{
  "torrent_title": "Show Name (English Name)",
  "file_title": "Show Name",
  "season_number": 1,
  "ini_script_name": "ShowName.ini",
  "source_group": "SubsPlease",
  "episode_offset": 0,
  "rss_feed": "https://nyaa.si/?page=rss&q=...",
  "release_day": "Saturday",
  "release_time": "12:00",
  "expected_episodes": 12,
  "is_active": true,
  "is_uncensored": false
}
```

## Pipeline Workflow

1. **RSS Detection** - Monitors configured feeds for new episodes
2. **Download** - Adds torrent to local qBittorrent, waits for completion
3. **Track Analysis** - Identifies audio/subtitle tracks and languages
4. **Encoding** - Runs PowerShell script with VapourSynth/x265
5. **Cleanup** - Deletes source file and LWI index files
6. **Muxing** - Remuxes with proper track names and ordering
7. **Screenshots** - Captures 3 frames, uploads to ImgBB
8. **Description** - Generates Nyaa description from template
9. **Torrent Creation** - Creates .torrent file
10. **FTP Upload** - Uploads torrent, description, and video to seedbox
11. **Seedbox Torrent** - Adds torrent to seedbox qBittorrent
12. **Nyaa Posting** - Posts to Nyaa with full metadata

## Track Naming Conventions

### Video
- `[Judas] x265 10b`

### Audio
- Just the language name: `Japanese`, `English`

### Subtitles (ordered by priority)
- English tracks first, then other languages
- Format: `English`, `English [Signs/Songs]`, `Spanish`, etc.

## Torrent Display Name Format

```
[Judas] Show Name (English Name) - S01E03v2 [1080p][HEVC x265 10bit][Dual-Audio][Multi-Subs] (Weekly)
```

Tags are dynamic based on actual track content:
- `Dual-Audio` - When both Japanese and English audio present
- `Multi-Subs` - When multiple subtitle languages present
- `Eng-Subs` - When only English subtitles

## Test Run Options

| Mode | Pipeline | Encode | Nyaa |
|------|----------|--------|------|
| Simulate | Fake delays | None | None |
| Real + Quick + Hidden | Full | 5-min ffmpeg | Hidden |
| Real + Quick + Public | Full | 5-min ffmpeg | Public |
| Real + Full + Hidden | Full | PowerShell x265 | Hidden |
| Real + Full + Public | Full | PowerShell x265 | Public |

## Troubleshooting

### Encoding Stuck at "Starting encode"
- Ensure PowerShell scripts don't require user interaction
- Check that all VapourSynth plugins are installed
- Verify paths in WorkerConfig.ini are correct

### Muxing Failed
- Ensure mkvmerge is in PATH or configured in settings
- Check for special characters in filenames

### Nyaa Posting Failed
- Refresh your Nyaa session cookies (they expire)
- Check that the torrent file was created successfully
- Verify your account can post (not banned/limited)

### FTP Upload Failed
- Check firewall settings for passive FTP
- Verify credentials and remote path exist
- Ensure sufficient disk space on seedbox

## License

This project is for personal use by the Judas encoding team.

## Acknowledgments

- [FluentFTP](https://github.com/robinrodricks/FluentFTP) for reliable FTP uploads
- [Newtonsoft.Json](https://www.newtonsoft.com/json) for JSON handling
- [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) for embedded browser
