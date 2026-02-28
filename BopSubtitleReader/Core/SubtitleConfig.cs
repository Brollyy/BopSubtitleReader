using BepInEx.Configuration;

namespace BopSubtitleReader.Core;

public sealed class SubtitleConfig(ConfigFile config)
{
	public ConfigEntry<bool> Enabled { get; } =
		config.Bind("Subtitles", "Enabled", true, "Master toggle for subtitle rendering.");

	public ConfigEntry<string> PreferredLanguage { get; } = config.Bind(
		"Subtitles",
		"PreferredLanguage",
		"",
		"Override language code (e.g., en, ja, fr). Empty uses game language.");

	public ConfigEntry<bool> UseGameLanguage { get; } = config.Bind("Subtitles", "UseGameLanguage", true,
		"Resolve language from the game localization state.");

	public ConfigEntry<bool> FallbackToEnglish { get; } = config.Bind(
		"Subtitles",
		"FallbackToEnglish",
		true,
		"If selected language is missing, use English subtitles when available.");

	public ConfigEntry<bool> FallbackToDefaultTrack { get; } = config.Bind(
		"Subtitles",
		"FallbackToDefaultTrack",
		true,
		"If selected language and English are missing, use catalog default language.");

	public ConfigEntry<bool> KaraokeEnabled { get; } = config.Bind(
		"Subtitles",
		"KaraokeEnabled",
		true,
		"Enable karaoke tag timing effects in subtitle text when segment data is available.");

	public ConfigEntry<bool> UseAssStyles { get; } = config.Bind(
		"Subtitles",
		"UseAssStyles",
		true,
		"Apply ASS/SSA style defaults and inline overrides. Disable to force the configured display style below.");

	public ConfigEntry<string> DisplayFontName { get; } = config.Bind(
		"SubtitleDisplay",
		"FontName",
		"",
		"Preferred subtitle font name. Empty uses built-in TMP defaults.");

	public ConfigEntry<float> DisplayFontSize { get; } = config.Bind(
		"SubtitleDisplay",
		"FontSize",
		40f,
		"Default subtitle font size.");

	public ConfigEntry<string> DisplayColorHexRgba { get; } = config.Bind(
		"SubtitleDisplay",
		"ColorHexRgba",
		"#FFFFFFFF",
		"Default subtitle text color in #RRGGBBAA format.");

	public ConfigEntry<string> DisplaySecondaryColorHexRgba { get; } = config.Bind(
		"SubtitleDisplay",
		"SecondaryColorHexRgba",
		"#FFFF00FF",
		"Default karaoke pre-highlight color in #RRGGBBAA format.");

	public ConfigEntry<string> DisplayOutlineColorHexRgba { get; } = config.Bind(
		"SubtitleDisplay",
		"OutlineColorHexRgba",
		"#000000FF",
		"Default subtitle outline color in #RRGGBBAA format.");

	public ConfigEntry<float> DisplayOutlineWidth { get; } = config.Bind(
		"SubtitleDisplay",
		"OutlineWidth",
		2f,
		"Default subtitle outline width.");

	public ConfigEntry<bool> DisplayBold { get; } = config.Bind(
		"SubtitleDisplay",
		"Bold",
		false,
		"Render subtitles in bold by default.");

	public ConfigEntry<bool> DisplayItalic { get; } = config.Bind(
		"SubtitleDisplay",
		"Italic",
		false,
		"Render subtitles in italic by default.");

	public ConfigEntry<int> DisplayAlignment { get; } = config.Bind(
		"SubtitleDisplay",
		"Alignment",
		2,
		new ConfigDescription(
			"Default subtitle alignment using ASS numpad values (1-9).",
			new AcceptableValueRange<int>(1, 9)));
}
