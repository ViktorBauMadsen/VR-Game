using System.Collections;
using UnityEngine;

// Handles the betting/payout logic for a slot machine. Call Spin() to play
// one round: the bet is deducted immediately, then after payoutDelay
// (simulating the reels spinning, even without spin visuals yet) a weighted
// random outcome is rolled against winTiers and paid out if one hits.
public class SlotMachine : MonoBehaviour
{
    [SerializeField] int betAmount = 10;
    [SerializeField] float payoutDelay = 0.5f;
    [SerializeField] float loseWeight = 60f; // relative chance of no win at all - 40% chance of winning something
    [SerializeField] SlotMachineReels reels;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip spinStartClip; // plays right as the reels start spinning
    [SerializeField] PayoutTier[] winTiers =
    {
        new PayoutTier { label = "Cherry", weight = 15f, payoutMultiplier = 0.5f },
        new PayoutTier { label = "Lemon", weight = 10f, payoutMultiplier = 1f },
        new PayoutTier { label = "Blackberry", weight = 7f, payoutMultiplier = 2f },
        new PayoutTier { label = "Watermelon", weight = 4f, payoutMultiplier = 4f },
        new PayoutTier { label = "Bell", weight = 2f, payoutMultiplier = 8f },
        new PayoutTier { label = "BAR", weight = 1.5f, payoutMultiplier = 15f },
        new PayoutTier { label = "Seven", weight = 0.5f, payoutMultiplier = 50f },
    };

    void Start()
    {
        if (reels == null) return;
        var sprites = new Sprite[winTiers.Length];
        for (int i = 0; i < winTiers.Length; i++) sprites[i] = winTiers[i].symbol;
        reels.SetSymbolPool(sprites);
    }

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

        StartCoroutine(ResolveSpin());
    }

    IEnumerator ResolveSpin()
    {
        // Roll the outcome up front so the reel animation can be timed to
        // settle on the real result exactly when the payout delay ends.
        var tier = RollOutcome();
        if (audioSource != null && spinStartClip != null) audioSource.PlayOneShot(spinStartClip);
        float extraDelay = 0f;
        if (reels != null)
        {
            reels.PlaySpin(tier, payoutDelay);
            extraDelay = reels.ExtraSettleDelay; // account for the left-to-right landing cascade
        }

        yield return new WaitForSeconds(payoutDelay + extraDelay);

        if (tier != null)
        {
            int payout = Mathf.RoundToInt(betAmount * tier.payoutMultiplier);
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
    public float payoutMultiplier = 2f;
    public Sprite symbol; // the symbol image shown on the reel for this tier
}
