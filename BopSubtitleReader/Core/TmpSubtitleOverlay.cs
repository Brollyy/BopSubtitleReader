using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BopSubtitleReader.Core;

public sealed class TmpSubtitleOverlay
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<TmpSubtitleOverlay>();

	private bool _initializationAttempted;
	private bool _available;
	private GameObject? _host;
	private TextMeshProUGUI? _tmpText;
	private TMP_FontAsset? _defaultFontAsset;
	private OverlayStyleState _defaultStyle = OverlayStyleState.CreateDefault();

	public void SetText(string text, SubtitleCueStyle? style)
	{
		if (!EnsureInitialized() || _tmpText is null || _host is null)
		{
			return;
		}

		ApplyStyle(style);
		_tmpText.text = text;
		// Force TMP to rebuild the mesh immediately so textInfo.characterInfo reflects the
		// current text and layout before GetSegmentViewportPosition queries it this frame.
		_tmpText.ForceMeshUpdate();
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
			Bold = false,
			Italic = false,
			Alignment = 2
		};
	}

	private void ApplyStyle(SubtitleCueStyle? style)
	{
		if (_tmpText is null)
		{
			return;
		}

		ApplyFont(style?.FontName);

		_tmpText.fontSize = style?.FontSize ?? _defaultStyle.FontSize;

		var color = _defaultStyle.Color;
		var colorHex = style?.ColorHexRgba;
		if (!string.IsNullOrWhiteSpace(colorHex)
		&& ColorUtility.TryParseHtmlString(colorHex, out var parsedColor))
		{
			color = parsedColor;
		}

		_tmpText.color = color;

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

		_tmpText.fontStyle = fontStyle;

		var alignment = style?.Alignment ?? _defaultStyle.Alignment;
		_tmpText.alignment = alignment switch
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

	private void ApplyFont(string? requestedFontName)
	{
		if (_tmpText is null)
		{
			return;
		}

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

		_tmpText.font = selected;
	}

	private sealed class OverlayStyleState
	{
		public float FontSize { get; set; }
		public Color Color { get; set; }
		public bool Bold { get; set; }
		public bool Italic { get; set; }
		public int Alignment { get; set; }

		public static OverlayStyleState CreateDefault()
		{
			return new OverlayStyleState
			{
				FontSize = 40f,
				Color = Color.white,
				Bold = false,
				Italic = false,
				Alignment = 2
			};
		}
	}
}
