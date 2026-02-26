## What's New in v1.2

**SOCKS5 Proxy**
Route torrent traffic through a SOCKS5 proxy. Configure host, port, and optional credentials in Settings.

**Native .torrent File Support**
Added torrents with a .torrent file now load instantly — no more waiting for metadata from DHT or trackers.

**Bug Fixes & Reliability**
- Download progress no longer resets when pausing and resuming.
- Seeding ratio now tracked persistently across app restarts.
- Fixed a memory leak when removing torrents.
- Settings file writes are now atomic to prevent data corruption.

Upgrading from v1.1 is seamless — all existing settings are preserved.
