using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Wires up the "reach into your pocket" chip-dispensing mechanic: a
// PlayerTorsoAnchor that follows the player's body (not their head), and
// four ChipDispenser pocket points hanging off it, one per denomination.
// Get-or-add throughout, and only positions a pocket the first time it's
// created - re-running after nudging one in the Inspector (to fit actual
// VR reach) doesn't stomp that tweak.
public static class SetupChipDispensers
{
    const string RigName = "XR Origin (XR Rig)";
    const string AnchorName = "PlayerTorsoAnchor";
    const float PocketDiameter = 0.08f;
    const float PocketThickness = 0.015f;

    // Left-to-right, low-to-high value, at waist height (PlayerTorsoAnchor
    // already accounts for that) and slightly in front of the body.
    static readonly (string name, float localX, string prefabPath)[] Pockets =
    {
        ("Pocket_Red", -0.3f, "Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Red.prefab"),
        ("Pocket_Blue", -0.1f, "Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Blue.prefab"),
        ("Pocket_Green", 0.1f, "Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Green.prefab"),
        ("Pocket_Black", 0.3f, "Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Black.prefab"),
    };

    [MenuItem("Tools/Roulette/Setup Chip Dispensers")]
    static void Setup()
    {
        var rig = GameObject.Find(RigName);
        if (rig == null)
        {
            Debug.LogError($"SetupChipDispensers: no '{RigName}' found in the scene.");
            return;
        }

        var mainCamera = rig.transform.Find("Camera Offset/Main Camera");
        if (mainCamera == null)
        {
            Debug.LogError($"SetupChipDispensers: couldn't find 'Camera Offset/Main Camera' under '{RigName}'.");
            return;
        }

        var anchorTransform = rig.transform.Find(AnchorName);
        GameObject anchor = anchorTransform != null ? anchorTransform.gameObject : null;
        if (anchor == null)
        {
            anchor = new GameObject(AnchorName);
            Undo.RegisterCreatedObjectUndo(anchor, "Create " + AnchorName);
            anchor.transform.SetParent(rig.transform, false);
        }

        var anchorScript = GetOrAddComponent<PlayerTorsoAnchor>(anchor);
        var anchorSo = new SerializedObject(anchorScript);
        anchorSo.FindProperty("head").objectReferenceValue = mainCamera;
        anchorSo.ApplyModifiedProperties();

        int configured = 0;
        foreach (var (name, localX, prefabPath) in Pockets)
        {
            var chipGo = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var chipPrefab = chipGo != null ? chipGo.GetComponent<CasinoChip>() : null;
            if (chipPrefab == null)
            {
                Debug.LogWarning($"SetupChipDispensers: couldn't load a CasinoChip prefab at '{prefabPath}' - skipped '{name}'. Run Tools/Roulette/Setup Casino Chips first.");
                continue;
            }

            CreatePocketStub(anchor.transform, name, new Vector3(localX, 0f, 0.15f), chipPrefab);
            configured++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupChipDispensers: torso anchor wired to '{mainCamera.name}', {configured} pocket(s) configured.");
    }

    static void CreatePocketStub(Transform parent, string name, Vector3 localPosition, CasinoChip chipPrefab)
    {
        var existing = parent.Find(name);
        GameObject stub = existing != null ? existing.gameObject : null;

        if (stub == null)
        {
            stub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stub.name = name;
            Undo.RegisterCreatedObjectUndo(stub, "Create " + name);
            stub.transform.SetParent(parent, false);
            stub.transform.localPosition = localPosition;
            stub.transform.localRotation = Quaternion.identity;
            stub.transform.localScale = new Vector3(PocketDiameter, PocketThickness * 0.5f, PocketDiameter);

            var sourceRenderer = chipPrefab.GetComponentInChildren<Renderer>();
            if (sourceRenderer != null) stub.GetComponent<Renderer>().sharedMaterial = sourceRenderer.sharedMaterial;
        }

        GetOrAddComponent<XRSimpleInteractable>(stub);
        var dispenser = GetOrAddComponent<ChipDispenser>(stub);
        var so = new SerializedObject(dispenser);
        so.FindProperty("chipPrefab").objectReferenceValue = chipPrefab;
        so.ApplyModifiedProperties();
    }

    static T GetOrAddComponent<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        return existing != null ? existing : go.AddComponent<T>();
    }
}
