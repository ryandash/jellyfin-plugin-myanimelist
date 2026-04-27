<h1 align="center">Jellyfin MyAnimeList Metadata Plugin</h1>

## About

This plugin adds the metadata provider for MyAnimeList using [Jikan](https://jikan.moe/) and MyAnimeList's public API.

#### Please report any issues you find.

To automate syncing with MyAnimeList using this metadata you can try using [MyAnimeList Sync](https://github.com/ryandash/jellyfin-myanimelist-sync) which is a fork of [vosmiic/jellyfin-ani-sync](https://github.com/vosmiic/jellyfin-ani-sync) customized to use the MyAnimeList metadata.

Folders must be formatted as shown below to get the best results.\
For accurate data use the anime name from MyAnimeList and Season 01 or the first season anime's name with the anime's season number.

```bash
Anime
└── Anime Name A
    ├── Specials
    │   └── Anime Name - S00E0# - Special Title.mkv
    ├── Season 01
    │   ├── Anime Name A - S01E01-E02.mkv
    │   └── Anime Name A - S01E03.mkv
    └── Season 02
        ├── Anime Name A - S02E01.mkv
        ├── Anime Name A - S02E02 Part 1.mkv
        └── Anime Name A - S02E02 Part 2.mkv
```

#### Example Special episode + special movie:
```bash
Anime
└── Violet Evergarden
    └── Specials
        ├── Violet Evergarden - S00E01 - The Day You Understand I Love You Will Surely Come.mkv
        └── Violet Evergarden - S00E01 - Recollections.mkv
```
###### Note: If a special has multiple episodes the episode number needs to be incremented to select the correct special.

#### Optional: MyAnimeList ID in Folder Name

You can optionally include a MyAnimeList ID directly in the series folder name that will be used instead of searching.

Format:
Anime Name [mal-<mal_id>]
Example:
```bash
Anime
└── Attack on Titan [mal-16498]
    └── Season 01
        └── Attack on Titan - S01E01.mkv
```

## Installation

### Automatic Installation (Recommended)

1. Open **Settings** → **Dashboard** → **Plugins** → **Repositories**
2. Click **Add Repository**, then enter:
   - **Name:** Any name you prefer (e.g. `MyAnimeList Metadata Plugin`)
   - **Repository URL:**
     ```bash
     https://raw.githubusercontent.com/ryandash/jellyfin-plugin-myanimelist/refs/heads/main/manifest.json
     ```
3. Click **Save**
4. Restart **Jellyfin Server**
5. Go to **Settings** → **Dashboard** → **Plugins** → **Available**
6. Find **MyAnimeList**, select it, and click **Install** (latest version)
7. Return to **Plugins** (**Settings** → **Dashboard** → **Plugins**) to confirm it appears in your installed plugins list

---

### Manual Installation

> For general plugin installation details, see the [official Jellyfin documentation](https://jellyfin.org/docs/general/server/plugins/index.html#installing).

1. Download the latest release from the [Releases page](https://github.com/ryandash/jellyfin-plugin-myanimelist/releases)
2. Extract the downloaded `.zip` file
3. Copy all extracted `.dll` files into: `plugins/myanimelist`
    > Refer to the [official plugin directory guide](https://jellyfin.org/docs/general/server/plugins/) if you're unsure where this folder is located
4. Restart **Jellyfin Server**
5. Navigate to **Settings** → **Dashboard** → **Plugins** to verify the plugin is installed

---

### Building from Visual Studio

1. Clone this repository:
```bash
git clone https://github.com/ryandash/jellyfin-plugin-myanimelist.git
```
2. Install Visual Studio with the .NET Desktop Development workload
4. Open the solution file in Visual Studio
5. Restore any missing dependencies, build the project, and check the Jellyfin plugin directory
    > Refer to the [official plugin directory guide](https://jellyfin.org/docs/general/server/plugins/) if you're unsure where this folder is located
    - Default output path for building was set in the project file as: `C:\ProgramData\Jellyfin\Server\plugins\Jellyfin.Plugin.MyAnimeList`
    - If the plugin is not output to the correct plugin location check the log for the output directory or change the default debug build location
7. Restart **Jellyfin Server**
8. Go to **Settings** → **Dashboard** → **Plugins** to confirm the plugin is installed

## Licence

This plugins code and packages are distributed under the GPLv2 License. See [LICENSE](./LICENSE) for more information.
