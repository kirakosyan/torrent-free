# AGENTS.md

## Scope
This repository contains a .NET MAUI application targeting .NET 10 and an xUnit test project.
Use this file as the default guidance for Codex-style agents working in this repo.

## Solution layout
- `src/TorrentFree` — main .NET MAUI app
- `tests/TorrentFree.UnitTests` — unit tests for app logic
- `src/TorrentFree/Services` — domain and infrastructure services
- `src/TorrentFree/ViewModels` — MVVM view models using CommunityToolkit.Mvvm
- `src/TorrentFree/Models` — persisted models and UI/domain state
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
- The test project links selected source files directly from `src/TorrentFree`; if new pure logic files need coverage, follow the existing test-project pattern.
- Run targeted tests first, then broader validation.

## Validation
Before finishing work, prefer this order:
1. Run relevant unit tests in `tests/TorrentFree.UnitTests`
2. Run a solution or project build
3. Only fix issues caused by the current change

Suggested commands:
- `dotnet test tests/TorrentFree.UnitTests/TorrentFree.UnitTests.csproj`
- `dotnet build src/TorrentFree/TorrentFree.csproj`

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
