using UnityEngine;

// Drives the rigged hitman (Resources/Mafia Hitman, built by Tools > Casino >
// Mafia > Build Mafia Assets) on top of the patrons' animator: walk / idle
// from the "Pose" parameter, the head turned to Target with look-at IK, and
// the arms bent with a two-bone solve after the animation - the right hand
// up to aim the pistol (Aim), both hands up at Target's face holding the
// hood (Reach). MafiaHit sets the fields; nothing else needs to.
[RequireComponent(typeof(Animator))]
public class MafiaHitmanRig : MonoBehaviour
{
    static readonly int PoseHash = Animator.StringToHash("Pose");
    const int PoseWalk = 1, PoseIdlePlain = 6;
    const float WalkClipSpeed = 1.1f; // metres a second the walk clip covers

    public Vector3 Target;
    public float Aim, Reach;
    public bool Walking;
    public float WalkSpeed = 1.4f;
    public Transform Gun, Hood;

    // The pistol's grip (see MafiaHit.BuildPistol): its centre in the gun's
    // space and how far it rakes back.
    public static readonly Vector3 GripCentre = new Vector3(0f, -0.025f, -0.005f);
    public const float GripRake = 12f;

    Animator _anim;
    Transform _head;
    readonly Transform[] _upper = new Transform[2], _lower = new Transform[2], _wrist = new Transform[2];
    readonly Vector3[] _palm = new Vector3[2];
    int _pose = -1;

    // Right hand: [finger][proximal, intermediate, distal], thumb first.
    readonly Transform[,] _fingers = new Transform[5, 3];
    Transform _knuckle;              // middle finger's proximal joint
    Quaternion _handFrameInverse;    // hand bone space -> (knuckles forward, index side up)
    bool _canGrip;

    static readonly HumanBodyBones[] FingerBones =
    {
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };
    // How far each finger bone bends round the grip (degrees from straight, cumulative).
    // The index finger rests on the trigger; the other three wrap the grip.
    static readonly float[,] Curl = { { 0f, 0f }, { 45f, 100f }, { 80f, 170f }, { 80f, 170f }, { 80f, 165f } };

    public Transform Head => _head != null ? _head : transform;

