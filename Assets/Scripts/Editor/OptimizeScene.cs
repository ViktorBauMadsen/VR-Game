using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Tools > Casino > Optimize Scene
//
// Cheap wins for the Quest found by Tools > Casino > Performance Report:
//
//   - Unity's built-in Sphere is 768 triangles and the decor tools used ~800
//     of them (chandelier crystals, palm fronds, lamp globes, bar pieces):
//     over half the scene's triangles. Each one is swapped for a low-poly
//     sphere picked by its size on screen (100 / 196 / 520 triangles), and
//     the 832-triangle Capsule for a 144-triangle one. Same size, same UVs,
//     smooth normals - at these sizes they look the same.
//   - Small objects (under 0.5 m) stop casting shadows. Their shadows are a
//     few pixels in the shadow map but every one is drawn a second time.
//
// Only MeshFilters and Renderers change - colliders (including a MeshCollider
// using the sphere mesh) are left alone. One Undo step; the scene is marked
// dirty, not saved. Run it again after rebuilding decor with the other tools.
public static class OptimizeScene
{
    const string Folder = "Assets/Generated/LowPoly";
    const float NoShadowBelow = 0.5f;

    [MenuItem("Tools/Casino/Optimize Scene")]
    public static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("OptimizeScene: leave Play mode first."); return; }

        var sphereSmall = GetMesh("LowPoly Sphere 100", 10, 3, 0f);  // under 0.3 m
        var sphereMid = GetMesh("LowPoly Sphere 196", 14, 4, 0f);    // under 1.5 m
        var sphereBig = GetMesh("LowPoly Sphere 520", 20, 7, 0f);
        var capsule = GetMesh("LowPoly Capsule 144", 12, 3, 0.5f);

        Undo.SetCurrentGroupName("Optimize Scene");
        int group = Undo.GetCurrentGroup();
        int spheres = 0, capsules = 0, shadowsOff = 0;
        long trisBefore = 0, trisAfter = 0;
        var scenes = new HashSet<Scene>();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (!IsBuiltin(mesh)) continue;
                    Mesh replacement = null;
                    if (mesh.name == "Sphere")
                    {
                        float size = SizeOf(mf.GetComponent<Renderer>(), mf.transform);
                        replacement = size < 0.3f ? sphereSmall : size < 1.5f ? sphereMid : sphereBig;
                        spheres++;
                    }
                    else if (mesh.name == "Capsule")
                    {
                        replacement = capsule;
                        capsules++;
                    }
                    if (replacement == null) continue;
                    trisBefore += Triangles(mesh);
                    trisAfter += Triangles(replacement);
                    Undo.RecordObject(mf, "Optimize Scene");
                    mf.sharedMesh = replacement;
                    scenes.Add(scene);
                }

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.shadowCastingMode != ShadowCastingMode.On) continue;
                    if (SizeOf(r, r.transform) >= NoShadowBelow) continue;
                    Undo.RecordObject(r, "Optimize Scene");
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    shadowsOff++;
                    scenes.Add(scene);
                }
            }
        }

        Undo.CollapseUndoOperations(group);
        foreach (var scene in scenes) EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"OptimizeScene: {spheres} spheres and {capsules} capsules swapped for low-poly ones " +
                  $"({trisBefore:N0} -> {trisAfter:N0} triangles), shadows off on {shadowsOff} small objects. " +
                  "Check the scene, then save it (Ctrl+S) - or Ctrl+Z to undo.");
    }

    static bool IsBuiltin(Mesh mesh) =>
        mesh != null && AssetDatabase.GetAssetPath(mesh) == "Library/unity default resources";

    // World size; inactive objects report empty bounds, so fall back to the
    // mesh bounds times the scale.
    static float SizeOf(Renderer r, Transform t)
    {
        if (r != null && r.bounds.size.sqrMagnitude > 0f) return MaxSize(r.bounds.size);
        var mf = t.GetComponent<MeshFilter>();
        var local = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.one;
        return MaxSize(Vector3.Scale(local, t.lossyScale));
    }

    static float MaxSize(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

    static int Triangles(Mesh mesh)
    {
        long n = 0;
        for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s) / 3;
        return (int)n;
    }

    // Loads the saved mesh asset, or builds and saves it.
    static Mesh GetMesh(string name, int segments, int halfRings, float halfCylinder)
    {
        string path = $"{Folder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        Directory.CreateDirectory(Folder);
        var mesh = BuildRounded(name, segments, halfRings, halfCylinder);
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    // A UV sphere of radius 0.5 (halfCylinder 0), or a capsule like Unity's
    // (radius 0.5, total height 1 + 2 * halfCylinder) when the two
    // hemispheres are pulled apart by +-halfCylinder.
    static Mesh BuildRounded(string name, int segments, int halfRings, float halfCylinder)
    {
        const float radius = 0.5f;
        float totalHeight = 2f * (radius + halfCylinder);

        // Rows from the top pole down; the capsule repeats the equator row
        // (once on each hemisphere), which makes the straight middle section.
        var rows = new List<(float theta, float offset)>();
        for (int i = 0; i <= halfRings; i++) rows.Add((Mathf.PI * 0.5f * i / halfRings, halfCylinder));
        for (int i = halfCylinder > 0f ? 0 : 1; i <= halfRings; i++) rows.Add((Mathf.PI * 0.5f * (1f + (float)i / halfRings), -halfCylinder));

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        foreach (var (theta, offset) in rows)
        {
            float ringRadius = Mathf.Sin(theta) * radius;
            float y = Mathf.Cos(theta) * radius + offset;
            for (int j = 0; j <= segments; j++)
            {
                float phi = 2f * Mathf.PI * j / segments;
                var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                vertices.Add(new Vector3(ringRadius * Mathf.Cos(phi), y, ringRadius * Mathf.Sin(phi)));
                normals.Add(n);
                uvs.Add(new Vector2(1f - (float)j / segments, y / totalHeight + 0.5f));
            }
        }

        var triangles = new List<int>();
        int stride = segments + 1;
        for (int i = 0; i < rows.Count - 1; i++)
        {
            bool topPole = i == 0, bottomPole = i + 1 == rows.Count - 1;
            for (int j = 0; j < segments; j++)
            {
                int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                if (!topPole) triangles.AddRange(new[] { a, b, d });
                if (!bottomPole) triangles.AddRange(new[] { a, d, c });
            }
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }
}
