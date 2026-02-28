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
}
