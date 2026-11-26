using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// What every casino guest NPC has in common: an Animator playing the
// Patron.controller (one int parameter, "Pose", picks the animation), a head
// that turns toward a nearby player (humanoid look-at IK), and no shadows
// or physics - they are scenery, cheap to draw in VR.
//
// They can also talk: a speech bubble over the head (made the first time
// they say something), and a body-sized collider with an XRSimpleInteractable
// - select them like Zorp at the token stall (aim at them / reach out and
// press the select button) and they answer with their next line.
//
// The guests are the rigged, colourful ithappy characters, so they never look
// like the casino staff (the bartender, croupier and dealers, who are built
// from smooth spheres and capsules).
[RequireComponent(typeof(Animator))]
public abstract class NpcCharacter : MonoBehaviour
{
    public const int PoseIdle = 0, PoseWalk = 1, PoseSlotSit = 2, PoseSitTalk = 3, PoseSlotWin = 4, PoseSlotLose = 5, PoseIdlePlain = 6;
    static readonly int PoseHash = Animator.StringToHash("Pose");

    protected Animator Anim { get; private set; }
    protected CasinoPatrons Manager { get; private set; }
    protected Transform PlayerHead => Manager != null ? Manager.PlayerHead : null;

    [Tooltip("How far away the player can be before this NPC turns to look at them.")]
    public float lookDistance = 3f;

    float _lookWeight;
    int _pose = -1;
    WorldTextPanel _bubble;
    float _speechLeft;
    Transform _headBone;

    public virtual void Init(CasinoPatrons manager, RuntimeAnimatorController controller)
    {
        Manager = manager;
        Anim = GetComponent<Animator>();
        Anim.runtimeAnimatorController = controller;
        Anim.applyRootMotion = false;
        Anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
        foreach (var c in GetComponentsInChildren<Collider>(true)) Destroy(c);
        SkinnedBounds.Encompass(transform);
        AddTalkTarget();
    }

    // ------------------------------------------------------------ talking

