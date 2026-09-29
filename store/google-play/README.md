# Google Play listing

Package `com.torrentfree.app`, listed as **Torrent Client**.

## Current release

Version **1.17** (code **22**) was submitted to production on September 29, 2026.
Publishing overview shows **Changes in review**, with automatic checks completed.
Managed publishing is off: the full rollout to the existing 178
countries starts after approval. The release includes 26 localized release notes,
ReTrace mapping and native debug symbols for both supported ABIs.

## Listing text

`listings/<play-locale>/` holds `title.txt` (30 characters), `short_description.txt`
(80) and `full_description.txt` (4,000) for every Play Console language. They are
generated from `store/listing-text/*.json` by `python store/build_listings.py`, which
also checks the length limits. The Play default language is en-GB (filled from the
English text). Spanish is written once for `es-419` and `es-ES`; Ukrainian exists only
on Play.

`release-notes-1.17.txt` is in Play Console's multi-language format: paste it into the
release notes box of the production release as is. The generator retains the 1.16
base listing; the 1.17 notes are maintained separately.

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
with `release-notes-1.17.txt`, and upload the matching native debug symbols ZIP.
