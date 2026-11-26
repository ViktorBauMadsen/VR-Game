using System.Collections;
using UnityEditor;
using UnityEngine;

// Tools > Casino > Mafia
//   Build Mafia Assets - the rigged hitman (Resources/Mafia Hitman.prefab, see
//                        SetupPatrons.BuildHitman), the sunset sky for the
//                        desert canyon (Resources/Mafia Sunset Sky.mat), and
//                        measures where the bang is in the recorded gunshot
//                        (MafiaSounds.gunshotBangAt) so the flash and the hit
//                        land on it.
//   Playtest/...       - in Play mode, without a headset: lose the run on the
//                        spot (the whole death scene), or jump straight to the
//                        canyon / the hitman to look at them.
public static class SetupMafia
{
    const string Menu = "Tools/Casino/Mafia/";
    const string SkyPath = "Assets/Scripts/Run/Resources/Mafia Sunset Sky.mat";
    const string SoundsPath = "Assets/Scripts/Run/Resources/MafiaSounds.asset";

    [MenuItem(Menu + "Build Mafia Assets")]
    public static void Build()
    {
        var hitman = SetupPatrons.BuildHitman();
        var sky = BuildSky();
        float bang = MeasureBang();
        AssetDatabase.SaveAssets();
        // Reload it from disk so nothing in memory points at a replaced material or mesh.
        if (hitman != null)
        {
            AssetDatabase.ImportAsset(SetupPatrons.HitmanPath, ImportAssetOptions.ForceUpdate);
            hitman = AssetDatabase.LoadAssetAtPath<GameObject>(SetupPatrons.HitmanPath);
        }
        Debug.Log($"SetupMafia: hitman {(hitman != null ? "built" : "FAILED (see warnings)")}, sunset sky {(sky != null ? "ok" : "FAILED")}, "
            + (bang >= 0f ? $"gunshot bang at {bang:0.000} s." : "no recorded gunshot to measure."));
        if (hitman != null) Selection.activeObject = hitman;
    }

    static Material BuildSky()
    {
        var shader = Shader.Find("Skybox/Procedural");
        if (shader == null) return null;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "Mafia Sunset Sky" };
            AssetDatabase.CreateAsset(mat, SkyPath);
        }
        mat.shader = shader;
        mat.SetFloat("_SunDisk", 2f);
        mat.SetFloat("_SunSize", 0.06f);
        mat.SetFloat("_SunSizeConvergence", 3f);
        mat.SetFloat("_AtmosphereThickness", 2.1f);
        mat.SetColor("_SkyTint", new Color(0.6f, 0.45f, 0.55f));
        mat.SetColor("_GroundColor", new Color(0.55f, 0.36f, 0.26f));
        mat.SetFloat("_Exposure", 1.25f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Where the loudest burst in the recorded gunshot begins: walk back from
    // the peak to the quiet before it, then forward to the first loud sample.
    static float MeasureBang()
    {
        var sounds = AssetDatabase.LoadAssetAtPath<MafiaSounds>(SoundsPath);
        if (sounds == null || sounds.gunshot == null) return -1f;
        var clip = sounds.gunshot;
        clip.LoadAudioData();
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) return -1f;

        int window = Mathf.Max(1, clip.frequency / 100) * clip.channels; // 10 ms
        int windows = data.Length / window;
        var level = new float[windows];
        float peak = 0f;
        int peakAt = 0;
        for (int w = 0; w < windows; w++)
        {
            float m = 0f;
            for (int i = w * window; i < (w + 1) * window; i++) m = Mathf.Max(m, Mathf.Abs(data[i]));
            level[w] = m;
            if (m > peak) { peak = m; peakAt = w; }
        }
        int start = peakAt;
        while (start > 0 && level[start - 1] > peak * 0.1f) start--;
        int sample = start * window;
        while (sample < data.Length && Mathf.Abs(data[sample]) < peak * 0.3f) sample++;
        float seconds = (float)(sample / clip.channels) / clip.frequency;

        var so = new SerializedObject(sounds);
        so.FindProperty("gunshotBangAt").floatValue = seconds;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sounds);
        return seconds;
    }

    // ------------------------------------------------------------ playtest

    [MenuItem(Menu + "Playtest/Lose Run Now (full death scene)")]
    static void LoseRun()
    {
        var run = CasinoRun.Instance;
        if (run == null) { Debug.LogWarning("SetupMafia: no CasinoRun in the scene."); return; }
        if (run.Phase != RunPhase.Running && run.Phase != RunPhase.Gathering) run.StartRun();
        run.GiveUp();
    }

    [MenuItem(Menu + "Playtest/Go To Canyon")]
    static void GoToCanyon()
    {
        KidnapScene.Enter();
        MafiaHit.Spawn(KidnapScene.HitmanStart, KidnapScene.LineCentre, true);
        Runner().StartCoroutine(Aim());
    }

    static IEnumerator Aim()
    {
        yield return MafiaHit.WalkTo(() => KidnapScene.StandPoint(KidnapScene.LocalIndex), 0.05f, 4f, () => KidnapScene.HeadOf(KidnapScene.LocalIndex));
        yield return MafiaHit.Act(() => KidnapScene.HeadOf(KidnapScene.LocalIndex), 0.8f, 1f, 0f);
    }

    [MenuItem(Menu + "Playtest/Hitman Walks Up (casino)")]
    static void WalkUp()
    {
        var cam = Camera.main;
        var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (cam == null) return;
        float floor = origin != null ? origin.transform.position.y : cam.transform.position.y - 1.6f;
        Runner().StartCoroutine(WalkUpRoutine(cam.transform, floor));
    }

    static IEnumerator WalkUpRoutine(Transform player, float floor)
    {
        yield return MafiaHit.Approach(player, floor);
        yield return MafiaHit.Act(() => player.position, 1f, 0f, 0f);
        yield return MafiaHit.WalkTo(() => player.position, 0.62f, 1.5f, () => player.position);
        yield return MafiaHit.Act(() => player.position, 0.55f, 0f, 1f);
    }

    [MenuItem(Menu + "Playtest/Back To Casino")]
    static void Back()
    {
        MafiaHit.Dismiss();
        KidnapScene.Exit();
    }

    [MenuItem(Menu + "Playtest/Lose Run Now (full death scene)", true)]
    [MenuItem(Menu + "Playtest/Go To Canyon", true)]
    [MenuItem(Menu + "Playtest/Hitman Walks Up (casino)", true)]
    [MenuItem(Menu + "Playtest/Back To Casino", true)]
    static bool InPlayMode() => Application.isPlaying;

    static MafiaPlaytestRunner Runner()
    {
        var runner = Object.FindAnyObjectByType<MafiaPlaytestRunner>();
        if (runner == null) runner = new GameObject("Mafia Playtest").AddComponent<MafiaPlaytestRunner>();
        return runner;
    }
}

// Runs the playtest coroutines (play mode only).
public class MafiaPlaytestRunner : MonoBehaviour { }
