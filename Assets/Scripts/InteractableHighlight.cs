using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Attach to any object that has an XR Grab Interactable (or other XRBaseInteractable).
// Tints the object's material when a controller is hovering/pointing at it,
// so the player can tell it's pickupable before they grab it.
[RequireComponent(typeof(Renderer))]
[RequireComponent(typeof(XRBaseInteractable))]
public class InteractableHighlight : MonoBehaviour
{
    [SerializeField] Color highlightColor = new Color(1f, 0.84f, 0.2f); // gold

    Renderer _renderer;
    XRBaseInteractable _interactable;
    MaterialPropertyBlock _propBlock;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _interactable = GetComponent<XRBaseInteractable>();
        _propBlock = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        _interactable.hoverEntered.AddListener(OnHoverEntered);
        _interactable.hoverExited.AddListener(OnHoverExited);
    }

    void OnDisable()
    {
        _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        _interactable.hoverExited.RemoveListener(OnHoverExited);
    }

    void OnHoverEntered(HoverEnterEventArgs args) => SetHighlighted(true);
    void OnHoverExited(HoverExitEventArgs args) => SetHighlighted(false);

    void SetHighlighted(bool highlighted)
    {
        _renderer.GetPropertyBlock(_propBlock);
        if (highlighted)
        {
            _propBlock.SetColor(BaseColorId, highlightColor);
            _propBlock.SetColor(ColorId, highlightColor);
        }
        else
        {
            _propBlock.Clear();
        }
        _renderer.SetPropertyBlock(_propBlock);
    }
}
