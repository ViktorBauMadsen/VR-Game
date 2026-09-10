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
    }

    void OnDestroy()
    {
        if (PlayerBalance.Instance != null)
            PlayerBalance.Instance.BalanceDelta -= OnBalanceDelta;
    }

    void OnBalanceDelta(int delta)
    {
        if (delta == 0 || feedbackPrefab == null || spawnPoint == null) return;

        var instance = Instantiate(feedbackPrefab, spawnPoint);
        string sign = delta > 0 ? "+" : "-";
        instance.Show($"{sign}${Mathf.Abs(delta)}", delta > 0 ? winColor : loseColor);
    }
}
