using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// The grabbable ping-pong ball. Driven entirely by the XRGrabInteractable's
// select events: selectEntered = picked up, selectExited = thrown. Only a
// ball that has been thrown (released since it was last picked up) can score,
// so dunking a cup while still holding the ball doesn't count.
//
// Throw velocity is computed here rather than by XRGrabInteractable's own
// Throw On Detach. XRI measures the ball's *world-space* motion, so anything
// the rig does while you hold the ball - walking with the stick, snap/smooth
// turning (which swings your hand round your head in a single frame) -
// gets added to the throw. Instead this samples the ball's position relative
// to the XR Origin, i.e. purely how your hand moved in your play space, and
// turns that into the release velocity.
//
// Between throws the ball waits kinematic, floating at its team's spawn point
// so it's always within reach. XRGrabInteractable restores isKinematic to its
// pre-grab value (true here) inside Drop(), before selectExited fires - so
// OnSelectExited switches it back to dynamic before applying the throw.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class BeerPongBall : MonoBehaviour
{
    [SerializeField] Transform spawnPoint;
    [Tooltip("Seconds after a throw before the ball returns to its spawn point if it hasn't scored.")]
    [SerializeField] float respawnDelay = 4f;
    [Tooltip("Return immediately if the ball falls this far below its spawn point (i.e. off the table).")]
    [SerializeField] float fallDistance = 0.6f;
    [SerializeField] float respawnAfterScoreDelay = 0.4f;

    [Header("Throwing")]
    [Tooltip("Multiplier on your real hand speed at release. 1 = exactly as fast as your hand moved.")]
    [SerializeField] float throwStrength = 1f;
    [Tooltip("How much of the hand's motion just before release is averaged into the throw, in seconds.")]
    [SerializeField] float throwSampleWindow = 0.06f;
    [Tooltip("Safety cap against tracking glitches. A firm beer pong throw is around 3-4 m/s.")]
    [SerializeField] float maxThrowSpeed = 7f;

    [Header("Cup impacts")]
    [Tooltip("Fraction of its speed the ball keeps after touching a cup. Real cups are soft plastic, so a rim hit nearly kills the ball.")]
    [Range(0f, 1f)]
    [SerializeField] float cupImpactSpeedKeep = 0.3f;

    const int SampleCount = 16;

    Rigidbody _rb;
    XRGrabInteractable _interactable;
    bool _inFlight;
    float _releaseTime;

    Transform _origin;
    readonly Vector3[] _samplePositions = new Vector3[SampleCount]; // XR Origin space
    readonly float[] _sampleTimes = new float[SampleCount];
    int _sampleHead;
    int _samplesStored;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _interactable = GetComponent<XRGrabInteractable>();
        _interactable.throwOnDetach = false; // we set the release velocity ourselves, see header
    }

    void Start() => Respawn();

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
        CancelInvoke(nameof(Respawn));
        _inFlight = false;

        var origin = args.interactorObject.transform.GetComponentInParent<XROrigin>();
        _origin = origin != null ? origin.transform : null;
        _samplesStored = 0;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearVelocity = Vector3.ClampMagnitude(GetHandVelocity() * throwStrength, maxThrowSpeed);
        _rb.angularVelocity = Vector3.zero;
        _inFlight = true;
        _releaseTime = Time.time;
    }

    // After XRI has moved the held ball to the hand for this frame.
    void LateUpdate()
    {
        if (!_interactable.isSelected) return;

        Vector3 p = _origin != null ? _origin.InverseTransformPoint(transform.position) : transform.position;
        _samplePositions[_sampleHead] = p;
        _sampleTimes[_sampleHead] = Time.time;
        _sampleHead = (_sampleHead + 1) % SampleCount;
        _samplesStored = Mathf.Min(_samplesStored + 1, SampleCount);
    }

    // Average hand velocity over the last throwSampleWindow seconds, measured
    // in the XR Origin's space and then rotated into world space.
    Vector3 GetHandVelocity()
    {
        if (_samplesStored < 2) return Vector3.zero;

        int newest = (_sampleHead - 1 + SampleCount) % SampleCount;
        int oldest = newest;
        for (int i = 1; i < _samplesStored; i++)
        {
            int idx = (newest - i + SampleCount) % SampleCount;
            oldest = idx;
            if (_sampleTimes[newest] - _sampleTimes[idx] >= throwSampleWindow) break;
        }

        float dt = _sampleTimes[newest] - _sampleTimes[oldest];
        if (dt <= 0.0001f) return Vector3.zero;

        Vector3 localVelocity = (_samplePositions[newest] - _samplePositions[oldest]) / dt;
        return _origin != null ? _origin.TransformVector(localVelocity) : localVelocity;
    }

    void Update()
    {
        if (!_inFlight || spawnPoint == null) return;

        bool fellOff = transform.position.y < spawnPoint.position.y - fallDistance;
        if (fellOff || Time.time - _releaseTime > respawnDelay)
            Respawn();
    }

    // Runs after the physics bounce has been resolved, so this scales down
    // the rebound: a rim hit leaves the ball almost dead, free to roll in or
    // drop off the edge.
    void OnCollisionEnter(Collision collision)
    {
        if (_rb.isKinematic) return;
        if (collision.collider.GetComponentInParent<BeerPongCup>() == null) return;

        _rb.linearVelocity *= cupImpactSpeedKeep;
        _rb.angularVelocity *= cupImpactSpeedKeep;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_inFlight) return;
        var cup = other.GetComponentInParent<BeerPongCup>();
        if (cup == null || !cup.IsStanding) return;

        _inFlight = false; // one cup per throw
        cup.Hit();
        Invoke(nameof(Respawn), respawnAfterScoreDelay);
    }

    public void Respawn()
    {
        if (_interactable.isSelected || spawnPoint == null) return;

        _inFlight = false;
        if (!_rb.isKinematic)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        _rb.isKinematic = true;
        _rb.position = spawnPoint.position;
        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
    }
}
