using UnityEngine;

// The gentleman spy who never leaves his bar stool. Black dinner jacket,
// a vodka martini in front of him, and a line for every occasion - shown in
// a speech bubble over his head, like the bartender's. Every now and then he
// orders another drink and the bartender answers him.
public class BondNpc : NpcCharacter
{
    static readonly string[] Greetings =
    {
        "Good evening. I don't believe we've met. The name's Bond..... James Bond.",
    };

    static readonly string[] Lines =
    {
        "Vodka martini, please. Shaken, not stirred.",
        "I never joke about my work.",
        "Shocking. Positively shocking.",
        "Roulette is a game of nerve. Never show your hand.",
        "I'd stay away from table three. The dealer works for SPECTRE.",
        "A gentleman always knows when to walk away from the table.",
        "The odds are only interesting when you can't afford to lose.",
        "One last hand. Then I have a plane to catch.",
        "Careful. Everyone in this room is watching everyone else.",
        "I prefer my luck the way I prefer my martinis. Dry.",
    };

    static readonly string[] Orders =
    {
        "Vodka martini, please. Shaken, not stirred.",
    };

    // (The bartender's answers are on the Bartender, with his voice.)

    [Header("Martini sips")]
    [Tooltip("Seconds between sips (random in this range).")]
    public Vector2 sipInterval = new Vector2(18f, 35f);
    [Tooltip("His mouth, from the head bone: forward / up, in metres.")]
    public float mouthForward = 0.14f, mouthUp = 0f;
    [Tooltip("Where the hand holds the glass at his mouth, from the mouth: forward / down.")]
    public float holdForward = 0.1f, holdDown = 0.09f;
    [Tooltip("How far the glass tips toward him while he drinks, in degrees.")]
    public float sipTilt = 35f;

    Bartender _bartender;
    float _nextLine, _nextOrder, _replyAt = -1f;
    bool _greeted;
    int _lineIndex;

    // The sip: reach for the glass, lift it to his mouth, tip it, put it back.
    // The arm is driven by hand IK; the glass follows the real hand bone, so it
    // stays in his grip even where the IK can't quite reach the target.
    const float GripHeight = 0.045f; // up the stem, from the glass's base
    const float TReach = 0.9f, TGrab = 0.25f, TLift = 1f, TSip = 1.6f, TLower = 1f, TRelease = 0.25f, TRetract = 0.8f;
    const float SipLength = TReach + TGrab + TLift + TSip + TLower + TRelease + TRetract;
    Transform _martini, _hand, _finger, _head;
    Vector3 _restPos, _gripOffset;
    Quaternion _restRot;
    AvatarIKGoal _goal;
    float _nextSip, _sipT = -1f;

    bool Sipping => _sipT >= 0f;

    public void SetMartini(Transform martini)
    {
        _martini = martini;
        if (martini == null || Anim == null || !Anim.isHuman) return;
        _restPos = martini.position;
        _restRot = martini.rotation;
        bool right = Vector3.Dot(martini.position - transform.position, transform.right) >= 0f;
        _goal = right ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
        _hand = Anim.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        _finger = Anim.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        _head = Anim.GetBoneTransform(HumanBodyBones.Head);
        _nextSip = Time.time + Random.Range(6f, 12f);
    }

    static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

    // Where in the sip we are at time t: arm IK weight, lift (0 counter .. 1 mouth),
    // carry (0 glass on the counter .. 1 glass in the hand) and tilt (0..1).
    static void Evaluate(float t, out float ik, out float lift, out float carry, out float tilt)
    {
        ik = 1f; lift = 0f; carry = 0f; tilt = 0f;
        float a = TReach, b = a + TGrab, c = b + TLift, d = c + TSip, e = d + TLower, f = e + TRelease;
        if (t < a) ik = Smooth(t / TReach);
        else if (t < b) carry = Smooth((t - a) / TGrab);
        else if (t < c) { carry = 1f; lift = Smooth((t - b) / TLift); }
        else if (t < d) { carry = 1f; lift = 1f; tilt = Mathf.Clamp01(Mathf.Sin((t - c) / TSip * Mathf.PI) * 1.6f); }
        else if (t < e) { carry = 1f; lift = 1f - Smooth((t - d) / TLower); }
        else if (t < f) carry = 1f - Smooth((t - e) / TRelease);
        else ik = 1f - Smooth((t - f) / TRetract);
    }

