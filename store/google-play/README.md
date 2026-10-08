# Google Play listing

Package `com.torrentfree.app`, listed as **Torrent Client - Downloader**.

## Current release

**1.23 / code 28** was submitted on **October 8, 2026 at 22:47 Europe/Oslo
(20:47 UTC)**, submission **22**, production release **12**. Status: **In review**;
initial automated checks were still running. Full rollout is requested across the
existing 178 countries/regions. Managed publishing is off, so approval triggers
publication. **1.23 is not live yet.**

Adds torrent file selection, search, saved choices, selected-file progress and a
responsive mobile picker. All 26 release notes, embedded ReTrace mapping and
matching native debug symbols were accepted. The preview confirms zero newly
unsupported devices. **Android 6.0 / API 23 is retained**; the owner approved
disabling the optional installer check, while Play app signing remains enabled.
Superseded 1.21/1.22 tags and artifacts remain unchanged and were never submitted.
See the [1.23 release record](../microsoft-store/release-notes-1.23.md).

Version **1.19 / code 24 is live**, published October 4 and verified October 8.

## Previous release history

**1.19 / code 24** fixes the shared-folder deletion regression affecting Android
and Windows. Its signed AAB, R8 mapping and matching native symbols are validated
and retained under `artifacts/store-1.19/`, with upload copies in `C:\temp`.
**Submitted and in review**, submission **21**, production release **11**, on
**October 2, 2026 at 22:30 Europe/Oslo (20:30 UTC)**. The code-24 AAB, all 26
localized release notes, embedded ReTrace mapping and matching native debug symbols
were accepted. Full rollout (100%) was requested across the existing targeted
countries. Managed publishing is off, so publication follows approval automatically.
The initial automated checks were still running when submission was confirmed.
Version 1.19 is **not live yet**.
See the [1.19 release record](../microsoft-store/release-notes-1.19.md).

**1.18 / code 23 is now live**, verified in Play Console during preparation of
1.19 on October 2. Production shows 100% rollout to 178 countries/regions and
Publishing overview reports the last publication on October 2.

Version **1.18** (code **23**) was submitted for production review on **October 1,
2026**, at **23:23 Europe/Oslo** (21:23 UTC), as submission **20**, production release
**10**. Submission activity now shows **Published**, verified October 2.
All 26 localized release notes, ReTrace mapping and
matching native debug symbols for both supported ABIs were accepted. The rollout
is 100% across the existing 178 countries/regions. Managed publishing remains off.
See the [1.18 release record](../microsoft-store/release-notes-1.18.md).

The earlier version **1.17** (code **22**) was published on September 30,
2026 and verified at 100% rollout in Play Console on October 1, 2026. It was
submitted to production on September 29, 2026.

## Listing text

`listings/<play-locale>/` holds `title.txt` (30 characters), `short_description.txt`
(80) and `full_description.txt` (4,000) for every Play Console language. They are
generated from `store/listing-text/*.json` by `python store/build_listings.py`, which
also checks the length limits. The Play default language is en-GB (filled from the
English text). Spanish is written once for `es-419` and `es-ES`; Ukrainian exists only
on Play.

`release-notes-1.23.txt` is in Play Console's multi-language format: paste it into the
release notes box of the production release as is. The generator retains the 1.16
base listing; the 1.23 notes are maintained separately.

Translations inherit the default graphics, so only the text needs updating per language.
Version 1.16 (code 21) and this listing were sent for review on September 27, 2026,
and were live before the 1.17 submission.

## Graphics

- Feature graphic: `feature_graphic_1024x500.png` in the repository root
  (`store/tools/feature-graphic.ps1` rebuilds it from a phone screenshot).
- Phone screenshots: `screenshots/phone/01–05.png`, 1440×2560 with English captions.
  `store/tools/frame-play-screenshots.ps1` frames `screenshots/raw` with the captions in
  `screenshot-captions.json` for any language.
- Tablet screenshots: none. Earlier listings used Windows screenshots here; Play expects
  Android tablet UI, so leave this section empty until real tablet captures exist.

Every screenshot shows openly licensed downloads (Debian images). Never show commercial
films, TV shows or release-group names: Play removes torrent apps whose listings appear to
encourage copyright infringement.

## Release

Build the signed AAB as described in `.codex/AGENTS.md`, upload it to the production track
with `release-notes-1.23.txt`, and upload the matching native debug symbols ZIP.
