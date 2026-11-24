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
    const string RouletteMessage = "Casino.Ro.v1", BlackjackMessage = "Casino.Bj.v1";
    const string BeerPongMessage = "Casino.Pong.v1", DartsMessage = "Casino.Darts.v1";
    readonly List<BeerPongTable> beerPong = new List<BeerPongTable>();
    readonly List<Dartboard> darts = new List<Dartboard>();
    readonly List<SlotMachineLever> levers = new List<SlotMachineLever>();

    CasinoMultiplayer multiplayer;
    readonly List<RouletteRound> roulette = new List<RouletteRound>();
    readonly List<BlackjackTable> blackjack = new List<BlackjackTable>();
    readonly List<SlotMachine> slots = new List<SlotMachine>();
    const string SlotMessage = "Casino.Slot.v1";
    readonly List<VideoSlotMachine> videoSlots = new List<VideoSlotMachine>();
    const string VideoSlotMessage = "Casino.VSlot.v1";

    bool Connected => multiplayer != null && multiplayer.InSession;
    bool Authority => multiplayer == null || !multiplayer.IsGuest;

    void OnEnable()
    {
        RouletteRound.Shared = BlackjackTable.Shared = () => Connected && multiplayer.PlayerCount > 1;
        RouletteRound.IsAuthority = BlackjackTable.IsAuthority = () => Authority;
        BlackjackTable.LocalId = () => multiplayer != null ? multiplayer.LocalId : 0UL;
        RouletteRound.Send = SendRoulette;
        BlackjackTable.Send = SendBlackjack;
        SlotMachine.LocalSpinStarted += SendSlot;
        VideoSlotMachine.LocalSpinStarted += SendVideoSlot;
        BeerPongTable.LocalCupHit += SendCup;
        Dartboard.LocalEvent += SendDarts;
    }

    void OnDisable()
    {
        RouletteRound.Shared = BlackjackTable.Shared = () => false;
        RouletteRound.IsAuthority = BlackjackTable.IsAuthority = () => true;
        BlackjackTable.LocalId = () => 0UL;
        RouletteRound.Send = null;
        BlackjackTable.Send = null;
        SlotMachine.LocalSpinStarted -= SendSlot;
        VideoSlotMachine.LocalSpinStarted -= SendVideoSlot;
        BeerPongTable.LocalCupHit -= SendCup;
        Dartboard.LocalEvent -= SendDarts;
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
        multiplayer.Register(BeerPongMessage, ReceiveCup);
        multiplayer.Register(DartsMessage, ReceiveDarts);
        multiplayer.Register(BlackjackMessage, ReceiveBlackjack);
        multiplayer.Register(SlotMessage, ReceiveSlot);
        multiplayer.Register(VideoSlotMessage, ReceiveVideoSlot);
    }

    // Video slots play every spin (bonus features included) from a seed, so
    // the seed and the bet are all the others need to play the same thing.
    void SendVideoSlot(VideoSlotMachine machine, int seed, int bet)
    {
        if (!Connected) return;
        int index = videoSlots.IndexOf(machine);
        if (index < 0) return;
        using var writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        writer.WriteValueSafe(seed);
        writer.WriteValueSafe(bet);
        if (multiplayer.IsHost) multiplayer.SendToGuests(VideoSlotMessage, writer);
        else multiplayer.SendToHost(VideoSlotMessage, writer);
    }

    void ReceiveVideoSlot(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || reader.Length - reader.Position != 9) return;
        reader.ReadValueSafe(out byte index);
        reader.ReadValueSafe(out int seed);
        reader.ReadValueSafe(out int bet);
        if (index >= videoSlots.Count || videoSlots[index] == null) return;
        videoSlots[index].PlayRemoteSpin(seed, bet);
        PullLever(videoSlots[index]);

        if (!multiplayer.IsHost) return;
        using var writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe(index);
        writer.WriteValueSafe(seed);
        writer.WriteValueSafe(bet);
        multiplayer.SendToGuests(VideoSlotMessage, writer, sender);
    }

    // --------------------------------------------------------------- slots
    // Whoever pulls a lever plays for their own money; everyone else just
    // sees (and hears) that machine spin and land on the same result.

    void SendSlot(SlotMachine machine, int tier)
    {
        if (!Connected) return;
        int index = slots.IndexOf(machine);
        if (index < 0) return;
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        writer.WriteValueSafe((byte)(tier + 1)); // 0 = no win
        if (multiplayer.IsHost) multiplayer.SendToGuests(SlotMessage, writer);
        else multiplayer.SendToHost(SlotMessage, writer);
    }

    void ReceiveSlot(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || reader.Length - reader.Position != 2) return;
        reader.ReadValueSafe(out byte index);
        reader.ReadValueSafe(out byte tier);
        if (index >= slots.Count || slots[index] == null) return;
        slots[index].PlayRemoteSpin(tier - 1);
        PullLever(slots[index]);

        if (!multiplayer.IsHost) return;
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe(index);
        writer.WriteValueSafe(tier);
        multiplayer.SendToGuests(SlotMessage, writer, sender);
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
        slots.Clear();
        slots.AddRange(FindObjectsByType<SlotMachine>());
        slots.Sort((a, b) => Compare(a.transform, b.transform));
        videoSlots.Clear();
        videoSlots.AddRange(FindObjectsByType<VideoSlotMachine>());
        videoSlots.Sort((a, b) => Compare(a.transform, b.transform));
        beerPong.Clear();
        beerPong.AddRange(FindObjectsByType<BeerPongTable>());
        beerPong.Sort((a, b) => Compare(a.transform, b.transform));
        darts.Clear();
        darts.AddRange(FindObjectsByType<Dartboard>());
        darts.Sort((a, b) => Compare(a.transform, b.transform));
        levers.Clear();
        levers.AddRange(FindObjectsByType<SlotMachineLever>());
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

    // Other players' chips (in their hands and on the table) are shown by
    // CasinoProps as real chip models, so there is no separate chip message.

    // ----------------------------------------------------------- beer pong
    // The ball itself is streamed by CasinoProps; this carries which cup it
    // sank, so every table loses the same cup and keeps the same score.

    void SendCup(BeerPongTable table, int cup)
    {
        if (!Connected || cup < 0) return;
        int index = beerPong.IndexOf(table);
        if (index < 0) return;
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        writer.WriteValueSafe((byte)cup);
        if (multiplayer.IsHost) multiplayer.SendToGuests(BeerPongMessage, writer);
        else multiplayer.SendToHost(BeerPongMessage, writer);
    }

    void ReceiveCup(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || reader.Length - reader.Position != 2) return;
        reader.ReadValueSafe(out byte index);
        reader.ReadValueSafe(out byte cup);
        if (index >= beerPong.Count || beerPong[index] == null) return;
        beerPong[index].RemoteCupHit(cup);

        if (!multiplayer.IsHost) return;
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe(index);
        writer.WriteValueSafe(cup);
        multiplayer.SendToGuests(BeerPongMessage, writer, sender);
    }

    // --------------------------------------------------------------- darts
    // Where a dart landed (and the scoreboard buttons), so everyone's board
    // counts the same game.

    void SendDarts(Dartboard board, byte what, int value, string label)
    {
        if (!Connected) return;
        int index = darts.IndexOf(board);
        if (index < 0) return;
        WriteDarts(index, what, value, label, ulong.MaxValue);
    }

    void WriteDarts(int index, byte what, int value, string label, ulong except)
    {
        var text = new FixedString32Bytes();
        text.CopyFromTruncated(label ?? "");
        using var writer = new FastBufferWriter(48, Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        writer.WriteValueSafe(what);
        writer.WriteValueSafe(value);
        writer.WriteValueSafe(text);
        if (multiplayer.IsHost) multiplayer.SendToGuests(DartsMessage, writer, except);
        else multiplayer.SendToHost(DartsMessage, writer);
    }

    void ReceiveDarts(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || reader.Length - reader.Position < 8) return;
        reader.ReadValueSafe(out byte index);
        reader.ReadValueSafe(out byte what);
        reader.ReadValueSafe(out int value);
        reader.ReadValueSafe(out FixedString32Bytes text);
        if (index >= darts.Count || darts[index] == null || what > Dartboard.EventReturn) return;
        string label = text.ToString();
        darts[index].RemoteEvent(what, value, label);
        if (multiplayer.IsHost) WriteDarts(index, what, value, label, sender);
    }

    // ----------------------------------------------------------- slot levers

    void PullLever(SlotMachineBase machine)
    {
        foreach (var lever in levers)
            if (lever != null && lever.Machine == machine) lever.PlayRemotePull();
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
