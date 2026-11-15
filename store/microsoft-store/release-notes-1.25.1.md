# Version 1.25.1 release record

Prepared November 15, 2026 (Europe/Oslo). **Not uploaded, submitted or published.**
Windows **1.25.1.0**, x64 and ARM64; Android **1.25.1**, version code **31**.

## Changes

Includes the reliability, diagnostics, settings, private-metadata and Android
export fixes described in the [1.25 preparation record](release-notes-1.25.md).
Windows smoke testing found that an earlier import error remained visible after
opening a valid `.torrent` file. File activation now clears the previous error
when beginning a new import, consistent with Browse and magnet activation.

The unsubmitted 1.25 packages and local `v1.25` tag are retained unchanged.
Replacement packages use a new version and annotated `v1.25.1` source tag.
Assembly and file versions use the four-part package version, accommodating the
three-part display version without producing an invalid five-part version.

## Qualification

The initial 1.25 x64 package passed startup, settings navigation and persistence,
Unicode numeric normalization, proxy draft retention, magnet preview and warm
redirection, Browse/file selection, pause, stop, restart and graceful shutdown.
Malformed file activation produced a visible error; the subsequent valid file
exposed the stale-message issue fixed here. About showed 1.25 / build 30.

The smoke launcher must keep its WinRT file object alive until activation has
completed. Exiting that helper immediately caused a native RPC-server-unavailable
failure before managed app startup; correcting the test helper resolved it.
Directly running the extracted EXE also lacked package identity. Neither setup
failure reproduces the original Store activation bucket.

Testing uses a temporary package identity with unchanged application binaries.
The installed 1.24 package remains installed. All original app-data files were
hash-verified on restoration; six original torrents and settings were preserved,
and one new non-test torrent added during testing was retained separately from
the smoke fixtures. Dumps and private logs remain local and are excluded from
release bundles.

Rebuilt-package validation, final 1.25.1 smoke results, source commit, artifact
hashes and store submission status will be recorded after verification.
ARM64 runtime and Android device flows have not been smoke-tested in this session.
