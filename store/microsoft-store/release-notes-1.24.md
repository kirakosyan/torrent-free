# Version 1.24 release record

Windows package **1.24.0.0** (x64 and ARM64). Google Play **1.24**, version code **29**.

- Compact cards keep progress, speed, and estimated time visible. Expand a card for more statistics.
- Search and status filters appear above five downloads. Hidden filters no longer affect the list.
- Interrupted saves recover more reliably, and queued downloads start when a transfer frees a slot.
- Download completion notifications open the corresponding card. Error details can be copied.
- About includes Feedback and rating and retains Patreon support.

Google Play retains Android 6.0 support (API 23) and targets Android 16 (API 36).

## Store status

| Store | Submission | Verified November 7, 2026 |
| --- | --- | --- |
| Microsoft Store | **21**, `1152921505702342637`, Windows 1.24.0.0 x64 and ARM64 | **In certification**. Publication starts after certification. |
| Google Play | **23**, Android 1.24/code 29, submitted 20:28 Europe/Oslo | **In review**. Full production rollout to all existing targeted countries, with managed publishing off. |

The consoles showed **1.23** as the live release on Windows and Google Play before submitting 1.24.

## Validation

- All **493** core tests and **10** store-tool tests passed. All six [CI jobs](https://github.com/kirakosyan/torrent-free/actions/runs/42405563903) passed on the release commit.
- The Windows packages passed identity, architecture, language, and toast manifest checks. The x64 payload was launched from a temporary side-by-side test registration using a separate identity and with activation extensions removed from the test manifest. The app loaded the stopped queue and showed 1.24/build 29, Feedback and rating, and Patreon in About. The shared queue hash was unchanged after closing the app. ARM64 was validated as a package.
- The signed Play bundle passed bundletool and signature verification. A derived APK was installed on the API 36 emulator and verified at 1.24/code 29. The queue and About dialog rendered, and search and status filters were hidden at five downloads. Both native symbol build IDs match the bundle libraries.
- Partner Center accepted both packages. The existing Xbox toast warning is informational for the selected Desktop and Windows Team availability. A fresh export confirmed that only the release notes changed. Google Play accepted all 26 release notes, R8 mapping, and the native symbols ZIP.
- Builds retained the existing XAML binding warning and Windows optional symbol-tool warning. The temporary Windows test registration was removed and the Android emulator was closed.

## Release traceability

The packages were built from clean commit [`75ed1c1`](https://github.com/kirakosyan/torrent-free/commit/75ed1c158707317e6e73df6471cc8447f7c32389), tagged [`v1.24`](https://github.com/kirakosyan/torrent-free/tree/v1.24). Packages, mapping, symbols, hashes, and Windows listing exports are retained in ignored `artifacts/store-1.24/`.

| Artifact | SHA-256 |
| --- | --- |
| `TorrentFree_1.24.0.0_x64.msix` | `201bc1fad49f55ed927291b5934268a1e1225e3cdfa795e80c9501121e2c5939` |
| `TorrentFree_1.24.0.0_arm64.msix` | `6c21bf10ef4d9dfacdc8fdd1612da1d4e22a6ecc2129dc9f4cd648e7dd3106db` |
| `com.torrentfree.app-v1.24-code29-google-play-upload-key.aab` | `d5bf6fe52ddf5aff0e73e06d34f24f3869f31b3ffea9ca183a3c5dd9c931236a` |
| `mapping.txt` | `c328aad3d6517062b2863c904f79a697ad83f714d9fe134f3dda91f2c0ee9a76` |
| `native-debug-symbols.zip` | `e39030f7ead25a0203cd425f82df9828f20166d4be482fee32fdceac4ea9afac` |
