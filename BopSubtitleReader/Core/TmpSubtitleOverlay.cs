using System;
using System.Collections.Generic;
using System.Linq;
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
	private readonly Dictionary<string, TMP_FontAsset> _fontAssetByKey = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<TMP_FontAsset> _fontAssets = [];

	public void SetText(string text, SubtitleCueStyle? style)
	{
		if (!EnsureInitialized() || _tmpText is null || _host is null)
		{
			return;
		}

		ApplyStyle(style);
		_tmpText.text = text;
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

		BuildFontCatalog();
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

	private void BuildFontCatalog()
	{
		_fontAssetByKey.Clear();
		_fontAssets.Clear();

		var loadedAssets = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
		foreach (var asset in loadedAssets)
		{
			_fontAssets.Add(asset);
			RegisterFontAssetKey(asset.name, asset);
			var sourceFontName = asset.sourceFontFile?.name;
			if (!string.IsNullOrWhiteSpace(sourceFontName))
			{
				RegisterFontAssetKey(sourceFontName!, asset);
			}
		}
	}

	private void AssignDefaultFontAsset()
	{
		if (_tmpText is null)
		{
			return;
		}

		var selected = SelectFallbackFontAsset();
		if (selected is null)
		{
			Log.Warn("No TMP font asset found for subtitle overlay.");
			return;
		}

		_tmpText.font = selected;
		ConfigureFallbackFonts(selected);
		_defaultFontAsset = selected;
		Log.Info($"Assigned TMP font asset '{selected.name}' to subtitle overlay.");
	}

	private void ApplyFont(string? requestedFontName)
	{
		if (_tmpText is null)
		{
			return;
		}

		// Rebuild the catalog lazily if it was empty at init time (fonts may not be loaded yet).
		if (_fontAssets.Count == 0)
		{
			BuildFontCatalog();
			if (_fontAssets.Count > 0)
			{
				AssignDefaultFontAsset();
			}
		}

		TMP_FontAsset? selected = null;
		if (!string.IsNullOrWhiteSpace(requestedFontName))
		{
			selected = ResolveFontAsset(requestedFontName!);
			if (selected is null)
			{
				Log.Warn($"Requested subtitle font '{requestedFontName}' was not found. Using fallback.");
			}
		}

		selected ??= _defaultFontAsset ?? SelectFallbackFontAsset();
		if (selected is null)
		{
			return;
		}

		_tmpText.font = selected;
	}

	private TMP_FontAsset? SelectFallbackFontAsset()
	{
		string[] preferred =
		[
			"NotInter-Regular SDF",
			"NotInter-Regular",
			"Arial SDF",
			"Arial",
			"Kardia Fat Runner SDF",
			"MPLUSRounded1c-Regular SDF",
			"TaiwanPearl-Regular SDF",
			"Binggrae SDF"
		];

		foreach (var font in preferred)
		{
			var resolved = ResolveFontAsset(font);
			if (resolved is not null)
			{
				return resolved;
			}
		}

		return _fontAssets.Count > 0 ? _fontAssets[0] : null;
	}

	private void ConfigureFallbackFonts(TMP_FontAsset primaryFontAsset)
	{
		string[] cjkFallbacks = ["MPLUSRounded1c-Regular SDF", "TaiwanPearl-Regular SDF", "Binggrae SDF"];
		var fallbackList = new List<TMP_FontAsset>();

		foreach (var fontName in cjkFallbacks)
		{
			var fallback = ResolveFontAsset(fontName);
			if (fallback is null || ReferenceEquals(fallback, primaryFontAsset) || fallbackList.Contains(fallback))
			{
				continue;
			}

			fallbackList.Add(fallback);
		}

		if (fallbackList.Count > 0)
		{
			primaryFontAsset.fallbackFontAssetTable = fallbackList;
			Log.Info($"Configured {fallbackList.Count} TMP fallback font(s) for subtitle overlay.");
		}
	}

	private TMP_FontAsset? ResolveFontAsset(string requestedFontName)
	{
		var normalized = NormalizeFontKey(requestedFontName);
		if (_fontAssetByKey.TryGetValue(normalized, out var exact))
		{
			return exact;
		}

		if (_fontAssets.Count == 0)
		{
			return null;
		}

		foreach (var candidate in _fontAssets)
		{
			var candidateKey = NormalizeFontKey(candidate.name);
			if (candidateKey.Contains(normalized) || normalized.Contains(candidateKey))
			{
				return candidate;
			}

			var sourceFontName = candidate.sourceFontFile?.name;
			if (!string.IsNullOrWhiteSpace(sourceFontName))
			{
				var sourceKey = NormalizeFontKey(sourceFontName!);
				if (sourceKey.Contains(normalized) || normalized.Contains(sourceKey))
				{
					return candidate;
				}
			}
		}

		return null;
	}

	private void RegisterFontAssetKey(string key, TMP_FontAsset asset)
	{
		var normalized = NormalizeFontKey(key);
		if (normalized.Length == 0 || _fontAssetByKey.ContainsKey(normalized))
		{
			return;
		}

		Log.Trace($"Registering font asset key: {key}");
		_fontAssetByKey[normalized] = asset;
	}

	private static string NormalizeFontKey(string key)
	{
		return new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
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
