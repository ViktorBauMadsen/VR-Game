using System.Collections.Generic;
using UnityEngine;

// Tags one hand-placed marker collider (a straight number, dozen, or
// even-money section) over the roulette betting layout sprite with what it
// actually means, parsed from the collider's name by
// SetupRouletteBettingSpots so nobody has to hand-fill 46 Inspectors.
// RouletteBettingManager reads Wins()/PayoutMultiplier off whichever spot a
// placed chip sits on; CasinoChip reads GetNextPlacementPosition to stack
// chips neatly instead of piling them at one point.
[RequireComponent(typeof(Collider))]
public class BettingSpot : MonoBehaviour
{
    public enum SpotType { StraightUp, Dozen, EvenMoney }
    public enum EvenMoneyKind { Low, High, Even, Odd, Red, Black }

    [SerializeField] SpotType spotType;
    [SerializeField] int number; // StraightUp: 0-36. Dozen: 1/2/3. Unused for EvenMoney.
    [SerializeField] EvenMoneyKind evenMoneyKind; // only read when spotType == EvenMoney

    Collider _collider;
    readonly List<CasinoChip> _chips = new List<CasinoChip>();

    public int ChipCount => _chips.Count;

    public int PayoutMultiplier
    {
        get
        {
            switch (spotType)
            {
                case SpotType.StraightUp: return 36; // 35:1 plus the stake back
                case SpotType.Dozen: return 3;        // 2:1 plus the stake back
                case SpotType.EvenMoney: return 2;    // 1:1 plus the stake back
                default: return 0;
            }
        }
    }

    void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    // 0 only wins the "0" straight-up spot - every Dozen/EvenMoney bet
    // (including Black, which isn't simply "not Red") loses on it.
    public bool Wins(int result)
    {
        switch (spotType)
        {
            case SpotType.StraightUp:
                return result == number;
            case SpotType.Dozen:
                return result != 0 && (result - 1) / 12 + 1 == number;
            case SpotType.EvenMoney:
                if (result == 0) return false;
                switch (evenMoneyKind)
                {
                    case EvenMoneyKind.Low: return result <= 18;
                    case EvenMoneyKind.High: return result >= 19;
                    case EvenMoneyKind.Even: return result % 2 == 0;
                    case EvenMoneyKind.Odd: return result % 2 != 0;
                    case EvenMoneyKind.Red: return RouletteWheel.IsRed(result);
                    case EvenMoneyKind.Black: return !RouletteWheel.IsRed(result);
                    default: return false;
                }
            default:
                return false;
        }
    }

    // Stacks straight up from the collider's own surface so multiple chips
    // on the same spot read as a pile instead of overlapping at one point.
    public Vector3 GetNextPlacementPosition(float chipHeight)
    {
        if (_collider == null) _collider = GetComponent<Collider>();
        Bounds b = _collider.bounds;
        float y = b.max.y + chipHeight * (0.5f + _chips.Count);
        return new Vector3(b.center.x, y, b.center.z);
    }

    public void RegisterChip(CasinoChip chip)
    {
        if (!_chips.Contains(chip)) _chips.Add(chip);
    }

    public void UnregisterChip(CasinoChip chip)
    {
        _chips.Remove(chip);
    }
}
