using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BopSubtitleReader.Core;

namespace BopSubtitleReader.Parser;

public sealed class SrtSubtitleParser : ISubtitleParser
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<SrtSubtitleParser>();

	private static readonly string[] LineSeparators = ["\r\n", "\n"];
	private static readonly Regex TimeRegex = new(
		@"^(?<start>\d{2}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[,.]\d{3})",
		RegexOptions.Compiled);

	public string Name => "SRT";

	public bool CanParse(SubtitleSourceAsset asset)
	{
		return string.Equals(asset.Extension, ".srt", StringComparison.OrdinalIgnoreCase);
	}

	public bool TryParse(SubtitleSourceAsset asset, out SubtitleCatalog catalog)
	{
		catalog = new SubtitleCatalog();
		var track = new SubtitleTrack
		{
			Language = string.IsNullOrWhiteSpace(asset.LanguageHint) ? "en" : LanguageResolver.Normalize(asset.LanguageHint),
			Source = asset.Source
		};

		var blocks = Regex.Split(asset.Content.Trim(), @"\r?\n\r?\n");
		foreach (var block in blocks)
		{
			var lines = block.Split(LineSeparators, StringSplitOptions.None);
			if (lines.Length < 2)
			{
				continue;
			}

			var timeLineIndex = 0;
			if (int.TryParse(lines[0], out _))
			{
				timeLineIndex = 1;
			}
			if (timeLineIndex >= lines.Length)
			{
				continue;
			}

			var match = TimeRegex.Match(lines[timeLineIndex]);
			if (!match.Success)
			{
				continue;
			}

			if (!TryParseSrtTime(match.Groups["start"].Value, out var startSeconds)
				|| !TryParseSrtTime(match.Groups["end"].Value, out var endSeconds))
			{
				continue;
			}

			var textLines = new List<string>();
			for (var i = timeLineIndex + 1; i < lines.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(lines[i]))
				{
					textLines.Add(lines[i].Trim());
				}
			}

			if (textLines.Count == 0 || endSeconds <= startSeconds)
			{
				continue;
			}

			track.Cues.Add(new SubtitleCue
			{
				StartSeconds = startSeconds,
				EndSeconds = endSeconds,
				Text = string.Join("\n", textLines)
			});
		}

		if (track.Cues.Count == 0)
		{
			Log.Warn($"SRT parser found no cues in '{asset.Source}'.");
			return false;
		}

		catalog.AddOrMerge(track);
		return true;
	}

	private static bool TryParseSrtTime(string value, out double seconds)
	{
		seconds = 0;
		var normalized = value.Replace(',', '.');
		if (!TimeSpan.TryParseExact(normalized, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var ts))
		{
			return false;
		}

		seconds = ts.TotalSeconds;
		return true;
	}
}
