---
name: Release numbering requirements
description: User-defined release numbering and description behavior.
---

Episode Offset subtracts from source numbering: source EP13 with offset 12 releases as EP01; source EP01 with offset -12 releases as EP13.

**Why:** The user explicitly defined these examples and corrected the release terminology: this is the episode numbering offset fix, not an episode-history fix.

**How to apply:** Offsets affect naming only. Preserve original episode numbers for source lookup, monitoring, queue identity, and history. Compute release names separately for automatic/manual releases and tests.

The per-show checkbox is called **Absolute Number**. When enabled, gray out Season Number and omit the season from names (for example, `[Judas] Show - 100.mkv`).

**Why:** The user requested episode-only names for absolute numbering.

**How to apply:** Keep filenames, torrent display names, and description titles consistent; preserve the saved season when the option is toggled.

Four-digit episode parsing is enabled only for shows with **Absolute Number** checked.

**Why:** The user explicitly restricted the One Piece parsing fix to checked shows.

**How to apply:** Pass the per-show option to RSS, automatic monitoring, and test/manual parsing. Never truncate an unrecognized number or silently substitute episode 1.

Generated description files start with the full torrent display name, followed by a blank line and the existing description.

**Why:** The user supplied a description example with that exact ordering.

**How to apply:** Retain the existing body, metadata, links, and screenshots.

Manual release controls include a **Version number** spinner after Nyaa Visibility, defaulting to v1 and allowing higher versions.

**Why:** The user requested an explicit selectable manual-release version.

**How to apply:** The selected value overrides source-title versions for manual releases and tests, not scheduled monitoring. v1 has no suffix. Higher versions follow the episode in filenames (`S01E01v2.mkv`) and the quality/audio/subtitle tags in torrent titles (`[1080p][HEVC x265 10bit][Dual-Audio][Multi-Subs]v2`).