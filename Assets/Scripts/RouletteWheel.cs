using System;
using System.Collections.Generic;
using UnityEngine;

// Orchestrates a full roulette spin. The winning pocket is picked FIRST,
// uniformly at random from the 37 wired pockets (European single-zero
// wheel: 0-36, each 1/37), then RouletteBall animates the ball to land in
// exactly that pocket. So the outcome is always fair and always matches
// what the player sees. No betting logic here - Spin() just produces a
// result via OnResult, the same way SlotMachine owns bet/payout while
// SlotMachineReels only handles the visual mechanism.
//
// The wheel turns too: at startup the model's mesh and the 37 pocket
// decals are moved under a runtime-only "SpinningHead" child sitting on
// the wheel's axis, and that spins (opposite to the ball) and slows down.
// The original root MeshRenderer is just hidden at runtime; nothing in the
// scene is deleted, and the static colliders are left alone.
public class RouletteWheel : MonoBehaviour
{
    // Standard European (single-zero) wheel pocket order, clockwise from 0.
    // Public so SetupRouletteWheel can paint each physical pocket with the
    // same number this array says it should read as.
    public static readonly int[] WheelOrder =
    {
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23,
        10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
    };

    static readonly HashSet<int> RedNumbers = new HashSet<int>
    {
        1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36
    };

    public static bool IsRed(int number) => RedNumbers.Contains(number);

    [SerializeField] RouletteBall ball;
    // One entry per physical pocket, each tagged with the number painted on
    // it, so a spin result always matches what's visually under the ball
    // instead of trusting a separate formula to stay in sync with the art.
    [SerializeField] RoulettePocket[] pockets;

    [Header("Wheel spin")]
    [Tooltip("Optional. Leave empty to build a spinning head at runtime from this object's mesh + pockets.")]
    [SerializeField] Transform rotor;
    [SerializeField] bool clockwise = true; // seen from above; the ball runs the other way
    [SerializeField] float wheelSpinSpeed = 90f; // deg/sec right after a spin starts
    [SerializeField] float wheelSpinUpTime = 0.6f;
    // The slowdown is timed per spin so the wheel stops exactly when the
    // ball comes to rest in its pocket.

    bool _isSpinning;
    RoulettePocket _target;
    float _wheelSpeed;
    float _wheelDecel; // deg/sec^2 for the current spin
    bool _spinningUp;

    public bool IsSpinning => _isSpinning;
    public RouletteBall Ball => ball;
    // Current wheel speed, 1 = full spin speed, 0 = stopped (for audio).
    public float WheelSpeed01 => wheelSpinSpeed > 0f ? _wheelSpeed / wheelSpinSpeed : 0f;
    public event Action<int> OnResult;

    void Awake()
    {
        if (ball == null)
        {
            Debug.LogWarning("RouletteWheel: ball not assigned.");
            return;
        }
        ball.Settled += HandleBallSettled;

        ValidatePockets();
        if (rotor == null) rotor = BuildRotor();
    }

    void OnDestroy()
    {
        if (ball != null) ball.Settled -= HandleBallSettled;
    }

    void Update()
    {
        if (rotor == null) return;

        float dt = Time.deltaTime;
        if (_spinningUp)
        {
            float accel = wheelSpinSpeed / Mathf.Max(0.01f, wheelSpinUpTime);
            _wheelSpeed = Mathf.MoveTowards(_wheelSpeed, wheelSpinSpeed, accel * dt);
            if (_wheelSpeed >= wheelSpinSpeed) _spinningUp = false;
        }
        else
        {
            _wheelSpeed = Mathf.MoveTowards(_wheelSpeed, 0f, _wheelDecel * dt);
        }

        if (_wheelSpeed > 0f)
            rotor.Rotate(0f, (clockwise ? 1f : -1f) * _wheelSpeed * dt, 0f, Space.Self);
    }

