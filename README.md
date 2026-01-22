# Torrent Free 🔥

A free, cross-platform torrent client UI built with .NET MAUI. Features a beautiful, professional interface for managing magnet link downloads.

> **⚠️ Demo Version**: This is a UI demonstration with simulated download progress. Real BitTorrent protocol integration would require a library like [MonoTorrent](https://github.com/alanmcgovern/monotorrent). The current implementation showcases the UI, architecture, and user experience.

![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-purple)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20Android%20|%20iOS-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

- **📥 Easy Downloads**: Simply paste a magnet link and click download
- **📊 Progress Tracking**: Real-time progress bars showing download status (simulated)
- **⏯️ Download Control**: Start, pause, stop, and resume downloads at any time
- **📜 Download History**: Keep track of all your downloads in one place
- **🌍 Multilanguage Support**: Available in English, French, Spanish, and Russian
- **💾 Persistent Storage**: Your downloads are saved and restored when you reopen the app
- **🎨 Professional UI**: Modern, clean interface with intuitive controls

## 📱 Supported Platforms

| Platform | Status |
|----------|--------|
| Windows | ✅ Supported |
| Android | ✅ Supported |
| iOS | ✅ Supported |
| macOS | ✅ Supported (via Mac Catalyst) |

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- For Android: Android SDK (API 21 or higher)
- For iOS/macOS: Xcode 15+ (macOS only)
- For Windows: Windows 10 version 1809 or higher

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
   
   # iOS (from macOS)
   dotnet build src/TorrentFree/TorrentFree.csproj -f net10.0-ios
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

1. **Copy a magnet link** from your torrent source
2. **Paste the link** into the input field at the top of the app
3. **Click the "Download" button** - the torrent will be added to your download list and start automatically

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

The app follows the **MVVM (Model-View-ViewModel)** pattern:

```
src/TorrentFree/
├── Models/              # Data models (TorrentItem, DownloadStatus)
├── ViewModels/          # View models with business logic
├── Views/               # XAML pages and views
├── Services/            # Business services
│   ├── TorrentService   # Torrent download management
│   ├── StorageService   # JSON persistence
│   └── LocalizationService # Multi-language support
├── Converters/          # XAML value converters
└── Resources/
    └── Strings/         # Localization resource files
```

### Data Persistence

Downloads are stored in a JSON file in the app's data directory:
- **Android**: `/data/data/com.torrentfree.app/files/torrents.json`
- **Windows**: `%LOCALAPPDATA%\TorrentFree\torrents.json`
- **iOS/macOS**: `~/Library/Containers/com.torrentfree.app/Data/Documents/torrents.json`

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

- **.NET 10 MAUI** - Cross-platform UI framework
- **CommunityToolkit.Mvvm** - MVVM infrastructure and source generators
- **System.Text.Json** - JSON serialization for data persistence

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## ⚠️ Disclaimer

This application is provided for educational purposes. Users are responsible for ensuring they only download content they have the legal right to access. The developers are not responsible for any misuse of this software.

