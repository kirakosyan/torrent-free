# Version 1.25.1 release record

Prepared November 15, 2026 (Europe/Oslo). **Not uploaded, submitted or published.**
Windows **1.25.1.0**, x64 and ARM64; Android **1.25.1**, version code **31**.

## Changes

Includes the reliability, diagnostics, settings, private-metadata and Android
export fixes described in the [1.25 preparation record](release-notes-1.25.md).
Windows smoke testing found that an earlier import error remained visible after
opening a valid `.torrent` file. File activation now clears the previous error
when beginning a new import, consistent with Browse and magnet activation.

The unsubmitted 1.25 packages and local `v1.25` tag are retained unchanged.
Replacement packages use a new version and annotated `v1.25.1` source tag.
Assembly and file versions use the four-part package version, accommodating the
three-part display version without producing an invalid five-part version.

## Qualification

The initial 1.25 x64 package passed startup, settings navigation and persistence,
Unicode numeric normalization, proxy draft retention, magnet preview and warm
redirection, Browse/file selection, pause, stop, restart and graceful shutdown.
Malformed file activation produced a visible error; the subsequent valid file
exposed the stale-message issue fixed here. About showed 1.25 / build 30.

The smoke launcher must keep its WinRT file object alive until activation has
completed. Exiting that helper immediately caused a native RPC-server-unavailable
failure before managed app startup; correcting the test helper resolved it.
Directly running the extracted EXE also lacked package identity. Neither setup
failure reproduces the original Store activation bucket.

Testing uses a temporary package identity with unchanged application binaries.
The installed 1.24 package remains installed. All original app-data files were
hash-verified on restoration; six original torrents and settings were preserved,
and one new non-test torrent added during testing was retained separately from
the smoke fixtures. Dumps and private logs remain local and are excluded from
release bundles.

## Final package qualification

Built from clean commit `22292b787563b335c301220bf98171ecdc9b4d1c`. Annotated tag **v1.25.1**
identifies the exact package source; the superseded **v1.25** tag is unchanged.
All **539 Release core tests** and **10 store-tool tests** passed.

- Both MSIX packages passed MakeAppx unpack, identity/version/architecture,
  24-language, activation-extension and payload-hash checks. Managed symbols are retained.
- The Play AAB passed bundletool and signature checks. Both APKs passed v2/v3
  signature and 16 KiB alignment checks. Package IDs, API 23 minimum/API 36 target,
  non-debuggable manifests, code 31 and version 1.25.1 match their store variants.
- Android signing certificate matches the previously submitted packages.
  R8 maps are retained, the Play map matches its embedded copy, and native debug
  symbol build IDs match every packaged library.
- **Final Windows x64 packaged-payload smoke passed.** Cold invalid-file activation
  reports an error; warm valid-file activation opens selection and clears that
  error. Cancellation leaves the fixture paused. Magnet activation restores a
  minimized window without starting a transfer. Five rapid secondary launches
  redirect to the same process. Restart retains the paused queue, About reports
  1.25.1 / build 31 / file 1.25.1.0, and shutdown exits cleanly.
- No app crash event or diagnostic fault log was recorded in the final smoke run.
  All seven real queue entries and original settings were hash-verified on restoration.
  The temporary registration was removed; installed Store version 1.24 was preserved.
- ARM64 runtime and Android device flows were not smoke-tested in this session.
  The original production activation bucket remains unproven; compare post-release
  health by version and collect managed diagnostics if it recurs.

Artifacts are retained in ignored `artifacts/store-1.25.1/`. The packages are:

| Package | SHA-256 |
| --- | --- |
| `windows/TorrentFree_1.25.1.0_x64.msix` | `2ecf48b27f3fab6050de2f39d7048e024b9545a50c30cc666e326d05135016bf` |
| `windows/TorrentFree_1.25.1.0_arm64.msix` | `1bce8f49ae60adc88d78f731365b18fddba709057c8ff6329a02fd90ca9691b1` |
| `android/play/com.torrentfree.app-v1.25.1-code31.aab` | `6ba1b13e4c28dd5c68e2f0f097834a81e317ab4c0207cda03376973c466c13bd` |
| `android/galaxy/com.torrentfree.app.galaxy-v1.25.1-code31.apk` | `4f2c417cd90c5f3bcbb7bf90707e346fe9bfe85dd1561355c11d8d028a56172a` |
| `android/huawei/com.torrentfree.app.huawei-v1.25.1-code31.apk` | `1718145b122a60878c802f748dde203ee56ebb82d379430c9773076b48620388` |

## Store status

Submission pending final CI verification. No 1.25.1 package has been submitted yet.
