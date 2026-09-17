using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A physical bet: grab it, drop it over a BettingSpot and it snaps into
// place and spends its value from PlayerBalance (same TrySpend/AddBalance
// convention SlotMachine uses); pick a placed chip back up and its value is
// refunded. Detection is a straight-down raycast from the release point
// rather than waiting for physics to settle, so placement reads as an
// actual "snap" instead of the chip visibly rolling into position.
// RouletteBettingManager reads CurrentSpot/Value off whichever chips are
// still placed once a spin resolves.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class CasinoChip : MonoBehaviour
{
    [SerializeField] int value = 5;
    [SerializeField] float snapRayDistance = 0.4f;

    Rigidbody _rb;
    XRGrabInteractable _interactable;
    float _chipHeight;
    readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

    public int Value => value;
    public BettingSpot CurrentSpot { get; private set; }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _interactable = GetComponent<XRGrabInteractable>();

        var rend = GetComponentInChildren<Renderer>();
        _chipHeight = rend != null ? rend.bounds.size.y : 0.02f;
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

    // Picking a placed chip back up cancels its bet - the same value it
    // spent to get snapped down is handed back.
    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (CurrentSpot != null) Unplace(refund: true);
    }

    void OnSelectExited(SelectExitEventArgs args) => TrySnapToSpot();

    // XRGrabInteractable restores Rigidbody.isKinematic to its pre-grab
    // value inside Drop(), synchronously and *before* selectExited fires -
    // so whichever branch runs here has to set isKinematic itself rather
    // than assume it's still whatever it was on pickup, or a chip dropped
    // off-spot after being un-placed can end up stuck frozen mid-air.
    void TrySnapToSpot()
    {
        int count = Physics.RaycastNonAlloc(transform.position, Vector3.down, _hitBuffer, snapRayDistance,
            ~0, QueryTriggerInteraction.Collide);

        BettingSpot spot = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (_hitBuffer[i].distance >= bestDistance) continue;
            var candidate = _hitBuffer[i].collider.GetComponentInParent<BettingSpot>();
            if (candidate == null) continue;
            bestDistance = _hitBuffer[i].distance;
            spot = candidate;
        }

        if (spot != null && PlayerBalance.Instance != null && PlayerBalance.Instance.TrySpend(value))
            Place(spot);
        else
            _rb.isKinematic = false;
    }

    void Place(BettingSpot spot)
    {
        CurrentSpot = spot;
        transform.position = spot.GetNextPlacementPosition(_chipHeight);
        transform.rotation = Quaternion.FromToRotation(transform.up, Vector3.up) * transform.rotation; // lie flat
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        spot.RegisterChip(this);
        RouletteBettingManager.Instance?.RegisterPlacedChip(this);
    }

    void Unplace(bool refund)
    {
        if (CurrentSpot == null) return;
        CurrentSpot.UnregisterChip(this);
        RouletteBettingManager.Instance?.UnregisterPlacedChip(this);
        CurrentSpot = null;
        if (refund) PlayerBalance.Instance?.AddBalance(value);
    }

    // Called by RouletteBettingManager once a spin resolves, win or lose -
    // chips are an infinite, on-demand resource (see ChipDispenser), so a
    // resolved bet just disappears rather than returning anywhere; balance
    // already reflects the correct net result via TrySpend/AddBalance.
    public void ResolveAndDestroy()
    {
        Unplace(refund: false); // must run first - unregisters before the object is gone
        Destroy(gameObject);
    }
}
