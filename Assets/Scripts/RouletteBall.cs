using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Scripted (not physics-driven) roulette ball. RouletteWheel picks the
// winning pocket first, then calls Spin() with it; this animates the ball
// to land exactly there.
//
// The trick: during a spin the ball is parented to the wheel's spinning
// head ("rotor") and animated in the rotor's LOCAL space. Its angle in that
// space goes from wherever it starts to the target pocket's angle, plus
// several whole laps, with a smoothly decaying speed. The wheel's own
// rotation is added on top automatically, so the ball always ends up resting
// in the chosen pocket and moving with it - no matter how the wheel spins.
//
// Path over the normalized spin time u (0..1):
//   0 .. dropStart      fast laps on the outer track (track radius/height
//                       come from where the Ball was placed in the scene)
//   dropStart .. landTime  spirals inward and down to the pocket ring,
//                       with one small "deflector" hop on the way
//   landTime .. 1       bounces along the pocket ring, decaying, and comes
//                       to rest exactly on the target pocket
//
// The Rigidbody (if any) is kept but forced kinematic - free physics is
// what used to launch the ball out of the bowl.
public class RouletteBall : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] Vector2 durationRange = new Vector2(8.5f, 10f); // seconds from launch to resting in the pocket
    [SerializeField] Vector2Int relativeLapsRange = new Vector2Int(7, 9); // whole laps relative to the wheel (inclusive)
    [Tooltip("How quickly the ball's speed dies off. 1 = linear slowdown, higher = fast for longer, then a sharper slowdown.")]
    [SerializeField, Min(0.5f)] float speedFalloffPower = 2f;
    [Tooltip("Fraction of the spin time when the ball leaves the outer track and starts dropping inward.")]
    [SerializeField, Range(0.1f, 0.9f)] float dropStart = 0.55f;
    [Tooltip("Fraction of the spin time when the ball reaches the pocket ring and starts bouncing.")]
    [SerializeField, Range(0.15f, 0.97f)] float landTime = 0.78f;
    [SerializeField] float launchBlendTime = 0.4f; // smooths the jump from a pocket back up to the track on re-spins
    [SerializeField] float settleConfirmTime = 0.5f; // pause after coming to rest before reporting

    [Header("Path (wheel-local units)")]
    [Tooltip("Off = use the Ball's position in the scene (at Play start) as the outer track. On = use the two values below.")]
    [SerializeField] bool overrideTrack = false;
    [SerializeField] float trackRadius = 0.93f;
    [SerializeField] float trackHeight = 0.25f;
    [Tooltip("Extra height above the pocket's number decal when resting. Raise if the ball sinks into the pocket, lower if it floats.")]
    [SerializeField] float restHeightOffset = 0f;
    [Tooltip("Rough half-width of the pocket ring. Surface-following stops this far outside the pocket centers so pocket walls don't lift the ball.")]
    [SerializeField] float pocketRingHalfWidth = 0.08f;

    [Header("Bounce")]
    [SerializeField] float deflectorHopHeight = 0.02f;
    [SerializeField] float pocketBounceHeight = 0.035f;
    [SerializeField] Vector2Int pocketBounceCountRange = new Vector2Int(3, 5);
    [SerializeField] float pocketRadialWobble = 0.01f;

    [Header("Surface following (optional)")]
    [Tooltip("Surfaces the ball must never sink into while dropping inward. Left empty, it auto-uses the wheel's own non-convex MeshCollider and any visible RouletteRampBuilder.")]
    [SerializeField] List<Collider> surfaceColliders = new List<Collider>();

    Transform _homeParent;
    Vector3 _homeLocalPos;
    Coroutine _running;

    public event Action Settled;
    // For audio/FX: a hit on the wheel (strength 0..1), and the final
    // moment the ball comes to rest in its pocket.
    public event Action<float> Bounced;
    public event Action Landed;

    // True while rolling on the track / down the slope (not while bouncing
    // in the pocket ring or resting).
    public bool IsRolling { get; private set; }
    // Ball speed relative to the wheel, 1 at launch down to 0 at rest.
    public float RollSpeed01 { get; private set; }

    float BallLocalRadius => transform.localScale.x * 0.5f; // Unity sphere mesh has radius 0.5

    void Awake()
    {
        _homeParent = transform.parent;
        _homeLocalPos = transform.localPosition;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None; // we move the transform directly
        }

        if (surfaceColliders.Count == 0 && _homeParent != null) AutoFindSurfaces();
    }

    void AutoFindSurfaces()
    {
        var wheelCollider = _homeParent.GetComponent<MeshCollider>();
        if (wheelCollider != null && !wheelCollider.convex) surfaceColliders.Add(wheelCollider);

        foreach (var ramp in _homeParent.GetComponentsInChildren<RouletteRampBuilder>())
        {
            var col = ramp.GetComponent<Collider>();
            var rend = ramp.GetComponent<Renderer>();
            // Only visible ramps - an invisible one is a hidden helper, not
            // something the player sees the ball rolling on.
            if (col != null && rend != null && rend.enabled) surfaceColliders.Add(col);
        }
    }

    // direction: +1 if the wheel spins clockwise seen from above (ball then
    // runs counter-clockwise relative to it), -1 for the opposite.
    // Returns how many seconds until the ball comes to rest in the pocket,
    // so the wheel can time its own slowdown to stop at the same moment.
    public float Spin(Transform rotor, RoulettePocket target, int direction)
    {
        if (_running != null) StopCoroutine(_running);
        float duration = UnityEngine.Random.Range(durationRange.x, durationRange.y);
        _running = StartCoroutine(SpinRoutine(rotor, target, direction >= 0 ? 1 : -1, duration));
        return duration;
    }

    IEnumerator SpinRoutine(Transform rotor, RoulettePocket target, int direction, float duration)
    {
        transform.SetParent(rotor, true);

        // Outer track: where the ball was placed in the scene, measured from
        // the rotor (which sits on the wheel's axis).
        Vector3 home = rotor.InverseTransformPoint(_homeParent.TransformPoint(_homeLocalPos));
        float trackR = overrideTrack ? trackRadius : Flat(home).magnitude;
        float trackH = overrideTrack ? trackHeight : home.y;

        // Target: the pocket's real (bounds-derived) center, ball resting on it.
        Vector3 rest = rotor.InverseTransformPoint(target.WorldCenter);
        rest.y += BallLocalRadius + restHeightOffset;
        float pocketR = Flat(rest).magnitude;
        float pocketH = rest.y;
        float pocketAngle = Mathf.Atan2(rest.z, rest.x) * Mathf.Rad2Deg;

        // Start from the ball's current angle (on re-spins it's sitting in
        // last round's pocket), lifted back up onto the track.
        Vector3 start = transform.localPosition;
        float startR = Flat(start).magnitude;
        float startH = start.y;
        float startAngle = startR > 1e-4f ? Mathf.Atan2(start.z, start.x) * Mathf.Rad2Deg
                                          : Mathf.Atan2(home.z, home.x) * Mathf.Rad2Deg;

        // Total relative travel = whole laps + exactly the gap to the target.
        int laps = UnityEngine.Random.Range(relativeLapsRange.x, relativeLapsRange.y + 1);
        float gap = Mathf.Repeat(direction * (pocketAngle - startAngle), 360f);
        float totalAngle = direction * (360f * laps + gap);

        int bounces = UnityEngine.Random.Range(pocketBounceCountRange.x, pocketBounceCountRange.y + 1);
        float drop = Mathf.Min(dropStart, landTime - 0.05f);
        float land = landTime;
        float falloff = speedFalloffPower + 1f;

        float t = 0f;
        Vector3 prevLocal = start;
        bool hopLanded = false, hitPocketRing = false;
        int lastBounce = 0;
        IsRolling = true;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            RollSpeed01 = Mathf.Pow(1f - u, speedFalloffPower);
            IsRolling = u < land;

            // Angle: speed ~ (1-u)^power, integrated and normalized to 0..1.
            float angle = startAngle + totalAngle * (1f - Mathf.Pow(1f - u, falloff));

            float r, h;
            if (u <= drop)
            {
                r = trackR;
                h = trackH;
            }
            else if (u <= land)
            {
                float s = (u - drop) / (land - drop);
                float e = s * s; // accelerates inward, like rolling down the slope
                r = Mathf.Lerp(trackR, pocketR, e);
                h = Mathf.Lerp(trackH, pocketH, e);
                if (r > pocketR + pocketRingHalfWidth && r < trackR - BallLocalRadius * 2f)
                {
                    float surface = SampleSurface(rotor, angle, r, trackH);
                    if (!float.IsNegativeInfinity(surface))
                        h = Mathf.Max(h, Mathf.Min(surface + BallLocalRadius, trackH));
                }
                h += deflectorHopHeight * Hump(s, 0.3f, 0.6f);
                if (!hopLanded && s >= 0.6f && deflectorHopHeight > 0f)
                {
                    hopLanded = true;
                    Bounced?.Invoke(0.5f);
                }
            }
            else
            {
                float s = (u - land) / (1f - land);
                float decay = (1f - s) * (1f - s);

                // First impact on the pocket ring, then one hit every time
                // a bounce comes back down (each |sin| zero), getting softer.
                if (!hitPocketRing)
                {
                    hitPocketRing = true;
                    Bounced?.Invoke(1f);
                }
                int bounce = Mathf.FloorToInt(s * bounces);
                if (bounce > lastBounce && bounce < bounces)
                {
                    lastBounce = bounce;
                    Bounced?.Invoke(Mathf.Max(0.15f, decay));
                }
                r = pocketR + pocketRadialWobble * decay * Mathf.Sin(s * Mathf.PI * bounces * 1.3f);
                h = pocketH + pocketBounceHeight * decay * Mathf.Abs(Mathf.Sin(s * Mathf.PI * bounces));
            }

            // Re-spin from a pocket: ease from where the ball was onto the track.
            if (launchBlendTime > 0f && t < launchBlendTime)
            {
                float w = Mathf.SmoothStep(0f, 1f, t / launchBlendTime);
                r = Mathf.Lerp(startR, r, w);
                h = Mathf.Lerp(startH, h, w);
            }

            Vector3 local = Polar(angle, r, h);
            transform.localPosition = local;
            Roll(rotor, local - prevLocal);
            prevLocal = local;
            yield return null;
        }

        // Exactly on the chosen pocket, riding along with the wheel.
        transform.localPosition = rest;
        IsRolling = false;
        RollSpeed01 = 0f;
        Landed?.Invoke();

        yield return new WaitForSeconds(settleConfirmTime);
        _running = null;
        Settled?.Invoke();
    }

    // Highest surface point under (angle, r), in rotor-local Y, or -inf.
    float SampleSurface(Transform rotor, float angleDeg, float r, float trackH)
    {
        if (surfaceColliders.Count == 0) return float.NegativeInfinity;

        Vector3 origin = rotor.TransformPoint(Polar(angleDeg, r, trackH + 0.5f));
        var ray = new Ray(origin, -rotor.up);
        float best = float.NegativeInfinity;
        foreach (var col in surfaceColliders)
        {
            if (col == null || !col.enabled) continue;
            if (col.Raycast(ray, out RaycastHit hit, 10f))
                best = Mathf.Max(best, rotor.InverseTransformPoint(hit.point).y);
        }
        return best;
    }

    // Spin the ball about its own axis to match how far it moved relative
    // to the wheel surface, so it visibly rolls instead of sliding.
    void Roll(Transform rotor, Vector3 localDelta)
    {
        Vector3 worldDelta = rotor.TransformVector(localDelta);
        float dist = worldDelta.magnitude;
        float worldRadius = transform.lossyScale.x * 0.5f;
        if (dist < 1e-6f || worldRadius < 1e-6f) return;

        Vector3 axis = Vector3.Cross(rotor.up, worldDelta / dist);
        transform.Rotate(axis, dist / worldRadius * Mathf.Rad2Deg, Space.World);
    }

    static Vector3 Polar(float angleDeg, float r, float h)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // 0 outside [a,b], a single smooth bump (peak 1) inside it.
    static float Hump(float s, float a, float b)
    {
        if (s <= a || s >= b) return 0f;
        return Mathf.Sin(Mathf.PI * (s - a) / (b - a));
    }
}
