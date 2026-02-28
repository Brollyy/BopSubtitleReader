using System;
using System.Collections.Generic;

namespace BopSubtitleReader.Core;

public sealed class SubtitleCue
{
	public float StartBeat { get; set; }
	public float EndBeat { get; set; }
	public double? StartSeconds { get; set; }
	public double? EndSeconds { get; set; }
	public string Text { get; set; } = string.Empty;
	public SubtitleCueStyle? Style { get; set; }
	public List<KaraokeSegment> KaraokeSegments { get; } = [];

	public bool UsesSecondsTiming => StartSeconds.HasValue && EndSeconds.HasValue;

	public bool IsActive(float beat)
	{
		return beat >= StartBeat && beat < EndBeat;
	}

	public float SortKey()
	{
		if (StartSeconds.HasValue)
		{
			return (float)StartSeconds.Value;
		}

		return StartBeat;
	}
}

public sealed class SubtitleCueStyle
{
	public string? FontName { get; set; }
	public float? FontSize { get; set; }
	public string? ColorHexRgba { get; set; }
	public string? SecondaryColorHexRgba { get; set; }
	public string? OutlineColorHexRgba { get; set; }
	public float? OutlineWidth { get; set; }
	public bool? Bold { get; set; }
	public bool? Italic { get; set; }
	public int? Alignment { get; set; }
}

public enum KaraokeTagType
{
	None = 0,
	K = 1,
	Kf = 2,
	Ko = 3
}

public sealed class KaraokeSegment
{
	public string Text { get; set; } = string.Empty;
	public float? Beat { get; set; }
	public double? Seconds { get; set; }
	public double? DurationSeconds { get; set; }
	public KaraokeTagType TagType { get; set; } = KaraokeTagType.K;
	public int? StartCharIndex { get; set; }
	public int? CharLength { get; set; }
}

public sealed class KaraokeRenderSegment
{
	public int StartCharIndex { get; set; }
	public int CharLength { get; set; }
	public string Text { get; set; } = string.Empty;
	public string BaseFaceColorHexRgba { get; set; } = "#FFFFFFFF";
	public string FillFaceColorHexRgba { get; set; } = "#FFFFFFFF";
	public string OutlineColorHexRgba { get; set; } = "#000000FF";
	public float FillProgress { get; set; } = 1f;
}

public sealed class SubtitleTrack
{
	public string Language { get; set; } = string.Empty;
	public List<SubtitleCue> Cues { get; } = [];
	public string Source { get; set; } = string.Empty;
}

public sealed class SubtitleCatalog
{
	public string DefaultLanguage { get; set; } = "en";
	public Dictionary<string, SubtitleTrack> Tracks { get; } = new(StringComparer.OrdinalIgnoreCase);

	public void AddOrMerge(SubtitleTrack track)
	{
		if (string.IsNullOrWhiteSpace(track.Language))
		{
			track.Language = DefaultLanguage;
		}

		if (!Tracks.TryGetValue(track.Language, out var existing))
		{
			Tracks[track.Language] = track;
			Sort(track);
			return;
		}

		existing.Cues.AddRange(track.Cues);
		Sort(existing);
	}

	private static void Sort(SubtitleTrack track)
	{
		track.Cues.Sort((a, b) => a.SortKey().CompareTo(b.SortKey()));
	}
}
