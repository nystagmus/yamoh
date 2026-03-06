# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Yamoh is a C#/.NET console application that automates the creation of poster overlays for Plex media managed by [Maintainerr](https://github.com/jorenn92/Maintainerr). It manages image assets on disk in a directory structure compatible with [Kometa](https://github.com/Kometa-Team/Kometa).

## Build & Run Commands

All commands run from `src/`:

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~OverlayGeometryTests.ScaledFontSize_ComputesCorrectly"

# Run the app (schedule mode or interactive CLI)
dotnet run --project Yamoh

# Run a specific command directly
dotnet run --project Yamoh -- update-maintainerr-overlays
dotnet run --project Yamoh -- test-overlay-image
```

## Solution Structure

```
src/
  Yamoh/                          # Main application
    Program.cs                    # Entry point: DI setup, host builder, CLI wiring
    Features/
      OverlayManager/             # Main command: fetches Maintainerr collections, applies overlays
      OverlayTestImage/           # Generates test poster previews
      GetPlexInfo/                # Utility command for Plex info
    Infrastructure/
      Configuration/              # Strongly-typed config classes + DataAnnotation validation
      External/                   # MaintainerrClient, PlexClient (HTTP clients)
      FileProcessing/             # AssetManager, AssetPathInfo — disk I/O for poster assets
      ImageProcessing/            # OverlayHelper (Magick.NET), OverlayGeometry
      Scheduling/                 # CronScheduler, ICronJob — CRON-based job runner
      EnvironmentUtility/         # AppEnvironment (paths), AppFolderInitializer
      Extensions/                 # IServiceCollection, DateTimeOffset, etc.
    Domain/
      Maintainerr/                # IMaintainerrCollectionResponse, V2/V3 response models
      Plex/                       # Plex API response models
      State/                      # OverlayStateManager (LiteDB), OverlayStateItem
    Defaults/                     # Default appsettings.json copied to config dir on first run
  Tests/Yamoh.Tests/
    OverlayGeometryTests.cs       # xUnit tests for overlay layout calculations
    TestsBase.cs                  # Shared test setup helpers
```

## Architecture

**Entry point flow:** `Program.cs` initializes `AppEnvironment`, runs `AppFolderInitializer` (copies defaults, checks folder permissions), builds the .NET Generic Host with DI, then uses `System.CommandLine` to dispatch CLI commands. If scheduling is enabled, the host runs indefinitely via `CronScheduler`.

**Commands** implement `IYamohCommand` and are auto-registered via `AddAllTypesOf<IYamohCommand>`. `CommandFactory` generates the CLI command tree from all registered `IYamohCommand` instances.

**Core processing loop** (`OverlayManagerCommand.RunAsync`):
1. Fetch Maintainerr collections via `MaintainerrClient`
2. Restore overlays for items no longer in Maintainerr
3. For each active collection with `DeleteAfterDays` set, build Plex metadata via `PlexMetadataBuilder`
4. For each item: get/backup original poster (`AssetManager`), apply overlay (`OverlayHelper`), update state (`OverlayStateManager`)
5. Optionally reorder Plex collection items by expiration date

**State persistence:** `OverlayStateManager` wraps a LiteDB database (`yamoh_state.db`) stored in the state folder. Tracks which items have overlays applied, their poster paths, and expiration dates.

**Maintainerr API versioning:** `MaintainerrClient` checks the Maintainerr API version and deserializes responses as `MaintainerrCollectionResponseV2` or `MaintainerrCollectionResponseV3` accordingly, both implementing `IMaintainerrCollectionResponse`.

**Image processing:** `OverlayHelper` uses Magick.NET to draw a colored rectangle + text onto poster images. `OverlayGeometry` calculates scaled positions for all alignments (left/center/right, top/center/bottom) relative to image dimensions.

**Configuration:** Four config sections — `Yamoh` (paths, URLs), `Overlay` (visual settings), `OverlayBehavior` (behavior flags), `Schedule` (CRON). All bound via `IOptions<T>` with `ValidateDataAnnotations` + `ValidateOnStart`. Environment variables override `appsettings.json` using double-underscore separators (e.g., `YAMOH__PLEXURL`).

**Config folder locations:**
- Docker: `/config/`
- Windows: `%ProgramData%\YAMOH\Config`
- Linux/macOS: `$HOME/.config/YAMOH/Config`