using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// The bartender. He greets you when you walk up, chats when you point at
// him and press grab (he's an XR Simple Interactable), and serves orders
// placed with the BarOrderButtons on the counter: Order() takes the money
// from PlayerBalance and he puts the item down on the bar.
//
// Everything he does is procedural - his head follows the player, he wipes
// the bar with one hand, reaches out to serve, and his mustache wiggles
// while he talks. What he says is shown in a speech bubble over his head.
// His arms are capsules stretched between shoulder, elbow and hand every
// frame, so only the hands need animating.
[RequireComponent(typeof(XRSimpleInteractable))]
public class Bartender : MonoBehaviour, IStaffRig
{
    // For StaffLook: right hand wipes the bar and serves, left rests on the counter.
    public Transform StaffHead => head;
    public Transform StaffHand(int index) => index == 0 ? rightHand : leftHand;
    public float StaffHandWeight(int index) => 1f;
    public bool StaffTalking => _talkTimeLeft > 0f;

    [Header("Body parts (all children of this object)")]
    [SerializeField] Transform head;
    [SerializeField] Transform mustache;
    [SerializeField] Transform rightShoulder;
    [SerializeField] Transform leftShoulder;
    [SerializeField] Transform rightHand;
    [SerializeField] Transform leftHand;
    [SerializeField] Transform rightUpperArm;
    [SerializeField] Transform rightForearm;
    [SerializeField] Transform leftUpperArm;
    [SerializeField] Transform leftForearm;
    [SerializeField] float armThickness = 0.085f;

    [Header("Bar")]
    [SerializeField] WorldTextPanel speechBubble;
    [SerializeField] Transform[] servePoints;

    // Each line can have a recorded voice (see VoiceLine).
    [Header("Talking")]
    [SerializeField] float greetDistance = 2.6f;
    [SerializeField] VoiceLine[] greetings =
    {
        new VoiceLine("Evening! What can I get you?"),
        new VoiceLine("Welcome back. The usual?"),
        new VoiceLine("Take a seat, friend. What'll it be?"),
    };
    [SerializeField] VoiceLine[] smallTalk =
    {
        new VoiceLine("Cigars are $100. Cocktails are on the menu."),
        new VoiceLine("Press DRINKS on the bar and pick your poison."),
        new VoiceLine("Hold it up to your mouth - you'll figure it out."),
        new VoiceLine("The roulette table's been hot tonight."),
        new VoiceLine("Easy on the whiskey. It sneaks up on you."),
        new VoiceLine("I've seen fortunes won and lost at those slots."),
    };
    [SerializeField] VoiceLine[] serveLines =
    {
        new VoiceLine("Coming right up."),
        new VoiceLine("Here you go. Enjoy!"),
        new VoiceLine("Excellent choice."),
        new VoiceLine("One of my favourites. Enjoy."),
        new VoiceLine("There you are, friend."),
    };
    [SerializeField] VoiceLine[] cantAffordLines =
    {
        new VoiceLine("Ah, you're a little short, friend."),
        new VoiceLine("Afraid that's a bit out of your budget tonight. Perhaps you should try your luck at the tables."),
        new VoiceLine("Come back when the tables have been kind to you."),
    };
    [Tooltip("When James Bond orders his martini (BondNpc).")]
    [SerializeField] VoiceLine[] bondReplies =
    {
        new VoiceLine("Shaken, not stirred. Right away, Mr. Bond."),
        new VoiceLine("Of course, Mr. Bond. The usual."),
        new VoiceLine("Coming right up, sir."),
    };

    [Header("Voice")]
    [Tooltip("How long the speech bubble stays up after a voice clip finishes, in seconds.")]
    [SerializeField] float afterSpeechHold = 0.5f;
    [SerializeField, Range(0f, 1f)] float voiceVolume = 1f;

