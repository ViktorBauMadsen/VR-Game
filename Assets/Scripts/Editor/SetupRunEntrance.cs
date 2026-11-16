using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Builds the casino's front door for starting a run (see RunEntrance): a
// door frame with two door leaves, a sign above it and four spawn points
// outside.
//
//   Tools > Casino > Build Run Entrance
//
// Stand OUTSIDE the casino in the Scene view, looking at the opening you
// want to use, and run it: it is dropped on the floor under the Scene view's
// focus point with its outside (+Z) turned toward the camera. Then move /
// rotate the "Run Entrance" root into the opening and scale its X to fit
// the gap (the opening is 2 m wide, 2.3 m high at scale 1). The four "Spawn"
// children are where players appear - drag them wherever you like. Delete
// the root and re-run to rebuild.
public static class SetupRunEntrance
{
    const string RootName = "Run Entrance";
    const float HalfWidth = 1f, Height = 2.3f, OpenAngle = 105f;

    [MenuItem("Tools/Casino/Build Run Entrance")]
    static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log($"SetupRunEntrance: '{RootName}' already exists (selected it). Delete it and re-run to rebuild.");
            return;
        }

        SetupAvatar.EnsureFolder("Assets/Avatar");
        SetupAvatar.EnsureFolder(SetupAvatar.MaterialsFolder);
        var wood = SetupAvatar.Lit("Blackjack Wood", new Color(0.2f, 0.1f, 0.05f), 0.6f);
        var gold = SetupAvatar.Lit("Blackjack Gold", new Color(0.9f, 0.68f, 0.2f), 0.8f, 0.8f);
        var door = SetupAvatar.Lit("Entrance Door", new Color(0.35f, 0.05f, 0.07f), 0.5f);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build " + RootName);
        SetupAvatar.PlaceAtSceneFocus(root.transform);
        var r = root.transform;

        // Frame.
        SetupAvatar.Box("Post L", r, wood, new Vector3(HalfWidth + 0.06f, Height * 0.5f, 0f), new Vector3(0.12f, Height, 0.16f), true);
        SetupAvatar.Box("Post R", r, wood, new Vector3(-HalfWidth - 0.06f, Height * 0.5f, 0f), new Vector3(0.12f, Height, 0.16f), true);
        SetupAvatar.Box("Lintel", r, wood, new Vector3(0f, Height + 0.06f, 0f), new Vector3(HalfWidth * 2f + 0.24f, 0.12f, 0.16f), false);

        // Two leaves, each on a hinge at its post, swinging into the casino (-Z).
        var hinges = new Transform[2];
        var angles = new float[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f; // +X post first
            var hinge = SetupAvatar.Group(i == 0 ? "Hinge L" : "Hinge R", r);
            hinge.localPosition = new Vector3(side * HalfWidth, 0f, 0f);
            var leaf = SetupAvatar.Box("Door", hinge, door, new Vector3(-side * HalfWidth * 0.5f, Height * 0.5f, 0f),
                new Vector3(HalfWidth - 0.01f, Height - 0.02f, 0.05f), true);
            leaf.gameObject.AddComponent<XRSimpleInteractable>();
            SetupAvatar.Box("Handle", hinge, gold, new Vector3(-side * (HalfWidth - 0.12f), 1.05f, 0.05f), new Vector3(0.04f, 0.3f, 0.05f), false);
            SetupAvatar.Box("Trim", hinge, gold, new Vector3(-side * HalfWidth * 0.5f, Height * 0.5f, 0.027f), new Vector3(HalfWidth * 0.7f, Height * 0.75f, 0.004f), false);
            hinges[i] = hinge;
            angles[i] = -side * OpenAngle;
        }

        // Sign above the door, read from outside (a Quad shows on its -Z side).
        var sign = SetupAvatar.TextQuad("Sign", r, "THE CASINO", Color.white, new Color(0.05f, 0.06f, 0.09f), new Vector2Int(1280, 320));
        sign.transform.localPosition = new Vector3(0f, Height + 0.42f, 0.09f);
        sign.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        sign.transform.localScale = new Vector3(2.2f, 0.55f, 1f);
        SetupAvatar.Box("Sign Board", r, gold, new Vector3(0f, Height + 0.42f, 0.06f), new Vector3(2.3f, 0.65f, 0.04f), false);

        // Spawn points outside, facing the door.
        var spawns = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var spawn = SetupAvatar.Group("Spawn " + (i + 1), r);
            spawn.localPosition = new Vector3((i % 2 == 0 ? 0.55f : -0.55f), 0f, 1.8f + (i / 2) * 1.1f);
            spawn.localRotation = Quaternion.Euler(0f, 180f, 0f);
            spawns[i] = spawn;
        }

        var entrance = root.AddComponent<RunEntrance>();
        var so = new SerializedObject(entrance);
        SetupAvatar.SetArray(so, "spawnPoints", spawns);
        SetupAvatar.SetArray(so, "hinges", hinges);
        var open = so.FindProperty("openAngles");
        open.arraySize = angles.Length;
        for (int i = 0; i < angles.Length; i++) open.GetArrayElementAtIndex(i).floatValue = angles[i];
        so.FindProperty("sign").objectReferenceValue = sign;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupRunEntrance: built '{RootName}' at {root.transform.position}. Move/rotate it into the casino's doorway "
            + "(+Z = outside, where the players wait) and scale X to fit. Save the scene to keep it.");
    }
}
