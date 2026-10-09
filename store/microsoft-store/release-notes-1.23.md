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
- Signed Google Play AAB and Huawei APK passed manifest, ZIP, signing-certificate
  and matching native-symbol build-ID checks. Both declare minimum API 23 and
  target API 36. Play bundletool validation passed; ReTrace mapping and native
  symbols were accepted in Play Console. Huawei's signing certificate matches 1.20.
- Windows x64 and ARM64 MSIX identities, versions, all 24 languages, payloads and
  toast/COM activation declarations passed local validation. Partner Center
  accepted both packages. Its Xbox toast warning is irrelevant because Xbox
  availability remains unchecked.
- Release builds succeeded with existing XAML Source-binding optimization and
  optional Windows symbol-tool warnings. Windows symbol packages were not generated.
- Google Play's preview reports zero newly unsupported devices across all form
  factors, including the same 8,973 phone and 4,881 tablet models.
- All 31 Windows notes matched a fresh export; every other listing field, image,
  caption, asset order and logo override was unchanged. All 26 Play and Huawei
  release-note translations were saved, preserving Huawei's five screenshots
  and icon per language.
- GitHub CI [37840700527](https://github.com/kirakosyan/torrent-free/actions/runs/37840700527):
  Windows, Play, Huawei and Galaxy builds and Linux tests passed. Windows tests
  passed 427/428 on both the initial run and retry, failing the pre-existing
  DownloadPath_UsesAndPersistsEngineEscaping test with a MonoTorrent fastresume
  file-lock IOException during StopAsync. This was also observed in the 1.20
  release; it remains unresolved and CI is not green. All 428 tests passed locally.
  Both failed logs are retained with the release artifacts.
- iOS and Mac Catalyst are not part of this release.

## Release traceability

Built from clean commit [`69a533b1e783d5d6eae4976b0e988c5006ef790a`](https://github.com/kirakosyan/torrent-free/commit/69a533b1e783d5d6eae4976b0e988c5006ef790a),
annotated tag **v1.23**, committed and pushed before building the final packages.
PR [#24](https://github.com/kirakosyan/torrent-free/pull/24) was squash-merged into
`main` at `0c1128937dd0c963a939a4acd7072e7377ea0d2b`.

Display version **1.23**, Android code **28**, Windows **1.23.0.0** (**x64/ARM64**).
Google Play has **arm64-v8a/x86_64**; Huawei is **arm64-v8a** only.
Packages, R8 mappings, matching native symbols, hashes and console evidence are
retained under ignored `artifacts/store-1.23/`. Huawei also retains its private
build logs and output under the existing private Apps release folder.

| Artifact | SHA-256 |
|---|---|
| com.torrentfree.app-v1.23-code28-google-play-upload-key.aab | `b43748de18281650e4429eaf955feb54e5e17c73cc03c8f559f069044e6eb95c` |
| mapping.txt | `05466314c1e149affb7f896c3c7b8de9b79fbdbea43586cef9abed95cdbb5803` |
| com.torrentfree.app-v1.23-code28-native-debug-symbols.zip | `197e88fca1fb744d44caba9eb3b45cbb8d1e56284179720576ce2a3e8936812e` |
| huawei/com.torrentfree.app.huawei-v1.23-code28.apk | `8129ffd15fa104752fed343b45fb8b037feaa7f658332d7487decb067d5ec4c4` |
| huawei/mapping.txt | `66d498d79a1b63ff6106699f67e3529ccde95fdb699028ae7e7c01ea580a4080` |
| huawei/native-symbols.zip | `97ac93ea6e51635de5629e8b110040631dcb5bfa732d7854c44727f3efca4472` |
| TorrentFree_1.23.0.0_x64.msix | `78d0f17f3ca6860d463c2b042ea9f9e490cf0420ba87f86d87f392ad2ea6d7ce` |
| TorrentFree_1.23.0.0_arm64.msix | `1e876c8e25375dc5225ad3ee679cc43cde5a7b4436b86efa43cac2cb0bd1b6e5` |

## Store status

All three stores were initially submitted on **October 8, 2026**. Microsoft
was corrected and resubmitted on **October 9**, as detailed below. The table
records each store's last verified state; pending does not mean live. Automatic
publication after approval is enabled in all three consoles.

| Store | Submission | Verified status and time (Europe/Oslo) |
|---|---|---|
| Google Play | **22**, production release **12**, code **28** | **In review**, **October 8, 22:47** (20:47 UTC). Initial automated checks were still running. |
| Microsoft Store | **20**, `1152921505702078202` | **Resubmitted, In certification**, **October 9, 09:36**. |
| Huawei AppGallery | App **119226643**, version **2057049026322245120** | **Reviewing**, **October 8, 22:48** (20:48 UTC). |

Google Play: full rollout across all **178** existing targeted countries/regions,
managed publishing off, all 26 release notes and both debug attachments accepted.
Huawei: full release to the inherited **198** selected countries/regions;
mainland China remains excluded. No login, purchases, collection or AI declaration
was changed. The versioned privacy URL now points to **v1.23**.
Microsoft: Desktop and Windows Team availability and existing mandatory-update
settings preserved, with publication as soon as certification passes.

Previous live releases verified during submission were **1.19** on Google Play
and Microsoft Store, and **1.20** on Huawei. The Android 7-only Google Play 1.22
draft was replaced before submission; its tag and artifacts remain unchanged.

## Microsoft listing rejection and resubmission: October 9, 2026

Certification report [`59ef343b-1cbd-4ef8-86e4-3f84ad58a3d4`](https://partner.microsoft.com/en-us/dashboard/products/9NNX2ZTPXC26/certification/reports/59ef343b-1cbd-4ef8-86e4-3f84ad58a3d4)
shows review completed **October 8** and **Attention needed**, with one issue:
**10.1.3 Search Terms**, language **GLOBAL**, problematic keyword **torrent free**.
The retained legacy keyword came from `store/tools/ms_listing_import.py` and was
present in all 31 localized listings. No package issue was listed in the report.

- Replaced that search term with **file download** in all 31 languages and changed
  the legacy sort title to the current **Torrent Client App** title.
- Updated the import generator, Microsoft Store README and `.codex/AGENTS.md` to
  prevent future imports from restoring the rejected metadata.
- Used a fresh export, preserving every other non-asset text field and the logo
  override flag; unchanged asset and caption rows were excluded from the import.
- Verified the newly downloaded export against the expected result: **62 intended
  cell changes across 31 languages**, with every other field identical, including
  release notes, screenshots, captions, asset order and logo settings. Generator
  output matches the corrected search terms; every locale has at most seven
  distinct terms, each within 30 characters.
- Added the correction to Notes for Certification, retaining existing test steps.
- Resubmitted submission **20** (`1152921505702078202`) on **October 9, 2026**;
  **In certification** verified **09:36 Europe/Oslo**, with automatic
  publication after approval. Version 1.23 is **not live yet** on Microsoft Store.

No application code or submitted package changed. The existing **1.23.0.0
x64/ARM64** packages, SHA-256 hashes and immutable **v1.23** source tag above
remain the release artifacts. No app rebuild or new app tests were needed for
this metadata correction. The previously recorded CI limitation remains unchanged.
Before/import/expected/verified CSVs, the correction audit and report/confirmation
screenshots are retained under ignored `artifacts/store-1.23/` with the
`listing-keywords-*` and `microsoft-*-2026-10-09` names.
