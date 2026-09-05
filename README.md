# Torrent Free (.NET MAUI)

## 🏬 Available in Stores

- **Android (Google Play):** https://play.google.com/store/apps/details?id=com.torrentfree.app
- **Microsoft Store:** https://apps.microsoft.com/detail/9nnx2ztpxc26

Cross-platform torrent client built with .NET MAUI and **MonoTorrent** (real engine, not simulated). Supports importing `.torrent` files and magnet links, shows live stats, and stores downloads next to the picked `.torrent` when possible.

![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-purple)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20Android%20|%20iOS-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

- **Import `.torrent` files** via native file picker (magnet links supported internally)
- **Open `.torrent` files with the app** (file association on Windows)
- **Real torrent engine (MonoTorrent)** for downloads
- **Start / Pause / Stop / Remove / Start All / Stop All** controls
- **Live stats**: progress, download/upload speed, seeds, peers, ETA
- **Seeding state** with pause support
- **Global limits**: upload/download speed caps, max active downloads/seeds, seeding ratio/time
- **Per-torrent limits**: upload/download caps and seeding ratio/time overrides
- **Safe delete dialog** with options to remove data and/or the `.torrent` file
- **Duplicate protection** by info-hash and magnet link
- **Save path** prefers the picked `.torrent` folder (if available) otherwise the default path
- **Persistent storage** of torrent list and settings

## 📝 Release Overview

The current app version is **v1.12**.

### v1.12 (latest)

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
| iOS | ✅ Supported (requires macOS) |
| macOS | ✅ Supported (Mac Catalyst) |

## 🚀 Getting Started

### Prerequisites

- .NET 8/10 SDK with .NET MAUI workload installed
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

### Managing Downloads

Each download in the list has action buttons:

| Button | Action |
|--------|--------|
| ▶️ | Start or resume a paused/stopped download |
| ⏸️ | Pause an active download |
| ⏹️ | Stop and reset a download |
| 🗑️ | Remove the download from the list |

### Download Status

| Status | Color | Description |
|--------|-------|-------------|
| Queued | 🟠 Orange | Waiting to start |
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

**File Associations** (Windows only)

- **Associate .torrent files**: Toggle whether `.torrent` files open with Torrent Free by default on supported platforms.

**Validation**

Values are normalized to safe ranges (e.g., non-negative, capped to maximums). If a value is out of range, the app adjusts it and shows a short warning message.

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

`StorageService` receives `StoragePaths` from the MAUI host. Reads and writes report failures to callers; replacing the queue requires a successful load, and successful writes retain the previous state as `torrents.json.bak`. Imported `.torrent` bytes are kept in the persistent `ImportedTorrents` directory, independent of the original file provider. Active seeding duration is persisted separately from pause time and application downtime. Legacy JSON remains readable.

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
