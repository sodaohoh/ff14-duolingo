# Cast Bar Translator

A Dalamud plugin that displays bilingual action names on cast bars — learn FFXIV skill names in different languages while you play!

## What It Does

When an enemy (or ally) is casting an action, this plugin displays the action name in **two languages** of your choice. It's like Duolingo, but for FFXIV!

**Example (Japanese + English):**
```
ファイジャ      ← Learning target
Fire IV         ← Native/reference
```

**Example (Chinese + Japanese):**
```
火焰           ← Learning target
ファイア       ← Native/reference
```

You can mix and match **any two languages** from the supported list!

## Supported Languages

| Language | Source |
|----------|--------|
| English | Game Data |
| Japanese | Game Data |
| German | Game Data |
| French | Game Data |
| Traditional Chinese | External JSON (auto-updated weekly) |

## Features

- Works on **Target**, **Target Cast Bar**, and **Focus Target** frames
- Configurable cast bar height for two-line display
- Automatic Traditional Chinese data updates via GitHub Actions
- Minimal performance impact with smart caching

## Installation

### Prerequisites

**System Requirements:**
- Windows 10 or higher
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022+ with ".NET Desktop Development" workload
- Git

**Game Requirements:**
- FINAL FANTASY XIV installed
- [XIVLauncher](https://goatcorp.github.io/) installed
- Game launched with Dalamud enabled at least once (this downloads dev files to `%appdata%\XIVLauncher\addon\Hooks\dev\`)

**Optional:**
If XIVLauncher is not installed in the default location, set the environment variable:
```
DALAMUD_HOME=C:\your\path\addon\Hooks\dev\
```

### Building from Source

```bash
# Verify .NET version (should be 10.0.x)
dotnet --version

# Clone and build
git clone https://github.com/your-username/CastBarTranslator.git
cd CastBarTranslator
dotnet build CastBarTranslator.sln
```

Output: `CastBarTranslator/bin/x64/Debug/CastBarTranslator.dll`

### Install from Third-Party Repo (Recommended)

1. In-game, open Dalamud settings with `/xlsettings`
2. Go to **Experimental** tab
3. Scroll down to **Custom Plugin Repositories**
4. Add this URL:
   ```
   https://raw.githubusercontent.com/sodaohoh/ff14-duolingo/refs/heads/master/repo.json
   ```
5. Save and close settings
6. Open Plugin Installer with `/xlplugins`
7. Search for "Cast Bar Translator" and install

### Install from Source (Development)

1. Build the plugin (see above)
2. In-game, open Dalamud settings with `/xlsettings`
3. Go to **Experimental** tab
4. Add the path to `CastBarTranslator.dll` in Dev Plugin Locations
5. Open Plugin Installer with `/xlplugins`
6. Enable **Cast Bar Translator** under Dev Tools > Installed Dev Plugins

## Configuration

Open the plugin settings to:
- Select **Top Language** (the language you want to learn)
- Select **Bottom Language** (your native/reference language)
- Adjust cast bar height (30-60px)
- Reload Chinese data manually if needed

## How It Works

```
Target Casting → Get Action ID → Lookup Both Languages → Display Bilingual Text
                                        ↓
                        ┌───────────────┴───────────────┐
                        ↓                               ↓
                   Top Language                   Bottom Language
                   (Learning)                     (Reference)
                        ↓                               ↓
              Game Data or JSON              Game Data or JSON
```

## Data Updates

Traditional Chinese translations are automatically updated every Tuesday at 10:00 UTC via GitHub Actions, syncing with FFXIV's typical maintenance schedule.

Data source: [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn)

## Project Structure

```
ff14-duolingo/
├── CastBarTranslator/          # C# plugin source
│   ├── Windows/
│   │   └── ConfigWindow.cs     # Settings UI
│   ├── Configuration.cs        # Plugin settings
│   └── Plugin.cs               # Main plugin logic
├── scripts/
│   └── generate_translations.py  # Chinese data generator
├── data/
│   └── goat.png                # Plugin icon
└── actions_zhtw.json           # Traditional Chinese translations
```

## Known Limitations

### Chinese Font Support

The game's native UI fonts do not support CJK (Chinese/Japanese/Korean) characters. This means:

- **English, German, French**: Display correctly on cast bars
- **Japanese**: Uses game's built-in Japanese font support
- **Traditional Chinese**: May display as boxes or garbled text on cast bars

The Chinese data is still useful for:
- Reference in the config window (which uses Dalamud's font)
- Future ImGui overlay implementation

## License

AGPL-3.0-or-later

## Contributing

Issues and pull requests are welcome!

## Acknowledgments

- [Dalamud](https://github.com/goatcorp/Dalamud) - Plugin framework
- [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) - Chinese game data
- [OpenCC](https://github.com/BYVoid/OpenCC) - Simplified/Traditional Chinese conversion
