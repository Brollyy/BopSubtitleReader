#if SKIP_GAME_REFERENCES
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
}

public sealed class JukeboxScript : MonoBehaviour
{
	public float CurrentBeat { get; set; }
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
#endif
