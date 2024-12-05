<h1 align="center">Jellyfin MyAnimeList Plugin</h1>

## About

This plugin adds the metadata provider for [MyAnimeList using Jikan](https://jikan.moe/).

Folders must be formatted as shown below to get valid season information:
For accurate data use the anime name from MyAnimeList and Season 01 or the season 01 anime's name with the anime's season number
```
Anime
├── Anime Name A
│   ├── Season 00
│   │   ├── Some Special.mkv
│   │   ├── Anime Name A S00E01.mkv
│   │   └── Anime Name A S00E02.mkv
│   ├── Season 01
│   │   ├── Anime Name A S01E01-E02.mkv
│   │   ├── Anime Name A S01E03.mkv
│   │   └── Anime Name A S01E04.mkv
│   └── Season 02
│       ├── Anime Name A S02E01.mkv
│       ├── Anime Name A S02E02.mkv
│       ├── Anime Name A S02E03 Part 1.mkv
│       └── Anime Name A S02E03 Part 2.mkv
└── Anime Name B
    ├── Season 01
    |   ├── Anime Name B S01E01.mkv
    |   └── Anime Name B S01E02.mkv
    └── Season 02
        ├── Anime Name B S02E01-E02.mkv
        └── Anime Name B S02E03.mkv
```
e.g. of folders with file
```
Anime
├── Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka V: Houjou no Megami-hen
│   ├── Season 01
│   │   ├── Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka V: Houjou no Megami-hen S01E01.mkv
```
or 
```
Anime
├── Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka
│   ├── Season 05
│   │   ├── Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka S05E01.mkv
```
## Installation

### Automatic (recommended)
1. Navigate to Settings > Admin Dashboard > Plugins > Repositories
2. Add a new repository with a `Repository URL` of `https://raw.githubusercontent.com/ryandash/jellyfin-plugin-myanimelist/main/manifest.json`. The name can be anything you like.
3. Save, and navigate to Catalogue.
4. myanimelist should be present. Click on it and install the latest version.
5. Navigate to Plugins in Jellyfin (Settings > Admin Dashboard > Plugins) to verify installation.

### Manual

[See the official Jellyfin documentation for install instructions](https://jellyfin.org/docs/general/server/plugins/index.html#installing).

1. Download a version from the [releases tab](https://github.com/jellyfin/jellyfin-plugin-anilist/releases) that matches your Jellyfin version.
2. Extract the zip file.
3. Copy the dll files into `plugins/myanimelist` (see [official](https://jellyfin.org/docs/general/server/plugins/) documentation on where to find the plugins folder).
4. Restart your Jellyfin instance.
5. Navigate to Plugins in Jellyfin (Settings > Admin Dashboard > Plugins) to verify installation.

### Building from visual studio

1. Git clone the latest version of this repository, [AnitomySharp](https://github.com/Xabis/AnitomySharp), and [jikan.net](https://github.com/Ervie/jikan.net), or any fork of the repositories
2. Download and install visual studio with .Net desktop development
3. Build AnitomySharp and jikan.net
4. Open my repository solution and add the missing references and build
5. Copy all dll files from the output bin directory to Jellyfins Plugin directory under plugins/myanimelist
6. Restart Jellyfin Server (Administration> Dashboard > Restart) and navigate to Plugins in Jellyfin (Administration > Dashboard > My Plugins) to verify installation.

## Licence

This plugins code and packages are distributed under the GPLv2 License. See [LICENSE](./LICENSE) for more information.
