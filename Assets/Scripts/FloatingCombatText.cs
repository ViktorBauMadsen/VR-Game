using TMPro;
using UnityEngine;

// Attach to a small TextMeshProUGUI prefab. Call Show(message, color) after
// instantiating it; it floats upward and fades out over Duration, then
// destroys itself. Used for the "+$200" / "-$10" balance feedback popups.
[RequireComponent(typeof(TextMeshProUGUI))]
[RequireComponent(typeof(RectTransform))]
public class FloatingCombatText : MonoBehaviour
{
    [SerializeField] float floatDistance = 40f; // in the RectTransform's local units
    [SerializeField] float duration = 1.2f;

    TextMeshProUGUI _text;
    RectTransform _rect;
    Vector2 _startPos;
    float _elapsed;

    void Awake()
    {
        _text = GetComponent<TextMeshProUGUI>();
        _rect = GetComponent<RectTransform>();
        _startPos = _rect.anchoredPosition;
    }

    public void Show(string message, Color color)
    {
        _text.text = message;
        _text.color = color;
        _elapsed = 0f;
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / duration);

        _rect.anchoredPosition = _startPos + Vector2.up * (floatDistance * t);

        Color c = _text.color;
        c.a = 1f - t;
        _text.color = c;

        if (_elapsed >= duration)
            Destroy(gameObject);
    }
}
