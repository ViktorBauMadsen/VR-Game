using System.Collections;
using UnityEngine;

// Handles the betting/payout logic for a slot machine. Call Spin() to play
// one round: the bet is deducted immediately, a weighted random outcome is
// rolled against winTiers up front, the reels animate onto that result one
// reel at a time, and the payout happens once the last reel has stopped.
public class SlotMachine : MonoBehaviour
{
    [Header("Bet")]
    [SerializeField] int betAmount = 10; // starting bet; the player changes it with the bet panel
    [SerializeField] int minBet = 10;
    [SerializeField] int maxBet = 100;
    [SerializeField] int betStep = 10;
    [Tooltip("Build the -/+ bet panel under the reels at runtime.")]
    [SerializeField] bool buildBetPanel = true;
    [Tooltip("Nudge the bet panel, in meters: x = right, y = up, z = toward the player.")]
    [SerializeField] Vector3 betPanelOffset = Vector3.zero;

    [Header("Rules")]
    [SerializeField] float payoutDelay = 0.5f; // only used if no reels are assigned
    [SerializeField] float loseWeight = 60f; // relative chance of no win at all - 40% chance of winning something
    [SerializeField] SlotMachineReels reels;

    [Header("Sound")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip spinStartClip; // plays right as the reels start spinning
    [Tooltip("Short tick played as the reels shift symbols. Empty = uses the generated default.")]
    [SerializeField] AudioClip reelTickClip;
    [Tooltip("Clunk played each time a reel stops. Empty = uses the generated default.")]
    [SerializeField] AudioClip reelStopClip;
    [SerializeField, Range(0f, 1f)] float reelTickVolume = 0.25f;
    [Tooltip("Ticks closer together than this are skipped, so 4 fast reels don't turn into a buzz.")]
    [SerializeField] float minTickGap = 0.05f;
    [SerializeField, Range(0f, 1f)] float reelStopVolume = 0.9f;
    [Tooltip("Each reel's stop sound is pitched up by this much more than the one before it (1... 2... 3... 4).")]
    [SerializeField] float reelStopPitchStep = 0.06f;
    [Tooltip("Played when the last reel stops on a win. Empty = uses the generated default.")]
    [SerializeField] AudioClip winClip;
    [Tooltip("Played instead of the normal win sound for wins paying at least Big Win Multiplier x the bet. Empty = uses the generated default.")]
    [SerializeField] AudioClip bigWinClip;
    [SerializeField] float bigWinMultiplier = 8f; // Bell, BAR and Seven by default
    [SerializeField, Range(0f, 1f)] float winVolume = 0.8f;

    [SerializeField] PayoutTier[] winTiers =
    {
        new PayoutTier { label = "Cherry", weight = 15f, payoutMultiplier = 0.5f },
        new PayoutTier { label = "Lemon", weight = 10f, payoutMultiplier = 1f },
        new PayoutTier { label = "Blackberry", weight = 7f, payoutMultiplier = 2f },
        new PayoutTier { label = "Watermelon", weight = 4f, payoutMultiplier = 4f },
        new PayoutTier { label = "Bell", weight = 2f, payoutMultiplier = 8f },
        new PayoutTier { label = "BAR", weight = 1.5f, payoutMultiplier = 15f },
        new PayoutTier { label = "Seven", weight = 0.5f, payoutMultiplier = 50f },
    };

    // Generated default sounds live in Assets/Audio/Resources/SlotSounds.
    const string DefaultTickPath = "SlotSounds/SlotReelTick";
    const string DefaultStopPath = "SlotSounds/SlotReelStop";
    const string DefaultWinPath = "SlotSounds/SlotWin";
    const string DefaultBigWinPath = "SlotSounds/SlotBigWin";

    bool _isSpinning;
    float _lastTickTime = -1f;
    // Separate sources: the stop sound's per-reel pitch must not bend the
    // ticks, win jingle (or the spin-start clip) playing at the same time.
    AudioSource _tickSource, _stopSource, _winSource;

    public bool IsSpinning => _isSpinning;
    public int BetAmount => betAmount;
    public bool CanBetDown => !_isSpinning && betAmount > minBet;
    public bool CanBetUp => !_isSpinning && betAmount < maxBet;

    public event System.Action<int> BetChanged;      // new bet amount
    public event System.Action SpinStateChanged;     // started or finished spinning
    public event System.Action SpinRejected;         // not enough balance for the bet

    // steps = +1 to raise the bet by betStep, -1 to lower it. Locked mid-spin.
    public void ChangeBet(int steps)
    {
        if (_isSpinning) return;
        int newBet = ClampBet(betAmount + steps * betStep);
        if (newBet == betAmount) return;
        betAmount = newBet;
        BetChanged?.Invoke(betAmount);

        if (_tickSource != null && reelTickClip != null)
        {
            Apply2DIfNoHeadset(_tickSource);
            _tickSource.PlayOneShot(reelTickClip, 0.8f);
        }
    }

    int ClampBet(int bet)
    {
        bet = Mathf.Clamp(bet, minBet, maxBet);
        if (betStep > 0) bet = minBet + Mathf.RoundToInt((bet - minBet) / (float)betStep) * betStep;
        return Mathf.Clamp(bet, minBet, maxBet);
    }

    void Awake()
    {
        betAmount = ClampBet(betAmount);

        if (reelTickClip == null) reelTickClip = Resources.Load<AudioClip>(DefaultTickPath);
        if (reelStopClip == null) reelStopClip = Resources.Load<AudioClip>(DefaultStopPath);
        if (winClip == null) winClip = Resources.Load<AudioClip>(DefaultWinPath);
        if (bigWinClip == null) bigWinClip = Resources.Load<AudioClip>(DefaultBigWinPath);

        _tickSource = CreateReelSource();
        _stopSource = CreateReelSource();
        _winSource = CreateReelSource();
    }

    // Matches the machine's own AudioSource (3D settings etc.) if it has one.
    AudioSource CreateReelSource()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        if (audioSource != null)
        {
            s.spatialBlend = audioSource.spatialBlend;
            s.minDistance = audioSource.minDistance;
            s.maxDistance = audioSource.maxDistance;
            s.rolloffMode = audioSource.rolloffMode;
            s.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
        }
        else
        {
            s.spatialBlend = 1f;
        }
        return s;
    }

