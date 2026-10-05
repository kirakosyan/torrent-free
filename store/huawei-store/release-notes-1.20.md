# Version 1.20: Huawei AppGallery

Display version **1.20**, Android code **25**, Huawei identity
**com.torrentfree.app.huawei**, architectures **arm64-v8a** and **x86_64**.
Shared Windows metadata is aligned to **1.20.0.0**; no Windows or Google Play
1.20 release is included in this submission.

## Changes

- Recognize Huawei AppGallery installations and route ratings and updates to it.
- Leave update availability to AppGallery; never call Play's update API for a
  Huawei installation. Include the installer store in the cached version identity.
- Add an APK build identity that can reuse the existing private signing key
  without conflicting with Google's signing certificate.
- Reuse the existing Play listing in all 26 languages and all five screenshots.

## Traceability and status

Preparation in progress on October 5, 2026 (Europe/Oslo). The Huawei app record
exists; no APK has been uploaded or submitted at this point. The exact source
commit will be identified by annotated tag **v1.20** before building. Package
hashes, validation and final store status will be recorded after verification.

No production key or package is stored in the public repository.
