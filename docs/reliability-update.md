# Windows diagnostics and reported bug fixes

## Store investigation

The Partner Center report inspected on 2026-11-15 contained four hits for
`STOWED_EXCEPTION_80131509_Microsoft.UI.Xaml.dll!DirectUI::DesktopWindowImpl::OnActivate`.
The available event was for version 1.23; its stack contained native frames only.
There was no managed exception message or downloadable dump identifying an app call site.
The other 249 entries in that failure table were uncategorized and cannot be attributed
to this activation bucket. These are failure-table counts, not app crash rates.

MAUI has a documented activation reentrancy problem:
[dotnet/maui#39428](https://github.com/dotnet/maui/issues/39428).
The app uses MAUI 10.0.100. Its own foreground prompt work and explicit activation
requests are now posted to the dispatcher so lifecycle callbacks can finish first.
This is a targeted mitigation, **not proof that the Store crash has been reproduced or fixed**.
The framework exception is not suppressed, and no blanket `Handled = true` is added.

## Diagnostics

Windows writes local diagnostics to `%LOCALAPPDATA%\TorrentFree\crash.log`, retaining
one rotated file, `crash.log.1`, with a 256 KiB limit per file. Isolated Debug runs use
`TORRENTFREE_DATA_DIR` for both queue state and diagnostics.

Entries include the session, app/MAUI/runtime/OS versions, architecture, thread,
exception HRESULT, full managed exception (including inner exceptions), and the last
32 lifecycle breadcrumbs. WinUI, AppDomain and unobserved task errors are recorded.
Activation import/redirection errors and settings failures also retain local details.
Breadcrumbs record activation kinds and window state, not magnet links, filenames,
notification arguments or proxy credentials. Exception text can contain local paths;
review logs before sharing them. Nothing is uploaded automatically.
Routine launch and window events remain in the in-memory breadcrumb ring; only faults
and responsiveness reports write snapshots, so normal sessions do not rotate away crash evidence.

A background timer probes the UI dispatcher. A response delay of at least 30 seconds
records one stall and then a recovery when it responds again. Gaps in the timer itself
(for example, system sleep) reset the probe. This supplies timing context, not a hung
thread stack dump or a diagnosis of every Store hang.

## Bug-only issue review

All 12 open issues labelled `bug` were reviewed. Other enhancement and hardening
issues remain outside this change.

| Issue | Analysis and change |
| --- | --- |
| #27 | Retain the private flag and file-import provenance. Missing/corrupt private or unverified file metadata cannot fall back to public magnet discovery, including file-list previews. Valid metadata still starts through MonoTorrent's private-torrent path. Legacy file imports with unknown privacy require the original file again; legacy magnets can recover after cache loss. Privacy is saved only when it changes. |
| #28 | Proxy fields are editable while disabled. Editing creates a draft retained across page reloads in the same session; one explicit Apply validates and saves the complete configuration before applying it. Unrelated autosaves use the last applied proxy. |
| #32 | Failed start cleanup retains the registered manager when stop/removal fails. Retry reuses it; callers share and await the actual in-flight stop task, with a state-event fallback for engine-originated stops. There is no polling or artificial 30-second wait ceiling. Metadata parsing failures are separated from engine registration failures. |
| #34 | Reaching a seed ratio/time limit cancels monitoring before teardown and persists Completed after the stop. Start All excludes Completed; an individual Start remains available. |
| #35 | A failed settings load disables the form and exposes a visible retry. Successful retry restores edits and persistence. |
| #36 | Both the percentage badge and status line floor progress, so 99.96% displays as 99% and 99.9%, respectively. |
| #37 | Global and per-torrent numeric entries share normalization before numeric binding: Unicode decimal digits, decimal separators, empty input and overflow. The normalized value is visible; clearing means zero. |
| #44 | Settings errors remain above the scrolling form, use localized messages and are announced to assistive technology. Technical exceptions go to the local log. |
| #47 | The magnet hint promises only magnet links, matching the parser. |
| #50 | Android export prepares a cancellable plan off the UI thread using local metadata only. Completed downloads with missing metadata fall back to the saved selection or on-disk files, excluding partial files and links. Shared ancestors are checked once, including roots with trailing separators. All reusable exports are verified before comparing the total missing bytes with free space, before any new copy. Copy/verification progress and cancellation preserve completed copies and remove the current partial destination. |
| #52 | Foreground and completion notifications use a dedicated monochrome small icon, consistent with [Android's notification guidance](https://developer.android.com/design/ui/mobile/guides/home-screen/notifications). Notification actions are a separate feature request and are not added. |
| #53 (partial) | Correctly typed `application/x-bittorrent` intents remain supported. Generic binary and untyped catch-all filters are removed so the app does not claim unrelated files. Android matches URI/MIME filters before the app can inspect a provider's display name; generic or opaque inputs must use Browse. This does not claim to fix every provider's Open with integration. See [Android intent data matching](https://developer.android.com/guide/topics/manifest/data-element). |

The Move and automatic-export suggestions in #50 are features and are not included.

## Validation and follow-up

Automated coverage includes private metadata loss, retry after injected start and cleanup
failures, completed seed state, numeric normalization, deferred foreground work, diagnostic
details, actual settings-view-model load/save/proxy behavior, selected-file export,
cancellation during copy and verification, and insufficient-space handling.
Review follow-up coverage adds legacy magnet cache loss, offline completed exports,
whole-export storage budgets, draft retention, and coordinated stop/seed teardown.

Windows smoke testing uses an isolated data folder. It covers startup, settings navigation,
Unicode/decimal/empty numeric persistence, proxy draft/apply persistence, deactivation and
reactivation, secondary-process redirection, and shutdown. App builds cover Windows and
the Android store variants. This is not a release or Store submission.

After a separately authorized release, compare health reports for the exact new app version.
If activation failures persist, obtain the new local managed log or a Windows crash dump.
Android export UI and OS chooser behavior still need physical-device qualification before
claiming those workflows are verified end to end.
