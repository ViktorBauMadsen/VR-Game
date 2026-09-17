using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Turns the poker-chip art (prefabs + the loose test copies already sitting
// in the scene) into actual grabbable bets: adds an XRGrabInteractable
// tuned for a responsive snap (see CasinoChip's header for why) plus
// CasinoChip itself and PickupIndicator, the same generic pickup-affordance
// dot every other pickupable already uses. Also repoints the prefabs at the
// correct material (see FixMaterial below - they shipped wired to the
// wrong one). Get-or-add throughout - the loose PokerChip_Red already has
// a hand-added XRGrabInteractable in SampleScene, and AddComponent would
// throw on its DisallowMultipleComponent attribute if run blindly.
//
// Deliberately skips InteractableHighlight: it requires a Renderer on the
// *same* GameObject it's attached to, but these chips have no renderer on
// their root - both LOD meshes live on separate LODGroup child objects -
// so there's nowhere on this prefab structure to put it without also
// restructuring where XRGrabInteractable/CasinoChip live.
public static class SetupCasinoChips
{
    // materialGuid: the prefabs shipped with every LOD renderer pointed at
    // the pack's generic GamePieces.mat, which isn't textured for this
    // mesh's UVs - the pack also ships a material actually made for these
    // chips (a shared PokerChip.mat for Red/Blue/Green, a separate
    // color-specific CasinoChip.mat for Black), so point every renderer at
    // that instead.
    static readonly (string path, int value, string materialGuid)[] ChipPrefabs =
    {
        ("Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Red.prefab", 5, "883908feb6514fd43a48e313a747e32c"),
        ("Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Blue.prefab", 10, "883908feb6514fd43a48e313a747e32c"),
        ("Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Green.prefab", 25, "883908feb6514fd43a48e313a747e32c"),
        ("Assets/SubstanceAssets/LittleGamesPack/Prefabs/Individual Pieces/PokerChips/PokerChip_Black.prefab", 100, "3ef1291d21a0d2b48bb1a04187b717ee"),
    };

    [MenuItem("Tools/Roulette/Setup Casino Chips")]
    static void Setup()
    {
        foreach (var (path, value, materialGuid) in ChipPrefabs)
            SetupPrefab(path, value, materialGuid);

        int sceneCount = SetupLooseSceneChips();
        Debug.Log($"SetupCasinoChips: configured {ChipPrefabs.Length} prefab(s) and {sceneCount} loose scene chip(s).");
    }

    static void SetupPrefab(string path, int value, string materialGuid)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogWarning($"SetupCasinoChips: couldn't load prefab at '{path}'.");
            return;
        }

        ConfigureChip(root, value);
        FixMaterial(root, materialGuid);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void FixMaterial(GameObject root, string materialGuid)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(materialGuid));
        if (material == null)
        {
            Debug.LogWarning($"SetupCasinoChips: couldn't resolve material guid '{materialGuid}'.");
            return;
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = material;
    }

    static int SetupLooseSceneChips()
    {
        int count = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (!t.name.StartsWith("PokerChip_") || t.name.Contains("_LOD")) continue;
            // Real prefab instances already picked this up from the asset above -
            // only loose/unpacked copies need it applied directly.
            if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue;

            ConfigureChip(t.gameObject, ValueForName(t.name));
            count++;
        }

        if (count > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        return count;
    }

    static int ValueForName(string name)
    {
        if (name.Contains("Black")) return 100;
        if (name.Contains("Green")) return 25;
        if (name.Contains("Blue")) return 10;
        return 5; // Red, or anything unrecognized - the lowest denomination
    }

    static void ConfigureChip(GameObject root, int value)
    {
        var grab = GetOrAddComponent<XRGrabInteractable>(root);
        var grabSo = new SerializedObject(grab);
        grabSo.FindProperty("m_MovementType").enumValueIndex = 2; // Instantaneous - 1:1 hand tracking for a responsive snap
        grabSo.FindProperty("m_ThrowOnDetach").boolValue = false; // avoids a "cannot throw a kinematic Rigidbody" warning on every successful snap
        grabSo.ApplyModifiedProperties();

        var chip = GetOrAddComponent<CasinoChip>(root);
        var chipSo = new SerializedObject(chip);
        chipSo.FindProperty("value").intValue = value;
        chipSo.ApplyModifiedProperties();

        EnsurePickupIndicator(root);
    }

    // AddComponent runs Awake immediately, even in Edit Mode - so a plain
    // GetOrAddComponent<PickupIndicator> would fire its dot-spawning Awake
    // right now and bake a real "Pickup Indicator" child into the saved
    // prefab/scene, instead of the dot being created fresh each real Play
    // Mode start the way it is on every other pickupable. Only strip that
    // one-off copy the first time the component is actually added -
    // GetOrAddComponent finding one already there next run means Awake
    // won't fire again, so there's nothing to clean up.
    static void EnsurePickupIndicator(GameObject root)
    {
        bool existed = root.GetComponent<PickupIndicator>() != null;
        GetOrAddComponent<PickupIndicator>(root);
        if (existed) return;

        var dot = root.transform.Find("Pickup Indicator");
        if (dot != null) Object.DestroyImmediate(dot.gameObject);
    }

    static T GetOrAddComponent<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        return existing != null ? existing : go.AddComponent<T>();
    }
}
