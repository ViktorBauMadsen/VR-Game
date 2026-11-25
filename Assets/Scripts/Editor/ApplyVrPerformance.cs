using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

// Tools > Casino > Apply VR Performance Settings
//
// Render settings tuned for a sharp, steady picture in the headset (the
// Quest uses the "Mobile" quality level / Mobile_RPAsset; PC VR "PC"):
//
//   Render scale 0.8 -> 1.0      the eye buffers were drawn at 80 % and
//                                stretched: the main cause of the blur.
//   MSAA off -> 4x               jagged edges crawl and shimmer as your head
//                                moves; 4x MSAA is nearly free on Quest GPUs.
//   HDR on -> off                HDR adds an extra full-screen copy every frame
//                                on Quest and nothing here uses it (no post).
//   Shadows 50 m -> 20 m         one 1024 shadow map stretched over 50 m was
//                                so coarse that shadow edges flickered.
//   Lights per object 4 -> 8     with more lights than that near an object,
//                                Unity drops one - lighting popped on and off.
//   Foveated rendering on        (OpenXR feature, Android) - level is set at
//                                runtime by VrComfort.
//   Fixed refresh rate on        (RefreshRateFeature, Android) - 72 Hz on
//                                every headset, whatever its system setting.
//
// VrComfort fixes the camera's depth precision (z-fighting flicker) and runs
// physics at the headset's refresh rate at runtime.
public static class ApplyVrPerformance
{
    [MenuItem("Tools/Casino/Apply VR Performance Settings")]
    public static void Apply()
    {
        var done = new List<string>();
        var assets = new HashSet<RenderPipelineAsset>();
        for (int i = 0; i < QualitySettings.count; i++)
        {
            var asset = QualitySettings.GetRenderPipelineAssetAt(i);
            if (asset != null) assets.Add(asset);
        }
        if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);

        foreach (var asset in assets)
        {
            bool mobile = asset.name.Contains("Mobile");
            var so = new SerializedObject(asset);
            Set(so, "m_RenderScale", 1f);
            Set(so, "m_MSAA", 4);
            Set(so, "m_SupportsHDR", false);
            Set(so, "m_ShadowDistance", 20f);
            Set(so, "m_AdditionalLightsPerObjectLimit", 8);
            if (mobile)
            {
                Set(so, "m_ShadowCascadeCount", 1);
                Set(so, "m_MainLightShadowmapResolution", 1024);
                Set(so, "m_SoftShadowsSupported", false);
            }
            else Set(so, "m_ShadowCascadeCount", 2);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            done.Add(asset.name);
        }

        UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        bool foveation = false, refreshRate = false;
        if (openxr != null)
        {
            var rr = openxr.GetFeature<RefreshRateFeature>();
            if (rr != null)
            {
                rr.enabled = true;
                EditorUtility.SetDirty(rr);
                refreshRate = true;
            }

            var feature = openxr.GetFeature<FoveatedRenderingFeature>();
            if (feature != null)
            {
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(openxr);
                foveation = true;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"ApplyVrPerformance: updated {string.Join(", ", done)} (render scale 1, MSAA 4x, HDR off, 20 m shadows, 8 lights/object). Foveated rendering {(foveation ? "on" : "not found")}. Fixed refresh rate {(refreshRate ? "on" : "not found")}.");
    }

    static void Set(SerializedObject so, string name, float value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("ApplyVrPerformance: no " + name); return; }
        p.floatValue = value;
    }

    static void Set(SerializedObject so, string name, int value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("ApplyVrPerformance: no " + name); return; }
        p.intValue = value;
    }

    static void Set(SerializedObject so, string name, bool value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("ApplyVrPerformance: no " + name); return; }
        p.boolValue = value;
    }
}
