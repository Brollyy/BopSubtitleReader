using System.Linq;
using HarmonyLib;

namespace BopSubtitleReader.Patches;

[HarmonyPatch(typeof(BopMixtapeSerializerV0), "WriteDirectory")]
public static class BopMixtapeSerializerWriteDirectoryPatch
{
	public static void Postfix(object[] __args)
	{
		var targetPath = __args?.OfType<string>().FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
		if (string.IsNullOrWhiteSpace(targetPath))
		{
			return;
		}

		Core.SubtitleAssetPreserver.Instance.ApplyToDirectory(targetPath!);
	}
}
