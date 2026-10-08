using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

// Builds the VR locomotion homework on top of the hand-modelled Maze in the
// Homework scene. What covers each required mechanic:
//   1. Continuous movement - left thumbstick, head-relative (forward = where
//      you look).
//   2. Rotation - snap turn in 45 degree steps on the right thumbstick.
//   3. Teleport to Areas - TeleportationArea on the maze floor and on the
//      ground outside it.
//   4. Teleport to Anchors - checkpoint pads along the maze's solution path,
//      then a floating-platform course over lava past the exit that can only
//      be crossed by teleporting from anchor to anchor.
// 1 and 2 already ship enabled on the Starter Assets XR Origin; this asserts
// that config rather than silently relying on prefab defaults. The rig and
// teleport areas are re-applied on every run; the course is only built if its
// root doesn't exist yet, so tweaks made after the first run aren't stomped -
// delete "Homework Locomotion" and re-run to rebuild it.
public static class SetupHomeworkMaze
{
    const string RigName = "XR Origin (XR Rig)";
    const string MazeName = "Maze";
    const string FloorName = "Floor";
    const string RootName = "Homework Locomotion";
    const string TeleportLayer = "Teleport";
    const string MaterialsFolder = "Assets/Homework/Materials";
    const string AnchorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/Starter Assets/DemoAssets/Prefabs/Teleport/Teleport Anchor.prefab";
    const float SnapTurnDegrees = 45f;

    // Measured from Maze.fbx, in metres, maze-local, Unity axes: a 16x16 grid
    // of 1.25 m cells spanning x,z in [-10, 10] with 6.76 m tall wall blocks,
    // inside an outer wall at +/-10.55. The entrance is an alcove sticking out
    // north (+z) at x in [0, 1.32]; the exit is a funnel opening south (-z)
    // around x = -5.9, z = -12.6.
    const float MazeHalfWidth = 10.5478f;
    const float CellSize = 1.25f;

    static readonly Vector3 SpawnPoint = new Vector3(0.66f, 0f, 12.3f);

    // (row, col): row 0 = north/entrance edge, col 0 = west (-x). All on the
    // solution path, at junctions where a wrong turn is tempting, facing the
    // way the path continues - teleporting onto one turns you the right way.
    static readonly (string name, int row, int col, Vector3 facing)[] MazeCheckpoints =
    {
        ("Checkpoint 1", 4, 3, Vector3.back),   // 4-way junction, path continues south
        ("Checkpoint 2", 9, 2, Vector3.left),   // path turns west
        ("Checkpoint 3", 14, 1, Vector3.right), // last corridor: exit is just east, then south
    };

    // Pad tops in the maze frame. Each hop is ~5 m across and ~0.9 m up, well
    // inside the Starter Assets teleport arc's reach, and the lava is wide
    // enough that the later platforms are out of reach from the ground beside it.
    static readonly (string name, Vector3 top)[] Platforms =
    {
        ("Platform 1", new Vector3(-5.9f, 0.8f, -17.0f)),
        ("Platform 2", new Vector3(-2.6f, 1.7f, -20.8f)),
        ("Platform 3", new Vector3(-6.4f, 2.6f, -24.4f)),
        ("Platform 4", new Vector3(-2.8f, 3.5f, -28.0f)),
    };
    static readonly Vector3 PlatformSize = new Vector3(1.8f, 0.25f, 1.8f);
    static readonly Vector3 GoalTop = new Vector3(-5.6f, 4.4f, -32.2f);
    static readonly Vector3 GoalSize = new Vector3(3.2f, 0.3f, 3.2f);

    // Sits just above the ground (which is 3 cm below the maze floor), leaving
    // a strip of normal ground between the exit funnel and the lava to land on.
    static readonly Vector3 LavaMin = new Vector3(-20f, -0.08f, -40f);
    static readonly Vector3 LavaMax = new Vector3(8f, 0.02f, -14f);
    static readonly Vector3 LavaRespawnPoint = new Vector3(-5.9f, 0f, -13.2f);

