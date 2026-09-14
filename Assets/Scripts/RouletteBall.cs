using System;
using UnityEngine;

// Physics-only: orbits around wheelCenter while decelerating and spiraling
// inward (faking the bowl's slope, since the wheel is built from flat
// primitives rather than a sculpted funnel), then hands off to real
// collisions with the rotor's fret dividers to settle into a pocket once
// it's slow enough to drop onto the rotor. Knows nothing about pocket
// numbers - RouletteWheel reads the final resting position once Settled
// fires, the same way SlotMachine (not SlotMachineReels) owns the result.
[RequireComponent(typeof(Rigidbody))]
public class RouletteBall : MonoBehaviour
{
    [SerializeField] Transform wheelCenter;
    [SerializeField] float orbitFriction = 0.6f;        // fraction of horizontal speed shed per second
    [SerializeField] float radialAssistForce = 2.5f;    // m/s^2, pulls the ball inward as it slows
    [SerializeField] float assistStartSpeed = 3f;       // above this speed, no inward assist yet
    [SerializeField] float assistFullSpeed = 1f;        // at/below this speed, full inward assist
    [SerializeField] float rotorRadius = 0.28f;         // ball is considered "on the rotor" inside this radius
    [SerializeField] float dropSpeedThreshold = 1.2f;   // must also be this slow to be allowed to drop
    [SerializeField] float settleLinearThreshold = 0.05f;
    [SerializeField] float settleAngularThreshold = 20f; // deg/sec
    [SerializeField] float settleConfirmTime = 1f;
    [SerializeField] float settlingDamping = 0.85f; // extra velocity decay per FixedUpdate once dropped onto the rotor - real pockets are cushioned and kill momentum fast, unlike bare low-friction plastic

    Rigidbody _rb;
    bool _orbiting;
    bool _settling;
    float _restTimer;

    public event Action Settled;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public void Launch(float startRadius, float speed, float startAngleDeg, float trackHeight)
    {
        _orbiting = true;
        _settling = false;
        _restTimer = 0f;

        float rad = startAngleDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        Vector3 pos = wheelCenter.position + dir * startRadius;
        pos.y = wheelCenter.position.y + trackHeight;

        _rb.position = pos;
        _rb.rotation = Quaternion.identity;
        _rb.angularVelocity = Vector3.zero;
        _rb.linearVelocity = new Vector3(-dir.z, 0f, dir.x) * speed;
    }

    void FixedUpdate()
    {
        if (_orbiting) UpdateOrbit();
        else if (_settling) UpdateSettling();
    }

    void UpdateOrbit()
    {
        Vector3 toCenter = wheelCenter.position - _rb.position;
        toCenter.y = 0f;
        float radius = toCenter.magnitude;

        Vector3 flatVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        float speed = flatVel.magnitude;

        _rb.linearVelocity -= flatVel * Mathf.Clamp01(orbitFriction * Time.fixedDeltaTime);

        float t = Mathf.InverseLerp(assistStartSpeed, assistFullSpeed, speed);
        if (t > 0f)
            _rb.AddForce(toCenter.normalized * (t * radialAssistForce), ForceMode.Acceleration);

        if (radius <= rotorRadius && speed <= dropSpeedThreshold)
        {
            _orbiting = false;
            _settling = true;
            _restTimer = 0f;
        }
    }

    void UpdateSettling()
    {
        _rb.linearVelocity *= settlingDamping;
        _rb.angularVelocity *= settlingDamping;

        bool atRest = _rb.linearVelocity.sqrMagnitude < settleLinearThreshold * settleLinearThreshold
                      && _rb.angularVelocity.sqrMagnitude < settleAngularThreshold * settleAngularThreshold;

        if (atRest)
        {
            _restTimer += Time.fixedDeltaTime;
            if (_restTimer >= settleConfirmTime)
            {
                _settling = false;
                Settled?.Invoke();
            }
        }
        else
        {
            _restTimer = 0f;
        }
    }
}
