using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

// Drives one of the rigged hand models (LeftHand.fbx / RightHand.fbx) from
// its controller, like the hands in the Quest home menu:
//   grip    -> middle, ring and pinky curl
//   trigger -> index finger curls
//   thumb   -> folds down while it rests on a button/thumbstick (or grips)
// At startup the hand parents itself under the rig's "Left Controller" /
// "Right Controller", so it tracks the controller exactly.
//
// The fingers are posed procedurally, with no animation clips: each joint's
// bend axis is worked out from the skeleton itself (perpendicular to the
// bone and to the palm), so it doesn't depend on how the model's bones
// happen to be oriented.
public class HandPoser : MonoBehaviour
{
    public enum Side { Left, Right }

    [SerializeField] Side side = Side.Right;
    [Tooltip("Degrees each finger joint bends at full curl (knuckle, middle, tip).")]
    [SerializeField] Vector3 fingerAngles = new Vector3(65f, 90f, 60f);
    [SerializeField] Vector3 thumbAngles = new Vector3(22f, 30f, 50f);
    [SerializeField, Range(0f, 0.5f)] float relaxedCurl = 0.08f;
    [SerializeField] float curlSpeed = 18f;

    class Joint
    {
        public Transform Bone;
        public Quaternion Rest;
        public Vector3 Axis; // bend axis in the bone's local space
        public float MaxAngle;
    }

    static readonly string[] FingerNames = { "index", "middle", "ring", "pinky", "thumb" };

    readonly Joint[][] _fingers = new Joint[5][];
    readonly float[] _curl = new float[5];
    InputAction _grip, _trigger, _triggerTouch, _thumbTouch;
    bool _attached;
    float _attachRetry;

    public Side HandSide => side;
    public Transform Wrist { get; private set; }

    public static Transform FindBone(Transform root, string suffix)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith(suffix)) return t;
        return null;
    }

    void Awake()
    {
        string hand = side == Side.Left ? "{LeftHand}" : "{RightHand}";
        _grip = new InputAction("Grip", InputActionType.Value, $"<XRController>{hand}/grip");
        _trigger = new InputAction("Trigger", InputActionType.Value, $"<XRController>{hand}/trigger");
        _triggerTouch = new InputAction("Trigger Touch", InputActionType.Button, $"<XRController>{hand}/triggerTouched");
        _thumbTouch = new InputAction("Thumb Touch", InputActionType.Button);
        _thumbTouch.AddBinding($"<XRController>{hand}/thumbstickTouched");
        _thumbTouch.AddBinding($"<XRController>{hand}/primaryTouched");
        _thumbTouch.AddBinding($"<XRController>{hand}/secondaryTouched");

        BuildJoints();
    }

    void OnEnable() { _grip.Enable(); _trigger.Enable(); _triggerTouch.Enable(); _thumbTouch.Enable(); }
    void OnDisable() { _grip.Disable(); _trigger.Disable(); _triggerTouch.Disable(); _thumbTouch.Disable(); }
    void OnDestroy() { _grip.Dispose(); _trigger.Dispose(); _triggerTouch.Dispose(); _thumbTouch.Dispose(); }

    void BuildJoints()
    {
        Wrist = FindBone(transform, "_hand");
        var index1 = FindBone(transform, "index1");
        var middle1 = FindBone(transform, "middle1");
        var pinky1 = FindBone(transform, "pinky1");
        if (Wrist == null || index1 == null || middle1 == null || pinky1 == null)
        {
            Debug.LogWarning($"HandPoser: couldn't find the hand bones under '{name}' - fingers won't move.");
            return;
        }

        // Which way the palm faces, from the knuckle layout. The cross
        // product comes out on the back of a right hand, the palm of a left.
        Vector3 n = Vector3.Cross(index1.position - Wrist.position, pinky1.position - Wrist.position).normalized;
        Vector3 palmOut = side == Side.Right ? -n : n;
        // The thumb folds across the palm, toward the base of the fingers.
        Vector3 thumbTarget = (index1.position + middle1.position) * 0.5f + palmOut * 0.03f;

        for (int f = 0; f < 5; f++)
        {
            bool thumb = f == 4;
            var joints = new Joint[3];
            Vector3 lastDirection = Vector3.zero;
            for (int j = 0; j < 3; j++)
            {
                var bone = FindBone(transform, FingerNames[f] + (j + 1));
                if (bone == null) { joints = null; break; }
                var next = j < 2 ? FindBone(transform, FingerNames[f] + (j + 2)) : null;
                Vector3 direction = next != null ? (next.position - bone.position).normalized : lastDirection;
                lastDirection = direction;

                Vector3 toward = thumb ? (thumbTarget - bone.position).normalized : palmOut;
                Vector3 axis = Vector3.Cross(direction, toward);
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(direction, palmOut);

                joints[j] = new Joint
                {
                    Bone = bone,
                    Rest = bone.localRotation,
                    Axis = Quaternion.Inverse(bone.rotation) * axis.normalized,
                    MaxAngle = (thumb ? thumbAngles : fingerAngles)[j],
                };
            }
            _fingers[f] = joints;
        }
    }

    void Update()
    {
        if (!_attached) TryAttach();

        float grip = _grip.ReadValue<float>();
        float trigger = _trigger.ReadValue<float>();
        float index = Mathf.Max(trigger, _triggerTouch.IsPressed() ? 0.15f : 0f);
        float thumb = _thumbTouch.IsPressed() ? 1f : Mathf.Max(grip, trigger) * 0.5f;

        SetCurl(0, index);
        SetCurl(1, grip);
        SetCurl(2, grip);
        SetCurl(3, grip);
        SetCurl(4, thumb);
    }

    void SetCurl(int finger, float target)
    {
        var joints = _fingers[finger];
        if (joints == null) return;

        target = Mathf.Max(target, relaxedCurl);
        _curl[finger] = Mathf.Lerp(_curl[finger], target, 1f - Mathf.Exp(-curlSpeed * Time.deltaTime));
        foreach (var joint in joints)
            joint.Bone.localRotation = joint.Rest * Quaternion.AngleAxis(_curl[finger] * joint.MaxAngle, joint.Axis);
    }

    // The rig is found by name, the same way CasinoMultiplayer does it. The
    // multiplayer bootstrap also draws simple blocky "Your hands" for the
    // local player - those are hidden, since these hands replace them.
    void TryAttach()
    {
        if (Time.unscaledTime < _attachRetry) return;
        _attachRetry = Time.unscaledTime + 0.5f;

        var rig = FindAnyObjectByType<XROrigin>();
        if (rig == null) return;
        string controllerName = side == Side.Left ? "Left Controller" : "Right Controller";
        foreach (var t in rig.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != controllerName) continue;
            Vector3 scale = transform.localScale;
            transform.SetParent(t, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = scale;
            _attached = true;
            break;
        }
    }

    void LateUpdate()
    {
        if (_blockHandsHidden || Time.frameCount % 30 != 0) return;
        var blocky = GameObject.Find("Your hands");
        if (blocky == null) return;
        foreach (var r in blocky.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        _blockHandsHidden = true;
    }

    bool _blockHandsHidden;
}