    [MenuItem("Tools/Homework/Setup Maze Locomotion")]
    static void Setup()
    {
        var rig = GameObject.Find(RigName);
        var maze = GameObject.Find(MazeName);
        var floor = GameObject.Find(FloorName);
        if (rig == null || maze == null || floor == null)
        {
            Debug.LogError($"SetupHomeworkMaze: needs '{RigName}', '{MazeName}' and '{FloorName}' in the open scene.");
            return;
        }

        var frame = MazeFrame.From(maze);
        if (frame == null) return;

        ConfigureRig(rig);
        ConfigureTeleportArea(maze);
        ConfigureTeleportArea(floor);

        if (GameObject.Find(RootName) != null)
        {
            Debug.Log($"SetupHomeworkMaze: rig and teleport areas re-applied. '{RootName}' already exists, so the course and spawn were left alone - delete it and re-run to rebuild.");
        }
        else if (BuildCourse(frame))
        {
            PlaceSpawn(rig, frame);
            Debug.Log($"SetupHomeworkMaze: built '{RootName}' ({MazeCheckpoints.Length} maze checkpoints, {Platforms.Length} platforms + goal) and moved the XR Origin into the maze entrance.");
        }

        CheckOnMazeFloor(frame);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // Applying a value equal to the prefab's doesn't create an override, so on
    // an untouched Starter Assets rig this changes nothing.
    static void ConfigureRig(GameObject rig)
    {
        var head = rig.transform.Find("Camera Offset/Main Camera");

        var move = rig.GetComponentInChildren<ContinuousMoveProvider>(true);
        if (move != null)
        {
            var so = new SerializedObject(move);
            so.FindProperty("m_ForwardSource").objectReferenceValue = head; // head-relative
            so.ApplyModifiedProperties();
        }
        else Debug.LogWarning("SetupHomeworkMaze: no ContinuousMoveProvider on the rig - continuous movement won't work.");

        var snapTurn = rig.GetComponentInChildren<SnapTurnProvider>(true);
        if (snapTurn != null)
        {
            var so = new SerializedObject(snapTurn);
            so.FindProperty("m_TurnAmount").floatValue = SnapTurnDegrees;
            so.ApplyModifiedProperties();
        }
        else Debug.LogWarning("SetupHomeworkMaze: no SnapTurnProvider on the rig - snap turn won't work.");

        // Decides what each thumbstick does: with smooth motion on, a stick
        // walks; with it off, pushing forward aims the teleport arc and
        // left/right snap-turns.
        foreach (var hand in rig.GetComponentsInChildren<ControllerInputActionManager>(true))
        {
            bool isLeft = hand.gameObject.name.Contains("Left");
            var so = new SerializedObject(hand);
            so.FindProperty("m_SmoothMotionEnabled").boolValue = isLeft;
            so.FindProperty("m_SmoothTurnEnabled").boolValue = false;
            so.ApplyModifiedProperties();
        }
    }

    static void ConfigureTeleportArea(GameObject go)
    {
        var area = go.GetComponent<TeleportationArea>();
        if (area == null) area = Undo.AddComponent<TeleportationArea>(go);

        Undo.RecordObject(area, "Configure Teleportation Area");
        // Only the teleport ray targets this layer, so the grab rays ignore the floor.
        area.interactionLayers = InteractionLayerMask.GetMask(TeleportLayer);
        // Keep facing whichever way you were; turning you is what anchors are for.
        area.matchOrientation = MatchOrientation.WorldSpaceUp;
        area.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
        // The maze is one mesh, so floor and walls share a collider. Only
        // upward-facing hits count, so aiming at a wall shows the invalid
        // (red) arc instead of teleporting you into it.
        area.filterSelectionByHitNormal = true;
        area.upNormalToleranceDegrees = 30f;
    }

    static bool BuildCourse(MazeFrame frame)
    {
        var anchorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnchorPrefabPath);
        if (anchorPrefab == null)
        {
            Debug.LogError($"SetupHomeworkMaze: couldn't load the XRI Starter Assets anchor at '{AnchorPrefabPath}' - is the Starter Assets sample (3.6.0) imported?");
            return false;
        }

        var lavaMat = CreateOrLoadMaterial("Lava", new Color(0.8f, 0.2f, 0.05f), new Color(0.9f, 0.18f, 0f));
        var platformMat = CreateOrLoadMaterial("Floating Platform", new Color(0.35f, 0.4f, 0.5f), Color.black);
        var padMat = CreateOrLoadMaterial("Checkpoint Pad", new Color(0.15f, 0.7f, 1f), new Color(0.1f, 0.45f, 0.9f));
        var goalMat = CreateOrLoadMaterial("Goal", new Color(1f, 0.8f, 0.2f), new Color(1f, 0.65f, 0.1f) * 1.5f);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create " + RootName);

        var signs = CreateChild("Signs", root.transform);
        CreateSign("Start Sign", signs.transform, frame.Point(new Vector3(0.66f, 2.3f, 10.8f)), frame.Facing(Vector3.back),
            "<b>ESCAPE THE MAZE</b>\n" +
            "Left stick: walk where you look\n" +
            "Right stick left/right: snap turn\n" +
            "Right stick forward, aim, release: teleport\n" +
            "Blue pads are checkpoints on the right path",
            new Vector2(1.25f, 0.75f), Color.white);
        CreateSign("Lava Sign", signs.transform, frame.Point(new Vector3(-5.9f, 2.8f, -15.4f)), frame.Facing(Vector3.back),
            "<b>LAVA!</b>\nTeleport across the floating platforms.\nFall in and you're sent back to the last pad.",
            new Vector2(3f, 0.9f), new Color(1f, 0.85f, 0.6f));

        var mazeGroup = CreateChild("Maze Checkpoints", root.transform);
        foreach (var (name, row, col, facing) in MazeCheckpoints)
            CreatePad(name, mazeGroup.transform, anchorPrefab, padMat, frame.Point(CellCentre(row, col)), frame.Facing(facing));

        // Each pad faces the next hop, so landing on one lines you up for the next.
        var course = CreateChild("Sky Course", root.transform);
        var coursePads = new List<CheckpointPad>();
        for (int i = 0; i < Platforms.Length; i++)
        {
            var (name, top) = Platforms[i];
            Vector3 next = i + 1 < Platforms.Length ? Platforms[i + 1].top : GoalTop;
            Vector3 toNext = next - top;
            toNext.y = 0f;

            var platform = CreatePlatform(name, course.transform, frame, top, PlatformSize, platformMat);
            coursePads.Add(CreatePad(name + " Pad", course.transform, anchorPrefab, padMat, frame.Point(top), frame.Facing(toNext),
                platformCollider: platform.GetComponent<Collider>()));
        }

        // The goal pad turns you back toward the maze for the view of what you
        // just got through; the banner appears in front of you when you land.
        var goalPlatform = CreatePlatform("Goal Platform", course.transform, frame, GoalTop, GoalSize, goalMat);
        var banner = CreateSign("You Escaped Banner", course.transform, frame.Point(GoalTop + new Vector3(0f, 2f, 2.6f)), frame.Facing(Vector3.forward),
            "<b>YOU ESCAPED!</b>", new Vector2(3.5f, 1f), new Color(1f, 0.85f, 0.3f));
        coursePads.Add(CreatePad("Goal Pad", course.transform, anchorPrefab, goalMat, frame.Point(GoalTop), frame.Facing(Vector3.forward), banner,
            goalPlatform.GetComponent<Collider>()));
        CreatePrimitive(PrimitiveType.Cylinder, "Goal Beacon", course.transform,
            frame.Point(GoalTop + new Vector3(0f, 1.5f, -1.3f)), frame.Facing(Vector3.forward), new Vector3(0.3f, 1.5f, 0.3f), goalMat);
        CreateSign("Finish Sign", course.transform, frame.Point(GoalTop + new Vector3(0f, 3.5f, -1.3f)), frame.Facing(Vector3.back),
            "<b>FINISH</b>", new Vector2(2.5f, 0.7f), Color.white);

        // A solid collider, not a trigger: the teleport ray ignores triggers,
        // so a trigger would let the arc pass through to the teleportable
        // ground underneath. Solid, it shows the arc as invalid.
        var lava = CreatePrimitive(PrimitiveType.Cube, "Lava", root.transform,
            frame.Point((LavaMin + LavaMax) * 0.5f), frame.Facing(Vector3.forward), LavaMax - LavaMin, lavaMat);
        var respawnPoint = CreateChild("Lava Respawn Point", root.transform);
        respawnPoint.transform.SetPositionAndRotation(frame.Point(LavaRespawnPoint), frame.Facing(Vector3.back));

        var lavaRespawn = lava.AddComponent<LavaRespawn>();
        var so = new SerializedObject(lavaRespawn);
        so.FindProperty("fallbackRespawn").objectReferenceValue = respawnPoint.transform;
        var padsProp = so.FindProperty("coursePads");
        padsProp.arraySize = coursePads.Count;
        for (int i = 0; i < coursePads.Count; i++) padsProp.GetArrayElementAtIndex(i).objectReferenceValue = coursePads[i];
        so.ApplyModifiedProperties();

        return true;
    }

