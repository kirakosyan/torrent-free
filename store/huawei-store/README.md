# Huawei AppGallery

Huawei builds use `-p:HuaweiStoreBuild=true -p:AppTargetFramework=net10.0-android`.
This produces an APK with identity `com.torrentfree.app.huawei` and the listing's
English title. It installs separately from the Play and Galaxy variants.
Selecting both Huawei and Galaxy build flags fails explicitly.

## Signing and updates

Reuse the existing private Android signing key and retain it for every Huawei
update. A Google Play upload key can sign an AppGallery APK, but differs from the
certificate Google uses for Play-delivered apps; the separate package avoids
cross-store signature conflicts. Keep keystores, aliases, passwords, generated
packages and account evidence outside Git. The private Apps folder contains the
reusable build helper and release artifacts. AppGallery receives only the signed
APK, never the keystore or password files.

AppGallery installs are recognized by installer `com.huawei.appmarket`. Rating
and update actions open that store's app details page using the running package,
with an AppGallery HTTPS fallback. Opening the page does not assert that a review
was submitted. AppGallery manages its own updates; the app returns unknown
availability and does not call Google Play's update API or display a misleading
Play update banner. Sideloads continue to suppress store prompts.
See [store prompt checks](../../docs/store-prompts.md).

## Listing assets

```powershell
rtk proxy python store/huawei-store/prepare_submission.py --output <private-folder>
```

The bundle reuses all 26 Play listing translations and five 1440x2560 phone
screenshots, plus the 512x512 icon. All languages map directly to choices verified
in AppGallery on October 5, 2026. Non-default listings inherit the English images.
The utility checks text/image limits shown in the portal and records image hashes.
It refuses output inside the repository and does not upload anything.

The portal requires 3-8 screenshots, each 320-3840 pixels per side and at most
5 MB for PNG/JPEG. PNG icons can be 216x216 or 512x512 and at most 2 MB.
Full introductions allow 8000 characters; brief introductions allow 80; names 30.
The app itself retains its 24 UI languages. Ukrainian remains listing-only.

## Release

See [1.20 release record](release-notes-1.20.md) for verified publication state.
Complete Huawei's own content rating, privacy, support, distribution and reviewer
fields. Test a store-delivered installation, rating handoff and later update on a
real device. Android APK support does not imply native HarmonyOS NEXT support.

References:
- [Huawei Developers: AppGallery redirection](https://medium.com/huawei-developers/common-redirection-functions-on-huawei-appgallery-2d178b762d43)
- [Huawei app information specifications](https://developer.huawei.com/consumer/en/doc/app/50104-01)