    public void Spin()
    {
        if (_isSpinning || ball == null || rotor == null) return;

        _target = PickRandomPocket();
        if (_target == null)
        {
            Debug.LogError("RouletteWheel: no pockets wired up - can't spin.");
            return;
        }

        _isSpinning = true;
        _spinningUp = true;
        float ballDuration = ball.Spin(rotor, _target, clockwise ? 1 : -1);

        // Spin up, then slow down linearly to reach zero right as the ball
        // settles. The ball is animated relative to the wheel, so this
        // timing never changes which pocket it lands in.
        float slowdownTime = Mathf.Max(0.5f, ballDuration - wheelSpinUpTime);
        _wheelDecel = wheelSpinSpeed / slowdownTime;
    }

    // Uniform over every wired pocket: 1/37 each on a European wheel.
    RoulettePocket PickRandomPocket()
    {
        var valid = new List<RoulettePocket>();
        if (pockets != null)
            foreach (var p in pockets)
                if (p != null) valid.Add(p);
        return valid.Count == 0 ? null : valid[UnityEngine.Random.Range(0, valid.Count)];
    }

    void HandleBallSettled()
    {
        _isSpinning = false;
        _spinningUp = false;
        _wheelSpeed = 0f; // guarantee it's fully stopped with the ball in the pocket
        int number = _target != null ? _target.Number : 0;

        // Sanity check: the ball should be sitting on the pocket we picked.
        RoulettePocket closest = FindClosestPocket();
        if (closest != null && closest != _target)
            Debug.LogWarning($"RouletteWheel: picked {number} but the nearest pocket to the ball is {closest.Number} - check RouletteBall's rest height / pocket wiring.");

        Debug.Log($"RouletteWheel: ball settled on {number}");
        OnResult?.Invoke(number);
    }

    void ValidatePockets()
    {
        var seen = new HashSet<int>();
        int count = 0;
        if (pockets != null)
            foreach (var p in pockets)
            {
                if (p == null) continue;
                count++;
                if (p.Number < 0 || p.Number > 36 || !seen.Add(p.Number))
                    Debug.LogError($"RouletteWheel: pocket '{p.name}' has invalid/duplicate number {p.Number}.");
            }
        if (count != 37)
            Debug.LogError($"RouletteWheel: expected 37 pockets (European 0-36), found {count}. Results won't have fair odds until this is fixed.");
    }

    // Runtime-only: an empty child on the wheel's axis, holding a copy of
    // this object's mesh plus all the pocket decals, so they spin together.
    Transform BuildRotor()
    {
        if (pockets == null || pockets.Length == 0) return null;

        // Pockets are evenly spaced around a circle, so their average
        // position is the wheel's axis even if the model's pivot isn't.
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var p in pockets)
        {
            if (p == null) continue;
            sum += p.WorldCenter;
            n++;
        }
        if (n == 0) return null;
        Vector3 axisLocal = transform.InverseTransformPoint(sum / n);
        axisLocal.y = 0f;

        var head = new GameObject("SpinningHead (runtime)").transform;
        head.SetParent(transform, false);
        head.localPosition = axisLocal;
        head.localRotation = Quaternion.identity;
        head.localScale = Vector3.one;

        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (mf != null && mr != null && mr.enabled)
        {
            var visual = new GameObject("WheelModel (spinning copy)");
            visual.transform.SetParent(transform, false); // same pose as the original mesh
            visual.transform.SetParent(head, true);
            visual.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var copy = visual.AddComponent<MeshRenderer>();
            copy.sharedMaterials = mr.sharedMaterials;
            copy.shadowCastingMode = mr.shadowCastingMode;
            copy.receiveShadows = mr.receiveShadows;
            mr.enabled = false;
        }

        foreach (var p in pockets)
            if (p != null) p.transform.SetParent(head, true);

        return head;
    }

    RoulettePocket FindClosestPocket()
    {
        if (pockets == null || pockets.Length == 0) return null;

        Vector3 ballPos = ball.transform.position;
        RoulettePocket closest = null;
        float bestDist = float.MaxValue;
        foreach (var pocket in pockets)
        {
            if (pocket == null) continue;
            float dist = Vector3.Distance(ballPos, pocket.WorldCenter);
            if (dist < bestDist)
            {
                bestDist = dist;
                closest = pocket;
            }
        }
        return closest;
    }

    [ContextMenu("Debug Spin")]
    void DebugSpin() => Spin();
}
