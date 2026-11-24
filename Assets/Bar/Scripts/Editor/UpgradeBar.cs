using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;
using Rng = System.Random;

// Tools > Bar > Upgrade Bar (Drinks Menu + Bottles)
//
// Works on the existing "Casino Bar" in the open scene:
//   - Drinks: builds Margarita, Gin and Tonic, Martini and Beer prefabs
//     (turned glasses with garnishes: salt rim and lime, ice and a straw, an
//     olive on a pick, a foam head) next to the existing Whiskey.
//   - Drinks menu: the Whiskey button becomes a DRINKS button that pops up a
//     small menu over the counter - one card per drink with a turning model
//     of it, its name and price. Point at a card and grab to order.
//   - Back bar: the plain bottles are replaced by real-looking liquor bottles
//     of all shapes and sizes (square bourbon, tall vodka, gin, rum, squat
//     tequila, cognac decanters, round liqueurs, champagne, vermouth,
//     bitters) with labels, caps, corks and foil, merged into a few meshes.
//
// Safe to run again: it rebuilds all of the above in place.
public static class UpgradeBar
{
    const string RootName = "Casino Bar";
    const string Folder = "Assets/Bar";
    const string MatFolder = Folder + "/Materials";
    const string PrefabFolder = Folder + "/Prefabs";
    const string MeshFolder = Folder + "/Meshes";
    const string TexFolder = Folder + "/Textures";
    const float CounterHeight = 1.05f;

    sealed class Drink
    {
        public string Title, ItemName;
        public int Price;
        public GameObject Prefab;
    }

    [MenuItem("Tools/Bar/Upgrade Bar (Drinks Menu + Bottles)")]
    public static void Upgrade()
    {
        var root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarning($"UpgradeBar: no '{RootName}' in the open scene - run Tools > Bar > Build Bar first.");
            return;
        }
        foreach (var f in new[] { MatFolder, PrefabFolder, MeshFolder, TexFolder }) EnsureFolder(f);

