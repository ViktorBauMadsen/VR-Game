using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Builds a roulette wheel entirely from primitives - no art asset exists
// for one yet. A static outer bowl wall + floor is what the ball orbits on;
// the rotor is a separate kinematic-Rigidbody disc with 37 radial fret
// dividers in the standard European wheel order, which is what actually
// corrals the ball into a pocket once it drops onto the rotor. See
// RouletteWheel/RouletteBall for the simulation itself - this script only
// exists to avoid hand-placing 37 pockets.
public static class SetupRouletteWheel
{
    const float OuterRadius = 0.4f;
    const float RotorRadius = 0.28f;
    const int WallSegments = 48;
    const float WallHeight = 0.06f;
    const float WallThickness = 0.015f;
    const float FloorThickness = 0.02f;
    const int PocketCount = 37;
    const float FretHeight = 0.035f;
    const float FretThickness = 0.006f;
    const float FretInnerRadius = 0.05f;
    const float RotorDiscThickness = 0.03f;
    const float RecessDepth = 0.03f; // how far below the bowl floor the rotor's pocket ring sits
    const float HubHeight = 0.05f;
    const float BallRadius = 0.02f;
    const float StandHeight = 0.9f;
    const float StandRadius = 0.2f;

    const string PhysicsFolder = "Assets/Physics";
    const string BallMaterialPath = "Assets/Materials/RouletteBall.mat";
    const string BallPhysMaterialPath = "Assets/Physics/RouletteBallSurface.physicMaterial";

    [MenuItem("Tools/Setup Roulette Wheel")]
    static void Setup()
    {
        if (GameObject.Find("RouletteWheel") != null)
        {
            Debug.LogError("SetupRouletteWheel: a 'RouletteWheel' already exists in the open scene.");
            return;
        }

        var woodMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Table.mat");
        var darkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Black.mat");
        var ballMat = CreateOrLoadWhiteMaterial();
        var physMat = CreateOrLoadBallPhysicsMaterial();

        var root = new GameObject("RouletteWheel");
        Undo.RegisterCreatedObjectUndo(root, "Create Roulette Wheel");
        root.transform.position = new Vector3(0f, StandHeight, 0f);

        BuildStand(root.transform, woodMat);
        var rotor = BuildRotor(root.transform, darkMat, darkMat, physMat);
        BuildBowl(root.transform, woodMat, physMat);
        var ballObj = BuildBall(root.transform, ballMat, physMat);
        var buttonObj = BuildSpinButton(root.transform, darkMat, physMat);

        var wheel = root.AddComponent<RouletteWheel>();
        var wheelSo = new SerializedObject(wheel);
        wheelSo.FindProperty("rotor").objectReferenceValue = rotor.transform;
        wheelSo.FindProperty("ball").objectReferenceValue = ballObj.GetComponent<RouletteBall>();
        wheelSo.FindProperty("ballStartRadius").floatValue = OuterRadius - WallThickness - BallRadius - 0.01f;
        wheelSo.FindProperty("ballTrackHeight").floatValue = BallRadius;
        wheelSo.ApplyModifiedProperties();

        var ballComp = ballObj.GetComponent<RouletteBall>();
        var ballSo = new SerializedObject(ballComp);
        ballSo.FindProperty("wheelCenter").objectReferenceValue = root.transform;
        ballSo.FindProperty("rotorRadius").floatValue = RotorRadius;
        ballSo.ApplyModifiedProperties();

        var buttonSo = new SerializedObject(buttonObj.GetComponent<RouletteSpinButton>());
        buttonSo.FindProperty("wheel").objectReferenceValue = wheel;
        buttonSo.ApplyModifiedProperties();

        BuildResultLabel(root.transform, wheel);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log("Roulette wheel created at the world origin - reposition it into the casino layout, save the scene, then Play and press the SpinButton (or right-click the RouletteWheel component and choose 'Debug Spin') to test.");
    }

    static void BuildStand(Transform parent, Material material)
    {
        CreatePrimitiveChild(PrimitiveType.Cylinder, "Stand", parent,
            new Vector3(0f, -StandHeight / 2f, 0f), Quaternion.identity,
            new Vector3(StandRadius * 2f, StandHeight / 2f, StandRadius * 2f),
            material, null);
    }

    static void BuildBowl(Transform parent, Material woodMaterial, PhysicsMaterial physMat)
    {
        var bowl = CreateChild("Bowl", parent);

        // Annular floor (a ring of segments, not a solid disc) - there needs
        // to be a real hole above the recessed rotor so the ball actually
        // drops through onto the pocket ring instead of just coasting past
        // on a solid surface forever.
        var floorRing = CreateChild("FloorRing", bowl.transform);
        float floorMidRadius = (RotorRadius + OuterRadius) * 0.5f;
        float floorWidth = OuterRadius - RotorRadius;
        BuildRing(floorRing.transform, "Segment_", WallSegments, floorMidRadius,
            floorWidth, FloorThickness, -FloorThickness * 0.5f, woodMaterial, physMat);

        var wall = CreateChild("Wall", bowl.transform);
        BuildRing(wall.transform, "Segment_", WallSegments, OuterRadius - WallThickness * 0.5f,
            WallThickness, WallHeight, WallHeight * 0.5f, woodMaterial, physMat);
    }

