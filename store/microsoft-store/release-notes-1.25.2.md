# TorrentFree 1.25.2

Windows package 1.25.2.0 for x64 and ARM64. Google Play version 1.25.2, build 32.

- Removing a torrent protects files that were already present and downloads whose ownership cannot be verified.
- Downloads avoid overlapping destinations and recover from missing files and interrupted magnet discovery.
- Proxy credentials are tied to the configured server. Restoring settings can prompt for the password again.
- The donation option is available on Windows only.

## Package qualification

Built from commit `adadd31f1c5d66e250bd5242479e76b4d0fcb104` and annotated tag `v1.25.2`. All 558 Release .NET tests and 11 store-tool tests passed.

| Package | SHA-256 |
| --- | --- |
| TorrentFree_1.25.2.0_x64.msix | `e680497aa756344f93c24ab088d4cb04bfcee4468a9ba0e6cdfaa12458daab88` |
| TorrentFree_1.25.2.0_arm64.msix | `56ccbc0401fcf48a6852d1662b6eb3b0503dccc6107a664340d32378545876d9` |
| com.torrentfree.app-v1.25.2-code32.aab | `52347b06bf083235b8df9b8d298298233defc3aa5a55c15cc32b01901461150b` |

The packaged x64 application was registered under a temporary Windows identity. It handled a malformed torrent, accepted a valid private torrent through file activation, verified existing content, reached seeding, and retained its paused state across a cold restart. With file deletion selected, removal emptied the queue and preserved the pre-existing file byte for byte. About displayed version 1.25.2, build 32 and the Windows donation button. The original profile was restored and checked against its file hashes. No application crash events were recorded.

Both MSIX packages passed MakeAppx, manifest, architecture and payload checks. The Play bundle passed bundletool validation. Its upload certificate matches the previous release, its embedded R8 map matches the retained map, and both native symbol files match the libraries by build ID. The signed bundle targets API 36 with minimum API 23. The Android release assemblies exclude the Windows donation control, handler and Patreon URL. Runtime testing covered Windows x64 with a local private fixture. ARM64 Windows and Android were package-validated.

## Store submissions on November 18, 2026

| Store | Submission | Status |
| --- | --- | --- |
| Microsoft Store | 23 (`1152921505702441783`) | In certification. Partner Center will publish after approval. |
| Google Play | 25, production release 15 | In review. Submitted at 19:13 Europe/Oslo for a 100% rollout in all targeted countries with managed publishing off. |

Microsoft accepted both packages. It flagged toast activation as unsupported on Xbox, which is excluded from the submission. Comparing all 454 rows of the before and after listing exports confirmed that only the release notes changed across 31 locale columns covering 24 languages.

Google accepted the signed bundle, R8 mapping, native debug symbols and release notes for 26 languages. The Android donation removal addresses the existing Payments policy notice. Play allowed submission with the app-level trader-status declaration deferred. Google determines whether the update resolves the notice during review.
