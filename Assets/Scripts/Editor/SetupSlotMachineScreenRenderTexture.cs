using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Switches the slot machine's reel screen from a manually-aligned overlay
// Canvas to a Render Texture painted directly onto the machine's own screen
// material - eliminates the plane-vs-mesh alignment problem entirely, since
// there's no separate plane anymore.
public static class SetupSlotMachineScreenRenderTexture
{
    const string RenderTexturePath = "Assets/Prefabs/SlotScreenRT.renderTexture";
    const string ScreenMaterialSourcePath = "Assets/ithappy/Casino_Free/Materials/Screens_1.mat";
    const string ScreenMaterialClonePath = "Assets/Prefabs/SlotScreen_Instance.mat";

    [MenuItem("Tools/Setup Slot Machine Screen Render Texture")]
    static void Setup()
    {
        var slotMachineObj = GameObject.Find("SlotMachine");
        if (slotMachineObj == null)
        {
            Debug.LogError("SetupSlotMachineScreenRenderTexture: couldn't find 'SlotMachine'.");
            return;
        }
        var screenCanvasObj = GameObject.Find("ScreenCanvas");
        if (screenCanvasObj == null)
        {
            Debug.LogError("SetupSlotMachineScreenRenderTexture: couldn't find 'ScreenCanvas' - run Tools > Setup Slot Machine Reels first.");
            return;
        }
        var reels = screenCanvasObj.GetComponent<SlotMachineReels>();
        var canvas = screenCanvasObj.GetComponent<Canvas>();
        var meshRenderer = slotMachineObj.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            Debug.LogError("SetupSlotMachineScreenRenderTexture: 'SlotMachine' has no MeshRenderer.");
            return;
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            Debug.LogError("SetupSlotMachineScreenRenderTexture: couldn't find the built-in 'UI' layer.");
            return;
        }

        // --- Render Texture ---
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
        if (rt == null)
        {
            rt = new RenderTexture(512, 342, 16, RenderTextureFormat.ARGB32) { name = "SlotScreenRT" };
            AssetDatabase.CreateAsset(rt, RenderTexturePath);
        }

        // --- Dedicated camera that renders only the reel UI into the Render Texture ---
        var camObj = GameObject.Find("ReelCamera");
        if (camObj == null)
        {
            camObj = new GameObject("ReelCamera");
            Undo.RegisterCreatedObjectUndo(camObj, "Create ReelCamera");
            camObj.transform.SetParent(slotMachineObj.transform, false);
        }
        var reelCam = camObj.GetComponent<Camera>();
        if (reelCam == null) reelCam = camObj.AddComponent<Camera>();
        reelCam.clearFlags = CameraClearFlags.SolidColor;
        reelCam.backgroundColor = new Color(0.05f, 0.05f, 0.15f, 1f);
        reelCam.cullingMask = 1 << uiLayer;
        reelCam.targetTexture = rt;
        reelCam.stereoTargetEye = StereoTargetEyeMask.None; // render mono into the Render Texture, not the XR headset
        reelCam.enabled = false; // SlotMachineReels enables it only while a spin is playing

        // --- Canvas becomes Screen Space - Camera, rendered by ReelCamera (position/rotation no longer matter) ---
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = reelCam;
        canvas.planeDistance = 1f;

        SetLayerRecursively(screenCanvasObj, uiLayer);

        var reelsSo = new SerializedObject(reels);
        reelsSo.FindProperty("reelCamera").objectReferenceValue = reelCam;
        reelsSo.ApplyModifiedProperties();

        // --- Clone the screen material (never edit the shared asset directly) and point it at the Render Texture ---
        var screenMat = AssetDatabase.LoadAssetAtPath<Material>(ScreenMaterialClonePath);
        if (screenMat == null)
        {
            var sourceMat = AssetDatabase.LoadAssetAtPath<Material>(ScreenMaterialSourcePath);
            if (sourceMat == null)
            {
                Debug.LogError($"SetupSlotMachineScreenRenderTexture: couldn't find source material at {ScreenMaterialSourcePath}.");
                return;
            }
            screenMat = new Material(sourceMat);
            AssetDatabase.CreateAsset(screenMat, ScreenMaterialClonePath);
        }
        screenMat.SetTexture("_BaseMap", rt);
        if (screenMat.HasProperty("_MainTex")) screenMat.SetTexture("_MainTex", rt);
        EditorUtility.SetDirty(screenMat);

        var mats = meshRenderer.sharedMaterials;
        if (mats.Length > 1)
        {
            mats[1] = screenMat; // Element 1 = Screens_1
            meshRenderer.sharedMaterials = mats;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = camObj;

        Debug.Log("Slot machine screen now renders via a Render Texture painted onto the mesh - ScreenCanvas's position/rotation no longer matter. Save the scene and test.");
    }

    static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
