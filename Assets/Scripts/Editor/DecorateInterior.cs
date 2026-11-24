using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Tools > Casino > Decorate Interior
//
// Dresses the casino hall in SampleScene so it reads as an upmarket lounge:
// walnut wainscoting with a gold chair rail, damask upper walls, gold crown
// moulding, a coffered ceiling, zoned rugs (entrance runner, roulette, blackjack,
// slots, bar, lounge, darts), procedurally painted art in gold frames with
// picture lights, wall sconces, crystal chandeliers over the roulette tables and
// entrance, billiard-style pendants over blackjack, bar pendants and LED shelf
// strips, plants, a small VIP seating corner, cocktail high-tops near the bar
// and velvet rope stanchions.
//
// Everything is created under a single "Interior Decor" root (re-running
// replaces it; Tools > Casino > Remove Interior Decor deletes it and restores
// the ambient lighting). No existing object is moved, disabled or deleted.
// Wall-mounted pieces skip themselves (with a log line) if an existing object
// is in the way. Generated textures/materials/meshes live in
// Assets/Materials/InteriorDecor.
//
// VR/Quest budget: lighting is mostly emissive. Only the 4 big chandeliers and
// the bar get a realtime point light each (no shadows); set AddRealtimeLights
// to false to go fully emissive. All decor is marked batching-static.
public static class DecorateInterior
{
    const string RootName = "Interior Decor";
    const string Folder = "Assets/Materials/InteriorDecor";
    const string TexFolder = Folder + "/Textures";
    const string MeshFolder = Folder + "/Meshes";
    const string Ithappy = "Assets/ithappy/Casino_Free/Prefabs/Assets/";
    const bool AddRealtimeLights = true;

    // Interior shell of the hall (measured from the wall cubes under "Assets").
    const float FloorY = 0.42f, CeilY = 5.265f;
    const float WestX = -9.97f, EastX = 4.18f, SouthZ = -0.35f, NorthZ = 28.09f;
    const float DoorMinX = -4.25f, DoorMaxX = -1.13f;
    const float WainscotTop = FloorY + 1.0f;
    const float CrownBottom = 5.03f;

    const string AmbientPrefKey = "DecorateInterior.PrevAmbient";

    static Transform root;
    static List<Bounds> obstacles;
    static Mats m;

    // ------------------------------------------------------------------ menu

    [MenuItem("Tools/Casino/Decorate Interior")]
    static void Decorate()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        CollectObstacles();
        EnsureFolder(Folder);
        EnsureFolder(TexFolder);
        EnsureFolder(MeshFolder);
        m = new Mats();

        var go = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(go, "Decorate Interior");
        root = go.transform;