    void AddTalkTarget()
    {
        var talk = new GameObject("Talk");
        talk.layer = gameObject.layer;
        talk.transform.SetParent(transform, false);
        float s = Mathf.Max(0.01f, transform.lossyScale.y);
        var capsule = talk.AddComponent<CapsuleCollider>(); // before the interactable, which collects its colliders on Awake
        capsule.height = 1.7f / s;
        capsule.radius = 0.28f / s;
        capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
        var interactable = talk.AddComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnTalkSelected);
    }

    void OnTalkSelected(SelectEnterEventArgs args)
    {
        if (args.interactorObject is XRBaseInputInteractor input) input.SendHapticImpulse(0.25f, 0.05f);
        OnTalkedTo();
    }

    // The player selected this NPC: say something to them.
    protected virtual void OnTalkedTo() { }

    protected Transform HeadBone
    {
        get
        {
            if (_headBone == null && Anim != null && Anim.isHuman) _headBone = Anim.GetBoneTransform(HumanBodyBones.Head);
            return _headBone;
        }
    }

    public Vector3 HeadPosition => HeadBone != null ? HeadBone.position : transform.position + Vector3.up * 1.55f * transform.lossyScale.y;

    protected bool Speaking => _speechLeft > 0f;

    // The recorded voice for this NPC's lines, if they have one (see NpcVoice).
    protected NpcVoice Voice { get; set; }
    // How long the bubble stays up after a voiced line ends.
    const float AfterSpeechHold = 0.5f;
    AudioSource _voiceSource;

    // Says a line: its recorded clip if Voice has one (the bubble then stays
    // up for the clip plus AfterSpeechHold), otherwise just the bubble.
    // Returns how long the line takes.
    public float Speak(string line)
    {
        AudioClip clip = Voice != null ? Voice.ClipFor(line) : null;
        if (clip == null)
        {
            float seconds = ReadTime(line);
            Say(line, seconds);
            return seconds;
        }

        if (_voiceSource == null) _voiceSource = CharacterVoice.Create(HeadBone != null ? HeadBone : transform, 12f / 3f);
        CharacterVoice.Play(_voiceSource, clip);
        StopMumble();
        ShowBubble(line, clip.length + AfterSpeechHold);
        return clip.length;
    }

    // Shows a line without a recording (with the quiet mumble, if Voice has
    // one). Anything they were saying is cut off.
    public void Say(string line, float seconds = 4.5f)
    {
        if (_voiceSource != null) _voiceSource.Stop();
        ShowBubble(line, seconds);
        StartMumble(Mathf.Min(MumbleTime(line), seconds));
    }

    // ------------------------------------------------------------ mumble

    AudioSource _mumbleSource;
    bool _mumbleFading;
    float _mumbleLeft;

    // How long the mumble runs for a line: about as long as saying it would
    // take (the bubble stays up longer, for reading).
    public static float MumbleTime(string line) => Mathf.Clamp(line.Length * 0.06f, 0.6f, 3.5f);

    void StartMumble(float seconds)
    {
        _mumbleLeft = seconds;
        AudioClip clip = Voice != null ? Voice.mumble : null;
        if (clip == null) return;
        if (_mumbleSource == null)
        {
            Transform mouth = HeadBone != null ? HeadBone : transform;
            _mumbleSource = mouth.gameObject.AddComponent<AudioSource>();
            _mumbleSource.playOnAwake = false;
            _mumbleSource.loop = true;
            _mumbleSource.spatialBlend = 1f;
            _mumbleSource.dopplerLevel = 0f;
            _mumbleSource.rolloffMode = AudioRolloffMode.Linear; // silent at maxDistance
            _mumbleSource.minDistance = 0.5f;
            _mumbleSource.clip = clip;
        }
        _mumbleSource.maxDistance = Mathf.Max(0.6f, Voice.mumbleRange);
        _mumbleSource.volume = Voice.mumbleVolume;
        _mumbleFading = false;
        if (!_mumbleSource.isPlaying)
        {
            _mumbleSource.Play();
            // From a random point, so guests near each other don't mumble in step.
            _mumbleSource.time = Random.Range(0f, Mathf.Max(0f, clip.length - seconds - 0.2f));
        }
    }

    // A quick fade rather than a hard cut.
    void StopMumble()
    {
        if (_mumbleSource != null && _mumbleSource.isPlaying) _mumbleFading = true;
    }

    void UpdateMumble()
    {
        if (_mumbleLeft > 0f)
        {
            _mumbleLeft -= Time.deltaTime;
            if (_mumbleLeft <= 0f) StopMumble();
        }
        if (!_mumbleFading || _mumbleSource == null) return;
        float full = Voice != null ? Voice.mumbleVolume : 0.1f;
        _mumbleSource.volume = Mathf.MoveTowards(_mumbleSource.volume, 0f, full * Time.deltaTime / 0.15f);
        if (_mumbleSource.volume <= 0f)
        {
            _mumbleSource.Stop();
            _mumbleFading = false;
        }
    }

    void ShowBubble(string line, float seconds)
    {
        if (_bubble == null)
        {
            // Over the head as it is now (sitting or standing), a constant size in the world.
            Vector3 local = transform.InverseTransformPoint(HeadPosition + Vector3.up * 0.42f);
            local.x = 0f;
            local.z = 0f;
            float s = Mathf.Max(0.01f, transform.lossyScale.x);
            _bubble = WorldTextPanel.Create(name + " Speech", transform, local, new Vector2(0.9f, 0.3f) / s,
                new Vector2Int(768, 256), new Color(1f, 0.95f, 0.85f), new Color(0.04f, 0.04f, 0.06f), 110f);
        }
        _bubble.SetText(line);
        _bubble.Visible = true;
        _speechLeft = seconds;
        FaceBubble();
    }

    // Hides the bubble (when its time is up, or when cut short).
    public void HideSpeech()
    {
        if (_speechLeft > 0f && _voiceSource != null) _voiceSource.Stop();
        StopMumble();
        _speechLeft = 0f;
        if (_bubble != null) _bubble.Visible = false;
    }

    // How long a line stays up: long enough to read.
    public static float ReadTime(string line) => Mathf.Clamp(1.8f + line.Length * 0.055f, 2.5f, 5.5f);

    protected virtual void LateUpdate()
    {
        if (_speechLeft > 0f)
        {
            _speechLeft -= Time.deltaTime;
            if (_speechLeft <= 0f) HideSpeech();
        }
        UpdateMumble();
        FaceBubble();
    }

    // Speech bubble faces the player (a Quad's visible face is its -Z).
    void FaceBubble()
    {
        if (_bubble == null || !_bubble.Visible || PlayerHead == null) return;
        Vector3 away = _bubble.transform.position - PlayerHead.position;
        away.y = 0f;
        if (away.sqrMagnitude > 0.001f) _bubble.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    // ------------------------------------------------------------ animation

    protected void SetPose(int pose)
    {
        if (pose == _pose || Anim == null) return;
        _pose = pose;
        Anim.SetInteger(PoseHash, pose);
    }

    protected int Pose => _pose;

    // Head (and a little of the upper body) toward the player when they are
    // close and roughly in front.
    protected virtual bool WantsToLook(out Vector3 target)
    {
        target = default;
        var head = PlayerHead;
        if (head == null) return false;
        Vector3 to = head.position - transform.position;
        if (to.sqrMagnitude > lookDistance * lookDistance) return false;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f || Vector3.Dot(transform.forward, to.normalized) < 0.1f) return false;
        target = head.position;
        return true;
    }

    void OnAnimatorIK(int layer)
    {
        if (Anim == null) return;
        bool look = WantsToLook(out Vector3 target);
        _lookWeight = Mathf.MoveTowards(_lookWeight, look ? 1f : 0f, Time.deltaTime * 1.5f);
        Anim.SetLookAtWeight(_lookWeight, 0.15f, 0.8f, 1f, 0.6f);
        if (look) Anim.SetLookAtPosition(target);
        else if (_lookWeight > 0f) Anim.SetLookAtPosition(transform.position + transform.forward * 2f + Vector3.up * 1.5f);
        OnIK();
    }

    // Extra IK (hands etc.) for NPCs that need it, after the look-at.
    protected virtual void OnIK() { }
}
