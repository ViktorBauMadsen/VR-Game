using System;
using System.Collections.Generic;
using Casino.Multiplayer;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Gathering = "Start game" was pressed: everyone is outside the casino door
// with run money, the clock is not ticking yet, waiting for all to press the door.
public enum RunPhase : byte { FreePlay, Running, Dead, Gathering }
public enum Powerup : byte { LuckyCharm, Bribe, Stall, Vest, Greed, PocketMoney, TaxDodge, HotStreak }
public enum RunEvent : byte { None, Started, Survived, VestSaved, Dead, Abandoned, Gathering }
// The twist of the day - picked at random by the host, good or bad.
public enum DayEvent : byte { None, HappyHour, EarlyCollector, LongDay, GenerousBoss, Shakedown, TokenRain, TightTables, TaxDay }

// The game loop on top of the casino: you owe the family money.
//
// A run is a series of days. Each day has a quota and a timer. When the timer
// runs out (or you choose to pay up early) the collector checks the whole
// team's cash: at or above the quota, he takes his cut and you live to see
// the next day - with a bigger quota. Short, and everybody gets shot: the run
// is over and you start again from day 1.
//
// Outside a run the casino is free play, exactly as before.
//
// Multiplayer: the host owns the run (day, quota, clock). Guests report their
// balance to the host and are told what happened. The quota scales with the
// number of players and the team's cash is pooled, so one lucky player can
// carry the others.
//
// Powerups are bought from the SHOP tab of the menu (CasinoMenu), paid from
// your own balance - see Describe() for what each does.
//
// Tuning: this object creates itself at runtime with the defaults below. To
// change them, add a CasinoRun component to any object in the scene and edit
// it in the Inspector - the scene's one is used instead.
[DefaultExecutionOrder(10010)]
public sealed class CasinoRun : MonoBehaviour
{
    const int DayEventCount = 9;
    const string StateMessage = "Casino.Run.v5", BalanceMessage = "Casino.Bal.v1", BuyMessage = "Casino.Buy.v1", ReadyMessage = "Casino.Ready.v1";
    const string RerollMessage = "Casino.Reroll.v1";
    const int StateBytes = 57;
    public const int PowerupCount = 8;
    // How many different powerups the shop stocks each day.
    public const int OfferSize = 3;
    const string BestDayKey = "Casino.BestDay";

    public static CasinoRun Instance { get; private set; }

    [Header("Hub")]
    [Tooltip("Play money everyone has in the hub (outside a run). Restored whenever a run ends.")]
    [SerializeField] int hubCash = 100000;

    [Header("Run")]
    [Tooltip("Cash every player starts a run with.")]
    [SerializeField] int startingCash = 500;
    [Tooltip("Day 1 quota per player.")]
    [SerializeField] int firstQuota = 600;
    [Tooltip("The quota is multiplied by this every day.")]
    [SerializeField] float quotaGrowth = 1.8f;
    [Tooltip("Part of the quota the collector actually takes when you make it.")]
    [SerializeField, Range(0f, 1f)] float collectorCut = 0.25f;
    [SerializeField] float dayLengthSeconds = 300f;

    [Header("Tokens (the shop's currency)")]
    [Tooltip("Tokens each player gets for making the quota at all.")]
    [SerializeField] int tokensForQuota = 2;
    [Tooltip("One extra token for every this-many percent the team is over the quota.")]
    [SerializeField] float percentOverPerBonusToken = 10f;
    [SerializeField] int maxBonusTokens = 10;
    [SerializeField] int stallPrice = 1;
    [SerializeField] int charmPrice = 2;
    [SerializeField] int bribePrice = 3;
    [SerializeField] int vestPrice = 6;
    [SerializeField] int greedPrice = 2;
    [SerializeField] int pocketMoneyPrice = 1;
    [SerializeField] int taxDodgePrice = 3;
    [SerializeField] int hotStreakPrice = 2;

    [Header("Powerups")]
    [Tooltip("Double Down: today's quota is multiplied by this, and today's tokens are doubled.")]
    [SerializeField] float greedQuotaMultiplier = 1.5f;
    [Tooltip("Pocket Money: cash handed over, as a part of today's quota per player.")]
    [SerializeField, Range(0f, 1f)] float pocketMoneyShare = 0.25f;
    [SerializeField] int hotStreakUses = 2;
    [SerializeField] int charmUses = 5;
    [SerializeField, Range(0f, 2f)] float charmBonus = 0.5f;
    [SerializeField, Range(0f, 0.9f)] float bribeDiscount = 0.2f;
    [SerializeField] float stallSeconds = 60f;
    [SerializeField] int maxStallsPerDay = 2;
    [Tooltip("What a player is left with at least after a vest saves the team.")]
    [SerializeField] int mercyCash = 100;

