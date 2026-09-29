# Microsoft Store release 1.17

- Display version: 1.17
- Build number: 22
- Windows package version: 1.17.0.0
- Architectures: x64 and ARM64
- Localized release notes: `release-notes-1.17.json` (24 languages; reused for matching regional Store listings)

## What's new

- Download controls and bandwidth charts start collapsed, leaving more room for torrents.
- Each torrent has a limits icon beside its delete button, opening the existing per-torrent editor.
- Improved control spacing, compact status badges and small-screen layouts.
- Reliability improvements for settings, imports and notifications. Removing a torrent preserves its files by default.

## Certification notes

Version 1.17 (Windows package 1.17.0.0), x64 and ARM64. No account or credentials are required. The update preserves the existing Store identity and user data. The app is a .NET MAUI desktop torrent client using MonoTorrent; runFullTrust is required for the desktop process, local file access and peer-to-peer transfers.

Test with content you have permission to download, such as a Debian installation image from debian.org. Check start, pause, resume and restart recovery. Download controls begin collapsed; expand them to view bandwidth and bulk controls. Use the sliders icon on a torrent card to open its limits editor. External magnet links are placed in the input for review before the user presses Download. The file-deletion options are unchecked by default.

## Validation

- All eight PR checks passed before merge; all 298 tests passed in Release configuration after the version update.
- Signed Android bundle validated with bundletool; package `com.torrentfree.app`, version 1.17, code 22, signing certificate verified. Mapping data and both ABI symbol files included in the release artifacts.
- Windows x64 and ARM64 Release MSIX builds succeeded. Identity, version, architecture, all 24 resource languages and the limits icon were verified in both packages.
- Windows packaging reported the existing missing `mspdbcmf.exe` warning; packages were generated successfully without a symbols package.
- Windows manual checks confirmed the per-torrent icon opens the matching torrent and closing the editor preserves collapsed controls.
