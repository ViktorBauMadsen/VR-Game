using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Tools > Casino > Decorate Exterior
//
// Turns the plain brown box into a grand casino: limestone facades with a
// granite plinth, pilasters, glowing arched windows, belt course, cornice and
// gold-capped parapet; an art-deco tower with a spire on the roof; a dressed
// porte-cochere (stone fascia, gold trim, marquee bulbs, coffered underside
// with downlights, marble-clad columns, "GRAND CASINO" sign); a marble lobby
// floor and red carpet with gold stanchions; and grounds - lawn, paved forecourt
// and walkways, a tiered fountain, palms, topiaries, hedges, lamp posts and a
// stone-and-iron boundary with gate pillars.
//
// Built under one "Exterior Decor" root (re-running replaces it; Tools > Casino
// > Remove Exterior Decor deletes it). Existing objects are not moved, disabled
// or deleted - the two existing entrance columns are encased in new marble
// shafts. "Tools > Casino > Evening Mode (toggle)" optionally swaps in a dusk
// sky and warms/lowers the sun (fully restorable by toggling again).
public static partial class DecorateInterior
{
    const string ExteriorRootName = "Exterior Decor";
    const string EveningPrefKey = "DecorateExterior.EveningPrev";

    // Exterior shell of the building (from the wall/roof cubes under "Assets").
    const float GroundY = 0.40f, RoofY = 7.11f;
    const float XWest = -10.07f, XEast = 4.27f, XSouth = -0.45f, XNorth = 28.225f;
    // Porte-cochere (canopy) blocks
    const float CanW = -7.31f, CanE = 2.21f, CanFront = 31.62f, CanBack = 28.23f, CanUnder = 5.32f;
    const float CanCenterW = -4.26f, CanCenterE = -0.79f, CanSideTop = 6.35f, CanCenterTop = 7.19f;

    // Layer heights (kept >= 1.5 cm apart so nothing z-fights at distance in VR).
    const float LawnY = 0.43f, PaveY = 0.445f, LobbyY = 0.455f, CarpetY = 0.462f;

    static readonly Wall XW = new Wall(new Vector3(XWest, 0, 0), Vector3.forward, Vector3.left);
    static readonly Wall XE = new Wall(new Vector3(XEast, 0, 0), Vector3.forward, Vector3.right);
    static readonly Wall XS = new Wall(new Vector3(0, 0, XSouth), Vector3.right, Vector3.back);
    static readonly Wall XN = new Wall(new Vector3(0, 0, XNorth), Vector3.right, Vector3.forward);

    static ExtMats x;

    [MenuItem("Tools/Casino/Decorate Exterior")]
    static void DecorateExteriorMenu()
    {
        var old = GameObject.Find(ExteriorRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        CollectObstacles();
        EnsureFolder(Folder);
        EnsureFolder(TexFolder);
        EnsureFolder(MeshFolder);
        m = new Mats();
        x = new ExtMats();

        var go = new GameObject(ExteriorRootName);
        Undo.RegisterCreatedObjectUndo(go, "Decorate Exterior");
        root = go.transform;

        BuildFacades();
        BuildRoof();
        BuildCanopy();
        BuildGround();
        BuildFountain();
        BuildPlanting();
        BuildLampPosts();
        BuildBoundary();

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Light>() == null && t.GetComponent<TextMeshPro>() == null)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = null;
        Debug.Log($"DecorateExterior: built '{ExteriorRootName}' ({root.GetComponentsInChildren<Transform>().Length} objects). Save the scene to keep it.");
    }

