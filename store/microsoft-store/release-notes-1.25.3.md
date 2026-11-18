# TorrentFree 1.25.3

Windows package 1.25.3.0 for x64 and ARM64. Google Play version 1.25.3, build 33.

Stopping a torrent immediately after starting or restarting it now stops the engine and saves the stopped state. Clean stops retain fast-resume data. After an unclean exit the engine can recheck downloaded files before continuing.

This release includes the held 1.25.2 changes that protect existing files, isolate download destinations, improve recovery and bind proxy passwords to their server. The donation option is available on Windows only.

## Package qualification

The packages were built from commit `9a7c6452ef4076b3d334568d23c9c1abde185e0a`, tagged `v1.25.3`. All 561 .NET Release tests and 11 store-tool tests passed. The three immediate-stop regression cases also passed across 60 fresh Windows test processes (180 executions). [Merged fix and CI](https://github.com/kirakosyan/torrent-free/pull/63)

| Package | SHA-256 |
| --- | --- |
| TorrentFree_1.25.3.0_x64.msix | `a01053782a4b89d0550b8c671e45649e0a8429890c8df7485c4bf5986dd721b3` |
| TorrentFree_1.25.3.0_arm64.msix | `1f965c24b527c8d395eaef5e8e092ce1eacf4df0cd6129e2fb74af807ca743ae` |
| com.torrentfree.app-v1.25.3-code33.aab | `0f62b8e2f19c777d522afe9b3f5927a1dc1a4c98dff14d7124763d08f46dd449` |

The packaged Windows x64 app recovered from a malformed file activation, opened a valid private torrent and reached seeding with an existing 512 MiB payload. It stopped in all three rapid restart–stop cycles and remained stopped after a cold restart. Removing the torrent with file deletion selected preserved the original payload byte for byte. About displayed build 33 and the donation button. No application crash events occurred, and all 20 original profile files matched their saved hashes after restoration.

Both Windows packages passed manifest, architecture, payload and MakeAppx checks. The signed Play bundle passed signature, manifest and bundle validation. Its upload certificate matched the previous release, the R8 map matched the retained map, and the native symbols matched both packaged libraries by build ID. The Android release assemblies exclude the donation control, handler and Patreon URL. Runtime smoke testing covered Windows x64. Windows ARM64 and Android received package validation only.

## Store submissions on November 18, 2026

| Store | Submission | Verified status at submission |
| --- | --- | --- |
| Microsoft Store | 23 (`1152921505702441783`) | In certification, with automatic publication after approval. |
| Google Play | 26, production | In review at 20:22 Europe/Oslo. Full rollout in all targeted countries with managed publishing off. |

Microsoft validated both replacement packages. Xbox is excluded from distribution, so the Xbox toast-activation warning does not affect the selected device families. A fresh export of all 454 listing rows confirmed that only the release notes changed in the 31 locale columns covering 24 languages.

Google accepted the signed build, the embedded R8 mapping, matching native debug symbols and all 26 localized release notes. The release removes Android donations to address the Payments policy notice. The app-level trader-status declaration remains outstanding.
