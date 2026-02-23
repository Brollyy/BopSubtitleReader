using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace BopSubtitleReader.Core;

public static class LanguageResolver
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass(typeof(LanguageResolver));

	private static readonly string[] LegacyCandidateTypeNames =
	[
		"LocalizationManager",
		"TempoLocalizationManager",
		"GameLocalization",
		"LanguageManager"
	];

	private static readonly string[] CandidateMemberNames =
	[
		"CurrentLanguage",
		"Language",
		"CurrentLocale",
		"Locale",
		"currentLanguage",
		"language",
		"m_CurrentLanguage"
	];

	private static readonly Dictionary<string, string> LanguageAliasMap = new(StringComparer.OrdinalIgnoreCase)
	{
		["english"] = "en",
		["japanese"] = "ja",
		["spanish"] = "es",
		["french"] = "fr",
		["german"] = "de",
		["italian"] = "it",
		["portuguese"] = "pt",
		["brazilian"] = "pt-br",
		["korean"] = "ko",
		["russian"] = "ru",
		["chinese"] = "zh",
		["schinese"] = "zh-cn",
		["tchinese"] = "zh-tw"
	};

	public static string Resolve(SubtitleConfig config)
	{
		if (!string.IsNullOrWhiteSpace(config.PreferredLanguage.Value))
		{
			return Normalize(config.PreferredLanguage.Value);
		}

		if (!config.UseGameLanguage.Value)
		{
			return string.Empty;
		}

		var language = TryResolveFromGame();
		if (!string.IsNullOrWhiteSpace(language))
		{
			var normalized = Normalize(language);
			Log.Info($"Resolved game language '{normalized}'.");
			return normalized;
		}

		Log.Warn("Could not resolve game language; selection will rely on fallback rules.");
		return string.Empty;
	}

	private static string TryResolveFromGame()
	{
		if (TryInvokeStatic("Localisation", "GetLocale", out var localeObject) && localeObject is CultureInfo locale)
		{
			return locale.Name;
		}

		if (TryReadStaticMember("Localisation", "language", out var localisationLanguage))
		{
			return localisationLanguage;
		}

		if (TryInvokeStatic("SettingsScript", "GetRuntimeDefaultLanguage", out var runtimeLanguage)
			&& runtimeLanguage is not null)
		{
			return runtimeLanguage.ToString();
		}

		if (TryReadStaticMember("SteamManager", "Language", out var steamLanguage))
		{
			return steamLanguage;
		}

		if (TryReadStaticMember("UAP_AccessibilityManager", "m_CurrentLanguage", out var accessibilityLanguage))
		{
			return accessibilityLanguage;
		}

		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			foreach (var typeName in LegacyCandidateTypeNames)
			{
				var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
				if (type is null)
				{
					continue;
				}

				foreach (var memberName in CandidateMemberNames)
				{
					if (TryReadStaticMember(type, memberName, out var value))
					{
						return value;
					}
				}
			}
		}

		return string.Empty;
	}

	private static bool TryReadStaticMember(string typeName, string memberName, out string value)
	{
		value = string.Empty;
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
			if (type is null)
			{
				continue;
			}

			if (TryReadStaticMember(type, memberName, out value))
			{
				return true;
			}
		}

		return false;
	}

	private static bool TryReadStaticMember(Type type, string memberName, out string value)
	{
		value = string.Empty;

		var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		if (property is not null)
		{
			try
			{
				var propertyValue = property.GetValue(null);
				if (propertyValue is not null)
				{
					value = propertyValue.ToString();
					return !string.IsNullOrWhiteSpace(value);
				}
			}
			catch
			{
				// Getter may depend on game systems not initialized yet.
			}
		}

		var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		if (field is null)
		{
			return false;
		}

		object? fieldValue;
		try
		{
			fieldValue = field.GetValue(null);
		}
		catch
		{
			return false;
		}

		if (fieldValue is null)
		{
			return false;
		}

		value = fieldValue.ToString();
		return !string.IsNullOrWhiteSpace(value);
	}

	private static bool TryInvokeStatic(string typeName, string methodName, out object? value)
	{
		value = null;
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
			if (type is null)
			{
				continue;
			}

			var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			if (method is null || method.GetParameters().Length != 0)
			{
				continue;
			}

			try
			{
				value = method.Invoke(null, null);
				return value is not null;
			}
			catch
			{
				// Invocation can fail while localization/systems are still booting.
			}
		}

		return false;
	}

	public static SubtitleTrack? SelectTrack(SubtitleCatalog catalog, string language, SubtitleConfig config)
	{
		if (!string.IsNullOrWhiteSpace(language))
		{
			var normalized = Normalize(language);
			if (catalog.Tracks.TryGetValue(normalized, out var exact))
			{
				return exact;
			}

			var prefix = normalized.Split('-')[0];
			var prefixTrack = catalog.Tracks
				.FirstOrDefault(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				.Value;
			if (prefixTrack is not null)
			{
				return prefixTrack;
			}
		}

		if (config.FallbackToEnglish.Value && catalog.Tracks.TryGetValue("en", out var english))
		{
			return english;
		}

		if (config.FallbackToDefaultTrack.Value && catalog.Tracks.TryGetValue(catalog.DefaultLanguage, out var defaultTrack))
		{
			return defaultTrack;
		}

		return null;
	}

	public static string Normalize(string language)
	{
		var normalized = language.Trim().Replace('_', '-').ToLowerInvariant();
		if (LanguageAliasMap.TryGetValue(normalized, out var mapped))
		{
			return mapped;
		}

		return normalized;
	}
}
