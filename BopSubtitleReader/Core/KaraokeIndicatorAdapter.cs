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
	private bool _available;
	private bool _loggedMissingMainCamera;
	private bool _loggedFirstShow;
	private string _unavailabilityReason = "Not initialized.";

	public bool IsAvailable
	{
		get
		{
			EnsureInitialized();
			return _available;
		}
	}

	public string UnavailabilityReason => _available ? "Available" : _unavailabilityReason;

	public void Show(SubtitleCue cue, float beat)
	{
		if (!EnsureInitialized() || _indicatorObject is null || _renderer is null || _bopSprite is null)
		{
			return;
		}

		var camera = Camera.main;
		if (camera is null)
		{
			if (!_loggedMissingMainCamera)
			{
				Log.Warn("Karaoke indicator cannot render because Camera.main is null.");
				_loggedMissingMainCamera = true;
			}
			return;
		}

		_indicatorObject.SetActive(true);
		_renderer.sprite = _bopSprite;
		if (!_loggedFirstShow)
		{
			Log.Info("Karaoke indicator is active.");
			_loggedFirstShow = true;
		}

		var activeMarkerBeat = cue.StartBeat;
		foreach (var segment in cue.KaraokeSegments)
		{
			var markerBeat = segment.Beat ?? cue.StartBeat;
			if (beat >= markerBeat)
			{
				activeMarkerBeat = markerBeat;
			}
		}

		var phase = Mathf.Clamp01(beat - activeMarkerBeat);
		var pulse = Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 2f));

		var worldPosition = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.14f, 4f));
		if (camera.orthographic)
		{
			worldPosition.z = 0f;
		}

		_indicatorObject.transform.position = worldPosition + new Vector3(0f, pulse * 0.15f, 0f);
		_indicatorObject.transform.localScale = Vector3.one * (0.35f + pulse * 0.1f);
	}

	public void Hide()
	{
		if (_indicatorObject is not null && _indicatorObject.activeSelf)
		{
			_indicatorObject.SetActive(false);
		}
	}

	private bool EnsureInitialized()
	{
		if (_initialized)
		{
			return _available;
		}

		_initialized = true;
		_bopSprite = LoadBundledSprite();
		if (_bopSprite is null)
		{
			if (string.IsNullOrWhiteSpace(_unavailabilityReason))
			{
				_unavailabilityReason = "Could not load karaoke sprite.";
			}
			Log.Warn($"Could not load bundled karaoke sprite. {_unavailabilityReason}");
			_available = false;
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
		_available = true;
		_unavailabilityReason = "Available";
		Log.Info("Custom karaoke indicator initialized from bundled bop sprite.");
		return true;
	}

	private Sprite? LoadBundledSprite()
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
					_unavailabilityReason = $"Bundled karaoke sprite not found. Searched embedded resource and '{spritePath}'.";
					return null;
				}

				pngBytes = File.ReadAllBytes(spritePath);
			}

			var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
			if (!texture.LoadImage(pngBytes))
			{
				UnityEngine.Object.Destroy(texture);
				_unavailabilityReason = "Failed to decode karaoke sprite png bytes.";
				return null;
			}

			texture.name = "karaoke_bop";
			texture.filterMode = FilterMode.Bilinear;
			var rect = new Rect(0f, 0f, texture.width, texture.height);
			return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f);
		}
		catch (Exception ex)
		{
			_unavailabilityReason = $"Failed loading karaoke sprite: {ex.Message}";
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
