using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Shared base for things bought at the bar (Cigar, BarDrink): a grabbable
// that knows when it is being held up to the player's mouth. The "mouth" is
// a point just below and in front of the headset camera. Once an item is
// used up it shrinks away a few seconds after being put down, so empties
// don't pile up on the bar.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public abstract class BarConsumable : MonoBehaviour
{
    [Tooltip("The part that has to reach the mouth (cigar's mouth end / glass rim).")]
    [SerializeField] protected Transform mouthPoint;
    [SerializeField] float mouthDistance = 0.15f;
    [SerializeField] float despawnDelay = 8f;

    XRGrabInteractable _grab;
    Transform _head;
    bool _spent;
    float _spentTimer;

    protected bool IsHeld => _grab.isSelected;

    protected Transform Head
    {
        get
        {
            if (_head == null && Camera.main != null) _head = Camera.main.transform;
            return _head;
        }
    }

    protected Vector3 MouthPosition => Head.position - Head.up * 0.09f + Head.forward * 0.06f;

    protected bool AtMouth
    {
        get
        {
            if (Head == null) return false;
            var from = mouthPoint != null ? mouthPoint.position : transform.position;
            return (from - MouthPosition).sqrMagnitude < mouthDistance * mouthDistance;
        }
    }

    protected virtual void Awake() => _grab = GetComponent<XRGrabInteractable>();

    protected void Haptic(float amplitude, float duration)
    {
        foreach (var interactor in _grab.interactorsSelecting)
            if (interactor is XRBaseInputInteractor input) input.SendHapticImpulse(amplitude, duration);
    }

    protected void MarkSpent() => _spent = true;

    protected virtual void Update()
    {
        if (!_spent || IsHeld)
        {
            _spentTimer = 0f;
            return;
        }

        _spentTimer += Time.deltaTime;
        float shrink = _spentTimer - despawnDelay;
        if (shrink <= 0f) return;
        if (shrink >= 0.4f)
        {
            Destroy(gameObject);
            return;
        }
        transform.localScale = Vector3.one * (1f - shrink / 0.4f);
    }
}