    public RunPhase Phase { get; private set; }
    public int Day { get; private set; }
    public int Quota { get; private set; }
    public float TimeLeft { get; private set; }
    public int TeamCash { get; private set; }
    public int Players { get; private set; } = 1;
    public int Vests { get; private set; }
    public bool BribedToday { get; private set; }
    public int StallsToday { get; private set; }
    public int CharmCharges { get; private set; }
    public int DiedOnDay { get; private set; }
    public int BestDay { get; private set; }
    // This player's shop currency, earned by beating quotas. Gone when the run ends.
    public int Tokens { get; private set; }
    // Tokens handed out at the last collection (for the announcement).
    public int LastAward { get; private set; }
    // At the door: how many players have pressed it, and whether this one has.
    public int ReadyCount { get; private set; }
    public bool LocalReady { get; private set; }
    // This player's place in the session (host 0) - picks a spawn spot.
    public int LocalSlot => multiplayer != null ? (int)(multiplayer.LocalId % 8) : 0;
    public bool GreedToday { get; private set; }
    public bool TaxDodgeToday { get; private set; }
    public int HotCharges { get; private set; }
    // The three powerups the shop sells today (the same for the whole team),
    // and how often it has been rerolled today - each reroll costs double.
    public Powerup[] Offer { get; } = { Powerup.LuckyCharm, Powerup.Bribe, Powerup.Stall };
    public int RerollCount { get; private set; }
    public int RerollPrice => 1 << Mathf.Min(RerollCount, 10);
    public int CollectorTake => TaxDodgeToday ? 0 : Mathf.RoundToInt(Quota * collectorCut * (TodayEvent == DayEvent.TaxDay ? 2f : 1f));

    // Today's random twist (none on day 1).
    public DayEvent TodayEvent { get; private set; }

    // This player's numbers for the run, for the end screen.
    public int StatBiggestWin { get; private set; }
    public int StatWinnings { get; private set; }
    public int StatWins { get; private set; }
    public int StatTokensEarned { get; private set; }
    public int StatPowerupsBought { get; private set; }
    public int StatPeakCash { get; private set; }
    // Who was holding the most cash across the run's collections (host = 0).
    public ulong TopEarner { get; private set; }
    public bool TopEarnerIsMe => multiplayer == null || TopEarner == multiplayer.LocalId;

    public static string EventTitle(DayEvent e)
    {
        switch (e)
        {
            case DayEvent.HappyHour: return "HAPPY HOUR";
            case DayEvent.EarlyCollector: return "THE COLLECTOR IS EARLY";
            case DayEvent.LongDay: return "SLOW DAY";
            case DayEvent.GenerousBoss: return "THE BOSS IS IN A GOOD MOOD";
            case DayEvent.Shakedown: return "SHAKEDOWN";
            case DayEvent.TokenRain: return "TOKEN BONUS";
            case DayEvent.TightTables: return "TIGHT TABLES";
            case DayEvent.TaxDay: return "TAX DAY";
            default: return "";
        }
    }

    public static string EventText(DayEvent e)
    {
        switch (e)
        {
            case DayEvent.HappyHour: return "Every win pays 25% extra profit today.";
            case DayEvent.EarlyCollector: return "You only get 60% of the usual time today.";
            case DayEvent.LongDay: return "You get 50% more time today.";
            case DayEvent.GenerousBoss: return "Today's quota is 15% lower.";
            case DayEvent.Shakedown: return "Today's quota is 20% higher.";
            case DayEvent.TokenRain: return "Make the quota today for 2 extra tokens each.";
            case DayEvent.TightTables: return "Every win pays 20% less profit today.";
            case DayEvent.TaxDay: return "The collector takes a double cut today.";
            default: return "";
        }
    }

    public static bool EventIsGood(DayEvent e) =>
        e == DayEvent.HappyHour || e == DayEvent.LongDay || e == DayEvent.GenerousBoss || e == DayEvent.TokenRain;
    public float DayLength => dayLengthSeconds;

    // True when this machine decides what happens (solo, or the host).
    public bool Authority => multiplayer == null || !multiplayer.IsGuest;

    // Something the player should be told about: what happened and how much they paid.
    public event Action<RunEvent, int> Happened;

