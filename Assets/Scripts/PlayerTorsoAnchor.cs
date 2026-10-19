using UnityEngine;
using UnityEngine.InputSystem;

// Follows Main Camera's yaw and horizontal position at a damped lag, so
// anything parented under it (the chip-pocket points) reads as attached to
// the player's body rather than either head-locked (swinging to eye
// height, tilting with head-look) or laggy/swimmy. Reading the camera's
// WORLD transform is enough to track the player regardless of how they
// moved to get there - it already reflects both physical room-scale
// walking and joystick locomotion, since XR Origin's move provider
// ultimately repositions the rig the camera sits under either way.
//
// The pockets can be hidden/shown with a button (B on the right Quest
// controller by default) so they don't block the view of a table when the
// player looks down at it. Only the children are toggled - the anchor
// itself keeps following, so the pockets reappear in the right place.
public class PlayerTorsoAnchor : MonoBehaviour
{
    [SerializeField] Transform head; // defaults to Camera.main
    [SerializeField] float waistDrop = 0.75f; // meters below head height
    [SerializeField] float positionSmoothTime = 0.2f;
    [SerializeField] float maxYawDegreesPerSecond = 270f;

    [Tooltip("Input System binding that toggles the pockets on/off.")]
    [SerializeField] string toggleBinding = "<XRController>{RightHand}/secondaryButton";
    [SerializeField] bool startVisible = true;

    Vector3 _velocity;
    float _currentYaw;
    InputAction _toggleAction;
    bool _visible;

    void Awake()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head != null) _currentYaw = head.eulerAngles.y;

        _toggleAction = new InputAction("Toggle Chip Pockets", InputActionType.Button, toggleBinding);
        _toggleAction.performed += _ => SetPocketsVisible(!_visible);
        SetPocketsVisible(startVisible);
    }

    void OnEnable() => _toggleAction.Enable();
    void OnDisable() => _toggleAction.Disable();
    void OnDestroy() => _toggleAction.Dispose();

    public void SetPocketsVisible(bool visible)
    {
        _visible = visible;
        foreach (Transform child in transform)
            child.gameObject.SetActive(visible);
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
