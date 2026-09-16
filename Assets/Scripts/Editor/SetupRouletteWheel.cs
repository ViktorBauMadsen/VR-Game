using System.IO;
using TMPro;
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
    // Sized so a single pocket (chord ~5.3cm at PocketChordFactor) is
    // comfortably wider than the ball (4cm) - at the original 0.4/0.28/0.24
    // scale a pocket was only ~2.3cm, so the ball straddled several
    // sections at once instead of visibly settling into one.
    const float OuterRadius = 0.73f;
    const float RotorRadius = 0.65f; // close to OuterRadius so the wood floor ring reads as a thin rim, not a dominant band
    const int WallSegments = 48;
    const float WallHeight = 0.18f; // tall enough to actually contain a bouncy ball, not just a slow orbiting one
    const float WallThickness = 0.015f;
    const float FloorThickness = 0.02f;
    const int PocketCount = 37;
    const float FretHeight = 0.035f;
    const float FretThickness = 0.006f;
    const float FretInnerRadius = 0.06f; // small hub, so most of the radius goes to the pockets themselves
    const float RotorDiscThickness = 0.03f;
    const float RecessDepth = 0.03f; // how far below the bowl floor the rotor's pocket ring sits
    const float HubHeight = 0.05f;
    const float PocketRingRadius = 0.62f; // where the walled pocket compartments end - smaller than RotorRadius, leaving a flat unwalled apron the ball crosses first
    const float RimWallHeight = 0.05f;
    const float LidHeight = WallHeight + 0.06f; // just above the wall top, not far above it - a lid placed high above a short wall leaves a wide-open gap a bouncy ball can sail sideways through before ever reaching the lid's height
    const float BallRadius = 0.02f;
    const float StandHeight = 0.9f;
    const float StandRadius = 0.37f; // scaled up with OuterRadius so the pedestal still looks like it's actually holding the (now much bigger) wheel up

    // Pocket number/color decals sitting on top of the rotor disc, between
    // the frets - purely visual, so the ball never touches them.
    const float PocketPlateHeight = 0.002f;
    const float PocketChordFactor = 0.92f; // shrink below the full pocket width so a sliver of the fret divider still shows
    const float PocketFontSizeMax = 6f;

    const string PhysicsFolder = "Assets/Physics";
    const string BallMaterialPath = "Assets/Materials/RouletteBall.mat";
    const string BallPhysMaterialPath = "Assets/Physics/RouletteBallSurface.physicMaterial";
    const string RedMaterialPath = "Assets/Materials/RouletteRed.mat";
    const string GreenMaterialPath = "Assets/Materials/RouletteGreen.mat";
    static readonly Color RedColor = new Color(0.72f, 0.04f, 0.04f);
    static readonly Color GreenColor = new Color(0.02f, 0.42f, 0.16f);

    [MenuItem("Tools/Setup Roulette Wheel")]
    static void Setup()
    {
        // Every dimension (bowl, rotor, frets, stand...) is derived from the
        // constants above, so short of hand-patching dozens of child
        // transforms the only reliable way to pick up a constant change is
        // to rebuild the whole thing - preserving wherever the old one was
        // placed in the scene so a re-run doesn't punt it back to the origin.
        Transform existingParent = null;
        Vector3 localPos = new Vector3(0f, StandHeight, 0f);
        Quaternion localRot = Quaternion.identity;
        Vector3 localScale = Vector3.one;

        var existingRoot = GameObject.Find("RouletteWheel");
        if (existingRoot != null)
        {
            existingParent = existingRoot.transform.parent;
            localPos = existingRoot.transform.localPosition;
            localRot = existingRoot.transform.localRotation;
            localScale = existingRoot.transform.localScale;
            Undo.DestroyObjectImmediate(existingRoot);
        }

        var woodMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Table.mat");
        var darkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Black.mat");
        var ballMat = CreateOrLoadWhiteMaterial();
        var physMat = CreateOrLoadBallPhysicsMaterial();
        var redMat = CreateOrLoadColorMaterial(RedMaterialPath, "RouletteRed", RedColor);
        var greenMat = CreateOrLoadColorMaterial(GreenMaterialPath, "RouletteGreen", GreenColor);

        var root = new GameObject("RouletteWheel");
        Undo.RegisterCreatedObjectUndo(root, "Create Roulette Wheel");
        root.transform.SetParent(existingParent, false);
        root.transform.localPosition = localPos;
        root.transform.localRotation = localRot;
        root.transform.localScale = localScale;

        BuildStand(root.transform, woodMat);
        var rotor = BuildRotor(root.transform, darkMat, darkMat, physMat);
        var pockets = BuildPockets(rotor.transform, darkMat, redMat, greenMat);
        BuildBowl(root.transform, woodMat, physMat);
        BuildLid(root.transform, physMat);
        var ballObj = BuildBall(root.transform, ballMat, physMat);
        var buttonObj = BuildSpinButton(root.transform, darkMat, physMat);

        float pocketRingRadius = AveragePocketRadius(pockets, root.transform.position);

        var wheel = root.AddComponent<RouletteWheel>();
        var wheelSo = new SerializedObject(wheel);
        wheelSo.FindProperty("ball").objectReferenceValue = ballObj.GetComponent<RouletteBall>();
        wheelSo.FindProperty("ballStartRadius").floatValue = OuterRadius - WallThickness - BallRadius - 0.01f;
        wheelSo.FindProperty("ballSpawnHeight").floatValue = BallRadius;
        AssignPockets(wheelSo, pockets);
        wheelSo.ApplyModifiedProperties();

        var ballComp = ballObj.GetComponent<RouletteBall>();
        var ballSo = new SerializedObject(ballComp);
        ballSo.FindProperty("wheelCenter").objectReferenceValue = root.transform;
        ballSo.FindProperty("pocketRingRadius").floatValue = pocketRingRadius;
        ballSo.ApplyModifiedProperties();

        var buttonSo = new SerializedObject(buttonObj.GetComponent<RouletteSpinButton>());
        buttonSo.FindProperty("wheel").objectReferenceValue = wheel;
        buttonSo.ApplyModifiedProperties();

        BuildResultLabel(root.transform, wheel);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log("Roulette wheel (re)created - if one already existed its scene placement was preserved, otherwise it was created at the world origin. Save the scene, then Play and press the SpinButton (or right-click the RouletteWheel component and choose 'Debug Spin') to test. Note: this rebuilds from scratch, so any manual Inspector tweaks on a previous wheel (spin speed, etc.) were reset to script defaults.");
    }

    static float AveragePocketRadius(RoulettePocket[] pockets, Vector3 center)
    {
        float sum = 0f;
        foreach (var pocket in pockets)
        {
            Vector3 flat = pocket.WorldCenter - center;
            flat.y = 0f;
            sum += flat.magnitude;
        }
        return sum / pockets.Length;
    }

    static void AssignPockets(SerializedObject wheelSo, RoulettePocket[] pockets)
    {
        var prop = wheelSo.FindProperty("pockets");
        prop.arraySize = pockets.Length;
        for (int i = 0; i < pockets.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = pockets[i];
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

        // Rim wall - this is what actually contains a bouncy ball; the
        // height drop alone (RecessDepth) only stops a slow, non-bouncy one.
        // Sits at PocketRingRadius rather than the full RotorRadius, leaving
        // a flat, unwalled apron between the two: the ball crosses that
        // after dropping off the bowl floor, shedding a bit more speed
        // before it ever reaches a wall, so entry isn't a hard collision.
        var rimWall = CreateChild("RimWall", rotor.transform);
        BuildRing(rimWall.transform, "Segment_", WallSegments, PocketRingRadius - WallThickness * 0.5f,
            WallThickness, RimWallHeight, RimWallHeight * 0.5f, fretMaterial, physMat);

        float pocketSize = 360f / PocketCount;
        float fretMidRadius = (FretInnerRadius + PocketRingRadius) * 0.5f;
        float fretLength = PocketRingRadius - FretInnerRadius;
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

    // One colored plate + number label per pocket, positioned in the exact
    // same angle*index layout the frets use (angle = i * pocketSize, so
    // pocket i sits centered between the dividers at (i-0.5) and
    // (i+0.5) * pocketSize). Each plate carries a RoulettePocket so
    // RouletteWheel can read the number straight off the section the ball
    // lands in instead of recomputing it from a parallel formula.
    static RoulettePocket[] BuildPockets(Transform rotor, Material blackMaterial, Material redMaterial, Material greenMaterial)
    {
        var existing = rotor.Find("Pockets");
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var pocketsRoot = CreateChild("Pockets", rotor);

        var order = RouletteWheel.WheelOrder;
        float pocketSize = 360f / order.Length;
        float midRadius = (FretInnerRadius + PocketRingRadius) * 0.5f;
        float radialLength = PocketRingRadius - FretInnerRadius;
        float chord = 2f * midRadius * Mathf.Sin(pocketSize * Mathf.Deg2Rad * 0.5f) * PocketChordFactor;

        var pockets = new RoulettePocket[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            int number = order[i];
            Material plateMat = number == 0 ? greenMaterial : (RouletteWheel.IsRed(number) ? redMaterial : blackMaterial);

            float angle = i * pocketSize;
            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector3 platePos = dir * midRadius + Vector3.up * (PocketPlateHeight * 0.5f);
            Quaternion plateRot = Quaternion.FromToRotation(Vector3.right, dir);

            var plate = CreatePrimitiveChild(PrimitiveType.Cube, $"Pocket_{i}_{number}", pocketsRoot.transform,
                platePos, plateRot, new Vector3(radialLength, PocketPlateHeight, chord), plateMat, null);
            Object.DestroyImmediate(plate.GetComponent<Collider>()); // decal only - the ball must never touch it

            var pocket = plate.AddComponent<RoulettePocket>();
            pocket.Number = number;
            pockets[i] = pocket;

            BuildPocketLabel(pocketsRoot.transform, i, number, dir, midRadius, chord);
        }

        return pockets;
    }

    // Lies the number flat on top of its pocket, facing up, with the top of
    // the digit pointing radially outward (the way numbers read on a real
    // wheel when standing at the table looking in).
    static void BuildPocketLabel(Transform parent, int index, int number, Vector3 dir, float radius, float boxSize)
    {
        var labelObj = new GameObject($"Label_{index}_{number}");
        Undo.RegisterCreatedObjectUndo(labelObj, "Create Pocket Label");
        labelObj.transform.SetParent(parent, false);
        labelObj.transform.localPosition = dir * radius + Vector3.up * (PocketPlateHeight + 0.001f);
        // TextMeshPro's readable face is its local -Z, not +Z, so "forward"
        // has to aim down for the text to actually face up toward a
        // top-down viewer - aiming it up instead (the naive choice) shows
        // the back of the glyphs, which renders mirrored.
        labelObj.transform.localRotation = Quaternion.LookRotation(Vector3.down, dir);

        var text = labelObj.AddComponent<TextMeshPro>();
        text.alignment = TextAlignmentOptions.Center;
        text.text = number.ToString();
        text.color = Color.white;
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.5f;
        text.fontSizeMax = PocketFontSizeMax;
        text.rectTransform.sizeDelta = new Vector2(boxSize * 0.9f, boxSize * 0.9f);
        text.ForceMeshUpdate();
    }

    // An invisible flat collider well above the bowl wall, as a backstop in
    // case a bounce ever gets enough vertical energy to threaten clearing
    // the wall - no renderer, so it never shows up in-game.
    static void BuildLid(Transform parent, PhysicsMaterial physMat)
    {
        var lid = CreateChild("Lid", parent);
        lid.transform.localPosition = new Vector3(0f, LidHeight, 0f);
        var collider = lid.AddComponent<BoxCollider>();
        collider.size = new Vector3(OuterRadius * 2.2f, 0.02f, OuterRadius * 2.2f);
        collider.sharedMaterial = physMat;
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
        return CreateOrLoadColorMaterial(BallMaterialPath, "RouletteBall", Color.white);
    }

    static Material CreateOrLoadColorMaterial(string path, string name, Color color)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // Always re-applies the tuning values, even if the asset already exists
    // from an earlier run - these numbers are expected to need iterating on,
    // and re-running Setup shouldn't require deleting the asset by hand
    // every time just to pick up a constant change.
    static PhysicsMaterial CreateOrLoadBallPhysicsMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallPhysMaterialPath);
        if (mat == null)
        {
            if (!Directory.Exists(PhysicsFolder)) Directory.CreateDirectory(PhysicsFolder);
            mat = new PhysicsMaterial("RouletteBallSurface");
            AssetDatabase.CreateAsset(mat, BallPhysMaterialPath);
        }

        mat.dynamicFriction = 0.15f;
        mat.staticFriction = 0.2f;
        mat.bounciness = 0.75f;
        mat.frictionCombine = PhysicsMaterialCombine.Average;
        mat.bounceCombine = PhysicsMaterialCombine.Maximum;
        EditorUtility.SetDirty(mat);

        return mat;
    }
}
