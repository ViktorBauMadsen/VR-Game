using UnityEngine;

// The old gentleman's walking cane. His walk animation was made for one (he
// grips it in his right hand and plants it with each step), but the
// character pack doesn't include it - so this builds one and, every frame
// after the animation has posed him, stands it with its tip on the floor a
// little ahead of and beside his hand and its crook handle in his grip.
public class PatronCane : MonoBehaviour
{
    const float ShaftRadius = 0.011f, CrookRadius = 0.04f;

    Transform _hand, _cane, _shaft, _crook;
    Material _wood, _tip;

    public void Init(Material woodSource, Material tipSource)
    {
        _hand = FindBone(transform, "RightHandProp") ?? FindBone(transform, "RightHand");
        if (_hand == null) { enabled = false; return; }

        _wood = new Material(woodSource) { color = new Color(0.22f, 0.12f, 0.06f) };
        _tip = new Material(tipSource != null ? tipSource : woodSource) { color = new Color(0.04f, 0.04f, 0.04f) };

        _cane = new GameObject("Cane").transform;
        _cane.SetParent(transform, false);
        _shaft = Part(PrimitiveType.Cylinder, _cane, _wood);
        Part(PrimitiveType.Cylinder, _cane, _tip).localScale = new Vector3(0.026f, 0.015f, 0.026f); // rubber foot, at the floor

        // The crook: a half circle over the top of the shaft, curling forward and down.
        _crook = new GameObject("Crook").transform;
        _crook.SetParent(_cane, false);
        const int segments = 6;
        Vector3 Point(int i)
        {
            float a = Mathf.PI * Mathf.Min(i, segments) / segments;
            var p = new Vector3(0f, Mathf.Sin(a) * CrookRadius, CrookRadius - Mathf.Cos(a) * CrookRadius);
            if (i > segments) p.y -= 0.03f; // short tail hanging down at the end
            return p;
        }
        for (int i = 0; i <= segments; i++)
        {
            Vector3 a = Point(i), b = Point(i + 1);
            var seg = Part(PrimitiveType.Cylinder, _crook, _wood);
            seg.localPosition = (a + b) * 0.5f;
            seg.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            seg.localScale = new Vector3(ShaftRadius * 2.1f, (b - a).magnitude * 0.5f + ShaftRadius, ShaftRadius * 2.1f);
        }
    }

    void LateUpdate()
    {
        if (_hand == null || _cane == null) return;
        Vector3 forward = transform.forward, right = transform.right;
        Vector3 grip = _hand.position;
        Vector3 tip = new Vector3(grip.x, transform.position.y, grip.z) + forward * 0.1f + right * 0.05f;
        Vector3 axis = grip - tip;
        float length = Mathf.Clamp(axis.magnitude - CrookRadius * 0.6f, 0.4f, 1.1f);
        Vector3 up = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
        Vector3 face = Vector3.ProjectOnPlane(forward, up);
        if (face.sqrMagnitude < 0.0001f) face = Vector3.ProjectOnPlane(Vector3.forward, up);

        _cane.SetPositionAndRotation(tip, Quaternion.LookRotation(face.normalized, up));
        _cane.localScale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z);
        _shaft.localPosition = new Vector3(0f, length * 0.5f, 0f);
        _shaft.localScale = new Vector3(ShaftRadius * 2f, length * 0.5f, ShaftRadius * 2f);
        _crook.localPosition = new Vector3(0f, length, 0f);
    }

    static Transform Part(PrimitiveType type, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    static Transform FindBone(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    void OnDestroy()
    {
        if (_wood != null) Destroy(_wood);
        if (_tip != null) Destroy(_tip);
    }
}
