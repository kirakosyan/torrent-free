# Version 1.19 release record

Display version **1.19**, Android version code **24**, Windows package version
**1.19.0.0** for **x64** and **ARM64**.

## Changes

- Fix removal of selected downloads when another completed torrent in the same
  folder has missing or unreadable metadata. Protect positively identified shared
  files instead of blocking the entire download folder. This fixes both Windows
  and Android because deletion is implemented in the shared Core service.
- Apply the same file-level ownership check when deleting only the source torrent.
  Known shared payloads and source metadata remain protected. The selected torrent
  still needs verified metadata or its manager file list to delete payloads; no
  files or directories are guessed from display names.

## Validation

- The regression tests failed on the previous implementation before the fix.
- All **24 removal safety tests** and the complete **386-test Release suite** pass.
- Coverage includes persisted completed legacy entries, missing/corrupt metadata,
  active/stopped selected downloads, source-only removal, and genuinely shared files.
- Platform packaging and store submissions are pending.

## Release traceability

Final packages will be built from the clean committed source tagged **v1.19**.
Packages, mapping, symbols and verification evidence will be retained under the
ignored `artifacts/store-1.19/` directory. Source commit, package hashes and store
submission identifiers will be recorded after building and submitting.

## Store status

Version 1.19 is in preparation on October 2, 2026 (Europe/Oslo); it has not yet
been submitted or published to either store.
