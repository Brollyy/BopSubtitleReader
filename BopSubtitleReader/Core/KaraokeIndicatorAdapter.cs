using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BopSubtitleReader.Core;

public sealed class KaraokeIndicatorAdapter
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<KaraokeIndicatorAdapter>();

	private GameObject? _indicatorObject;
	private SpriteRenderer? _renderer;
	private Sprite? _bopSprite;
	private bool _initialized;
	private Vector2 _currentViewportPos = new(0.5f, 0.14f);
	private bool _hasPosition;

	public bool IsAvailable { get; private set; }

	public void UpdateDisplay(SubtitleCue cue, float beat, TmpSubtitleOverlay? overlay)
	{
		if (!EnsureInitialized() || _indicatorObject is null || _renderer is null || _bopSprite is null)
		{
			return;
		}

		var camera = Camera.main;
		if (camera is null)
		{
			return;
		}

		Show();

		var activeSegmentIndex = -1;
		for (var i = 0; i < cue.KaraokeSegments.Count; i++)
		{
			var markerBeat = cue.KaraokeSegments[i].Beat ?? cue.StartBeat;
			if (beat >= markerBeat)
			{
				activeSegmentIndex = i;
			}
		}

		if (activeSegmentIndex < 0)
		{
			Hide();
			return;
		}

		var activeSegment = cue.KaraokeSegments[activeSegmentIndex];
		var activeMarkerBeat = activeSegment.Beat ?? cue.StartBeat;

		// Phase speed scales with segment duration so the indicator bounces once per segment
		// regardless of tempo — slower segments get a slower bounce.
		var nextMarkerBeat = activeSegmentIndex + 1 < cue.KaraokeSegments.Count
			? cue.KaraokeSegments[activeSegmentIndex + 1].Beat ?? cue.EndBeat
			: cue.EndBeat;
		var segmentDurationBeats = Mathf.Max(nextMarkerBeat - activeMarkerBeat, 0.001f);
		var phase = Mathf.Clamp01((beat - activeMarkerBeat) / segmentDurationBeats);
		var pulse = Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 2f));

		var targetViewportX = 0.5f;
		var targetViewportY = 0.14f;
		var charHeightFraction = 0f;
		var charStart = FindSegmentCharStart(cue, activeSegmentIndex);
		var segPos = charStart >= 0
			? overlay?.GetSegmentViewportPosition(charStart, activeSegment.Text.Length)
			: null;
		if (segPos.HasValue)
		{
			targetViewportX = segPos.Value.x;
			targetViewportY = segPos.Value.y;
			charHeightFraction = segPos.Value.z;
		}

		// Lerp toward the target viewport position for smooth transitions.
		// If the target Y changed by more than half a character height, the indicator is moving
		// to a different line — snap directly to avoid a distracting cross-line sweep.
		var target = new Vector2(targetViewportX, targetViewportY);
		var isLineChange = _hasPosition
			&& charHeightFraction > 0f
			&& Mathf.Abs(targetViewportY - _currentViewportPos.y) > charHeightFraction * 0.5f;
		_currentViewportPos = (!_hasPosition || isLineChange)
			? target
			: Vector2.Lerp(_currentViewportPos, target, Mathf.Min(Time.deltaTime * 15f, 1f));
		_hasPosition = true;

		var worldPosition = camera.ViewportToWorldPoint(new Vector3(_currentViewportPos.x, _currentViewportPos.y, 4f));
		if (camera.orthographic)
		{
			worldPosition.z = 0f;
		}

		// Bounce the indicator above the text.
		_indicatorObject.transform.position = worldPosition + new Vector3(0f, pulse * 0.15f, 0f);

		// Scale proportional to character height; fall back to fixed size when unknown.
		float scale;
		if (charHeightFraction > 0f)
		{
			var worldAtTop = camera.ViewportToWorldPoint(new Vector3(_currentViewportPos.x, _currentViewportPos.y + charHeightFraction, 4f));
			var charHeightWorld = Mathf.Abs(worldAtTop.y - worldPosition.y);
			scale = charHeightWorld * 0.6f;
		}
		else
		{
			scale = 0.35f;
		}

		_indicatorObject.transform.localScale = Vector3.one * (scale + pulse * scale * 0.2f);
	}

	/// <summary>
	/// Finds the character start index of the segment at <paramref name="activeSegmentIndex"/>
	/// within <see cref="SubtitleCue.Text"/>.
	/// <para>
	/// Assumes that segments appear in the same left-to-right order as they do in
	/// <see cref="SubtitleCue.Text"/> (i.e. segment[i].Text is always found after the end of
	/// segment[i-1].Text). Both parsers (ASS and JSON) produce segments in text order, so this
	/// assumption holds in practice. If a segment's text cannot be found starting from the
	/// expected position, the method returns -1 and the indicator falls back to center.
	/// </para>
	/// </summary>
	private static int FindSegmentCharStart(SubtitleCue cue, int activeSegmentIndex)
	{
		var searchFrom = 0;
		for (var i = 0; i <= activeSegmentIndex; i++)
		{
			var segText = cue.KaraokeSegments[i].Text;
			if (string.IsNullOrEmpty(segText))
			{
				continue;
			}

			var found = cue.Text.IndexOf(segText, searchFrom, StringComparison.Ordinal);
			if (found < 0)
			{
				return -1;
			}

			if (i == activeSegmentIndex)
			{
				return found;
			}

			searchFrom = found + segText.Length;
		}

		return -1;
	}

	public void Show()
	{
		if (_indicatorObject is null || _indicatorObject.activeSelf) return;
		Log.Trace("Karaoke indicator showing.");
		_indicatorObject.SetActive(true);
	}

	public void Hide()
	{
		if (_indicatorObject is null || !_indicatorObject.activeSelf) return;
		Log.Trace("Karaoke indicator is hidden.");
		_indicatorObject.SetActive(false);
		_hasPosition = false;
	}

	public bool EnsureInitialized()
	{
		if (_initialized)
		{
			return IsAvailable;
		}

		_initialized = true;
		_bopSprite = LoadBundledSprite();
		if (_bopSprite is null)
		{
			Log.Warn($"Could not load bundled karaoke ball sprite.");
			IsAvailable = false;
			return false;
		}

		var host = new GameObject("BSR_KaraokeBallIndicator");
		UnityEngine.Object.DontDestroyOnLoad(host);

		_renderer = host.AddComponent<SpriteRenderer>();
		_renderer.sortingOrder = 32000;
		_renderer.sprite = _bopSprite;
		_renderer.color = Color.white;

		_indicatorObject = host;
		_indicatorObject.SetActive(false);
		IsAvailable = true;

		Log.Info("Custom karaoke indicator initialized from bundled sprite.");
		return true;
	}

	private static Sprite? LoadBundledSprite()
	{
		try
		{
			byte[]? pngBytes = TryLoadEmbeddedPngBytes();
			if (pngBytes is null)
			{
				var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
				var spritePath = Path.Combine(pluginDirectory, "assets", "karaoke_bop.png");
				if (!File.Exists(spritePath))
				{
					return null;
				}

				pngBytes = File.ReadAllBytes(spritePath);
			}

			var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
			if (!texture.LoadImage(pngBytes))
			{
				UnityEngine.Object.Destroy(texture);
				return null;
			}

			texture.name = "karaoke_bop";
			texture.filterMode = FilterMode.Bilinear;
			var rect = new Rect(0f, 0f, texture.width, texture.height);
			return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static byte[]? TryLoadEmbeddedPngBytes()
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
		string[] names = assembly.GetManifestResourceNames();
		string? resourceName = names.FirstOrDefault(
			name => name.EndsWith("assets.karaoke_bop.png", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("karaoke_bop.png", StringComparison.OrdinalIgnoreCase));
		if (resourceName is null)
		{
			return null;
		}

		using Stream? stream = assembly.GetManifestResourceStream(resourceName);
		if (stream is null)
		{
			return null;
		}

		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		Log.Info($"Loaded karaoke sprite from embedded resource '{resourceName}'.");
		return memory.ToArray();
	}
}
