using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Attach to any pickupable object alongside its XR Grab Interactable.
// Spawns a small floating dot above the object to signal "this can be picked up".
// The dot hides automatically while the object is being held.
[RequireComponent(typeof(XRBaseInteractable))]
public class PickupIndicator : MonoBehaviour
{
    [SerializeField] Color dotColor = Color.white;
    [SerializeField] float dotSize = 0.04f;
    [SerializeField] float heightAboveObject = 0.08f;
    [SerializeField] bool hideWhileHeld = true;

    XRBaseInteractable _interactable;
    GameObject _dot;

    void Awake()
    {
        _interactable = GetComponent<XRBaseInteractable>();
        CreateDot();
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

    void CreateDot()
    {
        _dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _dot.name = "Pickup Indicator";
        Destroy(_dot.GetComponent<Collider>());

        var bounds = GetComponentInChildren<Renderer>()?.bounds;
        float topOffset = bounds.HasValue ? bounds.Value.extents.y : 0.05f;

        _dot.transform.SetParent(transform, false);
        _dot.transform.localScale = Vector3.one * dotSize;
        _dot.transform.position = transform.position + Vector3.up * (topOffset + heightAboveObject);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = dotColor;
        _dot.GetComponent<Renderer>().material = mat;
    }

    void OnSelectEntered(SelectEnterEventArgs args) => _dot.SetActive(false);
    void OnSelectExited(SelectExitEventArgs args) => _dot.SetActive(true);
}
