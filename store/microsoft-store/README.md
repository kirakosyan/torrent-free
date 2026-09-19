# Microsoft Store listings

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

Use `listings.csv` with Partner Center's **Import listings** action after uploading the MSIX packages.

To upload the included Store logo PNGs, choose **Import folder** and select the `store/microsoft-store` folder. The CSV paths include the root folder name (`microsoft-store/assets/...`) as Partner Center expects for folder imports.

The MSIX package manifest controls the languages shown under **Languages supported in packages**. The CSV controls the customer-facing Store listing text for each language. Partner Center keeps these as separate submission metadata.

If Partner Center's exported CSV already contains screenshot URLs, keep those asset rows from the export and copy these language columns into that latest exported template before importing. Microsoft requires a description and at least one screenshot for every completed listing.

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
