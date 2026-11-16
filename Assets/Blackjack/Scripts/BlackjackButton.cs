using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public enum BlackjackAction { BetDown, BetUp, Deal, Hit, Stand, Double }

// One of the push buttons on the blackjack table. Same "XR Simple
// Interactable + cap that travels down" setup as RouletteSpinButton.
[RequireComponent(typeof(XRSimpleInteractable))]
public class BlackjackButton : MonoBehaviour
{
    [SerializeField] BlackjackTable table;
    [SerializeField] BlackjackAction action;
    [SerializeField] Transform cap;
    [SerializeField] float pressDepth = 0.01f;
    [SerializeField] float pressSpeed = 0.15f;

    XRSimpleInteractable _interactable;
    Vector3 _restLocalPos;
    bool _pressed;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        if (cap != null) _restLocalPos = cap.localPosition;
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
        if (table != null) table.Press(action, args.interactorObject);
    }

    void OnSelectExited(SelectExitEventArgs args) => _pressed = false;

    void Update()
    {
        if (cap == null) return;
        Vector3 target = _restLocalPos + (_pressed ? Vector3.down * pressDepth : Vector3.zero);
        cap.localPosition = Vector3.MoveTowards(cap.localPosition, target, pressSpeed * Time.deltaTime);
    }
}
