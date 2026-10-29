using UnityEngine;

// Attach near the balance display in the PlayerHUD canvas. Spawns a
// FloatingCombatText popup every time PlayerBalance's balance changes -
// green "+$200" for gains, red "-$10" for losses.
public class PlayerHUDFeedback : MonoBehaviour
{
    [SerializeField] FloatingCombatText feedbackPrefab;
    [SerializeField] RectTransform spawnPoint; // defaults to this object's own RectTransform
    [SerializeField] Color winColor = Color.green;
    [SerializeField] Color loseColor = Color.red;
    [SerializeField] string notEnoughBalanceMessage = "Not enough balance";
    [Tooltip("Repeated refusals within this many seconds show only one message.")]
    [SerializeField] float messageCooldown = 0.75f;

    float _lastMessageTime = -10f;

    void Awake()
    {
        if (spawnPoint == null) spawnPoint = GetComponent<RectTransform>();
    }

    void Start()
    {
        if (PlayerBalance.Instance == null)
        {
            Debug.LogWarning("PlayerHUDFeedback: no PlayerBalance found in the scene.");
            return;
        }
        PlayerBalance.Instance.BalanceDelta += OnBalanceDelta;
        PlayerBalance.Instance.InsufficientFunds += OnInsufficientFunds;
    }

    void OnDestroy()
    {
        if (PlayerBalance.Instance != null)
        {
            PlayerBalance.Instance.BalanceDelta -= OnBalanceDelta;
            PlayerBalance.Instance.InsufficientFunds -= OnInsufficientFunds;
        }
    }

    void OnInsufficientFunds(int amountNeeded)
    {
        if (feedbackPrefab == null || spawnPoint == null) return;
        if (Time.unscaledTime - _lastMessageTime < messageCooldown) return;
        _lastMessageTime = Time.unscaledTime;

        var instance = Instantiate(feedbackPrefab, spawnPoint);
        // The popup prefab is sized for short "+$200" text; keep this on one line.
        var tmp = instance.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp != null) tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        instance.Show(notEnoughBalanceMessage, loseColor);
    }

    void OnBalanceDelta(int delta)
    {
        if (delta == 0 || feedbackPrefab == null || spawnPoint == null) return;

        var instance = Instantiate(feedbackPrefab, spawnPoint);
        string sign = delta > 0 ? "+" : "-";
        instance.Show($"{sign}${Mathf.Abs(delta)}", delta > 0 ? winColor : loseColor);
    }
}
