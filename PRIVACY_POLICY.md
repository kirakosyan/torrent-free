# Privacy Policy

**App name:** Torrent Client App (Torrent Free)
**App ID:** com.torrentfree.app  
**Platforms:** Android, iOS, macOS, Windows  
**Last updated:** 2026-09-05

---

## 1. Overview

Torrent Free is a free, open-source torrent client. We are committed to protecting your privacy. This policy explains what data the app accesses, how it is used, and what is never collected.

**Short version:** The developers do not collect personal information through the app. BitTorrent transfers exchange network information, including your IP address and torrent identifiers, with trackers and other peers as described below.

---

## 2. Data We Do NOT Collect

Torrent Free does **not** collect or send the following information to the developers:

- Personal identification information (name, email, phone number, etc.)
- Device identifiers or advertising IDs
- Location data
- Usage analytics or telemetry
- Crash reports sent to any remote server
- Browsing or download history

---

## 3. Data Stored Locally on Your Device

The app stores its queue and settings on your device. It does not upload them to a developer-operated service. Device backups or folder synchronization configured by you or your operating system may copy local data to your chosen provider.

| Data | Where it is stored | Purpose |
|---|---|---|
| Torrent list (magnet links, file metadata, download progress) | Local app data directory (JSON file) | Restore your downloads between sessions |
| App settings (speed limits, concurrent download limits, sort preferences) | Local app data directory (JSON file) | Persist your preferences |

You can clear app-managed data through your device's app storage settings or by uninstalling the app where the operating system removes its data. Downloaded files and exports in folders you chose may remain and can be deleted separately. Backup copies held by your operating system or synchronization provider are managed through that provider.

---

## 4. Internet Access and Peer-to-Peer Connections

Torrent Free uses internet access for BitTorrent downloads, seeding, and peer discovery. When you add a torrent, the app:

- Connects to **BitTorrent trackers** supplied with the torrent or used to bootstrap trackerless magnet links
- Uses distributed hash table (DHT) and local peer discovery when enabled by the connection configuration
- Connects directly to **other peers** (other users) in the BitTorrent swarm to exchange file data

> **Important:** When participating in a torrent swarm, your IP address is visible to trackers and other peers by the nature of the BitTorrent protocol. This is inherent to how BitTorrent works and is not something Torrent Free controls. Be mindful of the torrents you choose to download.

Optional SOCKS5 settings route supported torrent connections through the proxy you configure. The proxy provider can see connection information; using a proxy does not guarantee anonymity. The app does **not** contact any server owned or operated by the Torrent Free developers.

---

## 5. Android Permissions Explained

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

## 6. Third-Party Libraries

Torrent Free uses [MonoTorrent](https://github.com/alanmcgovern/monotorrent), an open-source BitTorrent library, to handle the network exchanges described above. No advertising SDKs or analytics frameworks are included in the app.

---

## 7. Children's Privacy

Torrent Free is not directed at children under the age of 13 (or the applicable age of digital consent in your jurisdiction). We do not knowingly collect any information from children.

---

## 8. Changes to This Policy

We may update this Privacy Policy from time to time. When we do, we will update the **Last updated** date at the top of this document. Continued use of the app after any changes constitutes acceptance of the updated policy.

---

## 9. Open Source

Torrent Free is open source. You are welcome to inspect the full source code to verify the claims made in this policy:

**Repository:** https://github.com/kirakosyan/torrent-free

---

## 10. Contact

If you have any questions or concerns about this Privacy Policy, please open an issue in the GitHub repository:

https://github.com/kirakosyan/torrent-free/issues
