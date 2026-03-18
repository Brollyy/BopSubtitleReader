using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BopSubtitleReader.Core;

public sealed class TmpSubtitleOverlay
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<TmpSubtitleOverlay>();

	private bool _initializationAttempted;
	private bool _available;
	private GameObject? _host;
	private GameObject? _cameraHost;
	private Camera? _overlayCamera;
	private Canvas? _canvas;
	private TextMeshProUGUI? _tmpText;
	private RectTransform? _tmpRect;
	private GameObject? _karaokeLayer;
	private readonly List<KaraokeSegmentView> _karaokeTextPool = [];
	private TMP_FontAsset? _defaultFontAsset;
	private readonly Dictionary<string, TMP_FontAsset> _runtimeFontCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _failedFontLookups = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _missingRequestedFontsWarned = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<TMP_FontAsset> _configuredFallbackFonts = [];
	private readonly Dictionary<TextMeshProUGUI, OutlineState> _outlineStateCache = [];
	private OverlayStyleState _defaultStyle = OverlayStyleState.CreateDefault();

	public void SetText(string text, SubtitleCueStyle? style)
	{
		if (!EnsureInitialized() || _tmpText is null || _host is null)
		{
			return;
		}

		ApplyStyle(_tmpText, style);
		_tmpText.text = text;
		// Force TMP to rebuild the mesh immediately so textInfo.characterInfo reflects the
		// current text and layout before GetSegmentViewportPosition queries it this frame.
		_tmpText.ForceMeshUpdate();
		HideKaraokeLayer();
		if (!_host.activeSelf)
		{
			_host.SetActive(true);
		}
	}

	public void SetKaraokeText(string text, SubtitleCueStyle? style, IReadOnlyList<KaraokeRenderSegment> segments)
	{
		if (!EnsureInitialized() || _tmpText is null || _tmpRect is null || _host is null)
		{
			return;
		}

		ApplyStyle(_tmpText, style);
		_tmpText.text = text;
		ApplyHiddenBaseLayoutStyle();
		_tmpText.ForceMeshUpdate();

		var layer = EnsureKaraokeLayer();
		if (layer is null)
		{
			return;
		}

		for (var i = 0; i < segments.Count; i++)
		{
			var segment = segments[i];
			if (!TryGetSegmentAnchor(segment.StartCharIndex, segment.CharLength, out var anchorX, out var anchorY))
			{
				SetKaraokeLabelActive(i, false);
				continue;
			}

			var label = EnsureKaraokeLabel(i);
			if (label is null)
			{
				continue;
			}

			var rootRect = label.RootRect;
			rootRect.anchoredPosition = new Vector2(anchorX, anchorY);
			var segmentRectSize = new Vector2(Mathf.Max(4f, _tmpRect.rect.width), Mathf.Max(4f, _tmpRect.rect.height));
			rootRect.sizeDelta = segmentRectSize;
			label.BaseRect.sizeDelta = segmentRectSize;
			label.FillRect.sizeDelta = segmentRectSize;
			label.MaskRect.anchoredPosition = Vector2.zero;

			ApplyStyle(label.BaseText, style, segment.BaseFaceColorHexRgba, segment.OutlineColorHexRgba);
			ApplyStyle(label.FillText, style, segment.FillFaceColorHexRgba, segment.OutlineColorHexRgba);
			label.BaseText.alignment = TextAlignmentOptions.BottomLeft;
			label.FillText.alignment = TextAlignmentOptions.BottomLeft;
			label.BaseText.textWrappingMode = TextWrappingModes.NoWrap;
			label.FillText.textWrappingMode = TextWrappingModes.NoWrap;
			label.BaseText.text = segment.Text;
			label.FillText.text = segment.Text;
			label.FillText.ForceMeshUpdate();
			var fillWidth = Mathf.Max(0f, label.FillText.preferredWidth);
			label.MaskRect.sizeDelta = new Vector2(fillWidth * Mathf.Clamp01(segment.FillProgress), segmentRectSize.y);
			SetKaraokeLabelActive(i, true);
		}

		for (var i = segments.Count; i < _karaokeTextPool.Count; i++)
		{
			SetKaraokeLabelActive(i, false);
		}

		if (!_host.activeSelf)
		{
			_host.SetActive(true);
		}
	}

	public void Hide()
	{
		if (_host is not null && _host.activeSelf)
		{
			_host.SetActive(false);
		}
	}

	/// <summary>
	/// Syncs the subtitle overlay camera's viewport to match <paramref name="referenceCamera"/>'s
	/// viewport rect so that subtitles are confined to the same on-screen area (important when
	/// the game camera is not full-screen, e.g. in the Bits &amp; Bops editor).
	/// Pass <c>null</c> to reset to a full-screen viewport.
	/// </summary>
	public void SetReferenceCamera(Camera? referenceCamera)
	{
		if (_overlayCamera is null)
		{
			return;
		}

		_overlayCamera.rect = referenceCamera
			? referenceCamera.rect
			: new Rect(0f, 0f, 1f, 1f);
	}

	private bool EnsureInitialized()
	{
		if (_available)
		{
			return true;
		}

		if (_initializationAttempted)
		{
			return false;
		}

		_initializationAttempted = true;

		// Create a dedicated overlay camera for the subtitle canvas.
		// Using a separate camera (not the game camera) prevents any post-processing effects
		// such as BopVisualEffects from being applied to the subtitle canvas.
		// Using ScreenSpaceCamera (rather than ScreenSpaceOverlay) ensures the canvas viewport
		// tracks the game camera's viewport rect, which is important in the editor where the
		// game view is not full-screen.
		var cameraHost = new GameObject("BOP_SubtitleCameraHost");
		Object.DontDestroyOnLoad(cameraHost);
		var overlayCamera = cameraHost.AddComponent<Camera>();
		overlayCamera.clearFlags = CameraClearFlags.Depth;
		overlayCamera.cullingMask = 0; // Render nothing from the 3D scene; only the canvas
		overlayCamera.depth = 100;     // Render after the main game camera
		overlayCamera.rect = new Rect(0f, 0f, 1f, 1f);
		_cameraHost = cameraHost;
		_overlayCamera = overlayCamera;

		var canvasObject = new GameObject("BOP_SubtitleCanvas");
		Object.DontDestroyOnLoad(canvasObject);
		// Place on the UI layer so that game-world cameras cannot accidentally render it.
		canvasObject.layer = 5; // Unity built-in "UI" layer

		var canvas = canvasObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceCamera;
		canvas.worldCamera = overlayCamera;
		canvas.sortingOrder = 100;
		_canvas = canvas;

		var textObject = new GameObject("BOP_SubtitleText");
		textObject.transform.SetParent(canvasObject.transform, false);

		var rect = textObject.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.08f, 0.02f);
		rect.anchorMax = new Vector2(0.92f, 0.20f);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;

		AssignDefaultFontAsset();
		ApplyTmpGlobalDefaultFont();

		_tmpText = textObject.AddComponent<TextMeshProUGUI>();
		if (_defaultFontAsset is not null)
		{
			_tmpText.font = _defaultFontAsset;
		}
		_tmpRect = rect;
		_tmpText.fontSize = 40f;
		_tmpText.textWrappingMode = TextWrappingModes.Normal;
		_tmpText.richText = true;
		_tmpText.alignment = TextAlignmentOptions.Bottom;

		CaptureDefaultStyle();

		_host = canvasObject;
		_available = true;
		_host.SetActive(false);
		Log.Info("TMP subtitle overlay initialized.");
		return true;
	}

	private void CaptureDefaultStyle()
	{
		if (_tmpText is null)
		{
			return;
		}

		_defaultStyle = new OverlayStyleState
		{
			FontSize = _tmpText.fontSize,
			Color = _tmpText.color,
			OutlineColor = Color.black,
			OutlineWidth = 2f,
			Bold = false,
			Italic = false,
			Alignment = 2
		};
	}

	private void ApplyStyle(TextMeshProUGUI text, SubtitleCueStyle? style, string? faceColorOverrideHex = null, string? outlineColorOverrideHex = null)
	{
		ApplyFont(text, style?.FontName);

		text.fontSize = style?.FontSize ?? _defaultStyle.FontSize;

		var color = _defaultStyle.Color;
		var colorHex = string.IsNullOrWhiteSpace(faceColorOverrideHex) ? style?.ColorHexRgba : faceColorOverrideHex;
		if (!string.IsNullOrWhiteSpace(colorHex)
		&& ColorUtility.TryParseHtmlString(colorHex, out var parsedColor))
		{
			color = parsedColor;
		}

		text.color = color;

		var outlineColor = _defaultStyle.OutlineColor;
		var outlineHex = string.IsNullOrWhiteSpace(outlineColorOverrideHex) ? style?.OutlineColorHexRgba : outlineColorOverrideHex;
		if (!string.IsNullOrWhiteSpace(outlineHex)
			&& ColorUtility.TryParseHtmlString(outlineHex, out var parsedOutline))
		{
			outlineColor = parsedOutline;
		}

		var assOutlineWidth = style?.OutlineWidth ?? _defaultStyle.OutlineWidth;
		var tmpOutlineWidth = Mathf.Clamp(assOutlineWidth * 0.04f, 0f, 0.25f);
		ApplyOutline(text, outlineColor, tmpOutlineWidth);

		var bold = style?.Bold ?? _defaultStyle.Bold;
		var italic = style?.Italic ?? _defaultStyle.Italic;
		var fontStyle = FontStyles.Normal;
		if (bold)
		{
			fontStyle |= FontStyles.Bold;
		}

		if (italic)
		{
			fontStyle |= FontStyles.Italic;
		}

		text.fontStyle = fontStyle;

		var alignment = style?.Alignment ?? _defaultStyle.Alignment;
		text.alignment = alignment switch
		{
			1 => TextAlignmentOptions.BottomLeft,
			2 => TextAlignmentOptions.Bottom,
			3 => TextAlignmentOptions.BottomRight,
			4 => TextAlignmentOptions.Left,
			5 => TextAlignmentOptions.Center,
			6 => TextAlignmentOptions.Right,
			7 => TextAlignmentOptions.TopLeft,
			8 => TextAlignmentOptions.Top,
			9 => TextAlignmentOptions.TopRight,
			_ => TextAlignmentOptions.Bottom
		};
	}

	/// <summary>
	/// Loads a TMP font asset from Unity's standard "Fonts &amp; Materials" resource folder.
	/// </summary>
	private static TMP_FontAsset? LoadFontAsset(string fontName)
	{
		return Resources.Load<TMP_FontAsset>($"Fonts & Materials/{fontName}");
	}

	private void AssignDefaultFontAsset()
	{
		string[] primaryCandidates = ["NotInter-Regular", "Arial", "Kardia Fat Runner"];
		TMP_FontAsset? primary = null;
		foreach (var name in primaryCandidates)
		{
			primary = ResolveFontAsset(name, allowRuntimeFontCreate: true);
			if (primary is not null)
			{
				Log.Info($"Primary subtitle font: {primary.name} (requested: {name}).");
				break;
			}
		}

		string[] cjkFallbacks = ["MPLUSRounded1c-Regular SDF", "TaiwanPearl-Regular SDF", "Binggrae SDF"];
		var fallbackList = new List<TMP_FontAsset>();
		foreach (var name in cjkFallbacks)
		{
			var fallback = ResolveFontAsset(name, allowRuntimeFontCreate: true);
			if (fallback is not null && !ReferenceEquals(fallback, primary))
			{
				fallbackList.Add(fallback);
			}
		}

		if (primary is null && fallbackList.Count > 0)
		{
			primary = fallbackList[0];
			fallbackList.RemoveAt(0);
			Log.Warn($"No preferred primary subtitle font found. Promoting fallback '{primary.name}' as primary.");
		}

		if (primary is null)
		{
			Log.Warn("No primary TMP font asset found for subtitle overlay.");
			return;
		}

		_configuredFallbackFonts.Clear();
		_configuredFallbackFonts.AddRange(fallbackList);
		if (fallbackList.Count > 0)
		{
			primary.fallbackFontAssetTable = fallbackList;
			Log.Info($"Configured {fallbackList.Count} CJK fallback font(s) for subtitle overlay.");
		}

		_defaultFontAsset = primary;
	}

	private void ApplyFont(TextMeshProUGUI text, string? requestedFontName)
	{
		TMP_FontAsset? selected = null;
		if (!string.IsNullOrWhiteSpace(requestedFontName))
		{
			selected = ResolveFontAsset(requestedFontName!, allowRuntimeFontCreate: false);

			if (selected is null)
			{
				if (_missingRequestedFontsWarned.Add(requestedFontName!))
				{
					Log.Warn($"Requested subtitle font '{requestedFontName}' was not found. Using fallback.");
				}
			}
		}

		selected ??= _defaultFontAsset;
		if (selected is null)
		{
			return;
		}

		EnsureFallbackFonts(selected);
		text.font = selected;
	}

	private TMP_FontAsset? ResolveFontAsset(string fontName, bool allowRuntimeFontCreate)
	{
		if (string.IsNullOrWhiteSpace(fontName))
		{
			return null;
		}

		if (_failedFontLookups.Contains(fontName))
		{
			return null;
		}

		if (_runtimeFontCache.TryGetValue(fontName, out var cached))
		{
			return cached;
		}

		var direct = LoadFontAsset(fontName);
		if (direct is null && !fontName.EndsWith(" SDF", StringComparison.OrdinalIgnoreCase))
		{
			direct = LoadFontAsset($"{fontName} SDF");
		}

		if (direct is not null)
		{
			_runtimeFontCache[fontName] = direct;
			return direct;
		}

		if (!allowRuntimeFontCreate)
		{
			_failedFontLookups.Add(fontName);
			return null;
		}

		var sourceName = fontName.EndsWith(" SDF", StringComparison.OrdinalIgnoreCase)
			? fontName.Substring(0, fontName.Length - 4)
			: fontName;
		var fonts = Resources.FindObjectsOfTypeAll<Font>();
		var sourceFont = fonts.FirstOrDefault(f => string.Equals(f.name, sourceName, StringComparison.OrdinalIgnoreCase))
			?? fonts.FirstOrDefault(f => string.Equals(f.name, fontName, StringComparison.OrdinalIgnoreCase));
		if (sourceFont is null)
		{
			_failedFontLookups.Add(fontName);
			return null;
		}

		try
		{
			var created = TMP_FontAsset.CreateFontAsset(sourceFont);
			if (created is null)
			{
				return null;
			}

			_runtimeFontCache[fontName] = created;
			_runtimeFontCache[sourceFont.name] = created;
			Log.Info($"Created runtime TMP font asset from source font '{sourceFont.name}'.");
			return created;
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to create TMP font asset from '{sourceFont.name}': {ex.Message}");
			_failedFontLookups.Add(fontName);
			return null;
		}
	}

	private void ApplyTmpGlobalDefaultFont()
	{
		if (_defaultFontAsset is null)
		{
			return;
		}

		if (TMP_Settings.instance is null)
		{
			return;
		}

		if (!ReferenceEquals(TMP_Settings.defaultFontAsset, _defaultFontAsset))
		{
			TMP_Settings.defaultFontAsset = _defaultFontAsset;
		}
	}

	private void EnsureFallbackFonts(TMP_FontAsset fontAsset)
	{
		if (_configuredFallbackFonts.Count == 0)
		{
			return;
		}

		if (fontAsset.fallbackFontAssetTable is null)
		{
			fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>(_configuredFallbackFonts);
			return;
		}

		foreach (var fallback in _configuredFallbackFonts)
		{
			if (!fontAsset.fallbackFontAssetTable.Contains(fallback))
			{
				fontAsset.fallbackFontAssetTable.Add(fallback);
			}
		}
	}

	private void ApplyHiddenBaseLayoutStyle()
	{
		if (_tmpText is null)
		{
			return;
		}

		var transparent = _tmpText.color;
		transparent.a = 0f;
		_tmpText.color = transparent;
		ApplyOutline(_tmpText, new Color(0f, 0f, 0f, 0f), 0f);
	}

	private void ApplyOutline(TextMeshProUGUI text, Color outlineColor, float outlineWidth)
	{
		if (_outlineStateCache.TryGetValue(text, out var cached)
			&& cached.Color == outlineColor
			&& Mathf.Approximately(cached.Width, outlineWidth))
		{
			return;
		}

		var mat = text.fontMaterial;
		if (mat is null)
		{
			return;
		}

		mat.EnableKeyword("OUTLINE_ON");
		mat.SetFloat("_OutlineWidth", outlineWidth);
		mat.SetColor("_OutlineColor", outlineColor);
		text.UpdateMeshPadding();

		if (cached is null)
		{
			_outlineStateCache[text] = new OutlineState
			{
				Color = outlineColor,
				Width = outlineWidth
			};
			return;
		}

		cached.Color = outlineColor;
		cached.Width = outlineWidth;
	}

	private GameObject? EnsureKaraokeLayer()
	{
		if (_karaokeLayer is not null)
		{
			_karaokeLayer.SetActive(true);
			return _karaokeLayer;
		}

		if (_tmpText is null)
		{
			return null;
		}

		_karaokeLayer = new GameObject("BOP_KaraokeLayer");
		_karaokeLayer.transform.SetParent(_tmpText.transform, false);
		var rect = _karaokeLayer.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(1f, 1f);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		return _karaokeLayer;
	}

	private KaraokeSegmentView? EnsureKaraokeLabel(int index)
	{
		while (_karaokeTextPool.Count <= index)
		{
			if (_karaokeLayer is null)
			{
				return null;
			}

			var rootGo = new GameObject($"KaraokeSeg_{_karaokeTextPool.Count:D2}");
			rootGo.transform.SetParent(_karaokeLayer.transform, false);
			var rootRect = rootGo.AddComponent<RectTransform>();
			rootRect.anchorMin = new Vector2(0.5f, 0.5f);
			rootRect.anchorMax = new Vector2(0.5f, 0.5f);
			rootRect.pivot = new Vector2(0f, 0f);
			rootRect.anchoredPosition = Vector2.zero;

			var baseGo = new GameObject("Base");
			baseGo.transform.SetParent(rootGo.transform, false);
			var baseRect = baseGo.AddComponent<RectTransform>();
			baseRect.anchorMin = new Vector2(0f, 0f);
			baseRect.anchorMax = new Vector2(0f, 0f);
			baseRect.pivot = new Vector2(0f, 0f);
			baseRect.anchoredPosition = Vector2.zero;
			ApplyTmpGlobalDefaultFont();
			var baseText = baseGo.AddComponent<TextMeshProUGUI>();
			baseText.richText = false;
			baseText.textWrappingMode = TextWrappingModes.NoWrap;
			if (_defaultFontAsset is not null)
			{
				baseText.font = _defaultFontAsset;
			}

			var maskGo = new GameObject("FillMask");
			maskGo.transform.SetParent(rootGo.transform, false);
			var maskRect = maskGo.AddComponent<RectTransform>();
			maskRect.anchorMin = new Vector2(0f, 0f);
			maskRect.anchorMax = new Vector2(0f, 0f);
			maskRect.pivot = new Vector2(0f, 0f);
			maskRect.anchoredPosition = Vector2.zero;
			maskGo.AddComponent<RectMask2D>();

			var fillGo = new GameObject("Fill");
			fillGo.transform.SetParent(maskGo.transform, false);
			var fillRect = fillGo.AddComponent<RectTransform>();
			fillRect.anchorMin = new Vector2(0f, 0f);
			fillRect.anchorMax = new Vector2(0f, 0f);
			fillRect.pivot = new Vector2(0f, 0f);
			fillRect.anchoredPosition = Vector2.zero;
			ApplyTmpGlobalDefaultFont();
			var fillText = fillGo.AddComponent<TextMeshProUGUI>();
			fillText.richText = false;
			fillText.textWrappingMode = TextWrappingModes.NoWrap;
			if (_defaultFontAsset is not null)
			{
				fillText.font = _defaultFontAsset;
			}

			_karaokeTextPool.Add(new KaraokeSegmentView
			{
				Root = rootGo,
				RootRect = rootRect,
				BaseText = baseText,
				BaseRect = baseRect,
				FillText = fillText,
				FillRect = fillRect,
				MaskRect = maskRect
			});
		}

		return _karaokeTextPool[index];
	}

	private void SetKaraokeLabelActive(int index, bool active)
	{
		if (index < 0 || index >= _karaokeTextPool.Count)
		{
			return;
		}

		var go = _karaokeTextPool[index].Root;
		if (go is not null && go.activeSelf != active)
		{
			go.SetActive(active);
		}
	}

	private void HideKaraokeLayer()
	{
		if (_karaokeLayer is not null && _karaokeLayer.activeSelf)
		{
			_karaokeLayer.SetActive(false);
		}
	}

	private bool TryGetSegmentAnchor(int charStart, int charLength, out float anchorX, out float anchorY)
	{
		anchorX = 0f;
		anchorY = 0f;
		if (_tmpText is null || charLength <= 0)
		{
			return false;
		}

		var textInfo = _tmpText.textInfo;
		if (textInfo is null || textInfo.characterCount <= 0)
		{
			return false;
		}

		var chars = textInfo.characterInfo;
		if (chars is null || chars.Length == 0)
		{
			return false;
		}

		var clampedStart = Mathf.Clamp(charStart, 0, Math.Min(textInfo.characterCount, chars.Length) - 1);
		var startInfo = chars[clampedStart];
		anchorX = startInfo.bottomLeft.x;
		anchorY = startInfo.baseLine;

		// If the start glyph has unusual baseline metadata, derive a stable baseline from
		// other visible characters in the same segment.
		var max = Math.Min(clampedStart + charLength, Math.Min(textInfo.characterCount, chars.Length));
		var baselineFound = false;
		for (var i = clampedStart; i < max; i++)
		{
			var c = chars[i];
			if (!c.isVisible)
			{
				continue;
			}

			anchorY = c.baseLine;
			baselineFound = true;
			break;
		}

		return baselineFound || clampedStart >= 0;
	}

	private sealed class OverlayStyleState
	{
		public float FontSize { get; set; }
		public Color Color { get; set; }
		public Color OutlineColor { get; set; }
		public float OutlineWidth { get; set; }
		public bool Bold { get; set; }
		public bool Italic { get; set; }
		public int Alignment { get; set; }

		public static OverlayStyleState CreateDefault()
		{
			return new OverlayStyleState
			{
				FontSize = 40f,
				Color = Color.white,
				OutlineColor = Color.black,
				OutlineWidth = 2f,
				Bold = false,
				Italic = false,
				Alignment = 2
			};
		}
	}

	private sealed class OutlineState
	{
		public Color Color { get; set; }
		public float Width { get; set; }
	}

	private sealed class KaraokeSegmentView
	{
		public GameObject? Root { get; set; }
		public RectTransform RootRect { get; set; } = null!;
		public TextMeshProUGUI BaseText { get; set; } = null!;
		public RectTransform BaseRect { get; set; } = null!;
		public TextMeshProUGUI FillText { get; set; } = null!;
		public RectTransform FillRect { get; set; } = null!;
		public RectTransform MaskRect { get; set; } = null!;
	}
}