        var bartender = root.GetComponentInChildren<Bartender>(true);
        var drinks = BuildDrinks();
        BuildBottles(root.transform);
        var menu = BuildMenu(root.transform, bartender, drinks);
        HookUpButtonAndTexts(root.transform, bartender, menu);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"UpgradeBar: {drinks.Count} drinks on the menu, new back-bar bottles. Save the scene to keep it.");
    }

    // ================================================================ drinks

    static List<Drink> BuildDrinks()
    {
        var glass = Clear("Glass", new Color(0.85f, 0.95f, 1f, 0.22f), 0.95f);
        var list = new List<Drink>();
        var whiskey = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Whiskey.prefab");
        if (whiskey != null) list.Add(new Drink { Title = "Whiskey", ItemName = "whiskey", Price = 50, Prefab = whiskey });

        // ---- Margarita: wide stemmed glass, salt rim, lime wedge
        {
            var liquid = Lit("Margarita", new Color(0.86f, 0.9f, 0.5f), 0.8f);
            var salt = Lit("Salt Rim", new Color(0.96f, 0.96f, 0.94f), 0.15f);
            var lime = Lit("Lime", new Color(0.42f, 0.72f, 0.16f), 0.55f);
            var prefab = BuildDrink("Margarita", 0.155f, new Vector3(0.1f, 0.158f, 0.1f), 0.25f, 2f, 9f, 0.101f, (t, pivot) =>
            {
                MeshPart("Glass", t, Lathe("Drink Margarita Glass", new[]
                {
                    P(0, 0), P(0.04f, 0), P(0.04f, 0.004f), P(0.008f, 0.009f), P(0.005f, 0.02f), P(0.005f, 0.085f), P(0.009f, 0.095f),
                    P(0.02f, 0.1f), P(0.035f, 0.107f), P(0.05f, 0.118f), P(0.058f, 0.135f), P(0.061f, 0.155f),
                    P(0.0585f, 0.155f), P(0.0555f, 0.136f), P(0.0475f, 0.12f), P(0.033f, 0.109f), P(0.018f, 0.103f), P(0, 0.101f),
                }, 28), glass, Vector3.zero);
                MeshPart("Liquid", pivot, Lathe("Drink Margarita Liquid", new[]
                {
                    P(0, 0), P(0.017f, 0.002f), P(0.032f, 0.008f), P(0.046f, 0.018f), P(0.053f, 0.03f), P(0, 0.03f),
                }, 24), liquid, Vector3.zero);
                MeshPart("Salt Rim", t, Lathe("Drink Margarita Salt", Ring(0.06f, 0.155f, 0.0028f), 28), salt, Vector3.zero);
                PrimPart("Lime Wedge", PrimitiveType.Sphere, t, lime, new Vector3(0.056f, 0.152f, 0f), Quaternion.Euler(0f, 0f, 35f), new Vector3(0.032f, 0.017f, 0.009f));
            });
            list.Add(new Drink { Title = "Margarita", ItemName = "margarita", Price = 60, Prefab = prefab });
        }

        // ---- Gin and tonic: highball with ice, a lime wheel and a straw
        {
            var tonic = Clear("Tonic", new Color(0.86f, 0.96f, 0.92f, 0.5f), 0.95f);
            var ice = Clear("Ice", new Color(0.93f, 0.97f, 1f, 0.6f), 0.9f);
            var lime = Lit("Lime", new Color(0.42f, 0.72f, 0.16f), 0.55f);
            var straw = Lit("Straw", new Color(0.08f, 0.08f, 0.09f), 0.6f);
            var prefab = BuildDrink("Gin and Tonic", 0.14f, new Vector3(0.07f, 0.14f, 0.07f), 0.35f, 2.2f, 7f, 0.012f, (t, pivot) =>
            {
                MeshPart("Glass", t, Lathe("Drink Highball Glass", new[]
                {
                    P(0, 0), P(0.031f, 0), P(0.032f, 0.004f), P(0.034f, 0.14f), P(0.0315f, 0.14f), P(0.0295f, 0.012f), P(0, 0.012f),
                }, 24), glass, Vector3.zero);
                MeshPart("Liquid", pivot, Lathe("Drink Highball Liquid", new[]
                {
                    P(0, 0), P(0.0293f, 0), P(0.0315f, 0.11f), P(0, 0.11f),
                }, 20), tonic, Vector3.zero);
                PrimPart("Ice 1", PrimitiveType.Cube, t, ice, new Vector3(0.008f, 0.1f, 0.006f), Quaternion.Euler(12f, 30f, 8f), Vector3.one * 0.022f);
                PrimPart("Ice 2", PrimitiveType.Cube, t, ice, new Vector3(-0.01f, 0.108f, -0.004f), Quaternion.Euler(-10f, 65f, 15f), Vector3.one * 0.021f);
                PrimPart("Ice 3", PrimitiveType.Cube, t, ice, new Vector3(0.002f, 0.083f, -0.01f), Quaternion.Euler(25f, 10f, -12f), Vector3.one * 0.02f);
                PrimPart("Lime Wheel", PrimitiveType.Cylinder, t, lime, new Vector3(0.03f, 0.136f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.042f, 0.002f, 0.042f));
                PrimPart("Straw", PrimitiveType.Cylinder, t, straw, new Vector3(-0.012f, 0.12f, 0.008f), Quaternion.Euler(10f, 0f, 12f), new Vector3(0.006f, 0.065f, 0.006f));
            });
            list.Add(new Drink { Title = "Gin & Tonic", ItemName = "gin and tonic", Price = 45, Prefab = prefab });
        }

        // ---- Martini: V glass, olive on a pick
        {
            var martini = Clear("Martini Liquid", new Color(0.93f, 0.95f, 0.85f, 0.5f), 0.95f);
            var olive = Lit("Olive", new Color(0.36f, 0.46f, 0.12f), 0.7f);
            var pimento = Lit("Pimento", new Color(0.75f, 0.1f, 0.08f), 0.6f);
            var pick = Lit("Cocktail Pick", new Color(0.45f, 0.3f, 0.16f), 0.3f);
            var prefab = BuildDrink("Martini", 0.162f, new Vector3(0.1f, 0.162f, 0.1f), 0.25f, 1.6f, 11f, 0.105f, (t, pivot) =>
            {
                MeshPart("Glass", t, Lathe("Drink Martini Glass", new[]
                {
                    P(0, 0), P(0.038f, 0), P(0.038f, 0.004f), P(0.008f, 0.008f), P(0.0045f, 0.02f), P(0.0045f, 0.098f), P(0.008f, 0.103f),
                    P(0.062f, 0.162f), P(0.0595f, 0.162f), P(0.006f, 0.106f), P(0, 0.105f),
                }, 28), glass, Vector3.zero);
                MeshPart("Liquid", pivot, Lathe("Drink Martini Liquid", new[] { P(0, 0), P(0.049f, 0.045f), P(0, 0.045f) }, 24), martini, Vector3.zero);
                PrimPart("Pick", PrimitiveType.Cylinder, t, pick, new Vector3(0.012f, 0.14f, 0f), Quaternion.Euler(0f, 0f, -25f), new Vector3(0.003f, 0.035f, 0.003f));
                PrimPart("Olive", PrimitiveType.Sphere, t, olive, new Vector3(0.006f, 0.126f, 0f), Quaternion.Euler(0f, 0f, -25f), new Vector3(0.014f, 0.017f, 0.014f));
                PrimPart("Pimento", PrimitiveType.Sphere, t, pimento, new Vector3(0.0105f, 0.1335f, 0f), Quaternion.identity, Vector3.one * 0.0065f);
            });
            list.Add(new Drink { Title = "Martini", ItemName = "martini", Price = 70, Prefab = prefab });
        }

        // ---- Beer: pint glass with a foam head
        {
            var beer = Lit("Beer", new Color(0.88f, 0.56f, 0.12f), 0.85f);
            var foam = Lit("Beer Foam", new Color(0.97f, 0.94f, 0.86f), 0.2f);
            var prefab = BuildDrink("Beer", 0.16f, new Vector3(0.085f, 0.16f, 0.085f), 0.5f, 2.8f, 6f, 0.012f, (t, pivot) =>
            {
                MeshPart("Glass", t, Lathe("Drink Pint Glass", new[]
                {
                    P(0, 0), P(0.03f, 0), P(0.031f, 0.004f), P(0.04f, 0.15f), P(0.042f, 0.155f), P(0.042f, 0.16f),
                    P(0.0395f, 0.16f), P(0.038f, 0.15f), P(0.0295f, 0.012f), P(0, 0.012f),
                }, 24), glass, Vector3.zero);
                MeshPart("Liquid", pivot, Lathe("Drink Pint Liquid", new[] { P(0, 0), P(0.0293f, 0), P(0.037f, 0.118f), P(0, 0.118f) }, 20), beer, Vector3.zero);
                MeshPart("Foam", pivot, Lathe("Drink Pint Foam", new[]
                {
                    P(0, 0.117f), P(0.0372f, 0.117f), P(0.0386f, 0.13f), P(0.032f, 0.138f), P(0.015f, 0.141f), P(0, 0.142f),
                }, 20), foam, Vector3.zero);
            });
            list.Add(new Drink { Title = "Beer", ItemName = "beer", Price = 30, Prefab = prefab });
        }
        return list;
    }

    // Origin at the bottom of the glass so it stands on the bar.
    static GameObject BuildDrink(string name, float mouthY, Vector3 box, float mass, float drinkSeconds, float drunkSeconds, float pivotY,
        Action<Transform, Transform> parts)
    {
        var go = new GameObject(name);
        var t = go.transform;
        var pivot = new GameObject("Liquid Pivot").transform;
        pivot.SetParent(t, false);
        pivot.localPosition = new Vector3(0f, pivotY, 0f);
        parts(t, pivot);

        var mouth = new GameObject("Mouth Point").transform;
        mouth.SetParent(t, false);
        mouth.localPosition = new Vector3(0f, mouthY, 0f);

        var collider = go.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, box.y * 0.5f, 0f);
        collider.size = box;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.angularDamping = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        var grab = new SerializedObject(go.AddComponent<XRGrabInteractable>());
        grab.FindProperty("m_MovementType").enumValueIndex = 2; // Instantaneous - 1:1 with the hand
        grab.FindProperty("m_UseDynamicAttach").boolValue = true;
        grab.ApplyModifiedPropertiesWithoutUndo();

        var so = new SerializedObject(go.AddComponent<BarDrink>());
        so.FindProperty("mouthPoint").objectReferenceValue = mouth;
        so.FindProperty("liquidPivot").objectReferenceValue = pivot;
        so.FindProperty("drinkSeconds").floatValue = drinkSeconds;
        so.FindProperty("drunkSeconds").floatValue = drunkSeconds;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ================================================================== menu

    static DrinkMenu BuildMenu(Transform bar, Bartender bartender, List<Drink> drinks)
    {
        var old = bar.Find("Drink Menu");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var brass = Lit("Bar Brass", new Color(0.85f, 0.65f, 0.25f), 0.85f, 1f);
        var dark = Lit("Bar Dark Wood", new Color(0.16f, 0.08f, 0.04f), 0.5f);
        var plinthMat = Lit("Menu Plinth", new Color(0.12f, 0.1f, 0.09f), 0.8f);

        var root = new GameObject("Drink Menu");
        Undo.RegisterCreatedObjectUndo(root, "Drink Menu");
        root.transform.SetParent(bar, false);
        // Floats over the counter by the DRINKS button, tilted back toward a standing customer (+Z).
        root.transform.localPosition = new Vector3(0.4f, CounterHeight + 0.33f, 0.12f);
        root.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
        var menu = root.AddComponent<DrinkMenu>();

        var cards = new GameObject("Cards").transform;
        cards.SetParent(root.transform, false);

        const float cardW = 0.15f, cardH = 0.21f, gap = 0.016f;
        float totalW = drinks.Count * cardW + (drinks.Count - 1) * gap;

        var header = TextQuad("Header", cards, "<color=#FFD24A>DRINKS</color>   <size=60%>point at one and grab to order</size>",
            new Vector2Int(1024, 96));
        header.localPosition = new Vector3(0f, cardH * 0.5f + 0.03f, 0.002f);
        header.localScale = new Vector3(Mathf.Min(totalW, 0.62f), 0.058f, 1f);
        PrimPart("Header Back", PrimitiveType.Cube, cards, dark, new Vector3(0f, cardH * 0.5f + 0.03f, -0.002f), Quaternion.identity,
            new Vector3(Mathf.Min(totalW, 0.62f) + 0.012f, 0.07f, 0.006f));

        for (int i = 0; i < drinks.Count; i++)
        {
            var d = drinks[i];
            var card = new GameObject("Card " + d.Title);
            card.transform.SetParent(cards, false);
            card.transform.localPosition = new Vector3(-totalW * 0.5f + cardW * 0.5f + i * (cardW + gap), 0f, 0f);

            var box = card.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, 0.03f);
            box.size = new Vector3(cardW, cardH, 0.08f);

            var frame = PrimPart("Frame", PrimitiveType.Cube, card.transform, brass, new Vector3(0f, 0f, -0.006f), Quaternion.identity, new Vector3(cardW, cardH, 0.006f));
            PrimPart("Back", PrimitiveType.Cube, card.transform, dark, new Vector3(0f, 0f, -0.002f), Quaternion.identity, new Vector3(cardW - 0.012f, cardH - 0.012f, 0.004f));
            PrimPart("Plinth", PrimitiveType.Cylinder, card.transform, plinthMat, new Vector3(0f, -0.047f, 0.045f), Quaternion.identity, new Vector3(0.075f, 0.003f, 0.075f));

            var preview = Preview(d.Prefab, card.transform);
            preview.localPosition = new Vector3(0f, -0.044f, 0.045f);
            preview.localRotation = Quaternion.Euler(0f, 20f * i, 0f);
            preview.localScale = Vector3.one * 0.72f;

            var label = TextQuad("Label", card.transform, $"{d.Title.ToUpper()}\n<color=#FFD24A>${d.Price}</color>", new Vector2Int(512, 176));
            label.localPosition = new Vector3(0f, -0.075f, 0.0005f);
            label.localScale = new Vector3(cardW - 0.02f, 0.046f, 1f);

            card.AddComponent<XRSimpleInteractable>();
            var so = new SerializedObject(card.AddComponent<DrinkMenuItem>());
            so.FindProperty("menu").objectReferenceValue = menu;
            so.FindProperty("bartender").objectReferenceValue = bartender;
            so.FindProperty("drinkPrefab").objectReferenceValue = d.Prefab;
            so.FindProperty("itemName").stringValue = d.ItemName;
            so.FindProperty("price").intValue = d.Price;
            so.FindProperty("preview").objectReferenceValue = preview;
            so.FindProperty("frame").objectReferenceValue = frame.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var mso = new SerializedObject(menu);
        mso.FindProperty("panel").objectReferenceValue = cards;
        mso.ApplyModifiedPropertiesWithoutUndo();
        return menu;
    }

    // A look-only copy of a drink for the menu card.
    static Transform Preview(GameObject prefab, Transform parent)
    {
        var go = (GameObject)Object.Instantiate(prefab, parent, false);
        go.name = prefab.name + " Preview";
        foreach (var c in go.GetComponentsInChildren<BarConsumable>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<XRGrabInteractable>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    static void HookUpButtonAndTexts(Transform bar, Bartender bartender, DrinkMenu menu)
    {
        // The Whiskey button opens the menu now.
        BarOrderButton drinksButton = null;
        foreach (var b in bar.GetComponentsInChildren<BarOrderButton>(true))
        {
            var so = new SerializedObject(b);
            if (so.FindProperty("itemName").stringValue == "whiskey" || b.transform.parent.name.Contains("Drinks")) { drinksButton = b; break; }
        }
        if (drinksButton != null)
        {
            var so = new SerializedObject(drinksButton);
            so.FindProperty("menu").objectReferenceValue = menu;
            so.ApplyModifiedPropertiesWithoutUndo();
            var station = drinksButton.transform.parent;
            Undo.RecordObject(station.gameObject, "Rename");
            station.name = "Order Drinks";
            var label = station.Find("Label");
            if (label != null) SetPanelText(label, "DRINKS\n<color=#FFD24A>MENU</color>");
        }
        else Debug.LogWarning("UpgradeBar: couldn't find the Whiskey order button to turn into the DRINKS button.");

        var board = bar.Find("Back Bar/Menu Board");
        if (board != null)
            SetPanelText(board, "<color=#FFD24A>THE LUCKY SIP</color>\n" +
                "<size=50%>Beer <color=#FFD24A>$30</color>   Gin & Tonic <color=#FFD24A>$45</color>   Whiskey <color=#FFD24A>$50</color>\n" +
                "Margarita <color=#FFD24A>$60</color>   Martini <color=#FFD24A>$70</color>   Cigar <color=#FFD24A>$100</color></size>\n" +
                "<size=38%>Press DRINKS on the bar for the menu</size>");

        if (bartender != null)
        {
            var so = new SerializedObject(bartender);
            var talk = so.FindProperty("smallTalk");
            for (int i = 0; i < talk.arraySize; i++)
            {
                var line = talk.GetArrayElementAtIndex(i);
                if (line.stringValue.Contains("whiskey is $50")) line.stringValue = "Press DRINKS on the bar and pick your poison.";
                if (line.stringValue.StartsWith("Just press a button")) line.stringValue = "Cigars are $100. Cocktails are on the menu.";
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void SetPanelText(Transform t, string text)
    {
        var panel = t.GetComponent<WorldTextPanel>();
        if (panel == null) return;
        var so = new SerializedObject(panel);
        so.FindProperty("text").stringValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // A quad showing text (WorldTextPanel); its visible face points along local +Z of the parent.
    static Transform TextQuad(string name, Transform parent, string text, Vector2Int size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up); // a Quad shows its -Z side
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = TextScreenMat();
        r.shadowCastingMode = ShadowCastingMode.Off;
        var so = new SerializedObject(go.AddComponent<WorldTextPanel>());
        so.FindProperty("text").stringValue = text;
        so.FindProperty("textColor").colorValue = Color.white;
        so.FindProperty("background").colorValue = new Color(0.05f, 0.03f, 0.02f);
        so.FindProperty("textureSize").vector2IntValue = size;
        so.ApplyModifiedPropertiesWithoutUndo();
        return go.transform;
    }

    static Material TextScreenMat()
    {
        string path = MatFolder + "/Bar Text Screen.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Bar Text Screen" };
        mat.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // =============================================================== bottles

    sealed class BottleMats
    {
        public Material Amber, DarkAmber, ClearGlass, Green, DarkGreen, Tequila, VermouthRed, Straw;
        public Material[] Liqueurs;
        public Material Black, Gold, Silver, RedCap, Cork;
        public Material LabelCream, LabelBlack, LabelWhite, LabelParchment, LabelRed;
    }

    static void BuildBottles(Transform bar)
    {
        var back = bar.Find("Back Bar");
        if (back == null) { Debug.LogWarning("UpgradeBar: no 'Back Bar' under the bar - bottles skipped."); return; }
        var old = back.Find("Bottles");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var m = new BottleMats
        {
            Amber = Lit("Bottle Amber", new Color(0.55f, 0.26f, 0.05f), 0.92f),
            DarkAmber = Lit("Bottle Dark Amber", new Color(0.26f, 0.1f, 0.03f), 0.92f),
            ClearGlass = Lit("Bottle Clear", new Color(0.78f, 0.85f, 0.88f), 0.95f),
            Green = Lit("Bottle Gin Green", new Color(0.18f, 0.42f, 0.3f), 0.93f),
            DarkGreen = Lit("Bottle Dark Green", new Color(0.05f, 0.16f, 0.08f), 0.93f),
            Tequila = Lit("Bottle Tequila", new Color(0.86f, 0.68f, 0.32f), 0.92f),
            VermouthRed = Lit("Bottle Vermouth", new Color(0.42f, 0.05f, 0.07f), 0.92f),
            Straw = Lit("Bottle Straw", new Color(0.85f, 0.74f, 0.4f), 0.92f),
            Liqueurs = new[]
            {
                Lit("Bottle Cherry", new Color(0.55f, 0.03f, 0.08f), 0.92f),
                Lit("Bottle Orange", new Color(0.92f, 0.46f, 0.08f), 0.92f),
                Lit("Bottle Curacao", new Color(0.06f, 0.32f, 0.7f), 0.92f),
                Lit("Bottle Melon", new Color(0.35f, 0.72f, 0.18f), 0.92f),
            },
            Black = Lit("Cap Black", new Color(0.03f, 0.03f, 0.035f), 0.6f),
            Gold = Lit("Cap Gold", new Color(0.86f, 0.66f, 0.28f), 0.8f, 1f),
            Silver = Lit("Cap Silver", new Color(0.75f, 0.76f, 0.78f), 0.8f, 1f),
            RedCap = Lit("Cap Red Wax", new Color(0.55f, 0.04f, 0.05f), 0.55f),
            Cork = Lit("Cap Cork", new Color(0.62f, 0.46f, 0.28f), 0.15f),
            LabelCream = Lit("Label Cream", Color.white, 0.25f, 0f, LabelTex("Label Cream", new Color(0.93f, 0.88f, 0.74f), new Color(0.5f, 0.08f, 0.06f), new Color(0.18f, 0.1f, 0.07f), 1)),
            LabelBlack = Lit("Label Black", Color.white, 0.35f, 0f, LabelTex("Label Black", new Color(0.05f, 0.05f, 0.05f), new Color(0.85f, 0.66f, 0.28f), new Color(0.9f, 0.85f, 0.75f), 2)),
            LabelWhite = Lit("Label White", Color.white, 0.3f, 0f, LabelTex("Label White", new Color(0.96f, 0.96f, 0.95f), new Color(0.1f, 0.25f, 0.6f), new Color(0.1f, 0.12f, 0.2f), 3)),
            LabelParchment = Lit("Label Parchment", Color.white, 0.2f, 0f, LabelTex("Label Parchment", new Color(0.82f, 0.7f, 0.5f), new Color(0.35f, 0.2f, 0.1f), new Color(0.25f, 0.14f, 0.07f), 4)),
            LabelRed = Lit("Label Red", Color.white, 0.3f, 0f, LabelTex("Label Red", new Color(0.62f, 0.06f, 0.07f), new Color(0.95f, 0.85f, 0.55f), new Color(0.98f, 0.95f, 0.9f), 5)),
        };

        var parts = new Dictionary<Material, MeshData>();
        MeshData Get(Material mat)
        {
            if (!parts.TryGetValue(mat, out var d)) parts[mat] = d = new MeshData();
            return d;
        }

        var rnd = new Rng(1931);
        float[] shelves = { 0.93f, 1.35f, 1.75f };
        const float x0 = -1.38f, x1 = 1.38f, z = -1.46f;
        for (int s = 0; s < shelves.Length; s++)
        {
            float x = x0;
            int lastType = -1, repeat = 0;
            while (true)
            {
                // Under the menu board (top shelf, middle) only short bottles fit.
                bool lowOnly = s == 2;
                int type;
                if (repeat > 0 && lastType >= 0) { type = lastType; repeat--; }
                else
                {
                    type = rnd.Next(11);
                    if (type == lastType) type = (type + 1) % 11;
                    if (rnd.NextDouble() < 0.25) repeat = 1; // two of the same brand side by side
                }
                float scale = Mathf.Lerp(0.95f, 1.05f, (float)rnd.NextDouble());
                float half = BottleHalfWidth(type) * scale;
                float cx = x + half;
                if (lowOnly && Mathf.Abs(cx) < 0.9f && BottleHeight(type) * scale > 0.2f)
                {
                    int[] shortOnes = { 5, 6, 9 };
                    type = shortOnes[rnd.Next(shortOnes.Length)];
                    repeat = 0;
                    half = BottleHalfWidth(type) * scale;
                    cx = x + half;
                }
                if (cx + half > x1) break;
                lastType = type;
                if (rnd.NextDouble() > 0.05)
                {
                    var pos = new Vector3(cx, shelves[s], z + Mathf.Lerp(-0.02f, 0.02f, (float)rnd.NextDouble()));
                    var rot = Quaternion.Euler(0f, Mathf.Lerp(-18f, 18f, (float)rnd.NextDouble()), 0f);
                    Bottle(type, Matrix4x4.TRS(pos, rot, Vector3.one * scale), m, rnd, Get);
                }
                x = cx + half + Mathf.Lerp(0.012f, 0.03f, (float)rnd.NextDouble());
            }
        }

        var group = new GameObject("Bottles").transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Bottles");
        // The meshes are in bar space; "Back Bar" sits at the bar root's origin, so they line up.
        group.SetParent(back, false);
        foreach (var kv in parts)
        {
            var mesh = SaveMesh(kv.Value, "Back Bar Bottles " + kv.Key.name);
            var go = new GameObject(kv.Key.name);
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = kv.Key;
            r.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
    }

    static float BottleHalfWidth(int type) => type switch
    {
        0 => 0.051f, 1 => 0.039f, 2 => 0.046f, 3 => 0.043f, 4 => 0.05f, 5 => 0.059f,
        6 => 0.051f, 7 => 0.046f, 8 => 0.037f, 9 => 0.026f, _ => 0.042f,
    };

    static float BottleHeight(int type) => type switch
    {
        0 => 0.255f, 1 => 0.3f, 2 => 0.258f, 3 => 0.29f, 4 => 0.232f, 5 => 0.198f,
        6 => 0.175f, 7 => 0.305f, 8 => 0.305f, 9 => 0.135f, _ => 0.265f,
    };

    // Each bottle: glass body (coloured by what's in it), cap/cork/foil, and a
    // label on the side facing the customers (+Z).
    static void Bottle(int type, Matrix4x4 m, BottleMats b, Rng rnd, Func<Material, MeshData> get)
    {
        T Pick<T>(params T[] options) => options[rnd.Next(options.Length)];
        void Round(Material mat, params Vector2[] prof) => AddLathe(get(mat), m, prof, 16, false, 0f, 360f);
        void Label(Material mat, float r, float y0, float y1, float arc = 120f) =>
            AddLathe(get(mat), m, new[] { P(r + 0.0009f, y0), P(r + 0.0009f, y1) }, 10, false, -90f - arc * 0.5f, arc);

        switch (type)
        {
            case 0: // square bourbon
                AddLathe(get(b.Amber), m, new[] { P(0, 0), P(0.068f, 0), P(0.0707f, 0.004f), P(0.0707f, 0.16f), P(0.05f, 0.185f), P(0, 0.19f) }, 4, true, 45f, 360f);
                Round(b.Amber, P(0.02f, 0.18f), P(0.017f, 0.2f), P(0.016f, 0.23f), P(0, 0.23f));
                Round(b.Black, P(0.0185f, 0.222f), P(0.0185f, 0.255f), P(0, 0.256f));
                AddLathe(get(b.LabelBlack), m, new[] { P(0.0717f, 0.04f), P(0.0717f, 0.13f) }, 1, true, 225f, 90f);
                break;
            case 1: // tall vodka
                Round(b.ClearGlass, P(0, 0), P(0.037f, 0), P(0.0385f, 0.004f), P(0.0385f, 0.205f), P(0.034f, 0.225f), P(0.02f, 0.24f), P(0.0155f, 0.25f), P(0.0155f, 0.275f), P(0, 0.275f));
                Round(b.Silver, P(0.0168f, 0.268f), P(0.0168f, 0.3f), P(0, 0.3f));
                Label(b.LabelWhite, 0.0385f, 0.07f, 0.16f);
                break;
            case 2: // gin
                Round(b.Green, P(0, 0), P(0.043f, 0), P(0.045f, 0.005f), P(0.045f, 0.165f), P(0.04f, 0.19f), P(0.022f, 0.207f), P(0.017f, 0.215f), P(0.017f, 0.24f), P(0, 0.24f));
                Round(Pick(b.RedCap, b.Black, b.Silver), P(0.0185f, 0.232f), P(0.0185f, 0.258f), P(0, 0.258f));
                Label(Pick(b.LabelCream, b.LabelWhite), 0.045f, 0.04f, 0.13f, 140f);
                break;
            case 3: // rum with a long neck and cork
                Round(b.DarkAmber, P(0, 0), P(0.04f, 0), P(0.042f, 0.008f), P(0.042f, 0.15f), P(0.034f, 0.18f), P(0.02f, 0.205f), P(0.0145f, 0.22f), P(0.0145f, 0.265f), P(0, 0.265f));
                Round(b.Cork, P(0.0155f, 0.26f), P(0.017f, 0.282f), P(0.012f, 0.289f), P(0, 0.29f));
                Label(b.LabelParchment, 0.042f, 0.03f, 0.12f, 130f);
                break;
            case 4: // squat tequila with a big stopper
                Round(b.Tequila, P(0, 0), P(0.048f, 0), P(0.05f, 0.006f), P(0.049f, 0.04f), P(0.042f, 0.11f), P(0.032f, 0.15f), P(0.019f, 0.172f), P(0.015f, 0.185f), P(0.015f, 0.2f), P(0, 0.2f));
                Round(b.Cork, P(0.016f, 0.195f), P(0.026f, 0.2f), P(0.026f, 0.225f), P(0.02f, 0.232f), P(0, 0.232f));
                AddLathe(get(Pick(b.LabelCream, b.LabelRed)), m, new[] { P(0.0489f, 0.05f), P(0.0439f, 0.1f) }, 10, false, -150f, 120f);
                break;
            case 5: // cognac decanter
                Round(b.DarkAmber, P(0, 0), P(0.034f, 0), P(0.052f, 0.025f), P(0.058f, 0.06f), P(0.054f, 0.1f), P(0.036f, 0.13f), P(0.016f, 0.148f), P(0.0125f, 0.156f), P(0.0125f, 0.18f), P(0, 0.18f));
                Round(b.Gold, P(0.0145f, 0.172f), P(0.0145f, 0.198f), P(0, 0.198f));
                AddLathe(get(b.LabelBlack), m, new[] { P(0.0585f, 0.048f), P(0.0585f, 0.075f) }, 10, false, -140f, 100f);
                break;
            case 6: // round liqueur
                Round(Pick(b.Liqueurs), P(0, 0), P(0.03f, 0), P(0.045f, 0.02f), P(0.05f, 0.055f), P(0.045f, 0.095f), P(0.028f, 0.12f), P(0.012f, 0.13f), P(0.011f, 0.16f), P(0, 0.16f));
                Round(Pick(b.Black, b.Gold), P(0.0125f, 0.152f), P(0.0125f, 0.175f), P(0, 0.175f));
                Label(Pick(b.LabelWhite, b.LabelCream), 0.0502f, 0.042f, 0.068f, 110f);
                break;
            case 7: // champagne
                Round(b.DarkGreen, P(0, 0), P(0.043f, 0), P(0.045f, 0.006f), P(0.045f, 0.16f), P(0.041f, 0.19f), P(0.028f, 0.23f), P(0.0165f, 0.265f), P(0.0155f, 0.29f), P(0, 0.29f));
                Round(b.Gold, P(0.0175f, 0.245f), P(0.0175f, 0.285f), P(0.018f, 0.3f), P(0, 0.305f));
                Label(b.LabelCream, 0.045f, 0.05f, 0.11f, 110f);
                break;
            case 8: // slim vermouth / aperitif
                Round(Pick(b.VermouthRed, b.Straw), P(0, 0), P(0.035f, 0), P(0.0365f, 0.005f), P(0.0365f, 0.19f), P(0.031f, 0.215f), P(0.016f, 0.24f), P(0.0135f, 0.25f), P(0.0135f, 0.29f), P(0, 0.29f));
                Round(Pick(b.Gold, b.Black), P(0.015f, 0.282f), P(0.015f, 0.305f), P(0, 0.305f));
                Label(Pick(b.LabelRed, b.LabelParchment), 0.0365f, 0.05f, 0.14f, 130f);
                break;
            case 9: // small bitters with an oversized paper label
                Round(b.DarkAmber, P(0, 0), P(0.024f, 0), P(0.025f, 0.004f), P(0.025f, 0.085f), P(0.017f, 0.1f), P(0.0095f, 0.108f), P(0.0095f, 0.118f), P(0, 0.118f));
                Round(Pick(b.Gold, b.RedCap), P(0.011f, 0.112f), P(0.011f, 0.135f), P(0, 0.135f));
                Label(b.LabelCream, 0.0255f, 0.01f, 0.088f, 300f);
                break;
            default: // round scotch with a cork
                Round(b.Amber, P(0, 0), P(0.039f, 0), P(0.041f, 0.006f), P(0.041f, 0.17f), P(0.037f, 0.19f), P(0.022f, 0.21f), P(0.016f, 0.218f), P(0.016f, 0.245f), P(0, 0.245f));
                Round(b.Cork, P(0.0175f, 0.24f), P(0.02f, 0.245f), P(0.02f, 0.262f), P(0, 0.265f));
                Label(Pick(b.LabelCream, b.LabelBlack), 0.041f, 0.04f, 0.13f, 130f);
                break;
        }
    }

    // Paper label: background, a border, a title band and lines of "small print".
    static Texture2D LabelTex(string name, Color paper, Color accent, Color ink, int seed)
    {
        return MakeTex(name, 256, 128, (u, v) =>
        {
            var c = paper;
            float edgeU = Mathf.Min(u, 1f - u), edgeV = Mathf.Min(v, 1f - v);
            if (edgeV < 0.05f || edgeU < 0.025f) c = accent;
            else if (edgeV < 0.075f || edgeU < 0.04f) c = paper;
            else if (edgeV < 0.09f || edgeU < 0.05f) c = Color.Lerp(paper, accent, 0.8f);
            else
            {
                // Title: a solid band of bold "letters" across the middle.
                if (v > 0.5f && v < 0.68f && u > 0.18f && u < 0.82f)
                {
                    float letter = Mathf.Repeat(u * 22f, 1f);
                    if (letter > 0.18f) c = Color.Lerp(c, ink, 0.9f);
                }
                // Brand crest / accent band.
                if (v > 0.74f && v < 0.8f && u > 0.3f && u < 0.7f) c = accent;
                // Small print rows.
                for (int row = 0; row < 3; row++)
                {
                    float y = 0.2f + row * 0.09f;
                    if (Mathf.Abs(v - y) < 0.018f)
                    {
                        float len = 0.25f + 0.2f * Hash(seed, row);
                        if (Mathf.Abs(u - 0.5f) < len && Mathf.Repeat(u * 60f + row, 1f) > 0.3f) c = Color.Lerp(c, ink, 0.55f);
                    }
                }
            }
            c.a = 1f;
            return c;
        });
    }

    static float Hash(int a, int b) => Mathf.Repeat(Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f, 1f);

    static Texture2D MakeTex(string name, int w, int h, Func<float, float, Color> f)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        string path = $"{TexFolder}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.mipmapEnabled = true;
        imp.anisoLevel = 4;
        imp.sRGBTexture = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ================================================================ meshes

    sealed class MeshData
    {
        public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
    }

    static Vector2 P(float r, float y) => new Vector2(r, y);

    // Small closed circle (radius rr around (cx, cy)) as a lathe profile - a torus.
    static Vector2[] Ring(float cx, float cy, float rr)
    {
        var p = new Vector2[11];
        for (int i = 0; i <= 10; i++)
        {
            float a = (-90f + i * 36f) * Mathf.Deg2Rad;
            p[i] = new Vector2(cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr);
        }
        return p;
    }

    static Mesh Lathe(string name, Vector2[] profile, int segs)
    {
        var d = new MeshData();
        AddLathe(d, Matrix4x4.identity, profile, segs, false, 0f, 360f);
        return SaveMesh(d, name);
    }

    // Spins a profile (x = radius, y = height, listed bottom to top) around Y.
    // flat = hard-edged faces (square bottles). The arc runs from a0 (degrees;
    // -90 faces +Z) clockwise seen from above, so faces point outward.
    static void AddLathe(MeshData d, Matrix4x4 m, Vector2[] prof, int segs, bool flat, float a0, float arc)
    {
        int n = prof.Length;
        var normals2 = new Vector2[n];
        var vlen = new float[n];
        for (int i = 0; i < n; i++)
        {
            var t = prof[Mathf.Min(i + 1, n - 1)] - prof[Mathf.Max(i - 1, 0)];
            normals2[i] = t.sqrMagnitude > 1e-12f ? new Vector2(t.y, -t.x).normalized : Vector2.right;
            if (i > 0) vlen[i] = vlen[i - 1] + Vector2.Distance(prof[i], prof[i - 1]);
        }
        float total = Mathf.Max(vlen[n - 1], 1e-5f);
        float step = arc / segs * Mathf.Deg2Rad, start = a0 * Mathf.Deg2Rad;
        Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));

        if (!flat)
        {
            int b = d.V.Count;
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= segs; j++)
                {
                    var dir = Dir(start + j * step);
                    d.V.Add(m.MultiplyPoint3x4(dir * prof[i].x + Vector3.up * prof[i].y));
                    d.N.Add(m.MultiplyVector(dir * normals2[i].x + Vector3.up * normals2[i].y).normalized);
                    d.UV.Add(new Vector2(1f - (float)j / segs, vlen[i] / total));
                }
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < segs; j++)
                {
                    int A = b + i * (segs + 1) + j, B = A + 1, C = A + segs + 1, D = C + 1;
                    d.T.Add(A); d.T.Add(B); d.T.Add(C);
                    d.T.Add(B); d.T.Add(D); d.T.Add(C);
                }
            return;
        }

        for (int j = 0; j < segs; j++)
        {
            float a = start + j * step;
            var face = Dir(a + step * 0.5f);
            int b = d.V.Count;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < 2; k++)
                {
                    var dir = Dir(a + k * step);
                    d.V.Add(m.MultiplyPoint3x4(dir * prof[i].x + Vector3.up * prof[i].y));
                    d.N.Add(m.MultiplyVector(face * normals2[i].x + Vector3.up * normals2[i].y).normalized);
                    d.UV.Add(new Vector2(1f - (float)k, vlen[i] / total));
                }
            for (int i = 0; i < n - 1; i++)
            {
                int A = b + i * 2, B = A + 1, C = A + 2, D = A + 3;
                d.T.Add(A); d.T.Add(B); d.T.Add(C);
                d.T.Add(B); d.T.Add(D); d.T.Add(C);
            }
        }
    }

    static Mesh SaveMesh(MeshData d, string name)
    {
        string path = $"{MeshFolder}/{name}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = mesh == null;
        if (create) mesh = new Mesh { name = name };
        else mesh.Clear();
        mesh.indexFormat = d.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(d.V);
        mesh.SetNormals(d.N);
        mesh.SetUVs(0, d.UV);
        mesh.SetTriangles(d.T, 0);
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static void MeshPart(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static Transform PrimPart(string name, PrimitiveType type, Transform parent, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    // ============================================================= materials

    static Material Lit(string name, Color color, float smooth, float metal = 0f, Texture tex = null)
    {
        string path = $"{MatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Metallic", metal);
        if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.mainTexture = tex; }
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Alpha-blended URP Lit, set up the way the shader GUI would.
    static Material Clear(string name, Color color, float smooth)
    {
        string path = $"{MatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
