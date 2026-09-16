using System;
using System.Collections.Generic;
using UnityEngine;

// Orchestrates a full roulette spin: spins the rotor down from a random
// speed, launches the ball to orbit and settle via RouletteBall's physics,
// then reads off which pocket it landed in once it's at rest. No betting
// logic here - Spin() just produces a result, the same way SlotMachine owns
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

    [SerializeField] Transform rotor;
    [SerializeField] RouletteBall ball;
    // Physical pocket order (index i sits at angle i * 360/37 in the
    // rotor's local space, same convention SetupRouletteWheel builds the
    // frets with) - each one is tagged with the number painted on it, so a
    // spin result always matches what's visually under the ball instead of
    // trusting a separate formula to stay in sync with the art.
    [SerializeField] RoulettePocket[] pockets;
    [SerializeField] float rotorSpinDuration = 8f;                      // seconds to coast to a stop
    [SerializeField] Vector2 rotorSpeedRange = new Vector2(180f, 320f); // deg/sec
    [SerializeField] Vector2 ballSpeedRange = new Vector2(3.5f, 5.5f);  // m/s
    [SerializeField] float ballStartRadius = 0.35f;
    [SerializeField] float ballTrackHeight = 0.02f;

    Rigidbody _rotorRb;
    float _rotorAngularSpeed;
    float _rotorDecelRate;
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
        _rotorRb = rotor.GetComponent<Rigidbody>();
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

        _rotorAngularSpeed = UnityEngine.Random.Range(rotorSpeedRange.x, rotorSpeedRange.y);
        _rotorDecelRate = _rotorAngularSpeed / rotorSpinDuration;

        float ballSpeed = UnityEngine.Random.Range(ballSpeedRange.x, ballSpeedRange.y);
        float startAngle = UnityEngine.Random.Range(0f, 360f);
        ball.Launch(ballStartRadius, ballSpeed, startAngle, ballTrackHeight);
    }

    void FixedUpdate()
    {
        if (_rotorAngularSpeed <= 0f || _rotorRb == null) return;

        _rotorAngularSpeed = Mathf.MoveTowards(_rotorAngularSpeed, 0f, _rotorDecelRate * Time.fixedDeltaTime);
        Quaternion delta = Quaternion.Euler(0f, _rotorAngularSpeed * Time.fixedDeltaTime, 0f);
        _rotorRb.MoveRotation(_rotorRb.rotation * delta);
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
        Vector3 local = rotor.InverseTransformPoint(ball.transform.position);
        float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;

        float pocketSize = 360f / WheelOrder.Length;
        int index = Mathf.RoundToInt(angle / pocketSize) % WheelOrder.Length;

        if (pockets != null && pockets.Length == WheelOrder.Length && pockets[index] != null)
            return pockets[index].Number;

        return WheelOrder[index];
    }

    [ContextMenu("Debug Spin")]
    void DebugSpin() => Spin();
}
