# Version 1.22 release record

Display version **1.22**, Android version code **27**, Windows package version
**1.22.0.0** for **x64** and **ARM64**.

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

Google Play now requires API 24 for automatic protection; its build requires
Android 7.0 or newer. Huawei retains API 23 (Android 6.0) and uses its existing
`com.torrentfree.app.huawei` identity, now ARM64 only. This release aligns
Microsoft Store, Google Play and Huawei AppGallery at 1.22.

The 1.21 Google Play upload failed minimum-SDK validation and was never submitted
for review. Its tag and artifacts remain unchanged.

## Validation

- The 1.22 metadata passed all **428 Release tests** and all **five MSBuild
  integration tests**, including Play's API 24 minimum and unchanged API 23 for
  Huawei/Galaxy in Debug and Release.
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

## Release traceability and outcome

Android artifacts were built from clean commit
`985b84968b49cc64cf54ef025b31d16fa741c534`, annotated tag **v1.22**.
Google Play's code-27 AAB passed upload processing but was not submitted for review.
The Huawei APK was built and verified but never uploaded. Windows packages were
not built. The release is superseded by [1.23](release-notes-1.23.md) to retain
Android 6.0 support, as requested by the owner. All 1.22 artifacts are retained in
ignored `artifacts/store-1.22/`; its tag is unchanged.

| Artifact | SHA-256 |
|---|---|
| com.torrentfree.app-v1.22-code27-google-play-upload-key.aab | `025ad9452cc04820ad5a21db39706fb3a3488f6ce0842341737175acecb2a204` |
| mapping.txt | `871d9a09988dd22e4d5d95050ce47894e7c28ff17b94a25c1871e280cf1dbd87` |
| com.torrentfree.app-v1.22-code27-native-debug-symbols.zip | `aa16ecd5fd968a388143d46be263f3b7fbd9d1c3deed9cdaea0aabfabbf007dd` |
| huawei/com.torrentfree.app.huawei-v1.22-code27.apk | `84e0aa5e93caafb750bc3fda86fdfd61c0574e838898f19775a207b7e54420c4` |
| huawei/mapping.txt | `66d498d79a1b63ff6106699f67e3529ccde95fdb699028ae7e7c01ea580a4080` |
| huawei/native-symbols.zip | `77a034017fce12967a56cec6cc07842d38694b76f4f55a6e38c202ca51efae45` |
