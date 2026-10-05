# Torrent Free (.NET MAUI)

## 🏬 Available in Stores

- **Android (Google Play):** https://play.google.com/store/apps/details?id=com.torrentfree.app
- **Microsoft Store:** https://apps.microsoft.com/detail/9nnx2ztpxc26
- **Samsung Galaxy Store:** preparation only; not submitted or published. See [Galaxy Store preparation](store/galaxy-store/README.md) for seller requirements, reusable Play assets, signing and store routing.

Cross-platform torrent client built with .NET MAUI and **MonoTorrent** (real engine, not simulated). Supports importing `.torrent` files and magnet links, shows live stats, and on Windows stores downloads next to the picked `.torrent` when possible.

![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-purple)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20Android%20|%20iOS-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

- **Import `.torrent` files** via native file picker, or paste a magnet link
- **Open magnet links and `.torrent` files from other apps** (Android share/open intents; `magnet:` links and `.torrent` file association on Windows)
- **Real torrent engine (MonoTorrent)** for downloads
- **Start / Pause / Stop / Remove / Start All / Stop All** controls
- **Live stats**: progress, download/upload speed, seeds, peers, ETA
- **Seeding state** with pause support
- **Wi-Fi-only transfers**: automatically pause downloads and seeding when Wi-Fi is unavailable, and resume waiting torrents when it returns. Manual pauses and stops are preserved.
- **Automatic resume**: transfers that were active when the app closed, or when Android ended background execution, resume automatically. Manual pauses are kept.
- **Global limits**: upload/download speed caps, max active downloads/seeds, seeding ratio/time
- **Per-torrent limits**: upload/download caps and seeding ratio/time overrides
- **Delete confirmation** with both file-deletion options selected by default; clear either option to keep those files
- **Duplicate protection** by info-hash and magnet link
- **Save path** prefers the picked `.torrent` folder on Windows (if writable), otherwise the default path
- **Persistent storage** of torrent list and settings

## 📝 Release Overview

Version **1.19** (Android code **24**, Windows **1.19.0.0**, x64 and ARM64) fixes
the shared-folder deletion regression: older torrents with missing file metadata
no longer block deletion of unrelated selected downloads. Known shared files remain
protected. The fix is in the shared Core service and applies to Windows and Android.