    [MenuItem("Tools/Casino/Remove Exterior Decor")]
    static void RemoveExterior()
    {
        var old = GameObject.Find(ExteriorRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // --------------------------------------------------------------- facades

    static void BuildFacades()
    {
        var g = Group("Facades");
        var runs = new List<(Wall w, float a0, float a1, float y0, string name)>
        {
            (XW, XSouth, XNorth, GroundY, "West"),
            (XE, XSouth, XNorth, GroundY, "East"),
            (XS, XWest, XEast, GroundY, "South"),
            (XN, XWest, DoorMinX, GroundY, "Front L"),
            (XN, DoorMaxX, XEast, GroundY, "Front R"),
            (XN, DoorMinX, DoorMaxX, 4.45f, "Front Over Door"),
        };
        const float stoneTop = 6.75f;
        foreach (var (w, a0, a1, y0, name) in runs)
        {
            var fg = Group(name, g);
            float len = a1 - a0, mid = (a0 + a1) * 0.5f, h = stoneTop - y0;
            Quad("Limestone", fg, x.Limestone, w.P(mid, y0 + h * 0.5f, 0.006f), w.Rot, len, h, len / 2.4f, h / 0.9f);
            if (y0 <= GroundY + 0.01f)
                Rail("Plinth", fg, w, a0, a1, GroundY + 0.3f, 0.6f, 0.09f, x.Granite);
            Rail("Belt Gold", fg, w, a0, a1, 5.245f, 0.04f, 0.085f, m.Gold);
            Rail("Belt Course", fg, w, a0, a1, 5.35f, 0.16f, 0.08f, x.StoneTrim);
            Rail("Cornice Gold", fg, w, a0, a1, 6.73f, 0.05f, 0.2f, m.Gold);
            Rail("Cornice", fg, w, a0, a1, 6.93f, 0.36f, 0.3f, x.StoneTrim);
        }

        // Pilasters with windows between them.
        Bays(g, "West", XW, Spread(XSouth + 0.3f, XNorth - 0.3f, 8));
        Bays(g, "East", XE, Spread(XSouth + 0.3f, XNorth - 0.3f, 8));
        Bays(g, "South", XS, Spread(XWest + 0.3f, XEast - 0.3f, 5));
        Bays(g, "Front", XN, new[] { XWest + 0.3f, -7.0f });
        Pilaster(g, XN, XEast - 0.3f);
    }

    static float[] Spread(float a, float b, int n)
    {
        var r = new float[n];
        for (int i = 0; i < n; i++) r[i] = Mathf.Lerp(a, b, i / (float)(n - 1));
        return r;
    }

    static void Bays(Transform parent, string name, Wall w, float[] pil)
    {
        var g = Group(name + " Bays", parent);
        foreach (var a in pil) Pilaster(g, w, a);
        for (int i = 0; i + 1 < pil.Length; i++)
        {
            float c = (pil[i] + pil[i + 1]) * 0.5f, gap = pil[i + 1] - pil[i] - 0.55f;
            if (gap < 1.6f) continue;
            float ww = Mathf.Min(1.4f, gap - 0.9f);
            Window(g, w, c, ww, 3.3f, 3.0f, true);
            Window(g, w, c, Mathf.Min(1.0f, ww), 0.6f, 6.0f, false);
        }
    }

    static void Pilaster(Transform parent, Wall w, float a)
    {
        if (!Free(w.Box(a, 3.7f, 0.08f, 0.72f, 6.6f, 0.16f), "pilaster")) return;
        var t = Group("Pilaster", parent);
        Box("Shaft", t, x.StoneTrim, w.P(a, 3.85f, 0.06f), w.Rot, new Vector3(0.55f, 5.7f, 0.12f));
        Box("Base", t, x.Granite, w.P(a, GroundY + 0.33f, 0.08f), w.Rot, new Vector3(0.7f, 0.66f, 0.16f));
        Box("Capital", t, m.Gold, w.P(a, 6.62f, 0.09f), w.Rot, new Vector3(0.7f, 0.16f, 0.18f));
    }

    static void Window(Transform parent, Wall w, float a, float width, float height, float yc, bool arched)
    {
        if (!Free(w.Box(a, yc, 0.08f, width + 0.4f, height + 0.4f, 0.16f), "window")) return;
        var t = Group(arched ? "Window" : "Attic Window", parent);
        var mat = x.WindowMat(width, height, arched);
        Quad("Glass", t, mat, w.P(a, yc, 0.012f), w.Rot, width, height, 1f, 1f);
        const float f = 0.07f;
        Box("Frame Bottom", t, x.Bronze, w.P(a, yc - height * 0.5f - f * 0.5f, 0.03f), w.Rot, new Vector3(width + 2 * f, f, 0.06f));
        Box("Frame L", t, x.Bronze, w.P(a - width * 0.5f - f * 0.5f, yc, 0.03f), w.Rot, new Vector3(f, height, 0.06f));
        Box("Frame R", t, x.Bronze, w.P(a + width * 0.5f + f * 0.5f, yc, 0.03f), w.Rot, new Vector3(f, height, 0.06f));
        if (!arched) Box("Frame Top", t, x.Bronze, w.P(a, yc + height * 0.5f + f * 0.5f, 0.03f), w.Rot, new Vector3(width + 2 * f, f, 0.06f));
        Box("Sill", t, x.StoneTrim, w.P(a, yc - height * 0.5f - f - 0.05f, 0.08f), w.Rot, new Vector3(width + 0.3f, 0.1f, 0.16f));
        if (arched)
        {
            Box("Hood", t, x.StoneTrim, w.P(a, yc + height * 0.5f + 0.1f, 0.07f), w.Rot, new Vector3(width + 0.4f, 0.12f, 0.14f));
            Box("Keystone", t, m.Gold, w.P(a, yc + height * 0.5f - 0.02f, 0.05f), w.Rot, new Vector3(0.2f, 0.3f, 0.1f));
        }
    }

    // ------------------------------------------------------------------ roof

    static void BuildRoof()
    {
        var g = Group("Roof");
        // Slate roof deck, glass skylight over the central ridge, and covers for the canopy tops.
        Flat(g, "Roof Deck", x.Slate, XWest, XEast, -0.38f, 28.22f, RoofY + 0.006f, 1.5f);
        Box("Skylight", g, x.Glass, new Vector3(-2.53f, 7.465f, 15.635f), Quaternion.identity, new Vector3(2.14f, 0.59f, 32.03f));
        for (float z = 0f; z <= 31.6f; z += 2.0f)
            Box("Skylight Rib", g, m.Gold, new Vector3(-2.53f, 7.47f, z), Quaternion.identity, new Vector3(2.18f, 0.62f, 0.05f));
        Flat(g, "Canopy Top L", x.Slate, CanW, CanCenterW, 27.92f, CanFront, CanSideTop + 0.006f, 1.5f);
        Flat(g, "Canopy Top R", x.Slate, CanCenterE, CanE, 27.92f, CanFront, CanSideTop + 0.006f, 1.5f);
        Flat(g, "Canopy Top C", x.Slate, CanCenterW, CanCenterE, 27.92f, CanFront, CanCenterTop + 0.006f, 1.5f);
        foreach (var (sxp, nrm) in new[] { (CanCenterW, Vector3.left), (CanCenterE, Vector3.right) })
        {
            var w = new Wall(new Vector3(sxp, 0, 0), Vector3.forward, nrm);
            float h = CanCenterTop - CanSideTop, len = CanFront - 27.92f;
            Quad("Canopy Step", g, x.Limestone, w.P((27.92f + CanFront) * 0.5f, CanSideTop + h * 0.5f, 0.006f), w.Rot, len, h, len / 2.4f, h / 0.9f);
            Box("Canopy Step Gold", g, m.Gold, w.P((27.92f + CanFront) * 0.5f, CanCenterTop - 0.03f, 0.03f), w.Rot, new Vector3(len, 0.06f, 0.06f));
        }
        var edges = new (Wall w, float a0, float a1)[] { (XW, XSouth, XNorth), (XE, XSouth, XNorth), (XS, XWest, XEast), (XN, XWest, XEast) };
        foreach (var (w, a0, a1) in edges)
        {
            float mid = (a0 + a1) * 0.5f, len = a1 - a0;
            Box("Parapet", g, x.StoneTrim, w.P(mid, RoofY + 0.27f, 0.0f), w.Rot, new Vector3(len, 0.54f, 0.3f));
            Box("Parapet Coping", g, m.Gold, w.P(mid, RoofY + 0.56f, 0.0f), w.Rot, new Vector3(len, 0.04f, 0.34f));
        }
        foreach (var c in new[] { new Vector3(XWest, 0, XSouth), new Vector3(XEast, 0, XSouth), new Vector3(XWest, 0, XNorth), new Vector3(XEast, 0, XNorth) })
        {
            Box("Corner Pedestal", g, x.StoneTrim, new Vector3(c.x, RoofY + 0.45f, c.z), Quaternion.identity, new Vector3(0.6f, 0.9f, 0.6f));
            Prim("Corner Urn", PrimitiveType.Sphere, g, m.Gold, new Vector3(c.x, RoofY + 1.08f, c.z), Quaternion.identity, new Vector3(0.36f, 0.4f, 0.36f));
            Prim("Corner Finial", PrimitiveType.Cylinder, g, m.Gold, new Vector3(c.x, RoofY + 1.38f, c.z), Quaternion.identity, new Vector3(0.06f, 0.14f, 0.06f));
        }

        // Art-deco tower over the entrance
        var t = Group("Entrance Tower", g);
        const float cx = -2.7f;
        float y = RoofY;
        var tiers = new[] { new Vector3(3.6f, 2.0f, 2.5f), new Vector3(2.8f, 1.3f, 1.9f), new Vector3(1.9f, 0.9f, 1.3f) };
        foreach (var s in tiers)
        {
            float cz = 28.1f - s.z * 0.5f - (2.5f - s.z) * 0.3f;
            Box("Tier", t, x.StoneTrim, new Vector3(cx, y + s.y * 0.5f, cz), Quaternion.identity, s);
            Box("Tier Band", t, m.Gold, new Vector3(cx, y + s.y - 0.06f, cz), Quaternion.identity, new Vector3(s.x + 0.08f, 0.1f, s.z + 0.08f));
            foreach (var sx in new[] { -1f, 1f })
                Box("Corner Fin", t, m.Gold, new Vector3(cx + sx * s.x * 0.5f, y + s.y * 0.5f, cz + s.z * 0.5f), Quaternion.identity, new Vector3(0.1f, s.y - 0.1f, 0.1f));
            // Glowing vertical slots on the front
            for (int i = -2; i <= 2; i++)
            {
                if (s.x < 2.5f && Mathf.Abs(i) == 2) continue;
                float fx = cx + i * s.x * 0.18f;
                Box("Light Slot", t, x.WindowGlow, new Vector3(fx, y + s.y * 0.45f, cz + s.z * 0.5f + 0.005f), Quaternion.identity, new Vector3(0.12f, s.y * 0.6f, 0.01f));
            }
            y += s.y;
        }
        // Medallion on the second tier front
        float medZ = 28.1f - tiers[1].z * 0.5f - (2.5f - tiers[1].z) * 0.3f + tiers[1].z * 0.5f + 0.04f;
        Prim("Medallion", PrimitiveType.Cylinder, t, m.Gold, new Vector3(cx, RoofY + 2.0f + 0.65f, medZ), Quaternion.Euler(90f, 0f, 0f), new Vector3(1.0f, 0.03f, 1.0f));
        Prim("Medallion Glow", PrimitiveType.Cylinder, t, m.Amber, new Vector3(cx, RoofY + 2.0f + 0.65f, medZ + 0.035f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.75f, 0.01f, 0.75f));
        for (int i = 0; i < 12; i++)
        {
            float ang = i * 30f;
            var dir = Quaternion.Euler(0, 0, ang) * Vector3.up;
            var p = new Vector3(cx, RoofY + 2.65f, medZ + 0.02f) + dir * 0.62f;
            Box("Ray", t, m.Gold, p, Quaternion.Euler(0, 0, ang), new Vector3(0.05f, 0.28f, 0.03f));
        }
        Prim("Spire", PrimitiveType.Cylinder, t, m.Gold, new Vector3(cx, y + 1.0f, 28.1f - tiers[2].z * 0.5f - (2.5f - tiers[2].z) * 0.3f), Quaternion.identity, new Vector3(0.16f, 1.0f, 0.16f));
        Prim("Spire Orb", PrimitiveType.Sphere, t, m.Glow, new Vector3(cx, y + 2.1f, 28.1f - tiers[2].z * 0.5f - (2.5f - tiers[2].z) * 0.3f), Quaternion.identity, Vector3.one * 0.32f);
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;
    }

    // ---------------------------------------------------------------- canopy

    static void BuildCanopy()
    {
        var g = Group("Porte-Cochere");
        var front = new Wall(new Vector3(0, 0, CanFront), Vector3.right, Vector3.forward);
        var sideW = new Wall(new Vector3(CanW, 0, 0), Vector3.forward, Vector3.left);
        var sideE = new Wall(new Vector3(CanE, 0, 0), Vector3.forward, Vector3.right);

        void Fascia(Wall w, float a0, float a1, float top)
        {
            float mid = (a0 + a1) * 0.5f, len = a1 - a0, h = top - CanUnder;
            Quad("Fascia", g, x.Limestone, w.P(mid, CanUnder + h * 0.5f, 0.007f), w.Rot, len, h, len / 2.4f, h / 0.9f);
            Box("Fascia Gold Base", g, m.Gold, w.P(mid, CanUnder + 0.03f, 0.03f), w.Rot, new Vector3(len + 0.06f, 0.07f, 0.06f));
            Box("Fascia Gold Top", g, m.Gold, w.P(mid, top - 0.03f, 0.03f), w.Rot, new Vector3(len + 0.06f, 0.06f, 0.06f));
            Bulbs(w, a0 + 0.12f, a1 - 0.12f, CanUnder + 0.17f);
            Bulbs(w, a0 + 0.12f, a1 - 0.12f, top - 0.14f);
        }
        void Bulbs(Wall w, float a0, float a1, float y)
        {
            int n = Mathf.FloorToInt((a1 - a0) / 0.3f);
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)Mathf.Max(1, n));
                if (!FreeQuiet(w.Box(a, y, 0.04f, 0.08f, 0.08f, 0.08f))) continue;
                Box("Bulb", g, x.Bulb, w.P(a, y, 0.04f), w.Rot * Quaternion.Euler(0, 0, 45f), Vector3.one * 0.07f);
            }
        }
        Fascia(front, CanW, CanCenterW, CanSideTop);
        Fascia(front, CanCenterW, CanCenterE, CanCenterTop);
        Fascia(front, CanCenterE, CanE, CanSideTop);
        Fascia(sideW, CanBack - 0.3f, CanFront, CanSideTop);
        Fascia(sideE, CanBack - 0.3f, CanFront, CanSideTop);

