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
  Final release package validation is pending.
- Device sleep/lid behavior and packaged notification activation have not been
  exercised end to end in this release preparation.
- Localized notes: 24 Windows app languages, mapped to 31 existing Store listings;
  26 Play languages. Existing availability, descriptions and screenshots are preserved.

## Package provenance

- Planned annotated tag: **v1.18**, on the exact clean source commit used to build
  the final store packages. Source commit and package hashes will be recorded after building.
- Retain final packages, mapping and matching native debug symbols under the
  ignored `artifacts/store-1.18/` directory, with upload copies under `C:\temp`.
- Signing material remains outside Git under `C:\temp\torrent-android`.

## Store status

As of October 1, 2026, version **1.18 has not been submitted or published**.

| Store | Target package | Status | Submission/publication record |
|-------|----------------|--------|-------------------------------|
| Microsoft Store | 1.18.0.0 x64 + ARM64 | Preparing packages | No submission yet |
| Google Play | 1.18, code 23 | Preparing signed AAB and symbols | No submission yet |

Version 1.17 remains live in both stores. Play Console reports its publication on
September 30, 2026 and 100% rollout. Microsoft submission 17
(`1152921505702004251`) is live; its exact publication date has not been verified.
Both statuses were checked in the store consoles on October 1, 2026.

## Certification notes

No account or test credentials are required. The Windows `runFullTrust` capability
is required for the .NET MAUI desktop torrent client. Use openly licensed torrent
content such as Debian images when testing.
