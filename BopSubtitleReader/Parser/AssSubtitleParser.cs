using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BopSubtitleReader.Core;

namespace BopSubtitleReader.Parser;

public sealed class AssSubtitleParser : ISubtitleParser
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<AssSubtitleParser>();
	private const string DefaultSecondaryColorHex = "#FFFF00FF";

	private static readonly string[] LineSeparators = ["\r\n", "\n"];

	public string Name => "ASS";

	public bool CanParse(SubtitleSourceAsset asset)
	{
		return string.Equals(asset.Extension, ".ass", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(asset.Extension, ".ssa", StringComparison.OrdinalIgnoreCase);
	}

	public bool TryParse(SubtitleSourceAsset asset, out SubtitleCatalog catalog)
	{
		catalog = new SubtitleCatalog();
		var track = new SubtitleTrack
		{
			Language = string.IsNullOrWhiteSpace(asset.LanguageHint) ? "en" : LanguageResolver.Normalize(asset.LanguageHint),
			Source = asset.Source
		};

		var styles = new Dictionary<string, SubtitleCueStyle>(StringComparer.OrdinalIgnoreCase);
		var styleFormat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var eventFormat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var currentSection = string.Empty;

		var lines = asset.Content.Split(LineSeparators, StringSplitOptions.None);
		foreach (var rawLine in lines)
		{
			var line = rawLine.Trim();
			if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
			{
				continue;
			}

			if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
			{
				currentSection = line;
				continue;
			}

			if (string.Equals(currentSection, "[V4+ Styles]", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(currentSection, "[V4 Styles]", StringComparison.OrdinalIgnoreCase))
			{
				ParseStyleLine(line, styleFormat, styles);
				continue;
			}

			if (!string.Equals(currentSection, "[Events]", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			ParseEventFormatOrDialogue(line, eventFormat, styles, track.Cues);
		}

		if (track.Cues.Count == 0)
		{
			Log.Warn($"ASS parser found no cues in '{asset.Source}'.");
			return false;
		}

		var karaokeCueCount = 0;
		for (var i = 0; i < track.Cues.Count; i++)
		{
			if (track.Cues[i].KaraokeSegments.Count > 0)
			{
				karaokeCueCount++;
			}
		}
		Log.Info($"ASS parser loaded {track.Cues.Count} cue(s), {karaokeCueCount} karaoke cue(s) from '{asset.Source}'.");

		catalog.AddOrMerge(track);
		return true;
	}

	private static void ParseStyleLine(
		string line,
		Dictionary<string, int> styleFormat,
		Dictionary<string, SubtitleCueStyle> styles)
	{
		if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
		{
			BuildFormatMap(line.Substring("Format:".Length), styleFormat);
			return;
		}

		if (!line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase) || styleFormat.Count == 0)
		{
			return;
		}

		var body = line.Substring("Style:".Length).Trim();
		var values = SplitCsvWithRemainder(body, styleFormat.Count);
		var name = ReadValue(values, styleFormat, "name");
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		var style = new SubtitleCueStyle();
		var fontName = ReadValue(values, styleFormat, "fontname");
		if (!string.IsNullOrWhiteSpace(fontName))
		{
			style.FontName = fontName.Trim();
		}

		if (TryParseFloat(ReadValue(values, styleFormat, "fontsize"), out var fontSize))
		{
			style.FontSize = fontSize;
		}

		var primaryColor = ReadValue(values, styleFormat, "primarycolour");
		if (TryParseAssColor(primaryColor, out var colorHex))
		{
			style.ColorHexRgba = colorHex;
		}

		var secondaryColor = ReadValue(values, styleFormat, "secondarycolour");
		if (TryParseAssColor(secondaryColor, out var secondaryHex))
		{
			style.SecondaryColorHexRgba = secondaryHex;
		}

		var outlineColor = ReadValue(values, styleFormat, "outlinecolour");
		if (TryParseAssColor(outlineColor, out var outlineHex))
		{
			style.OutlineColorHexRgba = outlineHex;
		}

		if (TryParseFloat(ReadValue(values, styleFormat, "outline"), out var outlineWidth))
		{
			style.OutlineWidth = outlineWidth;
		}

		if (TryParseBoolNumeric(ReadValue(values, styleFormat, "bold"), out var bold))
		{
			style.Bold = bold;
		}

		if (TryParseBoolNumeric(ReadValue(values, styleFormat, "italic"), out var italic))
		{
			style.Italic = italic;
		}

		if (TryParseInt(ReadValue(values, styleFormat, "alignment"), out var alignment))
		{
			style.Alignment = alignment;
		}

		styles[name] = style;
	}

	private static void ParseEventFormatOrDialogue(
		string line,
		Dictionary<string, int> eventFormat,
		Dictionary<string, SubtitleCueStyle> styles,
		List<SubtitleCue> cues)
	{
		if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
		{
			BuildFormatMap(line.Substring("Format:".Length), eventFormat);
			return;
		}

		if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase) || eventFormat.Count == 0)
		{
			return;
		}

		var body = line.Substring("Dialogue:".Length).Trim();
		var values = SplitCsvWithRemainder(body, eventFormat.Count);

		if (!TryParseAssTime(ReadValue(values, eventFormat, "start"), out var startSeconds)
			|| !TryParseAssTime(ReadValue(values, eventFormat, "end"), out var endSeconds)
			|| endSeconds <= startSeconds)
		{
			return;
		}

		var styleName = ReadValue(values, eventFormat, "style");
		SubtitleCueStyle? baseStyle = null;
		if (!string.IsNullOrWhiteSpace(styleName) && styles.TryGetValue(styleName, out var style))
		{
			baseStyle = style;
		}

		var textRaw = ReadValue(values, eventFormat, "text");
		if (string.IsNullOrWhiteSpace(textRaw))
		{
			return;
		}

		ParseDialogueText(textRaw, startSeconds, baseStyle, out var visibleText, out var cueStyle, out var karaokeSegments);
		if (string.IsNullOrWhiteSpace(visibleText))
		{
			return;
		}

		var cue = new SubtitleCue
		{
			StartSeconds = startSeconds,
			EndSeconds = endSeconds,
			Text = visibleText,
			Style = cueStyle
		};

		if (karaokeSegments.Count > 0)
		{
			cue.KaraokeSegments.AddRange(karaokeSegments);
		}

		cues.Add(cue);
	}

	private static void ParseDialogueText(
		string textRaw,
		double startSeconds,
		SubtitleCueStyle? baseStyle,
		out string visibleText,
		out SubtitleCueStyle? cueStyle,
		out List<KaraokeSegment> karaokeSegments)
	{
		karaokeSegments = [];
		var workingStyle = CloneStyle(baseStyle);
		var builder = new StringBuilder(textRaw.Length);
		double karaokeOffsetSeconds = 0;
		PendingKaraoke? pendingKaraoke = null;

		for (var index = 0; index < textRaw.Length;)
		{
			if (textRaw[index] == '{')
			{
				var close = textRaw.IndexOf('}', index + 1);
				if (close < 0)
				{
					break;
				}

				ApplyOverrides(
					textRaw.Substring(index + 1, close - index - 1),
					workingStyle,
					ref pendingKaraoke,
					ref karaokeOffsetSeconds);
				index = close + 1;
				continue;
			}

			var nextOverride = textRaw.IndexOf('{', index);
			if (nextOverride < 0)
			{
				nextOverride = textRaw.Length;
			}

			var chunk = NormalizeAssText(textRaw.Substring(index, nextOverride - index));
			var segmentStart = builder.Length;
			builder.Append(chunk);

			if (pendingKaraoke.HasValue)
			{
				var karaoke = pendingKaraoke.Value;
				if (chunk.Length > 0)
				{
					karaokeSegments.Add(new KaraokeSegment
					{
						Text = chunk,
						Seconds = startSeconds + karaokeOffsetSeconds,
						DurationSeconds = karaoke.DurationCentiseconds / 100.0,
						TagType = karaoke.TagType,
						StartCharIndex = segmentStart,
						CharLength = chunk.Length
					});
				}
				karaokeOffsetSeconds += karaoke.DurationCentiseconds / 100.0;
				pendingKaraoke = null;
			}

			index = nextOverride;
		}

		visibleText = builder.ToString();
		cueStyle = HasStyleValues(workingStyle) ? workingStyle : null;
	}

	private static void ApplyOverrides(
		string block,
		SubtitleCueStyle style,
		ref PendingKaraoke? pendingKaraoke,
		ref double karaokeOffsetSeconds)
	{
		var index = 0;
		while (index < block.Length)
		{
			var slash = block.IndexOf('\\', index);
			if (slash < 0 || slash + 1 >= block.Length)
			{
				break;
			}

			index = slash + 1;
			var end = index + 1;
			while (end < block.Length && block[end] != '\\')
			{
				end++;
			}

			var tag = block.Substring(index, end - index);
			ParseOverrideTag(tag, style, ref pendingKaraoke, ref karaokeOffsetSeconds);
			index = end;
		}
	}

	private static void ParseOverrideTag(
		string rawTag,
		SubtitleCueStyle style,
		ref PendingKaraoke? pendingKaraoke,
		ref double karaokeOffsetSeconds)
	{
		var tag = rawTag.Trim();
		if (tag.Length == 0)
		{
			return;
		}

		if (TryParseKaraokeTag(tag, out var karaoke))
		{
			if (pendingKaraoke.HasValue)
			{
				karaokeOffsetSeconds += pendingKaraoke.Value.DurationCentiseconds / 100.0;
			}
			pendingKaraoke = karaoke;
			return;
		}

		if (StartsWithTag(tag, "fs"))
		{
			var value = ExtractNumericSuffix(tag);
			if (TryParseFloat(value, out var fontSize))
			{
				style.FontSize = fontSize;
			}
			return;
		}

		if (tag.StartsWith("fn", StringComparison.OrdinalIgnoreCase))
		{
			var value = tag.Substring(2).Trim();
			if (!string.IsNullOrWhiteSpace(value))
			{
				style.FontName = value;
			}
			return;
		}

		if (StartsWithTag(tag, "b"))
		{
			var value = ExtractNumericSuffix(tag);
			if (TryParseBoolNumeric(value, out var bold))
			{
				style.Bold = bold;
			}
			return;
		}

		if (StartsWithTag(tag, "i"))
		{
			var value = ExtractNumericSuffix(tag);
			if (TryParseBoolNumeric(value, out var italic))
			{
				style.Italic = italic;
			}
			return;
		}

		if (StartsWithTag(tag, "an"))
		{
			var value = ExtractNumericSuffix(tag);
			if (TryParseInt(value, out var alignment) && alignment >= 1 && alignment <= 9)
			{
				style.Alignment = alignment;
			}
			return;
		}

		if (StartsWithTag(tag, "1c") || StartsWithTag(tag, "c"))
		{
			var value = tag.StartsWith("1c", StringComparison.OrdinalIgnoreCase) ? tag.Substring(2) : tag.Substring(1);
			if (TryParseAssColor(value, out var colorHex))
			{
				style.ColorHexRgba = colorHex;
			}
			return;
		}

		if (StartsWithTag(tag, "2c"))
		{
			var value = tag.Substring(2);
			if (TryParseAssColor(value, out var colorHex))
			{
				style.SecondaryColorHexRgba = colorHex;
			}
			return;
		}

		if (StartsWithTag(tag, "3c"))
		{
			var value = tag.Substring(2);
			if (TryParseAssColor(value, out var colorHex))
			{
				style.OutlineColorHexRgba = colorHex;
			}
			return;
		}

		if (StartsWithTag(tag, "1a"))
		{
			if (TryParseAssAlpha(tag.Substring(2), out var alpha))
			{
				style.ColorHexRgba = ApplyAssAlphaToRgba(style.ColorHexRgba, alpha, "#FFFFFFFF");
			}
			return;
		}

		if (StartsWithTag(tag, "2a"))
		{
			if (TryParseAssAlpha(tag.Substring(2), out var alpha))
			{
				style.SecondaryColorHexRgba = ApplyAssAlphaToRgba(style.SecondaryColorHexRgba, alpha, DefaultSecondaryColorHex);
			}
			return;
		}

		if (StartsWithTag(tag, "3a"))
		{
			if (TryParseAssAlpha(tag.Substring(2), out var alpha))
			{
				style.OutlineColorHexRgba = ApplyAssAlphaToRgba(style.OutlineColorHexRgba, alpha, "#000000FF");
			}
			return;
		}

		if (StartsWithTag(tag, "alpha"))
		{
			if (TryParseAssAlpha(tag.Substring(5), out var alpha))
			{
				style.ColorHexRgba = ApplyAssAlphaToRgba(style.ColorHexRgba, alpha, "#FFFFFFFF");
				style.SecondaryColorHexRgba = ApplyAssAlphaToRgba(style.SecondaryColorHexRgba, alpha, DefaultSecondaryColorHex);
				style.OutlineColorHexRgba = ApplyAssAlphaToRgba(style.OutlineColorHexRgba, alpha, "#000000FF");
			}
			return;
		}

		if (StartsWithTag(tag, "bord"))
		{
			var value = ExtractNumericSuffix(tag);
			if (TryParseFloat(value, out var width))
			{
				style.OutlineWidth = width;
			}
		}
	}

	private static bool TryParseKaraokeTag(string tag, out PendingKaraoke karaoke)
	{
		karaoke = default;
		KaraokeTagType tagType;
		string value;

		if (StartsWithTag(tag, "kf"))
		{
			tagType = KaraokeTagType.Kf;
			value = tag.Substring(2);
		}
		else if (StartsWithTag(tag, "ko"))
		{
			tagType = KaraokeTagType.Ko;
			value = tag.Substring(2);
		}
		else if (tag.StartsWith("K", StringComparison.Ordinal) && StartsWithTag(tag, "K"))
		{
			tagType = KaraokeTagType.Kf;
			value = tag.Substring(1);
		}
		else if (StartsWithTag(tag, "k"))
		{
			tagType = KaraokeTagType.K;
			value = tag.Substring(1);
		}
		else
		{
			return false;
		}

		if (!TryParseInt(ExtractNumericSuffix(value), out var centiseconds) || centiseconds <= 0)
		{
			return false;
		}

		karaoke = new PendingKaraoke(centiseconds, tagType);
		return true;
	}

	private static bool StartsWithTag(string tag, string key)
	{
		if (!tag.StartsWith(key, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (tag.Length == key.Length)
		{
			return true;
		}

		var next = tag[key.Length];
		return char.IsDigit(next) || next == '-' || next == '&';
	}

	private static string ExtractNumericSuffix(string value)
	{
		var idx = 0;
		while (idx < value.Length && !char.IsDigit(value[idx]) && value[idx] != '-')
		{
			idx++;
		}

		return idx >= value.Length ? string.Empty : value.Substring(idx);
	}

	private static string NormalizeAssText(string value)
	{
		return value
			.Replace("\\N", "\n")
			.Replace("\\n", "\n")
			.Replace("\\h", " ");
	}

	private static SubtitleCueStyle CloneStyle(SubtitleCueStyle? input)
	{
		if (input is null)
		{
			return new SubtitleCueStyle();
		}

		return new SubtitleCueStyle
		{
			FontName = input.FontName,
			FontSize = input.FontSize,
			ColorHexRgba = input.ColorHexRgba,
			SecondaryColorHexRgba = input.SecondaryColorHexRgba,
			OutlineColorHexRgba = input.OutlineColorHexRgba,
			OutlineWidth = input.OutlineWidth,
			Bold = input.Bold,
			Italic = input.Italic,
			Alignment = input.Alignment
		};
	}

	private static bool HasStyleValues(SubtitleCueStyle style)
	{
		return style.FontSize.HasValue
			|| !string.IsNullOrWhiteSpace(style.FontName)
			|| !string.IsNullOrWhiteSpace(style.ColorHexRgba)
			|| !string.IsNullOrWhiteSpace(style.SecondaryColorHexRgba)
			|| !string.IsNullOrWhiteSpace(style.OutlineColorHexRgba)
			|| style.OutlineWidth.HasValue
			|| style.Bold.HasValue
			|| style.Italic.HasValue
			|| style.Alignment.HasValue;
	}

	private static void BuildFormatMap(string formatBody, Dictionary<string, int> target)
	{
		target.Clear();
		var columns = formatBody.Split(',');
		for (var i = 0; i < columns.Length; i++)
		{
			target[NormalizeField(columns[i])] = i;
		}
	}

	private static string[] SplitCsvWithRemainder(string input, int fieldCount)
	{
		if (fieldCount <= 1)
		{
			return [input.Trim()];
		}

		var values = new string[fieldCount];
		var start = 0;
		for (var i = 0; i < fieldCount - 1; i++)
		{
			var comma = input.IndexOf(',', start);
			if (comma < 0)
			{
				values[i] = input.Substring(start).Trim();
				for (var j = i + 1; j < fieldCount; j++)
				{
					values[j] = string.Empty;
				}
				return values;
			}

			values[i] = input.Substring(start, comma - start).Trim();
			start = comma + 1;
		}

		values[fieldCount - 1] = input.Substring(start).Trim();
		return values;
	}

	private static string ReadValue(string[] values, Dictionary<string, int> format, string fieldName)
	{
		if (!format.TryGetValue(fieldName, out var index) || index < 0 || index >= values.Length)
		{
			return string.Empty;
		}

		return values[index].Trim();
	}

	private static string NormalizeField(string input)
	{
		return input.Trim().Replace(" ", string.Empty).ToLowerInvariant();
	}

	private static bool TryParseAssTime(string value, out double seconds)
	{
		seconds = 0;
		if (!TimeSpan.TryParseExact(value.Trim(), @"h\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var ts))
		{
			return false;
		}

		seconds = ts.TotalSeconds;
		return true;
	}

	private static bool TryParseFloat(string value, out float number)
	{
		return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
	}

	private static bool TryParseInt(string value, out int number)
	{
		return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
	}

	private static bool TryParseBoolNumeric(string value, out bool result)
	{
		result = false;
		if (!TryParseInt(value, out var number))
		{
			return false;
		}

		result = number != 0;
		return true;
	}

	private static bool TryParseAssColor(string value, out string rgbaHex)
	{
		rgbaHex = string.Empty;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var normalized = value.Trim();
		normalized = normalized.Trim('&');
		if (normalized.StartsWith("H", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(1);
		}

		if (normalized.Length == 0)
		{
			return false;
		}

		if (!uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
		{
			return false;
		}

		byte rr;
		byte gg;
		byte bb;
		byte aa;

		if (normalized.Length > 6)
		{
			var assAlpha = (byte)((packed >> 24) & 0xFF);
			bb = (byte)((packed >> 16) & 0xFF);
			gg = (byte)((packed >> 8) & 0xFF);
			rr = (byte)(packed & 0xFF);
			aa = (byte)(255 - assAlpha);
		}
		else
		{
			bb = (byte)((packed >> 16) & 0xFF);
			gg = (byte)((packed >> 8) & 0xFF);
			rr = (byte)(packed & 0xFF);
			aa = 255;
		}

		rgbaHex = $"#{rr:X2}{gg:X2}{bb:X2}{aa:X2}";
		return true;
	}

	private static bool TryParseAssAlpha(string value, out byte alpha)
	{
		alpha = 255;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var normalized = value.Trim().Trim('&');
		if (normalized.StartsWith("H", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(1);
		}

		if (!byte.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var assAlpha))
		{
			return false;
		}

		alpha = (byte)(255 - assAlpha);
		return true;
	}

	private static string ApplyAssAlphaToRgba(string? rgba, byte alpha, string fallbackRgba)
	{
		var baseColor = string.IsNullOrWhiteSpace(rgba) ? fallbackRgba : rgba!.Trim();
		if (!baseColor.StartsWith("#", StringComparison.Ordinal) || (baseColor.Length != 7 && baseColor.Length != 9))
		{
			baseColor = fallbackRgba;
		}

		var rgb = baseColor.Length >= 7 ? baseColor.Substring(1, 6) : fallbackRgba.Substring(1, 6);
		return $"#{rgb}{alpha:X2}";
	}

	private readonly struct PendingKaraoke
	{
		public int DurationCentiseconds { get; }
		public KaraokeTagType TagType { get; }

		public PendingKaraoke(int durationCentiseconds, KaraokeTagType tagType)
		{
			DurationCentiseconds = durationCentiseconds;
			TagType = tagType;
		}
	}
}
