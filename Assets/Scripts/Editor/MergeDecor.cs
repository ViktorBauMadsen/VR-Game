using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Tools > Casino > Merge Decor / Unmerge Decor
//
// The decor tools build chandeliers, palms, lamp posts, facades... out of
// thousands of small primitives, and every one is a separate draw call - far
// more than a Quest 2 can submit at 72 Hz. This merges the pieces that never
// move into one mesh per material per 8 m patch of floor, so the headset
// draws a few dozen big meshes instead of thousands of small ones.
//
// Merged meshes never straddle HallCulling's zones (inside the hall /
// outside / the shell), so its inside-outside culling keeps working.
//
// A piece is only merged if nothing can move or change it: not under a
// Rigidbody, Animator or any script, not referenced by any script in the
// scene, opaque, one material, readable mesh. The originals keep
// their colliders and are only switched off (Renderer.enabled), so
// Unmerge Decor puts everything back exactly. Merged meshes are saved under
// Assets/Generated/Merged/<scene>/.
//
// Rebuilding decor with another tool? Unmerge first, rebuild, merge again.
public static class MergeDecor
{
    const float Cell = 8f;
    const string RootName = "Merged Decor";

    struct Source
    {
        public MeshRenderer renderer;
        public Mesh mesh;
        public int subMesh;
        public Matrix4x4 matrix;
        public Bounds bounds;
    }

    sealed class MeshData
    {
        public Vector3[] vertices, normals;
        public Vector4[] tangents;
        public Vector2[] uv;
        public Color[] colors;
        public int[][] subMeshes;
    }

