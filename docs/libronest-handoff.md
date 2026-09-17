# Open completed audiobooks in LibroNest

Completed downloads containing MP3, M4B, M4A, AAC, FLAC, OGG or WAV files show an
**Open in LibroNest** button with a headphone icon on Windows and Android. Audio
formats identify candidates; music files can also display this action. Incomplete
downloads and downloads without supported audio do not display it.

The action includes supported tracks in nested folders and ignores artwork,
unrelated sibling downloads and symbolic links. It leaves the downloaded originals
in place for seeding. LibroNest asks before importing tracks as one audiobook and
uses its existing duplicate and library-limit handling.

## Platform contract

- Windows: `libronest://import?path=<URI-escaped absolute local file or folder>`.
  Launch is targeted to `9971ArmenKirakosyan.LibroNest_5yzvegktgaz4g`. For an older
  installed release without protocol support, a single audio file can still open
  through its existing file association. Folder handoff requires the companion
  LibroNest release. Remote/network paths are not part of the protocol contract.
- Android: package-targeted `ACTION_VIEW` for one track and `ACTION_SEND_MULTIPLE`
  for multiple tracks, using `audio/*`, `EXTRA_STREAM`, `ClipData` and temporary
  read grants. FileProvider exposes Torrent Free's private download directories and
  a dedicated handoff cache. Downloads in legacy/custom locations are copied into
  that cache when needed; normal downloads need no extra copy. Cached handoff copies
  are disposable Android app cache and can be cleared by the OS/user. No broad
  filesystem root or additional storage permission is exposed.
- If the required LibroNest handler is unavailable, open the native Microsoft Store
  or Google Play listing; fall back to its HTTPS listing if the store client cannot
  launch. An older release may need updating for multi-track import. After installing
  or updating, return to Torrent Free and press the button again.

Ship the companion LibroNest handoff change before releasing this Torrent Free
feature. Audio file associations remain owned by LibroNest; this action does not
change the user's default audio player.

## Verification

- Core tests cover all supported extensions, nested tracks, non-audio exclusion,
  sibling isolation and removed files.
- Build both Windows and Android targets; verify the merged Android manifest includes
  the non-exported FileProvider and LibroNest package query.
- On devices with the companion LibroNest build, check cold/warm opening of a single
  M4B and a multi-track folder, cancellation, duplicate handling, and import ordering.
- Without LibroNest, check the appropriate store listing. Also test an older installed
  version and a device without a native store client.
