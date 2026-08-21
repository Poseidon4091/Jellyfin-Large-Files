# Jellyfin Large Files

A Jellyfin plugin that lists the largest media per library (Movies, TV Shows, Anime, etc), with CSV export, to help find oversized media worth trimming.

Movies are listed individually. TV/anime episodes are rolled up into per-series totals, so a 40-season show shows up as one row with its combined size across every season, not hundreds of episode rows.

## Features

- Admin dashboard page listing the top N largest items, grouped by library
- Series roll-up for TV/anime (per-show total, not per-episode)
- Filter by minimum size
- Export a single category or everything to CSV

## Install (recommended — plugin repository)

1. In Jellyfin: **Dashboard → Plugins → Repositories → Add Repository**.
2. Repository URL:
   `https://raw.githubusercontent.com/Poseidon4091/Jellyfin-Large-Files/main/manifest.json`
3. Go to **Catalog**, find **Largest Files** under General, install it.
4. Restart Jellyfin.
5. It shows up in the admin dashboard nav as "Largest Files".

Updates from then on show up in the normal Jellyfin plugin catalog/update flow.

## Install (manual)

1. Download the zip from [Releases](https://github.com/Poseidon4091/Jellyfin-Large-Files/releases).
2. Unzip into your Jellyfin server's `plugins/Largest Files_<version>/` folder.
3. Restart Jellyfin.

## Build from source

Requires the .NET 9 SDK.

```
cd Jellyfin.Plugin.LargestFiles
dotnet build
```

## Cutting a release (maintainer)

Push a tag matching `vX.X.X.X` (matching the assembly version scheme, e.g. `v1.0.0.0`). A GitHub Actions workflow builds the plugin, creates a GitHub release with the zip attached, and updates `manifest.json` on `main` automatically — nothing manual required after that.

## API

- `GET /LargestFiles?perCategoryLimit=100&minSizeMb=0` — JSON, grouped by library
- `GET /LargestFiles/Csv?perCategoryLimit=100&minSizeMb=0` — CSV download

Both require an authenticated admin user.
