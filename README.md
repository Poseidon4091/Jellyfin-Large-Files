# Jellyfin Large Files

A Jellyfin plugin that lists the largest media files per library (Movies, TV Shows, Anime, etc), with CSV export, to help find oversized media worth trimming.

## Features

- Admin dashboard page listing the top N largest files, grouped by library
- Filter by minimum file size
- Export a single category or everything to CSV

## Build

Requires the .NET 9 SDK.

```
cd Jellyfin.Plugin.LargestFiles
dotnet build
```

## Install

1. Build the plugin (or grab a release build) to get `Jellyfin.Plugin.LargestFiles.dll`.
2. Copy it into your Jellyfin server's `plugins/LargestFiles_1.0.0.0/` folder.
3. Restart Jellyfin.
4. Open the admin dashboard and look for "Largest Files".

## API

- `GET /LargestFiles?perCategoryLimit=100&minSizeMb=0` — JSON, grouped by library
- `GET /LargestFiles/Csv?perCategoryLimit=100&minSizeMb=0` — CSV download

Both require an authenticated admin user.
