using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

// Runtime fixes for a steadier, sharper picture in the headset:
//
//   - Depth precision. The XR rig's camera came with a 1 cm near plane and a
//     1 km far plane, so the depth buffer couldn't tell apart surfaces a few
//     millimetres apart (rugs on the floor, panels on the walls, chips on the
//     felt) and they flickered through each other as you moved your head.
//     4 cm / 400 m is ten times more precise and still lets you hold a cigar
//     to your mouth and see to the end of the desert.
//   - Physics at the display rate. Physics ran at 50 Hz on a 72-120 Hz
//     headset, so anything held or thrown stuttered against your hand. The
//     fixed timestep now follows the headset's refresh rate.
//   - Foveated rendering (when the OpenXR Foveated Rendering feature is on -
//     Tools > Casino > Apply VR Performance Settings turns it on): the edges
//     of each eye, which you can't see sharply anyway, are drawn at lower
//     resolution - a big GPU saving on Quest.
//
// Installs itself in every scene; nothing to set up.
public sealed class VrComfort : MonoBehaviour
{
    const float NearPlane = 0.04f, FarPlane = 400f;
    [Range(0f, 1f)] public float foveation = 0.6f;

    readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();
    float _nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        Install();
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    static void Install()
    {
        if (FindAnyObjectByType<VrComfort>() != null) return;
        var go = new GameObject("VR Comfort");
        DontDestroyOnLoad(go);
        go.AddComponent<VrComfort>();
    }

    void Update()
    {
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 1f;

        var cam = Camera.main;
        if (cam != null)
        {
            if (cam.nearClipPlane < NearPlane) cam.nearClipPlane = NearPlane;
            if (cam.farClipPlane > FarPlane) cam.farClipPlane = FarPlane;
        }

        SubsystemManager.GetSubsystems(_displays);
        foreach (var display in _displays)
        {
            if (!display.running) continue;
            if (display.TryGetDisplayRefreshRate(out float hz) && hz >= 50f)
            {
                float step = Mathf.Clamp(1f / hz, 1f / 120f, 1f / 50f);
                if (Mathf.Abs(Time.fixedDeltaTime - step) > 0.0001f) Time.fixedDeltaTime = step;
            }
            if (Mathf.Abs(display.foveatedRenderingLevel - foveation) > 0.001f)
            {
                display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                display.foveatedRenderingLevel = foveation;
            }
        }
    }
}
