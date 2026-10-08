# Huawei AppGallery

Huawei builds use `-p:AndroidStore=Huawei -p:AppTargetFramework=net10.0-android`.
This produces an APK with identity `com.torrentfree.app.huawei`.
It installs separately from the Play and Galaxy variants.
`AndroidStore` accepts only `Play` (default), `Galaxy` or `Huawei`; the previous
boolean flags are rejected before restore/build with a migration message.

Future Huawei packages explicitly target **ARM64** (`android-arm64`). The single
APK omits emulator x86_64 libraries and does not support 32-bit-only devices.
AppGallery runs on non-Huawei Android devices too; this choice limits the package
to ARM64 devices. The submitted v1.20 APK retains its original ARM64/x86_64 set;
use a new release/code for a changed package. CI compiles the Huawei variant and
evaluates all store identities, formats, ABI policy and invalid selections.

The launcher label is **Torrent Client**, from Android `@string/app_name`, as
verified in the submitted APK. **Torrent Client - Downloader** is the English
store listing title. The unused Huawei `ApplicationTitle` override was removed.

## Signing and updates

Reuse the existing private Android signing key and retain it for every Huawei
update. A Google Play upload key can sign an AppGallery APK, but differs from the
certificate Google uses for Play-delivered apps; the separate package avoids
cross-store signature conflicts. Keep keystores, aliases, passwords, generated
packages and account evidence outside Git. The private Apps folder contains the
reusable build helper and release artifacts. AppGallery receives only the signed
APK, never the keystore or password files.

AppGallery installs are recognized by installer `com.huawei.appmarket`. Ratings
and listing handoffs use the native `appmarket://details?id=<running-package>`
format documented by Huawei. The unverified HTTPS fallback was removed from
current source; a failed native launch keeps the rating action retryable.
An unconfirmed store handoff uses the normal cooldown, without asserting a
submitted review or permanently disabling requests. Real-device verification
is still required. These review changes are not in the submitted v1.20 APK.

AppGallery manages updates externally. The app returns unknown update
availability, has no AppGallery update banner/action in the UI, and does not
call Google Play's update API. Sideloads continue to suppress store prompts.
See [store prompt checks](../../docs/store-prompts.md).

## Listing assets

```powershell
rtk proxy python store/huawei-store/prepare_submission.py --output <private-folder>
```

The bundle reuses all 26 Play listing translations and five 1440x2560 phone
screenshots, plus the 512x512 icon. All languages map directly to choices verified
in AppGallery on October 5, 2026. The portal describes image inheritance, but its
Next-step validation required explicit graphics for each configured translation.
The same English icon and five screenshots were therefore uploaded per language.
The utility checks text/image limits shown in the portal and records image hashes.
It refuses output inside the repository and does not upload anything.

The portal requires 3-8 screenshots, each 320-3840 pixels per side and at most
5 MB for PNG/JPEG. PNG icons can be 216x216 or 512x512 and at most 2 MB.
Full introductions allow 8000 characters; brief introductions allow 80; names 30.
The app itself retains its 24 UI languages. Ukrainian remains listing-only.

## Release

**1.22 / code 27** is being prepared on October 8, 2026, aligned with Google Play
and Microsoft Store. It includes torrent file selection and the Huawei follow-up
fixes below. See the [1.22 release record](release-notes-1.22.md) for current status.


See the [1.20 release record](release-notes-1.20.md#traceability-and-status) for
the sole verified review/publication status and its timestamp. That submission
selected all 199 available regions outside mainland China and new regions.
The completed questionnaire yielded
3+ (Brazil: All ages); the app is not intended only for children. Privacy URLs,
free pricing, no login/purchases/generative AI and reviewer instructions are saved.

Set release regions before uploading an APK. Removing mainland China changes
Huawei's distribution entity and requires uploading the APK again. The final
submission uses the same verified APK under the final region selection.
Test a store-delivered installation, rating handoff and later update on a
real device. Android APK support does not imply native HarmonyOS NEXT support.

References:
- [Huawei Developers: native AppGallery details link](https://developer.huawei.com/consumer/fr/doc/quickApp-Guides/quickapp-faq-0000001129279483)
- [Huawei app information specifications](https://developer.huawei.com/consumer/en/doc/app/50104-01)
