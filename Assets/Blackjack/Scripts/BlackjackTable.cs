using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// What can happen at a blackjack table. Every player's table plays these same
// events in the same order, which is what keeps the tables identical.
public enum BlackjackEvent : byte { BetDown, BetUp, Join, StartDeal, Hit, Stand, Double }

// Blackjack against the dealer, played with the buttons on the table
// (BlackjackButton): - / + pick the bet, DEAL starts a hand, then HIT, STAND
// or DOUBLE.
//
// Rules: six decks, shuffled fresh for every hand. Dealer draws to 16 and
// stands on all 17s. Blackjack pays 3:2. Double is allowed on the first two
// cards (one more card, then you stand). No splitting or insurance. The
// dealer checks for blackjack before you play.
//
// WITH FRIENDS the table is one shared hand that you play together:
//   - The first DEAL press opens a short "who's in?" countdown. Everyone who
//     presses DEAL before it ends is in the hand, each with their own money
//     on it (the bet shown on the table). Anyone else just watches.
//   - Everybody sees the same cards. Anyone who is in can press HIT, STAND or
//     DOUBLE - you win or lose together, so talk it over.
//   - DOUBLE doubles the bet of every player who can afford it.
// Alone there is no countdown: DEAL deals.
//
// How it stays in sync: a button press is not acted on directly. It becomes
// a BlackjackEvent that the host approves and sends to everyone (through the
// hooks below, wired up by CasinoTables); each table then plays the events
// in order, waiting for its own card animations where needed. The shuffle
// uses a seed the host sends with the deal, so all shoes are identical.
//
// Money follows the same convention as the other games: the bet is taken
// with PlayerBalance.TrySpend when you join the hand, and a win pays back
// stake + winnings. Each player's money stays on their own machine.
public class BlackjackTable : MonoBehaviour
{
    // Set by CasinoTables. Shared = other players are in the casino.
    public static Func<bool> Shared = () => false;
    public static Func<bool> IsAuthority = () => true;
    public static Func<ulong> LocalId = () => 0UL;
    // Authority: announce an approved event to the guests. Guest: ask the host for it.
    public static Action<BlackjackTable, BlackjackEvent, ulong, int> Send;

    [SerializeField] BlackjackDealer dealer;
    [SerializeField] WorldTextPanel infoPanel;
    [Tooltip("Where cards fly out from.")]
    [SerializeField] Transform shoe;
    [Tooltip("Position of the first (player's leftmost) card in each row; later cards go toward this transform's -X.")]
    [SerializeField] Transform dealerCards;
    [SerializeField] Transform playerCards;
    [SerializeField] Transform betStack;
    [SerializeField] Material cardMaterial;
    [SerializeField] Material chipMaterial;

    // (Renamed from "bets" so every table picked up the new, higher ladder.)
    [SerializeField] int[] betLadder = { 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 };
    int[] bets => betLadder;
    [SerializeField] int startingBetIndex = 2;
    [SerializeField] float cardSpacing = 0.095f;
    [SerializeField] float dealSeconds = 0.3f;
    [SerializeField] float pauseSeconds = 0.25f;
    [SerializeField] int decks = 6;
    [Tooltip("With friends: how long others have to press DEAL and join the hand.")]
    [SerializeField] float joinSeconds = 6f;

    enum State { Betting, Joining, Busy, PlayerTurn, RoundOver }

    class Hand
    {
        public readonly List<int> Cards = new List<int>();
        public readonly List<Transform> Objects = new List<Transform>();

        public int Total(out bool soft)
        {
            int total = 0, aces = 0;
            foreach (int card in Cards)
            {
                total += CardAtlas.Value(card);
                if (card % 13 == 0) aces++;
            }
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            soft = aces > 0;
            return total;
        }

        public int Total() => Total(out _);
        public bool IsBlackjack => Cards.Count == 2 && Total() == 21;
    }

    struct Pending
    {
        public BlackjackEvent What;
        public ulong Player;
        public int Arg;
    }

    readonly Hand _player = new Hand(), _dealer = new Hand();
    readonly List<int> _shoe = new List<int>();
    readonly List<GameObject> _chips = new List<GameObject>();
    readonly Queue<Pending> _queue = new Queue<Pending>();
    readonly HashSet<ulong> _joined = new HashSet<ulong>();
    State _state = State.Betting;
    int _betIndex;
    int _wager;          // this player's own money on the hand (0 = watching)
    bool _inHand;
    bool _holeHidden;
    float _joinDeadline, _waitingSince;
    int _shownSeconds = -1;
    AudioClip _flick;

