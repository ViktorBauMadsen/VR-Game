using System.Collections.Generic;
using UnityEngine;

// Central bookkeeping for chips placed as bets: chips don't know about each
// other, so this is what RouletteWheel.OnResult actually talks to. Payout
// already includes the stake back (CasinoChip.Place spent it up front via
// PlayerBalance.TrySpend, same convention SlotMachine uses), and every
// placed chip - winner or loser - is destroyed once a result comes in
// (chips are an infinite, on-demand resource from ChipDispenser, so
// there's nothing to return them to - PlayerBalance is the only ledger).
public class RouletteBettingManager : MonoBehaviour
{
    public static RouletteBettingManager Instance { get; private set; }

    [SerializeField] RouletteWheel wheel;

    readonly HashSet<CasinoChip> _placedChips = new HashSet<CasinoChip>();
    readonly List<CasinoChip> _resolveBuffer = new List<CasinoChip>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        if (wheel != null) wheel.OnResult += HandleResult;
    }

    void OnDisable()
    {
        if (wheel != null) wheel.OnResult -= HandleResult;
    }

    public void RegisterPlacedChip(CasinoChip chip) => _placedChips.Add(chip);
    public void UnregisterPlacedChip(CasinoChip chip) => _placedChips.Remove(chip);

    void HandleResult(int number)
    {
        // CasinoChip.ResolveAndDestroy() below calls back into
        // UnregisterPlacedChip on this same set, so resolve from a
        // snapshot rather than the live set.
        _resolveBuffer.Clear();
        _resolveBuffer.AddRange(_placedChips);

        foreach (var chip in _resolveBuffer)
        {
            if (chip == null) continue;

            if (chip.CurrentSpot != null && chip.CurrentSpot.Wins(number) && PlayerBalance.Instance != null)
                PlayerBalance.Instance.AddBalance(chip.Value * chip.CurrentSpot.PayoutMultiplier);

            chip.ResolveAndDestroy();
        }
    }
}
