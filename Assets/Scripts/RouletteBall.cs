using System;
using UnityEngine;

// Minimal on purpose: from wherever it's sitting (positioned by hand in the
// scene), Push() just gives it a small sideways shove and lets real physics
// take over. No orbit math, no launch repositioning - once this is proven
// stable, the spin behavior (speed, orbiting, decelerating toward the
// pockets) can be layered back on top.
[RequireComponent(typeof(Rigidbody))]
public class RouletteBall : MonoBehaviour
{
    [SerializeField] Vector3 pushDirection = Vector3.right;
    [SerializeField] float pushForce = 0.3f; // small on purpose - nudge it into a slow roll first, then increase once that looks right
    [SerializeField] float settleLinearThreshold = 0.05f;
    [SerializeField] float settleAngularThreshold = 20f; // deg/sec
    [SerializeField] float settleConfirmTime = 1f;

    Rigidbody _rb;
    bool _waitingToSettle;
    float _restTimer;

    public event Action Settled;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public void Push()
    {
        _waitingToSettle = true;
        _restTimer = 0f;
        _rb.AddForce(pushDirection.normalized * pushForce, ForceMode.Impulse);
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
            }
        }
        else
        {
            _restTimer = 0f;
        }
    }
}
