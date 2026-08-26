---
name: qBittorrent file finalization
description: Safe hand-off requirements between local qBittorrent downloads and the encoding pipeline.
---

Local qBittorrent downloads must have an explicitly confirmed paused or stopped state before their media is moved or torrent metadata is removed. A failed stop request, missing status response, or unconfirmed state is a failed queue item—not a reason to attempt the move.

**Why:** A completed-progress response does not guarantee qBittorrent has released its file handle or stopped managing the original path. Moving too early can cause file-lock failures and leave qBittorrent with inconsistent torrent state.

**How to apply:** Retry stop/state confirmation with cancellation support, verify the source file is stable and exclusively accessible, and re-probe exclusive access immediately before every file-move retry. Remove only torrent metadata with downloaded data retained. Let later pending queue items continue when one item fails.