    public bool Init()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null || _anim.runtimeAnimatorController == null || !_anim.isHuman) return false;
        _anim.applyRootMotion = false;
        _anim.updateMode = AnimatorUpdateMode.UnscaledTime; // the run may pause time
        _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _head = _anim.GetBoneTransform(HumanBodyBones.Head);
        for (int i = 0; i < 2; i++)
        {
            bool right = i == 0;
            _upper[i] = _anim.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            _lower[i] = _anim.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            _wrist[i] = _anim.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (_upper[i] == null || _lower[i] == null || _wrist[i] == null) return false;
        }
        InitGrip();
        SetPose(PoseIdlePlain);
        _anim.Play("Idle Plain", 0, Random.value);
        return true;
    }

    // Finds the right hand's finger bones and works out which way the hand
    // bone's knuckles and index side point, so the hand can be turned to the
    // grip whatever axes the rig uses.
    void InitGrip()
    {
        _canGrip = false;
        for (int f = 0; f < 5; f++)
            for (int j = 0; j < 3; j++)
            {
                var bone = _anim.GetBoneTransform(FingerBones[f * 3 + j]);
                // This skeleton maps two bones a finger; the tip is the next child.
                if (bone == null && j == 2 && _fingers[f, 1] != null && _fingers[f, 1].childCount > 0)
                    bone = _fingers[f, 1].GetChild(0);
                _fingers[f, j] = bone;
            }
        Transform wrist = _wrist[0];
        Transform index = _fingers[1, 0], little = _fingers[4, 0];
        _knuckle = _fingers[2, 0];
        if (_knuckle == null || index == null || little == null) return;
        Vector3 forward = wrist.InverseTransformDirection(_knuckle.position - wrist.position);
        Vector3 across = wrist.InverseTransformDirection(index.position - little.position);
        if (forward.sqrMagnitude < 1e-8f || across.sqrMagnitude < 1e-8f) return;
        _handFrameInverse = Quaternion.Inverse(Quaternion.LookRotation(forward, across));
        _canGrip = true;
    }

    void SetPose(int pose)
    {
        if (pose == _pose) return;
        _pose = pose;
        _anim.SetInteger(PoseHash, pose);
    }

    void Update()
    {
        if (_anim == null) return;
        bool walking = Walking && Aim < 0.5f && Reach < 0.5f;
        SetPose(walking ? PoseWalk : PoseIdlePlain);
        _anim.speed = walking ? Mathf.Clamp(WalkSpeed / WalkClipSpeed, 0.6f, 1.6f) : 1f;
    }

    void OnAnimatorIK(int layer)
    {
        if (_anim == null) return;
        // Head and a little of the chest follow whoever he is dealing with.
        _anim.SetLookAtWeight(1f, 0.25f, 0.8f, 1f, 0.6f);
        _anim.SetLookAtPosition(Target);
    }

    void LateUpdate()
    {
        if (_anim == null) return;
        Quaternion rotation = transform.rotation;
        Vector3 handsMid = Vector3.zero;

        for (int i = 0; i < 2; i++)
        {
            bool right = i == 0;
            float side = right ? 1f : -1f;
            Vector3 shoulder = _upper[i].position;
            Vector3 rest = _wrist[i].position;
            Vector3 goal = rest;
            float weight = Reach;
            if (right)
            {
                // Arm out straight-ish toward the target's head, the wrist at the gun's grip.
                Vector3 toward = Target - shoulder;
                Vector3 aimPoint = shoulder + toward.normalized * 0.5f + Vector3.down * 0.03f;
                goal = Vector3.Lerp(rest, aimPoint, Aim);
                weight = Mathf.Max(Aim, Reach);
            }
            // The hood held open either side of their face.
            Vector3 atFace = Target + rotation * new Vector3(side * 0.15f, -0.02f, -0.16f);
            goal = Vector3.Lerp(goal, atFace, Reach);

            if (weight > 0.001f)
            {
                // Elbows down and a little out.
                Vector3 pole = shoulder + rotation * new Vector3(side * 0.3f, -0.5f, -0.1f);
                SolveArm(_upper[i], _lower[i], _wrist[i], goal, pole);
            }
            Vector3 forearm = (_wrist[i].position - _lower[i].position).normalized;
            _palm[i] = _wrist[i].position + forearm * 0.07f;
            handsMid += _palm[i] * 0.5f;
        }

        if (Gun != null && Gun.gameObject.activeSelf)
        {
            Quaternion down = rotation * Quaternion.Euler(80f, 0f, 0f);
            Vector3 from = _palm[0];
            // Twice: the second pass aims from where the gun actually ended up.
            for (int pass = 0; pass < (_canGrip ? 2 : 1); pass++)
            {
                Vector3 toward = Target - from;
                Quaternion aimed = toward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toward, Vector3.up) : down;
                Quaternion gun = Quaternion.Slerp(down, aimed, Aim);
                if (_canGrip) GripGun(gun);
                else Gun.SetPositionAndRotation(from, gun);
                from = Gun.TransformPoint(new Vector3(0f, 0.045f, 0f));
            }
        }

        if (Hood != null)
        {
            bool hood = Reach > 0.05f;
            if (Hood.gameObject.activeSelf != hood) Hood.gameObject.SetActive(hood);
            if (hood) Hood.SetPositionAndRotation(handsMid + Vector3.up * 0.06f, rotation);
        }
    }

    // Turns the right hand so its knuckle line runs along the grip, sits the
    // grip in the palm, then wraps the fingers round it (index on the trigger,
    // thumb along the far side).
    void GripGun(Quaternion gun)
    {
        Transform wrist = _wrist[0];
        Quaternion rake = gun * Quaternion.Euler(GripRake, 0f, 0f);
        Vector3 forward = rake * Vector3.forward; // wrist -> knuckles, square to the grip
        Vector3 up = rake * Vector3.up;           // little finger -> index, along the grip
        Vector3 palm = Vector3.Cross(forward, up); // out of the palm (right hand)
        wrist.rotation = Quaternion.LookRotation(forward, up) * _handFrameInverse;

        float knuckles = Vector3.Distance(wrist.position, _knuckle.position);
        Vector3 grip = wrist.position + forward * (knuckles - 0.022f) + palm * 0.03f;
        Gun.SetPositionAndRotation(grip - gun * GripCentre, gun);

        Vector3 curlAxis = -up; // turns `forward` toward the palm
        for (int f = 1; f < 5; f++)
        {
            PointBone(_fingers[f, 0], _fingers[f, 1], Quaternion.AngleAxis(Curl[f, 0], curlAxis) * forward);
            PointBone(_fingers[f, 1], _fingers[f, 2], Quaternion.AngleAxis(Curl[f, 1], curlAxis) * forward);
            if (_fingers[f, 2] != null) _fingers[f, 2].rotation = Quaternion.AngleAxis(f == 1 ? 25f : 40f, curlAxis) * _fingers[f, 2].rotation;
        }
        // Thumb across the back of the grip and along the gun's far side.
        PointBone(_fingers[0, 0], _fingers[0, 1], (forward * 0.5f + palm * 0.8f + up * 0.35f).normalized);
        PointBone(_fingers[0, 1], _fingers[0, 2], (forward + palm * 0.35f + up * 0.1f).normalized);
    }

    static void PointBone(Transform bone, Transform child, Vector3 direction)
    {
        if (bone == null || child == null) return;
        Vector3 current = child.position - bone.position;
        if (current.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.FromToRotation(current, direction) * bone.rotation;
    }

    // Same plain two-bone solve the staff use (StaffLook).
    static void SolveArm(Transform a, Transform b, Transform c, Vector3 goal, Vector3 pole)
    {
        float ab = Vector3.Distance(a.position, b.position);
        float bc = Vector3.Distance(b.position, c.position);
        Vector3 toGoal = goal - a.position;
        if (toGoal.sqrMagnitude < 1e-6f) return;
        float d = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(ab - bc) + 0.01f, (ab + bc) * 0.985f);
        Vector3 dir = toGoal.normalized;

        Vector3 bend = Vector3.ProjectOnPlane(pole - a.position, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
        bend.Normalize();

        float cosA = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 elbow = a.position + (dir * cosA + bend * sinA) * ab;

        a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, a.position + dir * d - b.position) * b.rotation;
    }
}
