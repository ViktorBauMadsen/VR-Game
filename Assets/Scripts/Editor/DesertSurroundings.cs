using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Rng = System.Random;

// Tools > Casino > Desert Surroundings
//
// Leaves everything "Decorate Exterior" built around the building (facades,
// canopy, forecourt, fountain, palms, hedges, lamp posts, walls) and turns the
// land around it into desert: the lawn becomes sand, a sand plain runs out to
// the horizon, and dunes, distant mesas, saguaro / barrel / prickly-pear cacti,
// dead bushes and trees, tumbleweeds, rocks and sun-bleached bones (cattle
// skulls, rib cages) are scattered around. A warm haze hides the horizon.
//
// All scenery is merged into a handful of static meshes per material (saved in
// Assets/Materials/InteriorDecor/Desert) so it costs only a few draw calls on
// Quest. Re-running rebuilds it identically (fixed random seed).
//
// Tools > Casino > Rename Casino (The Grand Mirage) puts the new name on the
// marquee above the doors, with "CASINO" kept underneath.
public static partial class DecorateInterior
{
    const string DesertRootName = "Desert Surroundings";
    const string DesertMeshFolder = Folder + "/Desert";
    // The area "Decorate Exterior" landscaped (building, forecourt, palms, walls): nothing is scattered in here.
    const float KeepX0 = -19f, KeepX1 = 13f, KeepZ0 = -10f, KeepZ1 = 51f;
    const float PlainEdge = 420f;
    // Players can roam the desert out to an old ranch fence this far from the casino; invisible walls stop them there.
    const float BorderRadius = 110f, BorderWallHeight = 8f;
    const int BorderPosts = 170, BorderWalls = 72;
    static readonly Vector3 DesertCenter = new Vector3(-2.9f, 0f, 20f);

    static DesertMesh dm;
    static DesertMats dmat;
    static List<Bounds> desertBlockers;
    static List<Vector3> desertTaken; // x, z = position, y = radius

    [MenuItem("Tools/Casino/Desert Surroundings")]
    public static void BuildDesert()
    {
        var old = GameObject.Find(DesertRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        EnsureFolder(Folder);
        EnsureFolder(TexFolder);
        EnsureFolder(MeshFolder);
        if (AssetDatabase.IsValidFolder(DesertMeshFolder)) AssetDatabase.DeleteAsset(DesertMeshFolder);
        EnsureFolder(DesertMeshFolder);

        // Same material asset the exterior's lawn quads use, so the lawn turns to sand in place.
        var sand = MakeMat("Ext Lawn", Color.white, 0.08f, 0f, Textures.Sand());
        dmat = new DesertMats(sand);
        dm = new DesertMesh();

        var go = new GameObject(DesertRootName);
        Undo.RegisterCreatedObjectUndo(go, "Desert Surroundings");
        root = go.transform;

        CollectDesertBlockers();
        BuildSandPlain();
        BuildBorder();
        ScatterDesert();
        go.AddComponent<PlayerFallGuard>();
        dm.Save(Group("Scenery"));

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        // Desert haze: hides where the plain ends and makes the mesas read as far away.
        bool evening = RenderSettings.skybox != null && RenderSettings.skybox.name.Contains("Evening");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 110f;
        RenderSettings.fogEndDistance = 430f;
        RenderSettings.fogColor = evening ? new Color(0.6f, 0.4f, 0.33f) : new Color(0.86f, 0.76f, 0.62f);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"DesertSurroundings: built '{DesertRootName}' ({dm.Count} merged meshes). The lawn is sand now; the exterior decor is untouched. Save the scene to keep it.");
        dm = null;
    }

