using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor.XR.OpenXR.Features;
#endif

// Locks the headset to one refresh rate (XR_FB_display_refresh_rate), so the
// game runs the same on every Quest whatever its system setting says. A Quest
// left on 90 or 120 Hz has to draw 25-65 % more frames than at 72 Hz, and
// VrComfort runs physics at that rate too - enough to drop frames, which
// feels like heavy lag in the headset but doesn't show in a 30 fps recording.
//
// Turned on by Tools > Casino > Apply VR Performance Settings. The rate it
// ended up at is logged ("RefreshRate: ...") - see it with adb logcat.
#if UNITY_EDITOR
[OpenXRFeature(UiName = "Casino: Fixed Refresh Rate",
    BuildTargetGroups = new[] { UnityEditor.BuildTargetGroup.Android },
    Company = "VR-Game",
    Desc = "Requests a fixed display refresh rate (XR_FB_display_refresh_rate) so performance doesn't depend on the headset's refresh-rate setting.",
    OpenxrExtensionStrings = ExtensionName,
    Version = "1.0.0",
    FeatureId = FeatureId)]
#endif
public sealed class RefreshRateFeature : OpenXRFeature
{
    public const string FeatureId = "com.vrgame.feature.refreshrate";
    const string ExtensionName = "XR_FB_display_refresh_rate";

    [Tooltip("Refresh rate to request (Hz). Quest 2: 72, 80, 90 or 120. The closest supported rate at or below this is used.")]
    public float targetRefreshRate = 72f;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetInstanceProcAddrFn(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int EnumerateRatesFn(ulong session, uint capacity, out uint count, [Out] float[] rates);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetRateFn(ulong session, out float rate);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int RequestRateFn(ulong session, float rate);

    EnumerateRatesFn _enumerate;
    GetRateFn _get;
    RequestRateFn _request;

    protected override bool OnInstanceCreate(ulong instance)
    {
        _enumerate = null; _get = null; _request = null;
        if (!OpenXRRuntime.IsExtensionEnabled(ExtensionName))
        {
            Debug.Log("RefreshRate: " + ExtensionName + " not available, leaving the refresh rate alone.");
            return true;
        }
        try
        {
            var getProc = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(xrGetInstanceProcAddr);
            _enumerate = Load<EnumerateRatesFn>(getProc, instance, "xrEnumerateDisplayRefreshRatesFB");
            _get = Load<GetRateFn>(getProc, instance, "xrGetDisplayRefreshRateFB");
            _request = Load<RequestRateFn>(getProc, instance, "xrRequestDisplayRefreshRateFB");
        }
        catch (Exception e)
        {
            Debug.LogWarning("RefreshRate: couldn't load the refresh-rate functions: " + e.Message);
            _enumerate = null; _get = null; _request = null;
        }
        return true;
    }

    static T Load<T>(GetInstanceProcAddrFn getProc, ulong instance, string name) where T : Delegate
    {
        int result = getProc(instance, name, out IntPtr fn);
        if (result < 0 || fn == IntPtr.Zero) throw new Exception(name + " -> " + result);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    protected override void OnSessionBegin(ulong session)
    {
        if (_enumerate == null || _get == null || _request == null) return;
        try
        {
            _get(session, out float before);

            float chosen = 0f, lowest = float.MaxValue;
            if (_enumerate(session, 0, out uint count, null) >= 0 && count > 0)
            {
                var rates = new float[count];
                if (_enumerate(session, count, out count, rates) >= 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (rates[i] < lowest) lowest = rates[i];
                        if (rates[i] <= targetRefreshRate + 0.5f && rates[i] > chosen) chosen = rates[i];
                    }
                }
            }
            if (chosen <= 0f) chosen = lowest < float.MaxValue ? lowest : targetRefreshRate;

            int result = _request(session, chosen);
            _get(session, out float after);
            Debug.Log($"RefreshRate: was {before:0} Hz, requested {chosen:0} Hz (result {result}), now {after:0} Hz.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("RefreshRate: request failed: " + e.Message);
        }
    }
}
