# Version 1.18 release record

Display version **1.18**, Android version code **23**, Windows package version
**1.18.0.0** for **x64** and **ARM64**. This release includes all accumulated
changes from PRs [#20](https://github.com/kirakosyan/torrent-free/pull/20) and
[#21](https://github.com/kirakosyan/torrent-free/pull/21).

## Changes

- Restore both file-deletion checkboxes as selected by default. Clear either
  checkbox to keep those files. Protect shared payloads and source torrent files
  that changed after import, and warn when requested files remain.
- Keep failed removals visible and retryable. Stop completion monitoring before
  removal, restore it if an active stop fails, and allow unrelated imports while
  a transfer stops. Empty magnets no longer prevent deletion.
- Validate seeding ratios before persistence and refresh rejected edits in the
  limit editor. Android distinguishes unavailable downloads from copy failures
  and opens Downloads only after a successful export.
- Show Windows notifications when downloads finish. Clicking the notification
  opens the app and selects the torrent, including after an app restart.
- Add **Prevent sleep while downloading** in Windows Settings, off by default.
  It holds a process-owned system power request while any active download is
  below 100%, and releases it when downloads finish, pause, stop or are removed,
  when the setting is disabled, or when the app closes. Seeding alone allows sleep.
  Stalled downloads still hold the request on AC and battery power. Manual/lid
  sleep and Modern Standby battery limits retain precedence; the screen may turn off.
- Address sleep-prevention review feedback: avoid full-list progress scans and
  repeated native calls, suppress failed acquire retries, cache the localized
  request reason outside locks, test the shipped Core implementation directly,
  share the Settings card, and correct German/Danish sleep terminology.

## Validation

- PR #21: all eight GitHub Actions checks passed on commit
  `11b5378af2933d38d4cc1fef44c2b4d807899686`; merged as
  `88e9d9bfe8d729ce269a59d7ca966ee1514cf517`.
- Local pre-release validation: **382 tests passed**, including **25**
  sleep-prevention cases. Windows ARM64 and Android Debug builds passed with
  zero warnings or errors. Native Windows power request acquire/release was exercised.
- Version 1.18/build 23 metadata validation and the complete 382-test suite passed.
- All four checks for the final source commit passed in
  [GitHub Actions run 36926679194](https://github.com/kirakosyan/torrent-free/actions/runs/36926679194):
  Windows/Linux Core tests and Windows/Android app builds.
- Signed Android Release publish passed. Bundletool validation passed; the manifest
  has package `com.torrentfree.app`, code **23**, version **1.18**, target SDK **36**.
  JAR signature verification passed and the signing certificate matches the
  registered upload certificate. The embedded R8 mapping was retained, together
  with native symbols matching `libxamarin-app.so` for arm64-v8a and x86_64. The symbol
  files' GNU ELF build IDs match their packaged libraries for both ABIs.
- Windows Release x64 and ARM64 publishes passed. Each MSIX was checked for ZIP
  integrity, Store identity/publisher, version **1.18.0.0**, architecture, all
  24 languages, Core payload, and matching toast/COM registration with its executable.
  The packaging tool reported the optional Windows symbol tool `mspdbcmf.exe`
  unavailable; no Windows symbol package was requested or generated.
- Device sleep/lid behavior and packaged notification activation have not been
  exercised end to end in this release preparation.
- Localized notes: 24 Windows app languages, mapped to 31 existing Store listings;
  26 Play languages. Existing availability, descriptions and screenshots are preserved.

## Package provenance

- Annotated tag: [**v1.18**](https://github.com/kirakosyan/torrent-free/releases/tag/v1.18),
  pushed October 1, 2026. Exact clean source commit used for all final packages:
  [`ea3fb6a6820fe943666fd4ffdc88e64fbfd22acc`](https://github.com/kirakosyan/torrent-free/commit/ea3fb6a6820fe943666fd4ffdc88e64fbfd22acc).
- Retain final packages, mapping and matching native debug symbols under the
  ignored `artifacts/store-1.18/` directory, with upload copies under `C:\temp`.
- Signing material remains outside Git under `C:\temp\torrent-android`.

| Retained artifact | SHA-256 |
|-------------------|---------|
| Signed Android AAB, 1.18 code 23 | `34dc812e3a90f1870d95377e1dba7b0dc0653e571b530f1d0b3dc79477065ee6` |
| Android R8 mapping | `05466314c1e149affb7f896c3c7b8de9b79fbdbea43586cef9abed95cdbb5803` |
| Android native debug symbols ZIP | `dcae67fd2c45ce166fddf879c0753b2eb36af7d3c16213cae7c27666fec427dd` |
| Windows x64 MSIX | `99d7ff602fcddb9e2a37c9c107a17e6561ebae43e839b6e98c26c19bc05672be` |
| Windows ARM64 MSIX | `37d70254b3399ebed229ac09f71eafb8aca680613eb310789a229882381f1357` |

## Store status

As verified on **October 2, 2026 at 00:43 Europe/Oslo** (October 1 at 22:43 UTC),
Google Play **1.18 is in review** and Microsoft Store **1.18.0.0 is in certification**,
with pre-processing in progress.
**Neither release is live.**

| Store | Target package | Status | Submission/publication record |
|-------|----------------|--------|-------------------------------|
| Microsoft Store | 1.18.0.0 x64 + ARM64 | In certification; pre-processing in progress | Submission **18**, `1152921505702024112`, submitted October 2, 2026 at 00:43 Europe/Oslo (October 1 at 22:43 UTC) |
| Google Play | 1.18, code 23 | In review; automated checks completed | Submission **20**, October 1, 2026 at 23:23 Europe/Oslo (21:23 UTC); production release `10`, track `4698459015960602547` |

Google Play accepted the signed AAB with embedded ReTrace mapping and the matching
native debug symbols attachment. Release notes are supplied for all 26 languages.
The full rollout targets all existing 178 countries/regions, and the console shows
no changes to supported devices. Managed publishing remains off, so publication
will follow approval automatically.

Microsoft accepted both architecture packages. Its toast-extension warning applies
to Xbox; Xbox remains unchecked, with Desktop and Team availability preserved.
Release notes were saved and verified in an export for all 31 existing listing
languages; the unused default column remains empty. Descriptions and other fields
were retained. Importing the full CSV reordered screenshots in nine languages;
the original order and associated captions were restored, saved and verified by
reopening all nine listings. All original image/caption pairs were retained.
The final export failed, so the saved order was verified directly in the editor;
the local verification record is retained with the release artifacts. Future
notes-only imports must contain only the CSV header and `ReleaseNotes` row.
Tester instructions were updated from 1.17 to describe the checked deletion
defaults, completion notifications and sleep setting. Publication is set to start
automatically after certification passes.

Version 1.17 remains live in both stores. Play Console reports its publication on
September 30, 2026 and 100% rollout. Microsoft submission 17
(`1152921505702004251`) is live; its exact publication date has not been verified.
Both statuses were checked in the store consoles on October 1, 2026.

## Certification notes

No account or test credentials are required. The Windows `runFullTrust` capability
is required for the .NET MAUI desktop torrent client. Use openly licensed torrent
content such as Debian images when testing.
