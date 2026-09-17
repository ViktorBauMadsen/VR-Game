using UnityEngine;

// Follows Main Camera's yaw and horizontal position at a damped lag, so
// anything parented under it (the chip-pocket points) reads as attached to
// the player's body rather than either head-locked (swinging to eye
// height, tilting with head-look) or laggy/swimmy. Reading the camera's
// WORLD transform is enough to track the player regardless of how they
// moved to get there - it already reflects both physical room-scale
// walking and joystick locomotion, since XR Origin's move provider
// ultimately repositions the rig the camera sits under either way.
public class PlayerTorsoAnchor : MonoBehaviour
{
    [SerializeField] Transform head; // defaults to Camera.main
    [SerializeField] float waistDrop = 0.5f; // meters below head height
    [SerializeField] float positionSmoothTime = 0.2f;
    [SerializeField] float maxYawDegreesPerSecond = 270f;

    Vector3 _velocity;
    float _currentYaw;

    void Awake()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head != null) _currentYaw = head.eulerAngles.y;
    }

    // After the camera's XR pose is applied for this frame, so this reads
    // a fresh position rather than lagging a frame behind head tracking.
    void LateUpdate()
    {
        if (head == null) return;

        Vector3 targetPos = head.position - Vector3.up * waistDrop;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _velocity, positionSmoothTime);

        // No pitch/roll - pockets shouldn't tilt when the player looks up
        // or down. Capped degrees/sec instead of a fixed-fraction Slerp so
        // a fast head turn has a bounded worst-case lag rather than an
        // unbounded one.
        float targetYaw = head.eulerAngles.y;
        _currentYaw = Mathf.MoveTowardsAngle(_currentYaw, targetYaw, maxYawDegreesPerSecond * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
    }
}
