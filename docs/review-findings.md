# Review findings — 2026-09-28

The review identified useful correctness and usability fixes. The changes below are implemented; platform smoke checks that still need interactive testing are listed separately.

## Correctness and persistence

| Finding | Result |
| --- | --- |
| Same-length export replacement test is flaky | The test now explicitly advances the source mtime. Production export integrity checks are preserved. |
| Shutdown during start can persist `Failed` | Start failure handling checks shutdown cancellation before rollback and again before changing status. Regression tests cover disposal during manager creation, start and rollback. |
| Wi-Fi handover briefly reports no Wi-Fi | Android 8+ waits for the ordered capabilities callback; Android 6–7 use the connectivity snapshot fallback. |
| System language uses installation language | Captures `CurrentUICulture` before application overrides. “System Default” is translated in every supported resource set. |
| Crash log grows indefinitely | Current log and one backup are each capped at 256 KiB. Oversized legacy logs and entries are bounded too. |
| Rejected imports leave cache files | Preparation retains parsed bytes in memory. Cache publication occurs during acceptance, under the existing cache lock; rejected publication cleans up newly created, unreferenced files. |
| Association prompt consumed before a page exists | Prompting occurs from page initialization. The preference is saved only after the dialog returns. |
| Audiobook track ordering is unstable | Paths sort with ordinal, case-insensitive comparison. The test asserts returned order. This is lexical order; natural numeric sorting is not introduced. |
| Settings write on every keystroke | Edits coalesce for 400 ms. Navigation, page reload and suspension flush pending edits; Windows Closing awaits the flush before engine disposal. |
| Proxy password is rewritten unnecessarily | Known successful secret reads/writes suppress unchanged writes. Errors invalidate that knowledge so retries remain possible. |
| Settings factories can drop fields | Both factories clone the existing settings before applying their updates. |
| Sequential proxy teardown delays rebuild | Independent managers stop concurrently while retaining each torrent's operation lock. Regression test verifies overlapping stops. |

## User interface, platform behavior and documentation

| Finding | Result |
| --- | --- |
| Delete dialog preselects file deletion | The review changed both options to unchecked in v1.17. Reversed on 2026-10-01 at the user's request: both options start checked again; clearing both still removes only the list entry. Regression tests now cover the dialog defaults. |
| External magnets start without review | Incoming magnets are previewed in the main input with a visible Download button. They are not added or started until submitted. |
| Windows treats metered WLAN as Wi-Fi | Metered, roaming and over-limit connections are excluded, including WLAN. Unknown cost retains the existing permissive behavior; an unmarked hotspot can still qualify. |
| Secondary Windows process blocks its STA | Redirection is awaited asynchronously in `OnLaunched`, before MAUI service/window initialization. The timeout still exits the secondary process to protect shared state. |
| Badge text lacks contrast | Dark text on the existing status colors produces contrast ratios from 4.67:1 to 8.67:1. |
| No CI / repeated package versions / preview language | Added Windows and Linux core tests plus Android and Windows app builds in GitHub Actions. Package versions are centralized. Preview language overrides are removed. |
| Foreground notification is static | Android refreshes aggregate download progress or the seeding count every two seconds. |
| Completion notification taps do nothing | Android/iOS taps navigate to the main page and select the completed torrent. Pending taps survive initial storage-load failure. Immediate notifications no longer specify a time that may already have elapsed. |
| Privacy policy omissions | Added legacy storage permissions, imported metadata/resume caches, and local crash diagnostics with their rotated backup. |
| Apple platforms described as supported | README now marks iOS and Mac Catalyst experimental pending platform validation. |
| Duplicate localization API | Removed unused `ILocalizationService.GetString`. |

## Suggestions not adopted

**Replace destination export hashing with size/mtime checks:** not adopted. The destination is externally editable, and the regression suite intentionally changes an export without changing its length. Coarse timestamps can also stay unchanged. Metadata-only reuse would weaken corruption/replacement detection. A future optimization needs a reliable provider revision token or an explicitly weaker verification policy.

**Enable Android cleartext traffic:** unnecessary for the current tracker implementation. On a connected Samsung SM-S926B running Android 16, a separate .NET 10 probe using the same MonoTorrent 3.0.2 package and no cleartext opt-in reported:

```text
NATIVE_CLEARTEXT_ALLOWED=False
NATIVE_REQUEST=BLOCKED
MONOTORRENT_HTTP=d8:intervali1800e5:peers0:e
TRACKER_STATE=Ok
```

The probe announced a synthetic info hash to a local HTTP tracker through an ADB reverse tunnel; no public torrent traffic was used. The probe app and tunnel were removed afterward. This verifies the HTTP tracker path on that device, not every tracker or Android release. MonoTorrent explicitly uses a [socket-based HTTP handler](https://github.com/alanmcgovern/monotorrent/blob/e78faebd0aec117146cffccaaea987ab0629eec0/src/MonoTorrent.Factories/MonoTorrent/HttpRequestFactory.cs), consistent with Android's documented [cleartext policy limits for sockets](https://developer.android.com/guide/topics/manifest/application-element#usesCleartextTraffic).

**Windows completion notifications:** implemented in the 2026-10-01 follow-up using Windows App SDK notifications, packaged COM activation, and the existing single-instance routing and pending torrent selection. Native cold/warm click testing remains a release check.

## 2026-10-01 follow-up

- Failed manager stops or engine removals preserve the row, manager and monitor. File deletion starts only after successful cleanup, so the user can retry failures.
- Payload deletion protects other tracked torrents' files and source metadata. Missing metadata conservatively protects the other torrent's save directory. A publication/deletion gate prevents a new import from appearing midway through that ownership check.
- Source `.torrent` deletion verifies the current file's info hash. Changed or inaccessible files are retained with a localized warning.
- Shared ratio limits keep per-torrent and global ratios finite and within 0–100 before persistence. The settings validation also handles NaN correctly.
- Failed Android exports show a localized copy error. Opening public Downloads is attempted only after a successful export.
- Windows completion toasts select the completed download. The COM executable is explicit because MAUI does not replace its executable placeholder inside `com:ExeServer`.

Follow-up validation: **330 core tests passed**, including delete defaults and combinations, failed-stop retry, active/paused shared files, replaced metadata, ratio persistence, export failure, and notification payload/manifest checks. Windows ARM64 and Android Debug builds passed with zero warnings or errors. Windows ARM64 unsigned MSIX packaging was also exercised; optional symbol generation requires an unavailable local tool and was disabled for that check. Packaging reports PRI263 resource warnings. Notification display and cold/warm clicks still need a native UI check before release.

## Validation

- Final core suite: **294 passed, zero failures or skips**. An earlier full run also passed all 287 tests present at that point.
- Windows ARM64 Debug build: **zero warnings, zero errors**.
- Android Debug build: **zero warnings, zero errors**.
- HTTP tracker device probe: **passed** as described above.
- CI configuration is added but has not yet run on GitHub-hosted runners.

Before release, exercise Windows double-click/file redirection, metered hotspots, Android Wi-Fi handovers, and completion notification taps on cold and warm launches. iOS/Mac Catalyst builds, entitlements and device behavior remain unvalidated. The production app and its saved downloads were not launched or modified during the isolated HTTP probe.
