using UnityEngine;

// Sound effects for the roulette wheel. Put this on the same object as
// RouletteWheel. When added, it fills in the wheel, ball and the generated
// bounce/land clips from Assets/Audio/Roulette by itself. The roll and
// wheel loop slots start empty (silent) - drag recorded loops in to use them.
//
//  - Roll loop: plays from the ball while it rolls; volume and pitch follow
//    its speed, so it fades as the ball slows.
//  - Wheel loop: a soft whir from the wheel, fading as the wheel slows.
//  - Bounce clips: one random clip per hit, quieter for softer hits.
//  - Land clip: the final rattle as the ball settles in its pocket.
//
// All sounds are 3D, so in VR they come from the wheel/ball.
[DisallowMultipleComponent]
public class RouletteAudio : MonoBehaviour
{
    [SerializeField] RouletteWheel wheel;

    [Header("Clips")]
    [SerializeField] AudioClip rollLoop;
    [SerializeField] AudioClip wheelLoop;
    [SerializeField] AudioClip[] bounceClips;
    [SerializeField] AudioClip landClip;

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] float rollVolume = 0.6f;
    [Tooltip("How much quieter the spin sound gets as the ball slows before bouncing. 0 = stays at full volume.")]
    [SerializeField, Range(0f, 1f)] float rollSlowdownVolumeDrop = 0.3f;
    [Tooltip("Seconds the spin sound takes to fade out once the ball starts bouncing in the pockets.")]
    [SerializeField] float rollFadeOutTime = 0.8f;
    [Tooltip("Off = play the roll clip at its normal pitch (best for recorded sounds). On = pitch follows ball speed using the range below.")]
    [SerializeField] bool rollPitchFollowsSpeed = false;
    [SerializeField] Vector2 rollPitchRange = new Vector2(0.75f, 1.2f); // slow .. fast
    [SerializeField, Range(0f, 1f)] float wheelVolume = 0.2f;
    [SerializeField] Vector2 wheelPitchRange = new Vector2(0.6f, 1f);
    [SerializeField, Range(0f, 1f)] float bounceVolume = 0.8f;
    [SerializeField] float bouncePitchJitter = 0.08f;
    [SerializeField, Range(0f, 1f)] float landVolume = 0.7f;
    [SerializeField] float fadeSpeed = 6f; // how fast loop volumes follow their targets

    [Header("3D sound")]
    [SerializeField] float minDistance = 0.3f;
    [SerializeField] float maxDistance = 10f;
    [Tooltip("Testing without a headset, the listener (XR camera) sits wherever the rig spawned, often far from the wheel. With this on, sounds play at full volume (2D) whenever no headset is active, and switch back to 3D with the headset on.")]
    [SerializeField] bool use2DWhenNoHeadset = true;

    RouletteBall _ball;
    AudioSource _rollSource, _wheelSource, _hitSource;

    void Awake()
    {
        if (wheel == null) wheel = GetComponent<RouletteWheel>();
        _ball = wheel != null ? wheel.Ball : null;
        if (wheel == null || _ball == null)
        {
            Debug.LogWarning("RouletteAudio: needs a RouletteWheel with a Ball assigned.");
            enabled = false;
            return;
        }

        _rollSource = CreateSource(_ball.gameObject, rollLoop, true);
        _hitSource = CreateSource(_ball.gameObject, null, false);
        _wheelSource = CreateSource(gameObject, wheelLoop, true);
    }

    void OnEnable()
    {
        if (_ball == null) return;
        _ball.Bounced += HandleBounce;
        _ball.Landed += HandleLanded;
    }

    void OnDisable()
    {
        if (_ball == null) return;
        _ball.Bounced -= HandleBounce;
        _ball.Landed -= HandleLanded;
    }

    void Update()
    {
        float blend = use2DWhenNoHeadset && !UnityEngine.XR.XRSettings.isDeviceActive ? 0f : 1f;
        _rollSource.spatialBlend = _wheelSource.spatialBlend = _hitSource.spatialBlend = blend;

        // Spin sound: on while the ball spins around the track and drops in,
        // then a smooth fade-out once it starts bouncing on the pockets.
        float speed = _ball.RollSpeed01;
        float rollTarget = _ball.IsRolling ? rollVolume * Mathf.Lerp(1f - rollSlowdownVolumeDrop, 1f, speed) : 0f;
        float rollPitch = rollPitchFollowsSpeed ? Mathf.Lerp(rollPitchRange.x, rollPitchRange.y, speed) : 1f;
        float rollRate = _ball.IsRolling ? fadeSpeed : rollVolume / Mathf.Max(0.01f, rollFadeOutTime);
        UpdateLoop(_rollSource, rollTarget, rollPitch, rollRate);

        float wheelSpeed = Mathf.Clamp01(wheel.WheelSpeed01);
        UpdateLoop(_wheelSource, wheelVolume * wheelSpeed, Mathf.Lerp(wheelPitchRange.x, wheelPitchRange.y, wheelSpeed), fadeSpeed);
    }

    // Starts the clip from the beginning when it fades in, stops it once
    // fully faded out - so every spin starts the sound fresh.
    void UpdateLoop(AudioSource source, float targetVolume, float pitch, float fadeRate)
    {
        if (source.clip == null) return;
        source.volume = Mathf.MoveTowards(source.volume, targetVolume, fadeRate * Time.deltaTime);
        source.pitch = pitch;
        if (source.volume > 0.001f)
        {
            if (!source.isPlaying) source.Play();
        }
        else if (source.isPlaying)
        {
            source.Stop();
        }
    }

    void HandleBounce(float strength)
    {
        if (bounceClips == null || bounceClips.Length == 0) return;
        var clip = bounceClips[Random.Range(0, bounceClips.Length)];
        if (clip == null) return;
        _hitSource.pitch = 1f + Random.Range(-bouncePitchJitter, bouncePitchJitter);
        _hitSource.PlayOneShot(clip, bounceVolume * strength);
    }

    void HandleLanded()
    {
        if (landClip == null) return;
        _hitSource.pitch = 1f;
        _hitSource.PlayOneShot(landClip, landVolume);
    }

    AudioSource CreateSource(GameObject host, AudioClip clip, bool loop)
    {
        var s = host.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.playOnAwake = false;
        s.volume = 0f;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.minDistance = minDistance;
        s.maxDistance = maxDistance;
        s.dopplerLevel = 0f; // the ball moves fast; doppler would warble the pitch
        if (!loop) s.volume = 1f;
        return s;
    }

#if UNITY_EDITOR
    // Runs when the component is added (or Reset from its ⋮ menu): fill in
    // everything automatically so it works with no manual setup.
    void Reset()
    {
        wheel = GetComponent<RouletteWheel>();
        const string dir = "Assets/Audio/Roulette/";
        // No generated roll/whir loops (they sounded bad) - drop recorded
        // loops into those slots if you find good ones; empty slots stay silent.
        landClip = Load(dir + "RouletteBallLand.wav");
        bounceClips = new[]
        {
            Load(dir + "RouletteBallBounce_1.wav"),
            Load(dir + "RouletteBallBounce_2.wav"),
            Load(dir + "RouletteBallBounce_3.wav"),
            Load(dir + "RouletteBallBounce_4.wav"),
        };
    }

    static AudioClip Load(string path) => UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
#endif
}
