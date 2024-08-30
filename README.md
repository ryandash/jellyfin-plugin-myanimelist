<h1 align="center">Jellyfin MyAnimeList Plugin</h1>

## About

This plugin adds the metadata provider for [MyAnimeList using Jikan](https://jikan.moe/).

## Installation

### Automatic (recommended)
1. Navigate to Settings > Admin Dashboard > Plugins > Repositories
2. Add a new repository with a `Repository URL` of `https://raw.githubusercontent.com/ryandash/jellyfin-plugin-myanimelist/main/manifest.json`. The name can be anything you like.
3. Save, and navigate to Catalogue.
4. myanimelist should be present. Click on it and install the latest version.

### Manual

[See the official Jellyfin documentation for install instructions](https://jellyfin.org/docs/general/server/plugins/index.html#installing).

1. Download a version from the [releases tab](https://github.com/jellyfin/jellyfin-plugin-anilist/releases) that matches your Jellyfin version.
2. Extract the zip file.
3. Copy the `Jellyfin.Plugin.MyAnimeList.dll` file into `plugins/myanimelist` (see above official documentation on where to find the `plugins` folder).
4. Restart your Jellyfin instance.
5. Navigate to Plugins in Jellyfin (Settings > Admin Dashboard > Plugins).
6. Adjust the settings accordingly.

## Licence

This plugins code and packages are distributed under the GPLv2 License. See [LICENSE](./LICENSE) for more information.