    // A ring of thin box segments approximating a circle - used for both the
    // bowl's outer wall (standing tall, thin radially) and its floor
    // (lying flat, wide radially). LookRotation aligns each box's local Z
    // (its "thickness"/radial axis before scaling) to point outward, so its
    // width automatically runs tangentially.
    static void BuildRing(Transform parent, string namePrefix, int segments, float radius,
        float radialThickness, float height, float centerHeight, Material material, PhysicsMaterial physMat)
    {
        float segAngle = 360f / segments;
        float chord = 2f * radius * Mathf.Sin(segAngle * Mathf.Deg2Rad * 0.5f) * 1.15f;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * segAngle;
            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector3 pos = dir * radius + Vector3.up * centerHeight;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            CreatePrimitiveChild(PrimitiveType.Cube, namePrefix + i, parent,
                pos, rot, new Vector3(chord, height, radialThickness), material, physMat);
        }
    }

    static GameObject BuildRotor(Transform parent, Material discMaterial, Material fretMaterial, PhysicsMaterial physMat)
    {
        var rotor = CreateChild("Rotor", parent);
        rotor.transform.localPosition = new Vector3(0f, -RecessDepth, 0f);

        var rb = rotor.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var disc = CreatePrimitiveChild(PrimitiveType.Cylinder, "Disc", rotor.transform,
            new Vector3(0f, -RotorDiscThickness / 2f, 0f), Quaternion.identity,
            new Vector3(RotorRadius * 2f, RotorDiscThickness / 2f, RotorRadius * 2f),
            discMaterial, physMat);
        UseConvexMeshCollider(disc);

        // Central hub - blocks the ball from ever drifting into the "no
        // frets" zone near the axis, forcing it to settle in one of the 37
        // outer wedges instead of wandering near the middle indefinitely.
        // Doubles as the raised decorative spindle a real wheel has there.
        var hub = CreatePrimitiveChild(PrimitiveType.Cylinder, "Hub", rotor.transform,
            new Vector3(0f, HubHeight / 2f, 0f), Quaternion.identity,
            new Vector3(FretInnerRadius * 2f, HubHeight / 2f, FretInnerRadius * 2f),
            fretMaterial, physMat);
        UseConvexMeshCollider(hub);

        float pocketSize = 360f / PocketCount;
        float fretMidRadius = (FretInnerRadius + RotorRadius) * 0.5f;
        float fretLength = RotorRadius - FretInnerRadius;
        for (int i = 0; i < PocketCount; i++)
        {
            float angle = (i + 0.5f) * pocketSize;
            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector3 pos = dir * fretMidRadius + Vector3.up * (FretHeight * 0.5f);
            Quaternion rot = Quaternion.FromToRotation(Vector3.right, dir);
            CreatePrimitiveChild(PrimitiveType.Cube, "Fret_" + i, rotor.transform,
                pos, rot, new Vector3(fretLength, FretHeight, FretThickness),
                fretMaterial, physMat);
        }

        return rotor;
    }

    static GameObject BuildBall(Transform parent, Material ballMaterial, PhysicsMaterial physMat)
    {
        var ball = CreatePrimitiveChild(PrimitiveType.Sphere, "Ball", parent,
            new Vector3(0f, BallRadius, OuterRadius * 0.8f), Quaternion.identity,
            Vector3.one * (BallRadius * 2f), ballMaterial, physMat);

        var rb = ball.AddComponent<Rigidbody>();
        rb.mass = 0.02f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // The ball can move fast enough to tunnel through the thin fret/wall
        // colliders in a single discrete step; it never needs to collide
        // with other dynamic bodies, so plain Continuous (not
        // ContinuousDynamic) is enough to catch that against the
        // static/kinematic geometry.
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        ball.AddComponent<RouletteBall>();
        return ball;
    }

    static GameObject BuildSpinButton(Transform parent, Material material, PhysicsMaterial physMat)
    {
        var buttonObj = CreatePrimitiveChild(PrimitiveType.Cylinder, "SpinButton", parent,
            new Vector3(OuterRadius + StandRadius + 0.15f, -StandHeight * 0.35f, 0f), Quaternion.identity,
            new Vector3(0.08f, 0.01f, 0.08f), material, physMat);

        buttonObj.AddComponent<XRSimpleInteractable>();
        buttonObj.AddComponent<RouletteSpinButton>();
        return buttonObj;
    }

    static void BuildResultLabel(Transform parent, RouletteWheel wheel)
    {
        var labelRoot = CreateChild("ResultLabel", parent);
        labelRoot.transform.localPosition = new Vector3(0f, 0.35f, -(OuterRadius + 0.1f));
        labelRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var display = labelRoot.AddComponent<RouletteResultDisplay>();
        var so = new SerializedObject(display);
        so.FindProperty("wheel").objectReferenceValue = wheel;
        so.ApplyModifiedProperties();
    }

    static GameObject CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject CreatePrimitiveChild(PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Quaternion localRot, Vector3 localScale, Material material, PhysicsMaterial physMaterial)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;

        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;

        var collider = go.GetComponent<Collider>();
        if (physMaterial != null && collider != null) collider.sharedMaterial = physMaterial;

        return go;
    }

    static void UseConvexMeshCollider(GameObject go)
    {
        var old = go.GetComponent<Collider>();
        var sharedMat = old != null ? old.sharedMaterial : null;
        if (old != null) Object.DestroyImmediate(old);

        var mc = go.AddComponent<MeshCollider>();
        mc.convex = true;
        mc.sharedMaterial = sharedMat;
    }

    static Material CreateOrLoadWhiteMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BallMaterialPath);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader) { name = "RouletteBall" };
        mat.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(mat, BallMaterialPath);
        return mat;
    }

    static PhysicsMaterial CreateOrLoadBallPhysicsMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallPhysMaterialPath);
        if (mat != null) return mat;

        if (!Directory.Exists(PhysicsFolder)) Directory.CreateDirectory(PhysicsFolder);

        mat = new PhysicsMaterial("RouletteBallSurface")
        {
            dynamicFriction = 0.15f,
            staticFriction = 0.2f,
            bounciness = 0.3f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Average
        };
        AssetDatabase.CreateAsset(mat, BallPhysMaterialPath);
        return mat;
    }
}
