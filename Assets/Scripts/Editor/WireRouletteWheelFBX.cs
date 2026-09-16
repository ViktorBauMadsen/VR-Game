using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// One-time integration: takes the artist-made roulette wheel FBX (already
// dragged into the scene, scaled/positioned where it belongs, its 37 number
// sections named "Pocket0".."Pocket36") and wires it up to run through the
// same RouletteWheel/RouletteBall simulation the primitive wheel used -
// grouping the pockets under a spinning "Rotor", adding colliders built
// from their real divider geometry, and building the invisible-collision
// static bowl the ball orbits in before dropping onto them (the FBX has no
// bowl/track of its own, only the wheelhead + a stand). The old primitive
// wheel, if one is present, is disabled rather than deleted so you can
// compare or revert.
//
// Run this once, with the FBX's root object selected in the Hierarchy.
public static class WireRouletteWheelFBX
{
    static readonly Regex PocketNamePattern = new Regex(@"^Pocket(\d+)$");

    const int WallSegments = 48;
    const float WallHeight = 0.18f;
    const float WallThickness = 0.015f;
    const float FloorThickness = 0.02f;
    const float LidMargin = 0.06f;
    const float BallRadius = 0.02f;
    const float BowlMarginFactor = 1.2f; // static bowl wall sits this far beyond the measured pocket radius
    const float HubRadiusFactor = 0.1f;  // small center plug, as a fraction of the measured pocket radius - the FBX has no turret/spindle of its own

    const string BallMaterialPath = "Assets/Materials/RouletteBall.mat";
    const string BallPhysMaterialPath = "Assets/Physics/RouletteBallSurface.physicMaterial";

