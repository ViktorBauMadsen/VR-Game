using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Casino > Performance Report
//
// Scans every loaded scene (works in edit mode and in Play mode - Play mode
// also includes everything spawned at runtime) and writes
// PerformanceReport.txt in the project folder:
//   - totals: renderers, triangles, shadow casters, draw-call estimate
//   - heaviest objects, meshes and groups by triangle count
//   - hot spots: 4 m floor cells with the most triangles / renderers
//   - "look around": what's in view in 8 directions from the camera
//     (Play mode: the headset camera; edit mode: the Scene view camera)
//   - cameras, lights, particles, shaders and big textures
public static class ScenePerformanceReport
{
    const float Cell = 4f;

    sealed class Item
    {
        public Renderer r;
        public Mesh mesh;
        public int tris, subMeshes;
        public bool shadows, skinned;
    }

    [MenuItem("Tools/Casino/Performance Report")]
    public static void Run()
    {
        var items = new List<Item>();
        foreach (var r in AllInLoadedScenes<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (r.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
            items.Add(new Item
            {
                r = r,
                mesh = mesh,
                tris = Triangles(mesh),
                subMeshes = Mathf.Max(1, r.sharedMaterials.Length),
                shadows = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off,
                skinned = r is SkinnedMeshRenderer,
            });
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Performance report - {System.DateTime.Now:yyyy-MM-dd HH:mm} - {(Application.isPlaying ? "PLAY MODE" : "edit mode")}");
        sb.AppendLine("Scenes: " + string.Join(", ", LoadedScenes().Select(s => s.name)));
        sb.AppendLine();

        // ---- totals
        long totalTris = items.Sum(i => (long)i.tris);
        int drawCalls = items.Sum(i => i.subMeshes);
        sb.AppendLine("== TOTALS (active, enabled renderers) ==");
        sb.AppendLine($"Renderers: {items.Count}  (skinned: {items.Count(i => i.skinned)})");
        sb.AppendLine($"Triangles: {totalTris:N0}");
        sb.AppendLine($"Draw calls if everything were in view (renderer x material): {drawCalls:N0}");
        sb.AppendLine($"Shadow casters: {items.Count(i => i.shadows)}  ({items.Where(i => i.shadows).Sum(i => (long)i.tris):N0} tris drawn again into the shadow map)");
        int staticBatch = items.Count(i => GameObjectUtility.GetStaticEditorFlags(i.r.gameObject).HasFlag(StaticEditorFlags.BatchingStatic));
        sb.AppendLine($"Batching-static renderers: {staticBatch} of {items.Count}");
        sb.AppendLine($"LOD groups: {AllInLoadedScenes<LODGroup>().Count()}");
        sb.AppendLine("Quest 2 rough budget: ~100-200k tris and ~100-200 draw calls *in view* per frame at 72 Hz.");
        sb.AppendLine();

        // ---- heaviest objects
        sb.AppendLine("== TOP 40 OBJECTS BY TRIANGLES ==");
        foreach (var i in items.OrderByDescending(i => i.tris).Take(40))
            sb.AppendLine($"{i.tris,9:N0}  mats {i.subMeshes,2}  {(i.shadows ? "shadow" : "      ")} {(i.skinned ? "skinned" : "       ")}  {PathOf(i.r.transform)}  [{(i.mesh ? i.mesh.name : "no mesh")}]");
        sb.AppendLine();

        // ---- meshes by total cost (tris x copies)
        sb.AppendLine("== TOP 30 MESHES BY TOTAL TRIANGLES (tris x copies) ==");
        foreach (var g in items.Where(i => i.mesh != null).GroupBy(i => i.mesh)
                     .Select(g => new { mesh = g.Key, copies = g.Count(), each = g.First().tris, total = g.Sum(i => (long)i.tris) })
                     .OrderByDescending(g => g.total).Take(30))
        {
            string asset = AssetDatabase.GetAssetPath(g.mesh);
            if (string.IsNullOrEmpty(asset)) asset = "generated / in scene";
            sb.AppendLine($"{g.total,11:N0}  = {g.each,8:N0} x {g.copies,4}  {g.mesh.name}  ({asset})");
        }
        sb.AppendLine();

        // ---- groups (top two hierarchy levels)
        sb.AppendLine("== TOP 30 GROUPS (first two hierarchy levels) ==");
        foreach (var g in items.GroupBy(i => GroupOf(i.r.transform))
                     .Select(g => new { name = g.Key, n = g.Count(), tris = g.Sum(i => (long)i.tris), dc = g.Sum(i => i.subMeshes) })
                     .OrderByDescending(g => g.tris).Take(30))
            sb.AppendLine($"{g.tris,11:N0} tris  {g.n,5} renderers  {g.dc,5} draw calls  {g.name}");
        sb.AppendLine();

        // ---- floor hot spots
        sb.AppendLine($"== HOT SPOTS ({Cell} m floor cells, by triangles) ==");
        var cells = items.GroupBy(i => new Vector2Int(Mathf.FloorToInt(i.r.bounds.center.x / Cell), Mathf.FloorToInt(i.r.bounds.center.z / Cell)))
            .Select(g => new
            {
                c = g.Key, n = g.Count(), tris = g.Sum(i => (long)i.tris), dc = g.Sum(i => i.subMeshes),
                top = g.OrderByDescending(i => i.tris).First(),
            })
            .OrderByDescending(g => g.tris).ToList();
        foreach (var g in cells.Take(20))
            sb.AppendLine($"x {g.c.x * Cell,6:0}..{(g.c.x + 1) * Cell,-5:0} z {g.c.y * Cell,6:0}..{(g.c.y + 1) * Cell,-5:0}  {g.tris,10:N0} tris  {g.n,4} renderers  {g.dc,4} draw calls   biggest: {PathOf(g.top.r.transform)} ({g.top.tris:N0})");
        sb.AppendLine("By draw calls:");
        foreach (var g in cells.OrderByDescending(g => g.dc).Take(10))
            sb.AppendLine($"x {g.c.x * Cell,6:0}..{(g.c.x + 1) * Cell,-5:0} z {g.c.y * Cell,6:0}..{(g.c.y + 1) * Cell,-5:0}  {g.dc,4} draw calls  {g.n,4} renderers  {g.tris,10:N0} tris");
        sb.AppendLine();

        // ---- look around from the viewer
        LookAround(sb, items);

        // ---- cameras
        sb.AppendLine("== CAMERAS (each enabled one renders the scene again) ==");
        foreach (var c in AllInLoadedScenes<Camera>().Where(c => c.gameObject.activeInHierarchy))
            sb.AppendLine($"{(c.enabled ? "ENABLED " : "disabled")}  target {(c.targetTexture ? $"{c.targetTexture.width}x{c.targetTexture.height}" : "screen/headset")}  far {c.farClipPlane:0}  mask {c.cullingMask}  {PathOf(c.transform)}");
        sb.AppendLine();

        // ---- lights
        sb.AppendLine("== LIGHTS ==");
        foreach (var l in AllInLoadedScenes<Light>().Where(l => l.enabled && l.gameObject.activeInHierarchy))
            sb.AppendLine($"{l.type,-11} {l.lightmapBakeType,-8} shadows {l.shadows,-4} range {l.range,5:0.#}  {PathOf(l.transform)}");
        var probes = AllInLoadedScenes<ReflectionProbe>().Where(p => p.enabled && p.gameObject.activeInHierarchy).ToList();
        foreach (var p in probes)
            sb.AppendLine($"ReflectionProbe {p.mode} refresh {p.refreshMode}  {PathOf(p.transform)}");
        sb.AppendLine();

        // ---- particles
        var ps = AllInLoadedScenes<ParticleSystem>().Where(p => p.gameObject.activeInHierarchy).ToList();
        if (ps.Count > 0)
        {
            sb.AppendLine("== PARTICLE SYSTEMS ==");
            foreach (var p in ps.OrderByDescending(p => p.main.maxParticles).Take(20))
                sb.AppendLine($"max {p.main.maxParticles,6}  {PathOf(p.transform)}");
            sb.AppendLine();
        }

        // ---- shaders / materials
        var mats = items.SelectMany(i => i.r.sharedMaterials).Where(m => m != null).ToList();
        sb.AppendLine("== SHADERS (renderer slots using each) ==");
        foreach (var g in mats.GroupBy(m => m.shader ? m.shader.name : "null").OrderByDescending(g => g.Count()))
            sb.AppendLine($"{g.Count(),6}  {g.Key}");
        int transparent = mats.Count(m => m.renderQueue >= 2500);
        sb.AppendLine($"Transparent material slots: {transparent} (overdraw - expensive on Quest)");
        sb.AppendLine($"Unique materials: {mats.Distinct().Count()}");
        sb.AppendLine();

        // ---- textures
        sb.AppendLine("== TOP 25 TEXTURES BY MEMORY ==");
        var texs = new HashSet<Texture>();
        foreach (var m in mats.Distinct())
            foreach (var id in m.GetTexturePropertyNameIDs())
            {
                var t = m.GetTexture(id);
                if (t != null) texs.Add(t);
            }
        foreach (var t in texs.OrderByDescending(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t)).Take(25))
            sb.AppendLine($"{UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t) / 1024f / 1024f,7:0.0} MB  {t.width}x{t.height}  {t.name}  ({AssetDatabase.GetAssetPath(t)})");

        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PerformanceReport.txt"));
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"Performance report: {items.Count} renderers, {totalTris:N0} triangles. Written to {path}");
    }

