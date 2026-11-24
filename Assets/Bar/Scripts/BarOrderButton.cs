using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A push button on the bar counter that orders one item from the Bartender
// (who takes the money and serves it). Same "XR Simple Interactable + cap
// that travels down" setup as RouletteSpinButton.
[RequireComponent(typeof(XRSimpleInteractable))]
public class BarOrderButton : MonoBehaviour
{
    [SerializeField] Bartender bartender;
    [SerializeField] GameObject itemPrefab;
    [SerializeField] string itemName = "cigar";
    [SerializeField] int price = 100;
    [Tooltip("Extra rotation for the served item, relative to the serve point.")]
    [SerializeField] Vector3 serveEuler;
    [Tooltip("If set, the button opens/closes this drinks menu instead of ordering directly.")]
    [SerializeField] DrinkMenu menu;
    [SerializeField] Transform cap;
    [SerializeField] float pressDepth = 0.012f;
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
        if (menu != null)
        {
            menu.Toggle();
            return;
        }
        if (bartender != null)
            bartender.Order(itemPrefab, itemName, price, Quaternion.Euler(serveEuler), args.interactorObject);
    }

    void OnSelectExited(SelectExitEventArgs args) => _pressed = false;

    void Update()
    {
        if (cap == null) return;
        Vector3 target = _restLocalPos + (_pressed ? Vector3.down * pressDepth : Vector3.zero);
        cap.localPosition = Vector3.MoveTowards(cap.localPosition, target, pressSpeed * Time.deltaTime);
    }
}
