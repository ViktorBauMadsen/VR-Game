using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Tools > Casino > Build Patrons and Bond
//
// Sets up the casino guests (CasinoPatrons) in the open scene:
//   - Assets/Patrons/Patron.controller: one int parameter "Pose" choosing
//     idle / walk / slot-sitting / sit-and-talk / slot win / slot lose, with
//     IK on so heads can turn to the player. Override controllers swap in the
//     senior and plus-size walks.
//   - A senior gentleman assembled from the ithappy senior parts.
//   - James Bond: the purple-suited ithappy gentleman without his top hat,
//     the purple of the colour atlas turned into black dinner-jacket cloth.
//   - Materials for the stools and the martini glass.
//   - A NavMesh of the hall floor (agents 0.22 m wide, so they fit between
//     the tables), saved as an asset and added at runtime.
// Re-running replaces the "Casino Patrons" object and rebuilds the assets.
public static class SetupPatrons
{
    const string Folder = "Assets/Patrons";
    const string Ithappy = "Assets/ithappy/Casino_Free";
    const string RootName = "Casino Patrons";
    // Hall interior (see DecorateInterior): x -9.97..4.18, z -0.35..28.09, floor 0.42.
    static readonly Bounds Hall = new Bounds(new Vector3(-2.9f, 1.3f, 13.87f), new Vector3(14.1f, 2.2f, 28.4f));

