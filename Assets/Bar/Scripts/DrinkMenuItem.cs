using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// One card on the DrinkMenu. Select it (grab while pointing at it or
// touching it) to order that drink from the bartender; it lights up while
// you point at it.
[RequireComponent(typeof(XRSimpleInteractable))]
public class DrinkMenuItem : MonoBehaviour
{
    [SerializeField] DrinkMenu menu;
    [SerializeField] Bartender bartender;
    [SerializeField] GameObject drinkPrefab;
    [SerializeField] string itemName = "margarita";
    [SerializeField] int price = 60;
    [Tooltip("The little model of the drink on the card (it turns slowly).")]
    [SerializeField] Transform preview;
    [Tooltip("Card frame, brightened while pointed at.")]
    [SerializeField] Renderer frame;
    [SerializeField] Color hoverColour = new Color(1f, 0.85f, 0.4f);

    XRSimpleInteractable _interactable;
    MaterialPropertyBlock _block;
    Vector3 _scale;
    bool _hovered;

    public Transform Preview => preview;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        _scale = transform.localScale;
    }

    void OnEnable()
    {
        _interactable.selectEntered.AddListener(OnSelected);
        _interactable.hoverEntered.AddListener(OnHoverEntered);
        _interactable.hoverExited.AddListener(OnHoverExited);
    }

    void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(OnSelected);
        _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        _interactable.hoverExited.RemoveListener(OnHoverExited);
        SetHover(false);
    }

    void OnHoverEntered(HoverEnterEventArgs args) => SetHover(true);
    void OnHoverExited(HoverExitEventArgs args) => SetHover(_interactable.isHovered);

    void SetHover(bool on)
    {
        _hovered = on;
        transform.localScale = _scale * (on ? 1.06f : 1f);
        if (frame == null) return;
        _block ??= new MaterialPropertyBlock();
        frame.GetPropertyBlock(_block);
        if (on) _block.SetColor("_BaseColor", hoverColour);
        else _block.Clear();
        frame.SetPropertyBlock(_block);
    }

    void OnSelected(SelectEnterEventArgs args)
    {
        if (menu != null && !menu.IsOpen) return;
        if (bartender == null || drinkPrefab == null) return;
        if (bartender.Busy)
        {
            if (args.interactorObject is XRBaseInputInteractor input) input.SendHapticImpulse(0.3f, 0.08f);
            return;
        }
        bartender.Order(drinkPrefab, itemName, price, Quaternion.identity, args.interactorObject);
        if (menu != null) menu.Close();
    }
}
