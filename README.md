# Torrent Free (.NET MAUI)

Cross-platform torrent client built with .NET MAUI and **MonoTorrent** (real engine, not simulated). Supports importing `.torrent` files and magnet links, shows live stats, and stores downloads next to the picked `.torrent` when possible.

![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-purple)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20Android%20|%20iOS-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

- **Import `.torrent` files** via native file picker (magnet links supported internally)
- **Real torrent engine (MonoTorrent)** for downloads
- **Start / Pause / Stop / Remove** controls
- **Live stats**: progress, download/upload speed, seeds, peers, ETA
- **Duplicate protection** by info-hash and magnet link
- **Save path** prefers the picked `.torrent` folder (if available) otherwise the default path
- **Persistent storage** of torrent list

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
| Failed | 🔴 Red | Download encountered an error |
| Stopped | ⚪ Gray | Download stopped by user |

## 🏗️ Architecture

The app follows **MVVM**:

```
src/TorrentFree/
├── Models/              # TorrentItem, DownloadStatus
├── ViewModels/          # MainViewModel
├── Services/            # TorrentService (MonoTorrent), StorageService, LocalizationService
├── Converters/          # XAML value converters
└── Resources/           # Styles, strings, assets
```

### Data Persistence

Downloads are stored in a JSON file in the app's data directory. Actual payload files are downloaded by MonoTorrent to the designated save path.

## 🌐 Localization

The app supports the following languages:

| Language | Code | File |
|----------|------|------|
| English | en | `AppResources.resx` |
| French | fr | `AppResources.fr.resx` |
| Spanish | es | `AppResources.es.resx` |
| Russian | ru | `AppResources.ru.resx` |

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

