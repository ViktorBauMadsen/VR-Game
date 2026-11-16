using System;
using System.Collections.Generic;
using Casino.Multiplayer;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum RunPhase : byte { FreePlay, Running, Dead }
public enum Powerup : byte { LuckyCharm, Bribe, Stall, Vest }
public enum RunEvent : byte { None, Started, Survived, VestSaved, Dead, Abandoned }

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
    const string StateMessage = "Casino.Run.v1", BalanceMessage = "Casino.Bal.v1", BuyMessage = "Casino.Buy.v1";
    const int StateBytes = 43;
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
    [SerializeField] float quotaGrowth = 1.4f;
    [Tooltip("Part of the quota the collector actually takes when you make it.")]
    [SerializeField, Range(0f, 1f)] float collectorCut = 0.25f;
    [SerializeField] float dayLengthSeconds = 300f;

    [Header("Powerups")]
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
    public int CollectorTake => Mathf.RoundToInt(Quota * collectorCut);
    public float DayLength => dayLengthSeconds;

    // True when this machine decides what happens (solo, or the host).
    public bool Authority => multiplayer == null || !multiplayer.IsGuest;

    // Something the player should be told about: what happened and how much they paid.
    public event Action<RunEvent, int> Happened;

    CasinoMultiplayer multiplayer;
    readonly Dictionary<ulong, int> guestBalances = new Dictionary<ulong, int>();
    float nextSync;
    bool hubCashGiven;

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

    public void StartRun()
    {
        if (!Authority || Phase == RunPhase.Running) return;
        Day = 1;
        Vests = 0;
        BeginDay();
        Phase = RunPhase.Running;
        foreach (ulong guest in new List<ulong>(guestBalances.Keys)) guestBalances[guest] = startingCash;
        SendStateToAll(RunEvent.Started, null);
        Apply(RunEvent.Started, 0);
    }

    void BeginDay()
    {
        Players = 1 + (multiplayer != null && multiplayer.IsHost ? multiplayer.GuestIds.Count : 0);
        Quota = QuotaFor(Day, Players);
        TimeLeft = dayLengthSeconds;
        BribedToday = false;
        StallsToday = 0;
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
            DiedOnDay = Day;
            Phase = RunPhase.Dead;
            SendStateToAll(RunEvent.Dead, null);
            Apply(RunEvent.Dead, 0);
            return;
        }

        if (!paid) Vests--;
        int take = Mathf.Min(total, CollectorTake);
        var shares = new Dictionary<ulong, int>();
        foreach (var pair in guestBalances)
            if (multiplayer != null && multiplayer.IsGuestId(pair.Key)) shares[pair.Key] = Share(take, pair.Value, total);
        foreach (var pair in shares) guestBalances[pair.Key] = Mathf.Max(0, guestBalances[pair.Key] - pair.Value);

        Day++;
        BeginDay();
        var what = paid ? RunEvent.Survived : RunEvent.VestSaved;
        SendStateToAll(what, shares);
        Apply(what, Share(take, mine, total));
    }

    // Everyone chips in proportionally to what they are holding.
    static int Share(int take, int cash, int total) => total <= 0 ? 0 : (int)((long)take * cash / total);

    // What an event means for THIS player's wallet and screen. Runs on every machine.
    void Apply(RunEvent what, int pay)
    {
        var balance = PlayerBalance.Instance;
        switch (what)
        {
            case RunEvent.Started:
                CharmCharges = 0;
                if (balance != null) balance.SetBalance(startingCash);
                break;
            case RunEvent.Survived:
                if (balance != null) balance.ForceSpend(pay);
                RememberBest(Day - 1);
                break;
            case RunEvent.VestSaved:
                if (balance != null)
                {
                    balance.ForceSpend(pay);
                    if (balance.Balance < mercyCash) balance.SetBalance(mercyCash);
                }
                RememberBest(Day - 1);
                break;
            case RunEvent.Dead:
                CharmCharges = 0;
                RememberBest(DiedOnDay - 1);
                if (balance != null) balance.SetBalance(hubCash);
                break;
            case RunEvent.Abandoned:
                CharmCharges = 0;
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
            default: return "BULLETPROOF VEST";
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
            default:
                return "Miss a quota and the whole team survives anyway - once. The collector still takes what he can.";
        }
    }

    public string Owned(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.LuckyCharm: return CharmCharges > 0 ? CharmCharges + " wins left" : "";
            case Powerup.Bribe: return BribedToday ? "used today" : "";
            case Powerup.Stall: return StallsToday > 0 ? StallsToday + "/" + maxStallsPerDay + " today" : "";
            default: return Vests > 0 ? "wearing it" : "";
        }
    }

    public int Price(Powerup powerup)
    {
        int quota = Phase == RunPhase.Running ? Quota : QuotaFor(1, Players);
        float perPlayer = quota / (float)Mathf.Max(1, Players);
        switch (powerup)
        {
            case Powerup.LuckyCharm: return Mathf.Max(10, RoundTo(perPlayer * 0.15f, 10));
            case Powerup.Bribe: return Mathf.Max(10, RoundTo(quota * 0.10f, 10));
            case Powerup.Stall: return Mathf.Max(10, RoundTo(quota * 0.06f, 10));
            default: return Mathf.Max(10, RoundTo(quota * 0.5f, 10));
        }
    }

    // Null when it can be bought right now, otherwise a short reason.
    public string WhyNot(Powerup powerup)
    {
        if (Phase != RunPhase.Running) return "START A RUN";
        if (!TeamAllows(powerup)) return powerup == Powerup.Vest ? "OWNED" : "USED UP";
        var balance = PlayerBalance.Instance;
        if (balance == null || balance.Balance < Price(powerup)) return "TOO POOR";
        return null;
    }

    bool TeamAllows(Powerup powerup)
    {
        switch (powerup)
        {
            case Powerup.Bribe: return !BribedToday;
            case Powerup.Stall: return StallsToday < maxStallsPerDay;
            case Powerup.Vest: return Vests < 1;
            default: return true;
        }
    }

    public bool Buy(Powerup powerup)
    {
        var balance = PlayerBalance.Instance;
        if (WhyNot(powerup) != null || balance == null || !balance.TrySpend(Price(powerup))) return false;

        if (powerup == Powerup.LuckyCharm) CharmCharges += charmUses;
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
        }
    }

    int BoostWinnings(int profit)
    {
        if (CharmCharges <= 0 || Phase != RunPhase.Running) return profit;
        CharmCharges--;
        return profit + Mathf.RoundToInt(profit * charmBonus);
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
    }

    void OnSessionEnded()
    {
        guestBalances.Clear();
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
            writer.WriteValueSafe(BribedToday);
            writer.WriteValueSafe(StallsToday);
            writer.WriteValueSafe(DiedOnDay);
            writer.WriteValueSafe(startingCash);
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
        reader.ReadValueSafe(out bool bribed);
        reader.ReadValueSafe(out int stalls);
        reader.ReadValueSafe(out int diedOnDay);
        reader.ReadValueSafe(out int hostStartingCash);
        reader.ReadValueSafe(out byte what);
        reader.ReadValueSafe(out int pay);
        if (phase > (byte)RunPhase.Dead || what > (byte)RunEvent.Abandoned || float.IsNaN(timeLeft) || float.IsInfinity(timeLeft)) return;

        // Joined a lobby whose run is already going (or already over): swap
        // hub money for run money, or back, so the pooled cash stays honest.
        var wallet = PlayerBalance.Instance;
        bool wasRunning = Phase == RunPhase.Running, nowRunning = phase == (byte)RunPhase.Running;
        if (what == (byte)RunEvent.None && wasRunning != nowRunning && wallet != null)
            wallet.SetBalance(nowRunning ? Mathf.Clamp(hostStartingCash, 0, 1000000) : hubCash);

        Phase = (RunPhase)phase;
        Day = Mathf.Max(0, day);
        Quota = Mathf.Max(0, quota);
        TimeLeft = Mathf.Clamp(timeLeft, 0f, 86400f);
        TeamCash = Mathf.Max(0, teamCash);
        Players = Mathf.Clamp(players, 1, CasinoMultiplayer.MaxPlayers);
        Vests = Mathf.Clamp(vests, 0, 9);
        BribedToday = bribed;
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
        if (id == (byte)Powerup.LuckyCharm || id > (byte)Powerup.Vest) return;
        var powerup = (Powerup)id;
        if (Phase != RunPhase.Running || !TeamAllows(powerup)) return;
        ApplyTeamPowerup(powerup);
        SendStateToAll(RunEvent.None, null);
    }
}