On **October 2, 2026 (Europe/Oslo)**, all **386 Release tests** passed and all three
store packages were built and validated from clean commit `112eed6`, tagged
[v1.19](https://github.com/kirakosyan/torrent-free/releases/tag/v1.19).
All four GitHub Actions checks also passed. **Version 1.19 has been submitted to
both stores and is not live yet.** Status verified on October 2 at **22:36
Europe/Oslo (20:36 UTC)**:

| Store | Version 1.19 status | Submission |
|-------|--------------------|------------|
| Microsoft Store | In certification; pre-processing underway | **19**, `1152921505702031669`, x64 and ARM64 1.19.0.0 |
| Google Play | In review; initial automated checks running | **21**, production release **11**, code **24**, submitted at 22:30 Europe/Oslo |

Both stores are set to publish automatically after approval. Google Play requests
a 100% rollout across the existing targeted countries. All 31 Microsoft and 26
Google release notes were saved; Windows listing fields and assets were verified
unchanged apart from release notes. Submitted packages and diagnostics are retained.
See the [1.19 release record](store/microsoft-store/release-notes-1.19.md).

Both stores were verified to have **1.18 live** on October 2 during this release.

Version **1.18** (build **23**, Windows package **1.18.0.0**) includes all changes
from PRs [#20](https://github.com/kirakosyan/torrent-free/pull/20) and
[#21](https://github.com/kirakosyan/torrent-free/pull/21). Google Play was sent for
review on **October 1, 2026**; Microsoft Store was submitted for certification on
**October 2, 2026 (Europe/Oslo; October 1 UTC)**.
Microsoft submission **18** and Google submission **20** are published. The live
versions are Windows **1.18.0.0** and Android **1.18 / code 23**.

The annotated [**v1.18** tag](https://github.com/kirakosyan/torrent-free/releases/tag/v1.18)
is pushed on the exact source commit `ea3fb6a` used to build all packages. See the
[1.18 release notes and release record](store/microsoft-store/release-notes-1.18.md),
[Microsoft Store release details](store/microsoft-store/README.md), and
[Google Play release details](store/google-play/README.md).

See the [review findings and validation](docs/review-findings.md) for the current reliability, privacy, accessibility and build improvements, including remaining platform checks.

### v1.18 (live on Windows and Android)

- Restore both file-deletion options as checked by default in the delete dialog. Version 1.17 reversed the earlier fix during a usability review. Regression tests now exercise the dialog defaults as well as all file-deletion combinations.
- Keep torrents visible and retryable if stopping or removing the transfer fails. Stop completion monitoring before removal and restore it when an active stop fails. Preserve shared downloads and source `.torrent` files that have changed since import, with warnings when requested files remain. Empty magnets no longer block deletion, and unrelated imports can continue while a transfer stops.
- Normalize invalid seeding ratios before persistence and refresh rejected edits in the limit editor. Distinguish unavailable Android downloads from failed copies without opening an unrelated Downloads folder.
- Show Windows notifications when downloads complete. Clicking a notification opens the app and selects the completed torrent, including after an app restart.
- Add an optional Windows setting to prevent automatic sleep during active downloads. It is off by default and releases the request when downloads finish, pause or stop; seeding alone allows sleep.

### v1.17

- Download controls and bandwidth charts start collapsed to give the torrent list more room.
- Each torrent has a compact limits icon beside its delete button, opening that torrent's existing limits editor.
- Improved button spacing, status badges and small-screen layouts.
- More reliable settings, imports and notifications; removing a torrent keeps its files by default.

### v1.16

- The app is called Torrent Client in every language, and the Android launcher label is shortened to fit.
- The rating request is offered after three completed downloads instead of five.
- New Store and Play listings in all app languages, with screenshots of legal downloads. See [Microsoft Store listings](store/microsoft-store/README.md) and [Google Play listing](store/google-play/README.md).

### v1.15

- Pausing a magnet link that is still fetching metadata now really stops it.
- Transfers that were active when the app closed resume automatically instead of coming back paused.
- Wi-Fi and proxy changes keep fast-resume data, so transfers no longer re-check all data when they restart.
- "Delete downloaded files" now works for magnet downloads after a restart and reports files it could not delete.
- The delete dialog selects both file-removal options by default; clear either option to keep those files.
- Android: `.torrent` files picked from other apps download to the app download folder instead of a temporary cache; magnet links and `.torrent` files can be opened from other apps; exports show progress and read large files once; optional "keep device awake" setting.
- Windows: `magnet:` links open in the app; unmetered wired and VPN connections count for Wi-Fi-only transfers.
- The SOCKS5 proxy password is stored in platform secure storage instead of the settings file.
- Fewer state-file writes during transfers, and settings validation messages stay visible.

### v1.14

- Open completed audio downloads in LibroNest, with multi-track import and platform store fallback. See [audiobook handoff](docs/libronest-handoff.md).

- Added a startup update banner linking to Microsoft Store or Google Play.
- Added review requests after five completed downloads, with persistent opt-out and a ten-download/30-day reminder interval.
- Added Wi-Fi-only transfers with automatic resume while preserving manual pauses and stops.
- Improved download lifecycle, persistence, Windows reliability, and completed torrents queued for seeding.
- See [store prompt behavior and validation](docs/store-prompts.md).

### v1.13

- Improved saved queue recovery and retained imported torrent metadata across restarts.
- Fixed transfer monitoring, download/seeding queue admission, and active seeding time limits.
- Made Android download exports safer when changing destinations or retrying a copy.
- Extracted a platform-independent core and added regression tests against production models and storage.

### v1.12

- Improved swarm availability metrics and torrent status sorting.
- Updated the .NET MAUI, AndroidX, and test dependency stack.

### v1.11

- Hardened torrent parsing, storage migration, settings persistence, and concurrent engine rebuilds.
- Improved torrent lifecycle safety, Android foreground execution, and Windows activation behavior.

### v1.10

- Strengthened SOCKS5 privacy, torrent parsing, and thread safety.
- Improved Android foreground-service reliability, safe-area handling, themes, and localized store presentation.

### v1.9

- Added system, light, and dark theme selection.
- Applied speed limits and SOCKS5 settings through the MonoTorrent engine.
- Improved Android file handling, engine reliability, and multilingual support.

### v1.8

- Expanded localization and accessibility throughout the app.
- Improved activation reliability and desktop window-state persistence.

### v1.7

- Added Arabic localization with right-to-left UI support.
- Added bulk `Start All` / `Stop All` actions for torrents.
- Improved startup reliability, notifications, proxy handling, and torrent stability.

### v1.6

- Added full `tr` and `hi` resource sets for Turkish and Hindi across the app UI.

### v1.2

- **SOCKS5 Proxy** — Route torrent traffic through a SOCKS5 proxy. Configure host, port, and optional credentials in Settings.
- **Native .torrent file loading** — Torrents added with a `.torrent` file now load instantly without waiting for DHT/tracker metadata.
- **Download progress fix** — Progress no longer resets when pausing and resuming.
- **Persistent seeding ratio** — Upload tracking survives app restarts so ratio limits work correctly.
- **Memory leak fix** — Removing a torrent now properly releases engine resources.
- **Atomic storage writes** — Settings and torrent data are written atomically to prevent corruption on crash.
- **Full backward compatibility** — Upgrading from v1.1 preserves all existing settings.

## 📱 Supported Platforms

| Platform | Status |
|----------|--------|
| Windows | ✅ Supported (WinUI) |
| Android | ✅ Supported |
| iOS | Experimental; requires macOS and device validation |
| macOS | Experimental (Mac Catalyst); sandbox/listener validation pending |

## 🚀 Getting Started

### Prerequisites

- .NET 10 SDK with .NET MAUI workload installed
- Android SDK/Emulator for Android builds
- Windows App SDK (WinUI) for Windows builds
- Xcode 15+ (on macOS) for iOS/macOS

### Building the Project

1. Clone the repository:
   ```bash
   git clone https://github.com/kirakosyan/torrent-free.git
   cd torrent-free
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build for your target platform:
   ```bash
   # Android
   dotnet build src/TorrentFree/TorrentFree.csproj -f net10.0-android
   
   # Windows (from Windows)
   dotnet build src/TorrentFree/TorrentFree.csproj -f net10.0-windows10.0.19041.0
   
   # iOS/MacCatalyst (from macOS)
   dotnet build src/TorrentFree/TorrentFree.csproj -f net10.0-ios
   dotnet build src/TorrentFree/TorrentFree.csproj -f net10.0-maccatalyst
   ```

### Running the App

Windows Debug builds run unpackaged, so launching the project does not register a
development package over the Microsoft Store installation. Release builds still
produce MSIX packages with the existing Store identity. Debug and Store builds
share the saved torrent queue and settings; avoid running both at the same time.

```bash
# Android emulator
dotnet build src/TorrentFree/TorrentFree.csproj -t:Run -f net10.0-android

# Windows
dotnet run --project src/TorrentFree/TorrentFree.csproj -f net10.0-windows10.0.19041.0
```

## 📖 How to Use

### Adding a Torrent

1. Tap **Browse** and pick a `.torrent` file (magnet links are parsed internally)
2. The torrent is added and starts automatically (unless a duplicate is detected)

Magnet links opened from other apps appear in the input field for review. Press **Download** to add and start them.

### Managing Downloads

The bandwidth charts and bulk controls start collapsed. Tap **Show controls** above
the downloads to access **Start All**, **Stop All**, and **Downloading on top**.
Tap the **sliders icon** beside a torrent's delete button to edit that torrent's
speed and seeding limits in a full-page editor. Its name appears above the limit
fields. **Hide limits** returns to Downloads and preserves whether the controls
were collapsed. **Hide controls** collapses the bandwidth and bulk controls.

Each download in the list has action buttons:

| Button | Action |
|--------|--------|
| ▶️ | Start or resume a paused/stopped download |
| ⏸️ | Pause an active download |
| ⏹️ | Stop and reset a download |
| Sliders icon | Open this torrent's speed and seeding limits |
| 🗑️ | Remove the download from the list |

The delete confirmation starts with **Delete torrent file** and **Delete downloaded
files** checked. Clear either option to keep those files. Clearing both removes
only the list entry.

### Download Status

| Status | Color | Description |
|--------|-------|-------------|
| Queued | 🟠 Orange | Waiting to start, including transfers resumed after a restart |
| Downloading | 🔵 Blue | Actively downloading |
| Paused | ⚪ Gray | Download paused by user |
| Completed | 🟢 Green | Download finished successfully |
| Seeding | 🟣 Teal | Uploading after completion |
| Failed | 🔴 Red | Download encountered an error |
| Stopped | ⚪ Gray | Download stopped by user |

### Settings

Open the **Settings** page from the app shell. Changes are saved automatically and applied immediately.

**Speed Limits**

- **Download KB/s**: Global download speed cap in KB/s. Use `0` for unlimited.
- **Upload KB/s**: Global upload speed cap in KB/s. Use `0` for unlimited.

**Queue Limits**

- **Max Active Downloads**: Maximum number of torrents downloading at the same time. Use `0` for unlimited.
- **Max Active Seeds**: Maximum number of torrents seeding at the same time. Use `0` for unlimited.

**Seeding Limits**

- **Max Seed Ratio**: Stop seeding after the uploaded data reaches this ratio relative to the download (e.g., `1.0` means upload equals download). Use `0` for unlimited.
- **Max Seed Minutes**: Stop seeding after this many minutes. Use `0` for unlimited.

**Keep device awake during transfers** (Android only)

- Holds a partial wake lock while downloads or seeding are active so transfers continue with the screen off. Off by default because it uses more battery.

**Prevent sleep while downloading** (Windows only)

- Off by default. Prevents automatic idle sleep while at least one active download is below 100%, including while the app is minimized.
- Allows normal sleep when all active downloads finish, pause or stop. Seeding, queued downloads and downloads waiting for Wi-Fi do not keep the device awake. The screen may still turn off.
- Active downloads keep the request even when stalled, waiting for metadata or without peers. This applies on AC power and battery power and can drain a laptop battery; pause or stop those downloads, or turn the setting off, to allow idle sleep.
- Turning the setting off or closing the app releases the request. Manual sleep and lid-close sleep still take precedence. On Modern Standby devices running on battery, Windows terminates the request five minutes after the configured sleep timeout expires, as described in [Microsoft's power request documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-powersetrequest).

**SOCKS5 Proxy**

- While the proxy is on, DHT, UDP trackers, local peer discovery, port forwarding and incoming connections are disabled so they cannot reveal your IP address. Magnet links need HTTP or HTTPS trackers to find peers in this mode. The password is kept in platform secure storage.

**File Associations** (Windows only)

- **Associate .torrent files**: Toggle whether `.torrent` files open with Torrent Free by default on supported platforms.

**Validation**

Values are normalized to safe ranges (e.g., non-negative, capped to maximums). If a value is out of range, the app adjusts it and shows a warning message that stays until the next valid edit.

## 🗺️ Planned Features

The following features are planned for future releases:

- **RSS Feed Automation** — Subscribe to RSS feeds from torrent sites to automatically download new episodes, releases, or content matching custom filters.
- **Sequential Downloading** — Download pieces in order so that media files can be previewed or played before the full download completes.
- **Selective File Downloading** — Choose which files inside a multi-file torrent to download, skipping unwanted content to save disk space and bandwidth.

## 🏗️ Architecture

The app follows **MVVM**:

```
src/
├── TorrentFree.Core/    # net10.0 library; no MAUI or platform SDK dependencies
│   ├── Models/          # Production observable and persisted models
│   ├── Services/        # Torrent engine, storage, imports, export policy, parsing
│   └── Resources/       # Localization strings
└── TorrentFree/         # MAUI application
    ├── ViewModels/      # Main and settings view models
    ├── Services/        # Platform paths, UI dispatch, pickers, notifications, Android exports
    ├── Platforms/       # Windows, Android, iOS, Mac Catalyst integration
    ├── Converters/      # XAML value converters
    └── Resources/       # Styles and assets
```

### Data Persistence

Downloads are stored in a JSON file in the app's data directory. Actual payload files are downloaded by MonoTorrent to the designated save path.

`StorageService` receives `StoragePaths` from the MAUI host. Reads and writes report failures to callers; replacing the queue requires a successful load, and successful writes rotate the previous state into `torrents.json.bak` by rename. Progress-only changes are saved at most every 30 seconds; state changes are saved immediately. The SOCKS5 proxy password is kept in `ISecretStore` (platform secure storage) and removed from the JSON file, including a password saved by older versions. Imported `.torrent` bytes are kept in the persistent `ImportedTorrents` directory, independent of the original file provider. Active seeding duration is persisted separately from pause time and application downtime. Legacy JSON remains readable.

The app injects `IUiDispatcher` for observable model updates. Tests reference `TorrentFree.Core` directly, using the production models, storage, and MonoTorrent services. Only platform effects such as UI dispatch, notifications, and export destinations are substituted. Run the core suite without MAUI workloads:

```bash
dotnet test --project tests/TorrentFree.UnitTests/TorrentFree.UnitTests.csproj
```

## 🌐 Localization

The app supports the following languages:

| Language | Code | File |
|----------|------|------|
| English | en | `AppResources.resx` |
| Arabic | ar | `AppResources.ar.resx` |
| Chinese (Simplified) | zh-CN | `AppResources.zh-CN.resx` |
| Czech | cs-CZ | `AppResources.cs-CZ.resx` |
| Danish | da-DK | `AppResources.da-DK.resx` |
| Dutch | nl-NL | `AppResources.nl-NL.resx` |
| Finnish | fi-FI | `AppResources.fi-FI.resx` |
| French | fr | `AppResources.fr.resx` |
| German | de-DE | `AppResources.de-DE.resx` |
| Hindi | hi | `AppResources.hi.resx` |
| Hungarian | hu-HU | `AppResources.hu-HU.resx` |
| Indonesian | id | `AppResources.id.resx` |
| Italian | it-IT | `AppResources.it-IT.resx` |
| Japanese | ja-JP | `AppResources.ja-JP.resx` |
| Korean | ko-KR | `AppResources.ko-KR.resx` |
| Norwegian | nb-NO | `AppResources.nb-NO.resx` |
| Polish | pl-PL | `AppResources.pl-PL.resx` |
| Portuguese (Brazil) | pt-BR | `AppResources.pt-BR.resx` |
| Romanian | ro | `AppResources.ro.resx` |
| Russian | ru | `AppResources.ru.resx` |
| Spanish | es | `AppResources.es.resx` |
| Thai | th | `AppResources.th.resx` |
| Turkish | tr | `AppResources.tr.resx` |
| Vietnamese | vi | `AppResources.vi.resx` |

The app automatically uses the system language. To add more languages, create a new resource file following the naming pattern `AppResources.{culture-code}.resx`.

## 🔧 Technologies Used

- .NET MAUI
- MonoTorrent
- CommunityToolkit.Mvvm
- System.Text.Json

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## ⚠️ Disclaimer

This application is provided for educational purposes. Users are responsible for ensuring they only download content they have the legal right to access. The developers are not responsible for any misuse of this software.
