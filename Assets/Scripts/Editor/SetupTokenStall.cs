using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Builds the Token Stall (see TokenStall): a market stall with a striped
// canopy and a sign, four buy buttons on the counter with a card under each
// one on the counter's front, and Zorp the alien shopkeeper behind it.
//
//   Tools > Casino > Build Token Stall
//
// Like the other builders it is dropped on the floor under the Scene view's
// focus point with the customers' side (+Z) turned toward the Scene camera.
// Put it OUTSIDE the casino door, near the Run Entrance's spawn points - it
// is only open while the players are gathered there between days. Delete the
// root and re-run to rebuild.
public static class SetupTokenStall
{
    const string RootName = "Token Stall";
    const float Top = 0.95f;      // counter height
    const float HalfWidth = 1f;
    const float Depth = 0.5f;

    [MenuItem("Tools/Casino/Build Token Stall")]
    static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log($"SetupTokenStall: '{RootName}' already exists (selected it). Delete it and re-run to rebuild.");
            return;
        }

        SetupAvatar.EnsureFolder("Assets/Avatar");
        SetupAvatar.EnsureFolder(SetupAvatar.MaterialsFolder);
        var wood = SetupAvatar.Lit("Blackjack Wood", new Color(0.2f, 0.1f, 0.05f), 0.6f);
        var gold = SetupAvatar.Lit("Blackjack Gold", new Color(0.9f, 0.68f, 0.2f), 0.8f, 0.8f);
        var purple = SetupAvatar.Lit("Stall Purple", new Color(0.42f, 0.16f, 0.62f), 0.4f);
        var teal = SetupAvatar.Lit("Stall Teal", new Color(0.1f, 0.7f, 0.68f), 0.4f);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build " + RootName);
        SetupAvatar.PlaceAtSceneFocus(root.transform);
        var r = root.transform;

        // ----- Stall
        var stand = SetupAvatar.Group("Stand", r);
        SetupAvatar.Box("Counter", stand, wood, new Vector3(0f, Top * 0.5f, 0f), new Vector3(HalfWidth * 2f, Top, Depth), true);
        SetupAvatar.Box("Counter Top", stand, gold, new Vector3(0f, Top + 0.01f, 0f), new Vector3(HalfWidth * 2f + 0.08f, 0.02f, Depth + 0.08f), false);
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? 1f : -1f) * (HalfWidth + 0.02f);
            SetupAvatar.Box("Pole " + (i + 1), stand, gold, new Vector3(x, 1.3f, -Depth * 0.5f), new Vector3(0.05f, 2.6f, 0.05f), false);
            SetupAvatar.Box("Front Pole " + (i + 1), stand, gold, new Vector3(x, 1.2f, Depth * 0.5f), new Vector3(0.05f, 2.4f, 0.05f), false);
        }
        // Striped canopy, sloping down toward the customers.
        const int stripes = 8;
        float stripeWidth = (HalfWidth * 2f + 0.3f) / stripes;
        for (int i = 0; i < stripes; i++)
        {
            var stripe = SetupAvatar.Box("Canopy " + (i + 1), stand, i % 2 == 0 ? purple : teal,
                new Vector3(-HalfWidth - 0.15f + stripeWidth * (i + 0.5f), 2.5f, 0.05f), new Vector3(stripeWidth, 0.03f, Depth + 0.7f), false);
            stripe.localRotation = Quaternion.Euler(10f, 0f, 0f);
        }

        // A Quad shows on its -Z side, so text is turned to face the customers (+Z).
        var facing = Quaternion.LookRotation(Vector3.back, Vector3.up);
        var dark = new Color(0.06f, 0.03f, 0.1f);

        SetupAvatar.Box("Sign Board", stand, gold, new Vector3(0f, 2.18f, Depth * 0.5f + 0.25f), new Vector3(HalfWidth * 2f + 0.1f, 0.5f, 0.04f), false);
        var sign = SetupAvatar.TextQuad("Sign", r, "TOKEN STALL\n<size=42%>(buy powerups here)</size>", Color.white, dark, new Vector2Int(1536, 360));
        sign.transform.localPosition = new Vector3(0f, 2.18f, Depth * 0.5f + 0.275f);
        sign.transform.localRotation = facing;
        sign.transform.localScale = new Vector3(HalfWidth * 2f, 0.44f, 1f);

        // ----- Zorp
        var parts = BuildKeeper(r, out var keeperObject, out var bubble, out var antennae);

        var stall = root.AddComponent<TokenStall>();
        var so = new SerializedObject(stall);
        so.FindProperty("keeper").objectReferenceValue = keeperObject.GetComponent<XRSimpleInteractable>();
        so.FindProperty("head").objectReferenceValue = parts.Head;
        so.FindProperty("body").objectReferenceValue = parts.Body;
        SetupAvatar.SetArray(so, "shoulders", parts.Shoulders);
        SetupAvatar.SetArray(so, "hands", parts.Hands);
        SetupAvatar.SetArray(so, "upperArms", parts.UpperArms);
        SetupAvatar.SetArray(so, "forearms", parts.Forearms);
        SetupAvatar.SetArray(so, "antennae", antennae);
        so.FindProperty("speechBubble").objectReferenceValue = bubble;
        so.FindProperty("sign").objectReferenceValue = sign;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ----- Buttons, left to right as the customer sees them (+X to -X),
        // each with its card on the front of the counter below it.
        var buttons = SetupAvatar.Group("Buttons", r);
        Color[] colours =
        {
            new Color(0.2f, 0.4f, 0.9f), new Color(0.95f, 0.7f, 0.15f), new Color(0.15f, 0.7f, 0.25f), new Color(0.8f, 0.15f, 0.12f)
        };
        Powerup[] order = { Powerup.Stall, Powerup.LuckyCharm, Powerup.Bribe, Powerup.Vest };
        for (int i = 0; i < order.Length; i++)
        {
            float x = 0.72f - i * 0.48f;
            var station = SetupAvatar.Group($"{order[i]} Button", buttons);
            station.localPosition = new Vector3(x, Top + 0.02f, 0.1f);
            SetupAvatar.Part("Plinth", PrimitiveType.Cylinder, station, wood, new Vector3(0f, 0.006f, 0f), new Vector3(0.12f, 0.006f, 0.12f));

            var button = new GameObject("Button");
            button.transform.SetParent(station, false);
            var box = button.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.03f, 0f);
            box.size = new Vector3(0.12f, 0.06f, 0.12f);
            var cap = SetupAvatar.Part("Cap", PrimitiveType.Cylinder, button.transform,
                SetupAvatar.Lit($"Stall Button {order[i]}", colours[i], 0.7f), new Vector3(0f, 0.024f, 0f), new Vector3(0.1f, 0.012f, 0.1f));

            var card = SetupAvatar.TextQuad("Card", station, CasinoRun.Title(order[i]), Color.white, dark, new Vector2Int(640, 800), 90f);
            card.transform.localPosition = new Vector3(0f, -0.5f, Depth * 0.5f - 0.1f + 0.006f);
            card.transform.localRotation = facing;
            card.transform.localScale = new Vector3(0.44f, 0.55f, 1f);

            button.AddComponent<XRSimpleInteractable>();
            var bso = new SerializedObject(button.AddComponent<TokenStallButton>());
            bso.FindProperty("stall").objectReferenceValue = stall;
            bso.FindProperty("powerup").enumValueIndex = (int)order[i];
            bso.FindProperty("cap").objectReferenceValue = cap;
            bso.FindProperty("card").objectReferenceValue = card;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupTokenStall: built '{RootName}' at {root.transform.position}. Move/rotate the root so it stands outside the casino door "
            + "(customers on its +Z side). Save the scene to keep it.");
    }

    // The alien: the usual character body in a purple waistcoat, with green
    // skin, two wobbly antennae with glowing tips and a third eye.
    static SetupAvatar.Parts BuildKeeper(Transform root, out GameObject keeper, out WorldTextPanel bubble, out Transform[] antennae)
    {
        var skin = SetupAvatar.Lit("Alien Skin", new Color(0.45f, 0.86f, 0.42f), 0.55f);
        var glow = SetupAvatar.Lit("Alien Glow", new Color(1f, 0.35f, 0.8f), 0.8f);
        glow.EnableKeyword("_EMISSION");
        glow.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.8f) * 2.5f);
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(glow);
        var white = SetupAvatar.Lit("Alien Eye", Color.white, 0.8f);
        var black = SetupAvatar.Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f);

        var look = new SetupAvatar.Look
        {
            Jacket = SetupAvatar.Lit("Stall Purple", new Color(0.42f, 0.16f, 0.62f), 0.4f),
            Shirt = SetupAvatar.Lit("Alien Shirt", new Color(0.98f, 0.85f, 0.25f), 0.3f),
            Trousers = SetupAvatar.Lit("Stall Teal", new Color(0.1f, 0.7f, 0.68f), 0.4f),
            Skin = skin,
            Hair = skin, // bald: the "hair" blends into the head
            Accent = glow,
            Dark = black,
            Mouth = SetupAvatar.Lit("Alien Mouth", new Color(0.15f, 0.4f, 0.2f), 0.5f),
            Vest = true, BowTie = true,
            // Resting on the counter rather than clasped at the waist.
            HandRest = new Vector3(0.3f, Top + 0.07f, 0.42f),
        };

        keeper = new GameObject("Zorp");
        var t = keeper.transform;
        t.SetParent(root, false);
        t.localPosition = new Vector3(0f, 0f, -(Depth * 0.5f + 0.35f));
        var parts = SetupAvatar.BuildCharacter(t, look, hands: true, separateLegs: false);

        // Head extras (the head pivots at the neck; its middle is ~0.15 up).
        antennae = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            var antenna = SetupAvatar.Group(i == 0 ? "Antenna R" : "Antenna L", parts.Head);
            antenna.localPosition = new Vector3(side * 0.055f, 0.25f, 0f);
            antenna.localRotation = Quaternion.Euler(0f, 0f, -side * 18f);
            SetupAvatar.Part("Stalk", PrimitiveType.Cylinder, antenna, skin, new Vector3(0f, 0.08f, 0f), new Vector3(0.014f, 0.08f, 0.014f));
            SetupAvatar.Part("Tip", PrimitiveType.Sphere, antenna, glow, new Vector3(0f, 0.17f, 0f), Vector3.one * 0.05f);
            antennae[i] = antenna;
        }
        SetupAvatar.Part("Third Eye", PrimitiveType.Sphere, parts.Head, white, new Vector3(0f, 0.235f, 0.085f), Vector3.one * 0.055f);
        SetupAvatar.Part("Third Pupil", PrimitiveType.Sphere, parts.Head, black, new Vector3(0f, 0.238f, 0.108f), Vector3.one * 0.024f);

        bubble = SetupAvatar.TextQuad("Speech Bubble", t, "", new Color(0.1f, 0.08f, 0.06f), new Color(0.9f, 1f, 0.9f),
            new Vector2Int(768, 256), 90f);
        bubble.transform.localPosition = new Vector3(0f, 2.05f, 0.1f);
        bubble.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        bubble.transform.localScale = new Vector3(0.78f, 0.26f, 1f);

        var capsule = keeper.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1.3f, 0f);
        capsule.radius = 0.24f;
        capsule.height = 1f;
        keeper.AddComponent<XRSimpleInteractable>();

        for (int i = 0; i < 2; i++)
            CharacterLimbs.PoseArm(parts.UpperArms[i], parts.Forearms[i], parts.Shoulders[i].position, parts.Hands[i].position,
                t.rotation, i == 0 ? 1f : -1f, SetupAvatar.ArmThickness);
        return parts;
    }
}
