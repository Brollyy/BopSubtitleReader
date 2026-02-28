using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(MixtapeEditorScript), "SaveAsRiq", [typeof(string)])]
public static class MixtapeEditorSaveAsRiqPatch
{
	public static void Postfix(string path)
	{
		SubtitleAssetPreserver.Instance.ApplyToArchive(path);
	}
}