    int Bet => bets[_betIndex];

    void Start()
    {
        _betIndex = Mathf.Clamp(startingBetIndex, 0, bets.Length - 1);
        _flick = CreateFlick();
        Shuffle(Environment.TickCount);
        ShowChips(Bet);
        ShowBetting();
    }

    // ------------------------------------------------------------ buttons

    public void Press(BlackjackAction action, IXRSelectInteractor who)
    {
        BlackjackEvent what;
        switch (action)
        {
            case BlackjackAction.BetDown: what = BlackjackEvent.BetDown; break;
            case BlackjackAction.BetUp: what = BlackjackEvent.BetUp; break;
            case BlackjackAction.Deal: what = BlackjackEvent.Join; break;
            case BlackjackAction.Hit: what = BlackjackEvent.Hit; break;
            case BlackjackAction.Stand: what = BlackjackEvent.Stand; break;
            default: what = BlackjackEvent.Double; break;
        }

        bool ok = LocalCheck(what);
        if (ok)
        {
            if (IsAuthority()) ok = Request(what, LocalId());
            else Send?.Invoke(this, what, LocalId(), 0); // the host answers
        }
        if (who is XRBaseInputInteractor input) input.SendHapticImpulse(ok ? 0.3f : 0.6f, ok ? 0.06f : 0.15f);
    }

    // Things only this player can know: can they afford it, are they in the hand.
    bool LocalCheck(BlackjackEvent what)
    {
        var balance = PlayerBalance.Instance;
        switch (what)
        {
            case BlackjackEvent.Join:
                if (_state == State.Joining && _inHand) return false; // already in
                if (_state != State.Betting && _state != State.RoundOver && _state != State.Joining) return false;
                if (balance == null || !balance.CanAfford(Bet))
                {
                    if (balance != null) balance.ReportInsufficientFunds(Bet);
                    if (dealer != null) dealer.Say($"The bet is ${Bet}. You're a little short.");
                    return false;
                }
                return true;

            case BlackjackEvent.Hit:
            case BlackjackEvent.Stand:
            case BlackjackEvent.Double:
                if (_state != State.PlayerTurn) return false;
                if (!_inHand)
                {
                    if (dealer != null) dealer.Say("You're not in this hand - press DEAL on the next one.");
                    return false;
                }
                if (what == BlackjackEvent.Double && _player.Cards.Count != 2)
                {
                    if (dealer != null) dealer.Say("You can only double on your first two cards.");
                    return false;
                }
                return true;

            default:
                return _state == State.Betting || _state == State.RoundOver;
        }
    }

    // Authority only: approve an event (a local press, or a guest asking),
    // tell everyone, and play it here.
    public bool Request(BlackjackEvent what, ulong player)
    {
        if (!IsAuthority()) return false;
        int arg = 0;
        switch (what)
        {
            case BlackjackEvent.BetDown:
            case BlackjackEvent.BetUp:
                if (_state != State.Betting && _state != State.RoundOver) return false;
                break;
            case BlackjackEvent.Join:
                if (_state == State.Joining) { if (_joined.Contains(player)) return false; }
                else if (_state != State.Betting && _state != State.RoundOver) return false;
                arg = _betIndex; // so a late joiner's table shows the same bet
                break;
            case BlackjackEvent.Hit:
            case BlackjackEvent.Stand:
            case BlackjackEvent.Double:
                if (_state != State.PlayerTurn) return false;
                if (Shared() && !_joined.Contains(player)) return false;
                if (what == BlackjackEvent.Double && _player.Cards.Count != 2) return false;
                break;
            default:
                return false; // StartDeal only comes from the countdown below
        }
        Announce(what, player, arg);
        return true;
    }

    void Announce(BlackjackEvent what, ulong player, int arg)
    {
        Send?.Invoke(this, what, player, arg);
        Enqueue(what, player, arg);
    }

    // An approved event, from the host or from this table itself.
    public void Enqueue(BlackjackEvent what, ulong player, int arg)
    {
        if (_queue.Count == 0) _waitingSince = Time.unscaledTime;
        _queue.Enqueue(new Pending { What = what, Player = player, Arg = arg });
        Drain();
    }

    // -------------------------------------------------------------- events

