using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A dart: grab it, throw it. In the air it turns point-first into the
// direction it is flying; if it hits the Dartboard fast enough and from the
// front, it sticks where it landed and the board scores it. Anything else
// it hits is a miss and it just drops.
//
// THE THROW IS ASSISTED. A real throwing motion in VR is slow and short
// (no weight in your hand, and you let go early), so a dart launched at
// your hand's true speed drops to the floor long before the board. So the
// dart measures how your hand was moving over the last moments before you
// let go and launches itself along that line, a good deal faster, and
// falls more gently than real gravity. A flick of the wrist is enough.
// throwBoost, the speed limits and gravityScale below are the feel knobs;
// aimAssist nudges the throw toward the board (0 = none).
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

    [Header("Throw feel")]
    [Tooltip("The dart leaves at your hand's speed times this.")]
    [SerializeField] float throwBoost = 3f;
    [Tooltip("Any real throw goes at least this fast (m/s).")]
    [SerializeField] float minimumThrowSpeed = 7f;
    [SerializeField] float maximumThrowSpeed = 16f;
    [Tooltip("Hand speed (m/s) below which letting go is a drop, not a throw.")]
    [SerializeField] float dropBelowSpeed = 0.5f;
    [Tooltip("How hard gravity pulls on a flying dart. 1 = real.")]
    [SerializeField, Range(0f, 1f)] float gravityScale = 0.3f;
    [Tooltip("How much the throw is bent toward the middle of the board. 0 = not at all.")]
    [SerializeField, Range(0f, 0.6f)] float aimAssist = 0.12f;

    const int Samples = 8;

    Rigidbody _rb;
    XRGrabInteractable _grab;
    Transform _homeParent;
    Vector3 _homePosition;
    Quaternion _homeRotation;
    Vector3 _lastVelocity;
    bool _flying;
    readonly Vector3[] _positions = new Vector3[Samples];
    readonly float[] _times = new float[Samples];
    int _sample, _sampleCount;

    // The board this dart belongs to (set by the board).
    public Dartboard Owner { get; set; }
    public bool Held => _grab != null && _grab.isSelected;
    public float TipOffset => tipOffset;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _grab = GetComponent<XRGrabInteractable>();
        _grab.throwOnDetach = false; // the dart launches itself (see OnReleased)
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
        _sampleCount = 0;
        _rb.constraints = RigidbodyConstraints.None;
    }

    // Remember where the hand has been for the last few frames.
    void Update()
    {
        if (!Held) return;
        _positions[_sample] = transform.position;
        _times[_sample] = Time.time;
        _sample = (_sample + 1) % Samples;
        _sampleCount = Mathf.Min(_sampleCount + 1, Samples);
    }

    void OnReleased(SelectExitEventArgs args)
    {
        _rb.constraints = RigidbodyConstraints.None;
        _rb.isKinematic = false;
        _rb.angularVelocity = Vector3.zero;

        Vector3 hand = HandVelocity();
        float speed = hand.magnitude;
        if (speed < dropBelowSpeed)
        {
            // Just let go of it.
            _rb.useGravity = true;
            _rb.linearVelocity = hand;
            _flying = true;
            return;
        }

        Vector3 direction = hand / speed;
        if (Owner != null && aimAssist > 0f)
        {
            Vector3 toBoard = Owner.transform.position - transform.position;
            if (toBoard.sqrMagnitude > 0.01f && Vector3.Dot(direction, toBoard.normalized) > 0.5f)
                direction = Vector3.Slerp(direction, toBoard.normalized, aimAssist).normalized;
        }

        _rb.useGravity = false; // FixedUpdate applies the gentler pull
        _rb.linearVelocity = direction * Mathf.Clamp(speed * throwBoost, minimumThrowSpeed, maximumThrowSpeed);
        _rb.rotation = Quaternion.LookRotation(direction, Vector3.up);
        _lastVelocity = _rb.linearVelocity;
        _flying = true;
    }

    // Average hand velocity over the last ~0.12 s before release - one
    // frame's worth is too jittery to aim with.
    Vector3 HandVelocity()
    {
        if (_sampleCount < 2) return Vector3.zero;
        int newest = (_sample - 1 + Samples) % Samples;
        int oldest = newest;
        for (int i = 1; i < _sampleCount; i++)
        {
            int index = (newest - i + Samples) % Samples;
            if (_times[newest] - _times[index] > 0.12f) break;
            oldest = index;
        }
        float span = _times[newest] - _times[oldest];
        return span > 0.0001f ? (_positions[newest] - _positions[oldest]) / span : Vector3.zero;
    }

    void FixedUpdate()
    {
        if (!_flying) return;
        if (!_rb.useGravity) _rb.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
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
        _rb.useGravity = true; // whatever happens next, it falls normally

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
        _rb.useGravity = true;
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
