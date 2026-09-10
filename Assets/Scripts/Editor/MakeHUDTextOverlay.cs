using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Makes the PlayerHUD's text (BalanceText and the FloatingText popups) always
// render on top of everything, regardless of what's physically in front of it,
// by switching them to TMP's built-in "Overlay" shader (Queue = Overlay,
// ZTest Always, ZWrite Off - it skips the depth buffer entirely).
public static class MakeHUDTextOverlay
{
    const string FontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    const string OverlayMatPath = "Assets/Prefabs/HUDTextOverlay.mat";
    const string FloatingTextPrefabPath = "Assets/Prefabs/FloatingText.prefab";

    [MenuItem("Tools/Make HUD Text Always On Top")]
    static void Setup()
    {
        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (fontAsset == null)
        {
            Debug.LogError("MakeHUDTextOverlay: couldn't find the default LiberationSans SDF font asset.");
            return;
        }

        var overlayShader = Shader.Find("TextMeshPro/Mobile/Distance Field Overlay");
        if (overlayShader == null)
        {
            Debug.LogError("MakeHUDTextOverlay: couldn't find the TMP Overlay shader.");
            return;
        }

        var overlayMat = AssetDatabase.LoadAssetAtPath<Material>(OverlayMatPath);
        if (overlayMat == null)
        {
            overlayMat = new Material(fontAsset.material); // copies the font atlas/texture references
            overlayMat.shader = overlayShader;
            AssetDatabase.CreateAsset(overlayMat, OverlayMatPath);
        }

        int sceneCount = 0;
        foreach (var text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            text.fontSharedMaterial = overlayMat;
            EditorUtility.SetDirty(text);
            sceneCount++;
        }

        // Also update the FloatingText prefab asset directly - instances are
        // spawned from it at runtime and wouldn't otherwise pick this up.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FloatingTextPrefabPath) != null)
        {
            var contents = PrefabUtility.LoadPrefabContents(FloatingTextPrefabPath);
            var prefabText = contents.GetComponent<TextMeshProUGUI>();
            if (prefabText != null)
                prefabText.fontSharedMaterial = overlayMat;
            PrefabUtility.SaveAsPrefabAsset(contents, FloatingTextPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"MakeHUDTextOverlay: applied always-on-top material to {sceneCount} scene text object(s) and the FloatingText prefab.");
    }
}
