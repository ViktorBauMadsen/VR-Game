using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Builds the casino bar into the open scene: counter, back shelves with
// bottles, stools, a menu board, the bartender (built from smooth
// primitives - vest, bow tie, mustache), and two order buttons on the
// counter (Cigar $100, Whiskey $50). Also creates the Cigar and Whiskey
// prefabs the bartender serves, under Assets/Bar/Prefabs.
//
// Like the beer pong table, the bar is dropped onto whatever is under the
// Scene view's focus point, with the customer side turned toward the Scene
// view camera. Move/rotate the "Casino Bar" root freely afterwards -
// customers stand on its +Z side. Only builds if "Casino Bar" doesn't exist
// yet; delete it and re-run Tools > Bar > Build Bar to rebuild.
//
// It also builds itself once, automatically, the first time these scripts
// compile with the casino scene open.
public static class SetupBar
{
    const string RootName = "Casino Bar";
    const string Folder = "Assets/Bar";
    const string MaterialsFolder = Folder + "/Materials";
    const string PrefabsFolder = Folder + "/Prefabs";
    const string CasinoScene = "SampleScene";

    const float CounterHeight = 1.05f;
    const float CounterLength = 3.2f;
    const float BartenderZ = -0.65f;

    static string AutoBuildKey => "SetupBar.AutoBuilt." + Application.dataPath;

