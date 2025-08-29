# Yamoh - Maintainerr Overlay Helper

Yamoh is a C#/.NET 8+ console application that automates poster overlays for Plex media collections managed by Maintainerr.

## Features
- Periodically polls the Maintainerr API (`/api/collections`) to get collections and media items.
- For each media item, uses its Plex ID to query the Plex API for metadata (library name, folder, media file path).
- Builds an asset directory structure that mirrors the Plex library and media folder hierarchy, storing posters as `{AssetBaseDir}/{LibraryName}/{RelativeMediaDir}/poster.{ext}`.
- If an original poster does not exist in the asset directory, downloads it from Plex and detects its image format.
- FUTURE: Optionally manages posters directly in Plex (configurable).
- Applies a configurable overlay (text, color, font, transparency, etc.) to the poster image and saves the result, preserving the image format.
- Maintains state between runs to track which items have overlays applied.
- Restores original posters for items removed from the Maintainerr collection.
- Supports configuration via JSON file and environment variables (overlay appearance, asset paths, polling interval, etc.).
- Outputs the full configuration at startup for verification.
- Handles both Windows and Unix-style paths, network shares, and file operations.

## Usage
1. Configure the app using the provided JSON config file and environment variables.
2. Run the app as a console application or in Docker.
3. Overlays will be applied and managed automatically based on Maintainerr collections.

## Running Yamoh with Docker

You can run Yamoh using Docker. Make sure to map the `/config` volume so your configuration and state are persisted.

### Using Docker Command

```sh
# Replace /path/to/config with your local config directory
docker run \
  --name yamoh \
  -v /path/to/config:/config \
  -e YAMOH__PLEXURL="http://plex:32400" \
  -e YAMOH__PLEXTOKEN="your_plex_token" \
  -e YAMOH__MAINTAINERRURL="http://maintainerr:6246" \
  yamoh:latest
```

### Using Docker Compose

```yaml
version: '3.8'
services:
  yamoh:
    image: yamoh:latest
    container_name: yamoh
    environment:
      YAMOH__PLEXURL: "http://plex:32400"
      YAMOH__PLEXTOKEN: "your_plex_token"
      YAMOH__MAINTAINERRURL: "http://maintainerr:6246"
    volumes:
      - /path/to/config:/config
```

- Replace `/path/to/config` with the path to your local config directory.
- All configuration can be set via environment variables or files in the `/config` directory.

## Configuration Table

> [!Important]
> All paths (such as TempImagePath, AssetBasePath, BackupImagePath, FontPath) can be specified as either relative to the config directory or as absolute paths.
>
> The config directory is determined at runtime as follows:
> - **Docker:** `/config/`
> - **Windows:** `%ProgramData%\YAMOH\Config` (typically `C:\ProgramData\YAMOH\Config`)
> - **Linux/macOS:** `$HOME/.config/YAMOH/Config` (typically `/home/<user>/.config/YAMOH/Config` or `/Users/<user>/.config/YAMOH/Config`)

