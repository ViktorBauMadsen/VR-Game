using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Builds a darts corner (see Dartboard): a wooden cabinet with the board at
// regulation height, a scoreboard above it, a holder with three darts, a
// button that brings the darts back, and a line on the floor to throw from.
//
//   Tools > Casino > Build Darts
//
// Look at a wall in the Scene view (wall in the middle of the view) and run
// it: the cabinet is hung on that wall. With nothing solid in the middle of
// the view it is stood on the floor under the Scene view's focus point,
// facing the camera. Move/rotate the "Darts" root freely afterwards - you
// throw at its +Z side. Delete it and re-run to rebuild.
public static class SetupDarts
{
    const string RootName = "Darts";
    const float BullHeight = 1.73f; // regulation
    const float Oche = 2.37f;       // regulation throwing distance

    [MenuItem("Tools/Casino/Build Darts")]
    static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log($"SetupDarts: '{RootName}' already exists (selected it). Delete it and re-run to rebuild.");
            return;
        }

        SetupAvatar.EnsureFolder("Assets/Avatar");
        SetupAvatar.EnsureFolder(SetupAvatar.MaterialsFolder);
        var wood = SetupAvatar.Lit("Blackjack Wood", new Color(0.2f, 0.1f, 0.05f), 0.6f);
        var gold = SetupAvatar.Lit("Blackjack Gold", new Color(0.9f, 0.68f, 0.2f), 0.8f, 0.8f);
        var black = SetupAvatar.Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f);
        var steel = SetupAvatar.Lit("Dart Steel", new Color(0.7f, 0.72f, 0.76f), 0.85f, 0.9f);
        var white = SetupAvatar.Lit("Dart Line", new Color(0.92f, 0.92f, 0.9f), 0.2f);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build " + RootName);
        bool onWall = PlaceOnWallInView(root.transform);
        if (!onWall) SetupAvatar.PlaceAtSceneFocus(root.transform);
        var r = root.transform;

        // ----- Cabinet (everything sits in front of z = 0, the wall)
        SetupAvatar.Box("Cabinet", r, wood, new Vector3(0f, BullHeight, 0.03f), new Vector3(1.1f, 1.1f, 0.06f), true);
        SetupAvatar.Box("Cabinet Trim", r, gold, new Vector3(0f, BullHeight, 0.012f), new Vector3(1.16f, 1.16f, 0.024f), false);
        if (!onWall)
        {
            SetupAvatar.Box("Post L", r, wood, new Vector3(0.45f, (BullHeight - 0.55f) * 0.5f, 0.03f), new Vector3(0.08f, BullHeight - 0.55f, 0.06f), true);
            SetupAvatar.Box("Post R", r, wood, new Vector3(-0.45f, (BullHeight - 0.55f) * 0.5f, 0.03f), new Vector3(0.08f, BullHeight - 0.55f, 0.06f), true);
        }

        // ----- The board: a dark disc with a thin collider; Dartboard paints its face at runtime.
        var boardObject = new GameObject("Dartboard");
        boardObject.transform.SetParent(r, false);
        boardObject.transform.localPosition = new Vector3(0f, BullHeight, 0.1f);
        var disc = SetupAvatar.Part("Disc", PrimitiveType.Cylinder, boardObject.transform, black, new Vector3(0f, 0f, -0.02f), new Vector3(0.68f, 0.018f, 0.68f));
        disc.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var boardCollider = boardObject.AddComponent<BoxCollider>();
        boardCollider.center = new Vector3(0f, 0f, -0.02f);
        boardCollider.size = new Vector3(0.68f, 0.68f, 0.04f);

        // ----- Scoreboard above
        var dark = new Color(0.03f, 0.05f, 0.04f);
        var score = SetupAvatar.TextQuad("Scoreboard", r, "DARTS", Color.white, dark, new Vector2Int(1024, 512));
        score.transform.localPosition = new Vector3(0f, BullHeight + 0.82f, 0.05f);
        score.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up); // a Quad shows on its -Z side
        score.transform.localScale = new Vector3(0.9f, 0.45f, 1f);
        SetupAvatar.Box("Scoreboard Frame", r, gold, new Vector3(0f, BullHeight + 0.82f, 0.025f), new Vector3(0.98f, 0.53f, 0.04f), false);

        // ----- Holder with three darts, to the player's right of the board (-X), points down
        var holder = SetupAvatar.Box("Dart Holder", r, wood, new Vector3(-0.8f, 1.15f, 0.12f), new Vector3(0.3f, 0.08f, 0.2f), true);
        Color[] flights = { new Color(0.8f, 0.1f, 0.12f), new Color(0.1f, 0.35f, 0.85f), new Color(0.95f, 0.75f, 0.1f) };
        var darts = new Dart[flights.Length];
        for (int i = 0; i < darts.Length; i++)
            darts[i] = BuildDart(r, i, new Vector3(-0.8f + (i - 1) * 0.09f, 1.15f + 0.04f + 0.085f, 0.12f), steel, black,
                SetupAvatar.Lit("Dart Flight " + (i + 1), flights[i], 0.4f));

        // ----- "Bring the darts back" button under the holder
        var station = SetupAvatar.Group("Return Button", r);
        station.localPosition = new Vector3(-0.8f, 0.95f, 0.1f);
        SetupAvatar.Box("Plate", station, wood, Vector3.zero, new Vector3(0.22f, 0.22f, 0.04f), false);
        var button = new GameObject("Button");
        button.transform.SetParent(station, false);
        button.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        var box = button.AddComponent<BoxCollider>();
        box.size = new Vector3(0.14f, 0.14f, 0.06f);
        var cap = SetupAvatar.Part("Cap", PrimitiveType.Cylinder, button.transform, SetupAvatar.Lit("Dart Button", new Color(0.8f, 0.15f, 0.12f), 0.7f),
            Vector3.zero, new Vector3(0.12f, 0.015f, 0.12f));
        cap.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var label = SetupAvatar.TextQuad("Label", station, "DARTS BACK", Color.white, dark, new Vector2Int(384, 96));
        label.transform.localPosition = new Vector3(0f, -0.16f, 0.022f);
        label.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        label.transform.localScale = new Vector3(0.24f, 0.06f, 1f);

        // ----- Throw line on the floor
        SetupAvatar.Box("Throw Line", r, white, new Vector3(0f, 0.006f, Oche), new Vector3(1.2f, 0.012f, 0.05f), false);

        // The button goes under the board object so the board finds it.
        station.SetParent(boardObject.transform, true);
        button.AddComponent<XRSimpleInteractable>();

        var board = boardObject.AddComponent<Dartboard>();
        var so = new SerializedObject(board);
        var list = so.FindProperty("darts");
        list.arraySize = darts.Length;
        for (int i = 0; i < darts.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = darts[i];
        so.FindProperty("scoreboard").objectReferenceValue = score;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupDarts: built '{RootName}' at {root.transform.position} "
            + (onWall ? "on the wall in the middle of the Scene view" : "standing on the floor (no wall found in the middle of the Scene view)")
            + $". You throw at its +Z side; the white line is {Oche} m from the board. Save the scene to keep it.");
    }

    // A dart pointing along its own +Z, standing point-down in the holder.
    static Dart BuildDart(Transform root, int index, Vector3 localPosition, Material steel, Material black, Material flight)
    {
        var go = new GameObject("Dart " + (index + 1));
        var t = go.transform;
        t.SetParent(root, false);
        t.localPosition = localPosition;
        t.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        var along = Quaternion.Euler(90f, 0f, 0f); // Unity's cylinder runs along Y; turn it along Z
        SetupAvatar.Part("Point", PrimitiveType.Cylinder, t, steel, new Vector3(0f, 0f, 0.072f), new Vector3(0.004f, 0.023f, 0.004f)).localRotation = along;
        SetupAvatar.Part("Barrel", PrimitiveType.Cylinder, t, steel, new Vector3(0f, 0f, 0.02f), new Vector3(0.013f, 0.03f, 0.013f)).localRotation = along;
        SetupAvatar.Part("Shaft", PrimitiveType.Cylinder, t, black, new Vector3(0f, 0f, -0.04f), new Vector3(0.006f, 0.03f, 0.006f)).localRotation = along;
        SetupAvatar.Box("Flight A", t, flight, new Vector3(0f, 0f, -0.08f), new Vector3(0.045f, 0.0015f, 0.04f), false);
        SetupAvatar.Box("Flight B", t, flight, new Vector3(0f, 0f, -0.08f), new Vector3(0.0015f, 0.045f, 0.04f), false);

        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.direction = 2; // along Z
        capsule.center = new Vector3(0f, 0f, 0f);
        capsule.radius = 0.012f;
        capsule.height = 0.19f;

        var body = go.AddComponent<Rigidbody>();
        body.mass = 0.05f;
        body.angularDamping = 2f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        go.AddComponent<XRGrabInteractable>();
        return go.AddComponent<Dart>();
    }

    // Hangs the root on whatever upright surface is in the middle of the
    // Scene view: origin at floor level against the wall, +Z out of the wall.
    static bool PlaceOnWallInView(Transform root)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null) return false;
        var cam = view.camera.transform;
        if (!Physics.Raycast(cam.position, cam.forward, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (Mathf.Abs(hit.normal.y) > 0.3f) return false; // floor, ceiling or a slope - not a wall

        Vector3 outward = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
        Vector3 foot = hit.point + outward * 0.3f;
        float floor = Physics.Raycast(foot + Vector3.up * 0.2f, Vector3.down, out var ground, 20f, ~0, QueryTriggerInteraction.Ignore)
            ? ground.point.y : hit.point.y - BullHeight;
        root.SetPositionAndRotation(new Vector3(hit.point.x, floor, hit.point.z) + outward * 0.005f,
            Quaternion.LookRotation(outward, Vector3.up));
        return true;
    }
}
