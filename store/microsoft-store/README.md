# Microsoft Store listings

## Current release

Version **1.16** (build **21**, MSIX **1.16.0.0**) includes x64 and ARM64 packages.
The public Store listing showed the 1.15 release notes on September 26, 2026.
See [release notes and certification information](release-notes-1.16.md).

## Listing text and screenshots

All listing text lives in `store/listing-text/<language>.json`, one file per app
language. Run `python store/build_listings.py` after editing them; it validates the
Store and Play length limits and regenerates:

- `listings.csv` (title, description, short description, release notes, 14 features, logos)
- `release-notes-1.16.json` and `screenshot-captions.json`
- `STORE_DESCRIPTION.md` and the Google Play files under `store/google-play`

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
- **Listing languages:** one listing per app language; remove the duplicate regional
  listings (`es-es`, `fr-fr`, `hi-in`, `ru-ru`, `tr-tr`) that older submissions created.
- **Product page experiment:** after the update is published, create an experiment with the
  **dark** screenshots at a 50% split. Do not change the default screenshots while it runs.

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

Export the current listing from Partner Center, then copy the rows of the generated
`listings.csv` into that export so screenshot and trailer URLs are kept. To upload new
screenshots, add `DesktopScreenshot1`–`6` paths (for example
`microsoft-store/screenshots/light/01.png`) and `DesktopScreenshotCaption1`–`6` text,
then choose **Import folder** and select the `store/microsoft-store` folder. The CSV paths
include the root folder name as Partner Center expects for folder imports. Clearing an
image cell does not remove the old image; replace slots or remove extra screenshots in
Partner Center.

The MSIX package manifest controls the languages shown under **Languages supported in
packages**. The CSV controls the customer-facing Store listing text for each language.
Microsoft requires a description and at least one screenshot for every listing.

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
