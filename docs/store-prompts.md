# Store update and review prompts

These features are implemented for Windows and Android. No store release or version bump is included.

## Update banner

- The Downloads page checks on opening and foreground/resume. The check is asynchronous and bounded to ten seconds.
- Windows asks `StoreContext.GetAppAndOptionalStorePackageUpdatesAsync`, ignoring optional packages. Store-signed packaged installations are supported; loose Debug and development packages are excluded.
- Android uses Microsoft's binding for Google Play App Update. It asks Play about availability for this installation, rather than comparing a scraped public version string. Eligibility, installed version, signing and rollout rules remain controlled by the store.
- Successful results are cached for 24 hours; unavailable/failed checks are retried after one hour. Installing a different app version invalidates the cache. Errors do not interrupt downloads.
- The top banner opens the platform's store listing, with an HTTPS fallback. It does not download/install app updates itself. Dismissal lasts for the current app session.
- An available update takes priority over a review request.

## Review prompts

- Count a newly verified, completed torrent once, including transitions into seeding. Restored historical completions are not backfilled. Pauses, restarts, repeated callbacks and re-adding the same info-hash do not add another count.
- The first request becomes eligible after five unique completions. Background downloads count, but the banner is offered only on the foreground Downloads page.
- Offering the banner is persisted immediately. Choosing Later, canceling the Windows review dialog, or restarting after an offer requires both ten additional completions and 30 elapsed days before another offer.
- Don't ask again permanently opts out in local app data.
- Windows uses `RequestRateAndReviewAppAsync` with the native window owner. A successful submission disables future requests; cancellation only postpones them. Errors leave the banner available for retry.
- Android's Rate app action opens Google Play directly. Successful handoff disables future requests, without recording a confirmed submission. Play does not disclose whether the user submitted a review or even saw its native review dialog, and recommends a store link for an explicit Rate button.
- There is no claim to detect reviews submitted elsewhere or on another device. Local suppression is preserved across ordinary restarts/updates, but cannot be guaranteed after data clearing or reinstalling.

`store-prompts.json` is stored in the existing app data directory, separately from torrent/settings snapshots. It contains hashed completion identities, scheduling information, update cache state and review preferences. No filenames, magnet links or listening/download details are sent to the stores. Unreadable/corrupt prompt state is not silently reset, protecting an existing opt-out.

All banner text is localized across the app's 24 locales. Debug builds disable store prompts. The core policy is tested using an injected store, clock, dispatcher and state store.

## Validation before a future store release

The native store interactions require real eligible installations and cannot be proven by a local Debug run:

1. Install an older Store-signed Windows package and a Play-delivered Android build; make a higher version eligible for the test account/device. Confirm the update banner and correct listing on both platforms.
2. Verify current-version, offline, unavailable-store and staged-rollout cases; app startup/downloads must remain responsive.
3. Complete five distinct downloads; verify first offer, background/foreground handling, narrow-screen button wrapping, dark theme and RTL.
4. Test Later and opt-out, then restart/update the app. Verify the counter survives removal of completed torrent rows and is unaffected by settings changes.
5. On Windows, submit and cancel the native review dialog separately. On Android, verify Play handoff and opt-out without claiming submission.

References:

- https://learn.microsoft.com/en-us/uwp/api/windows.services.store.storecontext.getappandoptionalstorepackageupdatesasync
- https://learn.microsoft.com/en-us/windows/uwp/monetize/request-ratings-and-reviews
- https://developer.android.com/guide/playcore/in-app-updates/kotlin-java
- https://developer.android.com/guide/playcore/in-app-review#quotas
- https://developer.android.com/guide/playcore/in-app-review/kotlin-java
