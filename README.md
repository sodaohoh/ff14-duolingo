# Cast Bar Translator

[![Build Plugin](https://github.com/sodaohoh/ff14-duolingo/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/sodaohoh/ff14-duolingo/actions/workflows/build.yml)
[![Latest Release](https://img.shields.io/github/v/release/sodaohoh/ff14-duolingo?display_name=tag)](https://github.com/sodaohoh/ff14-duolingo/releases/latest)
[![License](https://img.shields.io/github/license/sodaohoh/ff14-duolingo)](LICENSE.md)

A Dalamud plugin that adds a configurable translated second line to FFXIV cast bars.

## What It Does

Cast Bar Translator adds a translation beneath the native cast name on the Target Cast Bar and Focus Target. The first line is FFXIV's own game text: the plugin does not select, replace, or rewrite it. You choose the language for the plugin-owned second line.

Example:

```text
ファイジャ
Fire IV
```

First line: native FFXIV cast text. Second line: configured translation (English).

## Features

- Preserves the game's native cast text unchanged.
- Adds an independent, plugin-owned translated line beneath it.
- Lets you choose the second-line language.
- Supports Target Cast Bar and Focus Target.
- Adapts font size for longer translated names to fit the available text width where possible.
- Refreshes translation data automatically through the scheduled GitHub Actions workflow.

## Supported Second-Line Languages

- English
- Japanese
- German
- French
- Traditional Chinese

English, Japanese, German, and French data are generated from the corresponding `Action.csv` files in [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining). Traditional Chinese data comes from [thewakingsands/ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) and is converted from Simplified Chinese with [OpenCC](https://github.com/BYVoid/OpenCC) using `s2twp`.

## Installation

1. In FFXIV, open Dalamud settings with `/xlsettings`.
2. Open **Experimental** and add this URL under **Custom Plugin Repositories**:

   ```text
   https://raw.githubusercontent.com/sodaohoh/ff14-duolingo/refs/heads/master/repo.json
   ```

3. Save the settings, then open the plugin installer with `/xlplugins`.
4. Find **Cast Bar Translator** and install it.

## Configuration

Open Cast Bar Translator settings in the Dalamud plugin installer. Choose **Second Language** for the plugin-owned translated line. Use **Reload Data** to reload translation data.

## How It Works

```text
Target / Focus target casting
            |
            v
      Cast Action ID
            |
            v
TranslationService lookup
 using Second Language
            |
            v
Plugin-owned text node below
 native game cast text
```

The translation lookup uses the cast action ID and configured second-line language. The native game text remains untouched.

## Translation Data / Data Updates

The update workflow regenerates translation data every Tuesday at 10:00 UTC and commits changes when data differs. English, Japanese, German, and French use `Action.csv` from [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining). Traditional Chinese uses [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) and [OpenCC](https://github.com/BYVoid/OpenCC).

## Development

### Prerequisites

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- XIVLauncher and a Dalamud development environment
- Git
- Visual Studio or another IDE (optional)

### Build from Source

```bash
git clone https://github.com/sodaohoh/ff14-duolingo.git
cd ff14-duolingo
dotnet build CastBarTranslator.sln
```

Build output: `CastBarTranslator/bin/x64/Debug/CastBarTranslator.dll`

### Project Structure

```text
CastBarTranslator/
  Features/
  Presentation/
  Translation/
  Windows/
CastBarTranslator.Tests/
data/
scripts/
repo.json
```

## Data Notes / Known Limitations

Traditional Chinese cast-bar rendering was validated in v0.0.0.3. The Traditional Chinese data is derived from Simplified Chinese source data and converted with OpenCC, so terminology is not guaranteed to match an official Traditional Chinese / Taiwan localization exactly.

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md).

## Acknowledgements

- [Dalamud](https://github.com/goatcorp/Dalamud) — plugin framework
- [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining) — English, Japanese, German, and French action data
- [thewakingsands/ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) — Chinese action data
- [OpenCC](https://github.com/BYVoid/OpenCC) — Simplified-to-Traditional Chinese conversion
