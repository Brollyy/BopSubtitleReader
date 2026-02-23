using System.Collections.Generic;
using System.Reflection;
using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch]
public static class MixtapeEditorPlaybackPatch
{
	private static readonly ClassLogger Log = ClassLogger.GetForClass(typeof(MixtapeEditorPlaybackPatch));

	public static IEnumerable<MethodBase> TargetMethods()
	{
		MethodBase? playOrStop = AccessTools.Method(typeof(MixtapeEditorScript), "PlayOrStopMixtape");
		if (playOrStop is not null)
		{
			yield return playOrStop;
		}
	}

	public static void Postfix(MethodBase __originalMethod)
	{
		if (TempoSceneManager.GetActiveSceneKey().ToString() != "MixtapeEditor")
		{
			return;
		}

		string methodName = __originalMethod.Name;
		if (methodName == "PlayOrStopMixtape" && !SubtitleRuntimeController.Instance.HasActiveSession)
		{
			// Start path: no active session yet, let loader startup patches prepare/bind subtitles.
			return;
		}

		Log.Trace($"Clearing subtitles after editor playback control: {methodName}.");
		SubtitleCoordinator.Instance?.Clear();
	}
}
