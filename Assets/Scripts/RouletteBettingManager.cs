using System;
using System.Collections.Generic;
using UnityEngine;

// Bookkeeping for the chips placed as bets on ONE roulette table: chips don't
// know about each other, so this is what that table's RouletteWheel.OnResult
// actually talks to. Payout already includes the stake back (CasinoChip.Place
// spent it up front via PlayerBalance.TrySpend, same convention SlotMachine
// uses), and every placed chip - winner or loser - is destroyed once a result
// comes in (chips are an infinite, on-demand resource from ChipDispenser, so
// there's nothing to return them to - PlayerBalance is the only ledger).
//
// There is one of these per table. BettingSpot.Manager finds the one that
// belongs to its own table (through the RouletteTable root); Instance is only
// a fallback for a loose, un-combined table in the scene.
//
// Multiplayer: every player's real chips and money stay on their own
// machine. What the other players bet is only SHOWN here, as small tinted
// stand-in chips next to each spot (ShowRemoteBet), so you can see where
// your friends put their money on the shared spin.
public class RouletteBettingManager : MonoBehaviour
{
    public static RouletteBettingManager Instance { get; private set; }

    // The local player put a chip down (true) or took it back (false).
    public static event Action<RouletteBettingManager, BettingSpot, int, bool> LocalBetChanged;

    [SerializeField] RouletteWheel wheel;

    readonly HashSet<CasinoChip> _placedChips = new HashSet<CasinoChip>();
    readonly List<CasinoChip> _resolveBuffer = new List<CasinoChip>();
    readonly Dictionary<(ulong, BettingSpot), List<GameObject>> _remote = new Dictionary<(ulong, BettingSpot), List<GameObject>>();
    RouletteWheel _subscribed;
    BettingSpot[] _spots;
    bool _resolving;

    public int PlacedCount => _placedChips.Count;

    public RouletteWheel Wheel
    {
        get => wheel;
        set
        {
            wheel = value;
            if (isActiveAndEnabled) Subscribe();
        }
    }

    // This table's spots in an order that is the same on every machine.
    public BettingSpot[] Spots
    {
        get
        {
            if (_spots != null) return _spots;
            var table = GetComponentInParent<RouletteTable>();
            var found = new List<BettingSpot>(table != null
                ? table.GetComponentsInChildren<BettingSpot>(true)
                : FindObjectsByType<BettingSpot>(FindObjectsInactive.Include));
            found.RemoveAll(spot => spot.Manager != this);
            found.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.name, b.name);
                if (byName != 0) return byName;
                Vector3 pa = a.transform.localPosition, pb = b.transform.localPosition;
                int byX = pa.x.CompareTo(pb.x);
                return byX != 0 ? byX : pa.z.CompareTo(pb.z);
            });
            return _spots = found.ToArray();
        }
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        if (wheel == null)
        {
            var table = GetComponentInParent<RouletteTable>();
            if (table != null) wheel = table.GetComponentInChildren<RouletteWheel>(true);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable() => Subscribe();

    void OnDisable()
    {
        if (_subscribed != null) _subscribed.OnResult -= HandleResult;
        _subscribed = null;
    }

    void Subscribe()
    {
        if (_subscribed == wheel) return;
        if (_subscribed != null) _subscribed.OnResult -= HandleResult;
        _subscribed = wheel;
        if (_subscribed != null) _subscribed.OnResult += HandleResult;
    }

    public void RegisterPlacedChip(CasinoChip chip)
    {
        if (_placedChips.Add(chip) && chip.CurrentSpot != null)
            LocalBetChanged?.Invoke(this, chip.CurrentSpot, chip.Value, true);
    }

    public void UnregisterPlacedChip(CasinoChip chip)
    {
        // When a spin resolves everyone clears the table themselves.
        if (_placedChips.Remove(chip) && !_resolving && chip.CurrentSpot != null)
            LocalBetChanged?.Invoke(this, chip.CurrentSpot, chip.Value, false);
    }

    void HandleResult(int number)
    {
        // CasinoChip.ResolveAndDestroy() below calls back into
        // UnregisterPlacedChip on this same set, so resolve from a
        // snapshot rather than the live set.
        _resolveBuffer.Clear();
        _resolveBuffer.AddRange(_placedChips);

        _resolving = true;
        foreach (var chip in _resolveBuffer)
        {
            if (chip == null) continue;

            if (chip.CurrentSpot != null && chip.CurrentSpot.Wins(number) && PlayerBalance.Instance != null)
                PlayerBalance.Instance.Payout(chip.Value * chip.CurrentSpot.PayoutMultiplier, chip.Value);

            chip.ResolveAndDestroy();
        }
        _resolving = false;
        _placedChips.Clear();
        ClearRemoteBets();
    }

    // ------------------------------------------------ other players' bets

    public void ShowRemoteBet(ulong player, BettingSpot spot, bool add, Color colour)
    {
        if (spot == null) return;
        var key = (player, spot);
        if (!_remote.TryGetValue(key, out var stack)) _remote[key] = stack = new List<GameObject>();

        if (!add)
        {
            if (stack.Count == 0) return;
            Destroy(stack[stack.Count - 1]);
            stack.RemoveAt(stack.Count - 1);
            return;
        }
        if (stack.Count >= 20) return;

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Other Player's Chip";
        var collider = marker.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);

        // Beside the spot's centre (a different side per player), stacked up.
        Bounds bounds = spot.GetComponent<Collider>().bounds;
        float angle = (player % 6) * 60f * Mathf.Deg2Rad;
        float reach = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.55f;
        const float height = 0.006f;
        marker.transform.SetParent(transform, true);
        marker.transform.position = new Vector3(bounds.center.x + Mathf.Cos(angle) * reach,
            bounds.max.y + height * (0.5f + stack.Count), bounds.center.z + Mathf.Sin(angle) * reach);
        marker.transform.rotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one;
        Vector3 parentScale = transform.lossyScale;
        marker.transform.localScale = new Vector3(0.028f / Mathf.Max(0.0001f, parentScale.x),
            height * 0.5f / Mathf.Max(0.0001f, parentScale.y), 0.028f / Mathf.Max(0.0001f, parentScale.z));
        marker.GetComponent<Renderer>().material.color = colour;
        stack.Add(marker);
    }

    public void ClearRemoteBets(ulong? onlyPlayer = null)
    {
        var done = new List<(ulong, BettingSpot)>();
        foreach (var pair in _remote)
        {
            if (onlyPlayer.HasValue && pair.Key.Item1 != onlyPlayer.Value) continue;
            foreach (var marker in pair.Value)
                if (marker != null) Destroy(marker);
            done.Add(pair.Key);
        }
        foreach (var key in done) _remote.Remove(key);
    }
}
