using System.Collections.Generic;
using UnityEngine;

// Generates the sloped, ring-shaped "apron" a real roulette bowl has
// between the outer wall (where the ball first spins fast) and the
// recessed pocket ring (where it needs to end up) - Unity has no built-in
// donut/torus-ramp primitive, so this builds one from a radius range and a
// height curve instead. Add this to an empty child of the wheel, position
// it at the wheel's center, tune the fields below, then use the "Rebuild
// Ramp" context menu (gear icon on this component in the Inspector) to
// generate/update the mesh. Re-running it after changing any field is
// always safe - it just replaces its own mesh.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RouletteRampBuilder : MonoBehaviour
{
    [SerializeField] float innerRadius = 0.41f;   // where this meets the pocket ring
    [SerializeField] float outerRadius = 0.80f;   // where this meets the outer wall/track
    [SerializeField] float innerHeight = 0f;      // local Y at innerRadius - match the pocket floor's height relative to this object
    [SerializeField] float outerHeight = 0.15f;   // local Y at outerRadius - match the outer track's height relative to this object
    [SerializeField] int radialSegments = 12;
    [SerializeField] int angularSegments = 96;
    // Height fraction (0 at inner radius, 1 at outer radius) along the
    // slope - the default eases in/out for a gentle bowl curve. Make it
    // steeper near t=1 (outer) and flatter near t=0 (inner) for a profile
    // closer to a real wheel's apron, or edit freely to taste.
    [SerializeField] AnimationCurve heightProfile = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] PhysicsMaterial physicsMaterial;

    [ContextMenu("Rebuild Ramp")]
    public void Rebuild()
    {
        int ringCount = radialSegments + 1;
        int vertsPerRing = angularSegments + 1; // duplicate seam vertex so UVs wrap cleanly
        var vertices = new Vector3[ringCount * vertsPerRing];
        var uvs = new Vector2[vertices.Length];

        for (int r = 0; r < ringCount; r++)
        {
            float t = r / (float)radialSegments; // 0 at inner, 1 at outer
            float radius = Mathf.Lerp(innerRadius, outerRadius, t);
            float height = Mathf.Lerp(innerHeight, outerHeight, heightProfile.Evaluate(t));

            for (int a = 0; a < vertsPerRing; a++)
            {
                float angle = (a / (float)angularSegments) * Mathf.PI * 2f;
                int i = r * vertsPerRing + a;
                vertices[i] = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
                uvs[i] = new Vector2(a / (float)angularSegments, t);
            }
        }

        var triangles = new List<int>();
        for (int r = 0; r < radialSegments; r++)
        {
            for (int a = 0; a < angularSegments; a++)
            {
                int i0 = r * vertsPerRing + a;
                int i1 = i0 + 1;
                int i2 = i0 + vertsPerRing;
                int i3 = i2 + 1;

                // Wound so the face normal points up (+Y) - the ball needs
                // to roll on top of this, not underneath it.
                triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                triangles.Add(i1); triangles.Add(i3); triangles.Add(i2);
            }
        }

        var mesh = new Mesh { name = "RouletteRamp" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        var collider = GetComponent<MeshCollider>();
        if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        if (physicsMaterial != null) collider.sharedMaterial = physicsMaterial;
    }
}
