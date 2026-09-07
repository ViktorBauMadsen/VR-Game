using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Attach to any pickupable object alongside its XR Grab Interactable.
// Shows a floating "Pick up (ItemName)" 3D text label above the item.
// The label is parented to the item so it always follows the item's
// position automatically (handled by Unity's transform hierarchy itself,
// not per-frame script logic), with its local offset and scale compensated
// so it sits at a fixed real-world height/size regardless of the item's own
// scale. Rotation is corrected every frame so it stays upright and facing
// the camera instead of tumbling with the item. Hides while the item is held.
[RequireComponent(typeof(XRBaseInteractable))]
public class PickupLabel : MonoBehaviour
{
    [SerializeField] string itemName;
    [SerializeField] float heightAboveObject = 0.16f;
    [SerializeField] bool hideWhileHeld = true;
    [SerializeField] Color textColor = Color.white;
    [SerializeField] float fontSize = 0.6f;

    XRBaseInteractable _interactable;
    Transform _label;
    Camera _cam;

    void Awake()
    {
        _interactable = GetComponent<XRBaseInteractable>();
        _cam = Camera.main;

        var bounds = GetComponentInChildren<Renderer>()?.bounds;
        float topOffset = bounds.HasValue ? bounds.Value.extents.y : 0.05f;

        BuildLabel(topOffset);
    }

    void OnEnable()
    {
        if (!hideWhileHeld) return;
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(OnSelectEntered);
        _interactable.selectExited.RemoveListener(OnSelectExited);
    }

    // Using Update rather than LateUpdate here as a deliberate test: LateUpdate
    // has not been taking effect for this component in this project for reasons
    // still unclear, so this checks whether Update behaves differently.
    void Update()
    {
        // Position is handled automatically by parenting (see BuildLabel).
        // Only rotation needs correcting each frame, so the label stays
        // upright and facing the camera instead of tumbling with the item.
        if (_label == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        Vector3 toCamera = _label.position - _cam.transform.position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude > 0.0001f)
            _label.rotation = Quaternion.LookRotation(toCamera);
    }

    void BuildLabel(float topOffset)
    {
        string content = "Pick up (" + (string.IsNullOrEmpty(itemName) ? gameObject.name : itemName) + ")";

        var labelObj = new GameObject("Pickup Label");
        _label = labelObj.transform;
        _label.SetParent(transform, false);

        // Compensate for the item's own scale so the label is always the
        // same real-world size and sits at the same real-world height above
        // the item, regardless of how big or small the item itself is.
        Vector3 parentScale = transform.lossyScale;
        float safeX = Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f);
        float safeY = Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f);
        float safeZ = Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f);

        _label.localPosition = new Vector3(0f, (topOffset + heightAboveObject) / safeY, 0f);
        _label.localScale = new Vector3(1f / safeX, 1f / safeY, 1f / safeZ);

        var text = labelObj.AddComponent<TextMeshPro>();
        text.text = content;
        text.color = textColor;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(0.815f, 0.15f);
        text.enableAutoSizing = false; // otherwise TMP recalculates its own "best fit" size and ignores fontSize below
        text.fontSize = fontSize;
        text.ForceMeshUpdate(); // TMP set up via script right after creation doesn't always apply values until forced
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (_label != null) _label.gameObject.SetActive(false);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (_label != null) _label.gameObject.SetActive(true);
    }
}
