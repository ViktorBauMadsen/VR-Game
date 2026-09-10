using System;
using UnityEngine;

// Tracks the player's casino balance. Access from anywhere via PlayerBalance.Instance.
// Casino game scripts call TrySpend to place a bet (fails if funds are insufficient)
// and AddBalance to pay out winnings. Subscribe to BalanceChanged to react to changes
// (the HUD display does this to keep the on-screen number in sync).
public class PlayerBalance : MonoBehaviour
{
    public static PlayerBalance Instance { get; private set; }

    [SerializeField] int startingBalance = 1000;

    public int Balance { get; private set; }
    public event Action<int> BalanceChanged;

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
    }

    // Returns false (and leaves the balance unchanged) if the player can't afford it.
    public bool TrySpend(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Balance < amount) return false;
        Balance -= amount;
        BalanceChanged?.Invoke(Balance);
        return true;
    }
}
