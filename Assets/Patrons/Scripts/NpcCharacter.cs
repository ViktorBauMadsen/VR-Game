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

    public void Say(string line, float seconds = 4.5f)
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

    public void HideSpeech()
    {
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
