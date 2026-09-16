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
// Deliberately minimal right now: tags each pocket with its number (leaving
// its existing collider alone entirely - not this script's business), and
// makes sure a Ball exists with the right components. It never repositions
// an existing Ball - that's placed and tuned by hand in the scene, and
// RouletteBall.Push() just nudges it from wherever it's sitting.
//
// Run this once, with the FBX's root object selected in the Hierarchy.
public static class WireRouletteWheelFBX
{
    static readonly Regex PocketNamePattern = new Regex(@"^Pocket(\d+)$");

    const float BallRadius = 0.02f;

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
        for (int i = 0; i < pocketTransforms.Count; i++)
        {
            var (number, t) = pocketTransforms[i];
            var pocket = t.GetComponent<RoulettePocket>();
            if (pocket == null) pocket = Undo.AddComponent<RoulettePocket>(t.gameObject);
            pocket.Number = number;
            pockets[i] = pocket;
        }

        var ballMat = AssetDatabase.LoadAssetAtPath<Material>(BallMaterialPath);
        var darkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Black.mat");

        // Only build a Ball if there isn't one already - an existing one is
        // positioned and tuned by hand, and re-running this tool must never
        // move it or reset that work.
        var existingBall = root.transform.Find("Ball");
        GameObject ballObj = existingBall != null ? existingBall.gameObject : BuildBall(root.transform, ballMat, ballPhysMat);

        float outerRadius = EstimateOuterRadius(root, pocketTransforms);

        var buttonObj = root.transform.Find("SpinButton")?.gameObject;
        if (buttonObj == null) buttonObj = BuildSpinButton(root.transform, outerRadius, darkMat, ballPhysMat);

        var wheel = root.GetComponent<RouletteWheel>();
        if (wheel == null) wheel = Undo.AddComponent<RouletteWheel>(root);

        var wheelSo = new SerializedObject(wheel);
        wheelSo.FindProperty("ball").objectReferenceValue = ballObj.GetComponent<RouletteBall>();
        var pocketsProp = wheelSo.FindProperty("pockets");
        pocketsProp.arraySize = pockets.Length;
        for (int i = 0; i < pockets.Length; i++)
            pocketsProp.GetArrayElementAtIndex(i).objectReferenceValue = pockets[i];
        wheelSo.ApplyModifiedProperties();

        var buttonSo = new SerializedObject(buttonObj.GetComponent<RouletteSpinButton>());
        buttonSo.FindProperty("wheel").objectReferenceValue = wheel;
        buttonSo.ApplyModifiedProperties();

        if (root.transform.Find("ResultLabel") == null)
            BuildResultLabel(root.transform, wheel, outerRadius);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"WireRouletteWheelFBX: wired up '{root.name}' - tagged {pockets.Length} pockets (colliders untouched). Ball {(existingBall != null ? "left exactly where you placed it" : "created at the wheel's center - move it wherever you want it to start")}. Save the scene, then Play and press the SpinButton (or the RouletteBall's 'Push' context menu) to test.");
    }

    // Just for placing the SpinButton/ResultLabel sensibly - not used for
    // any ball physics.
    static float EstimateOuterRadius(GameObject root, List<(int number, Transform t)> pocketTransforms)
    {
        var rootRenderer = root.GetComponent<Renderer>();
        if (rootRenderer != null)
        {
            Vector3 extents = rootRenderer.bounds.extents;
            return Mathf.Max(extents.x, extents.z);
        }

        float maxRadius = 0f;
        Vector3 center = root.transform.position;
        foreach (var (_, t) in pocketTransforms)
        {
            var renderer = t.GetComponent<Renderer>();
            if (renderer == null) continue;
            Vector3 flat = renderer.bounds.center - center;
            flat.y = 0f;
            maxRadius = Mathf.Max(maxRadius, flat.magnitude);
        }
        return maxRadius > 0f ? maxRadius * 1.5f : 1f;
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
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

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
