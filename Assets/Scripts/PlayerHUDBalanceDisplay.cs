using TMPro;
using UnityEngine;

// Attach to the balance TextMeshProUGUI element in the PlayerHUD canvas.
// Keeps the displayed text in sync with PlayerBalance.Instance.
[RequireComponent(typeof(TextMeshProUGUI))]
public class PlayerHUDBalanceDisplay : MonoBehaviour
{
    TextMeshProUGUI _text;

    void Awake()
    {
        _text = GetComponent<TextMeshProUGUI>();
    }

    void Start()
    {
        if (PlayerBalance.Instance == null)
        {
            Debug.LogWarning("PlayerHUDBalanceDisplay: no PlayerBalance found in the scene.");
            return;
        }
        PlayerBalance.Instance.BalanceChanged += UpdateText;
        UpdateText(PlayerBalance.Instance.Balance);
    }

    void OnDestroy()
    {
        if (PlayerBalance.Instance != null)
            PlayerBalance.Instance.BalanceChanged -= UpdateText;
    }

    void UpdateText(int balance)
    {
        _text.text = "$" + balance.ToString("N0");
    }
}
