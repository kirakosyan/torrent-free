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
- Signed Android Release publish and Windows x64/ARM64 Release publishes passed.
- Bundletool validation passed; Android version name is **1.19**, code **24**.
  JAR signature verification passed and the upload certificate matches the
  registered certificate. Both ABI symbol files match the packaged ELF build IDs.
- Both Windows MSIX packages passed ZIP integrity, Store identity, version,
  architecture, all 24 languages, Core payload and toast/COM registration checks.
  The optional Windows symbol tool was unavailable; no symbol package was requested.
- All four checks in [GitHub Actions run 37058823577](https://github.com/kirakosyan/torrent-free/actions/runs/37058823577)
  passed: Windows/Linux Core tests and Windows/Android app builds.
- No physical Android device or Windows Store installation upgrade was exercised.

## Release traceability

All final packages were built from clean commit
[`112eed651ff35798def0129325cc2b62e65425da`](https://github.com/kirakosyan/torrent-free/commit/112eed651ff35798def0129325cc2b62e65425da),
with the annotated [**v1.19** tag](https://github.com/kirakosyan/torrent-free/releases/tag/v1.19).
The commit and tag were pushed on October 2, 2026. Packages, mapping, symbols and
verification evidence are retained under the ignored `artifacts/store-1.19/`
directory, with upload copies under `C:\temp`.

| Artifact | SHA-256 |
|----------|---------|
| Android signed AAB, code 24 | `6437992512afab0c46c861a316f99266b8921846079029ce4a11e275b75792ba` |
| Android R8 mapping | `05466314c1e149affb7f896c3c7b8de9b79fbdbea43586cef9abed95cdbb5803` |
| Android native symbols ZIP | `89f8ab64fd75f124dfeb3d5477581033f8b41288dba46440615855d89f9a4223` |
| Windows x64 MSIX | `d9562694d5e5b6d60f6e14ee11d8120d0fb0b792628b9fe5916d809cb4b79532` |
| Windows ARM64 MSIX | `b1d12ec7ccd2372a4f7485e1e477fb17ab83af78971d23dd61613b64de605e11` |

## Store status

Version 1.19 has **not been submitted or published** to either store as of this
October 2, 2026 (Europe/Oslo) release attempt.

- Microsoft submission **19**, `1152921505702031669`, exists as a draft. A
  notes-only CSV import started for all 31 languages; the last observed progress
  was saving 11 of 31. Final completion was not verified. The package chooser
  failed before either new MSIX was uploaded. Verify saved notes through a fresh
  export, upload the retained x64 and ARM64 packages, update tester notes, and
  submit for certification. Existing descriptions and screenshots were not edited.
- Google Play: production draft opened for **1.19 / code 24**, with release notes
  entered for all 26 languages. AAB upload started; the last observed transfer
  was 2.97 MB of 35.8 MB. Completion and draft persistence were not verified after
  browser automation stopped. No symbols were uploaded and review was not requested.
  Inspect the draft and artifact library before retrying the upload. Use the
  retained AAB and matching native-symbol ZIP; do not rebuild under this tag.
- Both stores were verified to have **1.18 live**. Google Play production showed
  100% rollout across the existing 178 countries/regions, managed publishing off,
  and last publication October 2, 2026.
- Submission was blocked when Computer Use ended because it could not determine
  the current browser URL confidently enough to enforce policy. No further UI
  actions were performed after that stop.
