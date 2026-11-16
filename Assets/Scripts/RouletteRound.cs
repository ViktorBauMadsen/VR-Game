using System;
using UnityEngine;

// Decides WHEN a roulette wheel spins, so several players can bet on the
// same spin. Added to every RouletteWheel automatically.
//
//   Idle       nothing on the table. The first chip placed (by anyone)
//              starts the countdown.
//   Countdown  "bets close in N" - everybody gets their chips down.
//   Spinning   no more bets: chips can't be placed or taken back.
//   Result     the number is shown for a few seconds, then back to Idle.
//
// The spin button starts the countdown too; pressed while playing alone it
// skips the wait and spins immediately.
//
// Multiplayer: the host runs the clock and picks the winning number, and
// every player's wheel spins to that same number (CasinoTables carries the
// messages through the hooks below). Each player's own chips are paid on
// their own machine from that shared result.
[RequireComponent(typeof(RouletteWheel))]
public class RouletteRound : MonoBehaviour
{
    public const byte RequestCountdown = 0, CountdownStarted = 1, SpinStarted = 2;

    // Set by CasinoTables. Shared = other players are in the casino.
    public static Func<bool> Shared = () => false;
    public static Func<bool> IsAuthority = () => true;
    // Authority: tell the guests (CountdownStarted: arg = tenths of a second
    // left; SpinStarted: arg = winning number). Guest: ask the host (RequestCountdown).
    public static Action<RouletteRound, byte, int> Send;

    [SerializeField] float bettingSeconds = 20f;
    [SerializeField] float resultSeconds = 5f;
    [Tooltip("Where the countdown sign floats, above the wheel.")]
    [SerializeField] float signHeight = 0.75f;

    enum Phase { Idle, Countdown, Spinning, Result }

    RouletteWheel _wheel;
    RouletteBettingManager _betting;
    WorldTextPanel _sign;
    Phase _phase;
    float _timeLeft, _nextRequest, _nextResync;
    bool _requested;
    int _lastNumber = -1;
    string _shown;

    public RouletteWheel Wheel => _wheel;

    public RouletteBettingManager Betting
    {
        get
        {
            if (_betting != null) return _betting;
            var table = GetComponentInParent<RouletteTable>();
            if (table != null) return _betting = table.Betting;
            foreach (var manager in FindObjectsByType<RouletteBettingManager>())
                if (manager.Wheel == _wheel) return _betting = manager;
            return _betting = RouletteBettingManager.Instance;
        }
    }

    void Awake() => _wheel = GetComponent<RouletteWheel>();

    void OnEnable() => _wheel.OnResult += OnResult;
    void OnDisable() => _wheel.OnResult -= OnResult;

    void Start()
    {
        _sign = WorldTextPanel.Create("Roulette Countdown", null, transform.position + Vector3.up * signHeight,
            new Vector2(0.62f, 0.155f), new Vector2Int(768, 192), Color.white, new Color(0.03f, 0.03f, 0.05f), 130f);
    }

    void OnDestroy()
    {
        if (_sign != null) Destroy(_sign.gameObject);
    }

    // ------------------------------------------------------------ inputs

    public void PressSpin()
    {
        bool alone = !Shared() && IsAuthority();
        if (_phase == Phase.Idle) AskForCountdown();
        if (_phase == Phase.Countdown && alone) _timeLeft = 0f;
    }

    void AskForCountdown()
    {
        if (IsAuthority()) StartCountdown();
        else if (Time.unscaledTime >= _nextRequest)
        {
            _nextRequest = Time.unscaledTime + 1f;
            Send?.Invoke(this, RequestCountdown, 0);
        }
    }

    void StartCountdown()
    {
        _requested = false;
        _phase = Phase.Countdown;
        _timeLeft = bettingSeconds;
        _nextResync = Time.unscaledTime + 2f;
        Send?.Invoke(this, CountdownStarted, Mathf.RoundToInt(_timeLeft * 10f));
    }

    // A message for this table arrived (see the constants above).
    public void Receive(byte kind, int arg)
    {
        if (IsAuthority())
        {
            if (kind != RequestCountdown) return;
            if (_phase == Phase.Idle) StartCountdown();
            else if (_phase == Phase.Result) _requested = true;
            return;
        }
        if (kind == CountdownStarted && _phase != Phase.Spinning)
        {
            _phase = Phase.Countdown;
            _timeLeft = Mathf.Clamp(arg / 10f, 0f, 600f);
        }
        else if (kind == SpinStarted) BeginSpin(arg);
    }

    // -------------------------------------------------------------- clock

    void Update()
    {
        switch (_phase)
        {
            case Phase.Idle:
                var betting = Betting;
                if (_requested || (betting != null && betting.PlacedCount > 0)) AskForCountdown();
                break;

            case Phase.Countdown:
                _timeLeft = Mathf.Max(0f, _timeLeft - Time.deltaTime);
                if (!IsAuthority()) break; // guests wait for the host's spin
                if (_timeLeft <= 0f)
                {
                    int number = _wheel.PickRandomNumber();
                    if (number < 0) { _phase = Phase.Idle; break; }
                    Send?.Invoke(this, SpinStarted, number);
                    BeginSpin(number);
                }
                else if (Time.unscaledTime >= _nextResync)
                {
                    // Keeps late joiners and slow clocks in step.
                    _nextResync = Time.unscaledTime + 2f;
                    Send?.Invoke(this, CountdownStarted, Mathf.RoundToInt(_timeLeft * 10f));
                }
                break;

            case Phase.Spinning:
                if (!_wheel.IsSpinning) _phase = Phase.Idle; // safety net; OnResult normally ends it
                break;

            case Phase.Result:
                _timeLeft -= Time.deltaTime;
                if (_timeLeft <= 0f) _phase = Phase.Idle;
                break;
        }
        UpdateSign();
    }

    void BeginSpin(int number)
    {
        if (_wheel.SpinTo(number)) _phase = Phase.Spinning;
    }

    void OnResult(int number)
    {
        _lastNumber = number;
        _phase = Phase.Result;
        _timeLeft = resultSeconds;
    }

    // --------------------------------------------------------------- sign

    void UpdateSign()
    {
        if (_sign == null) return;
        string text;
        switch (_phase)
        {
            case Phase.Countdown:
                int seconds = Mathf.CeilToInt(_timeLeft);
                text = seconds > 0
                    ? $"BETS CLOSE IN <color={(seconds <= 5 ? "#FF6A5C" : "#FFD24A")}>{seconds}</color>"
                    : "NO MORE BETS";
                break;
            case Phase.Spinning:
                text = "<color=#FF6A5C>NO MORE BETS</color>";
                break;
            case Phase.Result:
                string colour = _lastNumber == 0 ? "#5CFF7A" : RouletteWheel.IsRed(_lastNumber) ? "#FF6A5C" : "#FFFFFF";
                string colourName = _lastNumber == 0 ? "GREEN" : RouletteWheel.IsRed(_lastNumber) ? "RED" : "BLACK";
                text = $"<color={colour}>{_lastNumber} {colourName}</color>";
                break;
            default:
                text = "<color=#FFD24A>PLACE YOUR BETS</color>";
                break;
        }
        if (text != _shown)
        {
            _shown = text;
            _sign.SetText(text);
        }

        // Always readable: turn toward the player (a Quad shows on its -Z side).
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 away = _sign.transform.position - cam.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude > 0.001f) _sign.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }
}
