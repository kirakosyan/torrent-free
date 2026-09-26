# Google Play listing

Package `com.torrentfree.app`, listed as **Torrent Client**.

## Listing text

`listings/<play-locale>/` holds `title.txt` (30 characters), `short_description.txt`
(80) and `full_description.txt` (4,000) for every Play Console language. They are
generated from `store/listing-text/*.json` by `python store/build_listings.py`, which
also checks the length limits. Spanish is written once and used for `es-419`, `es-ES`
and `es-US`; Ukrainian and Farsi exist only on Play.

`release-notes-1.16.txt` is in Play Console's multi-language format: paste it into the
release notes box of the production release as is.

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
with `release-notes-1.16.txt`, and upload the matching native debug symbols ZIP.