| Config Key                | Environment Variable         | Default Value         | Description |
|---------------------------|-----------------------------|-----------------------|-------------|
| Yamoh:PlexUrl             | YAMOH__PLEXURL              | ""                    | Plex server URL. Include port if required (e.g. `http://plex:32400/`) |
| Yamoh:PlexToken           | YAMOH__PLEXTOKEN            | ""                    | Plex API token. See [Finding a Plex Token](https://support.plex.tv/articles/204059436-finding-an-authentication-token-x-plex-token/) |
| Yamoh:MaintainerrUrl      | YAMOH__MAINTAINERRURL        | ""                    | Maintainerr API URL. Include port if required (e.g. `http://maintainerr:6246`)|
| Yamoh:TempImagePath       | YAMOH__TEMPIMAGEPATH         | "Temp"                | Temporary image folder |
| Yamoh:UseAssetMode        | YAMOH__USEASSETMODE          | true                  | FUTURE: Store overlays in asset directory (true) or manage in Plex (false) |
| Yamoh:AssetBasePath       | YAMOH__ASSETBASEPATH         | "assets"              | Base path for asset overlays |
| Yamoh:BackupImagePath     | YAMOH__BACKUPIMAGEPATH       | "assetsbackup"        | Path for backup of original posters |
| Yamoh:FontPath            | YAMOH__FONTPATH              | "Fonts"               | Path to font files |
| Yamoh:FontName            | YAMOH__FONTNAME              | "AvenirNextLTPro-Bold"| Font name for overlay text |
| Yamoh:FontColor           | YAMOH__FONTCOLOR             | "#FFFFFF"             | Overlay text color |
| Yamoh:FontTransparency    | YAMOH__FONTTRANSPARENCY      | 0.70                  | Overlay text transparency. Percentage value from 0.00 (Fully transparent) - 1.00 (Opaque) |
| Yamoh:BackColor           | YAMOH__BACKCOLOR             | "#7f161b"             | Overlay background color |
| Yamoh:BackTransparency    | YAMOH__BACKTRANSPARENCY      | 0.70                  | Overlay background transparency. Percentage value from 0.00 (Fully transparent) - 1.00 (Opaque) |
| Yamoh:FontSize            | YAMOH__FONTSIZE              | 75                    | Overlay text font size |
| Yamoh:Padding             | YAMOH__PADDING               | 20                    | Padding around overlay text in background container |
| Yamoh:BackRadius          | YAMOH__BACKRADIUS            | 0                     | Overlay background corner radius |
| Yamoh:HorizontalOffset    | YAMOH__HORIZONTALOFFSET      | 0                     | Horizontal offset for overlay |
| Yamoh:HorizontalAlign     | YAMOH__HORIZONTALALIGN       | "center"              | Horizontal alignment. left, center, right |
| Yamoh:VerticalOffset      | YAMOH__VERTICALOFFSET        | 0                     | Vertical offset for overlay |
| Yamoh:VerticalAlign       | YAMOH__VERTICALALIGN         | "bottom"              | Vertical alignment, top, center, bottom |
| Yamoh:BackWidth           | YAMOH__BACKWIDTH             | 1920                  | Overlay background width |
| Yamoh:BackHeight          | YAMOH__BACKHEIGHT            | 0                     | Overlay background height |
| Yamoh:DateFormat          | YAMOH__DATEFORMAT            | "MMM d"               | DateTime format for overlay. See [Date and Time Format Strings](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings) for examples |
| Yamoh:OverlayText         | YAMOH__OVERLAYTEXT           | "Leaving"             | Overlay text prefix for date |
| Yamoh:EnableDaySuffix     | YAMOH__ENABLEDAYSUFFIX       | true                  | Show day suffix in overlay (e.g. 12**th**, 31**st**, etc.) |
| Yamoh:EnableUppercase     | YAMOH__ENABLEUPPERCASE       | true                  | Uppercase overlay text |
| Yamoh:Language            | YAMOH__LANGUAGE              | "en-US"               | Overlay language for rendering the DateTime string. |
| Yamoh:ReapplyOverlays     | YAMOH__REAPPLYOVERLAYS       | false                 | Force reapply overlays |
| Yamoh:OverlayShowSeasons  | YAMOH__OVERLAYSHOWSEASONS    | true                  | Apply overlay to a Show's seasons (if Maintainerr collection is Show type) |
| Yamoh:OverlaySeasonEpisodes| YAMOH__OVERLAYSEASONEPISODES | true                  | Apply overlay to a Season's episodes (if Maintainerr collection is Season type, or OverlayShowSeasons == true ) |
| Yamoh:RestoreOnly         | YAMOH__RESTOREONLY           | false                 | Only restore original posters, do not apply overlays. Helpful to roll back changes made by this application |
| Schedule:Enabled          | SCHEDULE__ENABLED            | true                  | Enable scheduled overlay runs based on cron schedule. Otherwise Yamoh only works with cli arguments. |
| Schedule:RunOnStart       | SCHEDULE__RUNONSTART         | false                 | FUTURE: Run overlay manager on app start. Run on app start, and on cron schedule afterwards |
| Schedule:OverlayManagerCronSchedule | SCHEDULE__OVERLAYMANAGERCRONSCHEDULE | "30 * * * *" | Cron schedule for overlay manager.|

> Environment variable names use double underscores (`__`) to represent nested config keys.


## Contributing
Pull requests and issues are welcome! Please see `.github/ISSUE_TEMPLATE` for bug reports.

## License
MIT
