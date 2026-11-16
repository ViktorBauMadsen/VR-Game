using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// One-player blackjack against the dealer, played with the buttons on the
// table (BlackjackButton): - / + pick the bet, DEAL starts a hand, then
// HIT, STAND or DOUBLE.
//
// Rules: six-deck shoe, reshuffled when it runs low. Dealer draws to 16
// and stands on all 17s. Blackjack pays 3:2. Double is allowed on the
// first two cards (one more card, then you stand). No splitting or
// insurance. The dealer checks for blackjack before you play.
//
// Money follows the same convention as the other games: the bet is taken
// with PlayerBalance.TrySpend when the hand is dealt, and a win pays back
// stake + winnings with AddBalance.
//
// Cards come from CardAtlas and are dealt out of the shoe with a short
// flight; they lie in two rows running from the player's left to right.
public class BlackjackTable : MonoBehaviour
{
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

    [SerializeField] int[] bets = { 10, 20, 50, 100, 200, 500 };
    [SerializeField] int startingBetIndex = 2;
    [SerializeField] float cardSpacing = 0.095f;
    [SerializeField] float dealSeconds = 0.3f;
    [SerializeField] float pauseSeconds = 0.25f;
    [SerializeField] int decks = 6;

    enum State { Betting, Busy, PlayerTurn, RoundOver }

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

    readonly Hand _player = new Hand(), _dealer = new Hand();
    readonly List<int> _shoe = new List<int>();
    readonly List<GameObject> _chips = new List<GameObject>();
    State _state = State.Betting;
    int _betIndex;
    int _wager;
    bool _holeHidden;
    AudioClip _flick;

    int Bet => bets[_betIndex];

    void Start()
    {
        _betIndex = Mathf.Clamp(startingBetIndex, 0, bets.Length - 1);
        _flick = CreateFlick();
        Shuffle();
        ShowChips(Bet);
        ShowBetting();
    }

    // ------------------------------------------------------------ buttons

    public void Press(BlackjackAction action, IXRSelectInteractor who)
    {
        bool ok = false;
        switch (action)
        {
            case BlackjackAction.BetDown:
            case BlackjackAction.BetUp:
                if (_state != State.Betting && _state != State.RoundOver) break;
                _betIndex = Mathf.Clamp(_betIndex + (action == BlackjackAction.BetUp ? 1 : -1), 0, bets.Length - 1);
                if (_state == State.RoundOver) ClearCards();
                _state = State.Betting;
                ShowChips(Bet);
                ShowBetting();
                ok = true;
                break;

            case BlackjackAction.Deal:
                if (_state != State.Betting && _state != State.RoundOver) break;
                if (PlayerBalance.Instance == null || !PlayerBalance.Instance.TrySpend(Bet))
                {
                    if (dealer != null) dealer.Say($"The bet is ${Bet}. You're a little short.");
                    break;
                }
                _wager = Bet;
                StartCoroutine(DealRound());
                ok = true;
                break;

            case BlackjackAction.Hit:
                if (_state != State.PlayerTurn) break;
                StartCoroutine(Hit(false));
                ok = true;
                break;

            case BlackjackAction.Double:
                if (_state != State.PlayerTurn) break;
                if (_player.Cards.Count != 2)
                {
                    if (dealer != null) dealer.Say("You can only double on your first two cards.");
                    break;
                }
                if (PlayerBalance.Instance == null || !PlayerBalance.Instance.TrySpend(_wager))
                {
                    if (dealer != null) dealer.Say("Not enough to double, I'm afraid.");
                    break;
                }
                _wager *= 2;
                ShowChips(_wager);
                StartCoroutine(Hit(true));
                ok = true;
                break;

            case BlackjackAction.Stand:
                if (_state != State.PlayerTurn) break;
                StartCoroutine(DealerPlays());
                ok = true;
                break;
        }

        if (who is XRBaseInputInteractor input) input.SendHapticImpulse(ok ? 0.3f : 0.6f, ok ? 0.06f : 0.15f);
    }

    // -------------------------------------------------------------- round

    IEnumerator DealRound()
    {
        _state = State.Busy;
        if (dealer != null) dealer.Busy = true;
        ClearCards();
        ShowChips(_wager);
        if (_shoe.Count < 52)
        {
            Shuffle();
            if (dealer != null) dealer.Say("Shuffling up.");
            yield return new WaitForSeconds(0.8f);
        }
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

    // payout includes the stake (0 = lost it, wager = push).
    void Finish(int payout, string headline, string dealerLine)
    {
        if (payout > 0 && PlayerBalance.Instance != null) payout = PlayerBalance.Instance.Payout(payout, _wager);
        int net = payout - _wager;
        if (net > 0) BarSounds.Play(BarSounds.Ding, transform.position + Vector3.up, 0.6f);
        if (payout == 0) ShowChips(0);

        string colour = net > 0 ? "#5CFF7A" : net < 0 ? "#FF6A5C" : "#FFD24A";
        string amount = net > 0 ? $" +${net}" : net < 0 ? $" -${-net}" : "";
        SetInfo($"<color={colour}>{headline}{amount}</color>\n<size=45%>YOU {_player.Total()}   DEALER {_dealer.Total()}   -   DEAL TO PLAY AGAIN</size>");

        if (dealer != null)
        {
            dealer.Say(dealerLine, 4.5f);
            dealer.Busy = false;
        }
        _state = State.RoundOver;
    }

    // -------------------------------------------------------------- cards

    void Shuffle()
    {
        _shoe.Clear();
        for (int d = 0; d < Mathf.Max(1, decks); d++)
            for (int card = 0; card < 52; card++) _shoe.Add(card);
        for (int i = _shoe.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
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

    void ShowPlaying()
    {
        int player = _player.Total(out bool soft);
        string yours = _player.Cards.Count == 0 ? "-" : soft && player < 21 ? $"{player - 10}/{player}" : player.ToString();

        string house = "-";
        if (_dealer.Cards.Count > 0)
            house = _holeHidden ? CardAtlas.Value(_dealer.Cards[0]).ToString() : _dealer.Total().ToString();

        SetInfo($"YOU <color=#FFD24A>{yours}</color>     DEALER <color=#FFD24A>{house}</color>\n<size=45%>BET ${_wager}</size>");
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
