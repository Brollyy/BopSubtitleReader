using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BopSubtitleReader.Core;

public sealed class SubtitleRuntimeController : MonoBehaviour
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<SubtitleRuntimeController>();
	private static SubtitleRuntimeController? _instance;
	private static readonly AccessTools.FieldRef<MixtapeLoaderCustom, JukeboxScript?> JukeboxRef =
		AccessTools.FieldRefAccess<MixtapeLoaderCustom, JukeboxScript?>("jukebox");
	private static readonly AccessTools.FieldRef<MixtapeLoaderCustom, SceneKey[]> SceneKeysRef =
		AccessTools.FieldRefAccess<MixtapeLoaderCustom, SceneKey[]>("sceneKeys");
	private static readonly AccessTools.FieldRef<MixtapeLoaderCustom, Dictionary<SceneKey, GameplayScript>> ScriptsRef =
		AccessTools.FieldRefAccess<MixtapeLoaderCustom, Dictionary<SceneKey, GameplayScript>>("scripts");

	// Sentinel stored in _displayText while the karaoke overlay is actively rendering.
	private const string KaraokeActiveSentinel = "__KARAOKE_ACTIVE__";

	private readonly TmpSubtitleOverlay _overlay = new();

	private SubtitleTrack? _track;
	private SubtitleCue? _activeCue;
	private MixtapeLoaderCustom? _loader;
	private SubtitleConfig? _config;
	private bool _timingResolved;
	private string _displayText = string.Empty;
	private string _displayStyleKey = string.Empty;

	// Karaoke display cache: only rebuild the rendered segment list when the active
	// segment changes or a progressive-fill (Kf) segment is in progress.
	private SubtitleCue? _cachedKaraokeCue;
	private int _cachedActiveSegmentIndex = -1;
	public bool HasActiveSession => _track is not null && _loader is not null && _config is not null;

	public static SubtitleRuntimeController Instance
	{
		get
		{
			if (_instance is null || !_instance)
			{
				var host = new GameObject("BOP_SubtitleRuntimeController");
				DontDestroyOnLoad(host);
				_instance = host.AddComponent<SubtitleRuntimeController>();
			}

			return _instance;
		}
	}

	public void StartSession(MixtapeLoaderCustom loader, SubtitleTrack track, SubtitleConfig config)
	{
		_loader = loader;
		_track = track;
		_config = config;
		_activeCue = null;
		_displayText = string.Empty;
		_displayStyleKey = string.Empty;
		_timingResolved = false;
		_cachedKaraokeCue = null;
		_cachedActiveSegmentIndex = -1;
		_overlay.SetCamera(ResolveSubtitleCamera(loader));
		_overlay.Hide();
		var karaokeCueCount = track.Cues.Count(c => c.KaraokeSegments.Count > 0);
		Log.Info($"Subtitle session started with {track.Cues.Count} cue(s), {karaokeCueCount} karaoke cue(s).");
	}

	public void StopSession()
	{
		_track = null;
		_loader = null;
		_activeCue = null;
		_displayText = string.Empty;
		_displayStyleKey = string.Empty;
		_timingResolved = false;
		_cachedKaraokeCue = null;
		_cachedActiveSegmentIndex = -1;
		_overlay.Hide();
	}

	private void Update()
	{
		if (_track is null || _config is null || !_config.Enabled.Value)
		{
			SetDisplayText(string.Empty, null);
			return;
		}

		if (_loader is null || !_loader)
		{
			Log.Trace("Active loader was destroyed; clearing subtitle session.");
			StopSession();
			return;
		}

		var jukebox = JukeboxRef(_loader);
		if (jukebox is null || !jukebox)
		{
			SetDisplayText(string.Empty, null);
			return;
		}

		_overlay.SetCamera(ResolveSubtitleCamera(_loader));

		if (!_timingResolved)
		{
			ResolveCueTiming(_track, jukebox);
			_timingResolved = true;
		}

		var beat = jukebox.CurrentBeat;
		var cue = _track.Cues.FirstOrDefault(cue => cue.IsActive(beat));
		if (cue == _activeCue)
		{
			if (cue is not null)
			{
				UpdateDisplay(cue, beat);
			}
			return;
		}

		_activeCue = cue;
		if (cue is null)
		{
			SetDisplayText(string.Empty, null);
			return;
		}

		Log.Trace(
			$"Activated cue beat={cue.StartBeat:F2}-{cue.EndBeat:F2}, karaokeSegments={cue.KaraokeSegments.Count}, text='{Truncate(cue.Text, 80)}'.");
		UpdateDisplay(cue, beat);
	}

	private void UpdateDisplay(SubtitleCue cue, float beat)
	{
		if (_config is null)
		{
			return;
		}

		var hasKaraoke = cue.KaraokeSegments.Count > 0;
		if (!hasKaraoke || !_config.KaraokeEnabled.Value)
		{
			SetDisplayText(cue.Text, cue.Style);
			return;
		}

		SetKaraokeDisplay(cue, beat);
	}

	private void SetDisplayText(string value, SubtitleCueStyle? style)
	{
		var styleKey = BuildStyleKey(style);
		if (string.Equals(_displayText, value, StringComparison.Ordinal)
			&& string.Equals(_displayStyleKey, styleKey, StringComparison.Ordinal))
		{
			return;
		}

		_displayText = value;
		_displayStyleKey = styleKey;
		if (string.IsNullOrWhiteSpace(value))
		{
			_overlay.Hide();
			return;
		}

		_overlay.SetText(value, style);
	}

	private static string BuildStyleKey(SubtitleCueStyle? style)
	{
		if (style is null)
		{
			return string.Empty;
		}

		var fontSize = style.FontSize.HasValue
			? style.FontSize.Value.ToString(CultureInfo.InvariantCulture)
			: string.Empty;
		return
			$"{style.FontName}|{fontSize}|{style.ColorHexRgba}|{style.SecondaryColorHexRgba}|{style.OutlineColorHexRgba}|{style.OutlineWidth}|{style.Bold}|{style.Italic}|{style.Alignment}";
	}

	private static void ResolveCueTiming(SubtitleTrack track, JukeboxScript jukebox)
	{
		foreach (var cue in track.Cues)
		{
			if (cue.UsesSecondsTiming && cue is { StartSeconds: not null, EndSeconds: not null })
			{
				cue.StartBeat = jukebox.SecondsToBeats(cue.StartSeconds.Value);
				cue.EndBeat = jukebox.SecondsToBeats(cue.EndSeconds.Value);
			}

			foreach (var segment in cue.KaraokeSegments)
			{
				if (!segment.Beat.HasValue && segment.Seconds.HasValue)
				{
					segment.Beat = jukebox.SecondsToBeats(segment.Seconds.Value);
				}
			}
		}

		track.Cues.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
	}

	public static Camera? ResolveSubtitleCamera(MixtapeLoaderCustom? loader = null)
	{
		if (loader is not null)
		{
			SceneKey[] sceneKeys = SceneKeysRef(loader);
			Dictionary<SceneKey, GameplayScript> scripts = ScriptsRef(loader);
			foreach (SceneKey sceneKey in sceneKeys)
			{
				if (!scripts.TryGetValue(sceneKey, out GameplayScript gameplayScript) || gameplayScript is null)
				{
					continue;
				}

				if (gameplayScript.cameraScript == null)
				{
					continue;
				}

				Camera? camera = gameplayScript.cameraScript.GetComponent<Camera>();
				if (camera is not null && camera.enabled)
				{
					return camera;
				}
			}
		}

		return Camera.main;
	}

	private void SetKaraokeDisplay(SubtitleCue cue, float beat)
	{
		var activeIndex = FindActiveKaraokeSegmentIndex(cue, beat);
		var cueChanged = !ReferenceEquals(_cachedKaraokeCue, cue);
		var segmentChanged = activeIndex != _cachedActiveSegmentIndex;

		// The active segment's fill progress changes every frame only for Kf (progressive-fill) tags.
		var isActiveFill = activeIndex >= 0
			&& activeIndex < cue.KaraokeSegments.Count
			&& cue.KaraokeSegments[activeIndex].TagType == KaraokeTagType.Kf
			&& (cue.KaraokeSegments[activeIndex].Beat ?? cue.StartBeat) <= beat;

		// Skip the (expensive) rebuild when nothing has changed.
		// Also rebuild when karaoke display wasn't already active (e.g. KaraokeEnabled toggled on).
		if (!cueChanged && !segmentChanged && !isActiveFill
			&& string.Equals(_displayText, KaraokeActiveSentinel, StringComparison.Ordinal))
		{
			return;
		}

		_cachedKaraokeCue = cue;
		_cachedActiveSegmentIndex = activeIndex;

		var segments = BuildKaraokeRenderSegments(cue, beat);
		_overlay.SetKaraokeText(cue.Text, cue.Style, segments);
		_displayText = KaraokeActiveSentinel;
		_displayStyleKey = $"{BuildStyleKey(cue.Style)}|karaoke";
	}

	private static int FindActiveKaraokeSegmentIndex(SubtitleCue cue, float beat)
	{
		var activeIndex = -1;
		for (var i = 0; i < cue.KaraokeSegments.Count; i++)
		{
			if ((cue.KaraokeSegments[i].Beat ?? cue.StartBeat) <= beat)
			{
				activeIndex = i;
			}
		}

		return activeIndex;
	}

	private static List<KaraokeRenderSegment> BuildKaraokeRenderSegments(SubtitleCue cue, float beat)
	{
		var result = new List<KaraokeRenderSegment>(cue.KaraokeSegments.Count + 2);
		if (cue.KaraokeSegments.Count == 0)
		{
			result.Add(new KaraokeRenderSegment
			{
				StartCharIndex = 0,
				CharLength = cue.Text.Length,
				Text = cue.Text
			});
			return result;
		}

		var primaryColor = EnsureRgba(cue.Style?.ColorHexRgba, "#FFFFFFFF");
		var secondaryColor = EnsureRgba(cue.Style?.SecondaryColorHexRgba, "#FFFF00FF");
		var outlineColor = EnsureRgba(cue.Style?.OutlineColorHexRgba, "#000000FF");
		var hiddenOutlineColor = WithAlpha(outlineColor, 0f);
		var textCursor = 0;
		for (var i = 0; i < cue.KaraokeSegments.Count; i++)
		{
			var segment = cue.KaraokeSegments[i];
			if (string.IsNullOrEmpty(segment.Text))
			{
				continue;
			}

			if (!TryResolveSegmentRange(cue, segment, textCursor, out var startIndex, out var length))
			{
				continue;
			}

			if (startIndex > textCursor)
			{
				result.Add(new KaraokeRenderSegment
				{
					StartCharIndex = textCursor,
					CharLength = startIndex - textCursor,
					Text = cue.Text.Substring(textCursor, startIndex - textCursor),
					BaseFaceColorHexRgba = primaryColor,
					FillFaceColorHexRgba = primaryColor,
					OutlineColorHexRgba = outlineColor,
					FillProgress = 1f
				});
			}

			var startBeat = segment.Beat ?? cue.StartBeat;
			var endBeat = i + 1 < cue.KaraokeSegments.Count
				? cue.KaraokeSegments[i + 1].Beat ?? cue.EndBeat
				: cue.EndBeat;
			var duration = Mathf.Max(endBeat - startBeat, 0.001f);
			var phase = Mathf.Clamp01((beat - startBeat) / duration);
			var segmentText = cue.Text.Substring(startIndex, length);
			AddRenderedSegment(
				result,
				startIndex,
				segmentText,
				segment.TagType,
				beat,
				startBeat,
				endBeat,
				phase,
				primaryColor,
				secondaryColor,
				outlineColor,
				hiddenOutlineColor);
			textCursor = startIndex + length;
		}

		if (textCursor < cue.Text.Length)
		{
			result.Add(new KaraokeRenderSegment
			{
				StartCharIndex = textCursor,
				CharLength = cue.Text.Length - textCursor,
				Text = cue.Text.Substring(textCursor),
				BaseFaceColorHexRgba = primaryColor,
				FillFaceColorHexRgba = primaryColor,
				OutlineColorHexRgba = outlineColor,
				FillProgress = 1f
			});
		}

		return result;
	}

	private static void AddRenderedSegment(
		List<KaraokeRenderSegment> target,
		int startCharIndex,
		string text,
		KaraokeTagType tagType,
		float beat,
		float startBeat,
		float endBeat,
		float phase,
		string primaryColor,
		string secondaryColor,
		string outlineColor,
		string hiddenOutlineColor)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		if (beat < startBeat)
		{
			var preOutline = tagType == KaraokeTagType.Ko ? hiddenOutlineColor : outlineColor;
			target.Add(new KaraokeRenderSegment
			{
				StartCharIndex = startCharIndex,
				CharLength = text.Length,
				Text = text,
				BaseFaceColorHexRgba = secondaryColor,
				FillFaceColorHexRgba = secondaryColor,
				OutlineColorHexRgba = preOutline,
				FillProgress = 1f
			});
			return;
		}

		if (beat >= endBeat)
		{
			target.Add(new KaraokeRenderSegment
			{
				StartCharIndex = startCharIndex,
				CharLength = text.Length,
				Text = text,
				BaseFaceColorHexRgba = primaryColor,
				FillFaceColorHexRgba = primaryColor,
				OutlineColorHexRgba = outlineColor,
				FillProgress = 1f
			});
			return;
		}

		if (tagType == KaraokeTagType.Kf)
		{
			target.Add(new KaraokeRenderSegment
			{
				StartCharIndex = startCharIndex,
				CharLength = text.Length,
				Text = text,
				BaseFaceColorHexRgba = secondaryColor,
				FillFaceColorHexRgba = primaryColor,
				OutlineColorHexRgba = outlineColor,
				FillProgress = Mathf.Clamp01(phase)
			});
			return;
		}

		target.Add(new KaraokeRenderSegment
		{
			StartCharIndex = startCharIndex,
			CharLength = text.Length,
			Text = text,
			BaseFaceColorHexRgba = primaryColor,
			FillFaceColorHexRgba = primaryColor,
			OutlineColorHexRgba = outlineColor,
			FillProgress = 1f
		});
	}

	private static bool TryResolveSegmentRange(SubtitleCue cue, KaraokeSegment segment, int searchFrom, out int startIndex, out int length)
	{
		startIndex = -1;
		length = 0;
		if (segment.StartCharIndex.HasValue && segment.CharLength.HasValue)
		{
			startIndex = segment.StartCharIndex.Value;
			length = Math.Min(segment.CharLength.Value, Math.Max(0, cue.Text.Length - startIndex));
			return startIndex >= 0 && length > 0 && startIndex + length <= cue.Text.Length;
		}

		// Assumes segments appear left-to-right in cue.Text (same order as the parser produces them).
		// searchFrom ensures that repeated segment text (e.g. three "la" segments in "la la la")
		// is matched at the correct occurrence. If the text is not found from searchFrom onward,
		// the segment is skipped and the indicator falls back to center.
		startIndex = cue.Text.IndexOf(segment.Text, searchFrom, StringComparison.Ordinal);
		if (startIndex < 0)
		{
			return false;
		}

		length = segment.Text.Length;
		return length > 0;
	}

	private static string EnsureRgba(string? rgba, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(rgba) && rgba!.StartsWith("#", StringComparison.Ordinal))
		{
			if (rgba.Length == 7)
			{
				return $"{rgba}FF";
			}

			if (rgba.Length == 9)
			{
				return rgba;
			}
		}

		return fallback;
	}

	private static string WithAlpha(string rgba, float alpha)
	{
		var value = EnsureRgba(rgba, "#FFFFFFFF");
		var rgb = value.Substring(0, 7);
		var a = Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
		return $"{rgb}{a:X2}";
	}

	private static string Truncate(string value, int max)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= max)
		{
			return value;
		}

		return value.Substring(0, max) + "...";
	}

}
