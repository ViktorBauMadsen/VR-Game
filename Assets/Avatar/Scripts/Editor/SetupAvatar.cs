using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;

// Builds the player-facing character pieces into the open scene. Each one
// is only built if it doesn't exist yet, so delete any of them and re-run
// Tools > Avatar > Build to get it back:
//
//   "Player Hands"  - the rigged LeftHand/RightHand models, oriented from
//                     their own skeleton, driven by HandPoser.
//   "Player Avatar" - your character (fedora, navy jacket, gold tie), only
//                     visible in mirrors - see PlayerAvatarBody.
//   "Mirror"        - a standing stereo mirror, dropped at the Scene view's
//                     focus point facing the Scene camera. Move it freely.
//   "Croupier"      - the roulette dealer, placed beside the roulette wheel.
//
// It also runs once, automatically, the first time these scripts compile
// with the casino scene open.
public static class SetupAvatar
{
    const string Folder = "Assets/Avatar";
    const string MaterialsFolder = Folder + "/Materials";
    const string CasinoScene = "SampleScene";
    const float ArmThickness = 0.085f;

    static string AutoBuildKey => "SetupAvatar.AutoBuilt." + Application.dataPath;

    [InitializeOnLoadMethod]
    static void AutoBuildOnce()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorPrefs.GetBool(AutoBuildKey, false)) return;
            if (EditorSceneManager.GetActiveScene().name != CasinoScene) return;
            // Wait until the hand models have been imported.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Hands/RightHand.fbx") == null) return;
            EditorPrefs.SetBool(AutoBuildKey, true);
            Build();
        };
    }

    [MenuItem("Tools/Avatar/Build Hands, Avatar, Mirror and Croupier")]
    static void Build()
    {
        EnsureFolder(Folder);
        EnsureFolder(MaterialsFolder);
        int bodyLayer = EnsureLayer(PlayerAvatarBody.LayerName);

        string built = "";
        if (GameObject.Find("Player Hands") == null && BuildHands()) built += "Player Hands, ";
        if (GameObject.Find("Player Avatar") == null) { BuildPlayerAvatar(bodyLayer); built += "Player Avatar, "; }
        if (GameObject.Find("Mirror") == null) { BuildMirror(); built += "Mirror, "; }
        if (GameObject.Find("Croupier") == null) { BuildCroupier(); built += "Croupier, "; }

        AssetDatabase.SaveAssets();
        if (built.Length == 0)
        {
            Debug.Log("SetupAvatar: everything already exists. Delete an object and re-run to rebuild it.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupAvatar: built {built.TrimEnd(',', ' ')}. Save the scene to keep them.");
    }

    // -------------------------------------------------------------- hands

    static bool BuildHands()
    {
        var skin = Lit("Player Skin", new Color(0.9f, 0.68f, 0.55f), 0.35f);
        var root = new GameObject("Player Hands");
        Undo.RegisterCreatedObjectUndo(root, "Build Player Hands");

        bool ok = true;
        ok &= BuildHand(root.transform, "Left", HandPoser.Side.Left, skin);
        ok &= BuildHand(root.transform, "Right", HandPoser.Side.Right, skin);
        if (!ok)
        {
            Object.DestroyImmediate(root);
            Debug.LogError($"SetupAvatar: couldn't set up the hand models in {Folder}/Hands.");
        }
        return ok;
    }

    // The model is turned so that - whatever axes it was exported with - the
    // fingers point along the controller's forward and the palm faces
    // inward (toward the other hand), with the palm centred on the
    // controller. All of that is measured from the skeleton.
    static bool BuildHand(Transform parent, string label, HandPoser.Side side, Material skin)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/Hands/{label}Hand.fbx");
        if (asset == null) return false;

        var holder = new GameObject($"{label} Hand");
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = new Vector3(side == HandPoser.Side.Right ? 0.2f : -0.2f, 1.1f, 0f); // parked until Play

        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, holder.transform);
        var t = model.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;

        var wrist = HandPoser.FindBone(t, "_hand");
        var index1 = HandPoser.FindBone(t, "index1");
        var middle1 = HandPoser.FindBone(t, "middle1");
        var middle3 = HandPoser.FindBone(t, "middle3");
        var pinky1 = HandPoser.FindBone(t, "pinky1");
        if (wrist == null || index1 == null || middle1 == null || middle3 == null || pinky1 == null) return false;

        // Wrist to last middle knuckle is about 14 cm on an adult hand.
        float length = Vector3.Distance(wrist.position, middle3.position);
        if (length < 0.07f || length > 0.28f) t.localScale *= 0.14f / Mathf.Max(length, 1e-5f);

        Vector3 Local(Transform bone) => holder.transform.InverseTransformPoint(bone.position);
        Vector3 fingers = (Local(middle1) - Local(wrist)).normalized;
        Vector3 n = Vector3.Cross(Local(index1) - Local(wrist), Local(pinky1) - Local(wrist)).normalized;
        Vector3 palmOut = side == HandPoser.Side.Right ? -n : n;
        Vector3 palmTarget = side == HandPoser.Side.Right ? Vector3.left : Vector3.right;

        t.localRotation = Quaternion.LookRotation(Vector3.forward, palmTarget) * Quaternion.Inverse(Quaternion.LookRotation(fingers, palmOut));
        Vector3 palmCentre = (Local(wrist) + Local(middle1)) * 0.5f;
        t.localPosition -= palmCentre + new Vector3(0f, 0f, 0.03f);

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = skin;
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;

        var so = new SerializedObject(holder.AddComponent<HandPoser>());
        so.FindProperty("side").enumValueIndex = (int)side;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ------------------------------------------------------------- player

    static void BuildPlayerAvatar(int layer)
    {
        var look = new Look
        {
            Jacket = Lit("Player Jacket", new Color(0.08f, 0.13f, 0.32f), 0.45f),
            Shirt = Lit("Character Shirt", new Color(0.95f, 0.95f, 0.93f), 0.3f),
            Trousers = Lit("Player Trousers", new Color(0.06f, 0.07f, 0.12f), 0.4f),
            Skin = Lit("Player Skin", new Color(0.9f, 0.68f, 0.55f), 0.35f),
            Hair = Lit("Player Hair", new Color(0.36f, 0.2f, 0.08f), 0.5f),
            Accent = Lit("Player Gold", new Color(0.9f, 0.68f, 0.2f), 0.75f, 0.6f),
            Dark = Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f),
            Mouth = Lit("Character Mouth", new Color(0.55f, 0.2f, 0.2f), 0.3f),
            NeckTie = true, Fedora = true, Sideburns = true, PocketSquare = true,
        };

        var root = new GameObject("Player Avatar");
        Undo.RegisterCreatedObjectUndo(root, "Build Player Avatar");
        var parts = BuildCharacter(root.transform, look, hands: false, separateLegs: true);

        // Parked next to the origin facing +Z until Play, when it follows the headset.
        var so = new SerializedObject(root.AddComponent<PlayerAvatarBody>());
        so.FindProperty("head").objectReferenceValue = parts.Head;
        so.FindProperty("body").objectReferenceValue = parts.Body;
        SetArray(so, "shoulders", parts.Shoulders);
        SetArray(so, "upperArms", parts.UpperArms);
        SetArray(so, "forearms", parts.Forearms);
        SetArray(so, "hips", parts.Hips);
        SetArray(so, "legs", parts.Legs);
        SetArray(so, "feet", parts.Feet);
        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------- mirror

    static void BuildMirror()
    {
        var frameMat = Lit("Mirror Frame", new Color(0.85f, 0.65f, 0.25f), 0.85f, 1f);
        var backMat = Lit("Mirror Back", new Color(0.16f, 0.08f, 0.04f), 0.5f);

        var shader = Shader.Find("Casino/Stereo Mirror");
        string glassPath = MaterialsFolder + "/Mirror Glass.mat";
        var glass = AssetDatabase.LoadAssetAtPath<Material>(glassPath);
        if (glass == null)
        {
            // Falls back to a plain unlit material (one eye's image for both) if the shader is missing.
            glass = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Mirror Glass" };
            AssetDatabase.CreateAsset(glass, glassPath);
        }

        const float width = 1.2f, height = 2f;
        var root = new GameObject("Mirror");
        Undo.RegisterCreatedObjectUndo(root, "Build Mirror");
        PlaceAtSceneFocus(root.transform);

        // Root faces +Z; the quad is turned round so its visible -Z face does too.
        Box("Back", root.transform, backMat, new Vector3(0f, height / 2f + 0.1f, -0.03f), new Vector3(width + 0.12f, height + 0.12f, 0.05f), true);
        Box("Frame Top", root.transform, frameMat, new Vector3(0f, height + 0.13f, 0f), new Vector3(width + 0.16f, 0.06f, 0.08f), false);
        Box("Frame Bottom", root.transform, frameMat, new Vector3(0f, 0.07f, 0f), new Vector3(width + 0.16f, 0.06f, 0.08f), false);
        Box("Frame Left", root.transform, frameMat, new Vector3(-width / 2f - 0.05f, height / 2f + 0.1f, 0f), new Vector3(0.06f, height + 0.12f, 0.08f), false);
        Box("Frame Right", root.transform, frameMat, new Vector3(width / 2f + 0.05f, height / 2f + 0.1f, 0f), new Vector3(0.06f, height + 0.12f, 0.08f), false);
        Box("Foot Left", root.transform, backMat, new Vector3(-width / 2f, 0.02f, 0f), new Vector3(0.08f, 0.04f, 0.6f), false);
        Box("Foot Right", root.transform, backMat, new Vector3(width / 2f, 0.02f, 0f), new Vector3(0.08f, 0.04f, 0.6f), false);

        var quad = Prim("Glass", PrimitiveType.Quad, root.transform, glass);
        quad.localPosition = new Vector3(0f, height / 2f + 0.1f, 0.001f);
        quad.localRotation = Quaternion.Euler(0f, 180f, 0f);
        quad.localScale = new Vector3(width, height, 1f);
        var renderer = quad.GetComponent<Renderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        quad.gameObject.AddComponent<Mirror>();
        Selection.activeGameObject = root;
    }

    // ----------------------------------------------------------- croupier

    static void BuildCroupier()
    {
        var look = new Look
        {
            Jacket = Lit("Croupier Vest", new Color(0.05f, 0.25f, 0.13f), 0.45f),
            Shirt = Lit("Character Shirt", new Color(0.95f, 0.95f, 0.93f), 0.3f),
            Trousers = Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f),
            Skin = Lit("Croupier Skin", new Color(0.72f, 0.5f, 0.36f), 0.35f),
            Hair = Lit("Croupier Hair", new Color(0.55f, 0.55f, 0.56f), 0.45f),
            Accent = Lit("Croupier Gold", new Color(0.9f, 0.68f, 0.2f), 0.75f, 0.6f),
            Dark = Lit("Character Black", new Color(0.04f, 0.04f, 0.05f), 0.4f),
            Mouth = Lit("Character Mouth", new Color(0.55f, 0.2f, 0.2f), 0.3f),
            Visor = Lit("Croupier Visor", new Color(0.1f, 0.5f, 0.25f), 0.8f),
            Vest = true, BowTie = true, Goatee = true, DealerVisor = true,
        };

        var root = new GameObject("Croupier");
        Undo.RegisterCreatedObjectUndo(root, "Build Croupier");
        var parts = BuildCharacter(root.transform, look, hands: true, separateLegs: false);

        var textMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Text Screen.mat");
        if (textMat == null)
        {
            textMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Text Screen" };
            textMat.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(textMat, MaterialsFolder + "/Text Screen.mat");
        }
        var bubble = Prim("Speech Bubble", PrimitiveType.Quad, root.transform, textMat);
        bubble.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        bubble.localPosition = new Vector3(0f, 2.05f, 0.1f);
        bubble.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        bubble.localScale = new Vector3(0.78f, 0.26f, 1f);
        var panel = bubble.gameObject.AddComponent<WorldTextPanel>();
        var panelSo = new SerializedObject(panel);
        panelSo.FindProperty("textColor").colorValue = new Color(0.1f, 0.08f, 0.06f);
        panelSo.FindProperty("background").colorValue = new Color(1f, 0.97f, 0.9f);
        panelSo.FindProperty("textureSize").vector2IntValue = new Vector2Int(768, 256);
        panelSo.FindProperty("maxFontSize").floatValue = 90f;
        panelSo.ApplyModifiedPropertiesWithoutUndo();

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1.25f, 0f);
        capsule.radius = 0.26f;
        capsule.height = 1.1f;
        root.AddComponent<XRSimpleInteractable>();

        var wheel = Object.FindAnyObjectByType<RouletteWheel>();
        var so = new SerializedObject(root.AddComponent<Croupier>());
        so.FindProperty("wheel").objectReferenceValue = wheel;
        so.FindProperty("head").objectReferenceValue = parts.Head;
        so.FindProperty("body").objectReferenceValue = parts.Body;
        SetArray(so, "shoulders", parts.Shoulders);
        SetArray(so, "hands", parts.Hands);
        SetArray(so, "upperArms", parts.UpperArms);
        SetArray(so, "forearms", parts.Forearms);
        so.FindProperty("speechBubble").objectReferenceValue = panel;
        so.ApplyModifiedPropertiesWithoutUndo();

        PlaceCroupier(root.transform, wheel);
        for (int i = 0; i < 2; i++)
            CharacterLimbs.PoseArm(parts.UpperArms[i], parts.Forearms[i], parts.Shoulders[i].position, parts.Hands[i].position,
                root.transform.rotation, i == 0 ? 1f : -1f, ArmThickness);
    }

    // Beside the roulette layout, on whichever side is further from where
    // the player starts, facing the table. Stepped outward until he isn't
    // standing inside anything.
    static void PlaceCroupier(Transform croupier, RouletteWheel wheel)
    {
        if (wheel == null)
        {
            PlaceAtSceneFocus(croupier);
            Debug.LogWarning("SetupAvatar: no RouletteWheel in the scene - put the Croupier where you want him.");
            return;
        }

        var bounds = new Bounds(wheel.transform.position, Vector3.zero);
        foreach (var r in wheel.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        Vector3 along = Vector3.right;
        var betTable = GameObject.Find("RouletteBetTable");
        if (betTable != null)
        {
            foreach (var r in betTable.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            Vector3 d = betTable.transform.position - wheel.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) along = d.normalized;
        }

        Vector3 centre = bounds.center;
        Vector3 perp = Vector3.Cross(Vector3.up, along).normalized;
        var rig = Object.FindAnyObjectByType<XROrigin>();
        if (rig != null && Vector3.Dot(rig.transform.position - centre, perp) > 0f) perp = -perp;

        float reach = Mathf.Abs(perp.x) * bounds.extents.x + Mathf.Abs(perp.z) * bounds.extents.z;
        Vector3 spot = centre + perp * (reach + 0.45f);
        float floorY = rig != null ? rig.transform.position.y : bounds.min.y;
        for (int i = 0; i < 10; i++)
        {
            Vector3 feet = new Vector3(spot.x, floorY, spot.z);
            if (!Physics.CheckCapsule(feet + Vector3.up * 0.5f, feet + Vector3.up * 1.5f, 0.25f, ~0, QueryTriggerInteraction.Ignore)) break;
            spot += perp * 0.15f;
        }
        if (Physics.Raycast(new Vector3(spot.x, floorY + 1.5f, spot.z), Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore))
            floorY = hit.point.y;

        croupier.position = new Vector3(spot.x, floorY, spot.z);
        Vector3 face = centre - croupier.position;
        face.y = 0f;
        if (face.sqrMagnitude > 0.001f) croupier.rotation = Quaternion.LookRotation(face);
    }

    // ---------------------------------------------------- character build

    class Look
    {
        public Material Jacket, Shirt, Trousers, Skin, Hair, Accent, Dark, Mouth, Visor;
        public bool Vest;         // sleeveless: shirt-coloured arms. Otherwise a jacket with matching sleeves.
        public bool BowTie, NeckTie, Fedora, DealerVisor, Goatee, Sideburns, PocketSquare;
    }

    class Parts
    {
        public Transform Head, Body;
        public Transform[] Shoulders = new Transform[2], Hands = new Transform[2];
        public Transform[] UpperArms = new Transform[2], Forearms = new Transform[2];
        public Transform[] Hips = new Transform[2], Legs = new Transform[2], Feet = new Transform[2];
    }

    // Same smooth, all-spheres-and-capsules style as the bartender, facing
    // +Z, origin on the floor. Index 0 is the right side, 1 the left.
    static Parts BuildCharacter(Transform root, Look look, bool hands, bool separateLegs)
    {
        var parts = new Parts();

        var body = Group("Body", root);
        parts.Body = body;
        Part("Shirt", PrimitiveType.Capsule, body, look.Shirt, new Vector3(0f, 1.15f, 0f), new Vector3(0.42f, 0.33f, 0.28f));
        Part(look.Vest ? "Vest" : "Jacket", PrimitiveType.Capsule, body, look.Jacket,
            new Vector3(0f, look.Vest ? 1.11f : 1.12f, 0f), new Vector3(0.445f, look.Vest ? 0.27f : 0.31f, 0.305f));
        Part("Shirt Front", PrimitiveType.Sphere, body, look.Shirt, new Vector3(0f, 1.27f, 0.105f), new Vector3(0.13f, 0.27f, 0.115f));
        for (int i = 0; i < 3; i++)
            Part($"Button {i + 1}", PrimitiveType.Sphere, body, look.Accent, new Vector3(0f, 1.1f - i * 0.07f, 0.153f), Vector3.one * 0.022f);
        Part("Neck", PrimitiveType.Cylinder, body, look.Skin, new Vector3(0f, 1.46f, 0f), new Vector3(0.1f, 0.04f, 0.1f));
        Part("Collar L", PrimitiveType.Sphere, body, look.Shirt, new Vector3(-0.045f, 1.445f, 0.05f), new Vector3(0.09f, 0.05f, 0.09f));
        Part("Collar R", PrimitiveType.Sphere, body, look.Shirt, new Vector3(0.045f, 1.445f, 0.05f), new Vector3(0.09f, 0.05f, 0.09f));

        if (look.BowTie)
        {
            var bow = Group("Bow Tie", body);
            bow.localPosition = new Vector3(0f, 1.415f, 0.13f);
            Part("Left", PrimitiveType.Sphere, bow, look.Dark, new Vector3(-0.04f, 0f, 0f), new Vector3(0.075f, 0.05f, 0.03f));
            Part("Right", PrimitiveType.Sphere, bow, look.Dark, new Vector3(0.04f, 0f, 0f), new Vector3(0.075f, 0.05f, 0.03f));
            Part("Knot", PrimitiveType.Sphere, bow, look.Dark, new Vector3(0f, 0f, 0.008f), Vector3.one * 0.032f);
        }
        if (look.NeckTie)
        {
            Part("Tie Knot", PrimitiveType.Sphere, body, look.Accent, new Vector3(0f, 1.41f, 0.135f), new Vector3(0.045f, 0.04f, 0.035f));
            Part("Tie", PrimitiveType.Sphere, body, look.Accent, new Vector3(0f, 1.27f, 0.15f), new Vector3(0.055f, 0.27f, 0.04f));
        }
        if (look.PocketSquare)
            Part("Pocket Square", PrimitiveType.Sphere, body, look.Accent, new Vector3(-0.12f, 1.3f, 0.125f), new Vector3(0.06f, 0.035f, 0.03f));

        // Legs: one static block, or hips + stretchable legs + shoes for the player.
        if (separateLegs)
        {
            Part("Hips", PrimitiveType.Sphere, body, look.Trousers, new Vector3(0f, 0.86f, 0f), new Vector3(0.4f, 0.24f, 0.28f));
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                string n = i == 0 ? "Right" : "Left";
                parts.Hips[i] = Group($"{n} Hip", body);
                parts.Hips[i].localPosition = new Vector3(side * 0.1f, 0.84f, 0f);
                parts.Legs[i] = Part($"{n} Leg", PrimitiveType.Capsule, root, look.Trousers, new Vector3(side * 0.1f, 0.45f, 0f), new Vector3(0.14f, 0.43f, 0.14f));
                parts.Feet[i] = Part($"{n} Shoe", PrimitiveType.Sphere, root, look.Dark, new Vector3(side * 0.1f, 0.04f, 0.05f), new Vector3(0.12f, 0.08f, 0.24f));
            }
        }
        else
        {
            Part("Legs", PrimitiveType.Capsule, body, look.Trousers, new Vector3(0f, 0.5f, 0f), new Vector3(0.36f, 0.5f, 0.27f));
        }

        // Head, pivoting at the neck.
        var head = Group("Head", root);
        parts.Head = head;
        head.localPosition = new Vector3(0f, 1.47f, 0f);
        Part("Skull", PrimitiveType.Sphere, head, look.Skin, new Vector3(0f, 0.14f, 0f), new Vector3(0.26f, 0.275f, 0.26f));
        Part("Hair", PrimitiveType.Sphere, head, look.Hair, new Vector3(0f, 0.2f, -0.022f), new Vector3(0.272f, 0.2f, 0.265f));
        Part("Ear L", PrimitiveType.Sphere, head, look.Skin, new Vector3(-0.128f, 0.135f, 0f), new Vector3(0.035f, 0.055f, 0.045f));
        Part("Ear R", PrimitiveType.Sphere, head, look.Skin, new Vector3(0.128f, 0.135f, 0f), new Vector3(0.035f, 0.055f, 0.045f));
        Part("Eye L", PrimitiveType.Sphere, head, look.Dark, new Vector3(-0.047f, 0.16f, 0.116f), new Vector3(0.03f, 0.04f, 0.02f));
        Part("Eye R", PrimitiveType.Sphere, head, look.Dark, new Vector3(0.047f, 0.16f, 0.116f), new Vector3(0.03f, 0.04f, 0.02f));
        Part("Brow L", PrimitiveType.Capsule, head, look.Hair, new Vector3(-0.047f, 0.198f, 0.113f), new Vector3(0.016f, 0.026f, 0.016f))
            .localRotation = Quaternion.Euler(0f, 0f, 82f);
        Part("Brow R", PrimitiveType.Capsule, head, look.Hair, new Vector3(0.047f, 0.198f, 0.113f), new Vector3(0.016f, 0.026f, 0.016f))
            .localRotation = Quaternion.Euler(0f, 0f, -82f);
        Part("Nose", PrimitiveType.Sphere, head, look.Skin, new Vector3(0f, 0.125f, 0.132f), new Vector3(0.04f, 0.045f, 0.045f));
        Part("Mouth", PrimitiveType.Capsule, head, look.Mouth, new Vector3(0f, 0.078f, 0.119f), new Vector3(0.014f, 0.032f, 0.014f))
            .localRotation = Quaternion.Euler(0f, 0f, 90f);

        if (look.Goatee)
            Part("Goatee", PrimitiveType.Sphere, head, look.Hair, new Vector3(0f, 0.03f, 0.1f), new Vector3(0.06f, 0.06f, 0.05f));
        if (look.Sideburns)
        {
            Part("Sideburn L", PrimitiveType.Sphere, head, look.Hair, new Vector3(-0.118f, 0.12f, 0.04f), new Vector3(0.03f, 0.09f, 0.045f));
            Part("Sideburn R", PrimitiveType.Sphere, head, look.Hair, new Vector3(0.118f, 0.12f, 0.04f), new Vector3(0.03f, 0.09f, 0.045f));
        }
        if (look.Fedora)
        {
            var hat = Group("Fedora", head);
            hat.localPosition = new Vector3(0f, 0.245f, -0.01f);
            hat.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            Part("Brim", PrimitiveType.Sphere, hat, look.Dark, Vector3.zero, new Vector3(0.38f, 0.022f, 0.4f));
            Part("Crown", PrimitiveType.Capsule, hat, look.Dark, new Vector3(0f, 0.05f, 0f), new Vector3(0.255f, 0.105f, 0.265f));
            Part("Band", PrimitiveType.Cylinder, hat, look.Accent, new Vector3(0f, 0.025f, 0f), new Vector3(0.262f, 0.014f, 0.272f));
        }
        if (look.DealerVisor)
        {
            var visor = Group("Dealer Visor", head);
            visor.localPosition = new Vector3(0f, 0.215f, 0f);
            Part("Band", PrimitiveType.Cylinder, visor, look.Visor, Vector3.zero, new Vector3(0.275f, 0.014f, 0.272f));
            Part("Brim", PrimitiveType.Sphere, visor, look.Visor, new Vector3(0f, -0.008f, 0.14f), new Vector3(0.24f, 0.014f, 0.2f))
                .localRotation = Quaternion.Euler(12f, 0f, 0f);
        }

        // Arms: fixed shoulders on the body, capsules stretched to the hands.
        var sleeve = look.Vest ? look.Shirt : look.Jacket;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            string n = i == 0 ? "Right" : "Left";
            parts.Shoulders[i] = Group($"{n} Shoulder", body);
            parts.Shoulders[i].localPosition = new Vector3(side * 0.23f, 1.37f, 0f);
            Part("Joint", PrimitiveType.Sphere, parts.Shoulders[i], sleeve, Vector3.zero, Vector3.one * 0.11f);
            parts.UpperArms[i] = Part($"{n} Upper Arm", PrimitiveType.Capsule, root, sleeve, new Vector3(side * 0.27f, 1.2f, 0f), new Vector3(ArmThickness, 0.16f, ArmThickness));
            parts.Forearms[i] = Part($"{n} Forearm", PrimitiveType.Capsule, root, sleeve, new Vector3(side * 0.27f, 0.95f, 0f), new Vector3(ArmThickness, 0.16f, ArmThickness));
            if (!hands) continue;

            // Clasped in front of the waist.
            parts.Hands[i] = Group($"{n} Hand", root);
            parts.Hands[i].localPosition = new Vector3(side * 0.06f, 0.98f, 0.2f);
            Part("Palm", PrimitiveType.Sphere, parts.Hands[i], look.Skin, Vector3.zero, new Vector3(0.1f, 0.085f, 0.1f));
            Part("Cuff", PrimitiveType.Sphere, parts.Hands[i], look.Shirt, new Vector3(side * 0.03f, 0.03f, -0.035f), new Vector3(0.09f, 0.075f, 0.07f));
        }
        return parts;
    }

    // ------------------------------------------------------------ helpers

    static void PlaceAtSceneFocus(Transform root)
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

    static void SetArray(SerializedObject so, string property, Transform[] values)
    {
        var prop = so.FindProperty(property);
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform Prim(string name, PrimitiveType type, Transform parent, Material mat, bool collider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }

    static Transform Part(string name, PrimitiveType type, Transform parent, Material mat, Vector3 pos, Vector3 scale)
    {
        var t = Prim(name, type, parent, mat);
        t.localPosition = pos;
        t.localScale = scale;
        return t;
    }

    static Transform Box(string name, Transform parent, Material mat, Vector3 pos, Vector3 size, bool collider)
    {
        var t = Prim(name, PrimitiveType.Cube, parent, mat, collider);
        t.localPosition = pos;
        t.localScale = size;
        return t;
    }

    static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    // Adds a user layer (first free slot) if the project doesn't have it yet.
    static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0) return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 6; i < layers.arraySize; i++)
        {
            var slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue)) continue;
            slot.stringValue = layerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return i;
        }
        Debug.LogError($"SetupAvatar: no free layer slot for '{layerName}'.");
        return 0;
    }
}
