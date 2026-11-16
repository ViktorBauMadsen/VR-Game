using System;
using UnityEngine;

// Tracks the player's casino balance. Access from anywhere via PlayerBalance.Instance.
// Casino game scripts call TrySpend to place a bet (fails if funds are insufficient)
// and Payout to pay out a finished bet (AddBalance for plain refunds). Subscribe to
// BalanceChanged to react to the new total (the HUD display does this), or
// BalanceDelta to react to the amount that just changed (the floating +/- feedback
// text does this).
public class PlayerBalance : MonoBehaviour
{
    public static PlayerBalance Instance { get; private set; }

    // Lets a powerup boost winnings: gets the profit of a winning bet (payout
    // minus stake) and returns the profit to actually pay. Never called for
    // refunds or pushes, so picking a chip back up can't be farmed.
    public static Func<int, int> WinningsModifier;

    [SerializeField] int startingBalance = 1000;

    public int Balance { get; private set; }
    public int StartingBalance => startingBalance;
    public event Action<int> BalanceChanged;
    public event Action<int> BalanceDelta;
    // Something was refused because the player can't afford it (argument =
    // the amount needed). The HUD shows "Not enough balance" on this.
    public event Action<int> InsufficientFunds;

    public bool CanAfford(int amount) => Balance >= amount;

    public void ReportInsufficientFunds(int amountNeeded) => InsufficientFunds?.Invoke(amountNeeded);

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Balance = startingBalance;
    }

    public void AddBalance(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Balance += amount;
        BalanceChanged?.Invoke(Balance);
        BalanceDelta?.Invoke(amount);
    }

    // Pays out a resolved bet. payout includes the stake (same convention all
    // the games use); stake is what was spent to place it. Returns what was
    // actually paid, which is more than payout when a powerup boosts the win.
    public int Payout(int payout, int stake)
    {
        if (payout <= 0) return 0;
        int profit = payout - stake;
        if (profit > 0 && WinningsModifier != null)
            payout = stake + Mathf.Max(profit, WinningsModifier(profit));
        AddBalance(payout);
        return payout;
    }

    // Returns false (and leaves the balance unchanged) if the player can't afford it.
    public bool TrySpend(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Balance < amount)
        {
            ReportInsufficientFunds(amount);
            return false;
        }
        Balance -= amount;
        BalanceChanged?.Invoke(Balance);
        BalanceDelta?.Invoke(-amount);
        return true;
    }

    // Takes up to amount, never going below zero (the mafia's cut). Returns what was taken.
    public int ForceSpend(int amount)
    {
        int taken = Mathf.Clamp(amount, 0, Balance);
        if (taken == 0) return 0;
        Balance -= taken;
        BalanceChanged?.Invoke(Balance);
        BalanceDelta?.Invoke(-taken);
        return taken;
    }

    // Hard reset (a new run starts, or the player died). No +/- popup.
    public void SetBalance(int amount)
    {
        Balance = Mathf.Max(0, amount);
        BalanceChanged?.Invoke(Balance);
    }
}
