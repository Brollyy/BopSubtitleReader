using System.Collections;
using BopSubtitleReader.Core;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(MixtapeLoaderCustom), "Start")]
public static class MixtapeLoaderCustomStartPatch
{
	private static readonly AccessTools.FieldRef<MixtapeLoaderCustom, int> TotalRef =
		AccessTools.FieldRefAccess<MixtapeLoaderCustom, int>("total");

	public static void Prefix(MixtapeLoaderCustom __instance, out MixtapeLoaderCustom __state)
	{
		__state = __instance;
	}

	public static IEnumerator Postfix(IEnumerator __result, MixtapeLoaderCustom __state)
	{
		if (__result is null)
		{
			yield break;
		}

		var prepared = false;
		while (__result.MoveNext())
		{
			if (!prepared && TotalRef(__state) > 0)
			{
				SubtitleCoordinator.Instance?.BindToLoader(__state);
				prepared = true;
			}

			yield return __result.Current;
		}
	}
}
