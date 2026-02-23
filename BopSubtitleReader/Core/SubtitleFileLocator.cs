using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using BopSubtitleReader.Parser;

namespace BopSubtitleReader.Core;

public static class SubtitleFileLocator
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass(typeof(SubtitleFileLocator));

	private static readonly Regex SubtitleFileRegex = new(
		@"(?:^|/)(?:subtitles?|lyrics)(?:\.([a-zA-Z-]+))?\.(json|srt|ass|ssa)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public static bool TryLoadCatalog(
		string sourcePath,
		SubtitleParserStrategy parserStrategy,
		out SubtitleCatalog catalog)
	{
		catalog = new SubtitleCatalog();
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			return false;
		}

		Log.Info($"Scanning chart source '{sourcePath}' for subtitles.");
		foreach (var asset in EnumerateSubtitleFiles(sourcePath))
		{
			if (!parserStrategy.TryParse(asset, out var parsedCatalog))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(parsedCatalog.DefaultLanguage))
			{
				catalog.DefaultLanguage = parsedCatalog.DefaultLanguage;
			}

			foreach (var pair in parsedCatalog.Tracks)
			{
				catalog.AddOrMerge(pair.Value);
			}
		}

		Log.Info($"Loaded subtitle tracks: {string.Join(", ", catalog.Tracks.Keys.OrderBy(x => x))}");
		return catalog.Tracks.Count > 0;
	}

	private static IEnumerable<SubtitleSourceAsset> EnumerateSubtitleFiles(string sourcePath)
	{
		if (Directory.Exists(sourcePath))
		{
			foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories))
			{
				var subtitleFile = TryReadDiskFile(filePath);
				if (subtitleFile is not null)
				{
					yield return subtitleFile;
				}
			}

			yield break;
		}

		if (!File.Exists(sourcePath))
		{
			yield break;
		}

		using var archive = TryOpenArchive(sourcePath);
		if (archive is null)
		{
			yield break;
		}

		foreach (var entry in archive.Entries)
		{
			var match = SubtitleFileRegex.Match(entry.FullName.Replace('\\', '/'));
			if (!match.Success)
			{
				continue;
			}

			using var stream = entry.Open();
			using var reader = new StreamReader(stream);
			var content = reader.ReadToEnd();
			yield return new SubtitleSourceAsset(
				$"{sourcePath}:{entry.FullName}",
				content,
				"." + match.Groups[2].Value.ToLowerInvariant(),
				LanguageResolver.Normalize(match.Groups[1].Value));
		}
	}

	private static SubtitleSourceAsset? TryReadDiskFile(string filePath)
	{
		var match = SubtitleFileRegex.Match(filePath.Replace('\\', '/'));
		if (!match.Success)
		{
			return null;
		}

		try
		{
			return new SubtitleSourceAsset(
				filePath,
				File.ReadAllText(filePath),
				"." + match.Groups[2].Value.ToLowerInvariant(),
				LanguageResolver.Normalize(match.Groups[1].Value));
		}
		catch (Exception ex)
		{
			Log.Warn($"Could not read subtitle file '{filePath}': {ex.Message}");
			return null;
		}
	}

	private static ZipArchive? TryOpenArchive(string sourcePath)
	{
		try
		{
			return ZipFile.OpenRead(sourcePath);
		}
		catch (Exception ex)
		{
			Log.Warn($"Unable to inspect archive '{sourcePath}' as zip: {ex.Message}");
			return null;
		}
	}
}
