# Privacy Policy

**App name:** Torrent Free  
**App ID:** com.torrentfree.app  
**Platforms:** Android, iOS, macOS, Windows  
**Last updated:** 2026-04-05

---

## 1. Overview

Torrent Free is a free, open-source torrent client. We are committed to protecting your privacy. This policy explains what data the app accesses, how it is used, and what is never collected.

**Short version:** The app stores its torrent and settings data locally on your device. Official builds may send limited crash and performance diagnostics to Sentry so we can investigate failures and improve reliability.

---

## 2. Data We Do NOT Collect

Torrent Free does **not** collect or transmit:

- Personal identification information (name, email, phone number, etc.)
- Device identifiers or advertising IDs
- Location data
- Browsing or download history

---

## 3. Data Stored Locally on Your Device

All application data is stored exclusively on your device and is never sent anywhere by us:

| Data | Where it is stored | Purpose |
|---|---|---|
| Torrent list (magnet links, file metadata, download progress) | Local app data directory (JSON file) | Restore your downloads between sessions |
| App settings (speed limits, concurrent download limits, sort preferences) | Local app data directory (JSON file) | Persist your preferences |

You can delete all locally stored data by uninstalling the app or clearing its storage via your device's system settings.

---

## 4. Diagnostic Telemetry and Crash Reports

Official builds of Torrent Free may send limited diagnostic data to **Sentry** when the app crashes, throws an unexpected error, or records a performance trace. This data is used only to diagnose bugs, crashes, and app reliability issues.

Diagnostic events can include:

- App version and build number
- Operating system version and device or runtime details
- Stack traces and exception messages
- Breadcrumbs and logs around the failure
- Performance timings for app operations

We configure Sentry to avoid default personally identifiable information collection and we do **not** enable screenshot capture.

Torrent payload files and downloaded content are **not** uploaded to Sentry by the app.

## 5. Internet Access and Peer-to-Peer Connections

Torrent Free requires internet access **solely** to perform torrent downloads. When you add a torrent, the app:

- Connects to **BitTorrent trackers** listed in the torrent metadata to discover peers
- Connects directly to **other peers** (other users) in the BitTorrent swarm to exchange file data

> **Important:** When participating in a torrent swarm, your IP address is visible to trackers and other peers by the nature of the BitTorrent protocol. This is inherent to how BitTorrent works and is not something Torrent Free controls. Be mindful of the torrents you choose to download.

The app does **not** contact any server owned or operated by the Torrent Free developers.

---

## 6. Android Permissions Explained

The Android version of the app requests the following permissions:

| Permission | Reason |
|---|---|
| `INTERNET` | Required to connect to trackers and peers for downloading torrents |
| `ACCESS_NETWORK_STATE` | Check network availability before attempting connections |
| `POST_NOTIFICATIONS` | Show download progress notifications |
| `FOREGROUND_SERVICE` / `FOREGROUND_SERVICE_DATA_SYNC` | Keep downloads running when the app is in the background |
| `WAKE_LOCK` | Prevent the CPU from sleeping during active downloads |

No permission is used for any purpose other than what is described above.

---

## 7. Third-Party Libraries

Torrent Free uses the following notable third-party libraries:

- [MonoTorrent](https://github.com/alanmcgovern/monotorrent) for BitTorrent protocol operations
- [Sentry](https://sentry.io/) for crash reporting and performance diagnostics in official builds

MonoTorrent does not collect or transmit personal data on our behalf. Sentry receives only the diagnostic data described in Section 4. No advertising SDKs are included in the app.

---

## 8. Children's Privacy

Torrent Free is not directed at children under the age of 13 (or the applicable age of digital consent in your jurisdiction). We do not knowingly collect any information from children.

---

## 9. Changes to This Policy

We may update this Privacy Policy from time to time. When we do, we will update the **Last updated** date at the top of this document. Continued use of the app after any changes constitutes acceptance of the updated policy.

---

## 10. Open Source

Torrent Free is open source. You are welcome to inspect the full source code to verify the claims made in this policy:

**Repository:** https://github.com/kirakosyan/torrent-free

---

## 11. Contact

If you have any questions or concerns about this Privacy Policy, please open an issue in the GitHub repository:

https://github.com/kirakosyan/torrent-free/issues
