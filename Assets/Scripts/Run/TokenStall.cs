using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// The Token Stall outside the casino door: the only place powerups are sold.
// Its four buttons (TokenStallButton) each sell one powerup for tokens, and
// it is only open while everyone is gathered at the door between days - so
// the team has to decide what to buy BEFORE the next day starts.
//
// Zorp, the alien behind the counter, is the same kind of character as the
// croupier and the dealers: his head follows you, he greets you when you
// walk up, comments on what you buy, and tells you things if you point at
// him and squeeze grip. His antennae never stop wobbling.
//
// Built by Tools > Casino > Build Token Stall. Customers stand on its +Z side.
public class TokenStall : MonoBehaviour
{
    public static TokenStall Instance { get; private set; }

    [Header("Keeper")]
    [SerializeField] XRSimpleInteractable keeper;
    [SerializeField] Transform head;
    [SerializeField] Transform body;
    [Tooltip("Right, then left")]
    [SerializeField] Transform[] shoulders = new Transform[2];
    [SerializeField] Transform[] hands = new Transform[2];
    [SerializeField] Transform[] upperArms = new Transform[2];
    [SerializeField] Transform[] forearms = new Transform[2];
    [SerializeField] Transform[] antennae = new Transform[0];
    [SerializeField] float armThickness = 0.085f;
    [SerializeField] WorldTextPanel speechBubble;

    [Header("Stall")]
    [SerializeField] WorldTextPanel sign;
    [SerializeField] float greetDistance = 3f;

    static readonly string[] Greetings =
    {
        "Greetings, soft creature! Tokens for trinkets?",
        "Zorp! Finest Earth-luck, freshly bottled.",
        "Welcome! I accept tokens and compliments.",
    };
    static readonly string[] SmallTalk =
    {
        "Beat the quota, earn tokens. Beat it by a lot, earn many tokens.",
        "I only open between days. During work hours I nap.",
        "The vest is my best seller. Nobody ever returns it. Think about why.",
        "On my planet roulette has ninety pockets, and one of them bites.",
        "Talk it over with your crew. I have all the time in the galaxy.",
    };
    static readonly string[] Thanks =
    {
        "A fine choice! Probably.",
        "Pleasure doing business, biped.",
        "Zorp zorp! That means thank you.",
    };

    Transform _player;
    Quaternion _headRest;
    readonly Vector3[] _handRest = new Vector3[2];
    Quaternion[] _antennaRest;
    float _speechTimeLeft, _wave;
    bool _playerNear;
    int _talkIndex;
    string _signShown;

    void Awake()
    {
        if (Instance == null) Instance = this;
        if (head != null) _headRest = head.localRotation;
        for (int i = 0; i < 2; i++)
            if (hands[i] != null) _handRest[i] = hands[i].localPosition;
        _antennaRest = new Quaternion[antennae.Length];
        for (int i = 0; i < antennae.Length; i++)
            if (antennae[i] != null) _antennaRest[i] = antennae[i].localRotation;
    }

    void Start()
    {
        if (speechBubble != null) speechBubble.Visible = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (keeper != null) keeper.selectEntered.AddListener(OnPoked);
    }

    void OnDisable()
    {
        if (keeper != null) keeper.selectEntered.RemoveListener(OnPoked);
    }

    void OnPoked(SelectEnterEventArgs args) => Say(SmallTalk[_talkIndex++ % SmallTalk.Length], 5f);

    // A stall button was pressed.
    public void Buy(Powerup powerup, IXRSelectInteractor who)
    {
        var run = CasinoRun.Instance;
        string blocked = run == null ? "START A RUN" : run.WhyNot(powerup);
        bool bought = blocked == null && run.Buy(powerup);

        if (bought)
        {
            _wave = 1f;
            BarSounds.Play(BarSounds.Ding, transform.position + Vector3.up, 0.6f);
            Say(Thanks[Random.Range(0, Thanks.Length)]);
        }
        else if (blocked == null) Say("Hmm. My till is stuck. Try again?");
        else if (blocked.StartsWith("NEED")) Say("Not enough tokens, friend. Go and be luckier.");
        else if (blocked.StartsWith("CLOSED")) Say("Closed! Come back between days.");
        else if (blocked.StartsWith("START")) Say("Start a game first - I only trade with the desperate.");
        else Say("No more of those for now. Galactic law.");

        if (who is XRBaseInputInteractor input) input.SendHapticImpulse(bought ? 0.4f : 0.6f, bought ? 0.08f : 0.15f);
    }

