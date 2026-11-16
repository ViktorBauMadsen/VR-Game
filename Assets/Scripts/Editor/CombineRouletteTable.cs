using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns the loose roulette objects in the scene (the wheel, the betting
// layout with its 46 BettingSpots, the RouletteBettingManager and the
// Croupier) into ONE "Roulette Table" root, like "Blackjack Table", and saves
// it as a prefab so more tables can be dropped into the casino.
//
//   Tools > Roulette > 1. Combine Scene Roulette Into One Table
//       Nothing is deleted, moved or rebuilt - the objects are only
//       re-parented under a new root (world positions kept). Anything you
//       have selected in the Hierarchy when you run it (your table model,
//       legs, decorations...) is pulled into the table as well.
//
//   Tools > Roulette > 2. Add Another Roulette Table
//       Drops a new copy of the prefab on the floor under the Scene view's
//       focus point. Each table has its own wheel and its own bets.
public static class CombineRouletteTable
{
    const string Folder = "Assets/Prefabs/Roulette";
    const string PrefabPath = Folder + "/Roulette Table.prefab";

    [MenuItem("Tools/Roulette/1. Combine Scene Roulette Into One Table")]
    static void Combine()
    {
        RouletteWheel wheel = null;
        foreach (var w in Object.FindObjectsByType<RouletteWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (w.GetComponentInParent<RouletteTable>(true) == null) { wheel = w; break; }
        if (wheel == null)
        {
            var done = Object.FindAnyObjectByType<RouletteTable>();
            if (done != null) Selection.activeGameObject = done.gameObject;
            Debug.Log(done != null
                ? "CombineRouletteTable: the roulette is already combined (selected it). Use 'Add Another Roulette Table' for more."
                : "CombineRouletteTable: no RouletteWheel found in the open scene.");
            return;
        }

        // Everything that belongs to the table.
        var pieces = new List<Transform> { wheel.transform };
        RouletteBettingManager manager = null;
        foreach (var m in Object.FindObjectsByType<RouletteBettingManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (m.GetComponentInParent<RouletteTable>(true) != null) continue;
            if (manager == null) manager = m;
            pieces.Add(m.transform);
        }
        foreach (var spot in Object.FindObjectsByType<BettingSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (spot.GetComponentInParent<RouletteTable>(true) != null) continue;
            pieces.Add(spot.transform.parent != null ? spot.transform.parent : spot.transform);
        }
        foreach (var croupier in Object.FindObjectsByType<Croupier>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (croupier.GetComponentInParent<RouletteTable>(true) == null) pieces.Add(croupier.transform);
        foreach (var extra in Selection.transforms)
        {
            // Never swallow the player rig or something the wheel sits inside.
            if (extra.GetComponentInChildren<Camera>(true) != null || wheel.transform.IsChildOf(extra) && extra != wheel.transform) continue;
            pieces.Add(extra);
        }

        // Drop duplicates and anything already inside another piece.
        var unique = new List<Transform>();
        foreach (var piece in pieces)
        {
            if (piece == null || unique.Contains(piece)) continue;
            bool nested = false;
            foreach (var other in pieces)
                if (other != null && other != piece && piece.IsChildOf(other)) { nested = true; break; }
            if (!nested) unique.Add(piece);
        }

        Undo.SetCurrentGroupName("Combine Roulette Table");
        int group = Undo.GetCurrentGroup();

        var root = new GameObject("Roulette Table");
        Undo.RegisterCreatedObjectUndo(root, "Combine Roulette Table");
        if (wheel.transform.parent != null) root.transform.SetParent(wheel.transform.parent, false);
        root.transform.SetPositionAndRotation(FloorPoint(wheel.transform.position, unique), Quaternion.identity);

        foreach (var piece in unique)
            Undo.SetTransformParent(piece, root.transform, "Combine Roulette Table");

        var table = Undo.AddComponent<RouletteTable>(root);
        var so = new SerializedObject(table);
        so.FindProperty("wheel").objectReferenceValue = wheel;
        so.FindProperty("betting").objectReferenceValue = manager;
        so.ApplyModifiedProperties();
        if (manager != null)
        {
            var mso = new SerializedObject(manager);
            if (mso.FindProperty("wheel").objectReferenceValue == null)
            {
                mso.FindProperty("wheel").objectReferenceValue = wheel;
                mso.ApplyModifiedProperties();
            }
        }

        // Meshes/materials that only exist inside the scene file (the
        // generated ramp, for example) would come out empty in a prefab, so
        // they are saved as assets first.
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        int saved = PersistSceneOnlyAssets(root);

        string path = File.Exists(PrefabPath) ? AssetDatabase.GenerateUniqueAssetPath(PrefabPath) : PrefabPath;
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.UserAction, out bool ok);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"CombineRouletteTable: grouped {unique.Count} object(s) under '{root.name}'"
            + (ok ? $" and saved the prefab at {path}" : " (saving the prefab FAILED - see the Console)")
            + (saved > 0 ? $"; {saved} scene-only mesh/material(s) saved to {Folder}" : "")
            + ". Move/rotate/duplicate the root freely. Save the scene to keep it.");
    }

    [MenuItem("Tools/Roulette/2. Add Another Roulette Table")]
    static void AddAnother()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"CombineRouletteTable: no prefab at {PrefabPath} yet - run 'Tools > Roulette > 1. Combine Scene Roulette Into One Table' first.");
            return;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(instance, "Add Roulette Table");
        instance.name = GameObjectUtility.GetUniqueNameForSibling(null, prefab.name);
        SetupAvatar.PlaceAtSceneFocus(instance.transform);
        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(instance.scene);
        Debug.Log($"CombineRouletteTable: added '{instance.name}' at {instance.transform.position}. Move/rotate it into place and save the scene.");
    }

    // The floor under the wheel, so the root's origin sits at floor level
    // like the blackjack table's. Falls back to the lowest point of the pieces.
    static Vector3 FloorPoint(Vector3 wheelPosition, List<Transform> pieces)
    {
        var hits = Physics.RaycastAll(wheelPosition + Vector3.up * 0.5f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        float y = float.NaN;
        foreach (var hit in hits)
        {
            bool own = false;
            foreach (var piece in pieces)
                if (hit.collider.transform.IsChildOf(piece)) { own = true; break; }
            if (own || hit.distance >= best) continue;
            best = hit.distance;
            y = hit.point.y;
        }
        if (float.IsNaN(y))
        {
            y = wheelPosition.y;
            foreach (var piece in pieces)
                foreach (var renderer in piece.GetComponentsInChildren<Renderer>())
                    y = Mathf.Min(y, renderer.bounds.min.y);
        }
        return new Vector3(wheelPosition.x, y, wheelPosition.z);
    }

    static int PersistSceneOnlyAssets(GameObject root)
    {
        var copies = new Dictionary<Object, Object>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = Persist(filter.sharedMesh, ".asset", copies);
            if (mesh == filter.sharedMesh) continue;
            Undo.RecordObject(filter, "Combine Roulette Table");
            filter.sharedMesh = mesh;
        }
        foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
        {
            var mesh = Persist(collider.sharedMesh, ".asset", copies);
            if (mesh == collider.sharedMesh) continue;
            Undo.RecordObject(collider, "Combine Roulette Table");
            collider.sharedMesh = mesh;
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = Persist(materials[i], ".mat", copies);
                if (material == materials[i]) continue;
                materials[i] = material;
                changed = true;
            }
            if (!changed) continue;
            Undo.RecordObject(renderer, "Combine Roulette Table");
            renderer.sharedMaterials = materials;
        }
        if (copies.Count > 0) AssetDatabase.SaveAssets();
        return copies.Count;
    }

    static T Persist<T>(T source, string extension, Dictionary<Object, Object> copies) where T : Object
    {
        if (source == null || EditorUtility.IsPersistent(source)) return source;
        if (copies.TryGetValue(source, out var existing)) return (T)existing;

        var copy = Object.Instantiate(source);
        string name = string.IsNullOrWhiteSpace(source.name) ? typeof(T).Name : source.name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        copy.name = name;
        AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{name}{extension}"));
        copies[source] = copy;
        return copy;
    }
}