    CasinoMultiplayer multiplayer;
    readonly Dictionary<ulong, int> guestBalances = new Dictionary<ulong, int>();
    readonly HashSet<ulong> ready = new HashSet<ulong>();
    readonly Dictionary<ulong, int> carried = new Dictionary<ulong, int>();
    float nextSync;
    bool hubCashGiven, gathered, pocketMoneyToday;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        Install();
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    static void Install()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        var run = FindAnyObjectByType<CasinoRun>();
        if (run == null) run = new GameObject("Casino Run").AddComponent<CasinoRun>();
        if (run.GetComponent<CasinoRunHud>() == null) run.gameObject.AddComponent<CasinoRunHud>();
        if (run.GetComponent<CasinoMenu>() == null) run.gameObject.AddComponent<CasinoMenu>();
        if (run.GetComponent<CasinoTables>() == null) run.gameObject.AddComponent<CasinoTables>();
        if (run.GetComponent<CasinoProps>() == null) run.gameObject.AddComponent<CasinoProps>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        BestDay = PlayerPrefs.GetInt(BestDayKey, 0);
        PlayerBalance.WinningsModifier = BoostWinnings;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        PlayerBalance.WinningsModifier = null;
        Unhook();
    }

    // ------------------------------------------------------------ the day

    void Update()
    {
        Hook();
        var balance = PlayerBalance.Instance;
        if (balance == null) return;
        if (!hubCashGiven)
        {
            hubCashGiven = true;
            if (Phase != RunPhase.Running) balance.SetBalance(hubCash);
        }

        if (Phase == RunPhase.Running) StatPeakCash = Mathf.Max(StatPeakCash, balance.Balance);

        if (Authority)
        {
            Players = 1;
            TeamCash = balance.Balance;
            if (multiplayer != null && multiplayer.IsHost)
                foreach (ulong guest in multiplayer.GuestIds)
                {
                    Players++;
                    if (guestBalances.TryGetValue(guest, out int cash)) TeamCash += cash;
                }

            if (Phase == RunPhase.Running)
            {
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f) Collect();
            }
            else if (Phase == RunPhase.Gathering)
            {
                // Everyone who is still here has pressed the door: day 1 begins.
                if (multiplayer != null) ready.RemoveWhere(id => id != 0UL && !multiplayer.IsGuestId(id));
                ReadyCount = ready.Count;
                if (ReadyCount >= Players) OpenDoor();
            }
        }
        else if (Phase == RunPhase.Running)
        {
            TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
        }

        if (multiplayer != null && multiplayer.InSession && Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 0.5f;
            if (multiplayer.IsHost) SendStateToAll(RunEvent.None, null);
            else if (multiplayer.IsGuest) SendBalance(balance.Balance);
        }
    }

    public int QuotaFor(int day, int players) =>
        RoundTo(firstQuota * Mathf.Pow(quotaGrowth, Mathf.Max(0, day - 1)), 50) * Mathf.Max(1, players);

    // "Start game": everyone is sent to the casino door with run money. The
    // clock only starts once all players have pressed the door (PressDoor).
    // In a scene without a RunEntrance the run simply starts on the spot.
    public void StartRun()
    {
        if (!Authority || Phase == RunPhase.Running || Phase == RunPhase.Gathering) return;
        Day = 1;
        Vests = 0;
        BeginDay();
        foreach (ulong guest in new List<ulong>(guestBalances.Keys)) guestBalances[guest] = startingCash;
        if (RunEntrance.Instance == null) { OpenDoor(); return; }

        ready.Clear();
        ReadyCount = 0;
        Phase = RunPhase.Gathering;
        SendStateToAll(RunEvent.Gathering, null);
        Apply(RunEvent.Gathering, 0);
    }

    void OpenDoor()
    {
        Phase = RunPhase.Running;
        SendStateToAll(RunEvent.Started, null);
        Apply(RunEvent.Started, 0);
    }

    // This player pressed the casino door.
    public void PressDoor()
    {
        if (Phase != RunPhase.Gathering || LocalReady) return;
        LocalReady = true;
        if (Authority) { ready.Add(0UL); return; }

        ReadyCount++; // shown at once; the host's next update confirms it
        var messaging = multiplayer.Network.CustomMessagingManager;
        if (messaging == null) return;
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe((byte)1);
        messaging.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    void ReceiveReady(ulong sender, FastBufferReader reader)
    {
        if (multiplayer == null || !multiplayer.IsHost || !multiplayer.IsGuestId(sender)) return;
        if (Phase == RunPhase.Gathering) ready.Add(sender);
    }

    void BeginDay()
    {
        Players = 1 + (multiplayer != null && multiplayer.IsHost ? multiplayer.GuestIds.Count : 0);
        Quota = QuotaFor(Day, Players);
        TimeLeft = dayLengthSeconds;

        // Today's twist: nothing on day 1, and one day in four stays quiet.
        TodayEvent = Day <= 1 || UnityEngine.Random.value < 0.25f
            ? DayEvent.None : (DayEvent)UnityEngine.Random.Range(1, DayEventCount);
        switch (TodayEvent)
        {
            case DayEvent.EarlyCollector: TimeLeft *= 0.6f; break;
            case DayEvent.LongDay: TimeLeft *= 1.5f; break;
            case DayEvent.GenerousBoss: Quota = RoundTo(Quota * 0.85f, 50); break;
            case DayEvent.Shakedown: Quota = RoundTo(Quota * 1.2f, 50); break;
        }
        BribedToday = false;
        StallsToday = 0;
        GreedToday = false;
        TaxDodgeToday = false;
        RerollCount = 0;
        RollOffer();
    }

    // Picks today's stock: OfferSize different powerups, not the same set as before.
    void RollOffer()
    {
        var previous = (Powerup[])Offer.Clone();
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var pool = new List<int>();
            for (int i = 0; i < PowerupCount; i++) pool.Add(i);
            for (int i = 0; i < OfferSize; i++)
            {
                int pick = UnityEngine.Random.Range(0, pool.Count);
                Offer[i] = (Powerup)pool[pick];
                pool.RemoveAt(pick);
            }
            int same = 0;
            foreach (var powerup in Offer)
                if (Array.IndexOf(previous, powerup) >= 0) same++;
            if (same < OfferSize) break;
        }
    }

    // Null when the shop is selling right now, otherwise a short reason.
    string ShopClosed()
    {
        if (Phase != RunPhase.Running && Phase != RunPhase.Gathering) return "START A RUN";
        // With a Token Stall at the door, it only sells while everyone is
        // gathered there - decide before the day starts.
        if (Phase == RunPhase.Running && TokenStall.Instance != null && RunEntrance.Instance != null) return "CLOSED UNTIL TOMORROW";
        return null;
    }

    public string WhyNotReroll()
    {
        string closed = ShopClosed();
        if (closed != null) return closed;
        return Tokens < RerollPrice ? "NEED " + RerollPrice + " TOKENS" : null;
    }

    // New random stock for the whole team. Whoever presses pays; every
    // reroll on the same day costs twice as much as the one before.
    public bool Reroll()
    {
        if (WhyNotReroll() != null) return false;
        Tokens -= RerollPrice;
        RerollCount++;
        if (Authority)
        {
            RollOffer();
            SendStateToAll(RunEvent.None, null);
        }
        else
        {
            var messaging = multiplayer.Network.CustomMessagingManager;
            if (messaging == null) return true;
            using var writer = new FastBufferWriter(4, Allocator.Temp);
            writer.WriteValueSafe((byte)1);
            messaging.SendNamedMessage(RerollMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
        return true;
    }

    void ReceiveReroll(ulong sender, FastBufferReader reader)
    {
        if (multiplayer == null || !multiplayer.IsHost || !multiplayer.IsGuestId(sender) || ShopClosed() != null) return;
        RerollCount++;
        RollOffer();
        SendStateToAll(RunEvent.None, null);
    }

    // Pay before the clock runs out - skips the rest of the day.
    public void PayUpNow()
    {
        if (Authority && Phase == RunPhase.Running && TeamCash >= Quota) Collect();
    }

    // Back to free play without anyone getting shot.
    public void Abandon()
    {
        if (!Authority || Phase == RunPhase.FreePlay) return;
        Phase = RunPhase.FreePlay;
        SendStateToAll(RunEvent.Abandoned, null);
        Apply(RunEvent.Abandoned, 0);
    }

    void Collect()
    {
        var balance = PlayerBalance.Instance;
        int mine = balance != null ? balance.Balance : 0;
        int total = TeamCash;
        bool paid = total >= Quota;
        if (!paid && Vests <= 0)
        {
            Die();
            return;
        }

        if (!paid) Vests--;
        // 0 when the vest had to save you.
        LastAward = (TokensFor(total, Quota) + (paid && TodayEvent == DayEvent.TokenRain ? 2 : 0)) * (GreedToday ? 2 : 1);
        // For the end screen: who was carrying the team's money.
        carried.TryGetValue(0UL, out int hostCarried);
        carried[0UL] = hostCarried + mine;
        foreach (var pair in guestBalances)
        {
            carried.TryGetValue(pair.Key, out int guestCarried);
            carried[pair.Key] = guestCarried + pair.Value;
        }
        int take = Mathf.Min(total, CollectorTake);
        var shares = new Dictionary<ulong, int>();
        foreach (var pair in guestBalances)
            if (multiplayer != null && multiplayer.IsGuestId(pair.Key)) shares[pair.Key] = Share(take, pair.Value, total);
        foreach (var pair in shares) guestBalances[pair.Key] = Mathf.Max(0, guestBalances[pair.Key] - pair.Value);

        Day++;
        BeginDay();
        // Back outside the door between days: time to talk it over and spend
        // tokens before everyone presses the door again.
        if (RunEntrance.Instance != null)
        {
            Phase = RunPhase.Gathering;
            ready.Clear();
            ReadyCount = 0;
        }
        var what = paid ? RunEvent.Survived : RunEvent.VestSaved;
        SendStateToAll(what, shares);
        Apply(what, Share(take, mine, total));
    }

    void Die()
    {
        // Count the final day's cash too, then name whoever carried the most.
        var wallet = PlayerBalance.Instance;
        carried.TryGetValue(0UL, out int hostCarried);
        carried[0UL] = hostCarried + (wallet != null ? wallet.Balance : 0);
        foreach (var pair in guestBalances)
        {
            carried.TryGetValue(pair.Key, out int guestCarried);
            carried[pair.Key] = guestCarried + pair.Value;
        }
        int most = -1;
        foreach (var pair in carried)
            if (pair.Value > most) { most = pair.Value; TopEarner = pair.Key; }
        carried.Clear();

        DiedOnDay = Day;
        Phase = RunPhase.Dead;
        SendStateToAll(RunEvent.Dead, null);
        Apply(RunEvent.Dead, 0);
    }

    // No point going on: the mafia shoots everyone right now and the run is lost.
    public void GiveUp()
    {
        if (Authority && (Phase == RunPhase.Running || Phase == RunPhase.Gathering)) Die();
    }

    // Everyone chips in proportionally to what they are holding.
    static int Share(int take, int cash, int total) => total <= 0 ? 0 : (int)((long)take * cash / total);

    // What an event means for THIS player's wallet and screen. Runs on every machine.
    void Apply(RunEvent what, int pay)
    {
        var balance = PlayerBalance.Instance;
        // A new day (or no run at all): personal once-a-day buys are available again.
        if (what != RunEvent.None && !(what == RunEvent.Started && gathered)) pocketMoneyToday = false;
        if (what == RunEvent.Gathering || what == RunEvent.Dead || what == RunEvent.Abandoned
            || (what == RunEvent.Started && !gathered)) HotCharges = 0;
        if (what == RunEvent.Gathering || (what == RunEvent.Started && !gathered)) ResetStats();
        if (what == RunEvent.Survived) StatTokensEarned += LastAward;
        switch (what)
        {
            case RunEvent.Gathering:
                CharmCharges = 0;
                Tokens = 0;
                LocalReady = false;
                gathered = true;
                if (balance != null) balance.SetBalance(startingCash);
                break;
            case RunEvent.Started:
                // Already handed out at the door, unless there was no door.
                if (!gathered)
                {
                    CharmCharges = 0;
                    Tokens = 0;
                    if (balance != null) balance.SetBalance(startingCash);
                }
                gathered = false;
                LocalReady = false;
                break;
            case RunEvent.Survived:
                Tokens += LastAward;
                gathered = Phase == RunPhase.Gathering;
                LocalReady = false;
                if (balance != null) balance.ForceSpend(pay);
                RememberBest(Day - 1);
                break;
            case RunEvent.VestSaved:
                gathered = Phase == RunPhase.Gathering;
                LocalReady = false;
                if (balance != null)
                {
                    balance.ForceSpend(pay);
                    if (balance.Balance < mercyCash) balance.SetBalance(mercyCash);
                }
                RememberBest(Day - 1);
                break;
            case RunEvent.Dead:
                CharmCharges = 0;
                Tokens = 0;
                RememberBest(DiedOnDay - 1);
                if (balance != null) balance.SetBalance(hubCash);
                break;
            case RunEvent.Abandoned:
                CharmCharges = 0;
                Tokens = 0;
                gathered = false;
                LocalReady = false;
                if (balance != null) balance.SetBalance(hubCash);
                break;
        }
        if (what != RunEvent.None) Happened?.Invoke(what, pay);
    }

    void RememberBest(int daysSurvived)
    {
        if (daysSurvived <= BestDay) return;
        BestDay = daysSurvived;
        PlayerPrefs.SetInt(BestDayKey, BestDay);
    }

    // ----------------------------------------------------------- powerups

    public static string Title(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.LuckyCharm: return "LUCKY CHARM";
            case Powerup.Bribe: return "BRIBE THE COLLECTOR";
            case Powerup.Stall: return "STALL FOR TIME";
            case Powerup.Vest: return "BULLETPROOF VEST";
            case Powerup.Greed: return "DOUBLE DOWN";
            case Powerup.PocketMoney: return "POCKET MONEY";
            case Powerup.TaxDodge: return "TAX DODGE";
            default: return "HOT STREAK";
        }
    }

    // Team powerups change the shared run (the host applies them); the
    // others only affect the player who bought them.
    public static bool IsTeam(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.Bribe:
            case Powerup.Stall:
            case Powerup.Vest:
            case Powerup.Greed:
            case Powerup.TaxDodge:
                return true;
            default:
                return false;
        }
    }

    public string Describe(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.LuckyCharm:
                return $"Your next {charmUses} wins pay {Mathf.RoundToInt(charmBonus * 100f)}% extra profit. Only works for you.";
            case Powerup.Bribe:
                return $"Knocks {Mathf.RoundToInt(bribeDiscount * 100f)}% off today's quota. Once per day.";
            case Powerup.Stall:
                return $"Adds {Mathf.RoundToInt(stallSeconds)} seconds to today's clock. {maxStallsPerDay} per day.";
            case Powerup.Vest:
                return "Miss a quota and the whole team survives anyway - once. The collector still takes what he can.";
            case Powerup.Greed:
                return $"Today's quota goes up {Mathf.RoundToInt((greedQuotaMultiplier - 1f) * 100f)}%, but everyone earns double tokens for making it.";
            case Powerup.PocketMoney:
                return $"Cash in hand right now: {Mathf.RoundToInt(pocketMoneyShare * 100f)}% of today's quota per player. Once per day, only for you.";
            case Powerup.TaxDodge:
                return "The collector takes nothing today - you keep all your cash for tomorrow. Once per day.";
            default:
                return $"Your next {hotStreakUses} wins pay double profit. Only works for you.";
        }
    }

    public string Owned(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.LuckyCharm: return CharmCharges > 0 ? CharmCharges + " wins left" : "";
            case Powerup.Bribe: return BribedToday ? "used today" : "";
            case Powerup.Stall: return StallsToday > 0 ? StallsToday + "/" + maxStallsPerDay + " today" : "";
            case Powerup.Vest: return Vests > 0 ? "wearing it" : "";
            case Powerup.Greed: return GreedToday ? "active today" : "";
            case Powerup.PocketMoney: return pocketMoneyToday ? "used today" : "";
            case Powerup.TaxDodge: return TaxDodgeToday ? "active today" : "";
            default: return HotCharges > 0 ? HotCharges + " wins left" : "";
        }
    }

    // Price in TOKENS (not cash). Rough worth: a day that just scrapes the
    // quota earns tokensForQuota, so the vest is several good days of saving.
    public int Price(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.LuckyCharm: return charmPrice;
            case Powerup.Bribe: return bribePrice;
            case Powerup.Stall: return stallPrice;
            case Powerup.Vest: return vestPrice;
            case Powerup.Greed: return greedPrice;
            case Powerup.PocketMoney: return pocketMoneyPrice;
            case Powerup.TaxDodge: return taxDodgePrice;
            default: return hotStreakPrice;
        }
    }

    // Tokens every player earns when the team ends a day with this much cash:
    // a flat amount for making the quota, plus more for every step over it
    // (percentage of the quota, so it means the same on day 1 and day 10).
    public int TokensFor(int teamCash, int quota)
    {
        if (quota <= 0 || teamCash < quota) return 0;
        float overPercent = (teamCash - quota) * 100f / quota;
        int bonus = Mathf.FloorToInt(overPercent / Mathf.Max(1f, percentOverPerBonusToken));
        return tokensForQuota + Mathf.Clamp(bonus, 0, maxBonusTokens);
    }

    // Null when it can be bought right now, otherwise a short reason.
    public string WhyNot(Powerup powerup)
    {
        string closed = ShopClosed();
        if (closed != null) return closed;
        if (Array.IndexOf(Offer, powerup) < 0) return "NOT IN STOCK";
        if (!TeamAllows(powerup)) return powerup == Powerup.Vest ? "OWNED" : "USED UP";
        return Tokens < Price(powerup) ? "NEED " + Price(powerup) + " TOKENS" : null;
    }

    bool TeamAllows(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.Bribe: return !BribedToday;
            case Powerup.Stall: return StallsToday < maxStallsPerDay;
            case Powerup.Vest: return Vests < 1;
            case Powerup.Greed: return !GreedToday;
            case Powerup.TaxDodge: return !TaxDodgeToday;
            case Powerup.PocketMoney: return !pocketMoneyToday;
            default: return true;
        }
    }

    public bool Buy(Powerup powerup)
    {
        if (WhyNot(powerup) != null) return false;
        Tokens -= Price(powerup);
        StatPowerupsBought++;

        if (powerup == Powerup.LuckyCharm) CharmCharges += charmUses;
        else if (powerup == Powerup.HotStreak) HotCharges += hotStreakUses;
        else if (powerup == Powerup.PocketMoney)
        {
            pocketMoneyToday = true;
            float perPlayer = Quota / (float)Mathf.Max(1, Players);
            if (PlayerBalance.Instance != null) PlayerBalance.Instance.AddBalance(RoundTo(perPlayer * pocketMoneyShare, 10));
        }
        else
        {
            ApplyTeamPowerup(powerup); // guests: shown right away, the host's next update confirms it
            if (!Authority) SendBuy(powerup);
            else SendStateToAll(RunEvent.None, null);
        }
        return true;
    }

    void ApplyTeamPowerup(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.Bribe:
                Quota = Mathf.Max(50, RoundTo(Quota * (1f - bribeDiscount), 50));
                BribedToday = true;
                break;
            case Powerup.Stall:
                TimeLeft += stallSeconds;
                StallsToday++;
                break;
            case Powerup.Vest:
                Vests++;
                break;
            case Powerup.Greed:
                Quota = RoundTo(Quota * greedQuotaMultiplier, 50);
                GreedToday = true;
                break;
            case Powerup.TaxDodge:
                TaxDodgeToday = true;
                break;
        }
    }

    // Hot Streak is used up first (it is the bigger bonus), then the charm.
    // Then today's event has its say, and the win goes into the run's stats.
    int BoostWinnings(int profit)
    {
        if (Phase != RunPhase.Running) return profit;
        if (HotCharges > 0)
        {
            HotCharges--;
            profit *= 2;
        }
        else if (CharmCharges > 0)
        {
            CharmCharges--;
            profit += Mathf.RoundToInt(profit * charmBonus);
        }
        if (TodayEvent == DayEvent.HappyHour) profit += Mathf.RoundToInt(profit * 0.25f);
        else if (TodayEvent == DayEvent.TightTables) profit -= Mathf.RoundToInt(profit * 0.2f);

        StatWins++;
        StatWinnings += profit;
        StatBiggestWin = Mathf.Max(StatBiggestWin, profit);
        return profit;
    }

    void ResetStats()
    {
        StatBiggestWin = StatWinnings = StatWins = StatTokensEarned = StatPowerupsBought = StatPeakCash = 0;
        carried.Clear();
    }

    static int RoundTo(float value, int step) => Mathf.Max(step, Mathf.RoundToInt(value / step) * step);

    // --------------------------------------------------------- networking

    void Hook()
    {
        if (multiplayer != null) return;
        multiplayer = CasinoMultiplayer.Instance;
        if (multiplayer == null) return;
        multiplayer.SessionStarted += OnSessionStarted;
        multiplayer.SessionEnded += OnSessionEnded;
        multiplayer.GuestLeft += OnGuestLeft;
    }

    void Unhook()
    {
        if (multiplayer == null) return;
        multiplayer.SessionStarted -= OnSessionStarted;
        multiplayer.SessionEnded -= OnSessionEnded;
        multiplayer.GuestLeft -= OnGuestLeft;
        multiplayer = null;
    }

    void OnSessionStarted()
    {
        guestBalances.Clear();
        var messaging = multiplayer.Network.CustomMessagingManager;
        if (messaging == null) return;
        messaging.RegisterNamedMessageHandler(StateMessage, ReceiveState);
        messaging.RegisterNamedMessageHandler(BalanceMessage, ReceiveBalance);
        messaging.RegisterNamedMessageHandler(BuyMessage, ReceiveBuy);
        messaging.RegisterNamedMessageHandler(ReadyMessage, ReceiveReady);
        messaging.RegisterNamedMessageHandler(RerollMessage, ReceiveReroll);
    }

    void OnSessionEnded()
    {
        guestBalances.Clear();
        ready.Clear();
        if (LocalReady) ready.Add(0UL);
        // A guest who drops out mid-run carries on alone with a solo-sized quota.
        if (Phase == RunPhase.Running && Players > 1)
        {
            Quota = Mathf.Max(50, RoundTo(Quota / (float)Players, 50));
            Players = 1;
        }
    }

    void OnGuestLeft(ulong guest) => guestBalances.Remove(guest);

    void SendStateToAll(RunEvent what, Dictionary<ulong, int> pays)
    {
        if (multiplayer == null || !multiplayer.IsHost) return;
        var messaging = multiplayer.Network.CustomMessagingManager;
        if (messaging == null) return;
        foreach (ulong guest in multiplayer.GuestIds)
        {
            if (!multiplayer.Network.ConnectedClients.ContainsKey(guest)) continue;
            int pay = 0;
            if (pays != null) pays.TryGetValue(guest, out pay);
            using var writer = new FastBufferWriter(64, Allocator.Temp);
            writer.WriteValueSafe((byte)Phase);
            writer.WriteValueSafe(Day);
            writer.WriteValueSafe(Quota);
            writer.WriteValueSafe(TimeLeft);
            writer.WriteValueSafe(TeamCash);
            writer.WriteValueSafe(Players);
            writer.WriteValueSafe(Vests);
            writer.WriteValueSafe((byte)((BribedToday ? 1 : 0) | (GreedToday ? 2 : 0) | (TaxDodgeToday ? 4 : 0)));
            writer.WriteValueSafe((byte)Offer[0]);
            writer.WriteValueSafe((byte)Offer[1]);
            writer.WriteValueSafe((byte)Offer[2]);
            writer.WriteValueSafe((byte)Mathf.Min(RerollCount, 255));
            writer.WriteValueSafe((byte)TodayEvent);
            writer.WriteValueSafe((byte)Mathf.Min(TopEarner, 255UL));
            writer.WriteValueSafe(StallsToday);
            writer.WriteValueSafe(DiedOnDay);
            writer.WriteValueSafe(startingCash);
            writer.WriteValueSafe(ReadyCount);
            writer.WriteValueSafe(what == RunEvent.Survived ? LastAward : 0);
            writer.WriteValueSafe((byte)what);
            writer.WriteValueSafe(pay);
            messaging.SendNamedMessage(StateMessage, guest, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    void ReceiveState(ulong sender, FastBufferReader reader)
    {
        if (multiplayer == null || !multiplayer.IsGuest || sender != NetworkManager.ServerClientId) return;
        if (reader.Length - reader.Position != StateBytes) return;
        reader.ReadValueSafe(out byte phase);
        reader.ReadValueSafe(out int day);
        reader.ReadValueSafe(out int quota);
        reader.ReadValueSafe(out float timeLeft);
        reader.ReadValueSafe(out int teamCash);
        reader.ReadValueSafe(out int players);
        reader.ReadValueSafe(out int vests);
        reader.ReadValueSafe(out byte flags);
        reader.ReadValueSafe(out byte offer0);
        reader.ReadValueSafe(out byte offer1);
        reader.ReadValueSafe(out byte offer2);
        reader.ReadValueSafe(out byte rerolls);
        reader.ReadValueSafe(out byte dayEvent);
        reader.ReadValueSafe(out byte topEarner);
        reader.ReadValueSafe(out int stalls);
        reader.ReadValueSafe(out int diedOnDay);
        reader.ReadValueSafe(out int hostStartingCash);
        reader.ReadValueSafe(out int readyCount);
        reader.ReadValueSafe(out int award);
        reader.ReadValueSafe(out byte what);
        reader.ReadValueSafe(out int pay);
        if (phase > (byte)RunPhase.Gathering || what > (byte)RunEvent.Gathering || float.IsNaN(timeLeft) || float.IsInfinity(timeLeft)) return;
        ReadyCount = Mathf.Clamp(readyCount, 0, CasinoMultiplayer.MaxPlayers);
        if (what == (byte)RunEvent.Survived) LastAward = Mathf.Clamp(award, 0, 1000);

        // Joined a lobby whose run is already going (or already over): swap
        // hub money for run money, or back, so the pooled cash stays honest.
        var wallet = PlayerBalance.Instance;
        bool wasRunning = Phase == RunPhase.Running || Phase == RunPhase.Gathering;
        bool nowRunning = phase == (byte)RunPhase.Running || phase == (byte)RunPhase.Gathering;
        // Joined while the others are waiting at the door: go and join them.
        if (what == (byte)RunEvent.None && phase == (byte)RunPhase.Gathering && Phase != RunPhase.Gathering)
            what = (byte)RunEvent.Gathering;
        else if (what == (byte)RunEvent.None && wasRunning != nowRunning && wallet != null)
            wallet.SetBalance(nowRunning ? Mathf.Clamp(hostStartingCash, 0, 1000000) : hubCash);

        Phase = (RunPhase)phase;
        Day = Mathf.Max(0, day);
        Quota = Mathf.Max(0, quota);
        TimeLeft = Mathf.Clamp(timeLeft, 0f, 86400f);
        TeamCash = Mathf.Max(0, teamCash);
        Players = Mathf.Clamp(players, 1, CasinoMultiplayer.MaxPlayers);
        Vests = Mathf.Clamp(vests, 0, 9);
        BribedToday = (flags & 1) != 0;
        GreedToday = (flags & 2) != 0;
        TaxDodgeToday = (flags & 4) != 0;
        if (offer0 < PowerupCount && offer1 < PowerupCount && offer2 < PowerupCount)
        {
            Offer[0] = (Powerup)offer0;
            Offer[1] = (Powerup)offer1;
            Offer[2] = (Powerup)offer2;
        }
        RerollCount = rerolls;
        TodayEvent = dayEvent < DayEventCount ? (DayEvent)dayEvent : DayEvent.None;
        TopEarner = topEarner;
        StallsToday = Mathf.Clamp(stalls, 0, 99);
        DiedOnDay = Mathf.Max(0, diedOnDay);
        startingCash = Mathf.Clamp(hostStartingCash, 0, 1000000);
        Apply((RunEvent)what, Mathf.Max(0, pay));
    }

    void SendBalance(int cash)
    {
        var messaging = multiplayer.Network.CustomMessagingManager;
        if (messaging == null) return;
        using var writer = new FastBufferWriter(8, Allocator.Temp);
        writer.WriteValueSafe(cash);
        messaging.SendNamedMessage(BalanceMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    void ReceiveBalance(ulong sender, FastBufferReader reader)
    {
        if (multiplayer == null || !multiplayer.IsHost || !multiplayer.IsGuestId(sender)) return;
        if (reader.Length - reader.Position != 4) return;
        reader.ReadValueSafe(out int cash);
        guestBalances[sender] = Mathf.Max(0, cash);
    }

    void SendBuy(Powerup powerup)
    {
        var messaging = multiplayer.Network.CustomMessagingManager;
        if (messaging == null) return;
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe((byte)powerup);
        messaging.SendNamedMessage(BuyMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    void ReceiveBuy(ulong sender, FastBufferReader reader)
    {
        if (multiplayer == null || !multiplayer.IsHost || !multiplayer.IsGuestId(sender)) return;
        if (reader.Length - reader.Position != 1) return;
        reader.ReadValueSafe(out byte id);
        if (id >= PowerupCount || !IsTeam((Powerup)id)) return;
        var powerup = (Powerup)id;
        if ((Phase != RunPhase.Running && Phase != RunPhase.Gathering) || !TeamAllows(powerup)) return;
        ApplyTeamPowerup(powerup);
        SendStateToAll(RunEvent.None, null);
    }
}
