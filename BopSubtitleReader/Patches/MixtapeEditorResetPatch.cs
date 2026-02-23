using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(MixtapeEditorScript), "ResetAllAndReformat")]
public static class MixtapeEditorResetPatch
{
	public static void Postfix()
	{
		SubtitleCoordinator.Instance?.Clear();
	}
}
