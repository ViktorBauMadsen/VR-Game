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
// The fingers are posed procedurally, with no animation clips (FingerRig):
// each joint's bend axis is worked out from the skeleton itself
// (perpendicular to the bone and to the palm), so it doesn't depend on how
// the model's bones happen to be oriented.
//
// Other players see a copy of these same hands (CreateRemoteCopy, used by
// the multiplayer avatars), posed from the finger curls they send.
public class HandPoser : MonoBehaviour
{
    public enum Side { Left, Right }

    [SerializeField] Side side = Side.Right;
    [Tooltip("Degrees each finger joint bends at full curl (knuckle, middle, tip).")]
    [SerializeField] Vector3 fingerAngles = new Vector3(65f, 90f, 60f);
    [SerializeField] Vector3 thumbAngles = new Vector3(22f, 30f, 50f);
    [SerializeField, Range(0f, 0.5f)] float relaxedCurl = 0.08f;
    [SerializeField] float curlSpeed = 18f;

    FingerRig _rig;
    InputAction _grip, _trigger, _triggerTouch, _thumbTouch;
    bool _attached;
    float _attachRetry;

    public Side HandSide => side;
    public Transform Wrist => _rig != null ? _rig.Wrist : null;
    // Current curls (0 open .. 1 closed), sent to the other players.
    public float IndexCurl => _rig != null ? _rig.Curl(0) : 0f;
    public float GripCurl => _rig != null ? _rig.Curl(1) : 0f;
    public float ThumbCurl => _rig != null ? _rig.Curl(4) : 0f;

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

        _rig = new FingerRig(transform, side == Side.Right, fingerAngles, thumbAngles);
        if (_rig.Wrist == null) Debug.LogWarning($"HandPoser: couldn't find the hand bones under '{name}' - fingers won't move.");
    }

    void OnEnable() { _grip?.Enable(); _trigger?.Enable(); _triggerTouch?.Enable(); _thumbTouch?.Enable(); }
    void OnDisable() { _grip?.Disable(); _trigger?.Disable(); _triggerTouch?.Disable(); _thumbTouch?.Disable(); }
    void OnDestroy() { _grip?.Dispose(); _trigger?.Dispose(); _triggerTouch?.Dispose(); _thumbTouch?.Dispose(); }

    void Update()
    {
        if (!_attached) TryAttach();

        float grip = _grip.ReadValue<float>();
        float trigger = _trigger.ReadValue<float>();
        float index = Mathf.Max(trigger, _triggerTouch.IsPressed() ? 0.15f : 0f);
        float thumb = _thumbTouch.IsPressed() ? 1f : Mathf.Max(grip, trigger) * 0.5f;
        _rig.Pose(index, grip, thumb, relaxedCurl, curlSpeed, Time.deltaTime);
    }

    // A copy of this hand model for another player's avatar: same mesh, same
    // skeleton, but no input and no HandPoser (so nothing mistakes it for
    // the local player's hand). Its fingers start from the rest pose.
    public RemoteHand CreateRemoteCopy(Transform parent)
    {
        if (_rig == null || _rig.Wrist == null) return null;
        bool wasActive = gameObject.activeSelf;
        gameObject.SetActive(false); // copied switched off, so nothing on it starts up
        var copy = Instantiate(gameObject, parent, false);
        gameObject.SetActive(wasActive);
        copy.name = (side == Side.Left ? "Left" : "Right") + " Hand Model";

        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
        foreach (var joint in copy.GetComponentsInChildren<UnityEngine.Joint>(true)) DestroyImmediate(joint);
        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
        foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;

        _rig.ResetPose(copy.transform);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = transform.lossyScale;

        var remote = copy.AddComponent<RemoteHand>();
        remote.Init(new FingerRig(copy.transform, side == Side.Right, fingerAngles, thumbAngles), relaxedCurl, curlSpeed);
        copy.SetActive(true);
        return remote;
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

// The finger joints of one rigged hand and how far each finger is curled.
public sealed class FingerRig
{
    sealed class Joint
    {
        public Transform Bone;
        public Quaternion Rest;
        public Vector3 Axis; // bend axis in the bone's local space
        public float MaxAngle;
    }

    static readonly string[] FingerNames = { "index", "middle", "ring", "pinky", "thumb" };

    readonly Joint[][] _fingers = new Joint[5][];
    readonly float[] _curl = new float[5];

    public Transform Wrist { get; }

    public float Curl(int finger) => _curl[finger];

    // Call with the hand in its rest (open) pose.
    public FingerRig(Transform root, bool right, Vector3 fingerAngles, Vector3 thumbAngles)
    {
        var wrist = HandPoser.FindBone(root, "_hand");
        var index1 = HandPoser.FindBone(root, "index1");
        var middle1 = HandPoser.FindBone(root, "middle1");
        var pinky1 = HandPoser.FindBone(root, "pinky1");
        if (wrist == null || index1 == null || middle1 == null || pinky1 == null) return;
        Wrist = wrist;

        // Which way the palm faces, from the knuckle layout. The cross
        // product comes out on the back of a right hand, the palm of a left.
        Vector3 n = Vector3.Cross(index1.position - wrist.position, pinky1.position - wrist.position).normalized;
        Vector3 palmOut = right ? -n : n;
        // The thumb folds across the palm, toward the base of the fingers.
        Vector3 thumbTarget = (index1.position + middle1.position) * 0.5f + palmOut * 0.03f;

        for (int f = 0; f < 5; f++)
        {
            bool thumb = f == 4;
            var joints = new Joint[3];
            Vector3 lastDirection = Vector3.zero;
            for (int j = 0; j < 3; j++)
            {
                var bone = HandPoser.FindBone(root, FingerNames[f] + (j + 1));
                if (bone == null) { joints = null; break; }
                var next = j < 2 ? HandPoser.FindBone(root, FingerNames[f] + (j + 2)) : null;
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

    public void Pose(float index, float grip, float thumb, float relaxed, float speed, float dt)
    {
        Set(0, index, relaxed, speed, dt);
        Set(1, grip, relaxed, speed, dt);
        Set(2, grip, relaxed, speed, dt);
        Set(3, grip, relaxed, speed, dt);
        Set(4, thumb, relaxed, speed, dt);
    }

    void Set(int finger, float target, float relaxed, float speed, float dt)
    {
        var joints = _fingers[finger];
        if (joints == null) return;
        target = Mathf.Max(target, relaxed);
        _curl[finger] = Mathf.Lerp(_curl[finger], target, 1f - Mathf.Exp(-speed * dt));
        foreach (var joint in joints)
            joint.Bone.localRotation = joint.Rest * Quaternion.AngleAxis(_curl[finger] * joint.MaxAngle, joint.Axis);
    }

    // Puts a copy of this hand model's finger bones back in the rest pose.
    public void ResetPose(Transform copyRoot)
    {
        var bones = copyRoot.GetComponentsInChildren<Transform>(true);
        foreach (var joints in _fingers)
        {
            if (joints == null) continue;
            foreach (var joint in joints)
                foreach (var b in bones)
                    if (b.name == joint.Bone.name) { b.localRotation = joint.Rest; break; }
        }
    }
}
