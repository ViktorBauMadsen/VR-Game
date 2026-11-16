using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A dart: grab it, throw it. In the air it turns point-first into the
// direction it is flying; if it hits the Dartboard fast enough and from the
// front, it sticks where it landed and the board scores it. Anything else
// it hits is a miss and it just drops.
//
// At rest (in the holder, or stuck in the board) the dart is held still by
// freezing its Rigidbody rather than making it kinematic. XR Grab
// Interactable puts "kinematic" back to what it was when you let go, so a
// kinematic dart would simply hang in the air instead of being thrown.
//
// The model points along +Z: tipOffset is how far the point is from the
// dart's origin.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class Dart : MonoBehaviour
{
    [SerializeField] float tipOffset = 0.095f;

    Rigidbody _rb;
    XRGrabInteractable _grab;
    Transform _homeParent;
    Vector3 _homePosition;
    Quaternion _homeRotation;
    Vector3 _lastVelocity;
    bool _flying;

    // The board this dart belongs to (set by the board).
    public Dartboard Owner { get; set; }
    public bool Held => _grab != null && _grab.isSelected;
    public float TipOffset => tipOffset;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _grab = GetComponent<XRGrabInteractable>();
        _homeParent = transform.parent;
        _homePosition = transform.localPosition;
        _homeRotation = transform.localRotation;
        _rb.isKinematic = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Freeze();
    }

    void OnEnable()
    {
        _grab.selectEntered.AddListener(OnGrabbed);
        _grab.selectExited.AddListener(OnReleased);
    }

    void OnDisable()
    {
        _grab.selectEntered.RemoveListener(OnGrabbed);
        _grab.selectExited.RemoveListener(OnReleased);
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        _flying = false;
        _rb.constraints = RigidbodyConstraints.None;
    }

    void OnReleased(SelectExitEventArgs args)
    {
        _rb.constraints = RigidbodyConstraints.None;
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _flying = true;
    }

    void FixedUpdate()
    {
        if (!_flying) return;
        Vector3 velocity = _rb.linearVelocity;
        _lastVelocity = velocity;
        // Point-first, like a real dart's flights make it do.
        if (velocity.sqrMagnitude > 1f)
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(velocity.normalized, Vector3.up), 0.4f));
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!_flying || Held) return;
        _flying = false;

        var board = collision.collider.GetComponentInParent<Dartboard>();
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        if (board != null && board.TryStick(this, point, _lastVelocity)) return;
        if (Owner != null) Owner.Missed(this);
    }

    // The board caught the dart: sit exactly there, point buried a little.
    public void StickAt(Vector3 tipPoint, Vector3 direction)
    {
        _flying = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.SetPositionAndRotation(tipPoint - direction * (tipOffset - 0.012f), rotation);
        Freeze();
    }

    // Back to the holder (unless someone is holding it).
    public void ReturnHome()
    {
        if (Held) return;
        _flying = false;
        _rb.constraints = RigidbodyConstraints.None;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        if (_homeParent != null)
            transform.SetPositionAndRotation(_homeParent.TransformPoint(_homePosition), _homeParent.rotation * _homeRotation);
        else transform.SetPositionAndRotation(_homePosition, _homeRotation);
        Freeze();
    }

    void Freeze() => _rb.constraints = RigidbodyConstraints.FreezeAll;
}
