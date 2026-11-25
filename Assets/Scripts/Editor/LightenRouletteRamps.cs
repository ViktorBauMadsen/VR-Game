using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Roulette > Lighten Ramps
//
// Rebuilds every roulette wheel's ramp in the open scene with 96 slices
// around the bowl instead of 360 (8,640 -> 2,304 triangles each). The 360
// was there for a physics ball; the ball is a scripted animation now.
//
// The wheels share one saved mesh (Assets/Prefabs/Roulette/RouletteRamp.asset),
// so that asset is rewritten in place and every wheel - and the Roulette
// Table prefab - picks it up. The prefab's builder setting is lowered too,
// so "Rebuild Ramp" keeps the lighter version.
public static class LightenRouletteRamps
{
    [MenuItem("Tools/Roulette/Lighten Ramps")]
    public static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("LightenRouletteRamps: leave Play mode first."); return; }

        var builders = Object.FindObjectsByType<RouletteRampBuilder>(FindObjectsInactive.Include);
        if (builders.Length == 0) { Debug.Log("LightenRouletteRamps: no roulette ramps in this scene."); return; }

        Undo.SetCurrentGroupName("Lighten Ramps");
        int group = Undo.GetCurrentGroup();
        var rewritten = new HashSet<Mesh>();
        int before = 0, after = 0, rebuilt = 0;

        foreach (var builder in builders)
        {
            // The prefab's own setting first, so instances inherit it.
            var source = PrefabUtility.GetCorrespondingObjectFromSource(builder);
            if (source != null) SetSegments(source, false);
            SetSegments(builder, true);

            var mf = builder.GetComponent<MeshFilter>();
            var current = mf != null ? mf.sharedMesh : null;
            if (current != null && rewritten.Contains(current)) { rebuilt++; continue; }
            if (current != null) before += (int)current.GetIndexCount(0) / 3;

            var fresh = builder.CreateMesh();
            after += (int)fresh.GetIndexCount(0) / 3;

            if (current != null && AssetDatabase.Contains(current))
            {
                // Shared saved mesh: overwrite its data so every user updates.
                current.Clear();
                current.SetVertices(fresh.vertices);
                current.SetUVs(0, fresh.uv);
                current.SetNormals(fresh.normals);
                current.SetTriangles(fresh.triangles, 0);
                current.RecalculateBounds();
                EditorUtility.SetDirty(current);
                rewritten.Add(current);
                Object.DestroyImmediate(fresh);
            }
            else
            {
                Undo.RecordObject(mf, "Lighten Ramps");
                var col = builder.GetComponent<MeshCollider>();
                if (col != null) Undo.RecordObject(col, "Lighten Ramps");
                builder.Rebuild();
            }
            rebuilt++;
        }

        // Colliders re-read the rewritten mesh.
        foreach (var col in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
            if (col.sharedMesh != null && rewritten.Contains(col.sharedMesh))
            {
                var m = col.sharedMesh;
                col.sharedMesh = null;
                col.sharedMesh = m;
            }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(builders[0].gameObject.scene);
        Debug.Log($"LightenRouletteRamps: {rebuilt} ramp(s) now {RouletteRampBuilder.LightSegments} slices " +
                  $"({before:N0} -> {after:N0} triangles per distinct mesh). Save the scene (Ctrl+S).");
    }

    static void SetSegments(RouletteRampBuilder builder, bool undo)
    {
        var so = new SerializedObject(builder);
        var p = so.FindProperty("angularSegments");
        if (p == null || p.intValue == RouletteRampBuilder.LightSegments) return;
        p.intValue = RouletteRampBuilder.LightSegments;
        if (undo) so.ApplyModifiedProperties();
        else
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(builder);
        }
    }
}
