using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BopSubtitleReader.Core;

public sealed class TmpSubtitleOverlay
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<TmpSubtitleOverlay>();

	private bool _initializationAttempted;
	private bool _available;
	private GameObject? _host;
	private Component? _tmpComponent;
	private PropertyInfo? _textProperty;
	private PropertyInfo? _fontSizeProperty;
	private PropertyInfo? _fontProperty;
	private Type? _fontAssetType;
	private PropertyInfo? _fontAssetSourceFileProperty;
	private PropertyInfo? _fallbackFontAssetTableProperty;
	private PropertyInfo? _colorProperty;
	private PropertyInfo? _fontStyleProperty;
	private PropertyInfo? _alignmentProperty;
	private PropertyInfo? _horizontalAlignmentProperty;
	private PropertyInfo? _verticalAlignmentProperty;
	private OverlayStyleState _defaultStyle = OverlayStyleState.CreateDefault();
	private readonly Dictionary<string, UnityEngine.Object> _fontAssetByKey = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<UnityEngine.Object> _fontAssets = [];
	private UnityEngine.Object? _defaultFontAsset;

	public void SetText(string text, SubtitleCueStyle? style)
	{
		if (!EnsureInitialized())
		{
			return;
		}

		if (_tmpComponent is null || _textProperty is null || _host is null)
		{
			return;
		}

		ApplyStyle(style);
		_textProperty.SetValue(_tmpComponent, text);
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
		var tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI")
					  ?? AccessTools.TypeByName("TMPro.TMP_Text");
		if (tmpType is null)
		{
			Log.Warn("TMP type not found. Subtitle overlay disabled.");
			return false;
		}

		_textProperty = tmpType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
		_fontSizeProperty = tmpType.GetProperty("fontSize", BindingFlags.Public | BindingFlags.Instance);
		_fontProperty = tmpType.GetProperty("font", BindingFlags.Public | BindingFlags.Instance);
		_fontAssetType = _fontProperty?.PropertyType;
		_fontAssetSourceFileProperty = _fontAssetType?.GetProperty("sourceFontFile", BindingFlags.Public | BindingFlags.Instance);
		_fallbackFontAssetTableProperty = _fontAssetType?.GetProperty("fallbackFontAssetTable", BindingFlags.Public | BindingFlags.Instance);
		_colorProperty = tmpType.GetProperty("color", BindingFlags.Public | BindingFlags.Instance);
		_fontStyleProperty = tmpType.GetProperty("fontStyle", BindingFlags.Public | BindingFlags.Instance);
		_alignmentProperty = tmpType.GetProperty("alignment", BindingFlags.Public | BindingFlags.Instance);
		_horizontalAlignmentProperty = tmpType.GetProperty("horizontalAlignment", BindingFlags.Public | BindingFlags.Instance);
		_verticalAlignmentProperty = tmpType.GetProperty("verticalAlignment", BindingFlags.Public | BindingFlags.Instance);
		if (_textProperty is null)
		{
			Log.Warn("TMP text property not found. Subtitle overlay disabled.");
			return false;
		}

		var canvasType = AccessTools.TypeByName("UnityEngine.Canvas");
		if (canvasType is null)
		{
			Log.Warn("Unity Canvas type not found. Subtitle overlay disabled.");
			return false;
		}

		var canvasObject = new GameObject("BOP_SubtitleCanvas");
		UnityEngine.Object.DontDestroyOnLoad(canvasObject);

		var canvas = canvasObject.AddComponent(canvasType);
		SetEnumProperty(canvasType, canvas, "renderMode", "ScreenSpaceOverlay");
		SetProperty(canvasType, canvas, "sortingOrder", 32000);

		var textObject = new GameObject("BOP_SubtitleText");
		textObject.transform.SetParent(canvasObject.transform, false);

		var rect = textObject.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.08f, 0.02f);
		rect.anchorMax = new Vector2(0.92f, 0.20f);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;

		_tmpComponent = textObject.AddComponent(tmpType);
		ConfigureTmpComponent(_tmpComponent, tmpType);
		BuildFontCatalog();
		AssignDefaultFontAsset(_tmpComponent);
		CaptureDefaultStyle();

		_host = canvasObject;
		_available = true;
		_host.SetActive(false);
		Log.Info("TMP subtitle overlay initialized.");
		return true;
	}

	private void CaptureDefaultStyle()
	{
		if (_tmpComponent is null)
		{
			return;
		}

		_defaultStyle = new OverlayStyleState
		{
			FontSize = ReadFloat(_fontSizeProperty, _tmpComponent) ?? 40f,
			Color = ReadColor(_colorProperty, _tmpComponent) ?? Color.white,
			Bold = false,
			Italic = false,
			Alignment = 2
		};
	}

	private void ApplyStyle(SubtitleCueStyle? style)
	{
		if (_tmpComponent is null)
		{
			return;
		}

		ApplyFont(style?.FontName);

		var fontSize = style?.FontSize ?? _defaultStyle.FontSize;
		SetProperty(_fontSizeProperty, _tmpComponent, fontSize);

		var color = _defaultStyle.Color;
		var colorHex = style?.ColorHexRgba;
		if (!string.IsNullOrWhiteSpace(colorHex)
			&& ColorUtility.TryParseHtmlString(colorHex, out var parsedColor))
		{
			color = parsedColor;
		}
		SetProperty(_colorProperty, _tmpComponent, color);

		var bold = style?.Bold ?? _defaultStyle.Bold;
		var italic = style?.Italic ?? _defaultStyle.Italic;
		SetTmpFontStyle(bold, italic);

		var alignment = style?.Alignment ?? _defaultStyle.Alignment;
		SetTmpAlignment(alignment);
	}

	private void SetTmpFontStyle(bool bold, bool italic)
	{
		if (_tmpComponent is null || _fontStyleProperty is null || !_fontStyleProperty.CanWrite || !_fontStyleProperty.PropertyType.IsEnum)
		{
			return;
		}

		var flags = 0;
		if (bold && TryGetEnumIntValue(_fontStyleProperty.PropertyType, "Bold", out var boldValue))
		{
			flags |= boldValue;
		}

		if (italic && TryGetEnumIntValue(_fontStyleProperty.PropertyType, "Italic", out var italicValue))
		{
			flags |= italicValue;
		}

		if (flags == 0 && TryGetEnumValue(_fontStyleProperty.PropertyType, "Normal", out var normal))
		{
			_fontStyleProperty.SetValue(_tmpComponent, normal);
			return;
		}

		var enumValue = Enum.ToObject(_fontStyleProperty.PropertyType, flags);
		_fontStyleProperty.SetValue(_tmpComponent, enumValue);
	}

	private void SetTmpAlignment(int assAlignment)
	{
		if (_tmpComponent is null)
		{
			return;
		}

		var anchorName = assAlignment switch
		{
			1 => "BottomLeft",
			2 => "Bottom",
			3 => "BottomRight",
			4 => "Left",
			5 => "Center",
			6 => "Right",
			7 => "TopLeft",
			8 => "Top",
			9 => "TopRight",
			_ => "Bottom"
		};

		if (!SetEnumPropertyValue(_alignmentProperty, _tmpComponent, anchorName))
		{
			SetHorizontalVerticalFallback(assAlignment);
		}
	}

	private void SetHorizontalVerticalFallback(int assAlignment)
	{
		var horizontal = (assAlignment % 3) switch
		{
			1 => "Left",
			2 => "Center",
			0 => "Right",
			_ => "Center"
		};

		var vertical = assAlignment switch
		{
			>= 1 and <= 3 => "Bottom",
			>= 4 and <= 6 => "Middle",
			>= 7 and <= 9 => "Top",
			_ => "Bottom"
		};

		if (_tmpComponent is null)
		{
			return;
		}

		SetEnumPropertyValue(_horizontalAlignmentProperty, _tmpComponent, horizontal);
		SetEnumPropertyValue(_verticalAlignmentProperty, _tmpComponent, vertical);
	}

	private static void ConfigureTmpComponent(Component component, Type type)
	{
		SetProperty(type, component, "fontSize", 40f);
		SetProperty(type, component, "enableWordWrapping", true);
		SetProperty(type, component, "richText", true);
		SetEnumProperty(type, component, "alignment", "Bottom");
		SetEnumProperty(type, component, "horizontalAlignment", "Center");
		SetEnumProperty(type, component, "verticalAlignment", "Bottom");
	}

	private void BuildFontCatalog()
	{
		_fontAssetByKey.Clear();
		_fontAssets.Clear();
		if (_fontAssetType is null)
		{
			return;
		}

		UnityEngine.Object[] loadedAssets = Resources.FindObjectsOfTypeAll(_fontAssetType);
		for (var i = 0; i < loadedAssets.Length; i++)
		{
			UnityEngine.Object asset = loadedAssets[i];
			if (asset is null)
			{
				continue;
			}

			_fontAssets.Add(asset);
			RegisterFontAssetKey(asset.name, asset);
			string? sourceFontName = ReadSourceFontName(asset);
			if (!string.IsNullOrWhiteSpace(sourceFontName))
			{
				RegisterFontAssetKey(sourceFontName!, asset);
			}
		}
	}

	private void AssignDefaultFontAsset(Component component)
	{
		if (_fontProperty is null || !_fontProperty.CanWrite)
		{
			return;
		}

		UnityEngine.Object? selected = SelectFallbackFontAsset();
		if (selected is null)
		{
			Log.Warn("No TMP font asset found for subtitle overlay.");
			return;
		}

		_fontProperty.SetValue(component, selected);
		ConfigureFallbackFonts(selected);
		_defaultFontAsset = selected;
		Log.Info($"Assigned TMP font asset '{selected.name}' to subtitle overlay.");
	}

	private void ApplyFont(string? requestedFontName)
	{
		if (_tmpComponent is null || _fontProperty is null || !_fontProperty.CanWrite)
		{
			return;
		}

		UnityEngine.Object? selected = null;
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

		_fontProperty.SetValue(_tmpComponent, selected);
	}

	private UnityEngine.Object? SelectFallbackFontAsset()
	{
		string[] preferred = [
			"NotInter-Regular SDF",
			"NotInter-Regular",
			"Arial SDF",
			"Arial",
			"Kardia Fat Runner SDF",
			"MPLUSRounded1c-Regular SDF",
			"TaiwanPearl-Regular SDF",
			"Binggrae SDF"
		];

		for (var i = 0; i < preferred.Length; i++)
		{
			UnityEngine.Object? resolved = ResolveFontAsset(preferred[i]);
			if (resolved is not null)
			{
				return resolved;
			}
		}

		return _fontAssets.Count > 0 ? _fontAssets[0] : null;
	}

	private void ConfigureFallbackFonts(UnityEngine.Object primaryFontAsset)
	{
		if (_fontAssetType is null || _fallbackFontAssetTableProperty is null || !_fallbackFontAssetTableProperty.CanWrite)
		{
			return;
		}

		string[] cjkFallbacks = ["MPLUSRounded1c-Regular SDF", "TaiwanPearl-Regular SDF", "Binggrae SDF"];
		IList fallbackList = (IList)(Activator.CreateInstance(typeof(List<>).MakeGenericType(_fontAssetType))
			?? throw new InvalidOperationException("Failed to create TMP fallback list."));

		for (var i = 0; i < cjkFallbacks.Length; i++)
		{
			UnityEngine.Object? fallback = ResolveFontAsset(cjkFallbacks[i]);
			if (fallback is null || ReferenceEquals(fallback, primaryFontAsset) || fallbackList.Contains(fallback))
			{
				continue;
			}

			fallbackList.Add(fallback);
		}

		if (fallbackList.Count > 0)
		{
			_fallbackFontAssetTableProperty.SetValue(primaryFontAsset, fallbackList);
			Log.Info($"Configured {fallbackList.Count} TMP fallback font(s) for subtitle overlay.");
		}
	}

	private UnityEngine.Object? ResolveFontAsset(string requestedFontName)
	{
		string normalized = NormalizeFontKey(requestedFontName);
		if (_fontAssetByKey.TryGetValue(normalized, out UnityEngine.Object? exact))
		{
			return exact;
		}

		if (_fontAssets.Count == 0)
		{
			return null;
		}

		for (var i = 0; i < _fontAssets.Count; i++)
		{
			UnityEngine.Object candidate = _fontAssets[i];
			string candidateKey = NormalizeFontKey(candidate.name);
			if (candidateKey.Contains(normalized) || normalized.Contains(candidateKey))
			{
				return candidate;
			}

			string? sourceFontName = ReadSourceFontName(candidate);
			if (!string.IsNullOrWhiteSpace(sourceFontName))
			{
				string sourceKey = NormalizeFontKey(sourceFontName!);
				if (sourceKey.Contains(normalized) || normalized.Contains(sourceKey))
				{
					return candidate;
				}
			}
		}

		return null;
	}

	private string? ReadSourceFontName(UnityEngine.Object fontAsset)
	{
		if (_fontAssetSourceFileProperty is null)
		{
			return null;
		}

		object? source = _fontAssetSourceFileProperty.GetValue(fontAsset);
		return source is UnityEngine.Object sourceObject ? sourceObject.name : null;
	}

	private void RegisterFontAssetKey(string key, UnityEngine.Object asset)
	{
		string normalized = NormalizeFontKey(key);
		if (normalized.Length == 0 || _fontAssetByKey.ContainsKey(normalized))
		{
			return;
		}

		_fontAssetByKey[normalized] = asset;
	}

	private static string NormalizeFontKey(string key)
	{
		return new string(key.Where(ch => char.IsLetterOrDigit(ch)).ToArray()).ToLowerInvariant();
	}

	private static float? ReadFloat(PropertyInfo? property, object target)
	{
		if (property is null || !property.CanRead)
		{
			return null;
		}

		var value = property.GetValue(target);
		if (value is float number)
		{
			return number;
		}

		return null;
	}

	private static Color? ReadColor(PropertyInfo? property, object target)
	{
		if (property is null || !property.CanRead)
		{
			return null;
		}

		var value = property.GetValue(target);
		if (value is Color color)
		{
			return color;
		}

		if (value is Color32 color32)
		{
			return color32;
		}

		return null;
	}

	private static bool SetEnumPropertyValue(PropertyInfo? property, object target, string enumValue)
	{
		if (property is null || !property.CanWrite || !property.PropertyType.IsEnum)
		{
			return false;
		}

		if (TryGetEnumValue(property.PropertyType, enumValue, out var value))
		{
			property.SetValue(target, value);
			return true;
		}

		return false;
	}

	private static bool TryGetEnumValue(Type enumType, string name, out object? value)
	{
		var values = Enum.GetValues(enumType);
		foreach (var item in values)
		{
			if (string.Equals(item.ToString(), name, StringComparison.OrdinalIgnoreCase))
			{
				value = item;
				return true;
			}
		}

		value = null;
		return false;
	}

	private static bool TryGetEnumIntValue(Type enumType, string name, out int value)
	{
		value = 0;
		if (!TryGetEnumValue(enumType, name, out var enumValue) || enumValue is null)
		{
			return false;
		}

		value = Convert.ToInt32(enumValue, System.Globalization.CultureInfo.InvariantCulture);
		return true;
	}

	private static void SetProperty(PropertyInfo? property, object target, object value)
	{
		if (property is null || !property.CanWrite)
		{
			return;
		}

		property.SetValue(target, value);
	}

	private static void SetProperty(Type type, object target, string propertyName, object value)
	{
		var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
		SetProperty(property, target, value);
	}

	private static void SetEnumProperty(Type type, object target, string propertyName, string enumValue)
	{
		var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
		SetEnumPropertyValue(property, target, enumValue);
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