    [MenuItem("Tools/Casino/Merge Decor")]
    public static void Merge()
    {
        if (Application.isPlaying) { Debug.LogWarning("MergeDecor: leave Play mode first."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.GetRootGameObjects().Any(g => g.name == RootName))
        {
            Debug.LogWarning("MergeDecor: this scene is already merged - run Tools > Casino > Unmerge Decor first.");
            return;
        }

        var referenced = ReferencedByScripts(scene);
        var groups = new Dictionary<(HallCulling.Zone, int, int, Material, ShadowCastingMode, bool, int), List<Source>>();
        int candidates = 0, skipped = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<Camera>(true) != null) continue; // the player rig
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                candidates++;
                if (!Mergeable(r, root.transform, referenced, out var mesh)) { skipped++; continue; }

                var b = r.bounds;
                var zone = HallCulling.ZoneOf(b);
                int cx = Mathf.FloorToInt(b.center.x / Cell), cz = Mathf.FloorToInt(b.center.z / Cell);
                var key = (zone, cx, cz, r.sharedMaterial, r.shadowCastingMode, r.receiveShadows, r.gameObject.layer);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Source>();
                list.Add(new Source { renderer = r, mesh = mesh, subMesh = 0, matrix = r.localToWorldMatrix, bounds = b });
            }
        }

        // Fresh folder for this scene's meshes.
        string folder = $"Assets/Generated/Merged/{scene.name}";
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();

        Undo.SetCurrentGroupName("Merge Decor");
        int undoGroup = Undo.GetCurrentGroup();
        var mergedRoot = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(mergedRoot, scene);
        Undo.RegisterCreatedObjectUndo(mergedRoot, "Merge Decor");
        var record = mergedRoot.AddComponent<MergedDecor>();

        var cache = new Dictionary<Mesh, MeshData>();
        var disabled = new HashSet<MeshRenderer>();
        int meshes = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var pair in groups)
            {
                foreach (var part in SplitByZone(pair.Value, pair.Key.Item1))
                {
                    if (part.Count < 2) continue; // nothing to gain
                    var (zone, cx, cz, material, shadows, receive, layer) = pair.Key;
                    string name = $"{zone} {cx}_{cz} {material.name} {meshes}";
                    var mesh = Combine(part, cache, name);

                    var go = new GameObject(name);
                    go.transform.SetParent(mergedRoot.transform, false);
                    go.layer = layer;
                    GameObjectUtility.SetStaticEditorFlags(go,
                        GameObjectUtility.GetStaticEditorFlags(part[0].renderer.gameObject) & ~StaticEditorFlags.BatchingStatic);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = material;
                    mr.shadowCastingMode = shadows;
                    mr.receiveShadows = receive;

                    AssetDatabase.CreateAsset(mesh, $"{folder}/{Sanitize(name)}.asset");
                    meshes++;
                    foreach (var src in part) disabled.Add(src.renderer);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        int off = 0;
        foreach (var r in disabled)
        {
            Undo.RecordObject(r, "Merge Decor");
            r.enabled = false;
            record.sources.Add(r);
            off++;
        }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"MergeDecor: {off} of {candidates} renderers merged into {meshes} meshes ({skipped} left alone: they move, have scripts or are transparent). " +
                  "Check the scene, then save it (Ctrl+S). Undo: Ctrl+Z, or later Tools > Casino > Unmerge Decor.");
    }

    [MenuItem("Tools/Casino/Unmerge Decor")]
    public static void Unmerge()
    {
        if (Application.isPlaying) { Debug.LogWarning("MergeDecor: leave Play mode first."); return; }
        var scene = SceneManager.GetActiveScene();
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
        if (root == null) { Debug.Log("MergeDecor: nothing merged in this scene."); return; }

        Undo.SetCurrentGroupName("Unmerge Decor");
        int group = Undo.GetCurrentGroup();
        int on = 0;
        var record = root.GetComponent<MergedDecor>();
        if (record != null)
            foreach (var r in record.sources)
            {
                if (r == null) continue;
                Undo.RecordObject(r, "Unmerge Decor");
                r.enabled = true;
                on++;
            }
        Undo.DestroyObjectImmediate(root);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"MergeDecor: unmerged - {on} original renderers switched back on.");
    }

    static bool Mergeable(MeshRenderer r, Transform top, HashSet<GameObject> referenced, out Mesh mesh)
    {
        mesh = null;
        if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) return false;
        mesh = mf.sharedMesh;
        // One material, opaque, plain triangles, and CPU-readable mesh data.
        var mats = r.sharedMaterials;
        if (mesh.subMeshCount != 1 || mats.Length != 1 || mats[0] == null || mats[0].renderQueue >= 2500) return false;
        if (mesh.GetTopology(0) != MeshTopology.Triangles || !mesh.isReadable) return false;
        if (r.GetComponentInParent<Rigidbody>(true) != null) return false;
        if (r.GetComponentInParent<Animator>(true) != null || r.GetComponentInParent<Animation>(true) != null) return false;
        if (r.GetComponentInParent<LODGroup>(true) != null) return false;

        // Any script on the piece or a parent may move or restyle it; so may any
        // script that references it or a parent below the scene root.
        for (var t = r.transform; t != null; t = t.parent)
        {
            if (t.GetComponents<MonoBehaviour>().Any(m => m != null)) return false;
            if (t == top) break;
            if (referenced.Contains(t.gameObject)) return false;
        }
        return true;
    }

    // Every GameObject that a script in the scene points at (directly or via
    // one of its components).
    static HashSet<GameObject> ReferencedByScripts(Scene scene)
    {
        var set = new HashSet<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var it = new SerializedObject(mb).GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var obj = it.objectReferenceValue;
                    if (obj is GameObject go) set.Add(go);
                    else if (obj is Component c && c != mb) set.Add(c.gameObject);
                }
            }
        return set;
    }

    // Splits a group until each part's combined bounds sits in one HallCulling zone.
    static IEnumerable<List<Source>> SplitByZone(List<Source> sources, HallCulling.Zone zone)
    {
        var bounds = sources[0].bounds;
        foreach (var s in sources) bounds.Encapsulate(s.bounds);
        if (sources.Count == 1 || HallCulling.ZoneOf(bounds) == zone) { yield return sources; yield break; }

        var size = bounds.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        var sorted = sources.OrderBy(s => s.bounds.center[axis]).ToList();
        int half = sorted.Count / 2;
        foreach (var part in SplitByZone(sorted.GetRange(0, half), zone)) yield return part;
        foreach (var part in SplitByZone(sorted.GetRange(half, sorted.Count - half), zone)) yield return part;
    }

    static Mesh Combine(List<Source> sources, Dictionary<Mesh, MeshData> cache, string name)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        bool anyColors = false, allTangents = true;

        foreach (var src in sources)
        {
            if (!cache.TryGetValue(src.mesh, out var data))
            {
                data = new MeshData
                {
                    vertices = src.mesh.vertices,
                    normals = src.mesh.normals,
                    tangents = src.mesh.tangents,
                    uv = src.mesh.uv,
                    colors = src.mesh.colors,
                    subMeshes = Enumerable.Range(0, src.mesh.subMeshCount).Select(s => src.mesh.GetTriangles(s)).ToArray(),
                };
                cache[src.mesh] = data;
            }
            anyColors |= data.colors.Length > 0;
            allTangents &= data.tangents.Length > 0;
        }

        foreach (var src in sources)
        {
            var data = cache[src.mesh];
            var m = src.matrix;
            var normalMatrix = m.inverse.transpose;
            bool flip = m.determinant < 0f;
            var remap = new Dictionary<int, int>();
            var tris = data.subMeshes[src.subMesh];
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = Map(tris[i]), b = Map(tris[i + 1]), c = Map(tris[i + 2]);
                if (flip) { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
                else { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            }

            int Map(int v)
            {
                if (remap.TryGetValue(v, out int idx)) return idx;
                idx = vertices.Count;
                remap[v] = idx;
                vertices.Add(m.MultiplyPoint3x4(data.vertices[v]));
                normals.Add(data.normals.Length > 0 ? normalMatrix.MultiplyVector(data.normals[v]).normalized : Vector3.up);
                uvs.Add(data.uv.Length > 0 ? data.uv[v] : Vector2.zero);
                if (anyColors) colors.Add(data.colors.Length > 0 ? data.colors[v] : Color.white);
                if (allTangents)
                {
                    var t = data.tangents[v];
                    var dir = m.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                    tangents.Add(new Vector4(dir.x, dir.y, dir.z, flip ? -t.w : t.w));
                }
                return idx;
            }
        }

        var mesh = new Mesh { name = name };
        if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        if (anyColors) mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        if (allTangents) mesh.SetTangents(tangents);
        else mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
