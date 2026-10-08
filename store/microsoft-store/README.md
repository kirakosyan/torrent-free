# Microsoft Store listings

## Current release

**1.23 / build 28 / MSIX 1.23.0.0 (x64 and ARM64)** was submitted on
**October 8, 2026**, submission **20** (`1152921505702078202`). Status:
**In certification**, verified **22:54 Europe/Oslo**. **1.23 is not live yet**;
publication is automatic after certification.

Adds torrent file selection, search, saved choices, selected-file progress and a
responsive picker. Both packages validated. All 31 localized notes were verified
against a fresh export, with every other field and image unchanged. Certification
instructions describe the new file-selection workflow. Existing availability and
mandatory-update settings are preserved; Xbox remains unchecked.
See the [1.23 release record](release-notes-1.23.md).

Version **1.19 is live**, verified October 8. Submission 19 is the Store presence.

## Previous release history

**1.19 / build 24 / MSIX 1.19.0.0** was submitted for x64 and ARM64 on
**October 2, 2026**, as submission **19** (`1152921505702031669`). Partner Center
shows **In certification**, with pre-processing underway, verified at **22:36
Europe/Oslo (20:36 UTC)**. Both packages validated; the Xbox toast-extension warning
does not affect this release because Xbox availability is unchecked. Desktop and
Windows Team availability, mandatory-update settings and automatic publication
after certification are preserved. Version 1.19 is **not live yet**.

All 31 localized release notes were verified against a fresh export. The initial
notes-only import cleared optional feature/search fields and the logo override;
these were restored before submission. The final export differs from the original
only in `ReleaseNotes`, including unchanged screenshots, captions and asset order.
Certification testing notes describe the 1.19 deletion fix. Submitted packages
remain under `artifacts/store-1.19/` / `C:\temp\torrentfree-1.19-msix`.
The deletion fix, package hashes and validation are in the
[1.19 release record](release-notes-1.19.md). **1.18 remains live.**

Version **1.18** (build **23**, MSIX **1.18.0.0**) was submitted with x64 and ARM64
packages as submission **18** (`1152921505702024112`) on **October 2, 2026 at
00:43 Europe/Oslo** (October 1 at 22:43 UTC). Certification has passed, verified on
**October 2 at 01:14 Europe/Oslo** (October 1 at 23:14 UTC). Version **1.18.0.0 is
now live**, published October 2 Europe/Oslo (October 1 UTC), and verified on
**October 2 at 01:33 Europe/Oslo** (October 1 at 23:33 UTC). Partner Center confirms
submission 18 is the current Store presence; its live package details list x64 and
ARM64 **1.18.0.0**. The exact publication timestamp is not shown. The update has
localized release notes for all 31 existing listing languages, mapped from the
24 app languages. See [release notes, validation and release record](release-notes-1.18.md).

The previous release, **1.17.0.0**, was verified live in Partner Center on October 1,
2026. Submission 17 (`1152921505702004251`) was submitted on September 29, 2026;
its exact publication date has not been verified. See the [1.17 release record](release-notes-1.17.md).

## Listing text and screenshots

All listing text lives in `store/listing-text/<language>.json`, one file per app
language. Run `python store/build_listings.py` after editing them; it validates the
Store and Play length limits and regenerates:

- `listings.csv` (title, description, short description, release notes, 14 features, logos)
- `release-notes-1.16.json` and `screenshot-captions.json`
- `STORE_DESCRIPTION.md` and the Google Play files under `store/google-play`

The generator retains the 1.16 base listing. The current update uses
`release-notes-1.23.json`. Start with a fresh Partner Center export and replace
only the `ReleaseNotes` values. Preserve the original non-asset text rows,
including `Feature*`, `SearchTerm*`, hardware requirements and the
`OverrideLogosForWin10` flag in the import: omitting these can clear optional
fields or reset the logo flag. Omit unchanged screenshot, caption, trailer and
logo asset rows to avoid reordering assets. A CSV containing only `ReleaseNotes`
is not safe. Export again and compare every field against the original; only
the release notes should differ. If screenshot cards are dragged in the editor,
restore any captions it clears and verify the saved order.

Screenshots are 2880×1620 captures of an unpackaged Debug build (no debugger attached,
so no XAML toolbar) on a 200% display,
running legal downloads (Debian images and Blender open movies). Set **light** is the
default listing; set **dark** is the product page experiment variant. Enter the matching
line from `screenshot-captions.json` in each screenshot's caption field; Microsoft asks
for no marketing text on the images themselves.

| # | Light (default) | Dark (experiment) |
|---|---|---|
| 1 | Active downloads | Active downloads |
| 2 | Download list | Download list |
| 3 | Settings: Wi-Fi only, language, theme, folder | Same |
| 4 | Speed, queue, seeding and proxy settings | Same |
| 5 | Dark theme | Light theme |
| 6 | Japanese interface | Japanese interface |

To capture new screenshots without touching the Store app's queue, run a Debug build
with `TORRENTFREE_DATA_DIR` pointing at an empty folder; Debug builds honour it and
keep all state and downloads there.

### Partner Center settings

- **Device families:** keep Windows 10/11 Desktop and Windows 10 Team (Surface Hub);
  clear Mobile and Holographic, which cannot run a full-trust desktop app.
