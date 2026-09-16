using System;
using System.Collections.Generic;
using UnityEngine;

// Orchestrates a full roulette spin: the wheel itself never turns - only
// the ball moves. Spin() currently just gives RouletteBall a small push from
// wherever it's sitting (positioned by hand in the scene) and waits for it
// to settle, then reads off which pocket it landed in. No betting logic
// here - Spin() just produces a result, the same way SlotMachine owns
// bet/payout while SlotMachineReels only handles the visual mechanism.
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

    bool _isSpinning;

    public bool IsSpinning => _isSpinning;
    public event Action<int> OnResult;

    void Awake()
    {
        if (ball == null)
        {
            Debug.LogWarning("RouletteWheel: ball not assigned.");
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
        if (_isSpinning || ball == null) return;
        _isSpinning = true;
        ball.Push();
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
        RoulettePocket closest = FindClosestPocket();
        if (closest != null) return closest.Number;

        Debug.LogError("RouletteWheel: no pockets wired up - can't read a result.");
        return 0;
    }

    // Whichever pocket's real (bounds-derived) position is nearest the
    // ball's, by straight-line distance - simplest thing that works once
    // the ball is only ever colliding with the wheelhead's own real pocket
    // walls, and it needs no assumption about pocket spacing/angles at all.
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
