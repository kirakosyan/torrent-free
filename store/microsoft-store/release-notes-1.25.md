# Version 1.25 preparation record

Prepared November 15, 2026 (Europe/Oslo). **Not uploaded, submitted, or published.**
Superseded before submission by [1.25.1](release-notes-1.25.1.md) after Windows
smoke testing found a stale file-import error message. Original packages and tag
are retained unchanged; do not submit these superseded packages.
Windows **1.25.0.0**, x64 and ARM64; Android **1.25**, version code **30**.
No store submission or release tag has been pushed.

## Changes

- Recover legacy public magnets after metadata cache loss while retaining private/file-import safeguards.
- Share in-flight torrent stops and cancel monitoring before seed-limit teardown. Completed seeds stay completed during Start All.
- Retain proxy drafts until Apply, recover failed settings loads, and improve localized validation and numeric input.
- Keep incomplete progress below 100% in both the badge and status line.
- Prepare Android exports offline with cancellation, selected-file recovery and a full-export storage check before copying.
- Use monochrome foreground and completion notification icons. Use Browse for generic/untyped files without claiming unrelated files in Open with.
- Add local Windows exception/stall diagnostics and defer activation work to reduce reentrancy. Routine sessions do not displace crash evidence.

See [the investigation and issue analysis](../../docs/reliability-update.md).
The Store activation bucket was not reproduced: this is a targeted mitigation,
not a proven fix of that production crash. Android device/store handoffs remain
to be qualified; #53 is partially addressed through the explicit Browse fallback.

## Package preparation

| Platform | Identity | Format | Architectures |
| --- | --- | --- | --- |
| Microsoft Store | `9971ArmenKirakosyan.TorrentClientApp` | unsigned Store-upload MSIX | x64, ARM64 |
| Google Play | `com.torrentfree.app` | upload-key-signed AAB | ARM64, x86_64 |
| Galaxy Store | `com.torrentfree.app.galaxy` | signed APK | ARM64, x86_64 |
| Huawei AppGallery | `com.torrentfree.app.huawei` | signed APK | ARM64 |

Android minimum API 23 and target API 36 are retained. Microsoft signs the MSIX
after certification; these unsigned packages are intended for Store upload.
Packages, mapping files, matching native symbols, validation logs and SHA-256 hashes
are retained in ignored `artifacts/store-1.25/`.

## Validation and traceability

Before packaging: **539 Release core tests** and **10 store-tool tests** passed.
Release notes cover all **24 Microsoft** and **26 Android** locales; Android
notes are within the 500-character limit. Version metadata is aligned across
the project, Windows manifest and version consistency test.

The packages were built from clean commit `6bd2bc6f0b2650ef941484fd31e363207144b5d2` on
`codex/prepare-1.25`, based on merged main commit
`839cab64375df6553b1001994d690f1fcf9f7b3e`. Annotated tag **v1.25** identifies that
exact package source. The branch, commits and tag remain **local and unpushed**.
This record is a documentation-only follow-up to the package source commit.

- Both MSIX packages passed MakeAppx unpack, ZIP, Store identity, version,
  architecture, all-24-language and activation-extension checks. Packaged assemblies
  match the build outputs and report `1.25+6bd2bc6f0b2650ef941484fd31e363207144b5d2`.
- The signed Play AAB passed bundletool and JAR signature verification. Its R8 mapping
  matches the embedded mapping. Both APKs passed v2/v3 signature checks and 16 KiB
  ZIP alignment checks. All Android manifests are non-debuggable, use code 30/version
  1.25, and retain API 23 minimum/API 36 target with the expected identities and ABIs.
- Android signing certificate SHA-256 is
  `de280a386bd8397849fd3da981748f1d906956cde996e919016c96f7e8119cae`, matching
  the retained Play 1.24 and Huawei 1.23 packages. No signing key was generated or changed.
- Native symbol build IDs match each packaged `libxamarin-app.so`; the symbol ZIPs
  use the required ABI folders and library names. R8 mapping and Windows managed
  PDBs are retained alongside the packages. Play AAB and mapping fallbacks are in `C:/temp`.
- Builds retain the existing XC0025 XAML binding warning. Windows also reports the
  optional missing `mspdbcmf.exe` tool; no `.appxsym` container was generated.
  The matching managed PDBs are provided separately.
- Subsequent x64 Windows smoke testing passed startup, settings, magnet activation,
  file selection, pause/stop and shutdown, but found a stale import error after a
  valid file. See the 1.25.1 record for the fix and follow-up qualification.
  This does not establish Store delivery, Android device behavior, or a fix of
  the original production activation crash.

## Artifact hashes

All paths below are relative to `artifacts/store-1.25/`. `validation.json` contains
the manifest, signing and native build-ID evidence. `SHA256SUMS.txt` also covers
all mapping and symbol files. The portable `TorrentFree-1.25-build30-packages.zip`
contains the five packages, mapping/symbol files, localized notes and validation
record; it excludes signing credentials and private build logs.

| Package | SHA-256 |
| --- | --- |
| `windows/TorrentFree_1.25.0.0_x64.msix` | `8a5caddfc983ad87b918eaf1800a075057f8cc55a8637f9e0d70f22b676f0e63` |
| `windows/TorrentFree_1.25.0.0_arm64.msix` | `3cda23e585d3b343070749fb9541a9429901093a3f486b53d2d1c97e98f2dd7a` |
| `android/play/com.torrentfree.app-v1.25-code30.aab` | `909528673a1d8429c66a482468563ad188d847d977eed9e0ced146af79720c3a` |
| `android/galaxy/com.torrentfree.app.galaxy-v1.25-code30.apk` | `029585227f07f494b67d1846aadf46f5cf039233ef5fac5985e2aa8bd8a59071` |
| `android/huawei/com.torrentfree.app.huawei-v1.25-code30.apk` | `459be62d64c16c488443e73678e7d58699c5ec89c5009ea9b8355bdcc7dc5bbf` |

Store submission identifiers: **none**. All four store variants remain unsubmitted and unpublished.