    void OnEnable()
    {
        if (reels == null) return;
        reels.ReelTicked += HandleReelTick;
        reels.ReelStopped += HandleReelStopped;
    }

    void OnDisable()
    {
        if (reels == null) return;
        reels.ReelTicked -= HandleReelTick;
        reels.ReelStopped -= HandleReelStopped;
    }

    void Start()
    {
        if (reels == null) return;
        var sprites = new Sprite[winTiers.Length];
        for (int i = 0; i < winTiers.Length; i++) sprites[i] = winTiers[i].symbol;
        reels.SetSymbolPool(sprites);

        if (buildBetPanel) SlotBetPanel.Build(this, reels, betPanelOffset);
    }

    public void Spin()
    {
        if (_isSpinning) return; // ignore pulls while the reels are still going

        if (PlayerBalance.Instance == null)
        {
            Debug.LogWarning("SlotMachine: no PlayerBalance found in the scene.");
            return;
        }

        int bet = betAmount;
        if (!PlayerBalance.Instance.TrySpend(bet))
        {
            Debug.Log("SlotMachine: not enough balance to spin.");
            SpinRejected?.Invoke();
            return;
        }

        StartCoroutine(ResolveSpin(bet));
    }

    IEnumerator ResolveSpin(int bet)
    {
        _isSpinning = true;
        SpinStateChanged?.Invoke();

        // Roll the outcome up front; the reels then animate onto it.
        var tier = RollOutcome();
        if (audioSource != null && spinStartClip != null) audioSource.PlayOneShot(spinStartClip);

        if (reels != null)
        {
            reels.PlaySpin(tier);
            yield return new WaitUntil(() => !reels.IsSpinning);
        }
        else
        {
            yield return new WaitForSeconds(payoutDelay);
        }

        if (tier != null)
        {
            int payout = Mathf.RoundToInt(bet * tier.payoutMultiplier);
            PlayerBalance.Instance.AddBalance(payout);
            PlayWinSound(tier);
            Debug.Log($"SlotMachine: {tier.label}! Won {payout}.");
        }
        else
        {
            Debug.Log("SlotMachine: no win this spin.");
        }

        _isSpinning = false;
        SpinStateChanged?.Invoke();
    }