    [MenuItem("Tools/Casino/Remove Desert Surroundings")]
    static void RemoveDesert()
    {
        var old = GameObject.Find(DesertRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        RenderSettings.fog = false;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Casino/Rename Casino (The Grand Mirage)")]
    public static void RenameCasino()
    {
        int n = 0;
        foreach (var tmp in UnityEngine.Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include))
        {
            var p = tmp.transform.parent;
            if (tmp.name != "Sign Text" || p == null || p.name != "Marquee Sign") continue;
            Undo.RecordObject(tmp, "Rename Casino");
            Undo.RecordObject(tmp.rectTransform, "Rename Casino");
            ApplySignText(tmp);
            EditorUtility.SetDirty(tmp);
            n++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (n == 0) Debug.LogWarning("RenameCasino: no marquee sign found - run Tools > Casino > Decorate Exterior (it now builds the sign with the new name).");
        else Debug.Log($"RenameCasino: marquee now reads \"{CasinoName}\" with CASINO underneath. Save the scene to keep it.");
    }

    const string CasinoName = "THE GRAND MIRAGE";

    static void ApplySignText(TextMeshPro tmp)
    {
        tmp.richText = true;
        tmp.text = CasinoName + "\n<size=58%><cspace=0.5em>CASINO</cspace></size>";
        tmp.characterSpacing = 6f;
        tmp.lineSpacing = -18f;
        tmp.rectTransform.sizeDelta = new Vector2(4.2f, 0.86f);
    }

    // --------------------------------------------------------------- ground

    static void CollectDesertBlockers()
    {
        desertBlockers = new List<Bounds>();
        desertTaken = new List<Vector3>();
        var skip = new HashSet<string> { DesertRootName, "XR Origin (XR Rig)", "Player Avatar", "Player Hands" };
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (skip.Contains(r.transform.root.name)) continue;
            var b = r.bounds;
            if (b.size.x > 30f || b.size.z > 30f) continue; // ground planes
            desertBlockers.Add(b);
        }
    }

    static void BuildSandPlain()
    {
        // The exterior's lawn covers x/z -49.5..50.5; the plain rings it without overlapping.
        var g = Group("Sand Plain");
        const float a = -49.5f, b = 50.5f, R = PlainEdge;
        SandFlat(g, "Sand S", -R, R, -R, a);
        SandFlat(g, "Sand N", -R, R, b, R);
        SandFlat(g, "Sand W", -R, a, a, b);
        SandFlat(g, "Sand E", b, R, a, b);
    }

    static void SandFlat(Transform g, string name, float x0, float x1, float z0, float z1)
    {
        Flat(g, name, dmat.Sand, x0, x1, z0, z1, LawnY, 8f);
        var t = g.GetChild(g.childCount - 1);
        t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var box = t.gameObject.AddComponent<BoxCollider>();
        // Quad faces down its local z: the box sits just below the surface.
        box.center = new Vector3(0f, 0f, 0.25f);
        box.size = new Vector3(x1 - x0, z1 - z0, 0.5f);
    }

    // --------------------------------------------------------------- border

    static void BuildBorder()
    {
        var g = Group("Border");

        // One ground collider under the whole roaming area (top at ground level),
        // so there's nowhere inside the fence to fall through.
        var ground = new GameObject("Ground Collider");
        ground.transform.SetParent(g, false);
        ground.transform.position = new Vector3(DesertCenter.x, GroundY - 0.5f, DesertCenter.z);
        ground.AddComponent<BoxCollider>().size = new Vector3(BorderRadius * 2f + 40f, 1f, BorderRadius * 2f + 40f);

        // Invisible walls along the fence line.
        var walls = Group("Walls", g);
        float seg = 2f * Mathf.PI * BorderRadius / BorderWalls;
        for (int i = 0; i < BorderWalls; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / BorderWalls;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var w = new GameObject("Wall " + i);
            w.transform.SetParent(walls, false);
            w.transform.SetPositionAndRotation(DesertCenter + dir * BorderRadius + Vector3.up * (GroundY + BorderWallHeight * 0.5f - 0.5f),
                Quaternion.LookRotation(dir, Vector3.up));
            w.AddComponent<BoxCollider>().size = new Vector3(seg + 0.6f, BorderWallHeight + 1f, 0.5f);
        }

        // A weathered ranch fence marks the line so it doesn't feel like an invisible wall.
        var rnd = new Rng(4711);
        var posts = new Vector3[BorderPosts];
        for (int i = 0; i < BorderPosts; i++)
        {
            float a = i * Mathf.PI * 2f / BorderPosts;
            posts[i] = DesertCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (BorderRadius - 0.3f) + Vector3.up * LawnY;
        }
        for (int i = 0; i < BorderPosts; i++)
        {
            var p = posts[i];
            dm.At(p);
            var top = p + (Vector3.up + Flat(rnd) * 0.06f).normalized * Lerp(rnd, 1.1f, 1.3f);
            dm.Tube(dmat.Wood, p - Vector3.up * 0.1f, top, 0.07f, 0.06f, 5, 1f);
            var q = posts[(i + 1) % BorderPosts];
            foreach (float h in new[] { 0.45f, 0.95f })
            {
                double roll = rnd.NextDouble();
                if (roll < 0.08) continue; // rail missing
                var a0 = p + Vector3.up * h;
                var a1 = q + Vector3.up * (h + Lerp(rnd, -0.05f, 0.05f));
                if (roll < 0.16) a1 = Vector3.Lerp(a0, q + Vector3.up * 0.02f, 0.85f); // rail fallen down at one end
                dm.Tube(dmat.Wood, a0, a1, 0.035f, 0.035f, 4, 1f);
            }
        }
    }

    // -------------------------------------------------------------- scatter

    static void ScatterDesert()
    {
        var rnd = new Rng(19620305);

        // Dunes first (other things keep off them), then far mesas.
        for (int i = 0; i < 30; i++)
        {
            float rx = Lerp(rnd, 18f, 50f), rz = Lerp(rnd, 12f, 35f);
            if (!Sample(rnd, 85f, 300f, Mathf.Max(rx, rz), 1f, out var p)) continue;
            Dune(p, rx, rz, Lerp(rnd, 3f, 11f), Lerp(rnd, 0f, 360f));
        }
        for (int i = 0; i < 9; i++)
        {
            float ang = (i + Lerp(rnd, -0.3f, 0.3f)) * Mathf.PI * 2f / 9f, dist = Lerp(rnd, 235f, 320f);
            var p = DesertCenter + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
            p.y = LawnY;
            float r = Lerp(rnd, 22f, 55f);
            desertTaken.Add(new Vector3(p.x, r * 1.7f, p.z));
            Mesa(p, r, Lerp(rnd, 22f, 55f), i + 1);
        }

        Scatter(rnd, 25, 45f, 210f, 4f, 1.2f, p => Rock(p, Lerp(rnd, 1.5f, 3.5f), rnd));
        Scatter(rnd, 12, 40f, 180f, 3f, 1.4f, p => DeadTree(p, rnd));
        Scatter(rnd, 45, 32f, 170f, 1.4f, 1.6f, p => Saguaro(p, Lerp(rnd, 2f, 5.5f), rnd));
        Scatter(rnd, 12, 30f, 120f, 2.4f, 1.8f, p => BoneGroup(p, rnd));
        Scatter(rnd, 45, 30f, 130f, 0.7f, 1.8f, p => Barrel(p, Lerp(rnd, 0.7f, 1.4f), rnd));
        Scatter(rnd, 35, 30f, 130f, 1f, 1.8f, p => PricklyPear(p, rnd));
        Scatter(rnd, 90, 28f, 160f, 0.9f, 1.6f, p => DeadBush(p, Lerp(rnd, 0.7f, 1.3f), rnd));
        Scatter(rnd, 20, 30f, 110f, 0.7f, 1.6f, p => Tumbleweed(p, rnd));
        Scatter(rnd, 80, 28f, 150f, 0.8f, 1.6f, p => Rock(p, Lerp(rnd, 0.25f, 0.9f), rnd));
    }

    static void Scatter(Rng rnd, int count, float rMin, float rMax, float foot, float bias, Action<Vector3> make)
    {
        for (int i = 0; i < count; i++)
            if (Sample(rnd, rMin, rMax, foot, bias, out var p)) make(p);
    }

    static bool Sample(Rng rnd, float rMin, float rMax, float foot, float bias, out Vector3 p)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Lerp(rMin, rMax, Mathf.Pow((float)rnd.NextDouble(), bias));
            p = DesertCenter + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
            p.y = LawnY;
            if (!DesertFree(p, foot)) continue;
            desertTaken.Add(new Vector3(p.x, foot, p.z));
            dm.At(p);
            return true;
        }
        p = default;
        return false;
    }

    static bool DesertFree(Vector3 p, float r)
    {
        if (Mathf.Abs(p.x) > PlainEdge - 10f || Mathf.Abs(p.z) > PlainEdge - 10f) return false;
        if (p.x > KeepX0 - r && p.x < KeepX1 + r && p.z > KeepZ0 - r && p.z < KeepZ1 + r) return false;
        float fromCentre = new Vector2(p.x - DesertCenter.x, p.z - DesertCenter.z).magnitude;
        if (Mathf.Abs(fromCentre - BorderRadius) < r + 1.5f) return false; // keep the fence line clear
        foreach (var b in desertBlockers)
            if (p.x + r > b.min.x - 0.5f && p.x - r < b.max.x + 0.5f && p.z + r > b.min.z - 0.5f && p.z - r < b.max.z + 0.5f) return false;
        foreach (var t in desertTaken)
        {
            float dx = t.x - p.x, dz = t.z - p.z, rr = t.y + r;
            if (dx * dx + dz * dz < rr * rr) return false;
        }
        return true;
    }

    static float Lerp(Rng rnd, float a, float b) => Mathf.Lerp(a, b, (float)rnd.NextDouble());
    static Vector3 Flat(Rng rnd) { float a = Lerp(rnd, 0f, Mathf.PI * 2f); return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
    static Vector3 OnSphere(Rng rnd)
    {
        float z = Lerp(rnd, -1f, 1f), a = Lerp(rnd, 0f, Mathf.PI * 2f), s = Mathf.Sqrt(1f - z * z);
        return new Vector3(Mathf.Cos(a) * s, z, Mathf.Sin(a) * s);
    }

    // ------------------------------------------------------------ landforms

    static void Dune(Vector3 c, float rx, float rz, float h, float yaw)
    {
        desertTaken.Add(new Vector3(c.x, Mathf.Max(rx, rz) * 0.9f, c.z));
        dm.At(c);
        const int rings = 9, segs = 22;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var inv = Quaternion.Inverse(rot);
        var pos = new Vector3[rings + 1, segs + 1];
        var nrm = new Vector3[rings + 1, segs + 1];
        var uv = new Vector2[rings + 1, segs + 1];
        float H(float wx, float wz)
        {
            var l = inv * new Vector3(wx - c.x, 0f, wz - c.z);
            float q = (l.x / rx) * (l.x / rx) + (l.z / rz) * (l.z / rz);
            if (q >= 1f) return LawnY - 0.06f;
            float s = 1f - q;
            return LawnY - 0.06f + h * s * s * (0.8f + 0.4f * Mathf.PerlinNoise(wx * 0.035f + 50f, wz * 0.035f + 50f));
        }
        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= segs; j++)
            {
                float r = (float)i / rings, ph = j * Mathf.PI * 2f / segs;
                var w = c + rot * new Vector3(Mathf.Cos(ph) * r * rx, 0f, Mathf.Sin(ph) * r * rz);
                w.y = H(w.x, w.z);
                pos[i, j] = w;
                const float e = 0.6f;
                nrm[i, j] = new Vector3(H(w.x - e, w.z) - H(w.x + e, w.z), 2f * e, H(w.x, w.z - e) - H(w.x, w.z + e)).normalized;
                uv[i, j] = new Vector2(w.x / 8f, w.z / 8f);
            }
        dm.Grid(dmat.Sand, pos, nrm, uv);
    }

    static void Mesa(Vector3 c, float r, float h, int seed)
    {
        dm.At(c);
        const int segs = 26;
        float[] ys = { -1.5f, 0.12f * h, 0.3f * h, 0.55f * h, 0.85f * h, h };
        float[] rs = { 1.6f, 1.25f, 1.08f, 1.02f, 1f, 0.97f };
        int rows = ys.Length;
        var pos = new Vector3[rows, segs + 1];
        var uv = new Vector2[rows, segs + 1];
        float uRep = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * r / 16f));
        for (int k = 0; k < rows; k++)
            for (int j = 0; j <= segs; j++)
            {
                float ph = j * Mathf.PI * 2f / segs;
                float f = 0.75f + 0.5f * Mathf.PerlinNoise(seed * 3.1f + Mathf.Cos(ph) * 1.4f + 10f, seed * 1.7f + Mathf.Sin(ph) * 1.4f + 10f);
                float rr = r * rs[k] * f;
                // clockwise seen from above (rows run upwards)
                pos[k, j] = c + new Vector3(Mathf.Cos(ph) * rr, ys[k], -Mathf.Sin(ph) * rr);
                uv[k, j] = new Vector2((float)j / segs * uRep, ys[k] / 16f);
            }
        var nrm = new Vector3[rows, segs + 1];
        for (int k = 0; k < rows; k++)
            for (int j = 0; j <= segs; j++)
            {
                int jp = j == segs ? 1 : j + 1, jm = j == 0 ? segs - 1 : j - 1;
                var du = pos[k, jp] - pos[k, jm];
                var dv = pos[Mathf.Min(k + 1, rows - 1), j] - pos[Mathf.Max(k - 1, 0), j];
                nrm[k, j] = Vector3.Cross(du, dv).normalized;
            }
        dm.Grid(dmat.Mesa, pos, nrm, uv);

        var ring = new Vector3[segs];
        for (int j = 0; j < segs; j++) ring[j] = pos[rows - 1, j];
        dm.Fan(dmat.Rock, c + Vector3.up * h, Vector3.up, ring, 12f);
    }

    static void Rock(Vector3 p, float s, Rng rnd)
    {
        var radii = new Vector3(Lerp(rnd, 0.45f, 1f), Lerp(rnd, 0.3f, 0.7f), Lerp(rnd, 0.45f, 1f)) * s;
        dm.Ellipsoid(dmat.Rock, p + Vector3.up * (radii.y * 0.25f), Quaternion.Euler(Lerp(rnd, -10f, 10f), Lerp(rnd, 0f, 360f), 0f), radii,
            s > 1.2f ? 7 : 5, s > 1.2f ? 10 : 8, 0.45f, rnd.Next(), 2f);
        if (s > 1.2f) // a couple of smaller stones at the foot of a boulder
            for (int i = 0; i < 2; i++)
            {
                var q = p + Flat(rnd) * (Mathf.Max(radii.x, radii.z) + Lerp(rnd, 0.2f, 0.8f));
                var rr = new Vector3(Lerp(rnd, 0.2f, 0.45f), Lerp(rnd, 0.15f, 0.3f), Lerp(rnd, 0.2f, 0.45f)) * s * 0.6f;
                dm.Ellipsoid(dmat.Rock, q + Vector3.up * (rr.y * 0.2f), Quaternion.Euler(0f, Lerp(rnd, 0f, 360f), 0f), rr, 4, 7, 0.4f, rnd.Next(), 1f);
            }
    }

    // --------------------------------------------------------------- plants

    static void Saguaro(Vector3 p, float h, Rng rnd)
    {
        var mat = dmat.Cactus;
        float r = 0.15f + h * 0.025f;
        dm.Tube(mat, p - Vector3.up * 0.15f, p + Vector3.up * h, r * 1.08f, r, 12, 2f);
        dm.Ellipsoid(mat, p + Vector3.up * h, Quaternion.identity, new Vector3(r, r * 0.85f, r), 5, 12, 0f, 0, 2f);
        if (h < 2.3f) return;
        int arms = rnd.Next(1, 4);
        float yaw0 = Lerp(rnd, 0f, 360f);
        for (int i = 0; i < arms; i++)
        {
            float yaw = (yaw0 + i * (360f / arms) + Lerp(rnd, -35f, 35f)) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            float ay = h * Lerp(rnd, 0.35f, 0.62f), ar = r * 0.68f;
            float upLen = Mathf.Min(Lerp(rnd, 0.6f, 1.6f), h - ay - 0.15f);
            if (upLen < 0.3f) continue;
            var a0 = p + Vector3.up * ay;
            var a1 = a0 + dir * (r + Lerp(rnd, 0.28f, 0.55f));
            var a2 = a1 + Vector3.up * upLen;
            dm.Tube(mat, a0, a1, ar, ar, 10, 1.5f);
            dm.Ellipsoid(mat, a1, Quaternion.identity, Vector3.one * ar, 4, 10, 0f, 0, 1.5f);
            dm.Tube(mat, a1, a2, ar, ar * 0.95f, 10, 1.5f);
            dm.Ellipsoid(mat, a2, Quaternion.identity, new Vector3(ar * 0.95f, ar * 0.8f, ar * 0.95f), 4, 10, 0f, 0, 1.5f);
        }
    }

    static void Barrel(Vector3 p, float s, Rng rnd)
    {
        dm.Ellipsoid(dmat.Cactus, p + Vector3.up * 0.2f * s, Quaternion.Euler(0f, Lerp(rnd, 0f, 360f), 0f), new Vector3(0.3f, 0.36f, 0.3f) * s, 7, 14, 0f, 0, 2.5f);
        float top = (0.2f + 0.33f) * s;
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2f / 5f + Lerp(rnd, -0.3f, 0.3f);
            dm.Ellipsoid(dmat.Flower, p + new Vector3(Mathf.Cos(a) * 0.08f * s, top, Mathf.Sin(a) * 0.08f * s), Quaternion.identity,
                new Vector3(0.045f, 0.03f, 0.045f) * s, 3, 6, 0f, 0, 1f);
        }
    }

    static void PricklyPear(Vector3 p, Rng rnd)
    {
        var tips = new List<(Vector3 pos, Vector3 up)> { (p - Vector3.up * 0.05f, Vector3.up) };
        int count = 5 + rnd.Next(7);
        for (int i = 0; i < count; i++)
        {
            var (from, up) = tips[rnd.Next(tips.Count)];
            var dir = (up + Flat(rnd) * 0.75f).normalized;
            if (dir.y < 0.25f) dir = (dir + Vector3.up * 0.5f).normalized;
            float half = Lerp(rnd, 0.17f, 0.24f);
            var centre = from + dir * half;
            var rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, Lerp(rnd, 0f, 360f), 0f);
            dm.Ellipsoid(dmat.Cactus, centre, rot, new Vector3(half * 0.8f, half, 0.045f), 5, 10, 0f, 0, 1f);
            var tip = centre + dir * half * 0.9f;
            tips.Add((tip, dir));
            if (rnd.NextDouble() < 0.45)
                dm.Ellipsoid(dmat.Fruit, tip, Quaternion.identity, new Vector3(0.035f, 0.045f, 0.035f), 3, 6, 0f, 0, 1f);
        }
    }

    static void DeadBush(Vector3 p, float s, Rng rnd)
    {
        int stems = 5 + rnd.Next(4);
        for (int i = 0; i < stems; i++)
        {
            var dir = (Flat(rnd) * Lerp(rnd, 0.4f, 1.1f) + Vector3.up).normalized;
            Branch(dmat.Wood, p - Vector3.up * 0.05f, dir, Lerp(rnd, 0.35f, 0.7f) * s, 0.022f * s, 2, 4, rnd);
        }
    }

    static void DeadTree(Vector3 p, Rng rnd)
    {
        float h = Lerp(rnd, 1.4f, 2.5f);
        var top = p + (Vector3.up + Flat(rnd) * 0.18f).normalized * h;
        dm.Tube(dmat.Wood, p - Vector3.up * 0.1f, top, 0.15f, 0.09f, 7, 1f);
        int limbs = 2 + rnd.Next(3);
        for (int i = 0; i < limbs; i++)
        {
            var from = Vector3.Lerp(p, top, Lerp(rnd, 0.6f, 1f));
            var dir = (Flat(rnd) * Lerp(rnd, 0.5f, 1.2f) + Vector3.up).normalized;
            Branch(dmat.Wood, from, dir, Lerp(rnd, 0.9f, 1.5f), 0.075f, 3, 5, rnd);
        }
    }

    static void Branch(Material mat, Vector3 start, Vector3 dir, float len, float r, int depth, int sides, Rng rnd)
    {
        var end = start + dir * len;
        dm.Tube(mat, start, end, r, r * 0.55f, sides, 1f);
        if (depth == 0) return;
        int n = 2 + (rnd.NextDouble() < 0.3 ? 1 : 0);
        for (int i = 0; i < n; i++)
        {
            var from = Vector3.Lerp(start, end, Lerp(rnd, 0.55f, 1f));
            var nd = (dir + OnSphere(rnd) * 0.9f + Vector3.up * 0.2f).normalized;
            if (nd.y < 0.05f) nd = new Vector3(nd.x, 0.15f, nd.z).normalized;
            Branch(mat, from, nd, len * 0.6f, r * 0.6f, depth - 1, Mathf.Max(3, sides - 1), rnd);
        }
    }

    static void Tumbleweed(Vector3 p, Rng rnd)
    {
        float R = Lerp(rnd, 0.28f, 0.48f);
        var c = p + Vector3.up * R * 0.9f;
        for (int i = 0; i < 45; i++)
        {
            var a = c + OnSphere(rnd) * R * Lerp(rnd, 0.2f, 0.9f);
            var b = a + OnSphere(rnd) * R * Lerp(rnd, 0.5f, 1f);
            b = c + Vector3.ClampMagnitude(b - c, R);
            dm.Tube(dmat.Tumble, a, b, 0.011f, 0.007f, 3, 1f);
        }
    }

    // ---------------------------------------------------------------- bones

    static void BoneGroup(Vector3 p, Rng rnd)
    {
        Skull(p, Lerp(rnd, 0f, 360f), rnd);
        if (rnd.NextDouble() < 0.65) RibCage(p + Flat(rnd) * Lerp(rnd, 0.7f, 1.2f), Lerp(rnd, 0f, 360f), rnd);
        int bones = 2 + rnd.Next(3);
        for (int i = 0; i < bones; i++)
            LongBone(p + Flat(rnd) * Lerp(rnd, 0.5f, 1.7f), Lerp(rnd, 0f, 360f), Lerp(rnd, 0.3f, 0.55f));
    }

    static void Skull(Vector3 p, float yaw, Rng rnd)
    {
        var rot = Quaternion.Euler(Lerp(rnd, -12f, 6f), yaw, Lerp(rnd, -10f, 10f));
        Vector3 f = rot * Vector3.forward, rt = rot * Vector3.right, up = rot * Vector3.up;
        var c = p + Vector3.up * 0.11f;
        dm.Ellipsoid(dmat.Bone, c, rot, new Vector3(0.11f, 0.095f, 0.13f), 6, 10, 0.06f, rnd.Next(), 1f);
        var s0 = c + f * 0.07f - up * 0.02f;
        var s1 = c + f * 0.32f - up * 0.065f;
        dm.Tube(dmat.Bone, s0, s1, 0.075f, 0.048f, 9, 1f);
        dm.Ellipsoid(dmat.Bone, s1, rot, new Vector3(0.05f, 0.04f, 0.035f), 4, 8, 0f, 0, 1f);
        for (int side = -1; side <= 1; side += 2)
        {
            dm.Ellipsoid(dmat.Socket, c + f * 0.09f + rt * side * 0.072f + up * 0.025f, rot, Vector3.one * 0.032f, 4, 7, 0f, 0, 1f);
            dm.Ellipsoid(dmat.Socket, s1 + f * 0.02f + rt * side * 0.022f + up * 0.012f, rot, Vector3.one * 0.014f, 3, 6, 0f, 0, 1f);
            var h0 = c + rt * side * 0.085f + up * 0.06f;
            var h1 = h0 + rt * side * 0.15f + up * 0.01f;
            var h2 = h1 + rt * side * 0.09f + up * 0.07f + f * 0.03f;
            var h3 = h2 + rt * side * 0.02f + up * 0.08f + f * 0.05f;
            dm.Tube(dmat.Horn, h0, h1, 0.034f, 0.028f, 7, 1f);
            dm.Ellipsoid(dmat.Horn, h1, rot, Vector3.one * 0.028f, 3, 7, 0f, 0, 1f);
            dm.Tube(dmat.Horn, h1, h2, 0.028f, 0.018f, 7, 1f);
            dm.Ellipsoid(dmat.Horn, h2, rot, Vector3.one * 0.018f, 3, 7, 0f, 0, 1f);
            dm.Tube(dmat.Horn, h2, h3, 0.018f, 0.003f, 7, 1f);
        }
    }

    static void RibCage(Vector3 p, float yaw, Rng rnd)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 f = rot * Vector3.forward, rt = rot * Vector3.right;
        const int verts = 9;
        for (int i = 0; i < verts; i++)
        {
            var at = p + f * Mathf.Lerp(-0.45f, 0.45f, i / (verts - 1f)) + Vector3.up * 0.04f;
            dm.Ellipsoid(dmat.Bone, at, rot, new Vector3(0.045f, 0.035f, 0.042f), 4, 7, 0.1f, rnd.Next(), 1f);
        }
        dm.Ellipsoid(dmat.Bone, p - f * 0.58f + Vector3.up * 0.04f, rot, new Vector3(0.17f, 0.05f, 0.12f), 4, 9, 0.15f, rnd.Next(), 1f); // pelvis
        for (int k = 0; k < 7; k++)
        {
            float t = Mathf.Lerp(-0.3f, 0.38f, k / 6f);
            float R = Mathf.Max(0.14f, 0.32f * (1f - Mathf.Abs(t - 0.05f) * 1.1f));
            var sp = p + f * t + Vector3.up * 0.04f;
            for (int side = -1; side <= 1; side += 2)
            {
                if (rnd.NextDouble() < 0.18) continue; // missing rib
                float rise = side > 0 ? 0.78f : 0.32f;  // carcass lies on one side
                int segs = rnd.NextDouble() < 0.2 ? 3 : 5; // some ribs snapped short
                var prev = sp;
                for (int s = 1; s <= segs; s++)
                {
                    float a = s / 5f * 150f * Mathf.Deg2Rad;
                    var pt = sp + rt * side * Mathf.Sin(a) * R + Vector3.up * (1f - Mathf.Cos(a)) * R * rise + f * (s * 0.012f);
                    dm.Tube(dmat.Bone, prev, pt, 0.015f, 0.012f, 4, 1f);
                    prev = pt;
                }
            }
        }
    }

    static void LongBone(Vector3 p, float yaw, float len)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var d = rot * Vector3.forward * (len * 0.5f);
        var a = p + d + Vector3.up * 0.03f;
        var b = p - d + Vector3.up * 0.03f;
        dm.Tube(dmat.Bone, a, b, 0.022f, 0.02f, 7, 1f);
        dm.Ellipsoid(dmat.Bone, a, rot, new Vector3(0.048f, 0.034f, 0.04f), 4, 8, 0.1f, 1, 1f);
        dm.Ellipsoid(dmat.Bone, b, rot, new Vector3(0.042f, 0.032f, 0.036f), 4, 8, 0.1f, 2, 1f);
    }

    // ------------------------------------------------------- mesh building

    // Collects geometry per material and per ~90 m cell, then saves one mesh
    // per pair: a few static draw calls, and far cells are frustum-culled.
    sealed class DesertMesh
    {
        sealed class Part
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
        }

        const float Cell = 90f;
        readonly Dictionary<(Material, int, int), Part> parts = new Dictionary<(Material, int, int), Part>();
        int cx, cz;
        public int Count => parts.Count;

        public void At(Vector3 p) { cx = Mathf.FloorToInt(p.x / Cell); cz = Mathf.FloorToInt(p.z / Cell); }

        Part Get(Material m)
        {
            var k = (m, cx, cz);
            if (!parts.TryGetValue(k, out var p)) parts[k] = p = new Part();
            return p;
        }

        // Rows x cols vertex grid; quads (A,B,C)(B,D,C) - front faces need
        // cross(column step, row step) to point outwards.
        public void Grid(Material m, Vector3[,] pos, Vector3[,] nrm, Vector2[,] uv)
        {
            var p = Get(m);
            int rows = pos.GetLength(0), cols = pos.GetLength(1), b = p.v.Count;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++) { p.v.Add(pos[i, j]); p.n.Add(nrm[i, j]); p.uv.Add(uv[i, j]); }
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < cols - 1; j++)
                {
                    int A = b + i * cols + j, B = A + 1, C = A + cols, D = C + 1;
                    p.t.Add(A); p.t.Add(B); p.t.Add(C);
                    p.t.Add(B); p.t.Add(D); p.t.Add(C);
                }
        }

        // Ring must run clockwise seen from the side the normal points to.
        public void Fan(Material m, Vector3 centre, Vector3 normal, Vector3[] ring, float uvTile)
        {
            var p = Get(m);
            int b = p.v.Count;
            p.v.Add(centre); p.n.Add(normal); p.uv.Add(new Vector2(centre.x, centre.z) / uvTile);
            foreach (var r in ring) { p.v.Add(r); p.n.Add(normal); p.uv.Add(new Vector2(r.x, r.z) / uvTile); }
            for (int j = 0; j < ring.Length; j++)
            {
                p.t.Add(b); p.t.Add(b + 1 + j); p.t.Add(b + 1 + (j + 1) % ring.Length);
            }
        }

        public void Tube(Material m, Vector3 a, Vector3 b, float ra, float rb, int sides, float uRepeat)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var dn = d / len;
            var helper = Mathf.Abs(dn.y) < 0.99f ? Vector3.up : Vector3.right;
            var s1 = Vector3.Cross(dn, helper).normalized;
            var s2 = Vector3.Cross(dn, s1);
            float slope = (ra - rb) / len;
            var pos = new Vector3[2, sides + 1];
            var nrm = new Vector3[2, sides + 1];
            var uv = new Vector2[2, sides + 1];
            for (int k = 0; k < 2; k++)
                for (int j = 0; j <= sides; j++)
                {
                    float ang = j * Mathf.PI * 2f / sides;
                    var dir = Mathf.Cos(ang) * s1 + Mathf.Sin(ang) * s2;
                    pos[k, j] = (k == 0 ? a : b) + dir * (k == 0 ? ra : rb);
                    nrm[k, j] = (dir + dn * slope).normalized;
                    uv[k, j] = new Vector2((float)j / sides * uRepeat, k * len);
                }
            Grid(m, pos, nrm, uv);
        }

        public void Ellipsoid(Material m, Vector3 c, Quaternion rot, Vector3 radii, int lat, int lon, float jitter, int seed, float uRepeat)
        {
            var pos = new Vector3[lat + 1, lon + 1];
            var nrm = new Vector3[lat + 1, lon + 1];
            var uv = new Vector2[lat + 1, lon + 1];
            for (int i = 0; i <= lat; i++)
            {
                float th = i * Mathf.PI / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float ph = j * Mathf.PI * 2f / lon;
                    var u = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float f = 1f;
                    if (jitter > 0f)
                    {
                        int jj = i == 0 || i == lat ? 0 : j % lon;
                        f = 1f + (Hash01(seed, i, jj) - 0.5f) * jitter;
                    }
                    pos[i, j] = c + rot * Vector3.Scale(u * f, radii);
                    nrm[i, j] = (rot * new Vector3(u.x / radii.x, u.y / radii.y, u.z / radii.z)).normalized;
                    uv[i, j] = new Vector2((float)j / lon * uRepeat, 1f - (float)i / lat);
                }
            }
            Grid(m, pos, nrm, uv);
        }

        static float Hash01(int seed, int a, int b)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(a * 19349663) ^ (uint)(b * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffffff) / 16777216f;
            }
        }

        public void Save(Transform parent)
        {
            foreach (var kv in parts)
            {
                var (mat, x, z) = kv.Key;
                var p = kv.Value;
                if (p.v.Count == 0) continue;
                string name = $"{mat.name} {x}_{z}";
                var mesh = new Mesh { name = name };
                mesh.indexFormat = p.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(p.v);
                mesh.SetNormals(p.n);
                mesh.SetUVs(0, p.uv);
                mesh.SetTriangles(p.t, 0);
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{DesertMeshFolder}/{name}.asset");

                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }
    }

    sealed class DesertMats
    {
        public readonly Material Sand, Cactus, Flower, Fruit, Wood, Tumble, Bone, Horn, Socket, Rock, Mesa;

        public DesertMats(Material sand)
        {
            Sand = sand;
            Cactus = MakeMat("Desert Cactus", Color.white, 0.25f, 0f, Textures.Cactus());
            Flower = MakeMat("Desert Cactus Flower", new Color(1f, 0.78f, 0.2f), 0.3f, 0f);
            Fruit = MakeMat("Desert Cactus Fruit", new Color(0.72f, 0.1f, 0.28f), 0.45f, 0f);
            Wood = MakeMat("Desert Dead Wood", new Color(0.46f, 0.4f, 0.34f), 0.12f, 0f);
            Tumble = MakeMat("Desert Tumbleweed", new Color(0.66f, 0.55f, 0.38f), 0.1f, 0f);
            Bone = MakeMat("Desert Bone", new Color(0.9f, 0.86f, 0.76f), 0.3f, 0f);
            Horn = MakeMat("Desert Horn", new Color(0.4f, 0.34f, 0.27f), 0.45f, 0f);
            Socket = MakeMat("Desert Bone Shadow", new Color(0.07f, 0.05f, 0.04f), 0.1f, 0f);
            Rock = MakeMat("Desert Rock", Color.white, 0.15f, 0f, Textures.DesertRock());
            Mesa = MakeMat("Desert Mesa", Color.white, 0.1f, 0f, Textures.Mesa());
        }
    }

    // -------------------------------------------------------------- textures

    static partial class Textures
    {
        // Perlin noise cross-faded over the tile edges so the texture repeats seamlessly.
        static float TileNoise(float u, float v, float freq, float seed)
        {
            float a = Mathf.PerlinNoise(u * freq + seed, v * freq + seed);
            float b = Mathf.PerlinNoise((u - 1f) * freq + seed, v * freq + seed);
            float c = Mathf.PerlinNoise(u * freq + seed, (v - 1f) * freq + seed);
            float d = Mathf.PerlinNoise((u - 1f) * freq + seed, (v - 1f) * freq + seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        public static Texture2D Sand() => Make("Desert Sand", 256, 256, true, (u, v) =>
        {
            float n = TileNoise(u, v, 4f, 100f) * 0.6f + TileNoise(u, v, 16f, 130f) * 0.4f;
            float ripple = Mathf.Sin((v * 5f + TileNoise(u, v, 3f, 160f) * 0.7f) * Mathf.PI * 2f);
            float grain = Hash(Mathf.Floor(u * 256f), Mathf.Floor(v * 256f));
            var col = Mix(new Color(0.76f, 0.58f, 0.39f), new Color(0.93f, 0.8f, 0.6f), n * 1.3f - 0.15f);
            col *= (0.95f + 0.05f * ripple) * (0.95f + 0.09f * grain);
            col.a = 1f;
            return col;
        });

        public static Texture2D Cactus() => Make("Desert Cactus", 128, 128, true, (u, v) =>
        {
            float rib = 0.5f + 0.5f * Mathf.Cos(u * Mathf.PI * 2f * 8f);
            var col = Mix(new Color(0.11f, 0.25f, 0.11f), new Color(0.3f, 0.5f, 0.22f), rib * 0.8f + TileNoise(u, v, 6f, 105f) * 0.2f);
            if (rib > 0.9f && Hash(Mathf.Floor(u * 128f), Mathf.Floor(v * 24f)) > 0.55f) col = Mix(col, new Color(0.86f, 0.82f, 0.66f), 0.55f);
            col.a = 1f;
            return col;
        });

        public static Texture2D DesertRock() => Make("Desert Rock", 128, 128, true, (u, v) =>
        {
            float n = TileNoise(u, v, 5f, 107f) * 0.7f + TileNoise(u, v, 20f, 109f) * 0.3f;
            var col = Mix(new Color(0.42f, 0.29f, 0.21f), new Color(0.7f, 0.53f, 0.38f), n * 1.4f - 0.2f);
            if (Hash(Mathf.Floor(u * 128f), Mathf.Floor(v * 128f)) > 0.94f) col *= 0.8f;
            col.a = 1f;
            return col;
        });

        public static Texture2D Mesa() => Make("Desert Mesa", 64, 256, true, (u, v) =>
        {
            float w = TileNoise(u, v, 4f, 103f);
            float bands = 0.5f + 0.22f * Mathf.Sin((v * 3f + w * 0.12f) * Mathf.PI * 2f)
                               + 0.15f * Mathf.Sin(v * 8f * Mathf.PI * 2f + 1.3f)
                               + 0.1f * Mathf.Sin(v * 17f * Mathf.PI * 2f + 2.1f);
            var col = Mix(new Color(0.48f, 0.23f, 0.14f), new Color(0.82f, 0.52f, 0.32f), bands);
            if (Mathf.Sin(v * 2f * Mathf.PI * 2f + 0.5f) > 0.93f) col = Mix(col, new Color(0.9f, 0.78f, 0.6f), 0.6f);
            col *= 0.9f + 0.1f * TileNoise(u, v, 8f, 111f);
            col.a = 1f;
            return col;
        });
    }
}
