# BopSubtitleReader

BepInEx 5.x mod for Bits & Bops that adds chart subtitles loaded from `.bop` / `.riq` archives (or extracted chart folders).

## Adding subtitles

Add one or more files inside your chart archive/folder:

- `subtitles.en.srt`
- `subtitles.ja.srt`
- `subtitles.es.ass`
- `subtitles.json`
- `subtitles.en.json`

Supported filename patterns:
- `subtitles.<lang>.<ext>`
- `subtitles.<ext>`
- `subtitle.<lang>.<ext>`
- `lyrics.<lang>.<ext>`

Supported extensions:
- `.srt`
- `.ass` / `.ssa` (supports styling / karaoke timing)
- `.json` (custom format based on beats, best for rhythm precision)


`<lang>` should be an ISO-like code such as `en`, `ja`, `fr`, `pt-br`.

## Subtitle file formats

### 1. SRT

Use standard `.srt` files exported from common subtitle tools. SRT is time-based.

### 2. ASS/SSA

ASS/SSA is supported, with V4 ScriptType, and 1920x1680 resolution.

Supported karaoke tags: `\k`, `\K`, `\kf`, `\ko`.

Supported style inputs:
- style defaults from `[V4+ Styles]` / `[V4 Styles]`: `Fontname`, `Fontsize`, `PrimaryColour`, `Bold`, `Italic`, `Alignment`
- per-line override tags: `\fn`, `\fs`, `\c`, `\1c`, `\b`, `\i`, `\an`

These tags are mapped onto the [TMP overlay](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/TextMeshPro/index.html) (font size, color, bold/italic, alignment). Unsupported ASS drawing/effect tags are ignored.


### 3. JSON

This is a custom subtitle format - it allows for timing subtitles to beats of the song (respects BPM changes).

Single-track:

```json
{
  "language": "en",
  "cues": [
    {
      "startBeat": 12.0,
      "endBeat": 16.0,
      "text": "Ready, set, go!"
    },
    {
      "startBeat": 20.0,
      "endBeat": 24.0,
      "text": "Bounce with me",
      "karaoke": [
        { "beat": 20.0, "text": "Bounce" },
        { "beat": 22.0, "text": "with me" }
      ]
    }
  ]
}
```

Multi-track:

```json
{
  "defaultLanguage": "en",
  "tracks": {
    "en": [
      { "startBeat": 12, "endBeat": 16, "text": "Ready, set, go!" }
    ],
    "ja": [
      { "startBeat": 12, "endBeat": 16, "text": "よーい、スタート！" }
    ]
  }
}
```

## Language selection + fallback

Language resolution order:

1. `PreferredLanguage` (if set)
2. game language (if `UseGameLanguage=true`)
3. `en` (if `FallbackToEnglish=true`)
4. `defaultLanguage` from subtitle catalog (if `FallbackToDefaultTrack=true`)

## Config

Config file location:

- `<GameRoot>/BepInEx/config/BopSubtitleReader.cfg`

### `[Subtitles]`

| Key | Type / Values | What it does |
| --- | --- | --- |
| `Enabled` | `true`/`false` | Master toggle for all subtitle rendering. |
| `PreferredLanguage` | string | Force a language code like `en`, `ja`, `fr`. Leave empty to use automatic language resolution. |
| `UseGameLanguage` | `true`/`false` | If `true`, attempts to use the game's current language. |
| `FallbackToEnglish` | `true`/`false` | If selected language is missing, tries English (`en`). |
| `FallbackToDefaultTrack` | `true`/`false` | If selected language and English are missing, uses subtitle catalog `defaultLanguage`. |
| `KaraokeEnabled` | `true`/`false` | Enables karaoke timing effects when karaoke segment data exists. |
| `UseAssStyles` | `true`/`false` | If `true`, ASS/SSA style defaults and inline style tags are applied. If `false`, ASS styles are ignored and display uses the values from `[SubtitleDisplay]`. |

### `[SubtitleDisplay]`

These are the base display settings used by the TMP subtitle overlay.

| Key | Type / Values | What it does |
| --- | --- | --- |
| `FontName` | string | Preferred font name. Leave empty to use the mod's default TMP font selection. |
| `FontSize` | float | Default subtitle text size. |
| `ColorHexRgba` | color string | Default text color in `#RRGGBBAA`. |
| `SecondaryColorHexRgba` | color string | Default secondary karaoke color in `#RRGGBBAA` (pre-highlight color for karaoke tags). |
| `OutlineColorHexRgba` | color string | Default outline color in `#RRGGBBAA`. |
| `OutlineWidth` | float | Default outline thickness. |
| `Bold` | `true`/`false` | Render subtitles in bold by default. |
| `Italic` | `true`/`false` | Render subtitles in italic by default. |
| `Alignment` | `1..9` | ASS numpad alignment (`1` bottom-left, `2` bottom-center, `3` bottom-right, `4` mid-left, `5` center, `6` mid-right, `7` top-left, `8` top-center, `9` top-right). |

## Contributing

### Local setup

1. Copy `BopSubtitleReader/BopSubtitleReader.user.props.example` to `BopSubtitleReader/BopSubtitleReader.user.props`.
2. Set `GameRoot` in `BopSubtitleReader/BopSubtitleReader.user.props` to your Bits & Bops install path.
3. Build with `dotnet build BopSubtitleReader.sln`.

Derived paths:
- `BepInExPluginsDir = <GameRoot>/BepInEx/plugins`
- `UnityManagedDir = <GameRoot>/Bits & Bops_Data/Managed`

If `BepInExPluginsDir` exists, build output is copied there automatically.

### Pull requests

Formatting, code style rules and analyzer rules are enforced on pull requests.
To enable auto-format on commit locally, run the attached script once after cloning the repository.

Windows:
```cmd
scripts/setup-git-hooks.bat
```

Linux/macOS:
```bash
./scripts/setup-git-hooks.sh
```