        // Coffered underside with downlights
        Quad("Canopy Ceiling", g, m.Ceiling, new Vector3((CanW + CanE) * 0.5f, CanUnder - 0.004f, (CanBack + CanFront) * 0.5f),
            Quaternion.LookRotation(Vector3.up, Vector3.forward), CanE - CanW, CanFront - CanBack, (CanE - CanW) / 2f, (CanFront - CanBack) / 2f);
        for (float px = -6.4f; px <= 1.6f; px += 1.6f)
            foreach (var pz in new[] { 29.1f, 30.8f })
            {
                Prim("Downlight Trim", PrimitiveType.Cylinder, g, m.Gold, new Vector3(px, CanUnder - 0.012f, pz), Quaternion.identity, new Vector3(0.3f, 0.006f, 0.3f));
                Prim("Downlight", PrimitiveType.Cylinder, g, m.Glow, new Vector3(px, CanUnder - 0.016f, pz), Quaternion.identity, new Vector3(0.22f, 0.006f, 0.22f));
            }

        // Marble columns encasing the two existing entrance columns
        foreach (var c in new[] { new Vector3(0.45f, 0, 31.12f), new Vector3(-5.73f, 0, 31.12f) })
        {
            var t = Group("Column", g);
            float top = CanUnder;
            Prim("Plinth", PrimitiveType.Cube, t, x.Granite, new Vector3(c.x, GroundY + 0.2f, c.z), Quaternion.identity, new Vector3(1.25f, 0.4f, 1.25f));
            Prim("Base Ring", PrimitiveType.Cylinder, t, m.Gold, new Vector3(c.x, GroundY + 0.46f, c.z), Quaternion.identity, new Vector3(1.12f, 0.06f, 1.12f));
            float s0 = GroundY + 0.52f, s1 = top - 0.42f;
            Prim("Shaft", PrimitiveType.Cylinder, t, x.Fluted, new Vector3(c.x, (s0 + s1) * 0.5f, c.z), Quaternion.identity, new Vector3(1.0f, (s1 - s0) * 0.5f, 1.0f));
            Prim("Necking", PrimitiveType.Cylinder, t, m.Gold, new Vector3(c.x, s1 + 0.04f, c.z), Quaternion.identity, new Vector3(1.08f, 0.04f, 1.08f));
            Prim("Capital", PrimitiveType.Cylinder, t, m.Gold, new Vector3(c.x, s1 + 0.2f, c.z), Quaternion.identity, new Vector3(1.24f, 0.12f, 1.24f));
            Prim("Abacus", PrimitiveType.Cube, t, x.StoneTrim, new Vector3(c.x, top - 0.06f, c.z), Quaternion.identity, new Vector3(1.4f, 0.12f, 1.4f));
        }

