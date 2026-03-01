using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
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
			var root = JToken.Parse(asset.Content);

			if (root is JArray rootArray)
			{
				var track = ParseSingleTrack(rootArray, asset.LanguageHint, asset.Source);
				catalog.AddOrMerge(track);
				return true;
			}

			if (root is not JObject rootObject)
			{
				Log.Warn($"Unsupported subtitle JSON root in '{asset.Source}'.");
				return false;
			}

			if (rootObject.TryGetValue("defaultLanguage", out var defaultLanguageToken)
				&& defaultLanguageToken.Type == JTokenType.String)
			{
				catalog.DefaultLanguage = LanguageResolver.Normalize(defaultLanguageToken.Value<string>() ?? "en");
			}

			if (rootObject.TryGetValue("tracks", out var tracksToken)
				&& tracksToken is JObject tracksObject)
			{
				foreach (var trackProperty in tracksObject.Properties())
				{
					if (trackProperty.Value is not JArray trackArray)
					{
						Log.Warn($"Track '{trackProperty.Name}' in '{asset.Source}' is not an array and will be skipped.");
						continue;
					}

					var parsedTrack = ParseSingleTrack(trackArray, trackProperty.Name, asset.Source);
					catalog.AddOrMerge(parsedTrack);
				}

				return catalog.Tracks.Count > 0;
			}

			if (rootObject.TryGetValue("cues", out var cuesToken) && cuesToken is JArray cuesArray)
			{
				var language = asset.LanguageHint;
				if (rootObject.TryGetValue("language", out var languageToken) && languageToken.Type == JTokenType.String)
				{
					language = languageToken.Value<string>() ?? language;
				}

				var singleTrack = ParseSingleTrack(cuesArray, language, asset.Source);
				catalog.AddOrMerge(singleTrack);
				return true;
			}

			Log.Warn($"No supported subtitle shape found in '{asset.Source}'.");
			return false;
		}
		catch (Exception ex)
		{
			Log.Error($"Failed to parse subtitle json from '{asset.Source}': [{ex.GetType().Name}] {ex.Message}{Environment.NewLine}{ex}");
			return false;
		}
	}

	private static SubtitleTrack ParseSingleTrack(JArray cuesArray, string language, string source)
	{
		var track = new SubtitleTrack
		{
			Language = string.IsNullOrWhiteSpace(language) ? "en" : LanguageResolver.Normalize(language),
			Source = source
		};

		foreach (var cueToken in cuesArray)
		{
			if (cueToken is not JObject cueObject)
			{
				continue;
			}

			var cue = new SubtitleCue
			{
				Text = cueObject.TryGetValue("text", out var textToken) ? textToken.Value<string>() ?? string.Empty : string.Empty
			};

			var hasStartBeat = cueObject.TryGetValue("startBeat", out var startBeatToken);
			var hasEndBeat = cueObject.TryGetValue("endBeat", out var endBeatToken);
			var hasStartSeconds = cueObject.TryGetValue("startSeconds", out var startSecondsToken);
			var hasEndSeconds = cueObject.TryGetValue("endSeconds", out var endSecondsToken);
			var hasBeatTiming = hasStartBeat && hasEndBeat;
			var hasSecondTiming = hasStartSeconds && hasEndSeconds;

			if (!hasBeatTiming && !hasSecondTiming)
			{
				continue;
			}

			if (hasSecondTiming)
			{
				cue.StartSeconds = ReadDouble(startSecondsToken);
				cue.EndSeconds = ReadDouble(endSecondsToken);
			}
			else
			{
				cue.StartBeat = ReadFloat(startBeatToken);
				cue.EndBeat = ReadFloat(endBeatToken);
			}

			if (cueObject.TryGetValue("karaoke", out var karaokeToken)
				&& karaokeToken is JArray karaokeArray)
			{
				foreach (var segmentToken in karaokeArray)
				{
					if (segmentToken is not JObject segmentObject)
					{
						continue;
					}

					var hasBeat = segmentObject.TryGetValue("beat", out var beatToken);
					var hasSeconds = segmentObject.TryGetValue("seconds", out var secondsToken);
					if (!hasBeat && !hasSeconds)
					{
						continue;
					}

					cue.KaraokeSegments.Add(new KaraokeSegment
					{
						Beat = hasBeat ? ReadFloat(beatToken) : null,
						Seconds = hasSeconds ? ReadDouble(secondsToken) : null,
						Text = segmentObject.TryGetValue("text", out var segmentTextToken)
							? segmentTextToken.Value<string>() ?? string.Empty
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

	private static float ReadFloat(JToken? token)
	{
		if (token is null)
		{
			return 0f;
		}

		return token.Type switch
		{
			JTokenType.Float or JTokenType.Integer => token.Value<float>(),
			JTokenType.String when float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => 0f
		};
	}

	private static double ReadDouble(JToken? token)
	{
		if (token is null)
		{
			return 0d;
		}

		return token.Type switch
		{
			JTokenType.Float or JTokenType.Integer => token.Value<double>(),
			JTokenType.String when double.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => 0d
		};
	}
}
