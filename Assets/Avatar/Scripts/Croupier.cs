using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// The casino employee working the roulette table. He welcomes you when you
// walk up, calls "No more bets!" with a sweep of his arm when the wheel
// starts spinning, and announces the winning number and colour when the
// ball lands. Point at him and press grab and he explains how to play.
// Same procedural style as the Bartender: head follows the player, arms are
// capsules stretched from shoulder to hand, text shows in a speech bubble.
[RequireComponent(typeof(XRSimpleInteractable))]
public class Croupier : MonoBehaviour
{
    [SerializeField] RouletteWheel wheel;

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
        "Place your bets, please!",
        "Welcome to the table. Feeling lucky?",
        "Step right up - place your bets!",
    };
    [SerializeField] string[] smallTalk =
    {
        "Grab chips from your pockets and drop them on the table.",
        "Then press the spin button and we'll see what luck brings.",
        "A single number pays 35 to 1.",
        "Red or black? Classic choice.",
        "The wheel has no memory, my friend.",
    };
    [SerializeField] string noMoreBets = "No more bets!";

    XRSimpleInteractable _interactable;
    Transform _player;
    Quaternion _headRest;
    readonly Vector3[] _handRest = new Vector3[2];
    float _speechTimeLeft;
    float _gesture; // 1 right after a spin starts, fades to 0
    bool _playerNear, _wasSpinning;
    int _smallTalkIndex;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        _headRest = head.localRotation;
        for (int i = 0; i < 2; i++) _handRest[i] = hands[i].localPosition;
    }

    void Start()
    {
        if (wheel == null) wheel = FindAnyObjectByType<RouletteWheel>();
        if (wheel != null) wheel.OnResult += OnResult;
        if (speechBubble != null) speechBubble.Visible = false;
    }

    void OnDestroy()
    {
        if (wheel != null) wheel.OnResult -= OnResult;
    }

    void OnEnable() => _interactable.selectEntered.AddListener(OnPoked);
    void OnDisable() => _interactable.selectEntered.RemoveListener(OnPoked);

    void OnPoked(SelectEnterEventArgs args)
    {
        if (smallTalk.Length > 0) Say(smallTalk[_smallTalkIndex++ % smallTalk.Length]);
    }

    void OnResult(int number)
    {
        string colour = number == 0 ? "green" : RouletteWheel.IsRed(number) ? "red" : "black";
        Say($"{number}, {colour}!", 5f);
    }

    public void Say(string line, float seconds = 3.5f)
    {
        if (speechBubble == null) return;
        speechBubble.SetText(line);
        speechBubble.Visible = true;
        _speechTimeLeft = seconds;
    }

    void Update()
    {
        if (_player == null && Camera.main != null) _player = Camera.main.transform;

        if (wheel != null)
        {
            bool spinning = wheel.IsSpinning;
            if (spinning && !_wasSpinning)
            {
                Say(noMoreBets);
                _gesture = 1f;
            }
            _wasSpinning = spinning;
        }

        if (_player != null)
        {
            float distance = Vector3.Distance(_player.position, head.position);
            if (!_playerNear && distance < greetDistance)
            {
                _playerNear = true;
                if (greetings.Length > 0 && (wheel == null || !wheel.IsSpinning))
                    Say(greetings[Random.Range(0, greetings.Length)]);
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
        _gesture = Mathf.MoveTowards(_gesture, 0f, Time.deltaTime / 1.4f);
    }

    void LateUpdate()
    {
        // While the ball is rolling he watches the wheel, otherwise the player.
        Vector3? lookTarget = null;
        if (wheel != null && wheel.IsSpinning) lookTarget = wheel.transform.position;
        else if (_player != null) lookTarget = _player.position;
        CharacterLimbs.LookAt(head, _headRest, lookTarget);

        // Hands rest clasped in front; the right one sweeps out over the
        // table and back when a spin starts.
        float sweep = Mathf.Sin(_gesture * Mathf.PI);
        float breathe = Mathf.Sin(Time.time * 1.6f) * 0.004f;
        hands[0].localPosition = _handRest[0] + Vector3.up * breathe
            + new Vector3(0.28f - 0.5f * (1f - _gesture), 0.12f, 0.22f) * sweep;
        hands[1].localPosition = _handRest[1] + Vector3.up * breathe;

        Quaternion bodyRotation = body != null ? body.rotation : transform.rotation;
        for (int i = 0; i < 2; i++)
            CharacterLimbs.PoseArm(upperArms[i], forearms[i], shoulders[i].position, hands[i].position,
                bodyRotation, i == 0 ? 1f : -1f, armThickness);

        // Speech bubble always faces the player (a Quad's visible face is its -Z).
        if (speechBubble != null && _player != null && speechBubble.Visible)
        {
            Vector3 away = speechBubble.transform.position - _player.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f)
                speechBubble.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
