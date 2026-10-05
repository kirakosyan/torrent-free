# Galaxy Store preparation

Status on **October 4, 2026 (Europe/Oslo): prepared locally, not registered,
submitted, or published**. No production Galaxy APK, new release version or tag has
been issued. This work does not change the submitted Google Play 1.19 / code 24
or Microsoft Store 1.19.0.0 releases.

## Publishing prerequisites

Seller Portal currently requires **Corporate Commercial Distribution Seller**
status for Android registration, including free apps. A private/free account is
blocked before the app form. Complete Samsung's business verification process
before resuming registration. See [Samsung's seller preparation guide](https://developer.samsung.com/galaxy-store/prepare.html).

Samsung also requires Android Developer Verification approval for the package and
signing certificate. Register the Galaxy package/certificate with Google and
verify that Seller Portal reports the uploaded binary as installable before
submission. See [Samsung's ADV notice](https://seller.samsungapps.com/notice/getNoticeDetail.as?csNoticeID=0000011990).

The remaining store form includes category, content rating, privacy/support URLs,
countries, review instructions and publication settings. Reuse the Play support
and privacy details after checking the live console. Complete Samsung's own rating
questionnaire; Play's age rating does not automatically answer it. Test actual
Samsung-store installation, review handoff and a subsequent update before release.

## Package and signing

Build with `-p:GalaxyStoreBuild=true -p:AppTargetFramework=net10.0-android` to use
`com.torrentfree.app.galaxy` and APK output. Ordinary builds retain
`com.torrentfree.app` and their existing format. Review/update links use the running
package name. The identities install independently and do not share app data.

A Play **upload** key is not necessarily the key used to sign Play-delivered apps.
The Galaxy identity allows reuse of an existing private signing key without
conflicting with the Google-signed application. Keep that key for every Galaxy
update. Do not upload the keystore as an app binary. See
[Samsung's cross-store update guidance](https://developer.samsung.com/galaxy-store/cross-store-updates.html).

Signing keys, password files, seller details and generated packages belong outside
Git. Local signing notes and assets can be kept in the private Apps folder.
Before producing a submission package, follow `.codex/AGENTS.md`: increment the
release/build as appropriate, commit release notes and version metadata, build
from a clean tagged commit, and retain hashes, mapping and native symbols.

## Listing assets

Run from the repository root, using a local output folder outside Git:

```powershell
rtk proxy python store/galaxy-store/prepare_submission.py --output <local-folder>
```

This copies the five existing Play phone screenshots (1440x2560) and 512x512 icon,
and prepares 24 Galaxy listing languages from the 26 Play locales. Samsung has no
Hindi listing choice, so Hindi readers use the English default; the app retains
its Hindi UI. The identical Spanish variants map to one Spanish listing. Ukrainian
is a listing translation only, as on Play. Other screenshots keep the same English
captions as Play. The utility checks UTF-8 byte limits, PNG dimensions and hashes.

`listing-bundle.json` is a preparation record, not an API upload body. Text files
under `listings/<Samsung-language>/` are ready for the portal. It does not upload
anything or duplicate signing files. Samsung's current specification allows 4–8
screenshots, 320–3840 pixels, up to a 2:1 ratio; the icon must be 512x512 PNG and at
most 1024 KB. See [listing parameters and languages](https://developer.samsung.com/galaxy-store/galaxy-store-developer-api/content-publish-api/reference.html).

## Rating and updates

The Android installer determines the store. Play installs retain Play update
checks and review links. Galaxy installs open Samsung's rating page and leave
updates to Galaxy Store; in-app availability is unknown and no Play update banner
is shown. Unknown installers and sideloads suppress store prompts. The cache
identity includes the store, preventing stale Play availability from surviving a
store change. See [store prompt behavior and device checks](../../docs/store-prompts.md).

## Preparation validation

All 398 Release unit tests passed. Standard Android, Galaxy Android and Windows
ARM64 Release builds passed with zero warnings and errors. The Galaxy APK manifest
was checked for `com.torrentfree.app.galaxy`, code 24, display version 1.19,
minimum API 23, target API 36 and arm64-v8a/x86_64 libraries. This was a local
validation build, not a production-signed submission. All six copied asset hashes
match their source files. Real store/device handoffs remain to be tested after
seller eligibility is resolved.