    [MenuItem("Tools/Wire Up Selected Roulette Wheel FBX")]
    static void Wire()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            Debug.LogError("WireRouletteWheelFBX: select the roulette wheel FBX's root object in the Hierarchy first, then run this again.");
            return;
        }

        var pocketTransforms = new List<(int number, Transform t)>();
        foreach (Transform child in root.transform)
        {
            var match = PocketNamePattern.Match(child.name);
            if (match.Success)
                pocketTransforms.Add((int.Parse(match.Groups[1].Value), child));
        }

        if (pocketTransforms.Count != 37)
        {
            Debug.LogError($"WireRouletteWheelFBX: found {pocketTransforms.Count} direct children of '{root.name}' named 'Pocket<number>', expected 37. Check the FBX's hierarchy/naming and try again.");
            return;
        }

        var ballPhysMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallPhysMaterialPath);
        if (ballPhysMat == null)
        {
            Debug.LogError($"WireRouletteWheelFBX: couldn't find {BallPhysMaterialPath} - run 'Tools > Setup Roulette Wheel' once first so that asset exists (you can disable its output afterward), then re-run this.");
            return;
        }

        Undo.SetCurrentGroupName("Wire Up Roulette Wheel FBX");
        int undoGroup = Undo.GetCurrentGroup();

        // Group the 37 numbered pockets under their own Rotor so a spin only
        // turns the wheelhead, not the stand sitting alongside it.
        var rotorObj = new GameObject("Rotor");
        Undo.RegisterCreatedObjectUndo(rotorObj, "Create Rotor");
        rotorObj.transform.SetParent(root.transform, false);

        var rotorRb = Undo.AddComponent<Rigidbody>(rotorObj);
        rotorRb.isKinematic = true;
        rotorRb.useGravity = false;

        var pockets = new RoulettePocket[pocketTransforms.Count];
        Bounds bounds = default;
        bool boundsInit = false;

        for (int i = 0; i < pocketTransforms.Count; i++)
        {
            var (number, t) = pocketTransforms[i];
            Undo.SetTransformParent(t, rotorObj.transform, "Reparent Pocket");

            var pocket = t.GetComponent<RoulettePocket>();
            if (pocket == null) pocket = Undo.AddComponent<RoulettePocket>(t.gameObject);
            pocket.Number = number;
            pockets[i] = pocket;

            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (!boundsInit) { bounds = renderer.bounds; boundsInit = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (t.GetComponent<Collider>() == null)
            {
                var meshFilter = t.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    var mc = Undo.AddComponent<MeshCollider>(t.gameObject);
                    mc.sharedMesh = meshFilter.sharedMesh;
                    mc.convex = false; // fine here - the Rigidbody this sits under (Rotor) is kinematic
                    mc.sharedMaterial = ballPhysMat;
                }
                else
                {
                    Debug.LogWarning($"WireRouletteWheelFBX: '{t.name}' has no mesh to build a collider from - it won't collide with the ball.");
                }
            }
        }

        if (!boundsInit)
        {
            Debug.LogError("WireRouletteWheelFBX: none of the pocket objects have a Renderer - can't measure the wheel's size. Aborting.");
            Undo.RevertAllDownToGroup(undoGroup);
            return;
        }

        float outerRadius = 0f;
        Vector3 center = rotorObj.transform.position;
        foreach (var corner in BoundsCorners(bounds))
        {
            Vector3 flat = corner - center;
            flat.y = 0f;
            outerRadius = Mathf.Max(outerRadius, flat.magnitude);
        }

        float bowlOuterRadius = outerRadius * BowlMarginFactor;
        float hubRadius = outerRadius * HubRadiusFactor;

        var woodMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Table.mat");
        var darkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Black.mat");
        var ballMat = AssetDatabase.LoadAssetAtPath<Material>(BallMaterialPath);

        BuildHub(rotorObj.transform, hubRadius, darkMat, ballPhysMat);
        BuildBowl(root.transform, bowlOuterRadius, outerRadius, woodMat, ballPhysMat);
        BuildLid(root.transform, bowlOuterRadius);
        var ballObj = BuildBall(root.transform, bowlOuterRadius, ballMat, ballPhysMat);
        var buttonObj = BuildSpinButton(root.transform, bowlOuterRadius, darkMat, ballPhysMat);

        var wheel = root.GetComponent<RouletteWheel>();
        if (wheel == null) wheel = Undo.AddComponent<RouletteWheel>(root);

        var wheelSo = new SerializedObject(wheel);
        wheelSo.FindProperty("rotor").objectReferenceValue = rotorObj.transform;
        wheelSo.FindProperty("ball").objectReferenceValue = ballObj.GetComponent<RouletteBall>();
        wheelSo.FindProperty("ballStartRadius").floatValue = bowlOuterRadius - WallThickness - BallRadius - 0.01f;
        wheelSo.FindProperty("ballTrackHeight").floatValue = BallRadius;
        var pocketsProp = wheelSo.FindProperty("pockets");
        pocketsProp.arraySize = pockets.Length;
        for (int i = 0; i < pockets.Length; i++)
            pocketsProp.GetArrayElementAtIndex(i).objectReferenceValue = pockets[i];
        wheelSo.ApplyModifiedProperties();

        var ballComp = ballObj.GetComponent<RouletteBall>();
        var ballSo = new SerializedObject(ballComp);
        ballSo.FindProperty("wheelCenter").objectReferenceValue = root.transform;
        ballSo.FindProperty("rotorRadius").floatValue = outerRadius;
        ballSo.ApplyModifiedProperties();

        var buttonSo = new SerializedObject(buttonObj.GetComponent<RouletteSpinButton>());
        buttonSo.FindProperty("wheel").objectReferenceValue = wheel;
        buttonSo.ApplyModifiedProperties();

        BuildResultLabel(root.transform, wheel, bowlOuterRadius);

        var oldWheel = GameObject.Find("RouletteWheel");
        if (oldWheel != null && oldWheel != root)
        {
            Undo.RecordObject(oldWheel, "Disable old primitive wheel");
            oldWheel.SetActive(false);
            Debug.Log($"WireRouletteWheelFBX: disabled the old primitive '{oldWheel.name}' rather than deleting it - delete it yourself once you've confirmed the FBX wheel works.");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"WireRouletteWheelFBX: wired up '{root.name}' - measured pocket radius {outerRadius:F3}m, bowl radius {bowlOuterRadius:F3}m. The wheel's exact center (used to compute this) is '{root.name}' itself, so if the pockets don't look centered on it, that's the transform to check. Save the scene, then Play and press the SpinButton to test.");
    }

    static IEnumerable<Vector3> BoundsCorners(Bounds b)
    {
        yield return new Vector3(b.min.x, b.min.y, b.min.z);
        yield return new Vector3(b.min.x, b.min.y, b.max.z);
        yield return new Vector3(b.min.x, b.max.y, b.min.z);
        yield return new Vector3(b.min.x, b.max.y, b.max.z);
        yield return new Vector3(b.max.x, b.min.y, b.min.z);
        yield return new Vector3(b.max.x, b.min.y, b.max.z);
        yield return new Vector3(b.max.x, b.max.y, b.min.z);
        yield return new Vector3(b.max.x, b.max.y, b.max.z);
    }

    // A small visible plug at the wheel's dead center - the FBX only
    // supplies the 37 pockets and a stand, no turret/spindle, so without
    // this there'd be a see-through gap in the middle. Doubles as the
    // physical block that keeps the ball from drifting into that gap.
    static void BuildHub(Transform rotor, float radius, Material material, PhysicsMaterial physMat)
    {
        var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(hub, "Create Hub");
        hub.name = "Hub";
        hub.transform.SetParent(rotor, false);
        hub.transform.localPosition = new Vector3(0f, 0.025f, 0f);
        hub.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
        if (material != null) hub.GetComponent<Renderer>().sharedMaterial = material;
        var collider = hub.GetComponent<Collider>();
        if (collider != null) collider.sharedMaterial = physMat;
    }

    // Static (non-rotating) ball track - the FBX has no bowl/housing of its
    // own, so this is built the same way the primitive wheel's was, sized
    // to the FBX's actual measured radius instead of a hardcoded constant.
    static void BuildBowl(Transform parent, float bowlRadius, float pocketRadius, Material material, PhysicsMaterial physMat)
    {
        var bowl = new GameObject("Bowl");
        Undo.RegisterCreatedObjectUndo(bowl, "Create Bowl");
        bowl.transform.SetParent(parent, false);

        var floorRing = new GameObject("FloorRing");
        Undo.RegisterCreatedObjectUndo(floorRing, "Create FloorRing");
        floorRing.transform.SetParent(bowl.transform, false);
        float floorMidRadius = (pocketRadius + bowlRadius) * 0.5f;
        float floorWidth = bowlRadius - pocketRadius;
        BuildRing(floorRing.transform, WallSegments, floorMidRadius, floorWidth, FloorThickness, -FloorThickness * 0.5f, material, physMat);

        var wall = new GameObject("Wall");
        Undo.RegisterCreatedObjectUndo(wall, "Create Wall");
        wall.transform.SetParent(bowl.transform, false);
        BuildRing(wall.transform, WallSegments, bowlRadius - WallThickness * 0.5f, WallThickness, WallHeight, WallHeight * 0.5f, material, physMat);
    }

    static void BuildRing(Transform parent, int segments, float radius, float radialThickness, float height, float centerHeight, Material material, PhysicsMaterial physMat)
    {
        float segAngle = 360f / segments;
        float chord = 2f * radius * Mathf.Sin(segAngle * Mathf.Deg2Rad * 0.5f) * 1.15f;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * segAngle;
            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector3 pos = dir * radius + Vector3.up * centerHeight;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(seg, "Create Ring Segment");
            seg.name = "Segment_" + i;
            seg.transform.SetParent(parent, false);
            seg.transform.localPosition = pos;
            seg.transform.localRotation = rot;
            seg.transform.localScale = new Vector3(chord, height, radialThickness);

            if (material != null) seg.GetComponent<Renderer>().sharedMaterial = material;

            var collider = seg.GetComponent<Collider>();
            if (physMat != null && collider != null) collider.sharedMaterial = physMat;
        }
    }

    static void BuildLid(Transform parent, float bowlRadius)
    {
        var lid = new GameObject("Lid");
        Undo.RegisterCreatedObjectUndo(lid, "Create Lid");
        lid.transform.SetParent(parent, false);
        lid.transform.localPosition = new Vector3(0f, WallHeight + LidMargin, 0f);
        var collider = Undo.AddComponent<BoxCollider>(lid);
        collider.size = new Vector3(bowlRadius * 2.2f, 0.02f, bowlRadius * 2.2f);
    }

    static GameObject BuildBall(Transform parent, float bowlRadius, Material ballMaterial, PhysicsMaterial physMat)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Undo.RegisterCreatedObjectUndo(ball, "Create Ball");
        ball.name = "Ball";
        ball.transform.SetParent(parent, false);
        ball.transform.localPosition = new Vector3(0f, BallRadius, bowlRadius * 0.8f);
        ball.transform.localScale = Vector3.one * (BallRadius * 2f);
        if (ballMaterial != null) ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;
        var col = ball.GetComponent<Collider>();
        if (col != null) col.sharedMaterial = physMat;

        var rb = Undo.AddComponent<Rigidbody>(ball);
        rb.mass = 0.02f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        Undo.AddComponent<RouletteBall>(ball);
        return ball;
    }

    static GameObject BuildSpinButton(Transform parent, float bowlRadius, Material material, PhysicsMaterial physMat)
    {
        var buttonObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(buttonObj, "Create SpinButton");
        buttonObj.name = "SpinButton";
        buttonObj.transform.SetParent(parent, false);
        buttonObj.transform.localPosition = new Vector3(bowlRadius + 0.15f, 0.05f, 0f);
        buttonObj.transform.localScale = new Vector3(0.08f, 0.01f, 0.08f);
        if (material != null) buttonObj.GetComponent<Renderer>().sharedMaterial = material;
        var col = buttonObj.GetComponent<Collider>();
        if (col != null) col.sharedMaterial = physMat;

        Undo.AddComponent<XRSimpleInteractable>(buttonObj);
        Undo.AddComponent<RouletteSpinButton>(buttonObj);
        return buttonObj;
    }

    static void BuildResultLabel(Transform parent, RouletteWheel wheel, float bowlRadius)
    {
        var labelRoot = new GameObject("ResultLabel");
        Undo.RegisterCreatedObjectUndo(labelRoot, "Create ResultLabel");
        labelRoot.transform.SetParent(parent, false);
        labelRoot.transform.localPosition = new Vector3(0f, 0.35f, -(bowlRadius + 0.1f));
        labelRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var display = Undo.AddComponent<RouletteResultDisplay>(labelRoot);
        var so = new SerializedObject(display);
        so.FindProperty("wheel").objectReferenceValue = wheel;
        so.ApplyModifiedProperties();
    }
}