- **Sort title:** `Torrent Free`, so searches for the name used before 1.16 still find the listing.
- **Listing languages:** one listing per app language, plus the regional listings older
  submissions created (`ar-ae`, `en`, `es-es`, `fr-fr`, `hi-in`, `ru-ru`, `tr-tr`); the
  import fills those with the matching language's text.
- **Product page experiment:** the draft *Dark screenshots vs light (1.16)* holds the
  **dark** set. Submit it at a 50% split only after 1.16 is published, so the control is
  the new listing. Do not change the default screenshots while it runs.

## Build an update

Bump `AppDisplayVersion` and `AppBuildNumber` in the app project, align the Windows
manifest and version metadata test, then run the core tests. Build each supported
architecture separately:

```powershell
rtk proxy dotnet test --project tests/TorrentFree.UnitTests/TorrentFree.UnitTests.csproj -c Release

foreach ($architecture in @('x64', 'arm64')) {
    rtk proxy dotnet publish src/TorrentFree/TorrentFree.csproj `
        -f net10.0-windows10.0.19041.0 -c Release `
        -p:WindowsStoreBuild=true -p:RuntimeIdentifier=win-$architecture `
        -p:Platform=$architecture -p:SelfContained=true `
        -p:PublishSingleFile=false -p:PublishReadyToRun=false `
        -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=false -p:AppxSymbolPackageEnabled=false `
        -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxBundle=Never
    if ($LASTEXITCODE -ne 0) { throw "Packaging failed for $architecture" }
}
```

`WindowsStoreBuild` limits the MAUI app's restore to Windows without overriding
the Core project's `net10.0` target. The packaging mode above produces standalone
MSIX files accepted by Partner Center, avoiding the optional upload-container
and symbol-generation tools. Microsoft signs the packages after certification;
these unsigned files are for Store upload, not direct installation.

Before uploading, verify each MSIX manifest has the expected Store identity,
increased version, correct architecture, and all 24 languages. Upload both
architecture packages to the same update and wait for validation before submitting
for certification.

## Listing imports

1. Upload the six **light** screenshots to the en-us listing in the new submission (the
   first upload replaces an existing slot) and save.
2. Export listings, then build the import from that export:

   ```powershell
   python store/tools/ms_listing_import.py "<export.csv>" "<import.csv>"
   ```

   Every language gets the new text, 14 features, seven search terms, the sort title,
   the en-us screenshot URLs and its own captions. Missing languages are added as new
   columns and regional duplicates (`es-es`, `fr-fr`, `hi-in`, `ru-ru`, `tr-tr`, `ar-ae`,
   `en`) receive the matching language's text.
3. Choose **Import listings → Upload .csv**. Partner Center saves the languages one by
   one; keep the page open until it finishes.

The exported CSV still carries `SearchTerm1`–`SearchTerm7`. The MSIX package manifest
controls the languages shown under **Languages supported in packages**; the CSV controls
the customer-facing listing text for each language.

## Store update error 0x80073CFB on a development PC

A loose development registration can use the same package identity as the Store
app. Windows then refuses to replace it with a Store package. In the AppX
deployment log, this appears as "Another user has already installed an unpackaged
version of this app." The message can also occur for a development registration
belonging to the current user.

Confirm the cause before removing anything:

```powershell
Get-AppxPackage -Name '9971ArmenKirakosyan.TorrentClientApp' `
    -PackageTypeFilter Main,Bundle,Framework,Resource |
    Format-List PackageFullName,IsDevelopmentMode,InstallLocation,SignatureKind
```

For this conflict, `IsDevelopmentMode` is `True` and `InstallLocation` points to a
build directory, such as `bin\Debug\...\AppX`, rather than `WindowsApps`.

Close Torrent Client and back up `%LOCALAPPDATA%\TorrentFree` and
`%LOCALAPPDATA%\Packages\9971ArmenKirakosyan.TorrentClientApp_5yzvegktgaz4g`.
Remove only the confirmed development registration for the current user, retaining
its app data:

```powershell
$developmentPackages = Get-AppxPackage -Name '9971ArmenKirakosyan.TorrentClientApp' `
    -PackageTypeFilter Main,Bundle,Framework,Resource |
    Where-Object { $_.IsDevelopmentMode }
$developmentPackages | ForEach-Object {
    Remove-AppxPackage -Package $_.PackageFullName -PreserveApplicationData
}
```

Then install Torrent Client App again from Microsoft Store, or run:

```powershell
winget install --id 9NNX2ZTPXC26 --exact --source msstore
```

Verify `IsDevelopmentMode` is `False`, `SignatureKind` is `Store`, and the installed
version is current. If a different Windows account owns the conflicting
development registration, remove it from that account after preserving its data;
do not remove packages for all users indiscriminately.

Users with an ordinary Store installation do not need to uninstall or clear data.
They can use Microsoft Store's Library/Downloads update action. This registration
conflict is local to PCs that have installed a development build under the Store
identity; increasing the Store version cannot fix it. Windows Debug builds now run
unpackaged to avoid creating such registrations, while Release packaging retains
the Store identity required for in-place updates.
