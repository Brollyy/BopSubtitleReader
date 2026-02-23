using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(RiqLoader), "Load")]
public static class RiqLoaderLoadPatch
{
	public static void Prefix(string path)
	{
		SubtitleCoordinator.Instance?.PrepareFromRiqArchive(path);
	}
}
