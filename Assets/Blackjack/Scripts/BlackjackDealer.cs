using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// The blackjack dealer. BlackjackTable tells her what to say (Say) and
// where each card is going (DealTo - her right hand reaches toward it).
// Otherwise she behaves like the other staff: head follows the player,
// greets you when you walk up, and explains the rules if you point at her
// and press grab.
[RequireComponent(typeof(XRSimpleInteractable))]
public class BlackjackDealer : MonoBehaviour, IStaffRig
{
    // For StaffLook: both hands rest on the felt and the right one deals.
    public Transform StaffHead => head;
    public Transform StaffHand(int index) => hands[index];
    public float StaffHandWeight(int index) => 1f;
    public bool StaffTalking => _speechTimeLeft > 0f;

    [Header("Body parts")]
    [SerializeField] Transform head;
    [SerializeField] Transform body;
    [Tooltip("Right, then left")]
    [SerializeField] Transform[] shoulders = new Transform[2];
    [SerializeField] Transform[] hands = new Transform[2];
    [SerializeField] Transform[] upperArms = new Transform[2];
    [SerializeField] Transform[] forearms = new Transform[2];
    [SerializeField] float armThickness = 0.085f;
    [SerializeField] WorldTextPanel speechBubble;

    // What BlackjackTable asks her to say. Kept generic (no card totals or
    // amounts) so each one is a single recording; the table's display shows
    // the numbers.
    public enum Line { Win, Blackjack, Push, Lose, TooShort, NotInHand, DoubleFirstTwoOnly, CantCoverDouble }

    // Each line can have a recorded voice (see VoiceLine).
    [Header("Talking")]
    [SerializeField] float greetDistance = 2.6f;
    [SerializeField] VoiceLine[] greetings =
    {
        new VoiceLine("Blackjack! Care for a hand?"),
        new VoiceLine("Take a seat - the cards are warm tonight."),
    };
    [SerializeField] VoiceLine[] rules =
    {
        new VoiceLine("Get closer to 21 than me without going over."),
        new VoiceLine("Face cards count 10. Aces count 1 or 11."),
        new VoiceLine("HIT takes a card. STAND keeps what you have."),
        new VoiceLine("DOUBLE doubles your bet for exactly one more card."),
        new VoiceLine("Blackjack pays 3 to 2. I stand on 17."),
    };

    [Header("End of a hand")]
    [Tooltip("One picked at random.")]
    [SerializeField] VoiceLine[] wins =
    {
        new VoiceLine("You win! Nicely played."),
        new VoiceLine("Winner! Well done."),
    };
    [SerializeField] VoiceLine blackjack = new VoiceLine("Blackjack! Pays three to two.");
    [SerializeField] VoiceLine push = new VoiceLine("It's a push. Your bet comes back.");
    [Tooltip("One picked at random.")]
    [SerializeField] VoiceLine[] losses =
    {
        new VoiceLine("House wins. Better luck next hand."),
        new VoiceLine("Not this time."),
    };

    [Header("Can't do that")]
    [SerializeField] VoiceLine tooShort = new VoiceLine("You're a little short for that bet.");
    [SerializeField] VoiceLine notInHand = new VoiceLine("You're not in this hand - press DEAL on the next one.");
    [SerializeField] VoiceLine doubleFirstTwoOnly = new VoiceLine("You can only double on your first two cards.");
    [SerializeField] VoiceLine cantCoverDouble = new VoiceLine("You can't cover the double - your bet stays as it is.");

    [Header("Voice")]
    [Tooltip("How long the speech bubble stays up after a voice clip finishes, in seconds.")]
    [SerializeField] float afterSpeechHold = 0.5f;
    [SerializeField, Range(0f, 1f)] float voiceVolume = 1f;

    XRSimpleInteractable _interactable;
    AudioSource _voice;
    Transform _player;
    Quaternion _headRest;
    readonly Vector3[] _handRest = new Vector3[2];
    Vector3 _reachTarget; // local
    float _reach;         // 1 right after dealing a card, fades to 0
    float _speechTimeLeft;
    bool _playerNear;
    int _ruleIndex;

    public bool Busy { get; set; } // true mid-round: no greeting over game talk

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        _headRest = head.localRotation;
        for (int i = 0; i < 2; i++) _handRest[i] = hands[i].localPosition;

