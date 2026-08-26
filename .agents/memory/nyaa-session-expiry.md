---
name: Nyaa session expiry
description: Rules for tracking the local Nyaa session-cookie validity period and reminders.
---

The Nyaa session-cookie timer starts only when the session value is changed or the user explicitly confirms a reset. Settings that predate the timer must remain untracked until refreshed; never assign them a made-up new expiration date.

**Why:** The app cannot recover when an existing cookie was originally copied, and assuming it is fresh would hide an imminent auto-posting failure.

**How to apply:** Use the cookie refresh timestamp as the identity of its 28-day validity period. Record the one-day warning against that period so timer refreshes and saved-file reloads do not repeat the same prompt; clear the marker when the cookie or timer is reset.