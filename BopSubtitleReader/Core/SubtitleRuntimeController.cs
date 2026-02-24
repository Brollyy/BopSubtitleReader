using System;
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
	private static readonly AccessTools.FieldRef<MixtapeLoaderCustom, Camera?> CameraScriptRef =
		AccessTools.FieldRefAccess<MixtapeLoaderCustom, Camera?>("cameraScript");

	private readonly TmpSubtitleOverlay _overlay = new();

	private SubtitleTrack? _track;
	private SubtitleCue? _activeCue;
	private MixtapeLoaderCustom? _loader;
	private KaraokeIndicatorAdapter? _karaoke;
	private SubtitleConfig? _config;
	private bool _timingResolved;
	private string _displayText = string.Empty;
	private string _displayStyleKey = string.Empty;
	private bool _loggedKaraokeAdapterUnavailable;
	private bool _loggedKaraokeModeValue;
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

	public void StartSession(MixtapeLoaderCustom loader, SubtitleTrack track, SubtitleConfig config, KaraokeIndicatorAdapter karaoke)
	{
		_loader = loader;
		_track = track;
		_config = config;
		_karaoke = karaoke;
		_activeCue = null;
		_displayText = string.Empty;
		_displayStyleKey = string.Empty;
		_timingResolved = false;
		_loggedKaraokeAdapterUnavailable = false;
		_loggedKaraokeModeValue = false;
		_overlay.Hide();
		var karaokeCueCount = track.Cues.Count(c => c.KaraokeSegments.Count > 0);
		Log.Info($"Subtitle session started with {track.Cues.Count} cue(s), {karaokeCueCount} karaoke cue(s).");
		if (karaokeCueCount > 0)
		{
			string mode = NormalizeKaraokeMode(config.KaraokeMode.Value);
			if (mode is "auto" or "force")
			{
				bool available = _karaoke?.IsAvailable == true;
				string reason = _karaoke?.UnavailabilityReason ?? "Karaoke adapter instance missing.";
				Log.Info($"Karaoke indicator prewarm result: available={available}, reason='{reason}'.");
			}
		}
	}

	public void StopSession()
	{
		_track = null;
		_loader = null;
		_activeCue = null;
		_displayText = string.Empty;
		_displayStyleKey = string.Empty;
		_timingResolved = false;
		_loggedKaraokeAdapterUnavailable = false;
		_loggedKaraokeModeValue = false;
		_karaoke?.Hide();
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
			_karaoke?.Hide();
			return;
		}

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
			_karaoke?.Hide();
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
		var mode = NormalizeKaraokeMode(_config.KaraokeMode.Value);
		var karaokeRequested = mode is "auto" or "force";
		var forceKaraoke = mode == "force";

		if (!hasKaraoke || !karaokeRequested)
		{
			SetDisplayText(cue.Text, cue.Style);
			return;
		}

		if (!_loggedKaraokeModeValue)
		{
			Log.Info($"Karaoke mode resolved to '{mode}'.");
			_loggedKaraokeModeValue = true;
		}

		if (_karaoke?.IsAvailable == true)
		{
			_karaoke.Show(cue, beat, GetActiveCamera());
			SetDisplayText(cue.Text, cue.Style);
			return;
		}

		if (!_loggedKaraokeAdapterUnavailable)
		{
			Log.Warn($"Karaoke requested but indicator is unavailable (mode={mode}).");
			_loggedKaraokeAdapterUnavailable = true;
		}

		if (!forceKaraoke)
		{
			SetDisplayText(cue.Text, cue.Style);
			return;
		}

		SetDisplayText(BuildKaraokeFallbackText(cue, beat), cue.Style);
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
		return $"{style.FontName}|{fontSize}|{style.ColorHexRgba}|{style.Bold}|{style.Italic}|{style.Alignment}";
	}

	private Camera? GetActiveCamera()
	{
		if (_loader is null)
		{
			return Camera.main;
		}

		var cameraScript = CameraScriptRef(_loader);
		return cameraScript ?? Camera.main;
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

	private static string BuildKaraokeFallbackText(SubtitleCue cue, float beat)
	{
		if (cue.KaraokeSegments.Count == 0)
		{
			return cue.Text;
		}

		var activeIndex = 0;
		for (var i = 0; i < cue.KaraokeSegments.Count; i++)
		{
			var markerBeat = cue.KaraokeSegments[i].Beat ?? cue.StartBeat;
			if (beat >= markerBeat)
			{
				activeIndex = i;
			}
		}

		return $"{cue.Text}\n< {cue.KaraokeSegments[activeIndex].Text} >";
	}

	private static string Truncate(string value, int max)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= max)
		{
			return value;
		}

		return value.Substring(0, max) + "...";
	}

	private static string NormalizeKaraokeMode(string? configuredValue)
	{
		string mode = (configuredValue ?? string.Empty).Trim().ToLowerInvariant();
		if (mode is "off" or "auto" or "force")
		{
			return mode;
		}

		if (!string.IsNullOrWhiteSpace(configuredValue))
		{
			Log.Warn($"Unknown KaraokeMode '{configuredValue}'. Using Auto.");
		}
		return "auto";
	}
}