        // Voice comes from her head, in 3D.
        _voice = head.gameObject.AddComponent<AudioSource>();
        _voice.playOnAwake = false;
        _voice.spatialBlend = 1f;
        _voice.rolloffMode = AudioRolloffMode.Linear;
        _voice.minDistance = 1.5f;
        _voice.maxDistance = 14f;
    }

    void Start()
    {
        if (speechBubble != null) speechBubble.Visible = false;
    }

    void OnEnable() => _interactable.selectEntered.AddListener(OnPoked);
    void OnDisable() => _interactable.selectEntered.RemoveListener(OnPoked);

    void OnPoked(SelectEnterEventArgs args)
    {
        if (rules.Length > 0) Say(rules[_ruleIndex++ % rules.Length], 4.5f);
    }

    public void Say(Line line)
    {
        switch (line)
        {
            case Line.Win: if (wins.Length > 0) Say(wins[Random.Range(0, wins.Length)], 4.5f); break;
            case Line.Blackjack: Say(blackjack, 4.5f); break;
            case Line.Push: Say(push, 4.5f); break;
            case Line.Lose: if (losses.Length > 0) Say(losses[Random.Range(0, losses.Length)], 4.5f); break;
            case Line.TooShort: Say(tooShort); break;
            case Line.NotInHand: Say(notInHand); break;
            case Line.DoubleFirstTwoOnly: Say(doubleFirstTwoOnly); break;
            case Line.CantCoverDouble: Say(cantCoverDouble); break;
        }
    }

    // A line with a clip is spoken, and the bubble stays up until shortly
    // after it ends; without one the bubble shows for `seconds`. Either way,
    // whatever she was saying is cut off and replaced.
    public void Say(VoiceLine line, float seconds = 3.5f)
    {
        if (_voice != null) _voice.Stop();
        if (line.clip != null && _voice != null)
        {
            _voice.clip = line.clip;
            _voice.volume = voiceVolume;
            _voice.Play();
            seconds = line.clip.length + afterSpeechHold;
        }
        ShowBubble(line.text, seconds);
    }

    public void Say(string line, float seconds = 3.5f)
    {
        if (_voice != null) _voice.Stop();
        ShowBubble(line, seconds);
    }

    void ShowBubble(string line, float seconds)
    {
        if (speechBubble == null) return;
        speechBubble.SetText(line);
        speechBubble.Visible = true;
        _speechTimeLeft = seconds;
    }

    public void DealTo(Vector3 worldPosition)
    {
        _reachTarget = transform.InverseTransformPoint(worldPosition) + Vector3.up * 0.1f;
        _reach = 1f;
    }

    void Update()
    {
        if (_player == null && Camera.main != null) _player = Camera.main.transform;

        if (_player != null)
        {
            float distance = Vector3.Distance(_player.position, head.position);
            if (!_playerNear && distance < greetDistance)
            {
                _playerNear = true;
                if (!Busy && greetings.Length > 0) Say(greetings[Random.Range(0, greetings.Length)]);
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
        _reach = Mathf.MoveTowards(_reach, 0f, Time.deltaTime / 0.5f);
    }

    void LateUpdate()
    {
        CharacterLimbs.LookAt(head, _headRest, _player != null ? _player.position : (Vector3?)null);

        float breathe = Mathf.Sin(Time.time * 1.6f) * 0.004f;
        float reach = Mathf.Sin(_reach * Mathf.PI); // out and back
        hands[0].localPosition = Vector3.Lerp(_handRest[0], _reachTarget, reach * 0.8f) + Vector3.up * breathe;
        hands[1].localPosition = _handRest[1] + Vector3.up * breathe;

        Quaternion bodyRotation = body != null ? body.rotation : transform.rotation;
        for (int i = 0; i < 2; i++)
            CharacterLimbs.PoseArm(upperArms[i], forearms[i], shoulders[i].position, hands[i].position,
                bodyRotation, i == 0 ? 1f : -1f, armThickness);

        if (speechBubble != null && _player != null && speechBubble.Visible)
        {
            Vector3 away = speechBubble.transform.position - _player.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f)
                speechBubble.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
