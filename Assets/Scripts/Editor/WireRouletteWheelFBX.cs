using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// One-time integration: takes the artist-made roulette wheel FBX (already
// dragged into the scene, its 37 number sections named "Pocket0".."Pocket36")
// and wires it up to run through the RouletteWheel/RouletteBall simulation.
//
// The wheelhead's own single mesh is a complete, working ball-rolling
// surface on its own (real outer wall, real walled pocket compartments,
// all one static MeshCollider) - the only thing standing in the way is
// that each PocketN object is a flat number/color decal that ships with
// its own auto-imported paper-thin MeshCollider sitting exactly on top of
// that same surface. Two overlapping static colliders on the same surface
// is what was launching the ball: PhysX resolves the interpenetration with
// a hard push. So this script does the minimum: strip those decal
// colliders, tag each with its number, and point RouletteBall at the real
// measured pocket-ring radius so its "pull toward the ring as it slows"
// force actually aims at where the real walls are. No bowl/hub/wall/lid
// geometry gets built - none of it is needed on top of what the model
// already has.
//
// Run this once, with the FBX's root object selected in the Hierarchy.
public static class WireRouletteWheelFBX
{
    static readonly Regex PocketNamePattern = new Regex(@"^Pocket(\d+)$");

    const float BallRadius = 0.02f;
    const float SpawnClearance = 0.05f; // extra room above the wheelhead's tallest point before the ball free-falls onto it
    const float StartRadiusInset = 0.05f; // keep the launch orbit this far inside the wheelhead's outer edge

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

        // Undo a previous run's Rotor grouping, if present - pockets belong
        // as direct children of root now, nothing needs to rotate.
        var oldRotor = root.transform.Find("Rotor");
        if (oldRotor != null)
        {
            var pocketsUnderRotor = new List<Transform>();
            foreach (Transform child in oldRotor) pocketsUnderRotor.Add(child);
            foreach (var t in pocketsUnderRotor)
            {
                if (PocketNamePattern.IsMatch(t.name))
                    Undo.SetTransformParent(t, root.transform, "Reparent Pocket");
            }
            Undo.DestroyObjectImmediate(oldRotor.gameObject);
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
            Debug.LogError($"WireRouletteWheelFBX: couldn't find {BallPhysMaterialPath}. Run 'Tools > Setup Roulette Wheel' once first so that asset exists (you can disable its output afterward), then re-run this.");
            return;
        }

        Undo.SetCurrentGroupName("Wire Up Roulette Wheel FBX");
        int undoGroup = Undo.GetCurrentGroup();

        var pockets = new RoulettePocket[pocketTransforms.Count];
        Bounds pocketBounds = default;
        bool boundsInit = false;
        float radiusSum = 0f;

