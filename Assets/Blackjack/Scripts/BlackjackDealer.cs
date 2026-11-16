using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// The blackjack dealer. BlackjackTable tells her what to say (Say) and
// where each card is going (DealTo - her right hand reaches toward it).
// Otherwise she behaves like the other staff: head follows the player,
// greets you when you walk up, and explains the rules if you point at her
// and press grab.
[RequireComponent(typeof(XRSimpleInteractable))]
public class BlackjackDealer : MonoBehaviour
{
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

    [Header("Talking")]
    [SerializeField] float greetDistance = 2.6f;
    [SerializeField] string[] greetings =
    {
        "Blackjack! Care for a hand?",
        "Welcome. Pick your bet and press DEAL.",
        "Take a seat - the cards are warm tonight.",
    };
    [SerializeField] string[] rules =
    {
        "Get closer to 21 than me without going over.",
        "Face cards count 10. Aces count 1 or 11.",
        "HIT takes a card. STAND keeps what you have.",
        "DOUBLE doubles your bet for exactly one more card.",
        "Blackjack pays 3 to 2. I stand on 17.",
    };

    XRSimpleInteractable _interactable;
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

    public void Say(string line, float seconds = 3.5f)
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