    [InitializeOnLoadMethod]
    static void AutoBuildOnce()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorPrefs.GetBool(AutoBuildKey, false)) return;
            if (EditorSceneManager.GetActiveScene().name != CasinoScene) return;
            EditorPrefs.SetBool(AutoBuildKey, true);
            if (GameObject.Find(RootName) == null) Build();
        };
    }

    [MenuItem("Tools/Bar/Build Bar")]
    static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log($"SetupBar: '{RootName}' already exists. Delete it and re-run to rebuild.");
            return;
        }

        EnsureFolder(Folder);
        EnsureFolder(MaterialsFolder);
        EnsureFolder(PrefabsFolder);

        var m = new Mats();
        var cigarPrefab = BuildCigarPrefab(m);
        var whiskeyPrefab = BuildWhiskeyPrefab(m);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build " + RootName);
        PlaceRoot(root.transform);

        BuildCounter(root.transform, m);
        BuildBackBar(root.transform, m);
        BuildStools(root.transform, m);

        var serve = new Transform[3];
        for (int i = 0; i < serve.Length; i++)
        {
            serve[i] = new GameObject($"Serve Point {i + 1}").transform;
            serve[i].SetParent(root.transform, false);
            serve[i].localPosition = new Vector3((i - 1) * 0.2f, CounterHeight + 0.03f, 0.02f);
        }

        var bartender = BuildBartender(root.transform, m, serve);
        BuildOrderStation(root.transform, m, bartender, "Cigar", "cigar", 100, cigarPrefab, -0.55f,
            new Color(0.75f, 0.25f, 0.1f), new Vector3(0f, 90f, 0f));
        BuildOrderStation(root.transform, m, bartender, "Whiskey", "whiskey", 50, whiskeyPrefab, 0.55f,
            new Color(0.95f, 0.65f, 0.12f), Vector3.zero);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupBar: built '{RootName}' at {root.transform.position}. Move/rotate the root to place it - customers stand on its +Z side. Save the scene to keep it.");
    }

    // On the floor under the Scene view's focus point, customer side turned
    // (in 90 degree steps) toward the Scene view camera.
    static void PlaceRoot(Transform root)
    {
        var view = SceneView.lastActiveSceneView;
        Vector3 pivot = view != null ? view.pivot : Vector3.zero;
        Vector3 point = pivot;
        if (Physics.Raycast(pivot + Vector3.up * 3f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            point = hit.point;
        root.position = point;

        if (view != null && view.camera != null)
        {
            Vector3 toCamera = view.camera.transform.position - point;
            float yaw = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg;
            root.rotation = Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f);
        }
    }

    // ---------------------------------------------------------------- bar

    static void BuildCounter(Transform root, Mats m)
    {
        var counter = Group("Counter", root);
        Box("Counter Body", counter, m.DarkWood, new Vector3(0f, 0.5f, 0.05f), new Vector3(CounterLength, 1f, 0.5f));
        Box("Counter Top", counter, m.Wood, new Vector3(0f, CounterHeight - 0.025f, 0f), new Vector3(CounterLength + 0.1f, 0.05f, 0.7f));
        Box("Front Trim", counter, m.Brass, new Vector3(0f, 0.92f, 0.305f), new Vector3(CounterLength, 0.02f, 0.012f), collider: false);
        Box("Kick Plate", counter, m.Brass, new Vector3(0f, 0.06f, 0.305f), new Vector3(CounterLength, 0.12f, 0.012f), collider: false);

        var rail = Prim("Foot Rail", PrimitiveType.Cylinder, counter, m.Brass, collider: false);
        rail.localPosition = new Vector3(0f, 0.22f, 0.42f);
        rail.localRotation = Quaternion.Euler(0f, 0f, 90f);
        rail.localScale = new Vector3(0.04f, CounterLength / 2f, 0.04f);
        for (int i = -1; i <= 1; i++)
            Box($"Rail Bracket {i + 2}", counter, m.Brass, new Vector3(i * (CounterLength / 2f - 0.15f), 0.22f, 0.36f), new Vector3(0.03f, 0.03f, 0.12f), collider: false);
    }

    static void BuildBackBar(Transform root, Mats m)
    {
        var back = Group("Back Bar", root);
        Box("Back Panel", back, m.DarkWood, new Vector3(0f, 1.35f, -1.6f), new Vector3(CounterLength, 2.7f, 0.08f));
        Box("Back Cabinet", back, m.DarkWood, new Vector3(0f, 0.45f, -1.38f), new Vector3(CounterLength, 0.9f, 0.4f));
        Box("Cabinet Top", back, m.Wood, new Vector3(0f, 0.915f, -1.38f), new Vector3(CounterLength, 0.03f, 0.44f), collider: false);

        float[] shelfHeights = { 0.93f, 1.35f, 1.75f };
        Color[] bottleColors =
        {
            new Color(0.1f, 0.35f, 0.15f), new Color(0.45f, 0.22f, 0.06f), new Color(0.75f, 0.55f, 0.15f),
            new Color(0.15f, 0.25f, 0.5f), new Color(0.6f, 0.1f, 0.12f), new Color(0.85f, 0.85f, 0.8f),
        };
        var bottleMats = new Material[bottleColors.Length];
        for (int i = 0; i < bottleMats.Length; i++)
            bottleMats[i] = m.Lit($"Bottle {i + 1}", bottleColors[i], 0.85f);

        var random = new System.Random(11);
        var bottles = Group("Bottles", back);
        for (int s = 0; s < shelfHeights.Length; s++)
        {
            if (s > 0)
                Box($"Shelf {s}", back, m.Wood, new Vector3(0f, shelfHeights[s] - 0.015f, -1.46f), new Vector3(CounterLength - 0.2f, 0.03f, 0.22f), collider: false);

            for (float x = -CounterLength / 2f + 0.25f; x < CounterLength / 2f - 0.2f; x += 0.19f)
            {
                if (random.NextDouble() < 0.15) continue; // a few gaps
                float height = 0.2f + (float)random.NextDouble() * 0.08f;
                float width = 0.06f + (float)random.NextDouble() * 0.02f;
                var mat = bottleMats[random.Next(bottleMats.Length)];
                var bottle = Group("Bottle", bottles);
                bottle.localPosition = new Vector3(x + (float)random.NextDouble() * 0.03f, shelfHeights[s], -1.46f);

                var body = Prim("Body", PrimitiveType.Cylinder, bottle, mat, collider: false);
                body.localPosition = new Vector3(0f, height / 2f, 0f);
                body.localScale = new Vector3(width, height / 2f, width);
                var shoulder = Prim("Shoulder", PrimitiveType.Sphere, bottle, mat, collider: false);
                shoulder.localPosition = new Vector3(0f, height, 0f);
                shoulder.localScale = new Vector3(width, width * 0.8f, width);
                var neck = Prim("Neck", PrimitiveType.Cylinder, bottle, mat, collider: false);
                neck.localPosition = new Vector3(0f, height + 0.05f, 0f);
                neck.localScale = new Vector3(width * 0.35f, 0.05f, width * 0.35f);
            }
        }

        // Menu board. A Quad's visible face is its local -Z, so it looks
        // back along the bar's -Z for customers standing on the +Z side.
        Box("Menu Frame", back, m.Brass, new Vector3(0f, 2.3f, -1.55f), new Vector3(1.56f, 0.76f, 0.03f), collider: false);
        var menu = TextQuad("Menu Board", back, m,
            "<color=#FFD24A>THE LUCKY SIP</color>\n<size=70%>Cigar <color=#FFD24A>$100</color>     Whiskey <color=#FFD24A>$50</color></size>\n<size=40%>Press a button on the bar to order</size>",
            Color.white, new Color(0.05f, 0.03f, 0.02f), new Vector2Int(1024, 480));
        menu.localPosition = new Vector3(0f, 2.3f, -1.53f);
        menu.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        menu.localScale = new Vector3(1.5f, 0.7f, 1f);
    }

    static void BuildStools(Transform root, Mats m)
    {
        var stools = Group("Stools", root);
        for (int i = -1; i <= 1; i += 2)
        {
            var stool = Group("Stool", stools);
            stool.localPosition = new Vector3(i * 1.2f, 0f, 0.85f);
            var seat = Prim("Seat", PrimitiveType.Cylinder, stool, m.Leather, collider: false);
            seat.localPosition = new Vector3(0f, 0.74f, 0f);
            seat.localScale = new Vector3(0.36f, 0.035f, 0.36f);
            var leg = Prim("Leg", PrimitiveType.Cylinder, stool, m.Brass, collider: false);
            leg.localPosition = new Vector3(0f, 0.36f, 0f);
            leg.localScale = new Vector3(0.05f, 0.36f, 0.05f);
            var foot = Prim("Foot", PrimitiveType.Cylinder, stool, m.Brass, collider: false);
            foot.localPosition = new Vector3(0f, 0.015f, 0f);
            foot.localScale = new Vector3(0.38f, 0.015f, 0.38f);
            var ring = Prim("Foot Ring", PrimitiveType.Cylinder, stool, m.Brass, collider: false);
            ring.localPosition = new Vector3(0f, 0.3f, 0f);
            ring.localScale = new Vector3(0.26f, 0.01f, 0.26f);
        }
    }

    // A plinth with a big push button and a tilted price label behind it.
    static void BuildOrderStation(Transform root, Mats m, Bartender bartender, string title, string itemName, int price,
        GameObject prefab, float x, Color buttonColor, Vector3 serveEuler)
    {
        var station = Group($"Order {title}", root);
        station.localPosition = new Vector3(x, CounterHeight, 0.2f);

        Box("Plinth", station, m.DarkWood, new Vector3(0f, 0.012f, 0f), new Vector3(0.17f, 0.024f, 0.17f), collider: false);

        var button = new GameObject("Button");
        button.transform.SetParent(station, false);
        var box = button.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.04f, 0f);
        box.size = new Vector3(0.13f, 0.07f, 0.13f);

        var cap = Prim("Cap", PrimitiveType.Cylinder, button.transform, m.Lit($"Button {title}", buttonColor, 0.7f), collider: false);
        cap.localPosition = new Vector3(0f, 0.04f, 0f);
        cap.localScale = new Vector3(0.11f, 0.016f, 0.11f);

        button.AddComponent<XRSimpleInteractable>();
        var so = new SerializedObject(button.AddComponent<BarOrderButton>());
        so.FindProperty("bartender").objectReferenceValue = bartender;
        so.FindProperty("itemPrefab").objectReferenceValue = prefab;
        so.FindProperty("itemName").stringValue = itemName;
        so.FindProperty("price").intValue = price;
        so.FindProperty("serveEuler").vector3Value = serveEuler;
        so.FindProperty("cap").objectReferenceValue = cap;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Leans back 25 degrees so it faces up toward a standing customer.
        const float tilt = 25f * Mathf.Deg2Rad;
        var label = TextQuad("Label", station, m, $"{title.ToUpper()}\n<color=#FFD24A>${price}</color>",
            Color.white, new Color(0.05f, 0.03f, 0.02f), new Vector2Int(512, 256));
        label.localPosition = new Vector3(0f, 0.075f, -0.14f);
        label.localRotation = Quaternion.LookRotation(
            new Vector3(0f, -Mathf.Sin(tilt), -Mathf.Cos(tilt)),
            new Vector3(0f, Mathf.Cos(tilt), -Mathf.Sin(tilt)));
        label.localScale = new Vector3(0.24f, 0.12f, 1f);
    }

    // ---------------------------------------------------------- bartender

    // Faces +Z (the customers). Classic "floating VR character" build:
    // everything is a sphere or capsule, so he reads as smooth and round.
    static Bartender BuildBartender(Transform root, Mats m, Transform[] servePoints)
    {
        var go = new GameObject("Bartender");
        var t = go.transform;
        t.SetParent(root, false);
        t.localPosition = new Vector3(0f, 0f, BartenderZ);

        var body = Group("Body", t);
        Part("Legs", PrimitiveType.Capsule, body, m.Black, new Vector3(0f, 0.5f, 0f), new Vector3(0.36f, 0.5f, 0.27f));
        Part("Shirt", PrimitiveType.Capsule, body, m.Shirt, new Vector3(0f, 1.15f, 0f), new Vector3(0.42f, 0.33f, 0.28f));
        Part("Vest", PrimitiveType.Capsule, body, m.Black, new Vector3(0f, 1.11f, 0f), new Vector3(0.445f, 0.27f, 0.305f));
        Part("Shirt Front", PrimitiveType.Sphere, body, m.Shirt, new Vector3(0f, 1.27f, 0.105f), new Vector3(0.13f, 0.27f, 0.115f));
        for (int i = 0; i < 3; i++)
            Part($"Vest Button {i + 1}", PrimitiveType.Sphere, body, m.Brass, new Vector3(0f, 1.1f - i * 0.07f, 0.153f), Vector3.one * 0.022f);
        Part("Apron", PrimitiveType.Sphere, body, m.Shirt, new Vector3(0f, 0.78f, 0.06f), new Vector3(0.36f, 0.42f, 0.2f));
        Part("Neck", PrimitiveType.Cylinder, body, m.Skin, new Vector3(0f, 1.46f, 0f), new Vector3(0.1f, 0.04f, 0.1f));
        Part("Collar L", PrimitiveType.Sphere, body, m.Shirt, new Vector3(-0.045f, 1.445f, 0.05f), new Vector3(0.09f, 0.05f, 0.09f));
        Part("Collar R", PrimitiveType.Sphere, body, m.Shirt, new Vector3(0.045f, 1.445f, 0.05f), new Vector3(0.09f, 0.05f, 0.09f));

        var bow = Group("Bow Tie", body);
        bow.localPosition = new Vector3(0f, 1.415f, 0.13f);
        Part("Left", PrimitiveType.Sphere, bow, m.BowTie, new Vector3(-0.04f, 0f, 0f), new Vector3(0.075f, 0.05f, 0.03f));
        Part("Right", PrimitiveType.Sphere, bow, m.BowTie, new Vector3(0.04f, 0f, 0f), new Vector3(0.075f, 0.05f, 0.03f));
        Part("Knot", PrimitiveType.Sphere, bow, m.BowTie, new Vector3(0f, 0f, 0.008f), Vector3.one * 0.032f);

        // Head: pivots at the neck so it can turn to look at the player.
        var head = Group("Head", t);
        head.localPosition = new Vector3(0f, 1.47f, 0f);
        Part("Skull", PrimitiveType.Sphere, head, m.Skin, new Vector3(0f, 0.14f, 0f), new Vector3(0.26f, 0.275f, 0.26f));
        Part("Hair", PrimitiveType.Sphere, head, m.Hair, new Vector3(0f, 0.2f, -0.022f), new Vector3(0.272f, 0.2f, 0.265f));
        Part("Ear L", PrimitiveType.Sphere, head, m.Skin, new Vector3(-0.128f, 0.135f, 0f), new Vector3(0.035f, 0.055f, 0.045f));
        Part("Ear R", PrimitiveType.Sphere, head, m.Skin, new Vector3(0.128f, 0.135f, 0f), new Vector3(0.035f, 0.055f, 0.045f));
        Part("Eye L", PrimitiveType.Sphere, head, m.Black, new Vector3(-0.047f, 0.16f, 0.116f), new Vector3(0.03f, 0.04f, 0.02f));
        Part("Eye R", PrimitiveType.Sphere, head, m.Black, new Vector3(0.047f, 0.16f, 0.116f), new Vector3(0.03f, 0.04f, 0.02f));
        var browL = Part("Brow L", PrimitiveType.Capsule, head, m.Hair, new Vector3(-0.047f, 0.2f, 0.112f), new Vector3(0.016f, 0.026f, 0.016f));
        browL.localRotation = Quaternion.Euler(0f, 0f, 82f);
        var browR = Part("Brow R", PrimitiveType.Capsule, head, m.Hair, new Vector3(0.047f, 0.2f, 0.112f), new Vector3(0.016f, 0.026f, 0.016f));
        browR.localRotation = Quaternion.Euler(0f, 0f, -82f);
        Part("Nose", PrimitiveType.Sphere, head, m.Skin, new Vector3(0f, 0.125f, 0.132f), new Vector3(0.04f, 0.045f, 0.045f));

        // Handlebar mustache: two drooping capsules with curled-up tips.
        var mustache = Group("Mustache", head);
        mustache.localPosition = new Vector3(0f, 0.095f, 0.125f);
        for (int side = -1; side <= 1; side += 2)
        {
            var half = Part(side < 0 ? "Left" : "Right", PrimitiveType.Capsule, mustache, m.Hair,
                new Vector3(side * 0.04f, -0.004f, -0.004f), new Vector3(0.03f, 0.046f, 0.03f));
            half.localRotation = Quaternion.Euler(0f, 0f, side * 75f);
            Part(side < 0 ? "Left Tip" : "Right Tip", PrimitiveType.Sphere, mustache, m.Hair,
                new Vector3(side * 0.083f, 0.006f, -0.018f), Vector3.one * 0.026f);
        }

        // Arms: shoulders are fixed points, hands are animated, and the
        // Bartender script stretches the four arm capsules between them.
        var arms = new Transform[2, 4]; // [side, shoulder/hand/upper/fore]
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            string n = i == 0 ? "Right" : "Left";
            arms[i, 0] = Group($"{n} Shoulder", t);
            arms[i, 0].localPosition = new Vector3(side * 0.23f, 1.37f, 0f);
            Part("Joint", PrimitiveType.Sphere, arms[i, 0], m.Shirt, Vector3.zero, Vector3.one * 0.11f);
            arms[i, 2] = Part($"{n} Upper Arm", PrimitiveType.Capsule, t, m.Shirt, Vector3.zero, Vector3.one * 0.085f);
            arms[i, 3] = Part($"{n} Forearm", PrimitiveType.Capsule, t, m.Shirt, Vector3.zero, Vector3.one * 0.085f);
            arms[i, 1] = Group($"{n} Hand", t);
            arms[i, 1].localPosition = new Vector3(side * 0.2f, CounterHeight + 0.055f, 0.4f);
            Part("Palm", PrimitiveType.Sphere, arms[i, 1], m.Skin, Vector3.zero, new Vector3(0.1f, 0.075f, 0.11f));
            Part("Cuff", PrimitiveType.Sphere, arms[i, 1], m.Shirt, new Vector3(0f, 0.012f, -0.05f), new Vector3(0.095f, 0.08f, 0.06f));
        }
        Part("Cloth", PrimitiveType.Sphere, arms[0, 1], m.Shirt, new Vector3(0f, -0.035f, 0.01f), new Vector3(0.17f, 0.025f, 0.15f));

        // Speech bubble above his head; the script turns it to face the player.
        var bubble = TextQuad("Speech Bubble", t, m, "", new Color(0.1f, 0.08f, 0.06f), new Color(1f, 0.97f, 0.9f), new Vector2Int(768, 256));
        bubble.localPosition = new Vector3(0f, 2.02f, 0.1f);
        bubble.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        bubble.localScale = new Vector3(0.78f, 0.26f, 1f);
        var bubbleSo = new SerializedObject(bubble.GetComponent<WorldTextPanel>());
        bubbleSo.FindProperty("maxFontSize").floatValue = 90f;
        bubbleSo.ApplyModifiedPropertiesWithoutUndo();

        // Pointing at him and pressing grab makes him chat.
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1.25f, 0f);
        capsule.radius = 0.26f;
        capsule.height = 1.1f;
        go.AddComponent<XRSimpleInteractable>();

        var bartender = go.AddComponent<Bartender>();
        var so = new SerializedObject(bartender);
        so.FindProperty("head").objectReferenceValue = head;
        so.FindProperty("mustache").objectReferenceValue = mustache;
        so.FindProperty("rightShoulder").objectReferenceValue = arms[0, 0];
        so.FindProperty("rightHand").objectReferenceValue = arms[0, 1];
        so.FindProperty("rightUpperArm").objectReferenceValue = arms[0, 2];
        so.FindProperty("rightForearm").objectReferenceValue = arms[0, 3];
        so.FindProperty("leftShoulder").objectReferenceValue = arms[1, 0];
        so.FindProperty("leftHand").objectReferenceValue = arms[1, 1];
        so.FindProperty("leftUpperArm").objectReferenceValue = arms[1, 2];
        so.FindProperty("leftForearm").objectReferenceValue = arms[1, 3];
        so.FindProperty("speechBubble").objectReferenceValue = bubble.GetComponent<WorldTextPanel>();
        var points = so.FindProperty("servePoints");
        points.arraySize = servePoints.Length;
        for (int i = 0; i < servePoints.Length; i++)
            points.GetArrayElementAtIndex(i).objectReferenceValue = servePoints[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // Pose the arms in the editor too, the same way the script does at runtime.
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            Vector3 s = arms[i, 0].localPosition, h = arms[i, 1].localPosition;
            Vector3 elbow = (s + h) * 0.5f + new Vector3(side * 0.07f, -0.07f, -0.06f);
            Stretch(arms[i, 2], s, elbow, 0.085f);
            Stretch(arms[i, 3], elbow, h, 0.085f);
        }
        return bartender;
    }

    static void Stretch(Transform capsule, Vector3 a, Vector3 b, float thickness)
    {
        Vector3 d = b - a;
        capsule.localPosition = (a + b) * 0.5f;
        capsule.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        capsule.localScale = new Vector3(thickness, (d.magnitude + thickness * 0.6f) * 0.5f, thickness);
    }

    // ------------------------------------------------------------ prefabs

    // Lies along its local Z: mouth end at -Z, burning tip at +Z.
    static GameObject BuildCigarPrefab(Mats m)
    {
        string path = PrefabsFolder + "/Cigar.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var go = new GameObject("Cigar");
        var t = go.transform;
        var side = Quaternion.Euler(90f, 0f, 0f); // cylinder axis Y -> Z

        var body = Part("Body", PrimitiveType.Cylinder, t, m.Tobacco, Vector3.zero, new Vector3(0.022f, 0.062f, 0.022f));
        body.localRotation = side;
        Part("Mouth End", PrimitiveType.Sphere, t, m.Tobacco, new Vector3(0f, 0f, -0.062f), new Vector3(0.022f, 0.022f, 0.02f));
        var band = Part("Band", PrimitiveType.Cylinder, t, m.Brass, new Vector3(0f, 0f, -0.028f), new Vector3(0.0235f, 0.007f, 0.0235f));
        band.localRotation = side;
        var ember = Part("Ember", PrimitiveType.Cylinder, t, m.Ember, new Vector3(0f, 0f, 0.064f), new Vector3(0.0215f, 0.003f, 0.0215f));
        ember.localRotation = side;

        var mouth = Group("Mouth Point", t);
        mouth.localPosition = new Vector3(0f, 0f, -0.07f);

        var tipSmoke = BuildSmoke("Tip Smoke", t, new Vector3(0f, 0f, 0.07f), m.Smoke, exhale: false);
        var exhaleSmoke = BuildSmoke("Exhale Smoke", t, Vector3.zero, m.Smoke, exhale: true);

        var box = go.AddComponent<BoxCollider>(); // a box, so it doesn't roll off the bar
        box.size = new Vector3(0.024f, 0.024f, 0.14f);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.05f;
        rb.angularDamping = 2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        AddGrab(go);

        var so = new SerializedObject(go.AddComponent<Cigar>());
        so.FindProperty("mouthPoint").objectReferenceValue = mouth;
        so.FindProperty("ember").objectReferenceValue = ember.GetComponent<Renderer>();
        so.FindProperty("tipSmoke").objectReferenceValue = tipSmoke;
        so.FindProperty("exhaleSmoke").objectReferenceValue = exhaleSmoke;
        so.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(go, path);
    }

    // A whiskey tumbler; origin at the bottom so it stands on the bar.
    static GameObject BuildWhiskeyPrefab(Mats m)
    {
        string path = PrefabsFolder + "/Whiskey.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var go = new GameObject("Whiskey");
        var t = go.transform;

        Part("Base", PrimitiveType.Cylinder, t, m.GlassBase, new Vector3(0f, 0.007f, 0f), new Vector3(0.072f, 0.007f, 0.072f));
        var pivot = Group("Liquid Pivot", t);
        pivot.localPosition = new Vector3(0f, 0.014f, 0f);
        Part("Liquid", PrimitiveType.Cylinder, pivot, m.Whiskey, new Vector3(0f, 0.026f, 0f), new Vector3(0.066f, 0.026f, 0.066f));
        Part("Glass", PrimitiveType.Cylinder, t, m.Glass, new Vector3(0f, 0.047f, 0f), new Vector3(0.076f, 0.047f, 0.076f));

        var mouth = Group("Mouth Point", t);
        mouth.localPosition = new Vector3(0f, 0.094f, 0f);

        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.047f, 0f);
        box.size = new Vector3(0.07f, 0.094f, 0.07f);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.3f;
        rb.angularDamping = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        AddGrab(go);

        var so = new SerializedObject(go.AddComponent<BarDrink>());
        so.FindProperty("mouthPoint").objectReferenceValue = mouth;
        so.FindProperty("liquidPivot").objectReferenceValue = pivot;
        so.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(go, path);
    }

    static void AddGrab(GameObject go)
    {
        var so = new SerializedObject(go.AddComponent<XRGrabInteractable>());
        so.FindProperty("m_MovementType").enumValueIndex = 2; // Instantaneous - 1:1 with the hand
        so.FindProperty("m_UseDynamicAttach").boolValue = true; // hold it where you grabbed it
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static GameObject SavePrefab(GameObject go, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // World-space smoke. The tip wisp is emitted by rate (the Cigar script
    // sets it); the exhale cloud is emitted by the script particle by
    // particle at the player's mouth, so it has no emission of its own.
    static ParticleSystem BuildSmoke(string name, Transform parent, Vector3 localPos, Material mat, bool exhale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = exhale ? new ParticleSystem.MinMaxCurve(1.4f, 2.6f) : new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSize = exhale ? new ParticleSystem.MinMaxCurve(0.12f, 0.2f) : new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.85f, 0.85f, 0.85f, exhale ? 0.3f : 0.35f);
        main.gravityModifier = exhale ? -0.01f : -0.04f; // drifts upward
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = !exhale;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.003f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f));

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = exhale ? 0.06f : 0.03f;
        noise.frequency = 0.7f;

        if (exhale)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 10f;
            limit.drag = 1.8f; // the puff slows down and hangs in the air
        }

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return ps;
    }

    // ------------------------------------------------------------ helpers

    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform Prim(string name, PrimitiveType type, Transform parent, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }

    static Transform Box(string name, Transform parent, Material mat, Vector3 pos, Vector3 size, bool collider = true)
    {
        var t = Prim(name, PrimitiveType.Cube, parent, mat, collider);
        t.localPosition = pos;
        t.localScale = size;
        return t;
    }

    // A visual-only piece (no collider).
    static Transform Part(string name, PrimitiveType type, Transform parent, Material mat, Vector3 pos, Vector3 scale)
    {
        var t = Prim(name, type, parent, mat, collider: false);
        t.localPosition = pos;
        t.localScale = scale;
        return t;
    }

    static Transform TextQuad(string name, Transform parent, Mats m, string text, Color textColor, Color background, Vector2Int textureSize)
    {
        var t = Prim(name, PrimitiveType.Quad, parent, m.TextScreen, collider: false);
        t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        var so = new SerializedObject(t.gameObject.AddComponent<WorldTextPanel>());
        so.FindProperty("text").stringValue = text;
        so.FindProperty("textColor").colorValue = textColor;
        so.FindProperty("background").colorValue = background;
        so.FindProperty("textureSize").vector2IntValue = textureSize;
        so.ApplyModifiedPropertiesWithoutUndo();
        return t;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    // All the bar's materials, created once under Assets/Bar/Materials.
    class Mats
    {
        public readonly Material Wood, DarkWood, Brass, Leather, Black, Shirt, Skin, Hair, BowTie;
        public readonly Material Tobacco, Ember, Whiskey, Glass, GlassBase, Smoke, TextScreen;

        public Mats()
        {
            Wood = Lit("Bar Wood", new Color(0.42f, 0.22f, 0.1f), 0.75f);
            DarkWood = Lit("Bar Dark Wood", new Color(0.16f, 0.08f, 0.04f), 0.5f);
            Brass = Lit("Bar Brass", new Color(0.85f, 0.65f, 0.25f), 0.85f, metallic: 1f);
            Leather = Lit("Bar Leather", new Color(0.45f, 0.06f, 0.06f), 0.5f);
            Black = Lit("Bartender Black", new Color(0.04f, 0.04f, 0.05f), 0.4f);
            Shirt = Lit("Bartender Shirt", new Color(0.95f, 0.95f, 0.93f), 0.3f);
            Skin = Lit("Bartender Skin", new Color(0.93f, 0.72f, 0.58f), 0.35f);
            Hair = Lit("Bartender Hair", new Color(0.13f, 0.07f, 0.04f), 0.45f);
            BowTie = Lit("Bartender Bow Tie", new Color(0.65f, 0.05f, 0.08f), 0.5f);
            Tobacco = Lit("Cigar Tobacco", new Color(0.33f, 0.19f, 0.09f), 0.2f);
            Whiskey = Lit("Whiskey", new Color(0.8f, 0.42f, 0.08f), 0.9f);

            Ember = Lit("Cigar Ember", new Color(0.35f, 0.33f, 0.32f), 0.1f);
            Ember.EnableKeyword("_EMISSION"); // so the emissive variant is in the build; Cigar drives the colour
            Ember.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Ember.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(Ember);

            Glass = Transparent("Glass", "Universal Render Pipeline/Lit", new Color(0.85f, 0.95f, 1f, 0.22f));
            Glass.SetFloat("_Smoothness", 0.95f);
            GlassBase = Transparent("Glass Base", "Universal Render Pipeline/Lit", new Color(0.85f, 0.95f, 1f, 0.5f));
            GlassBase.SetFloat("_Smoothness", 0.95f);

            Smoke = Transparent("Smoke", "Universal Render Pipeline/Particles/Unlit", Color.white);
            var puff = SmokeTexture();
            Smoke.SetTexture("_BaseMap", puff);
            if (Smoke.HasProperty("_MainTex")) Smoke.SetTexture("_MainTex", puff);
            EditorUtility.SetDirty(Smoke);

            TextScreen = Load("Bar Text Screen");
            if (TextScreen == null)
            {
                TextScreen = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Bar Text Screen" };
                TextScreen.SetColor("_BaseColor", Color.white);
                AssetDatabase.CreateAsset(TextScreen, MaterialsFolder + "/Bar Text Screen.mat");
            }
        }

        static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");

        public Material Lit(string name, Color color, float smoothness, float metallic = 0f)
        {
            var mat = Load(name);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(mat, $"{MaterialsFolder}/{name}.mat");
            return mat;
        }

        // Alpha-blended URP material, set up the way the shader GUI would.
        static Material Transparent(string name, string shader, Color color)
        {
            var mat = Load(name);
            if (mat != null) return mat;
            mat = new Material(Shader.Find(shader)) { name = name };
            mat.SetColor("_BaseColor", color);
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
            AssetDatabase.CreateAsset(mat, $"{MaterialsFolder}/{name}.mat");
            return mat;
        }

        // A soft round blob, so smoke particles aren't hard-edged squares.
        static Texture2D SmokeTexture()
        {
            string path = MaterialsFolder + "/Smoke Puff.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            const int size = 64;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Smoke Puff", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (size / 2f)) / (size / 2f);
                float a = Mathf.Clamp01(1f - d);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }
    }
}
