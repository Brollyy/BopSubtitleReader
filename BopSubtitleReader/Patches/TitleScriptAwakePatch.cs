using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(TitleScript), "Awake")]
public static class TitleScriptAwakePatch
{
	public static void Postfix()
	{
		SubtitleCoordinator.Instance?.Clear();
	}
}
