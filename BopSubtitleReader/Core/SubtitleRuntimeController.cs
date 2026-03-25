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
	private static readonly AccessTools.FieldRef<MixtapeLoaderCustom, RiqLoader?> RiqLoaderRef =
		AccessTools.FieldRefAccess<MixtapeLoaderCustom, RiqLoader?>("riqLoader");
	private static readonly AccessTools.FieldRef<RiqLoader, Mixtape?> RiqLoaderMixtapeRef =
		AccessTools.FieldRefAccess<RiqLoader, Mixtape?>("mixtape");
	private static readonly AccessTools.FieldRef<MixtapeEditorScript, MixtapeEventScript?> MixtapePropertiesEventRef =
		AccessTools.FieldRefAccess<MixtapeEditorScript, MixtapeEventScript?>("mixtapePropertiesEvent");

	// Sentinel stored in _displayText while the karaoke overlay is actively rendering.
	private const string KaraokeActiveSentinel = "__KARAOKE_ACTIVE__";

	private readonly TmpSubtitleOverlay _overlay = new();
	private readonly List<KaraokeRenderSegment> _karaokeSegmentsBuffer = [];
	private readonly Stack<KaraokeRenderSegment> _karaokeSegmentPool = [];

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
	private float _cachedKaraokeBeat = float.NaN;
	private Camera? _cachedSubtitleCamera;
	private CameraViewportState? _cachedSubtitleViewportState;
	private float _nextCameraResolveTime;
	private const float CameraResolveIntervalSeconds = 0.5f;
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
		_cachedKaraokeBeat = float.NaN;
		_cachedSubtitleCamera = null;
		_cachedSubtitleViewportState = null;
		_nextCameraResolveTime = 0f;
		RefreshSubtitleCamera(force: true);
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
		_cachedKaraokeBeat = float.NaN;
		_cachedSubtitleCamera = null;
		_cachedSubtitleViewportState = null;
		_nextCameraResolveTime = 0f;
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

		RefreshSubtitleCamera();

		if (!_timingResolved)
		{
			ResolveCueTiming(_track, _loader, jukebox);
			_timingResolved = true;
		}

		var beat = jukebox.CurrentBeat;
		var cue = FindActiveCue(_track.Cues, beat);
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

		var displayStyle = ResolveDisplayStyle(cue.Style);
		var hasKaraoke = cue.KaraokeSegments.Count > 0;
		if (!hasKaraoke || !_config.KaraokeEnabled.Value)
		{
			SetDisplayText(cue.Text, displayStyle);
			return;
		}

		SetKaraokeDisplay(cue, beat, displayStyle);
	}

	private SubtitleCueStyle ResolveDisplayStyle(SubtitleCueStyle? cueStyle)
	{
		var configuredStyle = BuildConfiguredDisplayStyle();
		if (_config is null || !_config.UseAssStyles.Value || cueStyle is null)
		{
			return configuredStyle;
		}

		return MergeStyles(configuredStyle, cueStyle);
	}

	private SubtitleCueStyle BuildConfiguredDisplayStyle()
	{
		if (_config is null)
		{
			return new SubtitleCueStyle();
		}

		var fontName = string.IsNullOrWhiteSpace(_config.DisplayFontName.Value)
			? null
			: _config.DisplayFontName.Value;
		return new SubtitleCueStyle
		{
			FontName = fontName,
			FontSize = _config.DisplayFontSize.Value,
			ColorHexRgba = EnsureRgba(_config.DisplayColorHexRgba.Value, "#FFFFFFFF"),
			SecondaryColorHexRgba = EnsureRgba(_config.DisplaySecondaryColorHexRgba.Value, "#FFFF00FF"),
			OutlineColorHexRgba = EnsureRgba(_config.DisplayOutlineColorHexRgba.Value, "#000000FF"),
			OutlineWidth = Mathf.Max(0f, _config.DisplayOutlineWidth.Value),
			Bold = _config.DisplayBold.Value,
			Italic = _config.DisplayItalic.Value,
			Alignment = Mathf.Clamp(_config.DisplayAlignment.Value, 1, 9)
		};
	}

	private static SubtitleCueStyle MergeStyles(SubtitleCueStyle configuredStyle, SubtitleCueStyle cueStyle)
	{
		return new SubtitleCueStyle
		{
			FontName = cueStyle.FontName ?? configuredStyle.FontName,
			FontSize = cueStyle.FontSize ?? configuredStyle.FontSize,
			ColorHexRgba = cueStyle.ColorHexRgba ?? configuredStyle.ColorHexRgba,
			SecondaryColorHexRgba = cueStyle.SecondaryColorHexRgba ?? configuredStyle.SecondaryColorHexRgba,
			OutlineColorHexRgba = cueStyle.OutlineColorHexRgba ?? configuredStyle.OutlineColorHexRgba,
			OutlineWidth = cueStyle.OutlineWidth ?? configuredStyle.OutlineWidth,
			Bold = cueStyle.Bold ?? configuredStyle.Bold,
			Italic = cueStyle.Italic ?? configuredStyle.Italic,
			Alignment = cueStyle.Alignment ?? configuredStyle.Alignment
		};
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

	private static void ResolveCueTiming(SubtitleTrack track, MixtapeLoaderCustom loader, JukeboxScript jukebox)
	{
		var playbackRate = ResolvePlaybackRate(loader, jukebox);
		var secondsBeatScale = Mathf.Approximately(playbackRate, 0f) ? 1f : 1f / playbackRate;
		Log.Info(
			$"Resolving cue timing for mixtape using {jukebox.Bpm} BPM (playbackSpeed={playbackRate.ToString("F3", CultureInfo.InvariantCulture)})");
		foreach (var cue in track.Cues)
		{
			if (cue.UsesSecondsTiming && cue is { StartSeconds: not null, EndSeconds: not null })
			{
				cue.StartBeat = ResolveSecondsToBeat(jukebox, cue.StartSeconds.Value, secondsBeatScale);
				cue.EndBeat = ResolveSecondsToBeat(jukebox, cue.EndSeconds.Value, secondsBeatScale);
			}

			foreach (var segment in cue.KaraokeSegments)
			{
				if (!segment.Beat.HasValue && segment.Seconds.HasValue)
				{
					segment.Beat = ResolveSecondsToBeat(jukebox, segment.Seconds.Value, secondsBeatScale);
				}
			}
		}

		track.Cues.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
	}

	private static float ResolveSecondsToBeat(JukeboxScript jukebox, double seconds, float secondsBeatScale)
	{
		return jukebox.SecondsToBeats(seconds) * secondsBeatScale;
	}

	private static float ResolvePlaybackRate(MixtapeLoaderCustom? loader, JukeboxScript jukebox)
	{
		if (!TryGetBaseMixtapeBpm(loader, out var baseBpm, out var source))
		{
			Log.Info("ResolvePlaybackRate: base mixtape BPM unavailable; defaulting to 1.000");
			return 1f;
		}

		var playbackRate = jukebox.bpm / baseBpm;
		Log.Info(
			$"ResolvePlaybackRate: {jukebox.bpm.ToString("F3", CultureInfo.InvariantCulture)} / {baseBpm.ToString("F3", CultureInfo.InvariantCulture)} ({source})");
		return playbackRate > 0f ? playbackRate : 1f;
	}

	private static bool TryGetBaseMixtapeBpm(MixtapeLoaderCustom? loader, out float baseBpm, out string source)
	{
		baseBpm = 0f;
		source = "none";

		if (loader is not null && loader)
		{
			var riqLoader = RiqLoaderRef(loader);
			if (riqLoader is not null && riqLoader)
			{
				var loadedMixtape = RiqLoaderMixtapeRef(riqLoader);
				if (loadedMixtape is not null && loadedMixtape.bpm > 0f)
				{
					baseBpm = loadedMixtape.bpm;
					source = "riqLoader.mixtape";
					return true;
				}
			}
		}

		if (!MixtapeEditorScript.IsInEditor)
		{
			return false;
		}

		var editor = FindObjectOfType<MixtapeEditorScript>();
		if (editor is null || !editor)
		{
			return false;
		}

		var mixtapePropertiesEvent = MixtapePropertiesEventRef(editor);
		if (mixtapePropertiesEvent is null || !mixtapePropertiesEvent)
		{
			return false;
		}

		var entity = mixtapePropertiesEvent.Entity;
		if (entity is null)
		{
			return false;
		}

		var editorBpm = entity.GetFloat("bpm");
		if (editorBpm <= 0f)
		{
			return false;
		}

		baseBpm = editorBpm;
		source = "MixtapeEditorScript.mixtapePropertiesEvent";
		return true;
	}

	private void RefreshSubtitleCamera(bool force = false)
	{
		if (_loader is null || !_loader)
		{
			return;
		}

		var shouldResolve = force
			|| Time.unscaledTime >= _nextCameraResolveTime
			|| _cachedSubtitleCamera is null
			|| !_cachedSubtitleCamera
			|| !_cachedSubtitleCamera.enabled;
		if (!shouldResolve)
		{
			return;
		}

		var resolvedCamera = ResolveSubtitleCamera(_loader);
		var resolvedViewportState = CameraViewportState.Capture(resolvedCamera);
		var cameraChanged = !ReferenceEquals(_cachedSubtitleCamera, resolvedCamera);
		var viewportChanged = !_cachedSubtitleViewportState.HasValue
			|| !CameraViewportState.AreEquivalent(_cachedSubtitleViewportState.Value, resolvedViewportState);
		if (force || cameraChanged || viewportChanged)
		{
			_cachedSubtitleCamera = resolvedCamera;
			_cachedSubtitleViewportState = resolvedViewportState;
			_overlay.SetReferenceCamera(resolvedCamera);
		}

		_nextCameraResolveTime = Time.unscaledTime + CameraResolveIntervalSeconds;
	}

	private static SubtitleCue? FindActiveCue(List<SubtitleCue> cues, float beat)
	{
		var low = 0;
		var high = cues.Count - 1;
		while (low <= high)
		{
			var mid = low + ((high - low) >> 1);
			var cue = cues[mid];
			if (beat < cue.StartBeat)
			{
				high = mid - 1;
				continue;
			}

			if (beat >= cue.EndBeat)
			{
				low = mid + 1;
				continue;
			}

			return cue;
		}

		return null;
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

	private void SetKaraokeDisplay(SubtitleCue cue, float beat, SubtitleCueStyle style)
	{
		var activeIndex = FindActiveKaraokeSegmentIndex(cue, beat);
		var cueChanged = !ReferenceEquals(_cachedKaraokeCue, cue);
		var segmentChanged = activeIndex != _cachedActiveSegmentIndex;
		var beatChanged = float.IsNaN(_cachedKaraokeBeat) || !Mathf.Approximately(_cachedKaraokeBeat, beat);
		var styleKey = $"{BuildStyleKey(style)}|karaoke";
		var styleChanged = !string.Equals(_displayStyleKey, styleKey, StringComparison.Ordinal);

		// The active segment's fill progress changes every frame only for Kf (progressive-fill) tags.
		var isActiveFill = activeIndex >= 0
			&& activeIndex < cue.KaraokeSegments.Count
			&& cue.KaraokeSegments[activeIndex].TagType == KaraokeTagType.Kf
			&& (cue.KaraokeSegments[activeIndex].Beat ?? cue.StartBeat) <= beat;

		// Skip the (expensive) rebuild when nothing has changed.
		// Also rebuild when karaoke display wasn't already active (e.g. KaraokeEnabled toggled on).
		if (!cueChanged && !segmentChanged
			&& (!isActiveFill || !beatChanged)
			&& !styleChanged
			&& string.Equals(_displayText, KaraokeActiveSentinel, StringComparison.Ordinal))
		{
			return;
		}

		_cachedKaraokeCue = cue;
		_cachedActiveSegmentIndex = activeIndex;

		var segments = BuildKaraokeRenderSegments(cue, beat, style);
		_overlay.SetKaraokeText(cue.Text, style, segments);
		_displayText = KaraokeActiveSentinel;
		_displayStyleKey = styleKey;
		_cachedKaraokeBeat = beat;
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

	private List<KaraokeRenderSegment> BuildKaraokeRenderSegments(SubtitleCue cue, float beat, SubtitleCueStyle style)
	{
		RecycleKaraokeRenderSegments();
		var result = _karaokeSegmentsBuffer;
		if (cue.KaraokeSegments.Count == 0)
		{
			AddRenderSegment(
				result,
				0,
				cue.Text.Length,
				cue.Text,
				"#FFFFFFFF",
				"#FFFFFFFF",
				"#000000FF",
				1f);
			return result;
		}

		var primaryColor = EnsureRgba(style.ColorHexRgba, "#FFFFFFFF");
		var secondaryColor = EnsureRgba(style.SecondaryColorHexRgba, "#FFFF00FF");
		var outlineColor = EnsureRgba(style.OutlineColorHexRgba, "#000000FF");
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
				var gapLength = startIndex - textCursor;
				AddRenderSegment(
					result,
					textCursor,
					gapLength,
					cue.Text.Substring(textCursor, gapLength),
					primaryColor,
					primaryColor,
					outlineColor,
					1f);
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
			AddRenderSegment(
				result,
				textCursor,
				cue.Text.Length - textCursor,
				cue.Text.Substring(textCursor),
				primaryColor,
				primaryColor,
				outlineColor,
				1f);
		}

		return result;
	}

	private void AddRenderedSegment(
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
			AddRenderSegment(target, startCharIndex, text.Length, text, secondaryColor, secondaryColor, preOutline, 1f);
			return;
		}

		if (beat >= endBeat)
		{
			AddRenderSegment(target, startCharIndex, text.Length, text, primaryColor, primaryColor, outlineColor, 1f);
			return;
		}

		if (tagType == KaraokeTagType.Kf)
		{
			AddRenderSegment(target, startCharIndex, text.Length, text, secondaryColor, primaryColor, outlineColor, Mathf.Clamp01(phase));
			return;
		}

		AddRenderSegment(target, startCharIndex, text.Length, text, primaryColor, primaryColor, outlineColor, 1f);
	}

	private void RecycleKaraokeRenderSegments()
	{
		for (var i = 0; i < _karaokeSegmentsBuffer.Count; i++)
		{
			_karaokeSegmentPool.Push(_karaokeSegmentsBuffer[i]);
		}

		_karaokeSegmentsBuffer.Clear();
	}

	private void AddRenderSegment(
		List<KaraokeRenderSegment> target,
		int startCharIndex,
		int charLength,
		string text,
		string baseFaceColorHexRgba,
		string fillFaceColorHexRgba,
		string outlineColorHexRgba,
		float fillProgress)
	{
		if (charLength <= 0 || string.IsNullOrEmpty(text))
		{
			return;
		}

		var segment = _karaokeSegmentPool.Count > 0 ? _karaokeSegmentPool.Pop() : new KaraokeRenderSegment();
		segment.StartCharIndex = startCharIndex;
		segment.CharLength = charLength;
		segment.Text = text;
		segment.BaseFaceColorHexRgba = baseFaceColorHexRgba;
		segment.FillFaceColorHexRgba = fillFaceColorHexRgba;
		segment.OutlineColorHexRgba = outlineColorHexRgba;
		segment.FillProgress = fillProgress;
		target.Add(segment);
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

	private readonly struct CameraViewportState
	{
		public CameraViewportState(Rect rect, Rect pixelRect, RenderTexture? targetTexture)
		{
			Rect = rect;
			PixelRect = pixelRect;
			TargetTexture = targetTexture;
		}

		public Rect Rect { get; }
		public Rect PixelRect { get; }
		public RenderTexture? TargetTexture { get; }

		public static CameraViewportState Capture(Camera? camera)
		{
			return camera is null
				? new CameraViewportState(
					new Rect(0f, 0f, 1f, 1f),
					new Rect(0f, 0f, Screen.width, Screen.height),
					null)
				: new CameraViewportState(camera.rect, camera.pixelRect, camera.targetTexture);
		}

		public static bool AreEquivalent(CameraViewportState left, CameraViewportState right)
		{
			return AreRectsApproximatelyEqual(left.Rect, right.Rect)
				&& AreRectsApproximatelyEqual(left.PixelRect, right.PixelRect)
				&& ReferenceEquals(left.TargetTexture, right.TargetTexture);
		}

		private static bool AreRectsApproximatelyEqual(Rect a, Rect b)
		{
			return Mathf.Approximately(a.x, b.x)
				&& Mathf.Approximately(a.y, b.y)
				&& Mathf.Approximately(a.width, b.width)
				&& Mathf.Approximately(a.height, b.height);
		}
	}

}
