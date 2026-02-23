using System;
using System.Text.Json;
using BopSubtitleReader.Core;

namespace BopSubtitleReader.Parser;

public sealed class JsonSubtitleParser : ISubtitleParser
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<JsonSubtitleParser>();

	public string Name => "JSON";

	public bool CanParse(SubtitleSourceAsset asset)
	{
		return string.Equals(asset.Extension, ".json", StringComparison.OrdinalIgnoreCase);
	}

	public bool TryParse(SubtitleSourceAsset asset, out SubtitleCatalog catalog)
	{
		catalog = new SubtitleCatalog();

		try
		{
			using var document = JsonDocument.Parse(asset.Content);
			var root = document.RootElement;

			if (root.ValueKind == JsonValueKind.Array)
			{
				var track = ParseSingleTrack(root, asset.LanguageHint, asset.Source);
				catalog.AddOrMerge(track);
				return true;
			}

			if (root.ValueKind != JsonValueKind.Object)
			{
				Log.Warn($"Unsupported subtitle JSON root in '{asset.Source}'.");
				return false;
			}

			if (root.TryGetProperty("defaultLanguage", out var defaultLanguageElement)
				&& defaultLanguageElement.ValueKind == JsonValueKind.String)
			{
				catalog.DefaultLanguage = LanguageResolver.Normalize(defaultLanguageElement.GetString() ?? "en");
			}

			if (root.TryGetProperty("tracks", out var tracksElement)
				&& tracksElement.ValueKind == JsonValueKind.Object)
			{
				foreach (var trackProperty in tracksElement.EnumerateObject())
				{
					var parsedTrack = ParseSingleTrack(trackProperty.Value, trackProperty.Name, asset.Source);
					catalog.AddOrMerge(parsedTrack);
				}

				return catalog.Tracks.Count > 0;
			}

			if (root.TryGetProperty("cues", out var cuesElement) && cuesElement.ValueKind == JsonValueKind.Array)
			{
				var language = asset.LanguageHint;
				if (root.TryGetProperty("language", out var languageElement) && languageElement.ValueKind == JsonValueKind.String)
				{
					language = languageElement.GetString() ?? language;
				}

				var singleTrack = ParseSingleTrack(cuesElement, language, asset.Source);
				catalog.AddOrMerge(singleTrack);
				return true;
			}

			Log.Warn($"No supported subtitle shape found in '{asset.Source}'.");
			return false;
		}
		catch (Exception ex)
		{
			Log.Error($"Failed to parse subtitle json from '{asset.Source}': {ex.Message}");
			return false;
		}
	}

	private static SubtitleTrack ParseSingleTrack(JsonElement cuesElement, string language, string source)
	{
		var track = new SubtitleTrack
		{
			Language = string.IsNullOrWhiteSpace(language) ? "en" : LanguageResolver.Normalize(language),
			Source = source
		};

		foreach (var cueElement in cuesElement.EnumerateArray())
		{
			if (cueElement.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var cue = new SubtitleCue
			{
				Text = cueElement.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty
			};

			var hasStartBeat = cueElement.TryGetProperty("startBeat", out var startBeatElement);
			var hasEndBeat = cueElement.TryGetProperty("endBeat", out var endBeatElement);
			var hasStartSeconds = cueElement.TryGetProperty("startSeconds", out var startSecondsElement);
			var hasEndSeconds = cueElement.TryGetProperty("endSeconds", out var endSecondsElement);
			var hasBeatTiming = hasStartBeat && hasEndBeat;
			var hasSecondTiming = hasStartSeconds && hasEndSeconds;

			if (!hasBeatTiming && !hasSecondTiming)
			{
				continue;
			}

			if (hasSecondTiming)
			{
				cue.StartSeconds = ReadDouble(startSecondsElement);
				cue.EndSeconds = ReadDouble(endSecondsElement);
			}
			else
			{
				cue.StartBeat = ReadFloat(startBeatElement);
				cue.EndBeat = ReadFloat(endBeatElement);
			}

			if (cueElement.TryGetProperty("karaoke", out var karaokeElement)
				&& karaokeElement.ValueKind == JsonValueKind.Array)
			{
				foreach (var segmentElement in karaokeElement.EnumerateArray())
				{
					if (segmentElement.ValueKind != JsonValueKind.Object)
					{
						continue;
					}

					var hasBeat = segmentElement.TryGetProperty("beat", out var beatElement);
					var hasSeconds = segmentElement.TryGetProperty("seconds", out var secondsElement);
					if (!hasBeat && !hasSeconds)
					{
						continue;
					}

					cue.KaraokeSegments.Add(new KaraokeSegment
					{
						Beat = hasBeat ? ReadFloat(beatElement) : null,
						Seconds = hasSeconds ? ReadDouble(secondsElement) : null,
						Text = segmentElement.TryGetProperty("text", out var segmentTextElement)
							? segmentTextElement.GetString() ?? string.Empty
							: string.Empty
					});
				}
			}

			if (cue.UsesSecondsTiming && cue.EndSeconds > cue.StartSeconds)
			{
				track.Cues.Add(cue);
			}
			else if (!cue.UsesSecondsTiming && cue.EndBeat > cue.StartBeat)
			{
				track.Cues.Add(cue);
			}
		}

		track.Cues.Sort((a, b) => a.SortKey().CompareTo(b.SortKey()));
		return track;
	}

	private static float ReadFloat(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Number => element.GetSingle(),
			JsonValueKind.String when float.TryParse(element.GetString(), out var parsed) => parsed,
			_ => 0f
		};
	}

	private static double ReadDouble(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Number => element.GetDouble(),
			JsonValueKind.String when double.TryParse(element.GetString(), out var parsed) => parsed,
			_ => 0d
		};
	}
}