    public void Say(string line, float seconds = 4f)
    {
        if (speechBubble == null) return;
        speechBubble.SetText(line);
        speechBubble.Visible = true;
        _speechTimeLeft = seconds;
    }

    void Update()
    {
        if (_player == null && Camera.main != null) _player = Camera.main.transform;

        if (_player != null && head != null)
        {
            float distance = Vector3.Distance(_player.position, head.position);
            if (!_playerNear && distance < greetDistance)
            {
                _playerNear = true;
                Say(Greetings[Random.Range(0, Greetings.Length)]);
            }
            else if (_playerNear && distance > greetDistance + 1.5f) _playerNear = false;
        }

        if (_speechTimeLeft > 0f)
        {
            _speechTimeLeft -= Time.deltaTime;
            if (_speechTimeLeft <= 0f && speechBubble != null) speechBubble.Visible = false;
        }
        _wave = Mathf.MoveTowards(_wave, 0f, Time.deltaTime / 1.2f);

        if (sign == null) return;
        var run = CasinoRun.Instance;
        bool inRun = run != null && (run.Phase == RunPhase.Running || run.Phase == RunPhase.Gathering);
        string status = !inRun ? "OPENS DURING A GAME"
            : run.Phase == RunPhase.Gathering || RunEntrance.Instance == null ? $"<color=#5CFF7A>OPEN</color>   YOUR TOKENS: {run.Tokens}"
            : $"<color=#FF6A5C>CLOSED UNTIL TOMORROW</color>   YOUR TOKENS: {run.Tokens}";
        string text = $"<color=#FFD24A>TOKEN STALL</color>\n<size=42%>(buy powerups here)   {status}</size>";
        if (text == _signShown) return;
        _signShown = text;
        sign.SetText(text);
    }

    void LateUpdate()
    {
        if (head == null) return;
        CharacterLimbs.LookAt(head, _headRest, _player != null ? _player.position : (Vector3?)null);

        // Antennae sway on their own, each to its own rhythm.
        for (int i = 0; i < antennae.Length; i++)
            if (antennae[i] != null)
                antennae[i].localRotation = _antennaRest[i]
                    * Quaternion.Euler(Mathf.Sin(Time.time * 3.1f + i * 1.7f) * 9f, 0f, Mathf.Sin(Time.time * 2.3f + i * 2.9f) * 12f);

        // Hands rest on the counter; the right one waves after a sale.
        float lift = Mathf.Sin(_wave * Mathf.PI);
        float breathe = Mathf.Sin(Time.time * 1.6f) * 0.004f;
        if (hands[0] != null)
            hands[0].localPosition = _handRest[0] + Vector3.up * breathe
                + new Vector3(0.1f + Mathf.Sin(Time.time * 14f) * 0.05f, 0.4f, -0.1f) * lift;
        if (hands[1] != null) hands[1].localPosition = _handRest[1] + Vector3.up * breathe;

        Quaternion bodyRotation = body != null ? body.rotation : transform.rotation;
        for (int i = 0; i < 2; i++)
            if (hands[i] != null && shoulders[i] != null)
                CharacterLimbs.PoseArm(upperArms[i], forearms[i], shoulders[i].position, hands[i].position,
                    bodyRotation, i == 0 ? 1f : -1f, armThickness);

        // Speech bubble always faces the player (a Quad's visible face is its -Z).
        if (speechBubble != null && _player != null && speechBubble.Visible)
        {
            Vector3 away = speechBubble.transform.position - _player.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f) speechBubble.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