    // Test without VR: in Play mode, ⋮ menu on this component > Debug Spin.
    // Works exactly like pulling the lever (bet is taken, payout is paid).
    [ContextMenu("Debug Spin")]
    void DebugSpin()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("SlotMachine: Debug Spin only works in Play mode.");
            return;
        }
        Spin();
    }

    // Shows where the bet panel will appear (yellow box) when this machine
    // is selected, so betPanelOffset can be tuned without entering Play mode.
    void OnDrawGizmosSelected()
    {
        if (!buildBetPanel || reels == null) return;
        if (!SlotBetPanel.TryComputePose(this, reels, betPanelOffset, out Vector3 pos, out Quaternion rot)) return;
        Gizmos.color = Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(pos, rot, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.345f, 0.085f, 0.02f));
    }

    [ContextMenu("Debug Bet Up")]
    void DebugBetUp() => ChangeBet(+1);

    [ContextMenu("Debug Bet Down")]
    void DebugBetDown() => ChangeBet(-1);

    void HandleReelTick(int reel)
    {
        if (_tickSource == null || reelTickClip == null) return;
        if (Time.time - _lastTickTime < minTickGap) return;
        Apply2DIfNoHeadset(_tickSource);
        _lastTickTime = Time.time;
        _tickSource.PlayOneShot(reelTickClip, reelTickVolume * Random.Range(0.85f, 1f));
    }

    void HandleReelStopped(int reel)
    {
        if (_stopSource == null || reelStopClip == null) return;
        Apply2DIfNoHeadset(_stopSource);
        _stopSource.pitch = 1f + reel * reelStopPitchStep;
        _stopSource.PlayOneShot(reelStopClip, reelStopVolume);
    }

    void PlayWinSound(PayoutTier tier)
    {
        var clip = tier.payoutMultiplier >= bigWinMultiplier && bigWinClip != null ? bigWinClip : winClip;
        if (_winSource == null || clip == null) return;
        Apply2DIfNoHeadset(_winSource);
        _winSource.PlayOneShot(clip, winVolume);
    }

    // Testing without a headset, the listener (XR camera) stays at the rig's
    // spawn point, maybe far from this machine - play reel sounds 2D then.
    // With a headset on they keep the machine's normal 3D setting.
    void Apply2DIfNoHeadset(AudioSource s)
    {
        float normal = audioSource != null ? audioSource.spatialBlend : 1f;
        s.spatialBlend = UnityEngine.XR.XRSettings.isDeviceActive ? normal : 0f;
    }

    PayoutTier RollOutcome()
    {
        float totalWeight = loseWeight;
        foreach (var t in winTiers) totalWeight += t.weight;

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        foreach (var t in winTiers)
        {
            cumulative += t.weight;
            if (roll < cumulative) return t;
        }
        return null; // landed in the "lose" portion of the range
    }
}

[System.Serializable]
public class PayoutTier
{
    public string label = "Win";
    public float weight = 10f;
    public float payoutMultiplier = 2f;
    public Sprite symbol; // the symbol image shown on the reel for this tier
}
