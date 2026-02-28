using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(BopMixtapeSerializerV0), "ReadDirectory")]
public static class BopMixtapeSerializerReadDirectoryPatch
{
	public static void Postfix(string path)
	{
		SubtitleAssetPreserver.Instance.CaptureFromSource(path);
		SubtitleCoordinator.Instance?.PrepareFromBopDirectory(path);
	}
}
