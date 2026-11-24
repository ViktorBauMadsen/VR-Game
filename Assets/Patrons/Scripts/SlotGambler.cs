using System.Collections;
using UnityEngine;

// The regular: always on the same stool at the same slot machine, feeding it.
// Every so often he pulls the lever and the machine really spins (as a
// "remote" spin - nobody's money changes), then he celebrates or sulks. He
// waits while a player is standing right at his machine, so he never gets in
// the way of someone who wants a go.
public class SlotGambler : NpcCharacter
{
    SlotMachineBase _machine;
    SlotMachineLever _lever;
    Vector3 _machineFront;
    float _nextSpin;
    bool _busy;

    public void Init(CasinoPatrons manager, RuntimeAnimatorController controller, SlotMachineBase machine, Vector3 machineFront)
    {
        Init(manager, controller);
        _machine = machine;
        _machineFront = machineFront;
        foreach (var lever in FindObjectsByType<SlotMachineLever>())
            if (lever.Machine == machine) { _lever = lever; break; }
        lookDistance = 2f;
        SetPose(PoseSlotSit);
        _nextSpin = Time.time + Random.Range(4f, 9f);
    }

    void Update()
    {
        if (_busy || _machine == null || Time.time < _nextSpin) return;
        _nextSpin = Time.time + Random.Range(9f, 18f);

        // A player at the machine (or it is already spinning): leave it be.
        var head = PlayerHead;
        if (_machine.IsSpinning) return;
        if (head != null)
        {
            Vector3 to = head.position - _machineFront;
            to.y = 0f;
            if (to.sqrMagnitude < 1.1f * 1.1f) return;
        }
        StartCoroutine(Play());
    }

    // Her hand on the lever: IK toward the lever's knob (it moves with the
    // lever, so the hand rides it down), leaning in a little to reach.
    Transform _hand, _finger;
    AvatarIKGoal _goal;
    Vector3 _gripOffset;
    float _reach;

    static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

    void FindHand()
    {
        if (_lever == null || Anim == null || !Anim.isHuman) return;
        bool right = Vector3.Dot(_lever.KnobPosition - transform.position, transform.right) >= 0f;
        _goal = right ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
        _hand = Anim.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        _finger = Anim.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
    }

    IEnumerator Ramp(System.Action<float> set, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            set(Smooth(t / seconds));
            yield return null;
        }
        set(1f);
    }

    IEnumerator Play()
    {
        _busy = true;
        if (_hand == null) FindHand();
        if (_lever != null && _hand != null)
        {
            yield return Ramp(v => _reach = v, 0.8f);               // reach for the knob
            yield return Ramp(v => _lever.SetScriptedPull(v), 0.5f); // pull it down
            _lever.PlayRemotePull();                                // ka-chunk, and it springs back
            StartSpin();
            yield return new WaitForSeconds(0.3f);                  // hand rides it back up
            yield return Ramp(v => _reach = 1f - v, 0.6f);          // and lets go
            _reach = 0f;
        }
        else
        {
            if (_lever != null) _lever.PlayRemotePull();
            StartSpin();
        }

        yield return new WaitForSeconds(0.5f);
        float give = Time.time + 40f;
        while (_machine != null && _machine.IsSpinning && Time.time < give) yield return null;

        float roll = Random.value;
        if (roll < 0.25f) { SetPose(PoseSlotWin); yield return new WaitForSeconds(3.5f); }
        else if (roll < 0.6f) { SetPose(PoseSlotLose); yield return new WaitForSeconds(3f); }
        SetPose(PoseSlotSit);
        _busy = false;
    }

    void StartSpin()
    {
        if (_machine is VideoSlotMachine video) video.PlayRemoteSpin(Random.Range(int.MinValue, int.MaxValue), Mathf.Max(1, _machine.BetAmount));
        else if (_machine is SlotMachine classic) classic.PlayRemoteSpin(Random.value < 0.2f ? 0 : -1);
    }

    protected override void OnIK()
    {
        if (_hand == null) return;
        Anim.SetIKPositionWeight(_goal, _reach);
        if (_reach <= 0f || _lever == null) return;
        Vector3 knob = _lever.KnobPosition;
        // Lean the upper body toward the lever so the arm can get there.
        Vector3 toKnob = (knob - Anim.bodyPosition).normalized;
        Quaternion lean = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, toKnob, 0.3f));
        Anim.bodyRotation = Quaternion.Slerp(Quaternion.identity, lean, _reach) * Anim.bodyRotation;
        Anim.SetIKPosition(_goal, knob - _gripOffset);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (_hand == null) return;
        Vector3 palm = _finger != null ? Vector3.Lerp(_hand.position, _finger.position, 0.6f) : _hand.position;
        _gripOffset = palm - _hand.position;
    }

    static readonly string[] Lines =
    {
        "Shh. I can feel it. This one's the big one.",
        "I've been on this stool since Tuesday.",
        "This machine owes me a fortune. I'm just collecting.",
        "Don't touch my lucky stool.",
        "One more spin. Then I'll go home. Probably.",
        "You want a tip? Never leave a warm machine.",
    };
    int _lineIndex;

    protected override void OnTalkedTo()
    {
        string line = Lines[_lineIndex++ % Lines.Length];
        Say(line, Mathf.Max(ReadTime(line), 4f));
    }

    // Mostly watching the screen; glances at a player who comes close.
    protected override bool WantsToLook(out Vector3 target) => base.WantsToLook(out target) && !_busy;
}
