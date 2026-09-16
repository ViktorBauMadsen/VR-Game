using System;
using System.Collections;
using UnityEngine;

// Minimal on purpose: from wherever it's sitting (positioned by hand in the
// scene), Push() just gives it a small sideways shove and lets real physics
// take over. No orbit math, no launch repositioning - once this is proven
// stable, the spin behavior (speed, orbiting, decelerating toward the
// pockets) can be layered back on top.
//
// Stays kinematic (frozen) except while actually spinning, so it never
// starts rolling on its own just from gravity/the ramp's slope before a
// spin is triggered. A while after settling, it resets back to the exact
// spot it started from and re-freezes - same end result as despawning and
// respawning the ball, without needing a prefab/Instantiate at all.
[RequireComponent(typeof(Rigidbody))]
public class RouletteBall : MonoBehaviour
{
    [SerializeField] Vector3 pushDirection = Vector3.right;
    [SerializeField] Vector2 pushForceRange = new Vector2(0.18f, 0.22f); // randomized each spin - a fixed force from a fixed spot is a fixed outcome every time
    [SerializeField] float pushDirectionJitterDegrees = 15f; // random rotation applied to pushDirection each spin, for the same reason
    [SerializeField] float settleLinearThreshold = 0.05f;
    [SerializeField] float settleAngularThreshold = 20f; // deg/sec
    [SerializeField] float settleConfirmTime = 1f;
    [SerializeField] float resetDelayAfterSettle = 6f; // how long it stays visible in the landed pocket before resetting for the next spin

    Rigidbody _rb;
    Vector3 _startPosition;
    Quaternion _startRotation;
    bool _waitingToSettle;
    float _restTimer;

    public event Action Settled;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _startPosition = transform.position;
        _startRotation = transform.rotation;
        _rb.isKinematic = true; // stays put until the first Push()
    }

    public void Push()
    {
        StopAllCoroutines();
        ResetToStart();

        _rb.isKinematic = false;
        _waitingToSettle = true;
        _restTimer = 0f;

        float force = UnityEngine.Random.Range(pushForceRange.x, pushForceRange.y);
        float jitter = UnityEngine.Random.Range(-pushDirectionJitterDegrees, pushDirectionJitterDegrees);
        Vector3 dir = Quaternion.Euler(0f, jitter, 0f) * pushDirection.normalized;
        _rb.AddForce(dir * force, ForceMode.Impulse);
    }

    [ContextMenu("Push")]
    void DebugPush() => Push();

    void FixedUpdate()
    {
        if (!_waitingToSettle) return;

        bool atRest = _rb.linearVelocity.sqrMagnitude < settleLinearThreshold * settleLinearThreshold
                      && _rb.angularVelocity.sqrMagnitude < settleAngularThreshold * settleAngularThreshold;

        if (atRest)
        {
            _restTimer += Time.fixedDeltaTime;
            if (_restTimer >= settleConfirmTime)
            {
                _waitingToSettle = false;
                Settled?.Invoke();
                StartCoroutine(ResetAfterDelay());
            }
        }
        else
        {
            _restTimer = 0f;
        }
    }

    IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(resetDelayAfterSettle);
        ResetToStart();
    }

    void ResetToStart()
    {
        _rb.isKinematic = true;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        transform.position = _startPosition;
        transform.rotation = _startRotation;
    }
}
