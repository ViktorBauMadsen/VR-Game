using System;
using UnityEngine;

// Physics-only: orbits around wheelCenter while decelerating, gets pulled
// toward the real pocket ring's radius as it slows (not toward the bare
// center - the wheelhead's actual 3D pocket walls live out at
// pocketRingRadius, so that's the only place real collision can catch the
// ball), then hands off to real collisions with those walls (baked into the
// wheelhead's own mesh) to settle into a pocket. Knows nothing about pocket
// numbers - RouletteWheel reads the final resting position once Settled
// fires, the same way SlotMachine (not SlotMachineReels) owns the result.
[RequireComponent(typeof(Rigidbody))]
public class RouletteBall : MonoBehaviour
{
    [SerializeField] Transform wheelCenter;
    [SerializeField] float orbitFriction = 0.6f;        // fraction of horizontal speed shed per second
    [SerializeField] float radialAssistForce = 2.5f;    // m/s^2, pulls the ball toward pocketRingRadius as it slows
    [SerializeField] float assistStartSpeed = 3f;       // above this speed, no radial assist yet
    [SerializeField] float assistFullSpeed = 1f;        // at/below this speed, full radial assist
    [SerializeField] float pocketRingRadius = 0.41f;    // measured radius (from wheelCenter) where the real numbered pockets sit
    [SerializeField] float ringRadiusTolerance = 0.06f; // must be within this of pocketRingRadius, and slow, to drop into settling
    [SerializeField] float dropSpeedThreshold = 1.2f;   // must be this slow (as well as near the ring) to settle
    [SerializeField] float settleLinearThreshold = 0.05f;
    [SerializeField] float settleAngularThreshold = 20f; // deg/sec
    [SerializeField] float settleConfirmTime = 1f;
    [SerializeField] float settlingDamping = 0.995f; // gentle extra decay per FixedUpdate, just a safety net so a degenerate near-frictionless equilibrium can't stall forever - the actual "settles after bouncing around" look comes from real bounce/friction losses against the wheelhead's own pocket walls, so this should stay close to 1
    [SerializeField] float raycastStartHeight = 0.4f; // above wheelCenter.y - must clear the wheelhead's own real geometry but stay below any containing lid, so the downward probe below hits the real track and not the lid's underside
    [SerializeField] float spawnClearance = 0.04f;    // how far above whatever the probe hits to actually spawn the ball

    Rigidbody _rb;
    bool _orbiting;
    bool _settling;
    float _restTimer;

    public event Action Settled;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    // Finds the real track surface under the launch point via a downward
    // raycast rather than assuming a height, since that surface genuinely
    // varies by radius on a sculpted model - and a guessed/teleported height
    // that happens to land inside another collider (a containment lid, a
    // wall) is exactly what causes PhysX to shove the ball out violently on
    // the next physics step.
    public void Launch(float startRadius, float speed, float startAngleDeg)
    {
        _orbiting = true;
        _settling = false;
        _restTimer = 0f;

        float rad = startAngleDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        Vector3 pos = wheelCenter.position + dir * startRadius;

        Vector3 rayStart = pos;
        rayStart.y = wheelCenter.position.y + raycastStartHeight;
        pos.y = Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastStartHeight + 1f)
            ? hit.point.y + spawnClearance
            : rayStart.y;

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
        Vector3 radialDir = radius > 0.0001f ? toCenter / radius : Vector3.zero;

        Vector3 flatVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        float speed = flatVel.magnitude;

        _rb.linearVelocity -= flatVel * Mathf.Clamp01(orbitFriction * Time.fixedDeltaTime);

        float t = Mathf.InverseLerp(assistStartSpeed, assistFullSpeed, speed);
        if (t > 0f)
        {
            // Pull inward while outside the pocket ring, push back outward
            // if momentum carries it past the ring toward the empty hub -
            // either way the target is the ring, never the bare center.
            float radiusError = radius - pocketRingRadius;
            Vector3 dir = radiusError >= 0f ? radialDir : -radialDir;
            _rb.AddForce(dir * (t * radialAssistForce), ForceMode.Acceleration);
        }

        if (speed <= dropSpeedThreshold && Mathf.Abs(radius - pocketRingRadius) <= ringRadiusTolerance)
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
