using BopSubtitleReader.Parser;

namespace BopSubtitleReader.Core;

public sealed class SubtitleCoordinator(SubtitleConfig config)
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass<SubtitleCoordinator>();

	public static SubtitleCoordinator? Instance { get; private set; }

	private readonly KaraokeIndicatorAdapter _karaokeAdapter = new();
	private readonly SubtitleParserStrategy _parserStrategy = new();
	private SubtitleTrack? _pendingTrack;

	public static SubtitleCoordinator CreateAndSet(SubtitleConfig config)
	{
		var coordinator = new SubtitleCoordinator(config);
		Instance = coordinator;
		return coordinator;
	}

	public void PrepareFromBopDirectory(string directoryPath)
	{
		PrepareFromSource(directoryPath);
	}

	public void PrepareFromRiqArchive(string archivePath)
	{
		PrepareFromSource(archivePath);
	}

	public void BindToLoader(MixtapeLoaderCustom loader)
	{
		if (!config.Enabled.Value)
		{
			SubtitleRuntimeController.Instance.StopSession();
			return;
		}

		if (_pendingTrack is null)
		{
			Log.Trace("No pending subtitle track to bind to loader.");
			SubtitleRuntimeController.Instance.StopSession();
			return;
		}

		Log.Info($"Binding subtitle track '{_pendingTrack.Language}' to active loader.");
		SubtitleRuntimeController.Instance.StartSession(loader, _pendingTrack, config, _karaokeAdapter);
	}

	private void PrepareFromSource(string sourcePath)
	{
		_pendingTrack = null;

		if (!config.Enabled.Value)
		{
			SubtitleRuntimeController.Instance.StopSession();
			return;
		}

		if (!SubtitleFileLocator.TryLoadCatalog(sourcePath, _parserStrategy, out var catalog))
		{
			Log.Warn($"No subtitle files found in chart source '{sourcePath}'.");
			SubtitleRuntimeController.Instance.StopSession();
			return;
		}

		var language = LanguageResolver.Resolve(config);
		var selectedTrack = LanguageResolver.SelectTrack(catalog, language, config);
		if (selectedTrack is null)
		{
			Log.Warn("No subtitle track selected after language resolution and fallbacks.");
			SubtitleRuntimeController.Instance.StopSession();
			return;
		}

		Log.Info($"Selected subtitle track '{selectedTrack.Language}' from {selectedTrack.Source}.");
		_pendingTrack = selectedTrack;
	}

	public void Clear()
	{
		Log.Trace("Clearing active subtitle session.");
		_pendingTrack = null;
		SubtitleRuntimeController.Instance.StopSession();
	}
}
