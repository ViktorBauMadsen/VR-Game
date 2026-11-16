using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The "drunk" feeling after finishing a drink. Deliberately comfort-safe
// for VR: nothing moves the camera or the world (no sway, no wobble, no
// lens distortion, no FOV change) - it is purely a look and a sound:
// colour fringing, warmer/over-saturated colours with a slow hue drift,
// soft glow, a light vignette, and muffled audio. Fades in and out
// smoothly. Creates itself the first time it's needed; a second drink
// while still drunk extends it and makes it a bit stronger.
public class DrunkEffect : MonoBehaviour
{
    const float MaxSeconds = 20f;
    const float FadeInSeconds = 1.5f;
    const float FadeOutSeconds = 3f;

    public static DrunkEffect Instance { get; private set; }

    Volume _volume;
    ChromaticAberration _chroma;
    ColorAdjustments _color;
    float _timeLeft;
    float _strength;   // what this many drinks should feel like
    float _intensity;  // current, faded

    UniversalAdditionalCameraData _cameraData;
    bool _cameraHadPostProcessing;
    AudioLowPassFilter _lowPass;

    public static void AddDrink(float seconds)
    {
        if (Instance == null) new GameObject("Drunk Effect").AddComponent<DrunkEffect>();
        Instance.Extend(seconds);
    }

    void Awake()
    {
        Instance = this;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _chroma = profile.Add<ChromaticAberration>();
        _chroma.intensity.Override(0.6f);
        _color = profile.Add<ColorAdjustments>();
        _color.saturation.Override(30f);
        _color.hueShift.Override(0f);
        _color.postExposure.Override(0.15f);
        var bloom = profile.Add<Bloom>();
        bloom.intensity.Override(1.5f);
        bloom.threshold.Override(0.75f);
        bloom.scatter.Override(0.8f);
        var vignette = profile.Add<Vignette>();
        vignette.intensity.Override(0.28f);
        vignette.smoothness.Override(0.6f);

        _volume = gameObject.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 100f;
        _volume.sharedProfile = profile;
        _volume.weight = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        End();
    }

    void Extend(float seconds)
    {
        _strength = _timeLeft > 0f ? Mathf.Min(1f, _strength + 0.2f) : 0.6f;
        _timeLeft = Mathf.Min(_timeLeft + seconds, MaxSeconds);
        Begin();
    }

    void Update()
    {
        if (_timeLeft > 0f) _timeLeft -= Time.deltaTime;

        float target = _timeLeft > 0f ? _strength : 0f;
        float speed = target > _intensity ? 1f / FadeInSeconds : 1f / FadeOutSeconds;
        _intensity = Mathf.MoveTowards(_intensity, target, speed * Time.deltaTime);

        _volume.weight = _intensity;
        _chroma.intensity.value = 0.5f + 0.25f * Mathf.Sin(Time.time * 1.7f);
        _color.hueShift.value = 14f * Mathf.Sin(Time.time * 0.8f);
        if (_lowPass != null) _lowPass.cutoffFrequency = Mathf.Lerp(22000f, 1600f, _intensity);

        if (_intensity <= 0f && _timeLeft <= 0f) End();
    }

    // The player camera has post-processing switched off, so it's turned on
    // only for as long as the effect is visible.
    void Begin()
    {
        enabled = true;
        var cam = Camera.main;
        if (cam != null && _cameraData == null)
        {
            _cameraData = cam.GetUniversalAdditionalCameraData();
            _cameraHadPostProcessing = _cameraData.renderPostProcessing;
            _cameraData.renderPostProcessing = true;
        }

        if (_lowPass == null)
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null && !listener.TryGetComponent(out _lowPass))
                _lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
        }
        if (_lowPass != null) _lowPass.enabled = true;
    }

    void End()
    {
        if (_volume != null) _volume.weight = 0f;
        if (_cameraData != null)
        {
            _cameraData.renderPostProcessing = _cameraHadPostProcessing;
            _cameraData = null;
        }
        if (_lowPass != null) _lowPass.enabled = false;
        _strength = 0f;
        enabled = false;
    }
}
