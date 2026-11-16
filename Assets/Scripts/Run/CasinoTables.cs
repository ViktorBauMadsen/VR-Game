using System.Collections.Generic;
using Casino.Multiplayer;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Carries the table games over the multiplayer connection, so friends play
// the same roulette spin and the same blackjack hand:
//
//   Roulette   the host's countdown and winning number (RouletteRound), and
//              where everyone put their chips (RouletteBettingManager).
//   Blackjack  the host-approved button presses (BlackjackTable).
//
// The tables themselves know nothing about networking - they talk through a
// few static hooks that this class fills in. With nobody connected the hooks
// do nothing and every table simply runs by itself.
//
// Tables are matched between machines by their position in the scene, so
// everyone must be running the same build of the same scene.
[DefaultExecutionOrder(10005)]
public sealed class CasinoTables : MonoBehaviour
{
    const string RouletteMessage = "Casino.Ro.v1", ChipMessage = "Casino.Chip.v1", BlackjackMessage = "Casino.Bj.v1";

    CasinoMultiplayer multiplayer;
    readonly List<RouletteRound> roulette = new List<RouletteRound>();
    readonly List<BlackjackTable> blackjack = new List<BlackjackTable>();

    bool Connected => multiplayer != null && multiplayer.InSession;
    bool Authority => multiplayer == null || !multiplayer.IsGuest;

    void OnEnable()
    {
        RouletteRound.Shared = BlackjackTable.Shared = () => Connected && multiplayer.PlayerCount > 1;
        RouletteRound.IsAuthority = BlackjackTable.IsAuthority = () => Authority;
        BlackjackTable.LocalId = () => multiplayer != null ? multiplayer.LocalId : 0UL;
        RouletteRound.Send = SendRoulette;
        BlackjackTable.Send = SendBlackjack;
        RouletteBettingManager.LocalBetChanged += SendChip;
    }

    void OnDisable()
    {
        RouletteRound.Shared = BlackjackTable.Shared = () => false;
        RouletteRound.IsAuthority = BlackjackTable.IsAuthority = () => true;
        BlackjackTable.LocalId = () => 0UL;
        RouletteRound.Send = null;
        BlackjackTable.Send = null;
        RouletteBettingManager.LocalBetChanged -= SendChip;
        if (multiplayer == null) return;
        multiplayer.SessionStarted -= OnSessionStarted;
        multiplayer.GuestLeft -= OnPlayerLeft;
        multiplayer.SessionEnded -= OnSessionEnded;
        multiplayer = null;
    }

    void Update()
    {
        if (multiplayer != null) return;
        multiplayer = CasinoMultiplayer.Instance;
        if (multiplayer == null) return;
        multiplayer.SessionStarted += OnSessionStarted;
        multiplayer.GuestLeft += OnPlayerLeft;
        multiplayer.SessionEnded += OnSessionEnded;
    }

    void OnSessionStarted()
    {
        FindTables();
        multiplayer.Register(RouletteMessage, ReceiveRoulette);
        multiplayer.Register(ChipMessage, ReceiveChip);
        multiplayer.Register(BlackjackMessage, ReceiveBlackjack);
    }

    void OnPlayerLeft(ulong player)
    {
        foreach (var round in roulette)
            if (round != null && round.Betting != null) round.Betting.ClearRemoteBets(player);
    }

    void OnSessionEnded()
    {
        foreach (var round in roulette)
            if (round != null && round.Betting != null) round.Betting.ClearRemoteBets();
    }

    // Same order on every machine: sorted by where the table stands.
    void FindTables()
    {
        roulette.Clear();
        roulette.AddRange(FindObjectsByType<RouletteRound>());
        roulette.Sort((a, b) => Compare(a.transform, b.transform));
        blackjack.Clear();
        blackjack.AddRange(FindObjectsByType<BlackjackTable>());
        blackjack.Sort((a, b) => Compare(a.transform, b.transform));
    }

    static int Compare(Transform a, Transform b)
    {
        Vector3 pa = a.position, pb = b.position;
        int x = Mathf.RoundToInt(pa.x * 100f).CompareTo(Mathf.RoundToInt(pb.x * 100f));
        if (x != 0) return x;
        int z = Mathf.RoundToInt(pa.z * 100f).CompareTo(Mathf.RoundToInt(pb.z * 100f));
        return z != 0 ? z : Mathf.RoundToInt(pa.y * 100f).CompareTo(Mathf.RoundToInt(pb.y * 100f));
    }