        foreach (var (number, t) in pocketTransforms)
        {
            var pocket = t.GetComponent<RoulettePocket>();
            if (pocket == null) pocket = Undo.AddComponent<RoulettePocket>(t.gameObject);
            pocket.Number = number;

            // The actual bug fix: this decal must never physically collide -
            // the real pocket walls are already part of the wheelhead's own
            // single mesh collider, sitting right underneath.
            var decalCollider = t.GetComponent<Collider>();
            if (decalCollider != null) Undo.DestroyObjectImmediate(decalCollider);

            var renderer = t.GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"WireRouletteWheelFBX: '{t.name}' has no Renderer - can't measure its position.");
                continue;
            }
            if (!boundsInit) { pocketBounds = renderer.bounds; boundsInit = true; }
            else pocketBounds.Encapsulate(renderer.bounds);
        }

        if (!boundsInit)
        {
            Debug.LogError("WireRouletteWheelFBX: none of the pocket objects have a Renderer - can't measure the wheel's size. Aborting.");
            Undo.RevertAllDownToGroup(undoGroup);
            return;
        }

        Vector3 center = root.transform.position;
        foreach (var (_, t) in pocketTransforms)
        {
            var renderer = t.GetComponent<Renderer>();
            if (renderer == null) continue;
            Vector3 flat = renderer.bounds.center - center;
            flat.y = 0f;
            radiusSum += flat.magnitude;
        }
        float pocketRingRadius = radiusSum / pocketTransforms.Count;

        // The wheelhead's own overall mesh (if it has one directly on root)
        // tells us how far out the real outer wall/track goes and how tall
        // the tallest point is, so the ball launches from somewhere on the
        // real track and drops from safely above the real geometry instead
        // of a guessed constant.
        var rootRenderer = root.GetComponent<Renderer>();
        float outerRadius = pocketRingRadius * 1.5f; // fallback if root has no renderer of its own
        float maxY = center.y + 0.3f;
        if (rootRenderer != null)
        {
            Vector3 flat = new Vector3(rootRenderer.bounds.extents.x, 0f, rootRenderer.bounds.extents.z);
            outerRadius = Mathf.Max(flat.x, flat.z);
            maxY = rootRenderer.bounds.max.y;
        }

        float ballStartRadius = Mathf.Max(pocketRingRadius, outerRadius - StartRadiusInset);
        // Above the wheelhead's real tallest point, but left with a margin
        // (rather than hugging maxY) so it stays below a hand-placed
        // containment lid that might sit close above the wall - Launch()
        // then raycasts down from this height to find the real surface
        // under wherever it's actually launching, instead of assuming one.
        float raycastStartHeight = (maxY - center.y) + SpawnClearance;

        foreach (var name in new[] { "Ball", "SpinButton", "ResultLabel" })
        {
            var existing = root.transform.Find(name);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        }

        var ballMat = AssetDatabase.LoadAssetAtPath<Material>(BallMaterialPath);
        var darkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Black.mat");

        var ballObj = BuildBall(root.transform, ballMat, ballPhysMat);
        // Rest it on the known-good pocket ring for now, purely so it looks
        // reasonable in the Scene view before Play - Launch() repositions it
        // for real once a spin actually starts.
        ballObj.transform.position = new Vector3(center.x, center.y + (pocketBounds.center.y - center.y) + BallRadius, center.z) + Vector3.forward * pocketRingRadius;
        var buttonObj = BuildSpinButton(root.transform, outerRadius, darkMat, ballPhysMat);

        for (int i = 0; i < pocketTransforms.Count; i++)
            pockets[i] = pocketTransforms[i].t.GetComponent<RoulettePocket>();

        var wheel = root.GetComponent<RouletteWheel>();
        if (wheel == null) wheel = Undo.AddComponent<RouletteWheel>(root);

        var wheelSo = new SerializedObject(wheel);
        wheelSo.FindProperty("ball").objectReferenceValue = ballObj.GetComponent<RouletteBall>();
        wheelSo.FindProperty("ballStartRadius").floatValue = ballStartRadius;
        var pocketsProp = wheelSo.FindProperty("pockets");
        pocketsProp.arraySize = pockets.Length;
        for (int i = 0; i < pockets.Length; i++)
            pocketsProp.GetArrayElementAtIndex(i).objectReferenceValue = pockets[i];
        wheelSo.ApplyModifiedProperties();

        var ballComp = ballObj.GetComponent<RouletteBall>();
        var ballSo = new SerializedObject(ballComp);
        ballSo.FindProperty("wheelCenter").objectReferenceValue = root.transform;
        ballSo.FindProperty("pocketRingRadius").floatValue = pocketRingRadius;
        ballSo.FindProperty("raycastStartHeight").floatValue = raycastStartHeight;
        ballSo.ApplyModifiedProperties();

        var buttonSo = new SerializedObject(buttonObj.GetComponent<RouletteSpinButton>());
        buttonSo.FindProperty("wheel").objectReferenceValue = wheel;
        buttonSo.ApplyModifiedProperties();

        BuildResultLabel(root.transform, wheel, outerRadius);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"WireRouletteWheelFBX: wired up '{root.name}' - pocket ring radius {pocketRingRadius:F3}m, outer radius {outerRadius:F3}m, ball start radius {ballStartRadius:F3}m, raycast probe height {raycastStartHeight:F3}m above center. No new colliders/walls were built - the ball only ever collides with the wheelhead's own existing mesh (plus whatever you've added by hand, like a lid). Save the scene, then Play and press the SpinButton to test.");
    }

    static GameObject BuildBall(Transform parent, Material ballMaterial, PhysicsMaterial physMat)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Undo.RegisterCreatedObjectUndo(ball, "Create Ball");
        ball.name = "Ball";
        ball.transform.SetParent(parent, false);
        ball.transform.localPosition = Vector3.zero;
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

    static GameObject BuildSpinButton(Transform parent, float outerRadius, Material material, PhysicsMaterial physMat)
    {
        var buttonObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(buttonObj, "Create SpinButton");
        buttonObj.name = "SpinButton";
        buttonObj.transform.SetParent(parent, false);
        buttonObj.transform.localPosition = new Vector3(outerRadius + 0.15f, 0.05f, 0f);
        buttonObj.transform.localScale = new Vector3(0.08f, 0.01f, 0.08f);
        if (material != null) buttonObj.GetComponent<Renderer>().sharedMaterial = material;
        var col = buttonObj.GetComponent<Collider>();
        if (col != null) col.sharedMaterial = physMat;

        Undo.AddComponent<XRSimpleInteractable>(buttonObj);
        Undo.AddComponent<RouletteSpinButton>(buttonObj);
        return buttonObj;
    }

    static void BuildResultLabel(Transform parent, RouletteWheel wheel, float outerRadius)
    {
        var labelRoot = new GameObject("ResultLabel");
        Undo.RegisterCreatedObjectUndo(labelRoot, "Create ResultLabel");
        labelRoot.transform.SetParent(parent, false);
        labelRoot.transform.localPosition = new Vector3(0f, 0.35f, -(outerRadius + 0.1f));
        labelRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var display = Undo.AddComponent<RouletteResultDisplay>(labelRoot);
        var so = new SerializedObject(display);
        so.FindProperty("wheel").objectReferenceValue = wheel;
        so.ApplyModifiedProperties();
    }
}
