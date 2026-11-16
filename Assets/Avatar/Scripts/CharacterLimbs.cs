using UnityEngine;

// Shared posing helpers for the primitive-built characters (croupier,
// player avatar): limbs are capsules stretched between two points.
public static class CharacterLimbs
{
    // Stretches a capsule between two world points. Unity's capsule is 2
    // units tall along Y at scale 1; assumes its parent is uniformly scaled.
    public static void Stretch(Transform capsule, Vector3 a, Vector3 b, float thickness)
    {
        if (capsule == null) return;
        Vector3 d = b - a;
        float length = d.magnitude;
        if (length < 0.0001f) return;

        float parentScale = capsule.parent != null ? Mathf.Max(0.0001f, capsule.parent.lossyScale.x) : 1f;
        capsule.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / length));
        capsule.localScale = new Vector3(thickness, (length / parentScale + thickness * 0.6f) * 0.5f, thickness);
    }

    // Shoulder -> elbow -> hand, with the elbow pushed out to the side and
    // back/down (in the body's space) so the arm bends naturally.
    public static void PoseArm(Transform upper, Transform fore, Vector3 shoulder, Vector3 hand, Quaternion bodyRotation, float side, float thickness)
    {
        Vector3 elbow = (shoulder + hand) * 0.5f + bodyRotation * new Vector3(side * 0.08f, -0.09f, -0.07f);
        Stretch(upper, shoulder, elbow, thickness);
        Stretch(fore, elbow, hand, thickness);
    }

    // Turns a head (pivoting at the neck, facing its parent's +Z at rest)
    // toward a world point, within a comfortable neck range.
    public static void LookAt(Transform head, Quaternion restLocalRotation, Vector3? target, float maxYaw = 75f, float maxPitch = 30f)
    {
        Quaternion look = restLocalRotation;
        if (target.HasValue && head.parent != null)
        {
            Vector3 to = head.parent.InverseTransformPoint(target.Value) - head.localPosition;
            if (to.sqrMagnitude > 0.01f && to.magnitude < 8f)
            {
                float yaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -maxYaw, maxYaw);
                float flat = Mathf.Sqrt(to.x * to.x + to.z * to.z);
                float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg, -maxPitch, maxPitch);
                look = Quaternion.Euler(pitch, yaw, 0f);
            }
        }
        head.localRotation = Quaternion.Slerp(head.localRotation, look, 1f - Mathf.Exp(-5f * Time.deltaTime));
    }
}
