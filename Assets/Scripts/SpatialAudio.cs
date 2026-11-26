using System.Collections.Generic;
using UnityEngine;

// How the casino's game sounds (machines, tables, the bar, darts...) carry:
// full volume within `near` metres, a natural falloff after that, and
// silent past `far` - so you only hear a machine when you're near it.
// Unity's own 3D default (logarithmic, out to 500 m) never quite goes quiet.
public static class SpatialAudio
{
    public const float Near = 1f, Far = 12f;

    static readonly Dictionary<(float, float), AnimationCurve> Curves = new Dictionary<(float, float), AnimationCurve>();

    public static void Setup(AudioSource source, float near = Near, float far = Far)
    {
        if (source == null) return;
        far = Mathf.Max(far, near + 0.1f);
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.minDistance = near;
        source.maxDistance = far;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Curve(near, far));
    }

    // In place of AudioSource.PlayClipAtPoint, with the falloff above.
    // priority: lower = more important (Unity's default is 128); the most
    // important sounds keep playing when too many play at once.
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float near = Near, float far = Far, int priority = 128)
    {
        if (clip == null) return;
        var go = new GameObject("One Shot Audio");
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        Setup(source, near, far);
        source.priority = priority;
        source.playOnAwake = false;
        source.clip = clip;
        source.volume = volume;
        source.Play();
        Object.Destroy(go, clip.length + 0.1f);
    }

    // The custom rolloff curve runs over distance / far (0..1). It follows
    // the natural near/distance falloff, eased down to exactly 0 at `far`.
    static AnimationCurve Curve(float near, float far)
    {
        if (Curves.TryGetValue((near, far), out var cached)) return cached;
        var curve = new AnimationCurve();
        float start = near / far;
        curve.AddKey(0f, 1f);
        const int steps = 16;
        for (int i = 0; i <= steps; i++)
        {
            float x = Mathf.Lerp(start, 1f, (float)i / steps);
            float d = x * far;
            float natural = near / d, atFar = near / far;
            curve.AddKey(x, Mathf.Clamp01((natural - atFar) / (1f - atFar)));
        }
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        Curves[(near, far)] = curve;
        return curve;
    }
}
