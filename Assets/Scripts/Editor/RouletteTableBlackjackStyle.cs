using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Roulette > Rebuild Tables (Blackjack Style)
//
// Replaces the octagonal asset-store card table under each roulette wheel
// with a table in the same style as the blackjack tables: green felt top,
// dark leather rail, wood apron, wood pedestals on wide bases with a gold
// trim (same materials). It is round at the wheel's end and square at the
// betting layout's end (so players stand right at the mat), sized from what
// has to sit on it: the wheel and the whole betting layout, with a margin. The felt sits at the
// height of the betting layout's underside, so the layout and the wheel stay
// exactly where they are.
//
// Built as one mesh per material (4 draw calls a table). The old table is
// only switched off, not deleted; run again any time to rebuild, or
// Tools > Roulette > Restore Original Tables to go back.
public static class RouletteTableBlackjackStyle
{
    const string TableName = "Table (Blackjack Style)";
    const string MeshFolder = "Assets/Generated/RouletteTable";
    const float Margin = 0.1f;        // felt beyond the wheel
    const float SquareMargin = 0.04f; // felt beyond the layout at the square end
    const float Corner = 0.05f;       // rounding of the square end's corners
    const int ArcSegments = 16;       // per round end
    const int CornerSegments = 4;     // per square-end corner

