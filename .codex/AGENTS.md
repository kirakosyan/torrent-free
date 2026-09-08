# AGENTS.md

## Scope
This repository contains a .NET MAUI application targeting .NET 10 and an xUnit test project.
Use this file as the default guidance for Codex-style agents working in this repo.

## Solution layout
- `src/TorrentFree` — main .NET MAUI app
- `src/TorrentFree.Core` — platform-independent production models and services
- `tests/TorrentFree.UnitTests` — tests referencing the production Core assembly
- `src/TorrentFree/Services` — MAUI infrastructure and platform adapters
- `src/TorrentFree/ViewModels` — MVVM view models using CommunityToolkit.Mvvm
- `src/TorrentFree.Core/Models` — persisted models and UI/domain state
- `src/TorrentFree/Platforms` — platform-specific implementations

## Architecture expectations
- Keep the existing MVVM structure.
- Register new app services in `MauiProgram.cs`.
- Prefer constructor injection over service location.
- Keep UI code in pages/platform files and business logic in services/models/view models.
- Put platform-specific behavior behind interfaces or partial methods, not inline in shared code.
- Preserve MAUI implementations; do not introduce Xamarin.Forms guidance or APIs.

## Coding conventions
- Follow the existing C# style in the touched file.
- Use file-scoped namespaces.
- Keep nullable reference types enabled and satisfy warnings instead of suppressing them unless necessary.
- Prefer minimal, targeted changes over broad refactors.
- Reuse existing services and helpers before adding new abstractions.
- Do not edit generated files under `obj/`.
- Avoid adding comments unless the file already uses them or the logic is non-obvious.
- Keep public API and persisted model changes backward compatible where possible.

## Repository-specific practices
- `AppSettings` is persisted JSON-backed state. When adding settings, update all read/write/apply paths consistently.
- `StorageService` caches and persists both torrent data and settings; avoid changes that can drop unrelated persisted fields.
- Torrent lifecycle rules are centralized in services such as `TorrentService`, `TorrentRestoreRules`, and `TorrentManagerStateRules`; keep behavior changes there instead of scattering checks in UI code.
- Path-sensitive logic should remain guarded. Reuse `PathGuard` and `DownloadLocationResolver` instead of duplicating path logic.
- Desktop/window-specific behavior belongs in platform files such as `Platforms/Windows/*`.

## Testing guidance
- Add or update unit tests for behavior changes in services, rules, settings normalization, persistence, and path handling.
- Prefer testing pure logic in `tests/TorrentFree.UnitTests`.
- Test the production types in `TorrentFree.Core` through its project reference; do not duplicate models or link source files into tests.
- Run targeted tests first, then broader validation.

## Validation
Before finishing work, prefer this order:
1. Run relevant unit tests in `tests/TorrentFree.UnitTests`
2. Run a solution or project build
3. Only fix issues caused by the current change

Suggested commands:
- `dotnet test --project tests/TorrentFree.UnitTests/TorrentFree.UnitTests.csproj`
- `dotnet build src/TorrentFree/TorrentFree.csproj`

## Android Store Package Generation
- For Google Play releases, bump `AppBuildNumber` in `src/TorrentFree/TorrentFree.csproj`; bump `AppDisplayVersion` only when the public version should change. Keep `tests/TorrentFree.UnitTests/VersionMetadataConsistencyTests.cs` and the Windows manifest version aligned.
- Release Android builds should keep R8 mapping generation enabled with `AndroidLinkTool` set to `r8` and `AndroidCreateProguardMappingFile` set to `true`. The signed AAB should contain `BUNDLE-METADATA/com.android.tools.build.obfuscation/proguard.map`; also copy `mapping.txt` to `C:\temp` as a manual upload fallback.
- Signing files and passphrase labels are expected under `C:\temp`. Use them for publishing, but never print or commit passwords, aliases, keystores, or generated packages.
- Publish the signed AAB with `dotnet publish src\TorrentFree\TorrentFree.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=aab -p:AndroidKeyStore=true` plus the `AndroidSigningKeyStore`, `AndroidSigningStorePass`, `AndroidSigningKeyAlias`, and `AndroidSigningKeyPass` properties read from `C:\temp`.
- Copy the signed output from `src\TorrentFree\bin\Release\net10.0-android\publish\com.torrentfree.app-Signed.aab` to `C:\temp` with a name that includes the display version and code, for example `com.torrentfree.app-v1.10-code14-google-play-upload-key.aab`.
- Validate the AAB with bundletool from the installed .NET Android pack, for example `java -jar "C:\Program Files\dotnet\packs\Microsoft.Android.Sdk.Windows\36.1.43\tools\bundletool.jar" validate --bundle=<aab-path>`, and dump `/manifest/@package`, `/manifest/@android:versionCode`, and `/manifest/@android:versionName`.
- Native debug symbols for Play Console must be zipped with ABI folders at the ZIP root and only symbol files that match AAB library names. For this MAUI app, use `obj\Release\net10.0-android\app_shared_libraries\<abi>\libxamarin-app.dbg.so`, but write each ZIP entry as `<abi>/libxamarin-app.so`.
- Do not include `assembly-store.so`, `*.dbg.so` filenames, `.manifest` files, or extra parent directories in the Play native-symbol ZIP. Play rejects those as unexpected files.
- The Play native-symbol ZIP should contain entries like `arm64-v8a/libxamarin-app.so` and `x86_64/libxamarin-app.so`. Upload this corrected ZIP in Play Console's native debug symbols section for the matching version code.

## Agent do/don't list
Do:
- Make the smallest change that solves the requested problem.
- Keep dependency injection registration aligned with implementation changes.
- Preserve existing persistence behavior and migration safety.
- Respect MAUI and .NET 10 project settings.

Do not:
- Introduce unrelated refactors.
- Modify package versions unless explicitly asked.
- Edit generated artifacts or machine-specific files.
- Move business logic into code-behind when it belongs in services or view models.
