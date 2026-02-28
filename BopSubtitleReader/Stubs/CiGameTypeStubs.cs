#if SKIP_GAME_REFERENCES
using System.Collections.Generic;
using UnityEngine;

// CI-only stubs for game-managed types that would normally come from Assembly-CSharp.
#pragma warning disable CA1050
#pragma warning disable CA1051
#pragma warning disable CA1822

public enum SceneKey
{
	Unknown = 0,
	MixtapeEditor = 1
}

public sealed class SubtitleLineData
{
	public string key = string.Empty;
	public string text = string.Empty;
	public float startBeat;
	public float endBeat;
}

public sealed class MixtapeLoaderCustom : MonoBehaviour
{
	public int total;
	public JukeboxScript? jukebox;
	public SceneKey[] sceneKeys = [SceneKey.MixtapeEditor];
	public Dictionary<SceneKey, GameplayScript> scripts = [];
}

public sealed class GameplayScript : MonoBehaviour
{
	public MonoBehaviour? cameraScript;
}

public sealed class JukeboxScript : MonoBehaviour
{
	public float CurrentBeat { get; set; }

	public float SecondsToBeats(double seconds)
	{
		return (float)seconds;
	}
}

public sealed class MixtapeEditorScript
{
	public void Open(string path)
	{
	}

	public void ResetAllAndReformat()
	{
	}

	public void PlayOrStopMixtape()
	{
	}

	public void PauseOrUnpauseMixtape()
	{
	}

	public void FilePause()
	{
	}
}

public sealed class TitleScript
{
	public void Awake()
	{
	}
}

public static class BopMixtapeSerializerV0
{
	public static void ReadDirectory(string path)
	{
	}
}

public static class RiqLoader
{
	public static void Load(string path)
	{
	}
}

public static class TempoSceneManager
{
	public static SceneKey GetActiveSceneKey()
	{
		return SceneKey.Unknown;
	}
}

#pragma warning restore CA1051
#pragma warning restore CA1050
#pragma warning restore CA1822

namespace TMPro
{
#pragma warning disable CA1050
#pragma warning disable CA1051
#pragma warning disable CA1707
#pragma warning disable CA1822

	[System.Flags]
	public enum FontStyles
	{
		Normal = 0,
		Bold = 1,
		Italic = 2,
	}

	public enum TextAlignmentOptions
	{
		TopLeft = 257,
		Top = 258,
		TopRight = 260,
		Left = 513,
		Center = 514,
		Right = 516,
		BottomLeft = 1025,
		Bottom = 1026,
		BottomRight = 1028,
	}

	public class TMP_FontAsset : UnityEngine.ScriptableObject
	{
		public UnityEngine.Font? sourceFontFile { get; set; }
		public System.Collections.Generic.List<TMP_FontAsset>? fallbackFontAssetTable { get; set; }

		public static TMP_FontAsset? CreateFontAsset(UnityEngine.Font sourceFontFile)
		{
			if (sourceFontFile == null)
			{
				return null;
			}

			return new TMP_FontAsset
			{
				sourceFontFile = sourceFontFile
			};
		}
	}

	public static class TMP_Settings
	{
		public static object? instance { get; } = new object();
		public static TMP_FontAsset? defaultFontAsset { get; set; }
	}

	public struct TMP_CharacterInfo
	{
		public UnityEngine.Vector3 bottomLeft;
		public UnityEngine.Vector3 topRight;
		public float baseLine;
		public bool isVisible;
	}

	public class TMP_TextInfo
	{
		public int characterCount;
		public TMP_CharacterInfo[] characterInfo = [];
	}

	public class TextMeshProUGUI : UnityEngine.MonoBehaviour
	{
		public string text { get; set; } = string.Empty;
		public float fontSize { get; set; }
		public TMP_FontAsset? font { get; set; }
		public UnityEngine.Material? fontMaterial { get; set; } = new UnityEngine.Material(UnityEngine.Shader.Find("UI/Default"));
		public UnityEngine.Color color { get; set; }
		public FontStyles fontStyle { get; set; }
		public TextAlignmentOptions alignment { get; set; }
		public bool enableWordWrapping { get; set; }
		public TextWrappingModes textWrappingMode { get; set; }
		public bool richText { get; set; }
		public float preferredWidth { get; set; }
		public TMP_TextInfo textInfo { get; } = new TMP_TextInfo();
		public void ForceMeshUpdate() { }
		public void UpdateMeshPadding() { }
	}

	public enum TextWrappingModes
	{
		Normal = 0,
		NoWrap = 1,
		PreserveWhitespace = 2,
		PreserveWhitespaceNoWrap = 3,
	}

#pragma warning restore CA1051
#pragma warning restore CA1050
#pragma warning restore CA1707
#pragma warning restore CA1822
}

namespace UnityEngine.UI
{
#pragma warning disable CA1050

	public sealed class RectMask2D : UnityEngine.MonoBehaviour
	{
	}

#pragma warning restore CA1050
}

#endif