        // "GRAND CASINO" marquee on top of the canopy
        var s = Group("Marquee Sign", g);
        const float sx = -2.52f, sz = 31.3f, sw = 4.6f, sh = 1.1f, sy = CanCenterTop + 0.66f + sh * 0.5f;
        Box("Board", s, m.Black, new Vector3(sx, sy, sz), Quaternion.identity, new Vector3(sw, sh, 0.12f));
        Box("Frame Top", s, m.Gold, new Vector3(sx, sy + sh * 0.5f, sz), Quaternion.identity, new Vector3(sw + 0.12f, 0.08f, 0.16f));
        Box("Frame Bottom", s, m.Gold, new Vector3(sx, sy - sh * 0.5f, sz), Quaternion.identity, new Vector3(sw + 0.12f, 0.08f, 0.16f));
        Box("Frame L", s, m.Gold, new Vector3(sx - sw * 0.5f, sy, sz), Quaternion.identity, new Vector3(0.08f, sh, 0.16f));
        Box("Frame R", s, m.Gold, new Vector3(sx + sw * 0.5f, sy, sz), Quaternion.identity, new Vector3(0.08f, sh, 0.16f));
        Box("Post L", s, m.Gold, new Vector3(sx - 1.9f, CanCenterTop + 0.33f, sz), Quaternion.identity, new Vector3(0.12f, 0.66f, 0.12f));
        Box("Post R", s, m.Gold, new Vector3(sx + 1.9f, CanCenterTop + 0.33f, sz), Quaternion.identity, new Vector3(0.12f, 0.66f, 0.12f));
        for (float bx = -sw * 0.5f + 0.15f; bx <= sw * 0.5f - 0.14f; bx += 0.25f)
            foreach (var by in new[] { sh * 0.5f - 0.1f, -sh * 0.5f + 0.1f })
                Box("Bulb", s, x.Bulb, new Vector3(sx + bx, sy + by, sz + 0.07f), Quaternion.Euler(0, 0, 45f), Vector3.one * 0.06f);
        var textGo = new GameObject("Sign Text");
        textGo.transform.SetParent(s, false);
        var tmp = textGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.5f;
        tmp.fontSizeMax = 12f;
        tmp.characterSpacing = 10f;
        tmp.color = new Color(1f, 0.84f, 0.45f);
        tmp.rectTransform.sizeDelta = new Vector2(sw - 0.5f, sh - 0.42f);
        ApplySignText(tmp); // "THE GRAND MIRAGE" with CASINO underneath (DesertSurroundings.cs)
        textGo.transform.SetPositionAndRotation(new Vector3(sx, sy, sz + 0.065f), Quaternion.Euler(0f, 180f, 0f));
    }

    static bool FreeQuiet(Bounds b)
    {
        b.Expand(-0.005f);
        foreach (var o in obstacles) if (o.Intersects(b)) return false;
        return true;
    }

    // ---------------------------------------------------------------- ground

    static void BuildGround()
    {
        var g = Group("Grounds");
        // Lawn around the building footprint + walkway ring (never under the building).
        const float fx0 = -12.1f, fx1 = 6.3f, fz0 = -2.5f, fz1 = XNorth;
        Flat(g, "Lawn S", x.Lawn, -49.5f, 50.5f, -49.5f, fz0, LawnY, 4f);
        Flat(g, "Lawn N", x.Lawn, -49.5f, 50.5f, fz1, 50.5f, LawnY, 4f);
        Flat(g, "Lawn W", x.Lawn, -49.5f, fx0, fz0, fz1, LawnY, 4f);
        Flat(g, "Lawn E", x.Lawn, fx1, 50.5f, fz0, fz1, LawnY, 4f);
        // Walkways and forecourt
        Flat(g, "Walkway W", x.Paving, fx0, XWest, fz0, fz1, PaveY, 2f);
        Flat(g, "Walkway E", x.Paving, XEast, fx1, fz0, fz1, PaveY, 2f);
        Flat(g, "Walkway S", x.Paving, fx0, fx1, fz0, XSouth, PaveY, 2f);
        Flat(g, "Forecourt", x.Paving, -15f, 9.6f, fz1, 48.5f, PaveY, 2f);
        Flat(g, "Lobby Marble", x.Checker, -6.58f, 1.21f, CanBack, 32.93f, LobbyY, 1.2f);

        // Red carpet from the doors to the fountain, lined with gold stanchions.
        float c0 = 28.25f, c1 = 42.2f;
        var tex = Textures.Rug("Front Carpet", RugStyle.Runner, 1.5f, c1 - c0);
        var mat = MakeMat("Rug Front Carpet", Color.white, 0.05f, 0f, tex, new Color(0.1f, 0.1f, 0.1f), tex);
        Quad("Red Carpet", g, mat, new Vector3(-2.7f, CarpetY, (c0 + c1) * 0.5f), Quaternion.LookRotation(Vector3.down, Vector3.forward), 1.5f, c1 - c0, 1f, 1f);
        var left = new List<Vector3>(); var right = new List<Vector3>();
        for (float z = 33.4f; z <= 41.5f; z += 2.0f) { left.Add(new Vector3(-3.8f, CarpetY, z)); right.Add(new Vector3(-1.6f, CarpetY, z)); }
        RopeLine(g, "Carpet Rope L", left.ToArray());
        RopeLine(g, "Carpet Rope R", right.ToArray());
    }

    static void Flat(Transform parent, string name, Material mat, float x0, float x1, float z0, float z1, float y, float tile)
    {
        Quad(name, parent, mat, new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), Quaternion.LookRotation(Vector3.down, Vector3.forward),
            x1 - x0, z1 - z0, (x1 - x0) / tile, (z1 - z0) / tile);
    }

    // -------------------------------------------------------------- fountain

    static void BuildFountain()
    {
        var t = Group("Fountain");
        var c = new Vector3(-2.7f, GroundY, 44.8f);
        Prim("Surround", PrimitiveType.Cylinder, t, x.Granite, c + Vector3.up * (PaveY - GroundY + 0.012f), Quaternion.identity, new Vector3(6.4f, 0.012f, 6.4f));
        const int segs = 20; const float R = 2.35f;
        for (int i = 0; i < segs; i++)
        {
            float ang = i * 360f / segs;
            var dir = Quaternion.Euler(0, ang, 0) * Vector3.forward;
            var rot = Quaternion.LookRotation(dir);
            float len = 2f * Mathf.PI * R / segs + 0.05f;
            Box("Basin Wall", t, x.StoneTrim, c + dir * R + Vector3.up * 0.3f, rot, new Vector3(len, 0.6f, 0.28f));
            Box("Basin Coping", t, x.StoneTrim, c + dir * R + Vector3.up * 0.63f, rot, new Vector3(len + 0.04f, 0.07f, 0.4f));
            Box("Basin Gold", t, m.Gold, c + dir * (R + 0.15f) + Vector3.up * 0.55f, rot, new Vector3(len, 0.03f, 0.02f));
        }
        Prim("Pool", PrimitiveType.Cylinder, t, x.Water, c + Vector3.up * 0.5f, Quaternion.identity, new Vector3(2 * R - 0.2f, 0.02f, 2 * R - 0.2f));
        Prim("Pedestal", PrimitiveType.Cylinder, t, x.StoneTrim, c + Vector3.up * 0.95f, Quaternion.identity, new Vector3(0.45f, 0.45f, 0.45f));
        Prim("Bowl 1", PrimitiveType.Sphere, t, x.StoneTrim, c + Vector3.up * 1.42f, Quaternion.identity, new Vector3(2.3f, 0.4f, 2.3f));
        Prim("Bowl 1 Water", PrimitiveType.Cylinder, t, x.Water, c + Vector3.up * 1.54f, Quaternion.identity, new Vector3(1.82f, 0.01f, 1.82f));
        Prim("Bowl 1 Rim", PrimitiveType.Cylinder, t, m.Gold, c + Vector3.up * 1.54f, Quaternion.identity, new Vector3(1.88f, 0.008f, 1.88f));
        Prim("Stem", PrimitiveType.Cylinder, t, x.StoneTrim, c + Vector3.up * 2.0f, Quaternion.identity, new Vector3(0.25f, 0.4f, 0.25f));
        Prim("Bowl 2", PrimitiveType.Sphere, t, x.StoneTrim, c + Vector3.up * 2.42f, Quaternion.identity, new Vector3(1.2f, 0.26f, 1.2f));
        Prim("Bowl 2 Water", PrimitiveType.Cylinder, t, x.Water, c + Vector3.up * 2.5f, Quaternion.identity, new Vector3(0.94f, 0.01f, 0.94f));
        Prim("Bowl 2 Rim", PrimitiveType.Cylinder, t, m.Gold, c + Vector3.up * 2.5f, Quaternion.identity, new Vector3(0.98f, 0.008f, 0.98f));
        Prim("Finial Stem", PrimitiveType.Cylinder, t, m.Gold, c + Vector3.up * 2.75f, Quaternion.identity, new Vector3(0.1f, 0.2f, 0.1f));
        Prim("Finial", PrimitiveType.Sphere, t, m.Gold, c + Vector3.up * 3.05f, Quaternion.identity, new Vector3(0.3f, 0.42f, 0.3f));
        // Water curtains spilling off each bowl
        Prim("Spill 1", PrimitiveType.Cylinder, t, x.WaterFall, c + Vector3.up * 0.96f, Quaternion.identity, new Vector3(2.28f, 0.46f, 2.28f));
        Prim("Spill 2", PrimitiveType.Cylinder, t, x.WaterFall, c + Vector3.up * 1.98f, Quaternion.identity, new Vector3(1.18f, 0.44f, 1.18f));
        Prim("Jet", PrimitiveType.Cylinder, t, x.WaterFall, c + Vector3.up * 3.6f, Quaternion.identity, new Vector3(0.08f, 0.4f, 0.08f));
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
            if (r.sharedMaterial != x.Water && r.sharedMaterial != x.WaterFall) r.shadowCastingMode = ShadowCastingMode.On;
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.center = c + Vector3.up * 1.0f;
        col.radius = R + 0.15f;
        col.height = 2f * (R + 0.15f);
    }

    // -------------------------------------------------------------- planting

    static void BuildPlanting()
    {
        var g = Group("Planting");
        // Palms in planters on the forecourt, and along the lawns.
        foreach (var p in new[] { new Vector2(-10.5f, 34.5f), new Vector2(5.0f, 34.5f), new Vector2(-10.5f, 41.5f), new Vector2(5.0f, 41.5f) })
        {
            Prim("Palm Planter", PrimitiveType.Cylinder, g, x.StoneTrim, new Vector3(p.x, PaveY + 0.25f, p.y), Quaternion.identity, new Vector3(1.4f, 0.25f, 1.4f));
            Prim("Planter Rim", PrimitiveType.Cylinder, g, m.Gold, new Vector3(p.x, PaveY + 0.5f, p.y), Quaternion.identity, new Vector3(1.44f, 0.015f, 1.44f));
            Prim("Planter Soil", PrimitiveType.Cylinder, g, x.Bark, new Vector3(p.x, PaveY + 0.49f, p.y), Quaternion.identity, new Vector3(1.3f, 0.015f, 1.3f));
            Palm(g, new Vector3(p.x, PaveY + 0.45f, p.y), (int)(p.x * 7 + p.y * 3), 5.6f);
        }
        int seed = 1;
        foreach (var z in new[] { 2f, 10f, 18f, 26f })
        {
            Palm(g, new Vector3(-16f, LawnY, z), seed++, 6.5f);
            Palm(g, new Vector3(10.5f, LawnY, z + 3f), seed++, 6.2f);
        }
        foreach (var px in new[] { -13f, -3f, 7f }) Palm(g, new Vector3(px, LawnY, -6.5f), seed++, 6.8f);

        // Topiaries: urns at the canopy corners, cones along the carpet.
        Topiary(g, new Vector3(-7.0f, LobbyY, 32.4f), true);
        Topiary(g, new Vector3(1.6f, LobbyY, 32.4f), true);
        foreach (var z in new[] { 36.4f, 41.4f })
        {
            Topiary(g, new Vector3(-5.4f, PaveY, z), false);
            Topiary(g, new Vector3(0.0f, PaveY, z), false);
        }

        // Hedges in planters along the facades (below the window sills).
        Hedge(g, new Vector3(XWest - 0.33f, 0, (XSouth + XNorth) * 0.5f), new Vector3(0.66f, 0, XNorth - XSouth - 1.0f));
        Hedge(g, new Vector3(XEast + 0.33f, 0, (XSouth + XNorth) * 0.5f), new Vector3(0.66f, 0, XNorth - XSouth - 1.0f));
        Hedge(g, new Vector3((XWest + XEast) * 0.5f, 0, XSouth - 0.33f), new Vector3(XEast - XWest - 1.0f, 0, 0.66f));
        Hedge(g, new Vector3(-8.75f, 0, XNorth + 0.33f), new Vector3(2.5f, 0, 0.66f));
    }

    static void Hedge(Transform parent, Vector3 c, Vector3 size)
    {
        var t = Group("Hedge", parent);
        Box("Planter", t, x.Granite, new Vector3(c.x, PaveY + 0.15f, c.z), Quaternion.identity, new Vector3(size.x, 0.3f, size.z));
        Box("Planter Rim", t, m.Gold, new Vector3(c.x, PaveY + 0.305f, c.z), Quaternion.identity, new Vector3(size.x + 0.02f, 0.012f, size.z + 0.02f));
        Box("Boxwood", t, x.Foliage, new Vector3(c.x, PaveY + 0.525f, c.z), Quaternion.identity, new Vector3(size.x - 0.08f, 0.45f, size.z - 0.08f));
    }

    static void Topiary(Transform parent, Vector3 p, bool urn)
    {
        var t = Group(urn ? "Urn Topiary" : "Cone Topiary", parent);
        if (urn)
        {
            Prim("Urn Foot", PrimitiveType.Cylinder, t, m.Gold, p + Vector3.up * 0.08f, Quaternion.identity, new Vector3(0.4f, 0.08f, 0.4f));
            Prim("Urn", PrimitiveType.Sphere, t, x.StoneTrim, p + Vector3.up * 0.48f, Quaternion.identity, new Vector3(0.8f, 0.7f, 0.8f));
            Prim("Urn Lip", PrimitiveType.Cylinder, t, m.Gold, p + Vector3.up * 0.78f, Quaternion.identity, new Vector3(0.66f, 0.03f, 0.66f));
            Prim("Ball", PrimitiveType.Sphere, t, x.Foliage, p + Vector3.up * 1.25f, Quaternion.identity, Vector3.one * 0.9f);
            Prim("Ball Top", PrimitiveType.Sphere, t, x.Foliage, p + Vector3.up * 1.95f, Quaternion.identity, Vector3.one * 0.55f);
        }
        else
        {
            Box("Planter", t, x.StoneTrim, p + Vector3.up * 0.3f, Quaternion.identity, new Vector3(0.7f, 0.6f, 0.7f));
            Box("Planter Rim", t, m.Gold, p + Vector3.up * 0.61f, Quaternion.identity, new Vector3(0.74f, 0.03f, 0.74f));
            for (int i = 0; i < 4; i++)
            {
                float s = 0.62f - i * 0.13f;
                Prim("Tier", PrimitiveType.Sphere, t, x.Foliage, p + Vector3.up * (0.85f + i * 0.38f), Quaternion.identity, new Vector3(s, 0.48f, s));
            }
        }
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;
    }

    static void Palm(Transform parent, Vector3 basePos, int seed, float height)
    {
        var t = Group("Palm", parent);
        var rnd = new System.Random(seed * 7919 + 17);
        float R() => (float)rnd.NextDouble();
        var lean = Quaternion.Euler(0f, R() * 360f, 0f) * Vector3.forward;
        const int segs = 7;
        Vector3 prev = basePos;
        for (int i = 1; i <= segs; i++)
        {
            float u = i / (float)segs;
            var p = basePos + Vector3.up * height * u + lean * (0.6f * u * u);
            float thick = Mathf.Lerp(0.42f, 0.26f, u);
            Strut("Trunk", t, x.Bark, prev, p, thick);
            Prim("Trunk Ring", PrimitiveType.Cylinder, t, x.BarkDark, p, Quaternion.FromToRotation(Vector3.up, (p - prev).normalized), new Vector3(thick + 0.05f, 0.03f, thick + 0.05f));
            prev = p;
        }
        var top = prev;
        for (int i = 0; i < 11; i++)
        {
            float yaw = i * 360f / 11f + R() * 15f;
            float pitch = 18f + R() * 30f;
            var dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            float len = 2.4f + R() * 0.6f;
            var tip = top + dir * len * 0.5f + Vector3.down * 0.15f;
            Prim("Frond", PrimitiveType.Sphere, t, x.Frond, tip, Quaternion.LookRotation(dir) * Quaternion.Euler(0, 0, R() * 30f - 15f), new Vector3(0.55f, 0.04f, len));
        }
        for (int i = 0; i < 3; i++)
        {
            var d = Quaternion.Euler(0, i * 120f, 0) * Vector3.forward;
            Prim("Coconut", PrimitiveType.Sphere, t, x.BarkDark, top + d * 0.18f + Vector3.down * 0.2f, Quaternion.identity, Vector3.one * 0.2f);
        }
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;
    }

    // ------------------------------------------------------------ lamp posts

    static void BuildLampPosts()
    {
        var g = Group("Lamp Posts");
        var spots = new List<Vector3>
        {
            new Vector3(-5.4f, PaveY, 34.0f), new Vector3(0.0f, PaveY, 34.0f),
            new Vector3(-5.4f, PaveY, 39.0f), new Vector3(0.0f, PaveY, 39.0f),
            new Vector3(-6.8f, PaveY, 44.8f), new Vector3(1.4f, PaveY, 44.8f),
            new Vector3(-13.6f, PaveY, 46.8f), new Vector3(8.2f, PaveY, 46.8f),
        };
        foreach (var z in new[] { 4f, 14f, 24f }) { spots.Add(new Vector3(-11.6f, PaveY, z)); spots.Add(new Vector3(5.8f, PaveY, z)); }
        foreach (var p in spots) LampPost(g, p);
    }

    static void LampPost(Transform parent, Vector3 p)
    {
        var t = Group("Lamp Post", parent);
        const float h = 3.4f;
        Prim("Base", PrimitiveType.Cylinder, t, x.Iron, p + Vector3.up * 0.2f, Quaternion.identity, new Vector3(0.34f, 0.2f, 0.34f));
        Prim("Base Band", PrimitiveType.Cylinder, t, m.Gold, p + Vector3.up * 0.42f, Quaternion.identity, new Vector3(0.24f, 0.025f, 0.24f));
        Prim("Pole", PrimitiveType.Cylinder, t, x.Iron, p + Vector3.up * h * 0.5f, Quaternion.identity, new Vector3(0.11f, h * 0.5f, 0.11f));
        Prim("Collar", PrimitiveType.Cylinder, t, m.Gold, p + Vector3.up * (h - 0.4f), Quaternion.identity, new Vector3(0.16f, 0.03f, 0.16f));
        Box("Crossarm", t, x.Iron, p + Vector3.up * (h - 0.1f), Quaternion.identity, new Vector3(1.0f, 0.05f, 0.05f));
        foreach (var sx in new[] { -0.5f, 0.5f })
        {
            var c = p + new Vector3(sx, h + 0.1f, 0f);
            Prim("Lantern Cup", PrimitiveType.Cylinder, t, m.Gold, c - Vector3.up * 0.17f, Quaternion.identity, new Vector3(0.16f, 0.03f, 0.16f));
            Prim("Globe", PrimitiveType.Sphere, t, m.Glow, c, Quaternion.identity, Vector3.one * 0.32f);
            Prim("Lantern Cap", PrimitiveType.Sphere, t, x.Iron, c + Vector3.up * 0.17f, Quaternion.identity, new Vector3(0.18f, 0.06f, 0.18f));
        }
        Prim("Finial", PrimitiveType.Sphere, t, m.Gold, p + Vector3.up * (h + 0.05f), Quaternion.identity, Vector3.one * 0.12f);
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;
    }

    // -------------------------------------------------------------- boundary

    static void BuildBoundary()
    {
        var g = Group("Boundary");
        const float z = 48.5f, xw = -15f, xe = 9.6f, gate0 = -4.6f, gate1 = -0.8f;
        WallRun(g, new Vector3(xw, 0, z), new Vector3(gate0, 0, z));
        WallRun(g, new Vector3(gate1, 0, z), new Vector3(xe, 0, z));
        WallRun(g, new Vector3(xw, 0, 30.5f), new Vector3(xw, 0, z));
        WallRun(g, new Vector3(xe, 0, 30.5f), new Vector3(xe, 0, z));
        foreach (var gx in new[] { gate0 - 0.45f, gate1 + 0.45f }) GatePillar(g, new Vector3(gx, PaveY, z));
    }

    static void WallRun(Transform parent, Vector3 a, Vector3 b)
    {
        var t = Group("Boundary Wall", parent);
        var d = b - a; float len = d.magnitude; var dir = d / len;
        var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up));
        var mid = (a + b) * 0.5f;
        Box("Wall", t, x.StoneTrim, new Vector3(mid.x, PaveY + 0.3f, mid.z), rot, new Vector3(len, 0.6f, 0.36f));
        Box("Coping", t, x.StoneTrim, new Vector3(mid.x, PaveY + 0.63f, mid.z), rot, new Vector3(len + 0.06f, 0.06f, 0.46f));
        Quad("Railing A", t, x.Fence, new Vector3(mid.x, PaveY + 0.66f + 0.65f, mid.z), rot, len, 1.3f, len / 1.0f, 1f);
        int posts = Mathf.Max(1, Mathf.RoundToInt(len / 5f));
        for (int i = 0; i <= posts; i++)
        {
            var p = Vector3.Lerp(a, b, i / (float)posts);
            Box("Pier", t, x.StoneTrim, new Vector3(p.x, PaveY + 0.95f, p.z), Quaternion.identity, new Vector3(0.5f, 1.9f, 0.5f));
            Box("Pier Cap", t, x.StoneTrim, new Vector3(p.x, PaveY + 1.94f, p.z), Quaternion.identity, new Vector3(0.6f, 0.08f, 0.6f));
            Prim("Pier Ball", PrimitiveType.Sphere, t, m.Gold, new Vector3(p.x, PaveY + 2.1f, p.z), Quaternion.identity, Vector3.one * 0.26f);
        }
    }

    static void GatePillar(Transform parent, Vector3 p)
    {
        var t = Group("Gate Pillar", parent);
        Box("Pillar", t, x.StoneTrim, p + Vector3.up * 1.3f, Quaternion.identity, new Vector3(0.8f, 2.6f, 0.8f));
        Box("Pillar Base", t, x.Granite, p + Vector3.up * 0.25f, Quaternion.identity, new Vector3(0.92f, 0.5f, 0.92f));
        Box("Pillar Band", t, m.Gold, p + Vector3.up * 2.35f, Quaternion.identity, new Vector3(0.84f, 0.06f, 0.84f));
        Box("Pillar Cap", t, x.StoneTrim, p + Vector3.up * 2.66f, Quaternion.identity, new Vector3(0.95f, 0.12f, 0.95f));
        Prim("Lantern Base", PrimitiveType.Cylinder, t, m.Gold, p + Vector3.up * 2.78f, Quaternion.identity, new Vector3(0.3f, 0.05f, 0.3f));
        Box("Lantern", t, m.Glow, p + Vector3.up * 3.05f, Quaternion.Euler(0, 45f, 0), new Vector3(0.3f, 0.45f, 0.3f));
        Prim("Lantern Roof", PrimitiveType.Sphere, t, m.Gold, p + Vector3.up * 3.32f, Quaternion.identity, new Vector3(0.38f, 0.14f, 0.38f));
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;
    }

    // ---------------------------------------------------------- evening mode

    [MenuItem("Tools/Casino/Evening Mode (toggle)")]
    static void ToggleEvening()
    {
        var sun = RenderSettings.sun;
        if (sun == null)
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) { sun = l; break; }

        if (EditorPrefs.HasKey(EveningPrefKey))
        {
            var p = EditorPrefs.GetString(EveningPrefKey).Split('|');
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(p[0]);
            if (sun != null && p.Length >= 4)
            {
                Undo.RecordObjects(new Object[] { sun, sun.transform }, "Evening Mode Off");
                sun.color = StringToColor(p[1]);
                sun.intensity = float.Parse(p[2], CultureInfo.InvariantCulture);
                var e = p[3].Split(',');
                sun.transform.eulerAngles = new Vector3(float.Parse(e[0], CultureInfo.InvariantCulture), float.Parse(e[1], CultureInfo.InvariantCulture), float.Parse(e[2], CultureInfo.InvariantCulture));
            }
            EditorPrefs.DeleteKey(EveningPrefKey);
            Debug.Log("DecorateExterior: evening mode OFF (original sky and sun restored).");
        }
        else
        {
            EnsureFolder(Folder);
            EnsureFolder(TexFolder);
            var eu = sun != null ? sun.transform.eulerAngles : Vector3.zero;
            EditorPrefs.SetString(EveningPrefKey, string.Join("|",
                AssetDatabase.GetAssetPath(RenderSettings.skybox),
                sun != null ? ColorToString(sun.color) : "#FFFFFFFF",
                (sun != null ? sun.intensity : 1f).ToString(CultureInfo.InvariantCulture),
                $"{eu.x.ToString(CultureInfo.InvariantCulture)},{eu.y.ToString(CultureInfo.InvariantCulture)},{eu.z.ToString(CultureInfo.InvariantCulture)}"));

            string path = Folder + "/Ext Evening Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/6 Sided")); AssetDatabase.CreateAsset(sky, path); }
            var side = Textures.SkySide();
            foreach (var n in new[] { "_FrontTex", "_BackTex", "_LeftTex", "_RightTex" }) sky.SetTexture(n, side);
            sky.SetTexture("_UpTex", Textures.SkyUp());
            sky.SetTexture("_DownTex", Textures.SkyDown());
            sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
            sky.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            if (sun != null)
            {
                Undo.RecordObjects(new Object[] { sun, sun.transform }, "Evening Mode On");
                sun.color = new Color(1f, 0.6f, 0.38f);
                sun.intensity = 0.7f;
                sun.transform.eulerAngles = new Vector3(12f, eu.y, 0f);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("DecorateExterior: evening mode ON (dusk sky, low warm sun). Run again to restore.");
        }
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // -------------------------------------------------------------- materials

    class ExtMats
    {
        public readonly Material Limestone, StoneTrim, Granite, Bronze, Paving, Lawn, Checker, Foliage, Frond, Bark, BarkDark;
        public readonly Material Water, WaterFall, Fence, Fluted, Iron, Bulb, WindowGlow, Slate, Glass;
        readonly Dictionary<string, Material> windows = new Dictionary<string, Material>();

        public ExtMats()
        {
            Limestone = MakeMat("Ext Limestone", Color.white, 0.2f, 0f, Textures.Limestone());
            StoneTrim = MakeMat("Ext Stone Trim", new Color(0.9f, 0.85f, 0.74f), 0.3f, 0f);
            Granite = MakeMat("Ext Granite", Color.white, 0.6f, 0f, Textures.Granite());
            Bronze = MakeMat("Ext Bronze", new Color(0.16f, 0.11f, 0.07f), 0.55f, 0.6f);
            Paving = MakeMat("Ext Paving", Color.white, 0.25f, 0f, Textures.Paving());
            Lawn = MakeMat("Ext Lawn", Color.white, 0.08f, 0f, Textures.Sand()); // desert sand (see DesertSurroundings.cs)
            Checker = MakeMat("Ext Lobby Marble", Color.white, 0.85f, 0f, Textures.Checker());
            Foliage = MakeMat("Ext Foliage", Color.white, 0.15f, 0f, Textures.Leaves());
            Frond = MakeMat("Ext Palm Frond", new Color(0.55f, 0.85f, 0.45f), 0.2f, 0f, Textures.Leaves());
            Bark = MakeMat("Ext Palm Bark", new Color(0.45f, 0.35f, 0.25f), 0.1f, 0f);
            BarkDark = MakeMat("Ext Palm Bark Dark", new Color(0.28f, 0.2f, 0.13f), 0.1f, 0f);
            Water = MakeMat("Ext Water", new Color(0.1f, 0.35f, 0.42f), 0.97f, 0.1f, null, new Color(0.02f, 0.08f, 0.1f));
            WaterFall = Transparent("Ext Water Fall", new Color(0.75f, 0.9f, 1f, 0.28f));
            Fence = Cutout("Ext Iron Fence", Textures.Fence());
            Fluted = MakeMat("Ext Fluted Marble", Color.white, 0.6f, 0f, Textures.Fluted());
            Iron = MakeMat("Ext Iron", new Color(0.03f, 0.03f, 0.035f), 0.6f, 0.4f);
            Bulb = MakeMat("Ext Marquee Bulb", new Color(1f, 0.92f, 0.7f), 0.8f, 0f, null, new Color(2.0f, 1.6f, 0.9f));
            Slate = MakeMat("Ext Slate Roof", new Color(0.2f, 0.21f, 0.24f), 0.35f, 0.1f, Textures.Granite());
            Glass = MakeMat("Ext Skylight Glass", new Color(0.22f, 0.3f, 0.36f), 0.95f, 0.5f, null, new Color(0.08f, 0.07f, 0.05f));
            WindowGlow = MakeMat("Ext Window Glow", new Color(1f, 0.82f, 0.55f), 0.5f, 0f, null, new Color(1.2f, 0.85f, 0.45f));
        }

        public Material WindowMat(float w, float h, bool arched)
        {
            string key = $"{w:F2}x{h:F2}{(arched ? "a" : "")}";
            if (windows.TryGetValue(key, out var mat)) return mat;
            var (baseTex, emTex) = Textures.Window(w, h, arched);
            mat = MakeMat("Ext Window " + key, Color.white, 0.9f, 0f, baseTex, new Color(1f, 1f, 1f), emTex);
            windows[key] = mat;
            return mat;
        }

        static Material Transparent(string name, Color c)
        {
            var mat = MakeMat(name, c, 0.95f, 0f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        static Material Cutout(string name, Texture2D tex)
        {
            var mat = MakeMat(name, Color.white, 0.5f, 0.3f, tex);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            return mat;
        }
    }

    // -------------------------------------------------------------- textures

    static partial class Textures
    {
        static float Hash(float a, float b) => Frac(Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f);

        public static Texture2D Limestone() => Make("Limestone", 256, 192, true, (u, v) =>
        {
            // 2 blocks x 2 courses, staggered (tile = 2.4 m x 0.9 m)
            float course = Mathf.Floor(v * 2f);
            float bu = Frac(u * 2f + course * 0.5f), bv = Frac(v * 2f);
            float id = Hash(Mathf.Floor(u * 2f + course * 0.5f), course);
            float n = Fbm(u * 14f, v * 10f);
            var col = Mix(new Color(0.80f, 0.74f, 0.62f), new Color(0.92f, 0.87f, 0.76f), n * 0.7f + id * 0.3f);
            float joint = Mathf.Min(Mathf.Min(bu, 1f - bu) * 2.4f, Mathf.Min(bv, 1f - bv) * 0.45f);
            if (joint < 0.008f) col *= 0.72f;
            else if (joint < 0.016f) col *= 1.05f;
            col.a = 1f;
            return col;
        });

        public static Texture2D Granite() => Make("Granite", 128, 128, true, (u, v) =>
        {
            float n = Fbm(u * 20f, v * 20f), s = Hash(Mathf.Floor(u * 128f), Mathf.Floor(v * 128f));
            var col = Mix(new Color(0.09f, 0.085f, 0.085f), new Color(0.2f, 0.18f, 0.17f), n);
            if (s > 0.93f) col = Mix(col, new Color(0.55f, 0.5f, 0.45f), 0.6f);
            col.a = 1f;
            return col;
        });

        public static Texture2D Paving() => Make("Paving", 256, 256, true, (u, v) =>
        {
            float cu = Frac(u * 2f), cv = Frac(v * 2f);
            float id = Hash(Mathf.Floor(u * 2f), Mathf.Floor(v * 2f));
            var col = Mix(new Color(0.72f, 0.68f, 0.6f), new Color(0.84f, 0.8f, 0.72f), id * 0.6f + Fbm(u * 10f, v * 10f) * 0.4f);
            float edge = Mathf.Min(Mathf.Min(cu, 1f - cu), Mathf.Min(cv, 1f - cv));
            if (edge < 0.012f) col *= 0.6f;
            // thin inlay band every other stone
            if (Mathf.Abs(Frac(u) - 0.5f) < 0.006f || Mathf.Abs(Frac(v) - 0.5f) < 0.006f) col = Mix(col, new Color(0.45f, 0.35f, 0.25f), 0.25f);
            col.a = 1f;
            return col;
        });

        public static Texture2D Grass() => Make("Grass", 256, 256, true, (u, v) =>
        {
            float n = Fbm(u * 8f, v * 8f), f = Mathf.PerlinNoise(u * 90f, v * 90f);
            var col = Mix(new Color(0.15f, 0.32f, 0.1f), new Color(0.27f, 0.47f, 0.16f), n * 0.7f + f * 0.3f);
            // subtle mowing stripes
            col *= Frac(u * 2f) < 0.5f ? 1.04f : 0.96f;
            col.a = 1f;
            return col;
        });

        public static Texture2D Checker() => Make("Lobby Checker", 256, 256, true, (u, v) =>
        {
            bool white = (Mathf.Floor(u * 2f) + Mathf.Floor(v * 2f)) % 2f == 0f;
            float n = Fbm(u * 6f + (white ? 0f : 5f), v * 6f);
            float vein = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((u * 3f + v * 5f + n * 3f) * Mathf.PI)), 18f);
            var col = white ? Mix(new Color(0.9f, 0.88f, 0.84f), new Color(0.6f, 0.58f, 0.55f), vein * 0.6f)
                            : Mix(new Color(0.05f, 0.05f, 0.055f), new Color(0.75f, 0.6f, 0.35f), vein * 0.7f);
            float edge = Mathf.Min(Mathf.Min(Frac(u * 2f), 1f - Frac(u * 2f)), Mathf.Min(Frac(v * 2f), 1f - Frac(v * 2f)));
            if (edge < 0.01f) col = new Color(0.78f, 0.6f, 0.28f);
            col.a = 1f;
            return col;
        });

        public static Texture2D Leaves() => Make("Leaves", 128, 128, true, (u, v) =>
        {
            float n = Fbm(u * 12f, v * 12f), c = Mathf.PerlinNoise(u * 40f, v * 40f);
            var col = Mix(new Color(0.06f, 0.17f, 0.05f), new Color(0.2f, 0.42f, 0.14f), n * 0.5f + c * 0.5f);
            col.a = 1f;
            return col;
        });

        public static Texture2D Fluted() => Make("Fluted Marble", 256, 128, true, (u, v) =>
        {
            float f = Frac(u * 16f);
            float shade = 0.72f + 0.28f * Mathf.Sin(f * Mathf.PI);
            float n = Fbm(u * 8f, v * 3f);
            var col = Mix(new Color(0.88f, 0.86f, 0.82f), new Color(0.98f, 0.97f, 0.94f), n) * shade;
            if (f < 0.04f || f > 0.96f) col *= 0.85f;
            col.a = 1f;
            return col;
        });

        public static Texture2D Fence() => Make("Iron Fence", 128, 192, true, true, (u, v) =>
        {
            // tile = 1.0 m wide x 1.3 m tall; 8 pickets
            float y = v * 1.3f;
            float pu = Frac(u * 8f) - 0.5f;
            bool picket = Mathf.Abs(pu) < 0.09f && y < 1.15f;
            float tipH = 1.15f + 0.12f * (1f - Mathf.Abs(pu) / 0.25f);
            bool tip = Mathf.Abs(pu) < 0.25f && y >= 1.12f && y < tipH;
            bool rail = Mathf.Abs(y - 0.1f) < 0.025f || Mathf.Abs(y - 1.05f) < 0.025f;
            float ru = Frac(u * 8f + 0.5f) - 0.5f;
            bool ring = Mathf.Abs(new Vector2(ru * 0.125f, y - 0.95f).magnitude - 0.04f) < 0.008f;
            if (tip) return new Color(0.85f, 0.66f, 0.3f, 1f);
            if (picket || rail || ring) return new Color(0.05f, 0.05f, 0.055f, 1f);
            return new Color(0f, 0f, 0f, 0f);
        });

        public static (Texture2D, Texture2D) Window(float wm, float hm, bool arched)
        {
            int w = 96, h = Mathf.Clamp(Mathf.RoundToInt(96 * hm / wm), 48, 256);
            string key = $"{wm:F2}x{hm:F2}{(arched ? "a" : "")}";
            System.Func<float, float, bool, Color> f = (u, v, emissive) =>
            {
                float x = (u - 0.5f) * wm, y = v * hm;
                float archY = hm - wm * 0.5f;
                bool outside = arched && y > archY && new Vector2(x, y - archY).magnitude > wm * 0.5f;
                if (outside) return emissive ? Color.black : new Color(0.86f, 0.8f, 0.68f);
                // warm interior glow, brighter at the bottom
                var glow = Mix(new Color(1f, 0.8f, 0.5f), new Color(0.85f, 0.52f, 0.28f), v);
                // drapes at the sides
                float side = Mathf.Abs(x) / (wm * 0.5f);
                if (side > 0.68f)
                {
                    float fold = 0.6f + 0.4f * Mathf.Sin(x * 60f);
                    glow = new Color(0.45f, 0.05f, 0.08f) * fold;
                }
                // mullions & transom bars
                bool bar = Mathf.Abs(x) < 0.025f || Mathf.Abs(Frac(y / 0.55f) - 0.5f) > 0.47f;
                if (arched && y > archY)
                {
                    float r = new Vector2(x, y - archY).magnitude, a = Mathf.Atan2(y - archY, x);
                    bar = Mathf.Abs(r - wm * 0.25f) < 0.02f || Mathf.Abs(Frac(a / Mathf.PI * 4f) - 0.5f) > 0.46f || Mathf.Abs(x) < 0.025f;
                    if (r > wm * 0.47f) bar = true;
                }
                if (bar) return emissive ? Color.black : new Color(0.14f, 0.1f, 0.07f);
                var c = glow;
                c.a = 1f;
                return emissive ? c * 0.9f : c;
            };
            var b = Make("Window " + key, w, h, false, (u, v) => f(u, v, false));
            var e = Make("Window Glow " + key, w, h, false, (u, v) => f(u, v, true));
            return (b, e);
        }

        static Color SkyAt(float v)
        {
            // v: 0 = straight down, 0.5 = horizon, 1 = straight up
            if (v < 0.5f) return Mix(new Color(0.06f, 0.05f, 0.07f), new Color(0.35f, 0.2f, 0.2f), Mathf.Pow(v / 0.5f, 6f));
            float t = (v - 0.5f) / 0.5f;
            var c = Mix(new Color(1f, 0.55f, 0.28f), new Color(0.9f, 0.42f, 0.42f), Mathf.InverseLerp(0f, 0.1f, t));
            c = Mix(c, new Color(0.42f, 0.24f, 0.48f), Mathf.InverseLerp(0.08f, 0.35f, t));
            c = Mix(c, new Color(0.07f, 0.07f, 0.2f), Mathf.InverseLerp(0.3f, 1f, t));
            return c;
        }

        static Color Stars(float u, float v, float amount)
        {
            float s = Hash(Mathf.Floor(u * 512f), Mathf.Floor(v * 512f));
            return s > 1f - 0.004f * amount ? new Color(0.9f, 0.9f, 1f) * (0.5f + s * 0.5f) : Color.black;
        }

        public static Texture2D SkySide() => Make("Sky Side", 512, 512, false, (u, v) =>
        {
            var c = SkyAt(v) + Stars(u, v, Mathf.InverseLerp(0.7f, 1f, v));
            c.a = 1f; return c;
        });

        public static Texture2D SkyUp() => Make("Sky Up", 512, 512, false, (u, v) =>
        {
            float r = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f; // 0 centre (zenith) .. ~1.4 corners
            var c = SkyAt(1f - Mathf.Clamp01(r) * 0.5f * 0.85f) + Stars(u, v, 1f);
            c.a = 1f; return c;
        });

        public static Texture2D SkyDown() => Make("Sky Down", 64, 64, false, (u, v) => new Color(0.06f, 0.05f, 0.07f, 1f));
    }
}