    [MenuItem("Tools/Roulette/Rebuild Tables (Blackjack Style)")]
    public static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("RouletteTable: leave Play mode first."); return; }
        var tables = Object.FindObjectsByType<RouletteTable>(FindObjectsInactive.Exclude);
        if (tables.Length == 0) { Debug.Log("RouletteTable: no roulette tables in this scene."); return; }

        SetupAvatar.EnsureFolder("Assets/Generated");
        SetupAvatar.EnsureFolder(MeshFolder);
        var felt = SetupAvatar.Lit("Blackjack Felt", new Color(0.04f, 0.3f, 0.16f), 0.15f);
        var wood = SetupAvatar.Lit("Blackjack Wood", new Color(0.2f, 0.1f, 0.05f), 0.6f);
        var leather = SetupAvatar.Lit("Blackjack Leather", new Color(0.1f, 0.05f, 0.03f), 0.45f);
        var gold = SetupAvatar.Lit("Blackjack Gold", new Color(0.9f, 0.68f, 0.2f), 0.8f, 0.8f);

        Undo.SetCurrentGroupName("Rebuild Roulette Tables");
        int group = Undo.GetCurrentGroup();
        int built = 0;
        var notes = new List<string>();
        for (int i = 0; i < tables.Length; i++)
        {
            string note = Build(tables[i], i, felt, leather, wood, gold);
            if (note.StartsWith("ok")) built++;
            notes.Add($"'{tables[i].name}': {note}");
        }
        Undo.CollapseUndoOperations(group);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(tables[0].gameObject.scene);
        Debug.Log($"RouletteTable: rebuilt {built} of {tables.Length} table(s) in the blackjack style. Check them, then save the scene (Ctrl+S).\n" + string.Join("\n", notes));
    }

    [MenuItem("Tools/Roulette/Restore Original Tables")]
    public static void Restore()
    {
        Undo.SetCurrentGroupName("Restore Roulette Tables");
        int group = Undo.GetCurrentGroup();
        foreach (var table in Object.FindObjectsByType<RouletteTable>(FindObjectsInactive.Exclude))
        {
            var root = table.transform;
            var made = root.Find(TableName);
            if (made != null) Undo.DestroyObjectImmediate(made.gameObject);
            foreach (var old in OldTableParts(root))
            {
                Undo.RecordObject(old, "Restore Roulette Tables");
                old.SetActive(true);
            }
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("RouletteTable: original tables restored.");
    }

    static string Build(RouletteTable table, int index, Material felt, Material leather, Material wood, Material gold)
    {
        var root = table.transform;
        var toRoot = root.worldToLocalMatrix;

        var oldParts = OldTableParts(root);
        var oldMeshes = root.GetComponentsInChildren<MeshFilter>(true).Where(IsOldTable).ToList();
        var betTable = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Trim() == "RouletteBetTable");
        var wheel = table.Wheel != null ? table.Wheel.transform
            : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Trim() == "RouletteWheel");
        if (betTable == null) return "skipped - no RouletteBetTable under it";
        if (wheel == null) return "skipped - no wheel under it";

        // The betting layout is a flat sprite that lay on a squashed card
        // table; it now lies straight on the felt, a few mm above it.
        var bet = Union(betTable.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled), toRoot);
        var old = Union(oldMeshes, toRoot);
        if (bet == null) return "skipped - the betting layout has no visible renderer";
        float top = bet.Value.min.y - 0.004f;
        float floor = old != null ? old.Value.min.y : top - 0.95f;
        if (old != null && (top < floor + 0.4f || top > floor + 1.4f))
            return $"skipped - layout height looks wrong ({top - floor:0.00} m above the old table's feet)";

        // Footprint (root-local x/z points): the wheel as a circle, the
        // layout - and anything else on the wheel, like its spin button - as rectangles.
        var points = new List<Vector2>();
        Vector2 wheelCentre = ToXZ(toRoot.MultiplyPoint3x4(wheel.position));
        float wheelRadius = 0f, wheelBottom = float.MaxValue;
        foreach (var r in wheel.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled) continue;
            var b = LocalBounds(r, toRoot);
            if (b.max.y < top - 0.05f || b.min.y > top + 0.35f) continue; // under the felt / floating labels
            var c = ToXZ(b.center);
            float half = Mathf.Max(b.extents.x, b.extents.z);
            if ((c - wheelCentre).magnitude < 1.1f)
            {
                wheelRadius = Mathf.Max(wheelRadius, (c - wheelCentre).magnitude + half);
                if (b.min.y > floor + 0.3f) wheelBottom = Mathf.Min(wheelBottom, b.min.y); // not the post down to the floor
            }
            else AddCorners(points, b);
        }
        if (wheelRadius <= 0f) return "skipped - couldn't measure the wheel";
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.PI * 2f / 32f;
            points.Add(wheelCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * wheelRadius);
        }
        AddCorners(points, bet.Value);

        // Long axis along whichever way the footprint is longer.
        Vector2 min = new Vector2(points.Min(p => p.x), points.Min(p => p.y));
        Vector2 max = new Vector2(points.Max(p => p.x), points.Max(p => p.y));
        Vector2 centre = (min + max) * 0.5f;
        bool alongZ = max.y - min.y > max.x - min.x;
        Vector2 ToFrame(Vector2 p)
        {
            p -= centre;
            return alongZ ? new Vector2(p.y, -p.x) : p;
        }
        // The frame's +x end is the betting-layout end: flip it round if the layout is on the other side.
        bool flip = ToFrame(ToXZ(bet.Value.center)).x < ToFrame(wheelCentre).x;
        Vector2 Frame(Vector2 p) => flip ? -ToFrame(p) : ToFrame(p);
        var frame = points.Select(Frame).ToList();

        // Round end around the wheel; square end at the layout, kept close
        // to it so players can stand right at the mat.
        float D = frame.Max(p => Mathf.Abs(p.y)) + Margin;
        float Lsq = D * 0.5f, Lrd = D;
        foreach (var p in frame)
        {
            if (p.x > 0f) Lsq = Mathf.Max(Lsq, p.x + SquareMargin);
            else Lrd = Mathf.Max(Lrd, -p.x + Margin - Mathf.Sqrt(Mathf.Max(0f, D * D - p.y * p.y)) + D);
        }

        // ----- build
        var existing = root.Find(TableName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var go = new GameObject(TableName);
        Undo.RegisterCreatedObjectUndo(go, "Rebuild Roulette Tables");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(centre.x, 0f, centre.y);
        go.transform.localRotation = Quaternion.Euler(0f, (alongZ ? -90f : 0f) + (flip ? 180f : 0f), 0f);
        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(root.gameObject));

        var feltM = new MeshBuilder(); var railM = new MeshBuilder(); var woodM = new MeshBuilder(); var goldM = new MeshBuilder();
        // Same layer heights as the blackjack table (SetupBlackjack).
        feltM.Prism(0f, Lsq, Lrd, D, Corner, top - 0.04f, top, true, false);
        railM.Prism(0f, Lsq + 0.07f, Lrd + 0.07f, D + 0.07f, Corner + 0.07f, top - 0.08f, top - 0.01f, true, true);
        woodM.Prism(0f, Lsq + 0.02f, Lrd + 0.02f, D + 0.02f, Corner + 0.02f, top - 0.19f, top - 0.07f, false, true);

        float span = Lsq + Lrd;
        var feet = span > 1.6f ? new[] { -Lrd * 0.55f, Lsq * 0.55f } : new[] { (Lsq - Lrd) * 0.5f };
        foreach (float x in feet)
        {
            woodM.Prism(x, 0.225f, 0.225f, floor, top - 0.19f, false, false);         // pedestal
            woodM.Prism(x, 0.55f, 0.4f, floor, floor + 0.05f, true, false);            // base
            goldM.Prism(x, 0.25f, 0.25f, floor + 0.047f, floor + 0.063f, true, false); // trim
        }

        // If the wheel sat higher than the layout, a wood plinth fills the gap.
        string plinth = "";
        if (wheelBottom < float.MaxValue && wheelBottom > top + 0.01f)
        {
            var w = Frame(wheelCentre);
            float pr = wheelRadius * 0.85f;
            woodM.PrismAt(w, pr, top, wheelBottom + 0.002f);
            goldM.PrismAt(w, pr + 0.012f, top, top + 0.015f);
            plinth = $", wheel on a {(wheelBottom - top) * 100f:0} cm plinth";
        }

        string prefix = $"{MeshFolder}/{Sanitize(root.name)} {index}";
        Part(go.transform, "Felt", feltM, felt, prefix);
        Part(go.transform, "Rail", railM, leather, prefix);
        Part(go.transform, "Wood", woodM, wood, prefix);
        Part(go.transform, "Gold Trim", goldM, gold, prefix);

        // Solid top and posts, like the blackjack table.
        var solid = go.AddComponent<BoxCollider>();
        solid.center = new Vector3((Lsq - Lrd) * 0.5f, top - 0.06f, 0f);
        solid.size = new Vector3(span, 0.12f, 2f * D);
        foreach (float x in feet)
        {
            var post = go.AddComponent<BoxCollider>();
            post.center = new Vector3(x, (floor + top) * 0.5f, 0f);
            post.size = new Vector3(0.4f, top - floor, 0.4f);
        }

        foreach (var part in oldParts)
        {
            Undo.RecordObject(part, "Rebuild Roulette Tables");
            part.SetActive(false);
        }
        return $"ok - {span:0.00} x {2f * D:0.00} m, felt {top - floor:0.00} m high{plinth}, {oldParts.Count} old part(s) switched off";
    }

    static bool IsOldTable(MeshFilter mf) => mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("CardTable");

    // The direct children of the table root that hold the old card table.
    static List<GameObject> OldTableParts(Transform root)
    {
        var parts = new HashSet<GameObject>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!IsOldTable(mf)) continue;
            var t = mf.transform;
            while (t.parent != null && t.parent != root) t = t.parent;
            if (t.parent == root) parts.Add(t.gameObject);
        }
        return parts.ToList();
    }

    static void Part(Transform parent, string name, MeshBuilder builder, Material mat, string prefix)
    {
        var mesh = builder.ToMesh($"Roulette Table {name}");
        string path = $"{prefix} {name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(parent.gameObject));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Bounds? LocalBounds(MeshFilter mf, Matrix4x4 toRoot)
    {
        if (mf.sharedMesh == null) return null;
        var b = mf.sharedMesh.bounds;
        var m = toRoot * mf.transform.localToWorldMatrix;
        Bounds? result = null;
        for (int i = 0; i < 8; i++)
        {
            var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var p = m.MultiplyPoint3x4(corner);
            if (result == null) result = new Bounds(p, Vector3.zero);
            else { var r = result.Value; r.Encapsulate(p); result = r; }
        }
        return result;
    }

    static Bounds LocalBounds(Renderer r, Matrix4x4 toRoot)
    {
        var b = r.bounds;
        var result = new Bounds(toRoot.MultiplyPoint3x4(b.min), Vector3.zero);
        for (int i = 0; i < 8; i++)
            result.Encapsulate(toRoot.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return result;
    }

    static Bounds? Union(IEnumerable<Renderer> renderers, Matrix4x4 toRoot)
    {
        Bounds? all = null;
        foreach (var r in renderers)
        {
            var b = LocalBounds(r, toRoot);
            if (all == null) all = b;
            else { var a = all.Value; a.Encapsulate(b); all = a; }
        }
        return all;
    }

    static Bounds? Union(IEnumerable<MeshFilter> filters, Matrix4x4 toRoot)
    {
        Bounds? all = null;
        foreach (var mf in filters)
        {
            var b = LocalBounds(mf, toRoot);
            if (b == null) continue;
            if (all == null) all = b;
            else { var a = all.Value; a.Encapsulate(b.Value); all = a; }
        }
        return all;
    }

    static Vector2 ToXZ(Vector3 v) => new Vector2(v.x, v.z);

    static void AddCorners(List<Vector2> points, Bounds b)
    {
        points.Add(new Vector2(b.min.x, b.min.z));
        points.Add(new Vector2(b.min.x, b.max.z));
        points.Add(new Vector2(b.max.x, b.min.z));
        points.Add(new Vector2(b.max.x, b.max.z));
    }

    static string Sanitize(string s)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    // Extruded outlines, smooth-shaded around the sides: "stadiums" (a
    // rectangle with round ends; a circle when the half-length equals the
    // half-depth), or with the +x end square (small rounded corners).
    sealed class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        // Outline around (cx, 0), long axis x: reaching Lp toward +x and Lm
        // toward -x, half-depth D. The -x end is round; the +x end too, or
        // square with corners of radius rc when rc >= 0.
        // Counter-clockwise seen from above, with outward normals.
        static List<(Vector2 p, Vector2 normal)> Outline(float cx, float Lp, float Lm, float D, float rc)
        {
            var list = new List<(Vector2, Vector2)>();
            void Arc(Vector2 c, float r, float from, float to, int segments)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float a = Mathf.Lerp(from, to, (float)i / segments) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    list.Add((c + dir * r, dir));
                }
            }
            if (rc < 0f) Arc(new Vector2(cx + Mathf.Max(0f, Lp - D), 0f), D, -90f, 90f, ArcSegments);
            else
            {
                rc = Mathf.Min(rc, D);
                Arc(new Vector2(cx + Lp - rc, -(D - rc)), rc, -90f, 0f, CornerSegments);
                Arc(new Vector2(cx + Lp - rc, D - rc), rc, 0f, 90f, CornerSegments);
            }
            Arc(new Vector2(cx - Mathf.Max(0f, Lm - D), 0f), D, 90f, 270f, ArcSegments);
            return list;
        }

        public void Prism(float cx, float L, float D, float y0, float y1, bool topCap, bool bottomCap)
            => Prism(cx, L, L, D, -1f, y0, y1, topCap, bottomCap);

        public void Prism(float cx, float Lp, float Lm, float D, float rc, float y0, float y1, bool topCap, bool bottomCap)
        {
            var ring = Outline(cx, Lp, Lm, D, rc);
            cx += (Lp - Lm) * 0.5f; // cap fan centre: the middle of the shape
            int count = ring.Count;

            // Sides: quads between consecutive outline points (wrapping round).
            float u = 0f;
            int side = v.Count;
            for (int i = 0; i <= count; i++)
            {
                var (p, nr) = ring[i % count];
                if (i > 0) u += (p - ring[i - 1].p).magnitude;
                v.Add(new Vector3(p.x, y1, p.y)); n.Add(new Vector3(nr.x, 0f, nr.y)); uv.Add(new Vector2(u, y1));
                v.Add(new Vector3(p.x, y0, p.y)); n.Add(new Vector3(nr.x, 0f, nr.y)); uv.Add(new Vector2(u, y0));
            }
            for (int i = 0; i < count; i++)
            {
                int t0 = side + i * 2, b0 = t0 + 1, t1 = t0 + 2, b1 = t0 + 3;
                t.Add(t0); t.Add(t1); t.Add(b1);
                t.Add(t0); t.Add(b1); t.Add(b0);
            }

            if (topCap) Cap(ring, cx, y1, true);
            if (bottomCap) Cap(ring, cx, y0, false);
        }

        // A round prism (top cap only) centred anywhere on the floor plan.
        public void PrismAt(Vector2 centre, float radius, float y0, float y1)
        {
            int start = v.Count;
            Prism(0f, radius, radius, y0, y1, true, false);
            for (int i = start; i < v.Count; i++) v[i] += new Vector3(centre.x, 0f, centre.y);
        }

        void Cap(List<(Vector2 p, Vector2 normal)> ring, float cx, float y, bool up)
        {
            int c = v.Count;
            var normal = up ? Vector3.up : Vector3.down;
            v.Add(new Vector3(cx, y, 0f)); n.Add(normal); uv.Add(new Vector2(cx, 0f));
            foreach (var (p, _) in ring) { v.Add(new Vector3(p.x, y, p.y)); n.Add(normal); uv.Add(p); }
            int count = ring.Count;
            for (int i = 0; i < count; i++)
            {
                int a = c + 1 + i, b = c + 1 + (i + 1) % count;
                if (up) { t.Add(c); t.Add(b); t.Add(a); }
                else { t.Add(c); t.Add(a); t.Add(b); }
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
