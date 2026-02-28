using System;
using System.Collections.Generic;
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
	private TextMeshProUGUI? _tmpText;
	private RectTransform? _tmpRect;
	private GameObject? _karaokeLayer;
	private readonly List<KaraokeSegmentView> _karaokeTextPool = [];
	private TMP_FontAsset? _defaultFontAsset;
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
	/// Returns the viewport-space center X (x), top Y (y), and character height fraction (z)
	/// of the character range [charStart, charStart+charLength) within the currently rendered text,
	/// or null if the information is unavailable.
	/// </summary>
	public Vector3? GetSegmentViewportPosition(int charStart, int charLength)
	{
		if (!_available || _tmpText is null)
		{
			return null;
		}

		var textInfo = _tmpText.textInfo;
		if (textInfo is null)
		{
			return null;
		}

		var charCount = textInfo.characterCount;
		if (charCount == 0)
		{
			return null;
		}

		var charInfoArray = textInfo.characterInfo;
		if (charInfoArray is null)
		{
			return null;
		}

		var sumX = 0f;
		var maxScreenY = float.MinValue;
		var minScreenY = float.MaxValue;
		var visibleCount = 0;
		var charEnd = charStart + charLength;

		for (var i = charStart; i < Math.Min(charEnd, charCount); i++)
		{
			if (i >= charInfoArray.Length)
			{
				break;
			}

			var charInfo = charInfoArray[i];
			if (!charInfo.isVisible)
			{
				continue;
			}

			var localCenterX = (charInfo.bottomLeft.x + charInfo.topRight.x) * 0.5f;
			var localTopY = charInfo.topRight.y;
			var localBottomY = charInfo.bottomLeft.y;

			var worldTopPos = _tmpText.transform.TransformPoint(new Vector3(localCenterX, localTopY, 0f));
			var worldBottomPos = _tmpText.transform.TransformPoint(new Vector3(localCenterX, localBottomY, 0f));

			var screenTopPos = RectTransformUtility.WorldToScreenPoint(null, worldTopPos);
			var screenBottomPos = RectTransformUtility.WorldToScreenPoint(null, worldBottomPos);

			sumX += screenTopPos.x;
			if (screenTopPos.y > maxScreenY)
			{
				maxScreenY = screenTopPos.y;
			}

			if (screenBottomPos.y < minScreenY)
			{
				minScreenY = screenBottomPos.y;
			}

			visibleCount++;
		}

		if (visibleCount == 0)
		{
			return null;
		}

		var viewportX = (sumX / visibleCount) / Screen.width;
		var viewportTopY = maxScreenY / Screen.height;
		var charHeightFraction = (maxScreenY - minScreenY) / Screen.height;

		return new Vector3(viewportX, viewportTopY, charHeightFraction);
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

		var canvasObject = new GameObject("BOP_SubtitleCanvas");
		Object.DontDestroyOnLoad(canvasObject);

		var canvas = canvasObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 100;

		var textObject = new GameObject("BOP_SubtitleText");
		textObject.transform.SetParent(canvasObject.transform, false);

		var rect = textObject.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.08f, 0.02f);
		rect.anchorMax = new Vector2(0.92f, 0.20f);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;

		_tmpText = textObject.AddComponent<TextMeshProUGUI>();
		_tmpRect = rect;
		_tmpText.fontSize = 40f;
		_tmpText.textWrappingMode = TextWrappingModes.Normal;
		_tmpText.richText = true;
		_tmpText.alignment = TextAlignmentOptions.Bottom;

		AssignDefaultFontAsset();
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
		if (_tmpText is null)
		{
			return;
		}

		string[] primaryCandidates = ["NotInter-Regular SDF", "Arial SDF", "Kardia Fat Runner SDF"];
		TMP_FontAsset? primary = null;
		foreach (var name in primaryCandidates)
		{
			primary = LoadFontAsset(name);
			if (primary is not null)
			{
				Log.Info($"Primary subtitle font: {name}");
				break;
			}
		}

		if (primary is null)
		{
			Log.Warn("No primary TMP font asset found for subtitle overlay.");
			return;
		}

		string[] cjkFallbacks = ["MPLUSRounded1c-Regular SDF", "TaiwanPearl-Regular SDF", "Binggrae SDF"];
		var fallbackList = new List<TMP_FontAsset>();
		foreach (var name in cjkFallbacks)
		{
			var fallback = LoadFontAsset(name);
			if (fallback is not null && !ReferenceEquals(fallback, primary))
			{
				fallbackList.Add(fallback);
			}
		}

		if (fallbackList.Count > 0)
		{
			primary.fallbackFontAssetTable = fallbackList;
			Log.Info($"Configured {fallbackList.Count} CJK fallback font(s) for subtitle overlay.");
		}

		_tmpText.font = primary;
		_defaultFontAsset = primary;
	}

	private void ApplyFont(TextMeshProUGUI text, string? requestedFontName)
	{
		TMP_FontAsset? selected = null;
		if (!string.IsNullOrWhiteSpace(requestedFontName))
		{
			selected = LoadFontAsset(requestedFontName!);
			if (selected is null)
			{
				// Also try with " SDF" suffix, which is the TMP convention for SDF fonts.
				selected = LoadFontAsset($"{requestedFontName} SDF");
			}

			if (selected is null)
			{
				Log.Warn($"Requested subtitle font '{requestedFontName}' was not found. Using fallback.");
			}
		}

		selected ??= _defaultFontAsset;
		if (selected is null)
		{
			return;
		}

		text.font = selected;
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

	private static void ApplyOutline(TextMeshProUGUI text, Color outlineColor, float outlineWidth)
	{
		var mat = text.fontMaterial;
		if (mat is null)
		{
			return;
		}

		mat.EnableKeyword("OUTLINE_ON");
		mat.SetFloat("_OutlineWidth", outlineWidth);
		mat.SetColor("_OutlineColor", outlineColor);
		text.UpdateMeshPadding();
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
			var baseText = baseGo.AddComponent<TextMeshProUGUI>();
			baseText.richText = false;
			baseText.textWrappingMode = TextWrappingModes.NoWrap;

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
			var fillText = fillGo.AddComponent<TextMeshProUGUI>();
			fillText.richText = false;
			fillText.textWrappingMode = TextWrappingModes.NoWrap;

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
