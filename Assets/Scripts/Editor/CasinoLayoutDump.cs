using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Writes the open scene's hierarchy (depth 3) with world positions, rotations
// and renderer bounds to Temp/CasinoLayout.txt, so the interior can be planned
// outside the editor. Read-only: changes nothing in the scene.
public static class CasinoLayoutDump
{
    [MenuItem("Tools/Casino/Dump Layout")]
    static void Dump()
    {
        var sb = new StringBuilder();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            Write(sb, root.transform, 0);

        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
        sb.AppendLine("\n== LIGHTS ==");
        foreach (var l in lights)
            sb.AppendLine($"{Path(l.transform)} type={l.type} mode={l.lightmapBakeType} int={l.intensity} range={l.range} col={l.color} pos={l.transform.position} active={l.gameObject.activeInHierarchy}");

        sb.AppendLine($"\nRenderSettings ambientMode={RenderSettings.ambientMode} ambient={RenderSettings.ambientLight} sky={RenderSettings.skybox} fog={RenderSettings.fog}");

        File.WriteAllText("Temp/CasinoLayout.txt", sb.ToString());
        Debug.Log("CasinoLayoutDump: wrote Temp/CasinoLayout.txt");
    }

    // Scene-view camera shortcuts for reviewing the interior.
    [MenuItem("Tools/Casino/View/From Entrance")] static void ViewEntrance() => View(new Vector3(-2.7f, 1.7f, 27.6f), new Vector3(8f, 180f, 0f));
    [MenuItem("Tools/Casino/View/Toward Entrance")] static void ViewBack() => View(new Vector3(-2.7f, 1.7f, 2.5f), new Vector3(6f, 0f, 0f));
    [MenuItem("Tools/Casino/View/Lounge and Bar")] static void ViewLounge() => View(new Vector3(0.5f, 1.7f, 22.5f), new Vector3(8f, 240f, 0f));
    [MenuItem("Tools/Casino/View/Blackjack Row")] static void ViewBlackjack() => View(new Vector3(-5.5f, 1.7f, 22f), new Vector3(8f, 150f, 0f));

    static void View(Vector3 eye, Vector3 euler)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) return;
        var rot = Quaternion.Euler(euler);
        const float d = 2f;
        view.LookAtDirect(eye + rot * Vector3.forward * d, rot, d * Mathf.Tan(30f * Mathf.Deg2Rad));
        view.Repaint();
    }

    static void Write(StringBuilder sb, Transform t, int depth)
    {
        var b = Bounds(t, out bool has);
        sb.Append(new string(' ', depth * 2)).Append(t.name)
          .Append(t.gameObject.activeInHierarchy ? "" : " [inactive]")
          .Append($" pos={F(t.position)} rotY={t.eulerAngles.y:F0} scale={F(t.lossyScale)}");
        if (has) sb.Append($" bmin={F(b.min)} bmax={F(b.max)}");
        sb.Append($" kids={t.childCount}");
        if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
            sb.Append(" prefab=").Append(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)));
        sb.AppendLine();
        if (depth < 2)
            foreach (Transform c in t) Write(sb, c, depth + 1);
    }

    static Bounds Bounds(Transform t, out bool has)
    {
        has = false;
        var b = new Bounds();
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static string F(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