    [MenuItem("Tools/Casino/Build Patrons and Bond")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Patrons");

        var controller = BuildController(out var seniorCtrl, out var plusCtrl);
        var senior = BuildSenior();
        var bond = BuildBond();
        var nav = BakeNavMesh();

        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Patrons");
        var patrons = root.AddComponent<CasinoPatrons>();

        var prefabs = new List<GameObject>();
        var ctrls = new List<RuntimeAnimatorController>();
        void Add(GameObject prefab, RuntimeAnimatorController ctrl)
        {
            if (prefab == null) return;
            prefabs.Add(prefab);
            ctrls.Add(ctrl);
        }
        Add(Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/3_Actor.prefab"), null);
        Add(Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/209.prefab"), plusCtrl);
        Add(senior, seniorCtrl);
        Add(Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/200.prefab"), null);

        var so = new SerializedObject(patrons);
        so.FindProperty("navMesh").objectReferenceValue = nav;
        so.FindProperty("controller").objectReferenceValue = controller;
        SetArray(so.FindProperty("patronPrefabs"), prefabs);
        SetArray(so.FindProperty("patronControllers"), ctrls);
        so.FindProperty("gamblerPrefab").objectReferenceValue = Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/209.prefab");
        so.FindProperty("bondPrefab").objectReferenceValue = bond;
        so.FindProperty("stoolSeatMaterial").objectReferenceValue = Mat("Stool Leather", new Color(0.32f, 0.06f, 0.06f), 0.45f, 0f);
        so.FindProperty("stoolMetalMaterial").objectReferenceValue = Mat("Stool Brass", new Color(0.85f, 0.65f, 0.3f), 0.8f, 0.9f);
        so.FindProperty("glassMaterial").objectReferenceValue = GlassMat();
        so.FindProperty("oliveMaterial").objectReferenceValue = Mat("Olive", new Color(0.35f, 0.5f, 0.12f), 0.7f, 0f);
        SetStaffLooks(so);
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"SetupPatrons: built '{RootName}' ({prefabs.Count} looks, NavMesh {(nav != null ? "baked" : "MISSING")}, senior {(senior != null ? "ok" : "skipped")}, Bond {(bond != null ? "ok" : "MISSING")}). Save the scene to keep it.");
    }

    // The staff's characters: the vest-and-bow-tie outfit for the blackjack
    // dealers, the black shirt and tie (sunglasses hidden) for the croupier
    // and bartender.
    static void SetStaffLooks(SerializedObject so)
    {
        so.FindProperty("dealerLook").objectReferenceValue = Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/31_Cashier.prefab");
        so.FindProperty("staffLook").objectReferenceValue = Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/77_Security.prefab");
        so.FindProperty("bartenderLook").objectReferenceValue = BuildBartender();
    }

    // Gives the existing patrons root the staff characters, without rebuilding anything else.
    [MenuItem("Tools/Casino/Upgrade Staff Characters")]
    public static void UpgradeStaff()
    {
        var patrons = Object.FindAnyObjectByType<CasinoPatrons>();
        if (patrons == null)
        {
            Debug.LogWarning("SetupPatrons: no patrons in the scene - run Tools > Casino > Build Patrons and Bond first.");
            return;
        }
        var so = new SerializedObject(patrons);
        SetStaffLooks(so);
        so.ApplyModifiedProperties();
        FixStandingFootIK(so.FindProperty("controller").objectReferenceValue as AnimatorController);
        EditorSceneManager.MarkSceneDirty(patrons.gameObject.scene);
        EditorSceneManager.SaveScene(patrons.gameObject.scene);
        Debug.Log("SetupPatrons: staff characters set (dealers: 31_Cashier, croupier: 77_Security, bartender: Bartender.prefab). Scene saved - press Play to see them.");
    }

    // Foot IK on the standing idles pins the feet where the clip recorded them,
    // which on these characters bends the knees into a half squat. The idles
    // don't step, so they stand straight without it.
    static bool StandingIdle(string state) => state == "Idle" || state == "Idle Plain";

    static void FixStandingFootIK(AnimatorController controller)
    {
        if (controller == null) return;
        foreach (var layer in controller.layers)
            foreach (var child in layer.stateMachine.states)
                if (StandingIdle(child.state.name) && child.state.iKOnFeet)
                {
                    child.state.iKOnFeet = false;
                    EditorUtility.SetDirty(child.state);
                }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static void SetArray<T>(SerializedProperty prop, List<T> items) where T : Object
    {
        prop.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) Debug.LogWarning("SetupPatrons: missing " + path);
        return asset;
    }

    // ------------------------------------------------------------ animation

    static AnimatorController BuildController(out AnimatorOverrideController senior, out AnimatorOverrideController plus)
    {
        string path = Folder + "/Patron.controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        ctrl.AddParameter("Pose", AnimatorControllerParameterType.Int);
        var layers = ctrl.layers;
        layers[0].iKPass = true;
        ctrl.layers = layers;
        var sm = ctrl.layers[0].stateMachine;

        string anims = Ithappy + "/Animations/";
        var walk = Load<AnimationClip>(anims + "Adult/Adult_Walk.anim");
        (string name, AnimationClip clip)[] states =
        {
            ("Idle", Load<AnimationClip>(anims + "Animation_Controllers/Idle_LookAround.anim")),
            ("Walk", walk),
            ("Slot Sit", Load<AnimationClip>(anims + "Adult/Adult_Slot_Sitting.anim")),
            ("Sit Talk", Load<AnimationClip>(anims + "Adult/Adult_Female_Sit_01_Talk.anim")),
            ("Slot Win", Load<AnimationClip>(anims + "Adult/Adult_Slot_Win.anim")),
            ("Slot Lose", Load<AnimationClip>(anims + "Adult/Adult_Slot_Lose.anim")),
            ("Idle Plain", Load<AnimationClip>(anims + "Animation_Controllers/Idle.anim")),
        };
        for (int i = 0; i < states.Length; i++)
        {
            var state = sm.AddState(states[i].name, new Vector3(300f, i * 60f, 0f));
            state.motion = states[i].clip;
            state.iKOnFeet = !StandingIdle(states[i].name);
            if (i == 0) sm.defaultState = state;
            var t = sm.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.Equals, i, "Pose");
            t.hasExitTime = false;
            t.duration = 0.25f;
            t.canTransitionToSelf = false;
        }

        senior = Override(ctrl, walk, Load<AnimationClip>(anims + "Senior/Senior_Walk.anim"), "Patron Senior");
        plus = Override(ctrl, walk, Load<AnimationClip>(anims + "Plus-Size/Plus-sizeAdult_Walk.anim"), "Patron Plus-Size");
        return ctrl;
    }

    static AnimatorOverrideController Override(AnimatorController ctrl, AnimationClip from, AnimationClip to, string name)
    {
        if (from == null || to == null) return null;
        string path = Folder + "/" + name + ".overrideController";
        AssetDatabase.DeleteAsset(path);
        var aoc = new AnimatorOverrideController(ctrl) { name = name };
        aoc[from] = to;
        AssetDatabase.CreateAsset(aoc, path);
        return aoc;
    }

    // ------------------------------------------------------------ characters

    // The senior base skeleton with the senior clothes put on it: each part's
    // skinned mesh is moved onto the base's bones (they share bone names).
    static GameObject BuildSenior()
    {
        var basePrefab = Load<GameObject>(Ithappy + "/Prefabs/Senior_Base.prefab");
        if (basePrefab == null) return null;
        string[] parts = { "Senior_Male_Body_02", "Senior_Male_Face_Usual", "Senior_Male_Hat_06", "Senior_Male_Hat_Hairstyle_01",
            "Senior_Male_Hat_Mustache_01", "Senior_Male_Jacket_03", "Senior_Male_Pants_10", "Senior_Male_Shoes_06" };

        var character = Object.Instantiate(basePrefab);
        character.name = "Senior Patron";
        try
        {
            foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Object.DestroyImmediate(smr.transform.childCount == 0 && smr.GetComponents<Component>().Length <= 2 ? (Object)smr.gameObject : smr);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            int added = 0;
            foreach (string part in parts)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ithappy + "/Prefabs/Characters/" + part + ".prefab");
                if (prefab == null) continue;
                foreach (var source in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (source.sharedMesh == null) continue;
                    var mapped = new Transform[source.bones.Length];
                    bool ok = true;
                    for (int i = 0; i < mapped.Length && ok; i++)
                        ok = source.bones[i] != null && bones.TryGetValue(source.bones[i].name, out mapped[i]);
                    if (!ok || source.rootBone == null || !bones.ContainsKey(source.rootBone.name))
                    {
                        Debug.LogWarning("SetupPatrons: senior part " + part + " doesn't fit the base skeleton - skipped.");
                        continue;
                    }
                    var go = new GameObject("Part_" + part);
                    go.transform.SetParent(character.transform, false);
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = source.sharedMesh;
                    smr.sharedMaterials = source.sharedMaterials;
                    smr.bones = mapped;
                    smr.rootBone = bones[source.rootBone.name];
                    smr.localBounds = source.localBounds;
                    added++;
                }
            }
            if (added < 3 || character.GetComponent<Animator>() == null) return null;
            string path = Folder + "/Senior Patron.prefab";
            return PrefabUtility.SaveAsPrefabAsset(character, path);
        }
        finally
        {
            Object.DestroyImmediate(character);
        }
    }

    // The gentleman in the purple suit, recoloured: purple cloth becomes black,
    // and no top hat.
    static GameObject BuildBond()
    {
        var source = Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/200.prefab");
        if (source == null) return null;
        var bond = Object.Instantiate(source);
        bond.name = "James Bond";
        try
        {
            foreach (var smr in bond.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name.Contains("Hat_10")) Object.DestroyImmediate(smr.gameObject);

            var swapped = new Dictionary<Material, Material>();
            foreach (var renderer in bond.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!swapped.TryGetValue(mats[i], out var tux)) swapped[mats[i]] = tux = TuxedoMaterial(mats[i]);
                    mats[i] = tux;
                }
                renderer.sharedMaterials = mats;
            }
            return PrefabUtility.SaveAsPrefabAsset(bond, Folder + "/James Bond.prefab");
        }
        finally
        {
            Object.DestroyImmediate(bond);
        }
    }

    // ------------------------------------------------------------ bartender

    // The old procedural bartender: white shirt, black waistcoat with buttons,
    // red bow tie, big dark moustache. Same look on the rigged man so he isn't
    // a copy of the croupier: the security outfit with its shirt repainted
    // (sleeves, collar and a V at the chest white, the rest waistcoat black),
    // the senior patron's moustache moved onto his face, and a bow tie and
    // buttons added. Everything is coloured from the pack's palette texture
    // with the pack's material, so it shades like the rest of the characters.
    static GameObject BuildBartender()
    {
        var source = Load<GameObject>(Ithappy + "/Prefabs/Ready_Characters/77_Security.prefab");
        var palette = Load<Material>(Ithappy + "/Materials/Color.mat");
        if (source == null || palette == null) return null;
        var atlas = ReadTexture(palette.GetTexture("_BaseMap") as Texture2D);
        if (atlas == null) return null;
        Vector2 white = Swatch(atlas, new Color(0.96f, 0.95f, 0.92f), 0.8f, 1f);
        Vector2 vest = Swatch(atlas, new Color(0.11f, 0.11f, 0.13f), 0.8f, 1f);
        Vector2 red = Swatch(atlas, new Color(0.8f, 0.07f, 0.1f), 0f, 0.15f);
        Vector2 gold = Swatch(atlas, new Color(0.83f, 0.64f, 0.26f), 0.5f, 0.8f);
        Vector2 brown = Swatch(atlas, new Color(0.16f, 0.1f, 0.07f), 0.75f, 1f);
        Object.DestroyImmediate(atlas);

        var man = Object.Instantiate(source);
        man.name = "Bartender";
        var temps = new List<GameObject>();
        try
        {
            Transform root = man.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;
            var bones = new Dictionary<string, Transform>();
            foreach (var t in man.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            if (!bones.TryGetValue("Head", out var head) || !bones.TryGetValue("Neck", out var neck)
                || !bones.TryGetValue("Spine1", out var chest) || !bones.TryGetValue("Spine", out var belly))
            {
                Debug.LogWarning("SetupPatrons: bartender skeleton not as expected - using the plain security look.");
                return null;
            }

            SkinnedMeshRenderer shirt = null, face = null, hair = null;
            foreach (var smr in man.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name.Contains("Glasses")) Object.DestroyImmediate(smr.gameObject);
                else if (smr.name.Contains("Security_Shirt")) shirt = smr;
                else if (smr.name.Contains("Face")) face = smr;
                else if (smr.name.Contains("Hairstyle")) hair = smr;
            }
            if (shirt == null || face == null) return null;

            // Dark hair, the same brown as the moustache.
            var hairMesh = hair != null ? ReadableCopy(hair.sharedMesh) : null;
            if (hairMesh != null) hair.sharedMesh = SaveMesh(Paint(hairMesh, brown), "Bartender Hair");

            // ---- Shirt -> shirt + waistcoat.
            var shirtMesh = ReadableCopy(shirt.sharedMesh);
            if (shirtMesh == null) return null;
            var shirtPose = BakeToRoot(shirt, root, out var shirtNormals);
            float neckY = neck.position.y;
            float vBottom = neckY - 0.21f;
            float frontZ = chest.position.z;
            var weights = shirtMesh.boneWeights;
            var uv = new Vector2[shirtMesh.vertexCount];
            for (int i = 0; i < uv.Length; i++)
            {
                Vector3 p = shirtPose[i];
                float arm = 0f;
                if (i < weights.Length)
                {
                    var w = weights[i];
                    arm = ArmWeight(shirt, w.boneIndex0) * w.weight0 + ArmWeight(shirt, w.boneIndex1) * w.weight1
                        + ArmWeight(shirt, w.boneIndex2) * w.weight2 + ArmWeight(shirt, w.boneIndex3) * w.weight3;
                }
                bool sleeve = arm > 0.5f;
                bool collar = p.y > neckY - 0.03f;
                float open = Mathf.InverseLerp(vBottom, neckY, p.y) * 0.085f;
                bool shirtFront = p.z > frontZ && shirtNormals[i].z > 0.15f && p.y > vBottom && Mathf.Abs(p.x) < open;
                uv[i] = sleeve || collar || shirtFront ? white : vest;
            }
            shirtMesh.uv = uv;
            shirtMesh.name = "Bartender Shirt";
            shirt.sharedMesh = SaveMesh(shirtMesh, "Bartender Shirt");

            // Front of the waistcoat at a height (for the bow tie and buttons).
            float FrontAt(float y)
            {
                float best = frontZ + 0.1f;
                bool found = false;
                for (int i = 0; i < shirtPose.Length; i++)
                {
                    Vector3 p = shirtPose[i];
                    if (Mathf.Abs(p.x) > 0.03f || Mathf.Abs(p.y - y) > 0.025f || shirtNormals[i].z < 0f) continue;
                    if (!found || p.z > best) best = p.z;
                    found = true;
                }
                return best;
            }

            // ---- Bow tie on the collar, buttons down the waistcoat.
            var bowMesh = SaveMesh(Paint(BowTie(), red), "Bartender Bow Tie");
            float bowY = neckY - 0.035f;
            AddProp("Bow Tie", bowMesh, palette, chest, new Vector3(0f, bowY, FrontAt(bowY) + 0.012f));
            var buttonMesh = SaveMesh(Paint(Button(), gold), "Bartender Button");
            for (int b = 0; b < 3; b++)
            {
                float y = vBottom - 0.03f - b * 0.075f;
                AddProp("Button " + (b + 1), buttonMesh, palette, b == 0 ? chest : belly, new Vector3(0f, y, FrontAt(y) + 0.004f));
            }

            // ---- Moustache: the senior's, lined up under this face's nose.
            var moustachePrefab = Load<GameObject>(Ithappy + "/Prefabs/Characters/Senior_Male_Hat_Mustache_01.prefab");
            var seniorFacePrefab = Load<GameObject>(Ithappy + "/Prefabs/Characters/Senior_Male_Face_Usual.prefab");
            if (moustachePrefab != null && seniorFacePrefab != null)
            {
                var m = Object.Instantiate(moustachePrefab);
                var sf = Object.Instantiate(seniorFacePrefab);
                temps.Add(m);
                temps.Add(sf);
                m.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                sf.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var mSmr = m.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var sfSmr = sf.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (mSmr != null && sfSmr != null)
                {
                    Vector3 shift = Nose(BakeToRoot(face, root, out _)) - Nose(BakeToRoot(sfSmr, sf.transform, out _));
                    var verts = BakeToRoot(mSmr, m.transform, out var normals);
                    Vector3 centre = Vector3.zero;
                    for (int i = 0; i < verts.Length; i++) centre += verts[i];
                    centre /= Mathf.Max(1, verts.Length);
                    // A little bigger and bushier than the senior's - the old bartender's was his whole look.
                    var grow = new Vector3(1.3f, 1.25f, 1.15f);
                    for (int i = 0; i < verts.Length; i++) verts[i] = Vector3.Scale(verts[i] - centre, grow) + centre + shift + Vector3.forward * 0.004f;
                    Vector3 pivot = head.InverseTransformPoint(centre + shift);

                    var baked = new Mesh();
                    mSmr.BakeMesh(baked, true);
                    var mesh = new Mesh { name = "Bartender Moustache" };
                    var local = new Vector3[verts.Length];
                    var localN = new Vector3[verts.Length];
                    for (int i = 0; i < verts.Length; i++)
                    {
                        local[i] = head.InverseTransformPoint(verts[i]) - pivot;
                        localN[i] = head.InverseTransformDirection(normals[i]).normalized;
                    }
                    mesh.vertices = local;
                    mesh.normals = localN;
                    var tris = new List<int>();
                    for (int s = 0; s < baked.subMeshCount; s++) tris.AddRange(baked.GetTriangles(s));
                    mesh.SetTriangles(tris, 0);
                    mesh.RecalculateBounds();
                    Object.DestroyImmediate(baked);
                    var moustache = AddProp("Moustache", SaveMesh(Paint(mesh, brown), "Bartender Moustache"), palette, head, Vector3.zero);
                    moustache.transform.localPosition = pivot;
                    moustache.transform.localRotation = Quaternion.identity;
                }
            }

            return PrefabUtility.SaveAsPrefabAsset(man, Folder + "/Bartender.prefab");
        }
        finally
        {
            Object.DestroyImmediate(man);
            foreach (var t in temps) if (t != null) Object.DestroyImmediate(t);
        }
    }

    static float ArmWeight(SkinnedMeshRenderer smr, int bone)
    {
        if (bone < 0 || bone >= smr.bones.Length || smr.bones[bone] == null) return 0f;
        string n = smr.bones[bone].name;
        return n.Contains("Arm") || n.Contains("Hand") ? 1f : 0f;
    }

    // The renderer's current (rest) pose, in the character root's space.
    static Vector3[] BakeToRoot(SkinnedMeshRenderer smr, Transform root, out Vector3[] normals)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var v = baked.vertices;
        var n = baked.normals;
        Matrix4x4 toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = toRoot.MultiplyPoint3x4(v[i]);
            if (i < n.Length) n[i] = toRoot.MultiplyVector(n[i]).normalized;
        }
        if (n.Length != v.Length) n = new Vector3[v.Length];
        normals = n;
        Object.DestroyImmediate(baked);
        return v;
    }

    // Tip of the nose: the face's front-most point (characters face +Z).
    static Vector3 Nose(Vector3[] face)
    {
        Vector3 best = Vector3.zero;
        for (int i = 0; i < face.Length; i++) if (i == 0 || face[i].z > best.z) best = face[i];
        return best;
    }

    static Mesh ReadableCopy(Mesh source)
    {
        if (source == null) return null;
        if (!source.isReadable)
        {
            // Mesh data can't be copied without Read/Write; turn it on for this model.
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }
        var copy = Object.Instantiate(source);
        return copy.vertexCount > 0 && copy.uv.Length == copy.vertexCount ? copy : null;
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = Folder + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Every vertex samples one palette colour.
    static Mesh Paint(Mesh mesh, Vector2 swatch)
    {
        var uv = new Vector2[mesh.vertexCount];
        for (int i = 0; i < uv.Length; i++) uv[i] = swatch;
        mesh.uv = uv;
        return mesh;
    }

    static GameObject AddProp(string name, Mesh mesh, Material material, Transform bone, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(bone, false);
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // Two wings, tall at the ends and pinched at the knot, plus the knot.
    static Mesh BowTie()
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var parts = new List<CombineInstance>();
        for (int side = -1; side <= 1; side += 2)
        {
            var wing = Object.Instantiate(cube);
            var v = wing.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                float outer = v[i].x * side > 0f ? 1f : 0.4f;
                v[i] = new Vector3(v[i].x * 0.055f + side * 0.034f, v[i].y * 0.05f * outer, v[i].z * 0.022f * (outer > 0.5f ? 0.8f : 1f));
            }
            wing.vertices = v;
            parts.Add(new CombineInstance { mesh = wing, transform = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, side * 6f)) });
        }
        parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.Scale(new Vector3(0.022f, 0.026f, 0.026f)) });
        var bow = new Mesh();
        bow.CombineMeshes(parts.ToArray(), true, true);
        bow.RecalculateNormals();
        bow.RecalculateBounds();
        return bow;
    }

    static Mesh Button()
    {
        var sphere = Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Sphere.fbx"));
        var v = sphere.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], new Vector3(0.017f, 0.017f, 0.008f));
        sphere.vertices = v;
        sphere.RecalculateNormals();
        sphere.RecalculateBounds();
        return sphere;
    }

    static Texture2D ReadTexture(Texture2D tex)
    {
        string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var img = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        img.LoadImage(File.ReadAllBytes(path));
        return img;
    }

    // UV of the palette pixel closest to a colour, searched within a band of U
    // (the palette is a soft gradient, so the neighbourhood is the same colour).
    static Vector2 Swatch(Texture2D atlas, Color want, float uMin, float uMax)
    {
        var px = atlas.GetPixels32();
        int w = atlas.width, h = atlas.height;
        float best = float.MaxValue;
        int bx = w / 2, by = h / 2;
        for (int y = 8; y < h - 8; y += 2)
            for (int x = Mathf.Max(8, (int)(uMin * w)); x < Mathf.Min(w - 8, (int)(uMax * w)); x += 2)
            {
                Color c = px[y * w + x];
                float d = (c.r - want.r) * (c.r - want.r) + (c.g - want.g) * (c.g - want.g) + (c.b - want.b) * (c.b - want.b);
                if (d < best) { best = d; bx = x; by = y; }
            }
        return new Vector2((bx + 0.5f) / w, (by + 0.5f) / h);
    }

    // A copy of the material whose colour atlas has its purple band turned
    // into black dinner-jacket cloth (a dark charcoal, so folds still show).
    static Material TuxedoMaterial(Material source)
    {
        var tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") as Texture2D : null;
        string texPath = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
        if (string.IsNullOrEmpty(texPath) || !File.Exists(texPath)) return source;

        string outTex = Folder + "/Bond " + Path.GetFileNameWithoutExtension(texPath) + ".png";
        var img = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        img.LoadImage(File.ReadAllBytes(texPath));
        var px = img.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (h < 0.69f || h > 0.88f || s < 0.22f) continue;
            float shade = Mathf.Lerp(0.035f, 0.14f, v);
            var dark = Color.HSVToRGB(0.67f, 0.12f, shade);
            px[i] = new Color(dark.r, dark.g, dark.b, c.a);
        }
        img.SetPixels32(px);
        File.WriteAllBytes(outTex, img.EncodeToPNG());
        Object.DestroyImmediate(img);
        AssetDatabase.ImportAsset(outTex);

        var srcImporter = AssetImporter.GetAtPath(texPath) as TextureImporter;
        var importer = AssetImporter.GetAtPath(outTex) as TextureImporter;
        if (srcImporter != null && importer != null)
        {
            importer.sRGBTexture = srcImporter.sRGBTexture;
            importer.filterMode = srcImporter.filterMode;
            importer.mipmapEnabled = srcImporter.mipmapEnabled;
            importer.wrapMode = srcImporter.wrapMode;
            importer.maxTextureSize = srcImporter.maxTextureSize;
            importer.SaveAndReimport();
        }
        var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(outTex);

        string matPath = Folder + "/Bond " + source.name + ".mat";
        AssetDatabase.DeleteAsset(matPath);
        var mat = new Material(source) { name = "Bond " + source.name };
        mat.SetTexture("_BaseMap", newTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", newTex);
        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    // ------------------------------------------------------------ materials

    static Material Mat(string name, Color color, float smooth, float metal)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material GlassMat()
    {
        var mat = Mat("Martini Glass", new Color(0.85f, 0.95f, 1f, 0.28f), 0.95f, 0f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------ navmesh

    // Walkable hall floor from what you can see in it (render meshes, so
    // stools and decor without colliders are walked around too).
    static NavMeshData BakeNavMesh()
    {
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.22f;
        settings.agentHeight = 1.7f;
        settings.agentClimb = 0.2f;
        settings.agentSlope = 30f;

        var markups = new List<NavMeshBuildMarkup>();
        foreach (var rig in Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include))
            markups.Add(new NavMeshBuildMarkup { root = rig.transform, ignoreFromBuild = true, applyToChildren = true });
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(Hall, ~0, NavMeshCollectGeometry.RenderMeshes, 0, markups, sources);
        if (sources.Count == 0)
        {
            Debug.LogWarning("SetupPatrons: nothing to bake a NavMesh from in the hall.");
            return null;
        }
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, Hall, Vector3.zero, Quaternion.identity);
        if (data == null) return null;
        data.name = "Casino Patrons NavMesh";
        string path = Folder + "/Casino Patrons NavMesh.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(data, path);
        return data;
    }
}