    XRSimpleInteractable _interactable;
    AudioSource _voice;
    Transform _player;
    Vector3 _headRestPos, _rightRest, _leftRest;
    Quaternion _headRestRot, _mustacheRest;
    float _speechTimeLeft, _talkTimeLeft;
    bool _playerNear;
    bool _serving;
    int _smallTalkIndex, _serveSlot;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        _headRestPos = head.localPosition;
        _headRestRot = head.localRotation;
        _rightRest = rightHand.localPosition;
        _leftRest = leftHand.localPosition;
        if (mustache != null) _mustacheRest = mustache.localRotation;

        // Voice comes from his head, in 3D.
        _voice = CharacterVoice.Create(head);
    }

    void Start()
    {
        if (speechBubble != null) speechBubble.Visible = false;
        CharacterVoice.Preload(greetings, smallTalk, serveLines, cantAffordLines, bondReplies);
    }

    void OnEnable() => _interactable.selectEntered.AddListener(OnPoked);
    void OnDisable() => _interactable.selectEntered.RemoveListener(OnPoked);

    void OnPoked(SelectEnterEventArgs args)
    {
        if (smallTalk.Length == 0) return;
        Say(smallTalk[_smallTalkIndex++ % smallTalk.Length]);
    }

    // True while he's reaching out to put the last order on the bar.
    public bool Busy => _serving;

    public void Say(string line) => Say(line, null);

    public void Say(VoiceLine line) => Say(line.text, line.clip);

    public void Say(string line, AudioClip clip)
    {
        if (_voice != null) _voice.Stop();
        if (clip != null && _voice != null)
        {
            CharacterVoice.Play(_voice, clip, voiceVolume);
            _talkTimeLeft = clip.length;
            _speechTimeLeft = clip.length + afterSpeechHold;
        }
        else
        {
            _talkTimeLeft = Mathf.Clamp(line.Length * 0.05f, 0.8f, 2.5f);
            _speechTimeLeft = _talkTimeLeft + 2.5f;
        }

        if (speechBubble == null) return;
        speechBubble.SetText(line);
        speechBubble.Visible = true;
    }

    static VoiceLine Pick(VoiceLine[] lines) => lines[Random.Range(0, lines.Length)];

    public void ReplyToBond()
    {
        if (bondReplies.Length > 0) Say(Pick(bondReplies));
    }

    // Called by a BarOrderButton. Takes the money and serves the item, or
    // refuses if the player can't afford it (TrySpend already makes the HUD
    // show "Not enough balance").
    public void Order(GameObject prefab, string itemName, int price, Quaternion serveRotation, IXRSelectInteractor customer)
    {
        if (_serving || prefab == null) return;

        var balance = PlayerBalance.Instance;
        if (balance == null || !balance.TrySpend(price))
        {
            if (cantAffordLines.Length > 0) Say(Pick(cantAffordLines));
            if (customer is XRBaseInputInteractor input) input.SendHapticImpulse(0.6f, 0.15f);
            return;
        }

        BarSounds.Play(BarSounds.Ding, transform.position + Vector3.up * 1.1f, 0.6f);
        if (serveLines.Length > 0) Say(Pick(serveLines));
        StartCoroutine(Serve(prefab, serveRotation));
    }

    // Right hand reaches out to the next free spot on the bar, the item
    // appears under it, and the hand goes back to wiping.
    IEnumerator Serve(GameObject prefab, Quaternion serveRotation)
    {
        _serving = true;
        Transform point = servePoints != null && servePoints.Length > 0
            ? servePoints[_serveSlot++ % servePoints.Length]
            : transform;

        Vector3 reach = transform.InverseTransformPoint(point.position) + Vector3.up * 0.12f;
        Vector3 from = rightHand.localPosition;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            rightHand.localPosition = Vector3.Lerp(from, reach, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        Instantiate(prefab, point.position, point.rotation * serveRotation);
        yield return new WaitForSeconds(0.2f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
        {
            rightHand.localPosition = Vector3.Lerp(reach, _rightRest, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        _serving = false;
    }

    void Update()
    {
        if (_player == null && Camera.main != null) _player = Camera.main.transform;

        // Greet once each time the player walks up.
        if (_player != null)
        {
            float distance = Vector3.Distance(_player.position, head.position);
            if (!_playerNear && distance < greetDistance)
            {
                _playerNear = true;
                if (greetings.Length > 0) Say(Pick(greetings));
            }
            else if (_playerNear && distance > greetDistance + 1.5f)
            {
                _playerNear = false;
            }
        }

        if (_speechTimeLeft > 0f)
        {
            _speechTimeLeft -= Time.deltaTime;
            if (_speechTimeLeft <= 0f && speechBubble != null) speechBubble.Visible = false;
        }
        if (_talkTimeLeft > 0f) _talkTimeLeft -= Time.deltaTime;
    }

    void LateUpdate()
    {
        float time = Time.time;

        // Head: slight breathing bob, and look at the player within a
        // comfortable neck range.
        head.localPosition = _headRestPos + Vector3.up * (Mathf.Sin(time * 1.6f) * 0.004f);
        Quaternion look = _headRestRot;
        if (_player != null)
        {
            Vector3 to = transform.InverseTransformPoint(_player.position) - _headRestPos;
            if (to.magnitude < 7f && to.sqrMagnitude > 0.01f)
            {
                float yaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -75f, 75f);
                float flat = Mathf.Sqrt(to.x * to.x + to.z * to.z);
                float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg, -30f, 30f);
                look = Quaternion.Euler(pitch, yaw, 0f);
            }
        }
        head.localRotation = Quaternion.Slerp(head.localRotation, look, 1f - Mathf.Exp(-5f * Time.deltaTime));

        if (mustache != null)
        {
            float wiggle = _talkTimeLeft > 0f ? Mathf.Sin(time * 22f) * 7f : 0f;
            mustache.localRotation = _mustacheRest * Quaternion.Euler(wiggle * 0.5f, 0f, wiggle);
        }

        // Idle hands: the right one wipes the bar in small circles.
        if (!_serving)
            rightHand.localPosition = _rightRest + new Vector3(Mathf.Cos(time * 2.4f) * 0.06f, 0f, Mathf.Sin(time * 2.4f) * 0.035f);
        leftHand.localPosition = _leftRest + Vector3.up * (Mathf.Sin(time * 1.6f + 1f) * 0.004f);

        PoseArm(rightShoulder, rightHand, rightUpperArm, rightForearm, 1f);
        PoseArm(leftShoulder, leftHand, leftUpperArm, leftForearm, -1f);

        // Speech bubble always faces the player (a Quad's visible face is its -Z).
        if (speechBubble != null && _player != null && speechBubble.Visible)
        {
            Vector3 away = speechBubble.transform.position - _player.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f)
                speechBubble.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }

    // Elbow sits between shoulder and hand, pushed out to the side and
    // back/down a little so the arm bends naturally.
    void PoseArm(Transform shoulder, Transform hand, Transform upper, Transform fore, float side)
    {
        if (shoulder == null || hand == null) return;
        Vector3 s = shoulder.localPosition, h = hand.localPosition;
        Vector3 elbow = (s + h) * 0.5f + new Vector3(side * 0.07f, -0.07f, -0.06f);
        Stretch(upper, s, elbow);
        Stretch(fore, elbow, h);
    }

    // Unity's capsule is 2 units tall along Y at scale 1.
    void Stretch(Transform capsule, Vector3 a, Vector3 b)
    {
        if (capsule == null) return;
        Vector3 d = b - a;
        float length = d.magnitude;
        if (length < 0.0001f) return;
        capsule.localPosition = (a + b) * 0.5f;
        capsule.localRotation = Quaternion.FromToRotation(Vector3.up, d / length);
        capsule.localScale = new Vector3(armThickness, (length + armThickness * 0.6f) * 0.5f, armThickness);
    }
}