    static void LookAround(StringBuilder sb, List<Item> items)
    {
        Camera viewer = Application.isPlaying ? Camera.main : SceneView.lastActiveSceneView?.camera;
        if (viewer == null) { sb.AppendLine("(no camera for the look-around check)"); sb.AppendLine(); return; }
        Vector3 pos = viewer.transform.position;
        sb.AppendLine($"== LOOK AROUND from {pos:F1} ({(Application.isPlaying ? "headset camera" : "Scene view camera")}, 90 deg view, 400 m) ==");
        string[] names = { "N (+z)", "NE", "E (+x)", "SE", "S (-z)", "SW", "W (-x)", "NW" };
        var planes = new Plane[6];
        for (int d = 0; d < 8; d++)
        {
            var rot = Quaternion.Euler(0f, d * 45f, 0f);
            var proj = Matrix4x4.Perspective(90f, 1f, 0.04f, 400f);
            var view = Matrix4x4.TRS(pos, rot, Vector3.one).inverse;
            view.m20 *= -1f; view.m21 *= -1f; view.m22 *= -1f; view.m23 *= -1f; // OpenGL looks down -z
            GeometryUtility.CalculateFrustumPlanes(proj * view, planes);
            var vis = items.Where(i => GeometryUtility.TestPlanesAABB(planes, i.r.bounds)).ToList();
            var top = vis.OrderByDescending(i => i.tris).Take(3).Select(i => $"{i.r.name} {i.tris:N0}");
            sb.AppendLine($"{names[d],-7} {vis.Sum(i => (long)i.tris),10:N0} tris  {vis.Sum(i => i.subMeshes),5} draw calls  {vis.Count,5} renderers   heaviest: {string.Join(", ", top)}");
        }
        sb.AppendLine("(Frustum culling only - no occlusion culling, so walls don't hide what's behind them.)");
        sb.AppendLine();
    }

    static int Triangles(Mesh mesh)
    {
        if (mesh == null) return 0;
        long n = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
            if (mesh.GetTopology(s) == MeshTopology.Triangles) n += mesh.GetIndexCount(s) / 3;
        return (int)n;
    }

    static IEnumerable<Scene> LoadedScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) yield return s;
        }
        if (Application.isPlaying)
        {
            // DontDestroyOnLoad objects live in their own hidden scene.
            var probe = new GameObject("ScenePerformanceReport probe");
            Object.DontDestroyOnLoad(probe);
            var dd = probe.scene;
            Object.DestroyImmediate(probe);
            yield return dd;
        }
    }

    static IEnumerable<T> AllInLoadedScenes<T>() where T : Component
    {
        foreach (var s in LoadedScenes())
            foreach (var root in s.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<T>(true))
                    yield return c;
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null && parts.Count < 5; t = t.parent) parts.Add(t.name);
        if (t != null) parts.Add("...");
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string GroupOf(Transform t)
    {
        var chain = new List<Transform>();
        for (; t != null; t = t.parent) chain.Add(t);
        chain.Reverse();
        return chain.Count > 1 ? chain[0].name + "/" + chain[1].name : chain[0].name;
    }
}