    void Update()
    {
        Drain();

        if (_state == State.Joining)
        {
            float left = _joinDeadline - Time.unscaledTime;
            if (left <= 0f && IsAuthority() && _queue.Count == 0)
                Announce(BlackjackEvent.StartDeal, 0UL, UnityEngine.Random.Range(1, int.MaxValue));
            else
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(left));
                if (seconds != _shownSeconds) { _shownSeconds = seconds; ShowJoining(seconds); }
            }
        }

        // Something that should have played by now never could (a dropped
        // connection mid-hand, say): start over rather than hang forever.
        if (_queue.Count > 0 && Time.unscaledTime - _waitingSince > 20f) ResetTable();
    }

    void Drain()
    {
        while (_queue.Count > 0)
        {
            var next = _queue.Peek();
            bool idle = _state == State.Betting || _state == State.RoundOver;
            bool ready;
            switch (next.What)
            {
                case BlackjackEvent.BetDown:
                case BlackjackEvent.BetUp:
                    ready = idle;
                    break;
                case BlackjackEvent.Join:
                    ready = idle || _state == State.Joining;
                    break;
                case BlackjackEvent.StartDeal:
                    if (idle) { _queue.Dequeue(); continue; } // a hand we never saw the start of
                    ready = _state == State.Joining;
                    break;
                default:
                    if (idle || _state == State.Joining) { _queue.Dequeue(); continue; }
                    ready = _state == State.PlayerTurn;
                    break;
            }
            if (!ready) return; // cards are still moving - try again next frame
            _queue.Dequeue();
            _waitingSince = Time.unscaledTime;
            Play(next);
        }
    }

    void Play(Pending e)
    {
        bool mine = e.Player == LocalId();
        var balance = PlayerBalance.Instance;
        switch (e.What)
        {
            case BlackjackEvent.BetDown:
            case BlackjackEvent.BetUp:
                _betIndex = Mathf.Clamp(_betIndex + (e.What == BlackjackEvent.BetUp ? 1 : -1), 0, bets.Length - 1);
                if (_state == State.RoundOver) ClearCards();
                _state = State.Betting;
                ShowChips(Bet);
                ShowBetting();
                break;

            case BlackjackEvent.Join:
                if (_state != State.Joining)
                {
                    // First one in: a new hand begins.
                    ClearCards();
                    _joined.Clear();
                    _inHand = false;
                    _wager = 0;
                    _betIndex = Mathf.Clamp(e.Arg, 0, bets.Length - 1);
                    _state = State.Joining;
                    _joinDeadline = Time.unscaledTime + (Shared() ? joinSeconds : 0f);
                    _shownSeconds = -1;
                    ShowChips(Bet);
                }
                _joined.Add(e.Player);
                if (mine && !_inHand && balance != null && balance.TrySpend(Bet))
                {
                    _inHand = true;
                    _wager = Bet;
                }
                _shownSeconds = -1; // redraw the count of players
                break;

            case BlackjackEvent.StartDeal:
                Shuffle(e.Arg);
                StartCoroutine(DealRound());
                break;

            case BlackjackEvent.Hit:
                StartCoroutine(Hit(false));
                break;

            case BlackjackEvent.Double:
                if (_inHand)
                {
                    if (balance != null && balance.CanAfford(_wager) && balance.TrySpend(_wager)) _wager *= 2;
                    else if (dealer != null) dealer.Say("You can't cover the double - your bet stays as it is.");
                }
                ShowChips(Stake * (_inHand ? 1 : 2));
                StartCoroutine(Hit(true));
                break;

            case BlackjackEvent.Stand:
                StartCoroutine(DealerPlays());
                break;
        }
    }

    // What the chip stack on the table should show.
    int Stake => _inHand ? _wager : Bet;

    void ResetTable()
    {
        StopAllCoroutines();
        _queue.Clear();
        // A hand that never finished: the stake goes back.
        if (_inHand && _wager > 0 && _state != State.RoundOver && _state != State.Betting && PlayerBalance.Instance != null)
            PlayerBalance.Instance.AddBalance(_wager);
        _inHand = false;
        _wager = 0;
        _joined.Clear();
        ClearCards();
        _state = State.Betting;
        if (dealer != null) dealer.Busy = false;
        ShowChips(Bet);
        ShowBetting();
    }

    // -------------------------------------------------------------- round

    IEnumerator DealRound()
    {
        _state = State.Busy;
        if (dealer != null) dealer.Busy = true;
        ClearCards();
        ShowChips(Stake);
        ShowPlaying();

        yield return Deal(_player, true);
        yield return Deal(_dealer, true);
        yield return Deal(_player, true);
        yield return Deal(_dealer, false);

        bool playerBlackjack = _player.IsBlackjack, dealerBlackjack = _dealer.IsBlackjack;
        if (playerBlackjack || dealerBlackjack)
        {
            yield return RevealHole();
            if (playerBlackjack && dealerBlackjack) Finish(_wager, "PUSH", "Blackjack each. That's a push.");
            else if (playerBlackjack) Finish(_wager + _wager * 3 / 2, "BLACKJACK!", "Blackjack! Pays three to two.");
            else Finish(0, "DEALER BLACKJACK", "Blackjack for the house. Sorry!");
            yield break;
        }

        _state = State.PlayerTurn;
        ShowPlaying();
    }

    IEnumerator Hit(bool doubled)
    {
        _state = State.Busy;
        yield return Deal(_player, true);

        int total = _player.Total();
        if (total > 21)
        {
            yield return RevealHole();
            Finish(0, "BUST", $"{total}. Too many!");
        }
        else if (doubled || total == 21)
        {
            yield return DealerPlays();
        }
        else
        {
            _state = State.PlayerTurn;
            ShowPlaying();
        }
    }

    IEnumerator DealerPlays()
    {
        _state = State.Busy;
        yield return RevealHole();
        while (_dealer.Total() < 17)
            yield return Deal(_dealer, true);

        int player = _player.Total(), house = _dealer.Total();
        if (house > 21) Finish(_wager * 2, "YOU WIN", $"{house} - dealer busts. You win!");
        else if (player > house) Finish(_wager * 2, "YOU WIN", $"{player} beats {house}. You win!");
        else if (player == house) Finish(_wager, "PUSH", $"{house} each. Push - your bet comes back.");
        else Finish(0, "DEALER WINS", $"Dealer has {house}. House wins.");
    }

    // payout includes the stake (0 = lost it, wager = push). For someone
    // who is only watching, the wager and so the payout are 0.
    void Finish(int payout, string headline, string dealerLine)
    {
        if (payout > 0 && PlayerBalance.Instance != null) payout = PlayerBalance.Instance.Payout(payout, _wager);
        int net = payout - _wager;
        if (net > 0) BarSounds.Play(BarSounds.Ding, transform.position + Vector3.up, 0.6f);
        if (payout == 0) ShowChips(0);

        if (!_inHand && headline == "YOU WIN") headline = "PLAYERS WIN";
        string colour = net > 0 ? "#5CFF7A" : net < 0 ? "#FF6A5C" : "#FFD24A";
        string amount = net > 0 ? $" +${net}" : net < 0 ? $" -${-net}" : "";
        SetInfo($"<color={colour}>{headline}{amount}</color>\n<size=45%>{(Shared() ? "HAND" : "YOU")} {_player.Total()}   DEALER {_dealer.Total()}   -   DEAL TO PLAY AGAIN</size>");

        if (dealer != null)
        {
            dealer.Say(dealerLine, 4.5f);
            dealer.Busy = false;
        }
        _inHand = false;
        _wager = 0;
        _state = State.RoundOver;
    }

    // -------------------------------------------------------------- cards

    // Seeded, so every player's shoe is in the same order.
    void Shuffle(int seed)
    {
        var random = new System.Random(seed);
        _shoe.Clear();
        for (int d = 0; d < Mathf.Max(1, decks); d++)
            for (int card = 0; card < 52; card++) _shoe.Add(card);
        for (int i = _shoe.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (_shoe[i], _shoe[j]) = (_shoe[j], _shoe[i]);
        }
    }

    IEnumerator Deal(Hand hand, bool faceUp)
    {
        int card = _shoe[_shoe.Count - 1];
        _shoe.RemoveAt(_shoe.Count - 1);

        Transform row = hand == _player ? playerCards : dealerCards;
        Vector3 target = row.position - row.right * (hand.Cards.Count * cardSpacing) + row.up * (0.002f + hand.Cards.Count * 0.0005f);
        Quaternion up = row.rotation, down = row.rotation * Quaternion.Euler(0f, 0f, 180f);

        var t = CardAtlas.CreateCard(card, cardMaterial).transform;
        t.SetParent(transform, true);
        Vector3 from = shoe != null ? shoe.position : target + Vector3.up * 0.2f;
        t.SetPositionAndRotation(from, down);
        hand.Cards.Add(card);
        hand.Objects.Add(t);
        if (!faceUp) _holeHidden = true;

        if (dealer != null) dealer.DealTo(target);
        BarSounds.Play(_flick, from, 0.5f);

        for (float k = 0f; k < 1f; k += Time.deltaTime / dealSeconds)
        {
            float e = Mathf.SmoothStep(0f, 1f, k);
            Vector3 p = Vector3.Lerp(from, target, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 0.06f);
            t.SetPositionAndRotation(p, faceUp ? Quaternion.Slerp(down, up, Mathf.Clamp01(e * 1.6f - 0.3f)) : down);
            yield return null;
        }
        t.SetPositionAndRotation(target, faceUp ? up : down);

        if (_state != State.RoundOver) ShowPlaying();
        yield return new WaitForSeconds(pauseSeconds);
    }

    // Turns the dealer's face-down card over where it lies.
    IEnumerator RevealHole()
    {
        if (!_holeHidden || _dealer.Objects.Count < 2) yield break;
        _holeHidden = false;

        var t = _dealer.Objects[1];
        Vector3 at = t.position;
        Quaternion down = t.rotation, up = dealerCards.rotation;
        BarSounds.Play(_flick, at, 0.5f);
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.35f)
        {
            t.SetPositionAndRotation(at + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.06f), Quaternion.Slerp(down, up, Mathf.SmoothStep(0f, 1f, k)));
            yield return null;
        }
        t.SetPositionAndRotation(at, up);
        ShowPlaying();
        yield return new WaitForSeconds(pauseSeconds * 2f);
    }

    void ClearCards()
    {
        foreach (var hand in new[] { _player, _dealer })
        {
            foreach (var t in hand.Objects)
                if (t != null) Destroy(t.gameObject);
            hand.Objects.Clear();
            hand.Cards.Clear();
        }
        _holeHidden = false;
    }

    // ------------------------------------------------------------ display

    void ShowBetting() =>
        SetInfo($"BET <color=#FFD24A>${Bet}</color>\n<size=45%>- / + CHANGES THE BET   -   DEAL TO PLAY</size>");

    void ShowJoining(int seconds)
    {
        if (!Shared()) return; // alone it deals at once - nothing to wait for
        string you = _inHand ? "<color=#5CFF7A>YOU'RE IN</color>" : "PRESS DEAL TO JOIN";
        SetInfo($"DEALING IN <color=#FFD24A>{seconds}</color>\n<size=45%>BET ${Bet}   -   {_joined.Count} IN   -   {you}</size>");
    }

    void ShowPlaying()
    {
        int player = _player.Total(out bool soft);
        string yours = _player.Cards.Count == 0 ? "-" : soft && player < 21 ? $"{player - 10}/{player}" : player.ToString();

        string house = "-";
        if (_dealer.Cards.Count > 0)
            house = _holeHidden ? CardAtlas.Value(_dealer.Cards[0]).ToString() : _dealer.Total().ToString();

        string stake = _inHand ? $"YOUR BET ${_wager}" : "WATCHING";
        SetInfo($"{(Shared() ? "HAND" : "YOU")} <color=#FFD24A>{yours}</color>     DEALER <color=#FFD24A>{house}</color>\n<size=45%>{stake}</size>");
    }

    void SetInfo(string text)
    {
        if (infoPanel != null) infoPanel.SetText(text);
    }

    // A little stack of chips on the betting circle, taller for bigger bets.
    void ShowChips(int amount)
    {
        foreach (var chip in _chips) Destroy(chip);
        _chips.Clear();
        if (betStack == null || amount <= 0) return;

        int count = 1;
        foreach (int bet in bets)
            if (amount > bet) count++;
        count = Mathf.Min(count + (amount > bets[bets.Length - 1] ? 2 : 0), 10);

        for (int i = 0; i < count; i++)
        {
            var chip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            chip.name = "Bet Chip";
            var collider = chip.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            chip.transform.SetParent(betStack, false);
            chip.transform.localPosition = new Vector3(0f, 0.004f + i * 0.008f, 0f);
            chip.transform.localRotation = Quaternion.Euler(0f, i * 37f, 0f);
            chip.transform.localScale = new Vector3(0.045f, 0.0038f, 0.045f);
            if (chipMaterial != null) chip.GetComponent<Renderer>().sharedMaterial = chipMaterial;
            _chips.Add(chip);
        }
    }

    // A short burst of filtered noise: a card snapping onto felt.
    static AudioClip CreateFlick()
    {
        const int rate = 44100;
        var data = new float[(int)(rate * 0.07f)];
        var random = new System.Random(3);
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            float noise = (float)random.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.45f;
            data[i] = low * (1f - t) * (1f - t) * 0.8f;
        }
        var clip = AudioClip.Create("Card Flick", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
