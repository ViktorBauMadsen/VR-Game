using TMPro;
using UnityEngine;

// Attach anywhere near the wheel. Builds its own floating 3D text label at
// runtime (same approach as PickupLabel) showing the number from the most
// recent spin - purely for sanity-checking the physics result while there's
// no betting/payout UI yet.
public class RouletteResultDisplay : MonoBehaviour
{
    [SerializeField] RouletteWheel wheel;
    [SerializeField] Color textColor = Color.white;
    [SerializeField] float fontSize = 3f;

    TextMeshPro _text;

    void Awake()
    {
        var labelObj = new GameObject("Result Label");
        labelObj.transform.SetParent(transform, false);

        _text = labelObj.AddComponent<TextMeshPro>();
        _text.alignment = TextAlignmentOptions.Center;
        _text.rectTransform.sizeDelta = new Vector2(2f, 0.6f);
        _text.enableAutoSizing = false;
        _text.fontSize = fontSize;
        _text.color = textColor;
        _text.text = "-";
        _text.ForceMeshUpdate();
    }

    void OnEnable()
    {
        if (wheel != null) wheel.OnResult += HandleResult;
    }

    void OnDisable()
    {
        if (wheel != null) wheel.OnResult -= HandleResult;
    }

    void HandleResult(int number)
    {
        _text.text = "Landed: " + number;
    }
}
