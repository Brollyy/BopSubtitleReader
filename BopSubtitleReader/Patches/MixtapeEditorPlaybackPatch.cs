using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(MixtapeEditorScript), "PlayOrStopMixtape")]
public static class MixtapeEditorPlaybackPatch
{
	public static void Postfix()
	{
		SubtitleRuntimeController.Instance.StopSession();
	}
}
