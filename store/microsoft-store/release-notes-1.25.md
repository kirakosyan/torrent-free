# Version 1.25 preparation record

Prepared November 15, 2026 (Europe/Oslo). **Not uploaded, submitted, or published.**
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
will be retained in ignored `artifacts/store-1.25/`.

## Validation and traceability

Before packaging: **539 Release core tests** and **10 store-tool tests** passed.
Release notes cover all **24 Microsoft** and **26 Android** locales; Android
notes are within the 500-character limit. Version metadata is aligned across
the project, Windows manifest and version consistency test.

The source is prepared on `codex/prepare-1.25` from merged PR #61, main commit
`839cab64375df6553b1001994d690f1fcf9f7b3e`. The version and notes are committed before
final packaging. The exact package commit, local annotated tag, validation results
and artifact hashes will be recorded after the builds complete.
