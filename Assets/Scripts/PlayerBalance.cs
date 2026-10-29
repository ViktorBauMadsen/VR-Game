using System;
using UnityEngine;

// Tracks the player's casino balance. Access from anywhere via PlayerBalance.Instance.
// Casino game scripts call TrySpend to place a bet (fails if funds are insufficient)
// and AddBalance to pay out winnings. Subscribe to BalanceChanged to react to the new
// total (the HUD display does this), or BalanceDelta to react to the amount that just
// changed (the floating +/- feedback text does this).
public class PlayerBalance : MonoBehaviour
{
    public static PlayerBalance Instance { get; private set; }

    [SerializeField] int startingBalance = 1000;

    public int Balance { get; private set; }
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
}