    // ------------------------------------------------------------ roulette

    void SendRoulette(RouletteRound round, byte kind, int arg)
    {
        if (!Connected) return;
        int table = roulette.IndexOf(round);
        if (table < 0) return;
        using var writer = new FastBufferWriter(8, Allocator.Temp);
        writer.WriteValueSafe((byte)table);
        writer.WriteValueSafe(kind);
        writer.WriteValueSafe(arg);
        if (multiplayer.IsHost) multiplayer.SendToGuests(RouletteMessage, writer);
        else multiplayer.SendToHost(RouletteMessage, writer);
    }

    void ReceiveRoulette(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || reader.Length - reader.Position != 6) return;
        reader.ReadValueSafe(out byte table);
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out int arg);
        if (table >= roulette.Count || roulette[table] == null || kind > RouletteRound.SpinStarted) return;
        // Guests may only ask; only the host may announce.
        if (multiplayer.IsHost != (kind == RouletteRound.RequestCountdown)) return;
        roulette[table].Receive(kind, arg);
    }

    void SendChip(RouletteBettingManager betting, BettingSpot spot, int value, bool add)
    {
        if (!Connected) return;
        int table = roulette.FindIndex(round => round != null && round.Betting == betting);
        int index = System.Array.IndexOf(betting.Spots, spot);
        if (table < 0 || index < 0) return;
        using var writer = new FastBufferWriter(16, Allocator.Temp);
        WriteChip(writer, table, index, add, multiplayer.LocalId);
        if (multiplayer.IsHost) multiplayer.SendToGuests(ChipMessage, writer);
        else multiplayer.SendToHost(ChipMessage, writer);
    }

    static void WriteChip(FastBufferWriter writer, int table, int spot, bool add, ulong player)
    {
        writer.WriteValueSafe((byte)table);
        writer.WriteValueSafe((byte)spot);
        writer.WriteValueSafe(add);
        writer.WriteValueSafe(player);
    }

    void ReceiveChip(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || reader.Length - reader.Position != 11) return;
        reader.ReadValueSafe(out byte table);
        reader.ReadValueSafe(out byte spot);
        reader.ReadValueSafe(out bool add);
        reader.ReadValueSafe(out ulong player);
        if (multiplayer.IsHost) player = sender; // guests only speak for themselves
        if (player == multiplayer.LocalId || table >= roulette.Count || roulette[table] == null) return;
        var betting = roulette[table].Betting;
        if (betting == null || spot >= betting.Spots.Length) return;
        betting.ShowRemoteBet(player, betting.Spots[spot], add, CasinoMultiplayer.ColorFor(player));

        if (!multiplayer.IsHost) return;
        using var writer = new FastBufferWriter(16, Allocator.Temp);
        WriteChip(writer, table, spot, add, player);
        multiplayer.SendToGuests(ChipMessage, writer, sender);
    }

    // ----------------------------------------------------------- blackjack

    void SendBlackjack(BlackjackTable table, BlackjackEvent what, ulong player, int arg)
    {
        if (!Connected) return;
        int index = blackjack.IndexOf(table);
        if (index < 0) return;
        using var writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        writer.WriteValueSafe((byte)what);
        writer.WriteValueSafe(player);
        writer.WriteValueSafe(arg);
        if (multiplayer.IsHost) multiplayer.SendToGuests(BlackjackMessage, writer);
        else multiplayer.SendToHost(BlackjackMessage, writer);
    }

    void ReceiveBlackjack(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || reader.Length - reader.Position != 14) return;
        reader.ReadValueSafe(out byte index);
        reader.ReadValueSafe(out byte what);
        reader.ReadValueSafe(out ulong player);
        reader.ReadValueSafe(out int arg);
        if (index >= blackjack.Count || blackjack[index] == null || what > (byte)BlackjackEvent.Double) return;
        // The host decides whether a guest's press counts; guests play what the host approved.
        if (multiplayer.IsHost) blackjack[index].Request((BlackjackEvent)what, sender);
        else blackjack[index].Enqueue((BlackjackEvent)what, player, arg);
    }

    // The host only listens to its guests, a guest only to the host.
    bool FromPeer(ulong sender)
    {
        if (multiplayer == null || !multiplayer.InSession) return false;
        return multiplayer.IsHost ? multiplayer.IsGuestId(sender) : sender == NetworkManager.ServerClientId;
    }
}
