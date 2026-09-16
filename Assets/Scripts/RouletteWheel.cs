using System;
using System.Collections.Generic;
using UnityEngine;

// Orchestrates a full roulette spin: the wheel itself never turns - only
// the ball moves, launched at a random speed/angle to orbit and settle via
// RouletteBall's physics, then this reads off which pocket it landed in
// once it's at rest. No betting logic here - Spin() just produces a
// result, the same way SlotMachine owns bet/payout while SlotMachineReels
// only handles the visual mechanism.
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

    [SerializeField] Transform rotor;
    [SerializeField] RouletteBall ball;
    // Physical pocket order (index i sits at angle i * 360/37 in the
    // rotor's local space, same convention SetupRouletteWheel builds the
    // frets with) - each one is tagged with the number painted on it, so a
    // spin result always matches what's visually under the ball instead of
    // trusting a separate formula to stay in sync with the art.
    [SerializeField] RoulettePocket[] pockets;
    [SerializeField] Vector2 ballSpeedRange = new Vector2(3.5f, 5.5f);  // m/s
    [SerializeField] float ballStartRadius = 0.35f;
    [SerializeField] float ballTrackHeight = 0.02f;

    bool _isSpinning;

    public bool IsSpinning => _isSpinning;
    public event Action<int> OnResult;

    void Awake()
    {
        if (rotor == null || ball == null)
        {
            Debug.LogWarning("RouletteWheel: rotor or ball not assigned.");
            return;
        }
        ball.Settled += HandleBallSettled;
    }

    void OnDestroy()
    {
        if (ball != null) ball.Settled -= HandleBallSettled;
    }

    public void Spin()
    {
        if (_isSpinning || rotor == null || ball == null) return;
        _isSpinning = true;

        float ballSpeed = UnityEngine.Random.Range(ballSpeedRange.x, ballSpeedRange.y);
        float startAngle = UnityEngine.Random.Range(0f, 360f);
        ball.Launch(ballStartRadius, ballSpeed, startAngle, ballTrackHeight);
    }

    void HandleBallSettled()
    {
        _isSpinning = false;
        int number = ReadPocketNumber();
        Debug.Log($"RouletteWheel: ball settled on {number}");
        OnResult?.Invoke(number);
    }

    int ReadPocketNumber()
    {
        if (pockets != null && pockets.Length > 0)
        {
            RoulettePocket closest = FindClosestPocket();
            if (closest != null) return closest.Number;
        }

        // Fallback for a wheel with no pockets wired up - assumes the
        // standard evenly-spaced layout SetupRouletteWheel builds.
        Vector3 local = rotor.InverseTransformPoint(ball.transform.position);
        float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        float pocketSize = 360f / WheelOrder.Length;
        int index = Mathf.RoundToInt(angle / pocketSize) % WheelOrder.Length;
        return WheelOrder[index];
    }

    // Whichever pocket's own position is angularly nearest the ball, rather
    // than assuming pockets sit on an exact i * (360/37) grid starting at
    // angle 0 - that only holds for the primitive-built wheel. A hand-built
    // model can have its pockets at whatever angles they actually are.
    RoulettePocket FindClosestPocket()
    {
        Vector3 ballLocal = rotor.InverseTransformPoint(ball.transform.position);
        float ballAngle = Mathf.Atan2(ballLocal.z, ballLocal.x) * Mathf.Rad2Deg;

        RoulettePocket closest = null;
        float bestDiff = float.MaxValue;
        foreach (var pocket in pockets)
        {
            if (pocket == null) continue;
            Vector3 pocketLocal = rotor.InverseTransformPoint(pocket.WorldCenter);
            float pocketAngle = Mathf.Atan2(pocketLocal.z, pocketLocal.x) * Mathf.Rad2Deg;
            float diff = Mathf.Abs(Mathf.DeltaAngle(ballAngle, pocketAngle));
            if (diff < bestDiff)
            {
                bestDiff = diff;
                closest = pocket;
            }
        }
        return closest;
    }

    [ContextMenu("Debug Spin")]
    void DebugSpin() => Spin();
}
