using UnityEngine;

// Root of one complete roulette table (wheel + betting layout + croupier +
// furniture), so the whole thing moves, duplicates and saves as a prefab as a
// single object - like "Blackjack Table". Built by
// Tools > Roulette > Combine Scene Roulette Into One Table.
//
// Its only job at runtime is to tie this table's betting layout to this
// table's wheel, so several tables in one casino never pay out each other's
// chips.
public class RouletteTable : MonoBehaviour
{
    [SerializeField] RouletteWheel wheel;
    [SerializeField] RouletteBettingManager betting;

    public RouletteWheel Wheel => wheel;

    public RouletteBettingManager Betting
    {
        get
        {
            Resolve();
            return betting;
        }
    }

    void Awake() => Resolve();

    void Resolve()
    {
        if (wheel == null) wheel = GetComponentInChildren<RouletteWheel>(true);
        if (betting == null) betting = GetComponentInChildren<RouletteBettingManager>(true);
        if (betting == null) betting = gameObject.AddComponent<RouletteBettingManager>();
        if (betting.Wheel == null && wheel != null) betting.Wheel = wheel;
    }
}
