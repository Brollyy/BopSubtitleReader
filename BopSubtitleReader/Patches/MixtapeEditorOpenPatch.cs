using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(MixtapeEditorScript), "Open", [typeof(string)])]
public static class MixtapeEditorOpenPatch
{
	public static void Prefix()
	{
		SubtitleCoordinator.Instance?.Clear();
	}
}
