using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SetupSlotMachineReels
{
    const string SymbolFolder = "Assets/Prefabs/Symbols";

    [MenuItem("Tools/Setup Slot Machine Reels")]
    static void Setup()
    {
        var slotMachineObj = GameObject.Find("SlotMachine");
        if (slotMachineObj == null)
        {
            Debug.LogError("SetupSlotMachineReels: couldn't find a GameObject named 'SlotMachine' in the open scene.");
            return;
        }
        var slotMachine = slotMachineObj.GetComponent<SlotMachine>();
        if (slotMachine == null)
        {
            Debug.LogError("SetupSlotMachineReels: 'SlotMachine' has no SlotMachine component.");
            return;
        }

        if (!Directory.Exists(SymbolFolder))
            Directory.CreateDirectory(SymbolFolder);

        // --- Canvas over (roughly) the screen area - you'll need to reposition/resize this ---
        var canvasObj = GameObject.Find("ScreenCanvas");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("ScreenCanvas", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasObj, "Create ScreenCanvas");
            canvasObj.transform.SetParent(slotMachineObj.transform, false);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            var canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(600, 400);
            canvasRect.localPosition = new Vector3(0f, 1.3f, 0f);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.001f;
        }

        // --- 3 reel slots in a horizontal row ---
        Image[] slots = new Image[3];
        float[] xPositions = { -180f, 0f, 180f };
        for (int i = 0; i < 3; i++)
        {
            var slotName = "ReelSlot" + i;
            var slotObj = canvasObj.transform.Find(slotName)?.gameObject;
            if (slotObj == null)
            {
                slotObj = new GameObject(slotName, typeof(RectTransform));
                slotObj.transform.SetParent(canvasObj.transform, false);
                var img = slotObj.AddComponent<Image>();
                img.preserveAspect = true;

                var rect = slotObj.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(150f, 150f);
                rect.anchoredPosition = new Vector2(xPositions[i], 0f);
            }
            slots[i] = slotObj.GetComponent<Image>();
        }

        var reels = canvasObj.GetComponent<SlotMachineReels>();
        if (reels == null) reels = canvasObj.AddComponent<SlotMachineReels>();

        var reelsSo = new SerializedObject(reels);
        var slotsProp = reelsSo.FindProperty("reelSlots");
        slotsProp.arraySize = 3;
        for (int i = 0; i < 3; i++)
            slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        reelsSo.ApplyModifiedProperties();

        // --- Placeholder symbols for the existing win tiers ---
        var slotMachineSo = new SerializedObject(slotMachine);
        var reelsField = slotMachineSo.FindProperty("reels");
        reelsField.objectReferenceValue = reels;

        var tiersProp = slotMachineSo.FindProperty("winTiers");
        Color[] placeholderColors = { new Color(0.9f, 0.15f, 0.15f), new Color(1f, 0.7f, 0f), new Color(0.7f, 0.2f, 0.9f) };
        for (int i = 0; i < tiersProp.arraySize; i++)
        {
            var tierProp = tiersProp.GetArrayElementAtIndex(i);
            var symbolProp = tierProp.FindPropertyRelative("symbol");
            var labelProp = tierProp.FindPropertyRelative("label");
            if (symbolProp.objectReferenceValue == null)
            {
                Color c = placeholderColors[i % placeholderColors.Length];
                string safeName = labelProp.stringValue.Replace(" ", "");
                symbolProp.objectReferenceValue = CreateCircleSprite($"{SymbolFolder}/Symbol_{safeName}.png", c);
            }
        }
        slotMachineSo.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = canvasObj;

        Debug.Log("SlotMachine reels set up with placeholder symbols. Select 'ScreenCanvas' and use Move/Rotate/Scale to align it with the blue screen area, then save the scene.");
    }

    static Sprite CreateCircleSprite(string path, Color color, int size = 128)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f - 4f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                tex.SetPixel(x, y, dist <= radius ? color : new Color(0f, 0f, 0f, 0f));
            }
        }
        tex.Apply();

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.alphaIsTransparency = true;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
