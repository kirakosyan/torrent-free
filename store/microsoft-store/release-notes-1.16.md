# Microsoft Store release 1.16

- Display version: 1.16
- Build number: 21
- Windows package version: 1.16.0.0
- Architectures: x64 and ARM64
- Localized release notes: `release-notes-1.16.json` and the `ReleaseNotes` row of `listings.csv`

## What's new

- The app is called Torrent Client in every language, matching the Store listing.
- The rating request is offered after three completed downloads instead of five.
- New listing text in all 24 app languages, new screenshots and captions.

## Certification notes

Version 1.16 (Windows package 1.16.0.0), x64 and ARM64. No account or credentials are required. This update keeps the existing Store identity and user data. The app is a .NET MAUI desktop torrent client using MonoTorrent; runFullTrust is required for the desktop process, local file access, and peer-to-peer transfers.

Test with a torrent or magnet link for content you have permission to download, for example a Debian installation image from debian.org. Magnet links clicked in a browser open in the app. The in-app title now reads Torrent Client; the listing screenshots show only openly licensed downloads.
