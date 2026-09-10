using UnityEngine;

// Handles the betting/payout logic for a slot machine. Call Spin() to play
// one round (deducts betAmount from PlayerBalance, then rolls a weighted
// random outcome against winTiers and pays out if one hits).
public class SlotMachine : MonoBehaviour
{
    [SerializeField] int betAmount = 10;
    [SerializeField] float loseWeight = 79f; // relative chance of no win at all
    [SerializeField] PayoutTier[] winTiers =
    {
        new PayoutTier { label = "Small Win", weight = 15f, payoutMultiplier = 3 },
        new PayoutTier { label = "Big Win", weight = 5f, payoutMultiplier = 8 },
        new PayoutTier { label = "Jackpot", weight = 1f, payoutMultiplier = 20 },
    };

    public void Spin()
    {
        if (PlayerBalance.Instance == null)
        {
            Debug.LogWarning("SlotMachine: no PlayerBalance found in the scene.");
            return;
        }

        if (!PlayerBalance.Instance.TrySpend(betAmount))
        {
            Debug.Log("SlotMachine: not enough balance to spin.");
            return;
        }

        var tier = RollOutcome();
        if (tier != null)
        {
            int payout = betAmount * tier.payoutMultiplier;
            PlayerBalance.Instance.AddBalance(payout);
            Debug.Log($"SlotMachine: {tier.label}! Won {payout}.");
        }
        else
        {
            Debug.Log("SlotMachine: no win this spin.");
        }
    }

    PayoutTier RollOutcome()
    {
        float totalWeight = loseWeight;
        foreach (var t in winTiers) totalWeight += t.weight;

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        foreach (var t in winTiers)
        {
            cumulative += t.weight;
            if (roll < cumulative) return t;
        }
        return null; // landed in the "lose" portion of the range
    }
}

[System.Serializable]
public class PayoutTier
{
    public string label = "Win";
    public float weight = 10f;
    public int payoutMultiplier = 2;
}
