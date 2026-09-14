using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A simple pushable button for triggering a roulette spin in VR - standing
// in for the betting/lever flow that doesn't exist yet, same role the lever
// plays for the slot machine. Attach alongside an XR Simple Interactable.
[RequireComponent(typeof(XRSimpleInteractable))]
public class RouletteSpinButton : MonoBehaviour
{
    [SerializeField] Transform cap; // the visual part that travels down when pressed; defaults to this object
    [SerializeField] float pressDepth = 0.01f;
    [SerializeField] float pressSpeed = 10f;
    [SerializeField] RouletteWheel wheel;

    XRSimpleInteractable _interactable;
    Vector3 _restLocalPos;
    bool _pressed;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        if (cap == null) cap = transform;
        _restLocalPos = cap.localPosition;
    }

    void OnEnable()
    {
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(OnSelectEntered);
        _interactable.selectExited.RemoveListener(OnSelectExited);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        _pressed = true;
        if (wheel != null) wheel.Spin();
    }

    void OnSelectExited(SelectExitEventArgs args) => _pressed = false;

    void Update()
    {
        Vector3 target = _restLocalPos + (_pressed ? Vector3.down * pressDepth : Vector3.zero);
        cap.localPosition = Vector3.MoveTowards(cap.localPosition, target, pressSpeed * Time.deltaTime);
    }
}
