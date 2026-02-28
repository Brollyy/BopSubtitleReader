using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BopSubtitleReader.Core;

public sealed class SubtitleAssetPreserver
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<SubtitleAssetPreserver>();
	private static readonly Regex SubtitleFileRegex = new(
		@"^(?:subtitles?|lyrics)(?:\.([a-zA-Z-]+))?\.(json|srt|ass|ssa)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public static SubtitleAssetPreserver Instance { get; } = new();

	private readonly Dictionary<string, byte[]> _preservedFiles = new(StringComparer.OrdinalIgnoreCase);

	public void CaptureFromSource(string sourcePath)
	{
		Clear();
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			return;
		}

		try
		{
			if (Directory.Exists(sourcePath))
			{
				CaptureFromDirectory(sourcePath);
			}
			else if (File.Exists(sourcePath))
			{
				CaptureFromArchive(sourcePath);
			}
			else
			{
				Log.Trace($"Source path does not exist for subtitle capture: '{sourcePath}'.");
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to capture subtitle files from '{sourcePath}': {ex.Message}");
		}
	}

	public void ApplyToDirectory(string directoryPath)
	{
		if (_preservedFiles.Count == 0 || string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return;
		}

		var restored = 0;
		foreach (var file in _preservedFiles)
		{
			var targetPath = Path.Combine(directoryPath, file.Key.Replace('/', Path.DirectorySeparatorChar));
			var targetDir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrWhiteSpace(targetDir))
			{
				Directory.CreateDirectory(targetDir);
			}

			File.WriteAllBytes(targetPath, file.Value);
			restored++;
		}

		if (restored > 0)
		{
			Log.Info($"Restored {restored} subtitle file(s) into '{directoryPath}'.");
		}
	}

	public void ApplyToArchive(string archivePath)
	{
		if (_preservedFiles.Count == 0 || string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
		{
			return;
		}

		var restored = 0;
		using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
		foreach (var file in _preservedFiles)
		{
			archive.GetEntry(file.Key)?.Delete();
			var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
			using var stream = entry.Open();
			stream.Write(file.Value, 0, file.Value.Length);
			restored++;
		}

		if (restored > 0)
		{
			Log.Info($"Restored {restored} subtitle file(s) into archive '{archivePath}'.");
		}
	}

	public void Clear()
	{
		_preservedFiles.Clear();
	}

	private void CaptureFromDirectory(string sourcePath)
	{
		var root = Path.GetFullPath(sourcePath);
		foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
		{
			var fileName = Path.GetFileName(filePath);
			if (!IsSubtitlePath(fileName))
			{
				continue;
			}

			_preservedFiles[fileName] = File.ReadAllBytes(filePath);
		}

		if (_preservedFiles.Count > 0)
		{
			Log.Info($"Captured {_preservedFiles.Count} subtitle file(s) from directory source.");
		}
	}

	private void CaptureFromArchive(string archivePath)
	{
		using var archive = ZipFile.OpenRead(archivePath);
		foreach (var entry in archive.Entries)
		{
			var fileName = Path.GetFileName(entry.FullName.Replace('\\', '/'));
			if (!IsSubtitlePath(fileName))
			{
				continue;
			}

			using var entryStream = entry.Open();
			using var memory = new MemoryStream();
			entryStream.CopyTo(memory);
			_preservedFiles[fileName] = memory.ToArray();
		}

		if (_preservedFiles.Count > 0)
		{
			Log.Info($"Captured {_preservedFiles.Count} subtitle file(s) from archive source.");
		}
	}

	private static bool IsSubtitlePath(string path)
	{
		return SubtitleFileRegex.IsMatch(path);
	}
}
