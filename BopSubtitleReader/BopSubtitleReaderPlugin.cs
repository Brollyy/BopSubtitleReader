using System.Diagnostics.CodeAnalysis;
using BepInEx;
using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader;

/// <summary>
/// Main BepInEx entrypoint for the Bop Subtitle Reader mod.
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class BopSubtitleReaderPlugin : BaseUnityPlugin
{
	private ClassLogger? _log;

	[SuppressMessage(
		"Style",
		"IDE0051:Remove unused private members",
		Justification = "Unity message method invoked by engine.")]
	private void Awake()
	{
		ClassLogger.Initialize(Config, MyPluginInfo.PLUGIN_GUID);
		_log = ClassLogger.GetForClass<BopSubtitleReaderPlugin>();
		var subtitleConfig = new SubtitleConfig(Config);
		SubtitleCoordinator.CreateAndSet(subtitleConfig);

		var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
		harmony.PatchAll();
		_log.Info("Subtitle coordinator initialized.");
		Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded.");
	}
}
