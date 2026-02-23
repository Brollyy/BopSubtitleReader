# Subtitle Sample Set

This folder contains parser-focused subtitle samples for `BopSubtitleReader`.

## Files

`subtitles.en.srt`
- Covers SRT index + non-index blocks, comma and dot millisecond separators, multiline cue text.

`subtitles.en.ass`
- Covers ASS sections (`[V4+ Styles]`, `[Events]`), style defaults, inline style overrides (`\fs`, `\c`/`\1c`, `\b`, `\i`, `\an`), and karaoke timing tags (`\k`, `\K`, `\kf`, `\ko`).

`subtitles.json`
- Covers JSON multi-track object shape with `defaultLanguage`, `tracks`, beat-timed cues, seconds-timed cues, karaoke segments with `beat` and `seconds`, and string-number numeric values.

`subtitles.en.json`
- Covers JSON single-track object shape (`language` + `cues`) with mixed beat/seconds timing.

`lyrics.json`
- Covers JSON root-array cue shape.
