using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Builds a complete blackjack table into the open scene: an oval felt
// table, a card shoe, the six game buttons with labels (- / + bet, DEAL,
// HIT, STAND, DOUBLE), the score display lying on the felt, and the dealer
// standing behind it. See BlackjackTable for the rules.
//
// Like the bar, it is dropped onto the floor under the Scene view's focus
// point with the player's side turned toward the Scene camera. Move/rotate
// the "Blackjack Table" root freely afterwards - the player stands on its
// +Z side, the dealer on -Z. Only builds if it doesn't exist yet; delete it
// and re-run Tools > Blackjack > Build Blackjack Table to rebuild.
//
// It also builds itself once, automatically, the first time these scripts
// compile with the casino scene open.
public static class SetupBlackjack
{
    const string RootName = "Blackjack Table";
    const string CasinoScene = "SampleScene";
    const float Top = 0.95f;          // felt surface height
    const float HalfWidth = 1f;       // felt ellipse, along X
    const float HalfDepth = 0.65f;    // along Z

    static string AutoBuildKey => "SetupBlackjack.AutoBuilt." + Application.dataPath;

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

    [MenuItem("Tools/Blackjack/Build Blackjack Table")]
    static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log($"SetupBlackjack: '{RootName}' already exists. Delete it and re-run to rebuild.");
            return;
        }

        SetupAvatar.EnsureFolder("Assets/Avatar");
        SetupAvatar.EnsureFolder(SetupAvatar.MaterialsFolder);

        var felt = SetupAvatar.Lit("Blackjack Felt", new Color(0.04f, 0.3f, 0.16f), 0.15f);
        var wood = SetupAvatar.Lit("Blackjack Wood", new Color(0.2f, 0.1f, 0.05f), 0.6f);
        var leather = SetupAvatar.Lit("Blackjack Leather", new Color(0.1f, 0.05f, 0.03f), 0.45f);
        var gold = SetupAvatar.Lit("Blackjack Gold", new Color(0.9f, 0.68f, 0.2f), 0.8f, 0.8f);
        var black = SetupAvatar.Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f);
        var cardMat = SetupAvatar.Lit("Blackjack Card", Color.white, 0.25f);
        var chipMat = SetupAvatar.Lit("Blackjack Chip", new Color(0.75f, 0.08f, 0.1f), 0.6f);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build " + RootName);
        SetupAvatar.PlaceAtSceneFocus(root.transform);
        var r = root.transform;

        // ----- Table
        var table = SetupAvatar.Group("Table", r);
        SetupAvatar.Part("Felt", PrimitiveType.Cylinder, table, felt, new Vector3(0f, Top - 0.02f, 0f), new Vector3(HalfWidth * 2f, 0.02f, HalfDepth * 2f));
        SetupAvatar.Part("Rim", PrimitiveType.Cylinder, table, leather, new Vector3(0f, Top - 0.045f, 0f), new Vector3(HalfWidth * 2f + 0.14f, 0.035f, HalfDepth * 2f + 0.14f));
        SetupAvatar.Part("Apron", PrimitiveType.Cylinder, table, wood, new Vector3(0f, Top - 0.13f, 0f), new Vector3(HalfWidth * 2f + 0.04f, 0.06f, HalfDepth * 2f + 0.04f));
        SetupAvatar.Part("Pedestal", PrimitiveType.Cylinder, table, wood, new Vector3(0f, (Top - 0.19f) / 2f, 0f), new Vector3(0.45f, (Top - 0.19f) / 2f, 0.45f));
        SetupAvatar.Part("Base", PrimitiveType.Cylinder, table, wood, new Vector3(0f, 0.025f, 0f), new Vector3(1.1f, 0.025f, 0.8f));
        SetupAvatar.Part("Base Trim", PrimitiveType.Cylinder, table, gold, new Vector3(0f, 0.055f, 0f), new Vector3(0.5f, 0.008f, 0.5f));

        var solid = table.gameObject.AddComponent<BoxCollider>(); // table top
        solid.center = new Vector3(0f, Top - 0.06f, 0f);
        solid.size = new Vector3(HalfWidth * 2f - 0.3f, 0.12f, HalfDepth * 2f - 0.1f);
        var post = table.gameObject.AddComponent<BoxCollider>();
        post.center = new Vector3(0f, Top / 2f, 0f);
        post.size = new Vector3(0.4f, Top, 0.4f);

        // Betting circle and the card shoe (by the dealer's right hand).
        SetupAvatar.Part("Bet Circle", PrimitiveType.Cylinder, table, gold, new Vector3(0.5f, Top + 0.0005f, 0.1f), new Vector3(0.11f, 0.0005f, 0.11f));
        SetupAvatar.Part("Bet Circle Felt", PrimitiveType.Cylinder, table, felt, new Vector3(0.5f, Top + 0.001f, 0.1f), new Vector3(0.095f, 0.0005f, 0.095f));
        var betStack = SetupAvatar.Group("Bet Stack", r);
        betStack.localPosition = new Vector3(0.5f, Top + 0.002f, 0.1f);

        var shoeBody = SetupAvatar.Box("Shoe", table, black, new Vector3(0.72f, Top + 0.05f, -0.3f), new Vector3(0.12f, 0.1f, 0.2f), false);
        shoeBody.localRotation = Quaternion.Euler(0f, -20f, 0f);
        SetupAvatar.Box("Shoe Cards", table, cardMat, new Vector3(0.685f, Top + 0.055f, -0.215f), new Vector3(0.09f, 0.085f, 0.03f), false)
            .localRotation = Quaternion.Euler(-25f, -20f, 0f);
        var shoe = SetupAvatar.Group("Shoe Mouth", r);
        shoe.localPosition = new Vector3(0.66f, Top + 0.05f, -0.19f);

        // Rows start at the player's left (+X, since the player faces -Z)
        // and BlackjackTable lays cards toward -X.
        var dealerCards = SetupAvatar.Group("Dealer Cards", r);
        dealerCards.localPosition = new Vector3(0.24f, Top, -0.27f);
        var playerCards = SetupAvatar.Group("Player Cards", r);
        playerCards.localPosition = new Vector3(0.24f, Top, 0.2f);

        // Score display, flat on the felt between the two rows, top edge
        // toward the dealer (a Quad's visible face is its -Z).
        var dark = new Color(0.02f, 0.12f, 0.07f);
        var flat = Quaternion.LookRotation(Vector3.down, Vector3.back);
        var info = SetupAvatar.TextQuad("Score Display", r, "BLACKJACK", Color.white, dark, new Vector2Int(1024, 240));
        info.transform.localPosition = new Vector3(0f, Top + 0.002f, -0.04f);
        info.transform.localRotation = flat;
        info.transform.localScale = new Vector3(0.68f, 0.16f, 1f);

        var rules = SetupAvatar.TextQuad("Rules", r, "BLACKJACK PAYS 3 TO 2\n<size=60%>DEALER MUST STAND ON 17</size>",
            new Color(1f, 0.82f, 0.29f), new Color(0.04f, 0.3f, 0.16f), new Vector2Int(1024, 160));
        rules.transform.localPosition = new Vector3(0f, Top + 0.002f, -0.47f);
        rules.transform.localRotation = flat;
        rules.transform.localScale = new Vector3(0.48f, 0.075f, 1f);

        // ----- Dealer
        var dealer = BuildDealer(r);

        var game = root.AddComponent<BlackjackTable>();
        var so = new SerializedObject(game);
        so.FindProperty("dealer").objectReferenceValue = dealer;
        so.FindProperty("infoPanel").objectReferenceValue = info;
        so.FindProperty("shoe").objectReferenceValue = shoe;
        so.FindProperty("dealerCards").objectReferenceValue = dealerCards;
        so.FindProperty("playerCards").objectReferenceValue = playerCards;
        so.FindProperty("betStack").objectReferenceValue = betStack;
        so.FindProperty("cardMaterial").objectReferenceValue = cardMat;
        so.FindProperty("chipMaterial").objectReferenceValue = chipMat;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ----- Buttons, along the player's edge, left to right as the
        // player sees them (so from +X to -X).
        var buttons = SetupAvatar.Group("Buttons", r);
        Button(buttons, game, BlackjackAction.BetDown, "-", 0.66f, new Color(0.5f, 0.5f, 0.55f), wood, flat, dark);
        Button(buttons, game, BlackjackAction.BetUp, "+", 0.5f, new Color(0.5f, 0.5f, 0.55f), wood, flat, dark);
        Button(buttons, game, BlackjackAction.Deal, "DEAL", 0.24f, new Color(0.95f, 0.7f, 0.15f), wood, flat, dark);
        Button(buttons, game, BlackjackAction.Hit, "HIT", -0.04f, new Color(0.15f, 0.7f, 0.25f), wood, flat, dark);
        Button(buttons, game, BlackjackAction.Stand, "STAND", -0.3f, new Color(0.8f, 0.15f, 0.12f), wood, flat, dark);
        Button(buttons, game, BlackjackAction.Double, "DOUBLE", -0.56f, new Color(0.2f, 0.4f, 0.9f), wood, flat, dark);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupBlackjack: built '{RootName}' at {root.transform.position}. Move/rotate the root to place it - the player stands on its +Z side. Save the scene to keep it.");
    }

    // Sits just inside the felt's edge, with its label on the felt beyond it.
    static void Button(Transform parent, BlackjackTable game, BlackjackAction action, string label, float x, Color colour,
        Material plinthMat, Quaternion flat, Color labelBackground)
    {
        float z = HalfDepth * Mathf.Sqrt(1f - x * x / (HalfWidth * HalfWidth)) - 0.11f;

        var station = SetupAvatar.Group($"{action} Button", parent);
        station.localPosition = new Vector3(x, Top, z);
        SetupAvatar.Part("Plinth", PrimitiveType.Cylinder, station, plinthMat, new Vector3(0f, 0.006f, 0f), new Vector3(0.1f, 0.006f, 0.1f));

        var button = new GameObject("Button");
        button.transform.SetParent(station, false);
        var box = button.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.03f, 0f);
        box.size = new Vector3(0.1f, 0.06f, 0.1f);

        var cap = SetupAvatar.Part("Cap", PrimitiveType.Cylinder, button.transform,
            SetupAvatar.Lit($"Blackjack Button {action}", colour, 0.7f), new Vector3(0f, 0.024f, 0f), new Vector3(0.08f, 0.012f, 0.08f));

        button.AddComponent<XRSimpleInteractable>();
        var so = new SerializedObject(button.AddComponent<BlackjackButton>());
        so.FindProperty("table").objectReferenceValue = game;
        so.FindProperty("action").enumValueIndex = (int)action;
        so.FindProperty("cap").objectReferenceValue = cap;
        so.ApplyModifiedPropertiesWithoutUndo();

        var text = SetupAvatar.TextQuad("Label", station, label, Color.white, labelBackground, new Vector2Int(256, 96));
        text.transform.localPosition = new Vector3(0f, 0.002f, -0.095f);
        text.transform.localRotation = flat;
        text.transform.localScale = new Vector3(0.13f, 0.05f, 1f);
    }

    static BlackjackDealer BuildDealer(Transform root)
    {
        var look = new SetupAvatar.Look
        {
            Jacket = SetupAvatar.Lit("Dealer Vest", new Color(0.5f, 0.06f, 0.1f), 0.45f),
            Shirt = SetupAvatar.Lit("Character Shirt", new Color(0.95f, 0.95f, 0.93f), 0.3f),
            Trousers = SetupAvatar.Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f),
            Skin = SetupAvatar.Lit("Dealer Skin", new Color(0.96f, 0.78f, 0.66f), 0.35f),
            Hair = SetupAvatar.Lit("Dealer Hair", new Color(0.92f, 0.76f, 0.38f), 0.5f),
            Accent = SetupAvatar.Lit("Blackjack Gold", new Color(0.9f, 0.68f, 0.2f), 0.8f, 0.8f),
            Dark = SetupAvatar.Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f),
            Mouth = SetupAvatar.Lit("Dealer Lips", new Color(0.8f, 0.12f, 0.2f), 0.5f),
            Vest = true, BowTie = true, HairBun = true, Earrings = true,
            // Resting on the edge of the felt rather than clasped at the waist.
            HandRest = new Vector3(0.33f, Top + 0.05f, 0.42f),
        };

        var go = new GameObject("Dealer");
        var t = go.transform;
        t.SetParent(root, false);
        t.localPosition = new Vector3(0f, 0f, -(HalfDepth + 0.3f));
        var parts = SetupAvatar.BuildCharacter(t, look, hands: true, separateLegs: false);

        var bubble = SetupAvatar.TextQuad("Speech Bubble", t, "", new Color(0.1f, 0.08f, 0.06f), new Color(1f, 0.97f, 0.9f),
            new Vector2Int(768, 256), 90f);
        bubble.transform.localPosition = new Vector3(0f, 2.05f, 0.1f);
        bubble.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        bubble.transform.localScale = new Vector3(0.78f, 0.26f, 1f);

        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1.3f, 0f);
        capsule.radius = 0.24f;
        capsule.height = 1f;
        go.AddComponent<XRSimpleInteractable>();

        var dealer = go.AddComponent<BlackjackDealer>();
        var so = new SerializedObject(dealer);
        so.FindProperty("head").objectReferenceValue = parts.Head;
        so.FindProperty("body").objectReferenceValue = parts.Body;
        SetupAvatar.SetArray(so, "shoulders", parts.Shoulders);
        SetupAvatar.SetArray(so, "hands", parts.Hands);
        SetupAvatar.SetArray(so, "upperArms", parts.UpperArms);
        SetupAvatar.SetArray(so, "forearms", parts.Forearms);
        so.FindProperty("speechBubble").objectReferenceValue = bubble;
        so.ApplyModifiedPropertiesWithoutUndo();

        for (int i = 0; i < 2; i++)
            CharacterLimbs.PoseArm(parts.UpperArms[i], parts.Forearms[i], parts.Shoulders[i].position, parts.Hands[i].position,
                t.rotation, i == 0 ? 1f : -1f, SetupAvatar.ArmThickness);
        return dealer;
    }
}