        BuildWalls();
        BuildCeiling();
        BuildRugs();
        BuildArt();
        BuildSconces();
        BuildChandeliers();
        BuildBlackjackPendants();
        BuildBarDecor();
        BuildPlants();
        BuildVipCorner();
        BuildHighTops();
        BuildRopes();
        ApplyAmbience();

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Light>() == null)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = go;
        Debug.Log($"DecorateInterior: built '{RootName}' ({root.GetComponentsInChildren<Transform>().Length} objects). Save the scene to keep it.");
    }

    [MenuItem("Tools/Casino/Remove Interior Decor")]
    static void Remove()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        RestoreAmbience();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("DecorateInterior: removed decor and restored ambient lighting.");
    }

    // ------------------------------------------------------------- obstacles

    static void CollectObstacles()
    {
        obstacles = new List<Bounds>();
        var skipRoots = new HashSet<string> { "XR Origin (XR Rig)", "Player Avatar", "Player Hands", RootName };
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (skipRoots.Contains(r.transform.root.name)) continue;
            var t = r.transform;
            if (t.parent != null && t.parent.name == "Assets" && (t.name.StartsWith("Cube") || t.name.StartsWith("Floor"))) continue;
            var s = r.bounds.size;
            if (s.x > 8f || s.z > 8f) continue; // floor planes
            obstacles.Add(r.bounds);
        }
    }

    static bool Free(Bounds b, string what)
    {
        b.Expand(-0.01f);
        foreach (var o in obstacles)
            if (o.Intersects(b))
            {
                Debug.Log($"DecorateInterior: skipped {what} - something already there ({o.center}).");
                return false;
            }
        return true;
    }

    // ----------------------------------------------------------------- walls

    struct Wall
    {
        public Vector3 origin, along, normal;
        public Wall(Vector3 origin, Vector3 along, Vector3 normal) { this.origin = origin; this.along = along; this.normal = normal; }
        public Vector3 P(float a, float y, float off) => new Vector3(origin.x, 0f, origin.z) + along * a + normal * off + Vector3.up * y;
        public Quaternion Rot => Quaternion.LookRotation(-normal, Vector3.up);
        public Bounds Box(float a, float y, float off, float w, float h, float d)
        {
            var c = P(a, y, off);
            var size = Mathf.Abs(along.x) > 0.5f ? new Vector3(w, h, d) : new Vector3(d, h, w);
            return new Bounds(c, size);
        }
        public float AlongOf(Vector3 p) => Vector3.Dot(p, along);
    }

    static readonly Wall West = new Wall(new Vector3(WestX, 0, 0), Vector3.forward, Vector3.right);
    static readonly Wall East = new Wall(new Vector3(EastX, 0, 0), Vector3.forward, Vector3.left);
    static readonly Wall South = new Wall(new Vector3(0, 0, SouthZ), Vector3.right, Vector3.forward);
    static readonly Wall North = new Wall(new Vector3(0, 0, NorthZ), Vector3.right, Vector3.back);

    static void BuildWalls()
    {
        var g = Group("Walls");
        var runs = new List<(Wall w, float a0, float a1, string name)>
        {
            (West, SouthZ, NorthZ, "West"),
            (East, SouthZ, NorthZ, "East"),
            (South, WestX, EastX, "South"),
            (North, WestX, DoorMinX, "North L"),
            (North, DoorMaxX, EastX, "North R"),
        };
        foreach (var (w, a0, a1, name) in runs)
        {
            var wg = Group(name, g);
            float len = a1 - a0, mid = (a0 + a1) * 0.5f;
            // Panels (quads, a hair off the wall, behind everything already there).
            float wainH = WainscotTop - FloorY;
            Quad("Wainscot", wg, m.Wainscot, w.P(mid, FloorY + wainH * 0.5f, 0.006f), w.Rot, len, wainH, len / 1.0f, 1f);
            float upH = CrownBottom - WainscotTop;
            Quad("Damask", wg, m.Damask, w.P(mid, WainscotTop + upH * 0.5f, 0.006f), w.Rot, len, upH, len / 0.7f, upH / 0.7f);
            // Trims (thin boxes), split around anything mounted on the wall.
            Rail("Baseboard", wg, w, a0, a1, FloorY + 0.07f, 0.14f, 0.022f, m.DarkWood);
            Rail("Chair Rail", wg, w, a0, a1, WainscotTop, 0.05f, 0.03f, m.Gold);
            Rail("Crown Gold", wg, w, a0, a1, CrownBottom + 0.04f, 0.08f, 0.05f, m.Gold);
            Rail("Crown Wood", wg, w, a0, a1, (CrownBottom + 0.08f + CeilY) * 0.5f, CeilY - CrownBottom - 0.08f, 0.035f, m.DarkWood);
        }
    }

    static void Rail(string name, Transform parent, Wall w, float a0, float a1, float y, float h, float depth, Material mat)
    {
        var full = w.Box((a0 + a1) * 0.5f, y, depth * 0.5f, a1 - a0, h, depth);
        full.Expand(-0.004f);
        var cuts = new List<Vector2>();
        foreach (var o in obstacles)
            if (o.Intersects(full))
            {
                float p = w.AlongOf(o.min), q = w.AlongOf(o.max);
                cuts.Add(new Vector2(Mathf.Min(p, q) - 0.02f, Mathf.Max(p, q) + 0.02f));
            }
        cuts.Sort((x, z) => x.x.CompareTo(z.x));
        float start = a0;
        int i = 0;
        foreach (var c in cuts)
        {
            if (c.x > start) Seg(c.x);
            start = Mathf.Max(start, c.y);
        }
        Seg(a1);

        void Seg(float end)
        {
            if (end - start < 0.08f) return;
            Box($"{name} {++i}", parent, mat, w.P((start + end) * 0.5f, y, depth * 0.5f), w.Rot, new Vector3(end - start, h, depth));
        }
    }

    static void BuildCeiling()
    {
        var g = Group("Ceiling");
        float w = EastX - WestX, d = NorthZ - SouthZ;
        Quad("Coffered Ceiling", g, m.Ceiling, new Vector3((WestX + EastX) * 0.5f, CeilY - 0.004f, (SouthZ + NorthZ) * 0.5f),
            Quaternion.LookRotation(Vector3.up, Vector3.forward), w, d, w / 2f, d / 2f);
    }

    // ------------------------------------------------------------------ rugs

    static void BuildRugs()
    {
        var g = Group("Rugs");
        Rug(g, "Entrance Runner", -3.55f, -1.75f, 15.45f, 28.0f, RugStyle.Runner);
        Rug(g, "Roulette Rug A", -1.9f, 1.85f, 4.5f, 9.4f, RugStyle.Medallion);
        Rug(g, "Roulette Rug B", -6.6f, -2.95f, 4.4f, 9.3f, RugStyle.Medallion);
        Rug(g, "Roulette Rug C", -4.7f, -0.95f, 10.6f, 15.4f, RugStyle.Medallion);
        Rug(g, "Blackjack Rug", 0.2f, 3.9f, 9.6f, 20.3f, RugStyle.Trellis);
        Rug(g, "Slots Rug", -9.0f, -1.4f, 1.25f, 4.25f, RugStyle.Deco);
        Rug(g, "Bar Rug", -7.2f, -5.0f, 13.9f, 18.7f, RugStyle.Burgundy);
        Rug(g, "Lounge Rug", -9.85f, -6.3f, 20.4f, 27.9f, RugStyle.Lounge);
        Rug(g, "VIP Rug", -9.85f, -6.75f, 8.5f, 11.4f, RugStyle.Lounge);
        Rug(g, "Darts Rug", 0.9f, 3.9f, 21.6f, 28.0f, RugStyle.Trellis);
    }

    static void Rug(Transform parent, string name, float x0, float x1, float z0, float z1, RugStyle style)
    {
        float w = x1 - x0, h = z1 - z0;
        var tex = Textures.Rug(name, style, w, h);
        var mat = MakeMat("Rug " + name, Color.white, 0.05f, 0f, tex, new Color(0.12f, 0.12f, 0.12f), tex);
        Quad(name, parent, mat, new Vector3((x0 + x1) * 0.5f, FloorY + 0.009f, (z0 + z1) * 0.5f),
            Quaternion.LookRotation(Vector3.down, Vector3.forward), w, h, 1f, 1f);
    }

    // ------------------------------------------------------------------- art

    static void BuildArt()
    {
        var g = Group("Artwork");
        var pieces = new (Wall w, float a, float y, float width, float height, int style)[]
        {
            (East, 10.77f, 3.55f, 1.5f, 1.0f, 0),
            (East, 14.61f, 3.55f, 1.5f, 1.0f, 1),
            (East, 18.63f, 3.55f, 1.5f, 1.0f, 2),
            (East, 6.5f, 2.75f, 1.6f, 1.1f, 3),
            (East, 23.4f, 2.7f, 1.3f, 1.0f, 5),
            (West, 9.85f, 2.55f, 1.3f, 0.95f, 4),
            (West, 23.76f, 3.7f, 2.2f, 1.0f, 6),
            (West, 15.71f, 4.15f, 2.4f, 0.85f, 7),
            (West, 5.3f, 4.0f, 2.4f, 0.85f, 1),
            (South, -3.3f, 4.05f, 2.0f, 0.9f, 3),
            (South, -7.4f, 4.05f, 2.0f, 0.9f, 0),
            (South, 1.4f, 2.9f, 1.6f, 1.0f, 5),
            (North, -7.2f, 2.75f, 1.6f, 1.1f, 2),
            (North, 0.0f, 2.6f, 0.95f, 1.3f, 4),
            (North, -2.69f, 4.45f, 1.6f, 0.7f, 0),
        };
        int n = 0;
        foreach (var p in pieces)
        {
            const float fw = 0.09f, fd = 0.055f;
            if (!Free(p.w.Box(p.a, p.y, 0.06f, p.width + 2 * fw, p.height + 2 * fw + 0.3f, 0.12f), "artwork " + n)) { n++; continue; }
            var a = Group($"Painting {++n}", g);
            var tex = Textures.Art(p.style, p.width / p.height);
            var mat = MakeMat($"Art {p.style} {(p.width / p.height):F2}", Color.white, 0.2f, 0f, tex, new Color(0.38f, 0.38f, 0.38f), tex);
            var rot = p.w.Rot;
            Box("Liner", a, m.Black, p.w.P(p.a, p.y, 0.012f), rot, new Vector3(p.width + 0.04f, p.height + 0.04f, 0.024f));
            Quad("Canvas", a, mat, p.w.P(p.a, p.y, 0.026f), rot, p.width, p.height, 1f, 1f);
            Box("Frame Top", a, m.Gold, p.w.P(p.a, p.y + p.height * 0.5f + fw * 0.5f, fd * 0.5f), rot, new Vector3(p.width + 2 * fw, fw, fd));
            Box("Frame Bottom", a, m.Gold, p.w.P(p.a, p.y - p.height * 0.5f - fw * 0.5f, fd * 0.5f), rot, new Vector3(p.width + 2 * fw, fw, fd));
            Box("Frame Left", a, m.Gold, p.w.P(p.a - p.width * 0.5f - fw * 0.5f, p.y, fd * 0.5f), rot, new Vector3(fw, p.height, fd));
            Box("Frame Right", a, m.Gold, p.w.P(p.a + p.width * 0.5f + fw * 0.5f, p.y, fd * 0.5f), rot, new Vector3(fw, p.height, fd));
            // Picture light
            float ly = p.y + p.height * 0.5f + fw + 0.12f;
            Box("Picture Light Arm", a, m.Gold, p.w.P(p.a, ly, 0.07f), rot, new Vector3(0.03f, 0.03f, 0.14f));
            Box("Picture Light Hood", a, m.Gold, p.w.P(p.a, ly, 0.15f), rot, new Vector3(Mathf.Min(p.width * 0.6f, 0.9f), 0.045f, 0.06f));
            Box("Picture Light Glow", a, m.Glow, p.w.P(p.a, ly - 0.025f, 0.15f), rot, new Vector3(Mathf.Min(p.width * 0.6f, 0.9f) - 0.02f, 0.008f, 0.04f));
        }
    }

    // --------------------------------------------------------------- sconces

    static void BuildSconces()
    {
        var g = Group("Sconces");
        var spots = new List<(Wall w, float a, float y)>();
        foreach (var z in new[] { 4.6f, 8.7f, 12.7f, 16.6f, 20.7f, 25.2f }) spots.Add((East, z, 2.65f));
        foreach (var z in new[] { 8.6f, 11.15f, 13.65f, 18.3f, 22.0f, 25.6f }) spots.Add((West, z, 2.65f));
        foreach (var x in new[] { -7.48f, -5.43f, -3.37f, -0.9f, 3.45f }) spots.Add((South, x, 2.4f));
        foreach (var x in new[] { -9.2f, -5.2f, -0.85f, 3.7f }) spots.Add((North, x, 2.65f));

        int n = 0;
        foreach (var s in spots)
        {
            n++;
            if (!Free(s.w.Box(s.a, s.y + 0.05f, 0.18f, 0.3f, 0.5f, 0.36f), "sconce " + n)) continue;
            var t = Group($"Sconce {n}", g);
            var rot = s.w.Rot;
            Box("Backplate", t, m.Gold, s.w.P(s.a, s.y, 0.012f), rot, new Vector3(0.11f, 0.26f, 0.024f));
            Box("Arm", t, m.Gold, s.w.P(s.a, s.y - 0.05f, 0.09f), rot, new Vector3(0.025f, 0.025f, 0.15f));
            Prim("Cup", PrimitiveType.Cylinder, t, m.Gold, s.w.P(s.a, s.y - 0.03f, 0.17f), Quaternion.identity, new Vector3(0.06f, 0.02f, 0.06f));
            Prim("Shade", PrimitiveType.Cylinder, t, m.Shade, s.w.P(s.a, s.y + 0.08f, 0.17f), Quaternion.identity, new Vector3(0.17f, 0.085f, 0.17f));
            Prim("Shade Rim", PrimitiveType.Cylinder, t, m.Gold, s.w.P(s.a, s.y + 0.165f, 0.17f), Quaternion.identity, new Vector3(0.175f, 0.006f, 0.175f));
        }
    }

    // ----------------------------------------------------------- chandeliers

    static void BuildChandeliers()
    {
        var g = Group("Chandeliers");
        Chandelier(g, "Chandelier Roulette A", new Vector3(0.0f, 0, 7.0f), 1f, true);
        Chandelier(g, "Chandelier Roulette B", new Vector3(-4.5f, 0, 6.9f), 1f, true);
        Chandelier(g, "Chandelier Roulette C", new Vector3(-2.6f, 0, 13.0f), 1f, true);
        Chandelier(g, "Chandelier Entrance", new Vector3(-2.65f, 0, 20.9f), 1.1f, true);
        Chandelier(g, "Chandelier VIP", new Vector3(-8.0f, 0, 9.9f), 0.6f, false);
    }

    static void Chandelier(Transform parent, string name, Vector3 pos, float s, bool light)
    {
        var t = Group(name, parent);
        float top = CeilY;
        float c = top - 1.15f * s;
        Vector3 P(float x, float y, float z) => new Vector3(pos.x + x, y, pos.z + z);

        Prim("Ceiling Rose", PrimitiveType.Cylinder, t, m.Gold, P(0, top - 0.015f, 0), Quaternion.identity, new Vector3(0.6f * s, 0.015f, 0.6f * s));
        Prim("Rod", PrimitiveType.Cylinder, t, m.Gold, P(0, (top + c) * 0.5f, 0), Quaternion.identity, new Vector3(0.035f * s, (top - c) * 0.5f, 0.035f * s));
        Prim("Body", PrimitiveType.Sphere, t, m.Gold, P(0, c, 0), Quaternion.identity, Vector3.one * 0.16f * s);
        Prim("Finial", PrimitiveType.Sphere, t, m.Crystal, P(0, c - 0.32f * s, 0), Quaternion.identity, new Vector3(0.1f, 0.2f, 0.1f) * s);
        Prim("Stem", PrimitiveType.Cylinder, t, m.Gold, P(0, c - 0.12f * s, 0), Quaternion.identity, new Vector3(0.03f, 0.12f, 0.03f) * s);

        Tier(0.62f * s, c - 0.05f * s, 8, s);
        Tier(0.36f * s, c + 0.3f * s, 6, s);

        // Crystal drops hanging off the lower ring
        for (int i = 0; i < 16; i++)
        {
            float ang = (i + 0.5f) * Mathf.PI * 2f / 16f;
            var p = P(Mathf.Cos(ang) * 0.62f * s, c - 0.17f * s, Mathf.Sin(ang) * 0.62f * s);
            Prim("Drop", PrimitiveType.Sphere, t, m.Crystal, p, Quaternion.identity, new Vector3(0.035f, 0.08f, 0.035f) * s);
        }
        for (int i = 0; i < 10; i++)
        {
            float ang = i * Mathf.PI * 2f / 10f;
            var p = P(Mathf.Cos(ang) * 0.3f * s, c - 0.22f * s, Mathf.Sin(ang) * 0.3f * s);
            Prim("Drop", PrimitiveType.Sphere, t, m.Crystal, p, Quaternion.identity, new Vector3(0.04f, 0.09f, 0.04f) * s);
        }

        if (light && AddRealtimeLights)
            PointLight(t, P(0, c - 0.3f * s, 0), 7.5f, 2.2f);

        void Tier(float r, float y, int arms, float sc)
        {
            var ring = new GameObject("Ring");
            ring.transform.SetParent(t, false);
            ring.transform.position = P(0, y, 0);
            ring.AddComponent<MeshFilter>().sharedMesh = TorusMesh(r, 0.022f * sc);
            ring.AddComponent<MeshRenderer>().sharedMaterial = m.Gold;
            for (int i = 0; i < arms; i++)
            {
                float ang = i * Mathf.PI * 2f / arms;
                var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                var hub = P(0, y, 0);
                var tip = hub + dir * r;
                Strut("Spoke", t, m.Gold, hub, tip, 0.016f * sc);
                Prim("Cup", PrimitiveType.Cylinder, t, m.Gold, tip + Vector3.up * 0.025f * sc, Quaternion.identity, new Vector3(0.07f, 0.025f, 0.07f) * sc);
                Prim("Candle", PrimitiveType.Cylinder, t, m.Candle, tip + Vector3.up * 0.11f * sc, Quaternion.identity, new Vector3(0.035f, 0.065f, 0.035f) * sc);
                Prim("Flame", PrimitiveType.Sphere, t, m.Glow, tip + Vector3.up * 0.21f * sc, Quaternion.identity, new Vector3(0.05f, 0.08f, 0.05f) * sc);
            }
        }
    }

    // --------------------------------------------------- blackjack pendants

    static void BuildBlackjackPendants()
    {
        var g = Group("Blackjack Pendants");
        foreach (var z in new[] { 10.77f, 14.61f, 18.63f })
        {
            var t = Group($"Pendant z{z:F1}", g);
            const float x = 1.6f, barY = 3.45f;
            Prim("Cord", PrimitiveType.Cylinder, t, m.Black, new Vector3(x, (CeilY + barY) * 0.5f, z), Quaternion.identity, new Vector3(0.015f, (CeilY - barY) * 0.5f, 0.015f));
            Prim("Ceiling Cap", PrimitiveType.Cylinder, t, m.Gold, new Vector3(x, CeilY - 0.02f, z), Quaternion.identity, new Vector3(0.14f, 0.02f, 0.14f));
            Box("Bar", t, m.Gold, new Vector3(x, barY, z), Quaternion.identity, new Vector3(0.04f, 0.04f, 1.1f));
            foreach (var dz in new[] { -0.4f, 0.4f })
            {
                var c = new Vector3(x, barY - 0.12f, z + dz);
                Prim("Drop Rod", PrimitiveType.Cylinder, t, m.Gold, new Vector3(x, barY - 0.05f, z + dz), Quaternion.identity, new Vector3(0.015f, 0.05f, 0.015f));
                Prim("Shade", PrimitiveType.Cylinder, t, m.Emerald, c, Quaternion.identity, new Vector3(0.36f, 0.06f, 0.36f));
                Prim("Shade Crown", PrimitiveType.Sphere, t, m.Emerald, c + Vector3.up * 0.05f, Quaternion.identity, new Vector3(0.3f, 0.12f, 0.3f));
                Prim("Shade Rim", PrimitiveType.Cylinder, t, m.Gold, c - Vector3.up * 0.06f, Quaternion.identity, new Vector3(0.37f, 0.008f, 0.37f));
                Prim("Diffuser", PrimitiveType.Cylinder, t, m.Glow, c - Vector3.up * 0.062f, Quaternion.identity, new Vector3(0.32f, 0.004f, 0.32f));
            }
        }
    }

    // ------------------------------------------------------------- bar decor

    static void BuildBarDecor()
    {
        var g = Group("Bar Decor");
        // Globe pendants over the counter (counter runs along z at x ~ -8.3)
        foreach (var z in new[] { 14.7f, 15.71f, 16.72f })
        {
            const float x = -8.1f, y = 2.65f;
            Prim("Pendant Cord", PrimitiveType.Cylinder, g, m.Black, new Vector3(x, (CeilY + y) * 0.5f, z), Quaternion.identity, new Vector3(0.012f, (CeilY - y) * 0.5f, 0.012f));
            Prim("Pendant Cap", PrimitiveType.Cylinder, g, m.Gold, new Vector3(x, y + 0.12f, z), Quaternion.identity, new Vector3(0.09f, 0.03f, 0.09f));
            Prim("Pendant Globe", PrimitiveType.Sphere, g, m.Amber, new Vector3(x, y, z), Quaternion.identity, Vector3.one * 0.24f);
        }
        // LED strips under the two back-bar shelves and under the counter lip
        foreach (var y in new[] { 1.715f, 2.115f })
            Box("Shelf Glow", g, m.Glow, new Vector3(-9.66f, y, 15.71f), Quaternion.identity, new Vector3(0.012f, 0.01f, 2.96f));
        Box("Counter Glow", g, m.Amber, new Vector3(-7.974f, 1.295f, 15.71f), Quaternion.identity, new Vector3(0.008f, 0.014f, 3.18f));
        if (AddRealtimeLights) PointLight(g, new Vector3(-7.9f, 2.5f, 15.71f), 4.5f, 1.4f);
    }

    // ----------------------------------------------------------------- plants

    static void BuildPlants()
    {
        var g = Group("Plants");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ithappy + "Flower_03.prefab");
        if (prefab == null) { Debug.LogWarning("DecorateInterior: Flower_03 prefab not found, no plants."); return; }
        var spots = new (float x, float z, float s, float rot)[]
        {
            (-5.35f, 24.4f, 0.7f, 20f),   // flanking the entrance aisle
            (0.1f, 24.4f, 0.7f, 200f),
            (3.55f, 12.7f, 0.65f, 45f),   // between blackjack tables
            (3.55f, 16.6f, 0.65f, 130f),
            (3.55f, 21.0f, 0.7f, 75f),
            (3.55f, 0.3f, 0.7f, 10f),     // corners
            (-9.4f, 1.7f, 0.55f, 160f),
            (-7.4f, 13.55f, 0.5f, 90f),   // bar ends
            (-7.4f, 17.85f, 0.5f, 250f),
        };
        int n = 0;
        foreach (var p in spots)
            Place(prefab, g, $"Plant {++n}", new Vector3(p.x, FloorY, p.z), p.rot, p.s);
    }

    // ------------------------------------------------------ VIP corner seating

    static void BuildVipCorner()
    {
        var g = Group("VIP Corner");
        var couch = AssetDatabase.LoadAssetAtPath<GameObject>(Ithappy + "Couch_05.prefab");
        var chair = AssetDatabase.LoadAssetAtPath<GameObject>(Ithappy + "Armchair_02.prefab");
        if (couch != null) Place(couch, g, "VIP Couch", new Vector3(-9.45f, FloorY + 0.01f, 9.9f), 90f, 1f);
        if (chair != null)
        {
            Place(chair, g, "VIP Armchair 1", new Vector3(-7.15f, FloorY, 9.35f), 270f, 1f);
            Place(chair, g, "VIP Armchair 2", new Vector3(-7.15f, FloorY, 10.45f), 270f, 1f);
        }
        RoundTable(g, "VIP Coffee Table", new Vector3(-8.2f, FloorY, 9.9f), 0.42f, 0.45f, false);
    }

    static void BuildHighTops()
    {
        var g = Group("Cocktail High-Tops");
        RoundTable(g, "High-Top 1", new Vector3(-6.0f, FloorY, 15.2f), 1.08f, 0.34f, true);
        RoundTable(g, "High-Top 2", new Vector3(-6.0f, FloorY, 17.9f), 1.08f, 0.34f, true);
    }

    static void RoundTable(Transform parent, string name, Vector3 basePos, float height, float radius, bool candle)
    {
        var t = Group(name, parent);
        Prim("Base", PrimitiveType.Cylinder, t, m.Gold, basePos + Vector3.up * 0.015f, Quaternion.identity, new Vector3(radius * 1.2f, 0.015f, radius * 1.2f));
        Prim("Pedestal", PrimitiveType.Cylinder, t, m.Gold, basePos + Vector3.up * height * 0.5f, Quaternion.identity, new Vector3(0.07f, height * 0.5f, 0.07f));
        var top = Prim("Top", PrimitiveType.Cylinder, t, m.Marble, basePos + Vector3.up * (height + 0.02f), Quaternion.identity, new Vector3(radius * 2f, 0.02f, radius * 2f));
        Prim("Top Edge", PrimitiveType.Cylinder, t, m.Gold, basePos + Vector3.up * (height - 0.005f), Quaternion.identity, new Vector3(radius * 2f + 0.02f, 0.012f, radius * 2f + 0.02f));
        // Flat collider so drinks can be set down on it.
        var col = top.AddComponent<BoxCollider>();
        col.size = new Vector3(1f, 2f, 1f) * 0.98f;
        var body = t.gameObject.AddComponent<CapsuleCollider>();
        body.center = basePos + Vector3.up * height * 0.5f - t.position;
        body.radius = 0.08f;
        body.height = height;
        if (candle)
        {
            var c = basePos + Vector3.up * (height + 0.04f);
            Prim("Candle Holder", PrimitiveType.Cylinder, t, m.Amber, c + Vector3.up * 0.05f, Quaternion.identity, new Vector3(0.07f, 0.05f, 0.07f));
            Prim("Candle Rim", PrimitiveType.Cylinder, t, m.Gold, c + Vector3.up * 0.003f, Quaternion.identity, new Vector3(0.08f, 0.003f, 0.08f));
        }
    }

    // -------------------------------------------------------------- stanchions

    static void BuildRopes()
    {
        var g = Group("Velvet Ropes");
        RopeLine(g, "Lounge Rope", new[] { new Vector3(-6.1f, FloorY, 23.3f), new Vector3(-6.1f, FloorY, 25.4f), new Vector3(-6.1f, FloorY, 27.5f) });
        RopeLine(g, "VIP Rope", new[] { new Vector3(-6.25f, FloorY, 8.85f), new Vector3(-6.25f, FloorY, 10.3f) });
    }

    static void RopeLine(Transform parent, string name, Vector3[] posts)
    {
        var t = Group(name, parent);
        const float h = 0.95f;
        foreach (var p in posts)
        {
            Prim("Post Base", PrimitiveType.Cylinder, t, m.Gold, p + Vector3.up * 0.02f, Quaternion.identity, new Vector3(0.3f, 0.02f, 0.3f));
            Prim("Post", PrimitiveType.Cylinder, t, m.Gold, p + Vector3.up * h * 0.5f, Quaternion.identity, new Vector3(0.05f, h * 0.5f, 0.05f));
            Prim("Post Knob", PrimitiveType.Sphere, t, m.Gold, p + Vector3.up * (h + 0.03f), Quaternion.identity, Vector3.one * 0.085f);
        }
        for (int i = 0; i + 1 < posts.Length; i++)
        {
            Vector3 a = posts[i] + Vector3.up * (h - 0.06f), b = posts[i + 1] + Vector3.up * (h - 0.06f);
            const int segs = 10;
            Vector3 prev = a;
            for (int k = 1; k <= segs; k++)
            {
                float u = k / (float)segs;
                var p = Vector3.Lerp(a, b, u) + Vector3.down * (0.22f * 4f * u * (1f - u));
                Strut("Rope", t, m.Velvet, prev, p, 0.035f);
                prev = p;
            }
        }
    }

    // --------------------------------------------------------------- ambience

    static void ApplyAmbience()
    {
        if (!EditorPrefs.HasKey(AmbientPrefKey))
            EditorPrefs.SetString(AmbientPrefKey, string.Join("|",
                ((int)RenderSettings.ambientMode).ToString(), ColorToString(RenderSettings.ambientSkyColor),
                ColorToString(RenderSettings.ambientEquatorColor), ColorToString(RenderSettings.ambientGroundColor),
                RenderSettings.ambientIntensity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.60f, 0.49f, 0.39f);
        RenderSettings.ambientEquatorColor = new Color(0.50f, 0.39f, 0.30f);
        RenderSettings.ambientGroundColor = new Color(0.28f, 0.20f, 0.15f);
    }

    static void RestoreAmbience()
    {
        if (!EditorPrefs.HasKey(AmbientPrefKey)) return;
        var parts = EditorPrefs.GetString(AmbientPrefKey).Split('|');
        if (parts.Length == 5)
        {
            RenderSettings.ambientMode = (AmbientMode)int.Parse(parts[0]);
            RenderSettings.ambientSkyColor = StringToColor(parts[1]);
            RenderSettings.ambientEquatorColor = StringToColor(parts[2]);
            RenderSettings.ambientGroundColor = StringToColor(parts[3]);
            RenderSettings.ambientIntensity = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
        }
        EditorPrefs.DeleteKey(AmbientPrefKey);
    }

    static string ColorToString(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    static Color StringToColor(string s) => ColorUtility.TryParseHtmlString(s, out var c) ? c : Color.gray;

    // ---------------------------------------------------------------- helpers

    static Transform Group(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root, false);
        return go.transform;
    }

    static GameObject Prim(string name, PrimitiveType type, Transform parent, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static GameObject Box(string name, Transform parent, Material mat, Vector3 pos, Quaternion rot, Vector3 size) =>
        Prim(name, PrimitiveType.Cube, parent, mat, pos, rot, size);

    static void Strut(string name, Transform parent, Material mat, Vector3 a, Vector3 b, float thickness)
    {
        var d = b - a;
        Prim(name, PrimitiveType.Cylinder, parent, mat, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized),
            new Vector3(thickness, d.magnitude * 0.5f, thickness));
    }

    static void Quad(string name, Transform parent, Material mat, Vector3 pos, Quaternion rot, float w, float h, float u, float v)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh(w, h, u, v);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void PointLight(Transform parent, Vector3 pos, float range, float intensity)
    {
        var go = new GameObject("Warm Light");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = new Color(1f, 0.76f, 0.5f);
        l.shadows = LightShadows.None;
        l.lightmapBakeType = LightmapBakeType.Realtime;
    }

    static void Place(GameObject prefab, Transform parent, string name, Vector3 pos, float yaw, float scale)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
    }

    static Mesh QuadMesh(float w, float h, float u, float v)
    {
        string path = $"{MeshFolder}/Quad_{w:F2}x{h:F2}_{u:F2}x{v:F2}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
        float x = w * 0.5f, y = h * 0.5f;
        mesh.vertices = new[] { new Vector3(-x, -y, 0), new Vector3(x, -y, 0), new Vector3(-x, y, 0), new Vector3(x, y, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(u, 0), new Vector2(0, v), new Vector2(u, v) };
        mesh.triangles = new[] { 0, 3, 1, 3, 0, 2 };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Mesh TorusMesh(float R, float r)
    {
        string path = $"{MeshFolder}/Torus_{R:F3}_{r:F3}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        const int seg = 32, side = 8;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            for (int j = 0; j <= side; j++)
            {
                float b = j * Mathf.PI * 2f / side;
                var n = new Vector3(Mathf.Cos(b) * Mathf.Cos(a), Mathf.Sin(b), Mathf.Cos(b) * Mathf.Sin(a));
                verts.Add(new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R) + n * r);
                norms.Add(n);
            }
        }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < side; j++)
            {
                int A = i * (side + 1) + j, B = (i + 1) * (side + 1) + j, C = A + 1, D = B + 1;
                tris.AddRange(new[] { A, C, D, A, D, B });
            }
        mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Material MakeMat(string name, Color color, float smooth, float metal, Texture tex = null, Color? emission = null, Texture emissionTex = null)
    {
        string path = $"{Folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Metallic", metal);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.SetTexture("_EmissionMap", emissionTex);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    class Mats
    {
        public readonly Material Gold, DarkWood, Black, Velvet, Marble, Glow, Amber, Candle, Crystal, Shade, Emerald;
        public readonly Material Wainscot, Damask, Ceiling;

        public Mats()
        {
            Gold = MakeMat("Decor Gold", new Color(0.86f, 0.66f, 0.30f), 0.62f, 0.55f, null, new Color(0.10f, 0.07f, 0.02f));
            DarkWood = MakeMat("Decor Dark Wood", new Color(0.17f, 0.085f, 0.045f), 0.5f, 0f);
            Black = MakeMat("Decor Black Lacquer", new Color(0.03f, 0.025f, 0.025f), 0.8f, 0f);
            Velvet = MakeMat("Decor Velvet", new Color(0.5f, 0.03f, 0.08f), 0.3f, 0f, null, new Color(0.08f, 0.0f, 0.01f));
            Glow = MakeMat("Decor Warm Glow", new Color(1f, 0.9f, 0.7f), 0.5f, 0f, null, new Color(1.6f, 1.15f, 0.6f));
            Amber = MakeMat("Decor Amber Glass", new Color(1f, 0.7f, 0.35f), 0.9f, 0f, null, new Color(1.1f, 0.6f, 0.2f));
            Candle = MakeMat("Decor Candle", new Color(1f, 0.96f, 0.88f), 0.3f, 0f, null, new Color(0.55f, 0.5f, 0.42f));
            Crystal = MakeMat("Decor Crystal", new Color(0.95f, 0.95f, 1f), 0.95f, 0.1f, null, new Color(0.7f, 0.65f, 0.58f));
            Shade = MakeMat("Decor Lamp Shade", new Color(0.95f, 0.8f, 0.6f), 0.2f, 0f, null, new Color(0.95f, 0.62f, 0.3f));
            Emerald = MakeMat("Decor Emerald Shade", new Color(0.03f, 0.3f, 0.16f), 0.85f, 0.2f, null, new Color(0.01f, 0.07f, 0.035f));
            Marble = MakeMat("Decor Marble", Color.white, 0.85f, 0f, Textures.Marble());
            Wainscot = MakeMat("Decor Wainscot", Color.white, 0.45f, 0f, Textures.Wainscot());
            Damask = MakeMat("Decor Damask", Color.white, 0.15f, 0f, Textures.Damask(), new Color(0.08f, 0.08f, 0.08f), Textures.Damask());
            Ceiling = MakeMat("Decor Coffered Ceiling", Color.white, 0.3f, 0f, Textures.Ceiling(), new Color(0.06f, 0.06f, 0.06f), Textures.Ceiling());
        }
    }

    // ------------------------------------------------------------- textures

    enum RugStyle { Runner, Medallion, Trellis, Deco, Burgundy, Lounge }

    static class Textures
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        static Texture2D Make(string name, int w, int h, bool repeat, Func<float, float, Color> f)
        {
            if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{TexFolder}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.filterMode = FilterMode.Trilinear;
            imp.sRGBTexture = true;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.SaveAndReimport();
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            cache[name] = result;
            return result;
        }

        static float Frac(float v) => v - Mathf.Floor(v);
        static float Band(float d, float width, float aa = 0.004f) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width, width + aa, Mathf.Abs(d)));
        static float Fbm(float x, float y)
        {
            float s = 0f, a = 0.5f;
            for (int i = 0; i < 4; i++) { s += a * Mathf.PerlinNoise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; }
            return s / 0.9375f;
        }
        static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        public static Texture2D Damask() => Make("Damask", 256, 256, true, (u, v) =>
        {
            var bas = new Color(0.27f, 0.045f, 0.065f);
            var motif = new Color(0.34f, 0.065f, 0.085f);
            var gold = new Color(0.62f, 0.46f, 0.22f);
            float fill = 0f, ring = 0f;
            var centers = new[] { new Vector3(0.5f, 0.5f, 1f), new Vector3(0, 0, 0.7f), new Vector3(1, 0, 0.7f), new Vector3(0, 1, 0.7f), new Vector3(1, 1, 0.7f) };
            foreach (var c in centers)
            {
                float dx = (u - c.x) / c.z, dy = (v - c.y) / c.z * 0.8f;
                float r = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx);
                float petal = 0.75f + 0.25f * Mathf.Cos(6f * th);
                fill = Mathf.Max(fill, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f * petal, 0.2f * petal + 0.01f, r)));
                ring = Mathf.Max(ring, Band(r - 0.3f, 0.008f, 0.006f));
                fill = Mathf.Max(fill, Band(r - 0.09f, 0.02f) * 0.5f);
            }
            float diamond = Band(Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) - 0.5f, 0.004f, 0.006f);
            var col = Mix(bas, motif, fill);
            col = Mix(col, gold, ring * 0.4f + diamond * 0.25f);
            return col * (0.95f + 0.1f * Mathf.PerlinNoise(u * 40f, v * 40f));
        });

        public static Texture2D Wainscot() => Make("Wainscot", 256, 256, true, (u, v) =>
        {
            float grain = Fbm(u * 3f, v * 30f);
            var wood = Mix(new Color(0.13f, 0.06f, 0.03f), new Color(0.25f, 0.13f, 0.07f), grain);
            const float m = 0.1f, bev = 0.03f;
            float inX = Mathf.Min(u - m, 1f - m - u), inY = Mathf.Min(v - m * 1.3f, 1f - m * 1.3f - v);
            float inside = Mathf.Min(inX, inY);
            if (inside > 0f && inside < bev)
            {
                bool lit = (inX < inY) ? (u < 0.5f) : (v > 0.5f);
                wood *= lit ? 1.35f : 0.6f;
            }
            else if (inside >= bev) wood *= 0.9f;
            if (Mathf.Abs(inside) < 0.004f) wood = Mix(wood, new Color(0.6f, 0.45f, 0.2f), 0.6f);
            wood.a = 1f;
            return wood;
        });

        public static Texture2D Ceiling() => Make("Ceiling Coffers", 256, 256, true, (u, v) =>
        {
            var frame = new Color(0.12f, 0.065f, 0.035f) * (0.85f + 0.3f * Fbm(u * 2f, v * 25f));
            var field = new Color(0.14f, 0.05f, 0.08f);
            var gold = new Color(0.7f, 0.52f, 0.25f);
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            if (edge < 0.07f) { frame.a = 1f; return frame; }
            var col = field * (0.9f + 0.2f * Mathf.InverseLerp(0.07f, 0.5f, edge));
            if (edge < 0.1f) col *= 0.7f;
            col = Mix(col, gold, Band(edge - 0.12f, 0.004f));
            float r = new Vector2(u - 0.5f, v - 0.5f).magnitude;
            float th = Mathf.Atan2(v - 0.5f, u - 0.5f);
            col = Mix(col, gold, Band(r - 0.08f * (0.8f + 0.2f * Mathf.Cos(8f * th)), 0.006f) + (r < 0.03f ? 1f : 0f));
            col.a = 1f;
            return col;
        });

        public static Texture2D Marble() => Make("Marble", 256, 256, true, (u, v) =>
        {
            float n = Fbm(u * 3f, v * 3f);
            float vein = 1f - Mathf.Abs(Mathf.Sin((u * 4f + v * 2f + n * 4f) * Mathf.PI));
            vein = Mathf.Pow(vein, 14f);
            float fine = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((u * 9f - v * 5f + n * 6f) * Mathf.PI)), 30f);
            var col = Mix(new Color(0.05f, 0.05f, 0.055f), new Color(0.11f, 0.1f, 0.1f), n);
            col = Mix(col, new Color(0.85f, 0.7f, 0.4f), vein * 0.9f);
            col = Mix(col, new Color(0.8f, 0.8f, 0.78f), fine * 0.5f);
            col.a = 1f;
            return col;
        });

        public static Texture2D Rug(string name, RugStyle style, float wm, float hm)
        {
            const float ppm = 96f;
            float scale = Mathf.Min(1f, 1024f / (Mathf.Max(wm, hm) * ppm));
            int w = Mathf.Max(32, Mathf.RoundToInt(wm * ppm * scale));
            int h = Mathf.Max(32, Mathf.RoundToInt(hm * ppm * scale));

            Color field, accent, border;
            switch (style)
            {
                case RugStyle.Runner: field = new Color(0.42f, 0.04f, 0.06f); accent = new Color(0.78f, 0.58f, 0.25f); border = new Color(0.08f, 0.03f, 0.03f); break;
                case RugStyle.Medallion: field = new Color(0.03f, 0.2f, 0.12f); accent = new Color(0.8f, 0.62f, 0.28f); border = new Color(0.02f, 0.07f, 0.05f); break;
                case RugStyle.Trellis: field = new Color(0.05f, 0.07f, 0.18f); accent = new Color(0.75f, 0.58f, 0.27f); border = new Color(0.4f, 0.05f, 0.07f); break;
                case RugStyle.Deco: field = new Color(0.04f, 0.035f, 0.04f); accent = new Color(0.82f, 0.63f, 0.3f); border = new Color(0.36f, 0.04f, 0.06f); break;
                case RugStyle.Burgundy: field = new Color(0.3f, 0.04f, 0.07f); accent = new Color(0.8f, 0.6f, 0.28f); border = new Color(0.05f, 0.03f, 0.03f); break;
                default: field = new Color(0.78f, 0.7f, 0.56f); accent = new Color(0.45f, 0.06f, 0.09f); border = new Color(0.3f, 0.05f, 0.07f); break;
            }

            return Make("Rug " + name, w, h, false, (u, v) =>
            {
                float x = u * wm, y = v * hm;
                float edge = Mathf.Min(Mathf.Min(x, wm - x), Mathf.Min(y, hm - y));
                float wool = 0.92f + 0.16f * Fbm(x * 6f, y * 6f);
                Color col;
                if (edge < 0.04f) col = accent * 0.8f;                         // binding
                else if (edge < 0.22f)                                            // border band
                {
                    col = border;
                    float along = (Mathf.Min(x, wm - x) < Mathf.Min(y, hm - y)) ? y : x;
                    float d = Mathf.Abs(Frac(along / 0.25f) - 0.5f) + Mathf.Abs(edge - 0.13f) / 0.25f * 2f;
                    col = Mix(col, accent, Band(d - 0.28f, 0.03f, 0.03f));
                }
                else
                {
                    col = field;
                    col = Mix(col, accent, Band(edge - 0.27f, 0.012f, 0.008f));  // inner line
                    float cx = x - wm * 0.5f, cy = y - hm * 0.5f;
                    switch (style)
                    {
                        case RugStyle.Runner:
                        case RugStyle.Burgundy:
                        {
                            float fx = Frac(cx / 0.6f) - 0.5f, fy = Frac(y / 0.6f) - 0.5f;
                            float dd = Mathf.Abs(fx) + Mathf.Abs(fy);
                            col = Mix(col, accent, Band(dd - 0.3f, 0.025f, 0.02f) + (dd < 0.07f ? 1f : 0f));
                            break;
                        }
                        case RugStyle.Trellis:
                        {
                            float fx = Frac(cx / 0.5f), fy = Frac(cy / 0.5f);
                            float lat = Mathf.Min(Mathf.Abs(fx - fy), Mathf.Abs(fx + fy - 1f));
                            float ring = new Vector2(fx - 0.5f, fy - 0.5f).magnitude;
                            col = Mix(col, accent, Band(lat, 0.018f, 0.015f) * 0.8f + Band(ring - 0.12f, 0.015f, 0.012f));
                            break;
                        }
                        case RugStyle.Deco:
                        {
                            float fy = Frac(cy / 0.5f);
                            float chev = Frac(cx / 0.5f + Mathf.Abs(fy - 0.5f));
                            col = Mix(col, accent, Band(chev - 0.5f, 0.04f, 0.02f) * 0.85f);
                            break;
                        }
                        default: // Medallion / Lounge
                        {
                            float rx = cx / (wm * 0.5f - 0.3f), ry = cy / (hm * 0.5f - 0.3f);
                            float r = Mathf.Sqrt(rx * rx + ry * ry);
                            float th = Mathf.Atan2(ry, rx);
                            float petal = 0.42f + 0.06f * Mathf.Cos(12f * th);
                            if (r < petal) col = Mix(field, accent, 0.25f + 0.75f * Band(r - petal * 0.6f, 0.03f, 0.03f));
                            col = Mix(col, accent, Band(r - petal - 0.04f, 0.012f, 0.01f) + Band(r - 0.15f, 0.015f, 0.01f));
                            float fx = Frac(cx / 0.7f) - 0.5f, fy = Frac(cy / 0.7f) - 0.5f;
                            if (r > petal + 0.1f) col = Mix(col, accent, (Mathf.Abs(fx) + Mathf.Abs(fy) < 0.06f) ? 0.8f : 0f);
                            break;
                        }
                    }
                }
                col *= wool;
                col.a = 1f;
                return col;
            });
        }

        // Generic abstract paintings in a handful of styles (no references to real works).
        public static Texture2D Art(int style, float aspect)
        {
            int w = 384, h = Mathf.Clamp(Mathf.RoundToInt(384 / aspect), 96, 512);
            var rnd = new System.Random(1234 + style * 97);
            float R() => (float)rnd.NextDouble();
            var blobs = new Vector4[12];
            for (int i = 0; i < blobs.Length; i++) blobs[i] = new Vector4(R(), R(), 0.05f + R() * 0.18f, R());
            var gold = new Color(0.85f, 0.66f, 0.3f);

            return Make($"Art {style} {aspect:F2}", w, h, false, (u, v) =>
            {
                float x = u * aspect, y = v;
                Color col;
                switch (style)
                {
                    case 0: // deco sunburst
                    {
                        float dx = x - aspect * 0.5f, dy = y + 0.1f;
                        float th = Mathf.Atan2(dy, dx), r = Mathf.Sqrt(dx * dx + dy * dy);
                        bool ray = Frac(th / Mathf.PI * 14f) < 0.5f;
                        col = ray ? new Color(0.06f, 0.06f, 0.08f) : new Color(0.12f, 0.1f, 0.12f);
                        col = Mix(col, gold, Band(Frac(r * 5f) - 0.5f, 0.03f, 0.02f) * (r > 0.35f ? 1f : 0f) + (r < 0.3f ? 1f : 0f));
                        if (r < 0.3f) col *= 0.85f + 0.15f * Mathf.Cos(r * 40f);
                        break;
                    }
                    case 1: // soft colour fields
                    {
                        col = new Color(0.35f, 0.07f, 0.06f);
                        float b1 = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.04f, 0.0f, Mathf.Max(Mathf.Abs(y - 0.68f) - 0.17f, Mathf.Abs(u - 0.5f) - 0.4f)));
                        float b2 = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.04f, 0.0f, Mathf.Max(Mathf.Abs(y - 0.27f) - 0.14f, Mathf.Abs(u - 0.5f) - 0.4f)));
                        col = Mix(col, new Color(0.85f, 0.45f, 0.12f), b1);
                        col = Mix(col, new Color(0.55f, 0.12f, 0.08f), b2);
                        col *= 0.9f + 0.15f * Fbm(u * 8f, v * 8f);
                        break;
                    }
                    case 2: // offset concentric rings
                    {
                        float r = new Vector2(x - aspect * 0.38f, y - 0.55f).magnitude;
                        int band = Mathf.FloorToInt(r * 9f);
                        var a = new Color(0.07f, 0.12f, 0.25f); var b = new Color(0.9f, 0.84f, 0.7f);
                        col = (band % 3 == 0) ? a : (band % 3 == 1) ? b : gold;
                        col = Mix(col, new Color(0.05f, 0.08f, 0.16f), Mathf.InverseLerp(0.6f, 1.2f, r));
                        break;
                    }
                    case 3: // abstract landscape at dusk
                    {
                        col = Mix(new Color(0.95f, 0.62f, 0.32f), new Color(0.22f, 0.14f, 0.32f), y);
                        float sun = new Vector2(x - aspect * 0.65f, y - 0.55f).magnitude;
                        col = Mix(col, new Color(1f, 0.88f, 0.6f), 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.1f, 0.12f, sun)));
                        for (int k = 0; k < 3; k++)
                        {
                            float hill = 0.42f - k * 0.12f + 0.06f * Mathf.Sin(x * (3f + k * 2f) + k * 1.7f) + 0.03f * Mathf.Sin(x * 11f + k);
                            if (y < hill) col = Mix(new Color(0.3f, 0.15f, 0.2f), new Color(0.08f, 0.05f, 0.09f), k / 2f);
                        }
                        break;
                    }
                    case 4: // gold line composition on black
                    {
                        col = new Color(0.04f, 0.04f, 0.05f) * (0.9f + 0.2f * Fbm(u * 5f, v * 5f));
                        float lines = 0f;
                        for (int i = 0; i < 6; i++)
                        {
                            var bl = blobs[i];
                            float r = new Vector2(x - bl.x * aspect, y - bl.y).magnitude;
                            lines = Mathf.Max(lines, Band(r - bl.z * 2.5f, 0.004f, 0.003f));
                        }
                        lines = Mathf.Max(lines, Band(y - 0.33f - 0.2f * u, 0.003f));
                        col = Mix(col, gold, lines);
                        float disc = new Vector2(x - blobs[7].x * aspect, y - blobs[7].y).magnitude;
                        if (disc < 0.1f) col = new Color(0.62f, 0.08f, 0.09f);
                        break;
                    }
                    case 5: // soft brushed blobs
                    {
                        col = new Color(0.88f, 0.84f, 0.76f);
                        var pal = new[] { new Color(0.08f, 0.35f, 0.38f), gold, new Color(0.6f, 0.1f, 0.12f), new Color(0.12f, 0.12f, 0.2f) };
                        for (int i = 0; i < blobs.Length; i++)
                        {
                            var bl = blobs[i];
                            float dx = (x - bl.x * aspect) / (bl.z * 1.6f), dy = (y - bl.y) / bl.z;
                            float wobble = 0.15f * Mathf.Sin(Mathf.Atan2(dy, dx) * 5f + i);
                            float k = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.85f + wobble, 1.05f + wobble, Mathf.Sqrt(dx * dx + dy * dy)));
                            col = Mix(col, pal[i % pal.Length], k * 0.85f);
                        }
                        col *= 0.93f + 0.1f * Fbm(u * 30f, v * 6f);
                        break;
                    }
                    case 6: // deco scallop fans
                    {
                        float sx = x * 6f, sy = y * 6f;
                        float row = Mathf.Floor(sy);
                        float fx = Frac(sx + (row % 2f) * 0.5f) - 0.5f, fy = Frac(sy);
                        float r = new Vector2(fx, fy).magnitude;
                        col = (Frac(r * 4f) < 0.5f) ? new Color(0.05f, 0.18f, 0.16f) : new Color(0.08f, 0.26f, 0.22f);
                        col = Mix(col, gold, Band(r - 0.5f, 0.02f, 0.015f));
                        break;
                    }
                    default: // fluid marble with gold veins
                    {
                        float n = Fbm(x * 2.5f, y * 2.5f);
                        float t = Frac(n * 3f + x * 0.3f);
                        col = Mix(new Color(0.08f, 0.1f, 0.2f), new Color(0.5f, 0.12f, 0.16f), Mathf.SmoothStep(0, 1, t));
                        col = Mix(col, new Color(0.92f, 0.88f, 0.8f), Mathf.Pow(Mathf.Abs(Mathf.Sin(n * 18f)), 30f) * 0.7f);
                        col = Mix(col, gold, Band(t - 0.5f, 0.015f, 0.01f));
                        break;
                    }
                }
                col *= 0.96f + 0.06f * Mathf.PerlinNoise(u * 120f, v * 120f); // canvas tooth
                col.a = 1f;
                return col;
            });
        }
    }
}
