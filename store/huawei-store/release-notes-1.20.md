# Version 1.20: Huawei AppGallery

Display version **1.20**, Android code **25**, Huawei identity
**com.torrentfree.app.huawei**, architectures **arm64-v8a** and **x86_64**.
Shared Windows metadata is aligned to **1.20.0.0**; no Windows or Google Play
1.20 release is included in this submission.

## Changes

- Recognize Huawei AppGallery installations and route ratings/listing handoffs to it.
- Leave update availability to AppGallery; never call Play's update API for a
  Huawei installation. Include the installer store in the cached version identity.
- Add an APK build identity that can reuse the existing private signing key
  without conflicting with Google's signing certificate.
- Reuse the existing Play listing in all 26 languages and all five screenshots.

## Traceability and status

The signed APK was built from clean commit
[`ca0d43a8601383810fc414d4f558496b57912e6b`](https://github.com/kirakosyan/torrent-free/commit/ca0d43a8601383810fc414d4f558496b57912e6b),
annotated tag **v1.20**. The tag and source were pushed on October 5, 2026.
All **401 Release unit tests** pass, including 14 Android routing cases.
The signed Huawei Release publish passed. APK signatures v1/v2/v3 and the retained
signing certificate were verified; the manifest confirms version 1.20, code 25,
minimum API 23, target API 36 and both ABIs. Native symbol build IDs match both
packaged libraries. ZIP integrity passed. Standard Release evaluation retains
the Play package/AAB, and selecting two store build flags fails explicitly.
The APK's launcher label is **Torrent Client** (`@string/app_name`); the longer
English store listing title does not replace that Android label.
Both source CI runs passed their Windows/Linux tests and Android/Galaxy/Windows
builds. One Windows test run hit an existing torrent resume-file lock and passed
on retry; the other Windows run passed initially.

| Artifact | SHA-256 |
|---|---|
| Signed Huawei APK | `895f7974ac9a5819f11babf8d6d0797009a2b2e8cde5be7974832c4c8c9b32ab` |
| R8 mapping | `05466314c1e149affb7f896c3c7b8de9b79fbdbea43586cef9abed95cdbb5803` |
| Native symbols ZIP | `8f9ff3f173c92945397239fa23b993f8b1d13ea9abde3f09e2931fa9e6e5e35d` |

No Android device was connected. AppGallery-delivered installation, rating
handoff and a later store update still require real-device validation.

Submitted on **October 5, 2026**, with status **Reviewing** verified at **17:27
Europe/Oslo (15:27 UTC)**. The console confirms the app is submitted and pending
review. It is **not live yet**. Publication is set to **Immediately once approved**.

- Huawei app ID: **119226643**.
- Version/submission record: **2054699080516380160**.
- Package: **com.torrentfree.app.huawei**, **1.20 (25)**, 33.28 MB in the portal.
- Distribution: **199 available countries/regions**, excluding mainland China;
  new countries/regions enabled. Final package uploaded at **17:23:43 Europe/Oslo**.
- All **26** Play translations, the icon and the same **five screenshots per
  language** were saved and passed portal validation; default **English (UK)**.
- Category: **Tools / Tools**, mobile phone; **Free**, no in-app purchases.
- Rating: **3+**, Brazil **All ages**; not intended only for children.
- No sign-in, developer personal-data collection or generative AI declared.
  Privacy policy points to the versioned public policy at tag **v1.20**; support
  and privacy rights use the public issue tracker. BitTorrent network exchanges
  are explained in that policy. Reviewer notes include a legal Debian torrent
  workflow and the AppGallery rating/update behavior.

The portal required a repeat upload after the region selection changed the
distribution entity. Both uploads used the exact APK/hash above. No replacement
package was built. Submission evidence, private build helper, signing references,
assets and diagnostics are retained in the private Apps folder.

No production key or package is stored in the public repository.

## Changes after submission

**Publication verified October 8, 2026:** AppGallery shows **1.20 Released**.
The approved review used EMUI 12.0.0 (P30 Pro) and EMUI 10.1.0 (P40 Pro) in a
multilingual environment. This supersedes the pending status recorded above at
submission; the exact publication time was not verified.

PR review fixes made after the `v1.20` source tag are for the **next release**.
They do not change the submitted APK, hash, tag or historical validation above.
A replacement store package must have a new version/code and release tag.

- Replace the store booleans with `AndroidStore=Play|Galaxy|Huawei`; reject legacy
  flags and unknown choices before restore/build. CI includes a Huawei compile
  and real MSBuild checks of identities, formats, ARM64 selection and validation.
- Set future Huawei APKs to ARM64 only. The original v1.20 APK contains ARM64 and
  x86_64; neither ABI policy supports 32-bit-only devices. Remove the ineffective
  Huawei title override while retaining the launcher label verified above.
- Keep Huawei's officially documented native details link, remove the unverified
  web fallback, and use a cooldown for unconfirmed Android store handoffs. Preserve
  existing opt-outs. Real-device AppGallery handoff validation remains outstanding.
- Share listing validation, asset copying/hashing and bundle writing between
  Galaxy and Huawei; both tools enforce output outside the repository.
- Keep verified review/publication status in this record; READMEs link here.

Follow-up validation: **401 Release core tests**, including **46 targeted
routing/prompt cases**, **five listing-preparation tests**, and **four MSBuild
integration tests** passed. The Huawei Debug build succeeded with zero warnings
and errors. Its APK manifest confirms `com.torrentfree.app.huawei`, launcher label
`Torrent Client` and **arm64-v8a only**. This is a local development build, not a
replacement submission.
