# Microsoft Store release 1.15

- Display version: 1.15
- Build number: 20
- Windows package version: 1.15.0.0
- Architectures: x64 and ARM64
- Partner Center submission: 15 (`1152921505701979465`)
- Submitted: September 25, 2026
- Status at submission: **In certification** (pre-processing started)
- Publishing: automatically as soon as certification passes
- Source release commit: `c923069`
- Both packages passed Partner Center validation. Release notes were imported and
  verified by export for all 14 existing Store listings; screenshots and other
  listing fields were preserved.

[Partner Center submission status](https://partner.microsoft.com/en-us/dashboard/products/9NNX2ZTPXC26/overview)

## Validation

- All 280 unit tests passed in Release configuration.
- Both architecture packages built successfully and contain all 24 supported app languages.
- Package manifests retain the existing Store name and publisher identity.
- SHA-256 (x64): `0b17eddeb433c740488b175ed3460cfc90f40fcb6829c8085fb50a6d020438b0`
- SHA-256 (ARM64): `b11beec5baa3c18b4c931d35e9de83da2a098c138b7a650b897e2e404f25c1b1`

## What's new

- Open magnet links directly in Torrent Client App on Windows.
- Resume active transfers after restarting the app, and retain fast-resume data when Wi-Fi or proxy settings change.
- Improve pausing while magnet metadata is loading and deleting downloaded files after a restart.
- Select both file-removal options by default in the delete dialog; clear either option to keep those files.
- Allow unmetered wired and VPN connections with Wi-Fi-only transfers.
- Store SOCKS5 proxy passwords in platform secure storage when available.
- Reduce state-file writes and improve settings validation feedback.

## Certification notes

Version 1.15 (Windows package 1.15.0.0), x64 and ARM64. No account or credentials are required. This update retains the existing Store identity and user data. The app is a .NET MAUI desktop torrent client using MonoTorrent; runFullTrust is required for the desktop process, local file access, and peer-to-peer transfers.

Test with a torrent or magnet link for content you have permission to download. Check start, pause, resume, and restart recovery. Windows magnet links now open in the app. Wi-Fi-only mode also allows unmetered wired and VPN connections. In the delete confirmation, both file-removal options are selected by default; clear either option to keep those files. Proxy passwords use platform secure storage when available.