    Vector3 GripTarget(float lift)
    {
        Vector3 rest = _restPos + _restRot * Vector3.up * GripHeight;
        if (_head == null) return rest;
        Vector3 fwd = transform.forward, up = Vector3.up;
        Vector3 mouth = _head.position + fwd * mouthForward + up * mouthUp;
        Vector3 atMouth = mouth + fwd * holdForward - up * holdDown;
        // A slight arc out and up rather than a straight line.
        return Vector3.Lerp(rest, atMouth, lift) + (fwd * 0.06f + up * 0.04f) * Mathf.Sin(lift * Mathf.PI);
    }

    protected override bool WantsToLook(out Vector3 target)
    {
        if (Sipping) { target = default; return false; } // eyes on the glass, not turning away from it
        return base.WantsToLook(out target);
    }

    protected override void OnIK()
    {
        if (_hand == null) return;
        float ik = 0f, lift = 0f;
        if (Sipping) Evaluate(_sipT, out ik, out lift, out _, out _);
        Anim.SetIKPositionWeight(_goal, ik);
        if (ik > 0f) Anim.SetIKPosition(_goal, GripTarget(lift) - _gripOffset);
    }

    void UpdateSip()
    {
        if (_hand == null || _martini == null) return;
        if (Sipping)
        {
            _sipT += Time.deltaTime;
            if (_sipT >= SipLength)
            {
                _sipT = -1f;
                _martini.SetPositionAndRotation(_restPos, _restRot);
                _nextSip = Time.time + Random.Range(sipInterval.x, sipInterval.y);
            }
        }
        else if (Time.time >= _nextSip && !Speaking && _replyAt < 0f) _sipT = 0f;
    }

    public void Init(CasinoPatrons manager, RuntimeAnimatorController controller, Bartender bartender)
    {
        Init(manager, controller);
        _bartender = bartender;
        Voice = NpcVoice.Load("James Bond");
        lookDistance = 3.5f;
        SetPose(PoseSitTalk);
        _lineIndex = Random.Range(0, Lines.Length);
        _nextLine = Time.time + Random.Range(6f, 12f);
        _nextOrder = Time.time + Random.Range(25f, 45f);
    }

    // The player selected him: his next line, and the idle chatter waits a bit.
    protected override void OnTalkedTo()
    {
        if (!_greeted)
        {
            _greeted = true;
            Speak(Greetings[Random.Range(0, Greetings.Length)]);
        }
        else Speak(Lines[_lineIndex++ % Lines.Length]);
        _nextLine = Time.time + Random.Range(12f, 20f);
    }

    void Update()
    {
        var head = PlayerHead;
        float distance = head != null ? Vector3.Distance(head.position, transform.position) : 999f;

        UpdateSip();
        if (Sipping)
        {
            // No talking with a glass at his lips - the next line waits.
            _nextLine = Mathf.Max(_nextLine, Time.time + 1f);
            _nextOrder = Mathf.Max(_nextOrder, Time.time + 1f);
        }
        else if (!_greeted && distance < 2.6f)
        {
            _greeted = true;
            float length = Speak(Greetings[Random.Range(0, Greetings.Length)]);
            _nextLine = Time.time + length + Random.Range(8f, 14f);
        }
        else if (_greeted && distance > 6f) _greeted = false;

        // Small talk while someone is around to hear it.
        if (Time.time >= _nextLine)
        {
            _nextLine = Time.time + Random.Range(12f, 22f);
            if (distance < 6f && !Speaking) Speak(Lines[_lineIndex++ % Lines.Length]);
        }

        // Ordering another round, and the bartender's answer.
        if (Time.time >= _nextOrder)
        {
            _nextOrder = Time.time + Random.Range(40f, 70f);
            if (distance < 8f && !Speaking)
            {
                // The bartender answers once he's finished ordering.
                float length = Speak(Orders[Random.Range(0, Orders.Length)]);
                _replyAt = Time.time + length + 0.4f;
            }
        }
        if (_replyAt > 0f && Time.time >= _replyAt)
        {
            _replyAt = -1f;
            if (_bartender != null) _bartender.ReplyToBond();
        }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        // The glass follows his actual (animated + IK) hand while he holds it.
        if (_hand != null)
        {
            Vector3 palm = _finger != null ? Vector3.Lerp(_hand.position, _finger.position, 0.6f) : _hand.position;
            _gripOffset = palm - _hand.position;
            if (Sipping && _martini != null)
            {
                Evaluate(_sipT, out _, out _, out float carry, out float tilt);
                Quaternion held = Quaternion.AngleAxis(-sipTilt * tilt, transform.right) * _restRot;
                Vector3 heldPos = palm - held * Vector3.up * GripHeight;
                _martini.SetPositionAndRotation(Vector3.Lerp(_restPos, heldPos, carry), Quaternion.Slerp(_restRot, held, carry));
            }
        }
    }
}
