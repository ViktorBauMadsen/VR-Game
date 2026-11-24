using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VideoSlots;

// Tools > Slot Machines:
//  - Bake Video Slot Art: renders the video slot art (symbols, logos,
//    banners, wheel, digits, effects) into PNGs under
//    Assets/VideoSlots/Resources/VideoSlots, plus the screen material.
//  - Convert Selected To Golden Inferno / Star Wheel: turns a classic slot
//    machine into a video slot. The old reel screen and pay table are
//    switched off (not deleted), the lever and settings carry over.
public static class VideoSlotTools
{
    const string OutDir = "Assets/VideoSlots/Resources/VideoSlots";

    [MenuItem("Tools/Slot Machines/Bake Video Slot Art")]
    public static void BakeArt()
    {
        Directory.CreateDirectory(OutDir);
        var fontAsset = TMP_Settings.defaultFontAsset;
        if (fontAsset == null) fontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (fontAsset == null)
        {
            Debug.LogError("Video slots: no TextMeshPro default font found - import TMP Essentials first.");
            return;
        }
        var font = new SdfFont(fontAsset);
        try
        {
            for (int i = 0; i < SlotTextures.All.Length; i++)
            {
                var e = SlotTextures.All[i];
                EditorUtility.DisplayProgressBar("Baking video slot art", e.name, (float)i / SlotTextures.All.Length);
                var px = e.build(font);
                SlotTextures.Premultiply(px);
                var tex = new Texture2D(e.w, e.h, TextureFormat.RGBA32, false, false);
                tex.SetPixels(px);
                tex.Apply();
                string path = $"{OutDir}/{e.name}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false; // premultiplied
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Trilinear;
                imp.anisoLevel = 4;
                imp.maxTextureSize = 2048;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }

            string matPath = $"{OutDir}/SlotScreen.mat";
            var shader = Shader.Find("Casino/SlotScreen");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else mat.shader = shader;
            mat.SetFloat("_SrcBlend", 1f);
            mat.SetFloat("_DstBlend", 10f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        Debug.Log($"Video slots: art baked to {OutDir}.");
    }

    [MenuItem("Tools/Slot Machines/Convert Selected To Golden Inferno")]
    static void ConvertInferno() => ConvertSelected<GoldenInfernoSlot>("Golden Inferno");

    [MenuItem("Tools/Slot Machines/Convert Selected To Star Wheel")]
    static void ConvertStarWheel() => ConvertSelected<StarWheelSlot>("Star Wheel");

    [MenuItem("Tools/Slot Machines/Convert Selected To Fruit Cocktail")]
    static void ConvertFruitCocktail() => ConvertSelected<FruitCocktailSlot>("Fruit Cocktail");

    static void ConvertSelected<T>(string title) where T : VideoSlotMachine
    {
        var targets = Selection.gameObjects.Select(g => g.GetComponentInParent<SlotMachine>()).Where(m => m != null).Distinct().ToList();
        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("Convert slot machine", "Select a classic slot machine (an object with the SlotMachine component) first.", "OK");
            return;
        }
        if (!File.Exists($"{OutDir}/Fx.png")) BakeArt();
        foreach (var old in targets) Convert<T>(old, title);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static void Convert<T>(SlotMachine old, string title) where T : VideoSlotMachine
    {
        var go = old.gameObject;
        Undo.RegisterFullObjectHierarchyUndo(go, "Convert to " + title);

        // Settings that carry over.
        var src = new SerializedObject(old);
        int bet = src.FindProperty("betAmount").intValue;
        var ladder = src.FindProperty("betLadder");
        bool buildPanel = src.FindProperty("buildBetPanel").boolValue;
        Vector3 panelOffset = src.FindProperty("betPanelOffset").vector3Value;
        var audio = src.FindProperty("audioSource").objectReferenceValue as AudioSource;
        int[] ladderValues = new int[ladder.arraySize];
        for (int i = 0; i < ladderValues.Length; i++) ladderValues[i] = ladder.GetArrayElementAtIndex(i).intValue;

        // Old screen pieces: switched off, kept in the scene.
        var hide = new List<GameObject>();
        foreach (var n in new[] { "Screen", "Pay Table_0", "ReelCamera" })
        {
            var t = go.transform.Find(n);
            if (t != null) hide.Add(t.gameObject);
        }
        foreach (var reels in go.GetComponentsInChildren<SlotMachineReels>(true))
        {
            if (reels.gameObject == go) Undo.DestroyObjectImmediate(reels);
            else if (!hide.Any(h => reels.transform.IsChildOf(h.transform))) hide.Add(reels.gameObject);
        }
        foreach (var h in hide) h.SetActive(false);

        var levers = Object.FindObjectsByType<SlotMachineLever>(FindObjectsInactive.Include)
            .Where(l => new SerializedObject(l).FindProperty("slotMachine").objectReferenceValue == old).ToList();

        Undo.DestroyObjectImmediate(old);
        var vs = Undo.AddComponent<T>(go);

        var so = new SerializedObject(vs);
        so.FindProperty("betAmount").intValue = bet;
        var lad = so.FindProperty("betLadder");
        lad.arraySize = ladderValues.Length;
        for (int i = 0; i < ladderValues.Length; i++) lad.GetArrayElementAtIndex(i).intValue = ladderValues[i];
        so.FindProperty("buildBetPanel").boolValue = buildPanel;
        so.FindProperty("betPanelOffset").vector3Value = panelOffset;
        so.FindProperty("audioSource").objectReferenceValue = audio;
        so.FindProperty("cabinetRenderer").objectReferenceValue = go.GetComponent<MeshRenderer>();
        var hideProp = so.FindProperty("hideOnStart");
        hideProp.arraySize = hide.Count;
        for (int i = 0; i < hide.Count; i++) hideProp.GetArrayElementAtIndex(i).objectReferenceValue = hide[i];

        SetClip(so, "reelTickClip", "Assets/Audio/Resources/SlotSounds/SlotReelTick.wav");
        SetClip(so, "reelStopClip", "Assets/Audio/Resources/SlotSounds/SlotReelStop.wav");
        SetClip(so, "winClip", "Assets/Audio/Resources/SlotSounds/SlotWin.wav");
        SetClip(so, "bigWinClip", "Assets/Audio/Resources/SlotSounds/SlotBigWin.wav");
        SetClip(so, "bonusChimeClip", "Assets/ithappy/Casino_Free/Sound/Callum Donaldson - Casino Tones - Win Bonus Chimes.wav");
        SetClip(so, "ascendingChimeClip", "Assets/ithappy/Casino_Free/Sound/Callum Donaldson - Casino Tones - Ascending Win Chime.wav");
        if (vs is StarWheelSlot)
        {
            var clacks = so.FindProperty("wheelClackClips");
            clacks.arraySize = 4;
            for (int i = 0; i < 4; i++)
                clacks.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/Roulette/RouletteBallBounce_{i + 1}.wav");
            SetClip(so, "wheelSpinLoopClip", "Assets/Audio/Roulette/Roullete wheel spin sound.wav");
            SetClip(so, "wheelStopClip", "Assets/Audio/Roulette/RouletteBallLand.wav");
        }
        so.ApplyModifiedProperties();

        foreach (var lever in levers)
        {
            var lso = new SerializedObject(lever);
            Undo.RecordObject(lever, "Repoint lever");
            lso.FindProperty("slotMachine").objectReferenceValue = vs;
            lso.ApplyModifiedProperties();
        }

        Undo.RecordObject(go, "Rename");
        go.name = $"{title} Slot ({go.name})";
        Debug.Log($"Video slots: {go.name} converted ({levers.Count} lever(s) repointed, {hide.Count} old screen piece(s) switched off).", go);
    }

    static void SetClip(SerializedObject so, string prop, string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) Debug.LogWarning($"Video slots: sound not found at {path}.");
        so.FindProperty(prop).objectReferenceValue = clip;
    }
}