    // The entrance alcove's back wall is single-sided (visible from inside
    // only), and the rig was standing just behind it - looking at an open
    // doorway with a wall in it you can't see. Start inside the alcove instead,
    // facing into the maze.
    static void PlaceSpawn(GameObject rig, MazeFrame frame)
    {
        Undo.RecordObject(rig.transform, "Move XR Origin to maze entrance");
        rig.transform.SetPositionAndRotation(frame.Point(SpawnPoint), frame.Facing(Vector3.back));
        PrefabUtility.RecordPrefabInstancePropertyModifications(rig.transform);
    }

    // The checkpoint cells and spawn are hard-coded from Maze.fbx's layout;
    // this catches them landing inside a wall block instead (the maze was
    // re-modelled, or the import came out mirrored).
    static void CheckOnMazeFloor(MazeFrame frame)
    {
        Physics.SyncTransforms();
        float floorY = frame.Point(Vector3.zero).y;

        var spots = new List<(string name, Vector3 metres)> { ("Spawn", SpawnPoint) };
        foreach (var (name, row, col, _) in MazeCheckpoints) spots.Add((name, CellCentre(row, col)));

        int blocked = 0;
        foreach (var (name, metres) in spots)
        {
            Vector3 p = frame.Point(metres);
            if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f) && hit.point.y > floorY + 0.5f)
            {
                Debug.LogWarning($"SetupHomeworkMaze: '{name}' at {p} is on top of '{hit.collider.name}' {hit.point.y - floorY:F1} m above the maze floor, not in a corridor - has Maze.fbx changed?");
                blocked++;
            }
        }
        if (blocked == 0) Debug.Log("SetupHomeworkMaze: spawn and maze checkpoints all verified on open corridor floor.");
    }

    static Vector3 CellCentre(int row, int col) =>
        new Vector3(-10f + CellSize * (col + 0.5f), 0f, 10f - CellSize * (row + 0.5f));

    static GameObject CreatePlatform(string name, Transform parent, MazeFrame frame, Vector3 top, Vector3 size, Material material)
    {
        return CreatePrimitive(PrimitiveType.Cube, name, parent, frame.Point(top - new Vector3(0f, size.y * 0.5f, 0f)), frame.Facing(Vector3.forward), size, material);
    }

    static CheckpointPad CreatePad(string name, Transform parent, GameObject anchorPrefab, Material material,
        Vector3 position, Quaternion facing, GameObject showWhenReached = null, Collider platformCollider = null)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(anchorPrefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.name = name;
        // The anchor prefab's Match Orientation is Target Up And Forward, so
        // this rotation is also the way the player faces after teleporting here.
        go.transform.SetPositionAndRotation(position, facing);

        // The pad alone is a 1 m disc: an arc aimed at a floating platform
        // tends to clip the platform's edge or side first, which isn't a
        // teleport target, so it reads as "out of range". Registering the
        // whole platform with the anchor makes any hit on it valid - the
        // player still lands on the anchor point, so it stays a fixed-point
        // teleport. An explicit list replaces the default (child colliders),
        // so the pad's own collider is listed too.
        if (platformCollider != null)
        {
            var anchorSo = new SerializedObject(go.GetComponent<TeleportationAnchor>());
            var colliders = anchorSo.FindProperty("m_Colliders");
            var padColliders = go.GetComponentsInChildren<Collider>();
            colliders.arraySize = padColliders.Length + 1;
            for (int i = 0; i < padColliders.Length; i++) colliders.GetArrayElementAtIndex(i).objectReferenceValue = padColliders[i];
            colliders.GetArrayElementAtIndex(padColliders.Length).objectReferenceValue = platformCollider;
            anchorSo.ApplyModifiedProperties();
        }

        var padRenderer = go.GetComponentInChildren<Renderer>();
        if (padRenderer != null) padRenderer.sharedMaterial = material;

        var pad = go.AddComponent<CheckpointPad>();
        var so = new SerializedObject(pad);
        so.FindProperty("padRenderer").objectReferenceValue = padRenderer;
        so.FindProperty("showWhenReached").objectReferenceValue = showWhenReached;
        so.ApplyModifiedProperties();
        return pad;
    }

    static GameObject CreateSign(string name, Transform parent, Vector3 position, Quaternion readerFacing, string text, Vector2 size, Color color)
    {
        var go = CreateChild(name, parent);
        var tmp = go.AddComponent<TextMeshPro>();
        // TextMeshPro's readable face is its local -Z, so its forward has to
        // point the same way the reader is looking.
        go.transform.SetPositionAndRotation(position, readerFacing);
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.2f;
        tmp.fontSizeMax = 10f;
        tmp.rectTransform.sizeDelta = size;
        tmp.ForceMeshUpdate();
        return go;
    }

    static GameObject CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static Material CreateOrLoadMaterial(string name, Color baseColor, Color emission)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets/Homework", "Materials");
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", baseColor);
        if (emission.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // Maze.fbx is authored in centimetres, and depending on import settings
    // the 0.01 ends up baked into the mesh or on the transform. Positions
    // above are written in metres in the maze's own frame and converted here,
    // so they follow the maze if it's moved or rotated.
    class MazeFrame
    {
        Transform _maze;
        float _unitsPerMetre;

        public static MazeFrame From(GameObject maze)
        {
            var filter = maze.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                Debug.LogError($"SetupHomeworkMaze: '{maze.name}' has no mesh on its root - expected the Maze.fbx instance.");
                return null;
            }
            return new MazeFrame { _maze = maze.transform, _unitsPerMetre = mesh.bounds.extents.x / MazeHalfWidth };
        }

        public Vector3 Point(Vector3 metres) => _maze.TransformPoint(metres * _unitsPerMetre);

        // Horizontal facing in the maze's frame, as a world rotation.
        public Quaternion Facing(Vector3 direction)
        {
            Vector3 world = _maze.TransformDirection(direction);
            world.y = 0f;
            return Quaternion.LookRotation(world.normalized, Vector3.up);
        }
    }
}
