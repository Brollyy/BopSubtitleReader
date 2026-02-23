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

### 1. SRT (creator-friendly)

Use standard `.srt` files exported from common subtitle tools.

SRT is time-based. At runtime, the mod converts subtitle seconds to chart beats using `JukeboxScript.SecondsToBeats(double)`, so tempo changes are respected automatically.

### 2. ASS/SSA

ASS/SSA `Dialogue` lines are supported and converted with the same runtime `SecondsToBeats` logic as SRT.

Supported karaoke tags:
- `\k`
- `\K`
- `\kf`
- `\ko`

Supported style inputs:
- style defaults from `[V4+ Styles]` / `[V4 Styles]`: `Fontname`, `Fontsize`, `PrimaryColour`, `Bold`, `Italic`, `Alignment`
- per-line override tags: `\fn`, `\fs`, `\c` / `\1c`, `\b`, `\i`, `\an`

These tags are mapped onto the TMP overlay (font size, color, bold/italic, alignment). Unsupported ASS drawing/effect tags are ignored safely.


### 3. JSON (recommended for precise chart sync)

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

## Karaoke / Meet & Tweet feasibility notes

Game metadata inspection confirms Meet & Tweet chart templates include:

- `meetAndTweet/set lyrics`
- `meetAndTweetSky/set lyrics`

and `MeetAndTweetScript` contains `lyrics` / `lyrics2` sprite renderers.

In this mod:

- `KaraokeMode=Auto` attempts to reuse in-game karaoke-like indicator paths when discoverable at runtime.
- If unavailable, subtitles still render via overlay fallback.
- The custom karaoke ball indicator uses bundled sprite asset `assets/karaoke_bop.png`.

Subtitle rendering uses a TextMeshPro-based overlay (`TextMeshProUGUI`) so styling/alignment follows the game's TMP-centric UI stack.

## Config

`[Subtitles]`

- `Enabled=true`
- `PreferredLanguage=`
- `UseGameLanguage=true`
- `FallbackToEnglish=true`
- `FallbackToDefaultTrack=true`
- `KaraokeMode=Auto` (`Off`, `Auto`, `Force`)
- `TimelineReferenceBpm=120`

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

Windows:
```cmd
scripts/setup-git-hooks.bat
```

Linux/macOS:
```bash
./scripts/setup-git-hooks.sh
```
