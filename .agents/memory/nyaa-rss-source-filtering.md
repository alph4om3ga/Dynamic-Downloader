---
name: Nyaa RSS source filtering
description: How RSS uploader filters and source-group title checks are combined.
---

For a Nyaa RSS URL that includes a non-empty `u=` uploader parameter, treat the feed-level uploader restriction as authoritative and do not reject an item because its title does not contain the show's saved `SourceGroup`.

**Why:** A show's default source group can remain `Erai-raws`, while the RSS URL is explicitly scoped to another uploader such as VARYG. Applying both filters causes valid items to appear in the RSS checker but be silently skipped by automation.

**How to apply:** Keep title-based `SourceGroup` matching for broad feeds (without `u=`). When changing RSS matching, preserve the uploader-filter exception so source-restricted feeds continue to queue their matching episode.