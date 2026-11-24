using UnityEngine;
using UnityEngine.Rendering;

// What a staff member (bartender, croupier, blackjack dealer) exposes so a
// rigged character can stand in for their primitive body.
public interface IStaffRig
{
    // Turned toward whatever they're looking at (forward = gaze).
    Transform StaffHead { get; }
    // 0 = right, 1 = left. Where the procedural hand is (palm centre).
    Transform StaffHand(int index);
    // How much the real hand should follow it (0 = the idle animation's hand).
    float StaffHandWeight(int index);
    // True while a line is being spoken (the bartender's moustache wiggles).
    bool StaffTalking { get; }
}

// The casino staff were built from spheres and capsules, which looked crude
// next to the rigged guests. At start each one gets an ithappy character
// standing in the same spot: the primitive body is hidden (its colliders and
// speech bubble stay, so talking and the game logic are unchanged), and the
// character is driven from it every frame - head look-at follows the old
// head's gaze, and the hands follow the old hands with IK (dealing cards,
// wiping and serving at the bar, the croupier's sweep), on top of a
// standing idle animation.
[RequireComponent(typeof(Animator))]
public class StaffLook : MonoBehaviour
{
    static readonly int PoseHash = Animator.StringToHash("Pose");
    const int PoseIdlePlain = 6;

    IStaffRig _rig;
    Animator _anim;
    readonly float[] _weight = new float[2];
    Transform _moustache;
    Quaternion _moustacheRest;

    public static StaffLook Dress(MonoBehaviour staff, GameObject prefab, RuntimeAnimatorController controller)
    {
        if (staff == null || prefab == null || !(staff is IStaffRig rig)) return null;
        Transform root = staff.transform;
        if (root.GetComponentInChildren<StaffLook>(true) != null) return null; // already dressed

        // Hide the primitive body; the speech bubble stays.
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.GetComponentInParent<WorldTextPanel>(true) == null) r.enabled = false;

        Vector3 facing = root.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.001f) facing = Vector3.forward;
        facing.Normalize();
        // The primitive arms were long, so the old body stands well back from the
        // counter / felt. Step the character forward until its resting hands are
        // a natural ~0.38 m in front of it (capped so it never walks into the furniture).
        float handsAhead = 0f;
        int handCount = 0;
        for (int i = 0; i < 2; i++)
        {
            var hand = rig.StaffHand(i);
            if (hand == null) continue;
            handsAhead += Vector3.Dot(hand.position - root.position, facing);
            handCount++;
        }
        float step = handCount > 0 ? Mathf.Clamp(handsAhead / handCount - 0.38f, 0f, 0.3f) : 0f;
        var go = Instantiate(prefab, root.position + facing * step, Quaternion.LookRotation(facing));
        go.name = staff.name + " (character)";
        // Parent only under an unscaled hierarchy so the character keeps its proportions.
        Vector3 s = root.lossyScale;
        if (Mathf.Abs(s.x - 1f) < 0.01f && Mathf.Abs(s.y - 1f) < 0.01f && Mathf.Abs(s.z - 1f) < 0.01f) go.transform.SetParent(root, true);

        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.Contains("Glasses")) r.enabled = false; // the security outfit's sunglasses
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
        SkinnedBounds.Encompass(go.transform);

        var look = go.AddComponent<StaffLook>();
        look._rig = rig;
        look._anim = go.GetComponent<Animator>();
        look._anim.runtimeAnimatorController = controller;
        look._anim.applyRootMotion = false;
        look._anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        look._anim.SetInteger(PoseHash, PoseIdlePlain);
        look._anim.Play("Idle Plain", 0, Random.value); // not everyone breathing in step
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == "Moustache") { look._moustache = t; look._moustacheRest = t.localRotation; }
        return look;
    }

    void OnAnimatorIK(int layer)
    {
        if (_rig == null || _anim == null) return;

        var head = _rig.StaffHead;
        if (head != null)
        {
            _anim.SetLookAtWeight(1f, 0.1f, 0.7f, 1f, 0.5f);
            _anim.SetLookAtPosition(head.position + head.forward * 2f);
        }
    }

    // Hands follow the old procedural hands with a plain two-bone solve on the
    // arm bones after the animation is applied. (Humanoid hand IK was tried
    // first, but it drags the hips toward low goals and the staff stood in a
    // squat behind the bar.)
    void LateUpdate()
    {
        if (_rig == null || _anim == null) return;
        if (_moustache != null)
        {
            float wiggle = _rig.StaffTalking ? Mathf.Sin(Time.time * 22f) * 5f : 0f;
            _moustache.localRotation = _moustacheRest * Quaternion.Euler(wiggle * 0.4f, 0f, wiggle);
        }
        for (int i = 0; i < 2; i++)
        {
            var hand = _rig.StaffHand(i);
            float want = hand != null ? Mathf.Clamp01(_rig.StaffHandWeight(i)) : 0f;
            _weight[i] = Mathf.MoveTowards(_weight[i], want, Time.deltaTime * 4f);
            if (_weight[i] <= 0f) continue;

            bool right = i == 0;
            var upper = _anim.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = _anim.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var wrist = _anim.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (upper == null || lower == null || wrist == null) continue;

            // The procedural hand is the palm; the wrist sits a little behind it.
            Vector3 goal = hand.position - transform.forward * 0.06f;
            goal = Vector3.Lerp(wrist.position, goal, _weight[i]);
            // Elbows hang down, slightly out and back.
            Vector3 pole = upper.position + transform.right * (right ? 0.25f : -0.25f) - transform.up * 0.45f - transform.forward * 0.15f;
            SolveArm(upper, lower, wrist, goal, pole);
        }
    }

    static void SolveArm(Transform a, Transform b, Transform c, Vector3 goal, Vector3 pole)
    {
        float ab = Vector3.Distance(a.position, b.position);
        float bc = Vector3.Distance(b.position, c.position);
        Vector3 toGoal = goal - a.position;
        // Never fully straight: a locked elbow looks robotic and pops.
        float d = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(ab - bc) + 0.01f, (ab + bc) * 0.97f);
        if (toGoal.sqrMagnitude < 1e-6f) return;
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
