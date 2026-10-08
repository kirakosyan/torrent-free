# Version 1.23 release record

Display version **1.23**, Android version code **28**, Windows package version
**1.23.0.0** for **x64** and **ARM64**.

## Changes

- Choose individual torrent files before downloading. Search by file or folder,
  select or clear visible results, and review the selected count and total size.
  Magnet links fetch metadata before downloading content; canceling leaves the
  new torrent paused.
- Change the selection later from each torrent. Selections persist across app
  restarts, and progress, size, ETA, and completion follow the selected files.
  Deselected existing files are retained. Shared torrent pieces can require small
  portions of skipped files.
- A responsive, accessible picker supports the app's 24 languages, light and dark
  themes, portrait and landscape layouts, and the mobile keyboard.
- Preserve completed selections and inactive transfer states. Recover from failed
  saves, corrupted cached metadata, and interrupted metadata retrieval. Windows
  activation brings the app forward before displaying file choices.

## Compatibility

All Android variants retain minimum API 23 / **Android 6.0**. The owner explicitly
approved disabling Google Play's optional automatic installer check on October 8,
2026, to preserve Android 6.0 support. Google Play app signing remains enabled.
The feature itself does not require a higher minimum Android version.

Huawei retains `com.torrentfree.app.huawei`, its existing signing key and ARM64-only
packaging. This release aligns Microsoft Store, Google Play and AppGallery at 1.23.
The superseded 1.21 and 1.22 artifacts were never submitted for review; their tags
and packages remain unchanged.

## Validation

- The 1.23 metadata passed all **428 Release tests** and all **five MSBuild
  integration tests**, including API 23 for default Play, explicit Play, Galaxy and Huawei
  in both Debug and Release.
- The feature passed all **428 Release tests**, including 27 file-selection tests.
- Windows and Android Debug builds passed with zero warnings or errors.
- Windows UI checks covered import, search, bulk actions, selection, empty-selection
  prevention, cancel/reopen, and persistence.
- A physical Samsung SM-S926B on Android 16 was checked in portrait and landscape,
  with the keyboard open, at 115% font scale, with long names and 25 files. Search,
  clear, cancel, apply, and saved selections passed. Android's original keyboard
  mode was restored after dismissing the picker. An isolated QA application and
  generated torrent were used without downloading content.
- Release package validation and submission results will be recorded below after
  the packages are built from the clean tagged source.
- iOS and Mac Catalyst are not part of this release.

## Release traceability

Final packages will be built from the clean commit identified by the annotated
**v1.23** tag. Signed Android packages, R8 mapping, native debug symbols, Windows
packages, hashes, and verification evidence are retained under the ignored
`artifacts/store-1.23/` directory, with upload copies in `C:\temp`.

## Store status

**Prepared for submission on October 8, 2026; not submitted or live yet.**
Microsoft submission **20** (`1152921505702078202`) and a Google Play production
draft have been created. Huawei AppGallery is also included in this release. The previous **1.19** release was verified live in both
store consoles on October 8. Google Play shows full rollout across 178 countries
and publication on October 4, 2026. Microsoft identifies submission 19 as its
current Store presence; its exact publication date was not shown.
