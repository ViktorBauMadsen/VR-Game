using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Builds a complete beer pong table into the open scene: an 8 ft table, a
// 3-2-1 triangle of 6 cups at each end (Red at -Z, Blue at +Z), one grabbable
// ball per team floating at its end, and a scoreboard facing each player.
// See BeerPongTable for the rules.
//
// The table is dropped onto whatever is under the Scene view's focus point,
// so frame the spot you want first (F on an object, or just orbit there),
// then move/rotate the root freely afterwards - everything is relative to it.
// Only builds if "Beer Pong Table" doesn't already exist, so tweaks aren't
// stomped (re-running on an existing table just re-lays its scoreboards);
// delete it and re-run to rebuild. The cup mesh, materials and
// ball physics material are saved under Assets/BeerPong.
public static class SetupBeerPong
{
    const string RootName = "Beer Pong Table";
    const string Folder = "Assets/BeerPong";
    const string MaterialsFolder = Folder + "/Materials";
    const string CupMeshPath = Folder + "/Cup Mesh.asset";
    const string BallPhysicsPath = Folder + "/Ping Pong Ball.physicsMaterial";

    // Regulation 8 ft x 2 ft table, 76 cm tall.
    const float TableLength = 2.44f;
    const float TableWidth = 0.61f;
    const float TableHeight = 0.76f;
    const float TableTopThickness = 0.04f;

    // Cups are a little bigger than a real 16 oz cup, and the ball a little
    // bigger than a real 40 mm one - in VR a regulation-size game is
    // frustratingly hard to hit and fiddly to grab.
    const float CupTopRadius = 0.055f;
    const float CupBottomRadius = 0.038f;
    const float CupHeight = 0.12f;
    const float CupWall = 0.004f;
    const float CupSpacing = 0.116f;
    const float CupBackRowInset = 0.09f; // from the table's end edge to the back row's centres
    const float BallDiameter = 0.044f;

    [MenuItem("Tools/Beer Pong/Build Beer Pong Table")]
    static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            BuildScoreboards(existing);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"SetupBeerPong: '{RootName}' already exists - only rebuilt its scoreboards. Delete it and re-run to fully rebuild.");
            return;
        }

        EnsureFolder(Folder);
        EnsureFolder(MaterialsFolder);

        var tableMat = CreateOrLoadMaterial("Table Top", new Color(0.05f, 0.18f, 0.1f), 0.4f);
        var legMat = CreateOrLoadMaterial("Table Legs", new Color(0.12f, 0.12f, 0.13f), 0.6f);
        var lineMat = CreateOrLoadMaterial("Table Line", Color.white, 0.3f);
        var cupMats = new[]
        {
            CreateOrLoadMaterial("Cup Red", new Color(0.85f, 0.08f, 0.08f), 0.7f),
            CreateOrLoadMaterial("Cup Blue", new Color(0.1f, 0.3f, 0.95f), 0.7f),
        };
        var beerMat = CreateOrLoadMaterial("Beer", new Color(0.95f, 0.65f, 0.12f), 0.9f);
        var ballMat = CreateOrLoadMaterial("Ping Pong Ball", new Color(1f, 0.97f, 0.9f), 0.5f);
        var cupMesh = CreateOrLoadCupMesh();
        var ballPhysics = CreateOrLoadBallPhysics();

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build " + RootName);
        root.transform.position = FindDropPoint();
        var table = root.AddComponent<BeerPongTable>();

        BuildTableBody(root.transform, tableMat, legMat, lineMat);

        for (int side = 0; side < 2; side++)
            BuildCupTriangle(root.transform, side, cupMesh, cupMats[side], beerMat);

        for (int side = 0; side < 2; side++)
            BuildBall(root.transform, side, ballMat, ballPhysics);

        BuildScoreboards(root);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupBeerPong: built '{RootName}' at {root.transform.position}. Move/rotate the root to place it - Red throws from -Z, Blue from +Z.");
    }

    // Raycast down from the Scene view's focus point so the legs stand on the floor.
    static Vector3 FindDropPoint()
    {
        var view = SceneView.lastActiveSceneView;
        var pivot = view != null ? view.pivot : Vector3.zero;
        if (Physics.Raycast(pivot + Vector3.up * 3f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        return pivot;
    }

    static void BuildTableBody(Transform root, Material topMat, Material legMat, Material lineMat)
    {
        var top = CreatePrimitive("Table Top", PrimitiveType.Cube, root, topMat);
        top.transform.localPosition = new Vector3(0f, TableHeight - TableTopThickness / 2f, 0f);
        top.transform.localScale = new Vector3(TableWidth, TableTopThickness, TableLength);

        // Cosmetic centre line and edge trim - no colliders, the top's box covers them.
        var centreLine = CreatePrimitive("Centre Line", PrimitiveType.Cube, root, lineMat, collider: false);
        centreLine.transform.localPosition = new Vector3(0f, TableHeight + 0.0005f, 0f);
        centreLine.transform.localScale = new Vector3(TableWidth, 0.001f, 0.02f);

        const float legSize = 0.05f;
        float legHeight = TableHeight - TableTopThickness;
        float lx = TableWidth / 2f - legSize;
        float lz = TableLength / 2f - legSize;
        int i = 0;
        foreach (var (x, z) in new[] { (-lx, -lz), (lx, -lz), (-lx, lz), (lx, lz) })
        {
            var leg = CreatePrimitive($"Leg {++i}", PrimitiveType.Cube, root, legMat);
            leg.transform.localPosition = new Vector3(x, legHeight / 2f, z);
            leg.transform.localScale = new Vector3(legSize, legHeight, legSize);
        }
    }

    // Side 0 (Red) sits at -Z, side 1 (Blue) at +Z. Rows run back (3 cups,
    // nearest the table's end) to front (1 cup, pointing at the opponent).
    static void BuildCupTriangle(Transform root, int side, Mesh cupMesh, Material cupMat, Material beerMat)
    {
        string team = side == 0 ? "Red" : "Blue";
        var group = new GameObject($"{team} Cups").transform;
        group.SetParent(root, false);

        float sign = side == 0 ? -1f : 1f;
        float backRowZ = TableLength / 2f - CupBackRowInset;
        float rowStep = CupSpacing * Mathf.Sqrt(3f) / 2f; // tightly packed triangle

        int n = 0;
        for (int row = 0; row < 3; row++)
        {
            int cupsInRow = 3 - row;
            for (int i = 0; i < cupsInRow; i++)
            {
                float x = (i - (cupsInRow - 1) / 2f) * CupSpacing;
                float z = sign * (backRowZ - row * rowStep);
                BuildCup(group, $"{team} Cup {++n}", side, new Vector3(x, TableHeight, z), cupMesh, cupMat, beerMat);
            }
        }
    }

    static void BuildCup(Transform parent, string name, int side, Vector3 localPos, Mesh cupMesh, Material cupMat, Material beerMat)
    {
        var cup = new GameObject(name);
        cup.transform.SetParent(parent, false);
        cup.transform.localPosition = localPos;
        cup.AddComponent<MeshFilter>().sharedMesh = cupMesh;
        cup.AddComponent<MeshRenderer>().sharedMaterial = cupMat;
        cup.AddComponent<MeshCollider>().sharedMesh = cupMesh; // concave - real walls the ball can rattle off

        var cupSo = new SerializedObject(cup.AddComponent<BeerPongCup>());
        cupSo.FindProperty("side").intValue = side;
        cupSo.ApplyModifiedPropertiesWithoutUndo();

        // Visual only: a ball that drops in passes straight through the beer
        // to the score zone below it.
        const float beerHeight = CupHeight * 0.45f;
        float beerRadius = Mathf.Lerp(CupBottomRadius, CupTopRadius, beerHeight / CupHeight) - CupWall;
        var beer = CreatePrimitive("Beer", PrimitiveType.Cylinder, cup.transform, beerMat, collider: false);
        beer.transform.localPosition = new Vector3(0f, beerHeight, 0f);
        beer.transform.localScale = new Vector3(beerRadius * 2f, 0.002f, beerRadius * 2f);

        var zone = new GameObject("Score Zone");
        zone.transform.SetParent(cup.transform, false);
        zone.transform.localPosition = new Vector3(0f, CupHeight * 0.35f, 0f);
        var trigger = zone.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = CupBottomRadius - CupWall;
    }

    static void BuildBall(Transform root, int side, Material ballMat, PhysicsMaterial physics)
    {
        string team = side == 0 ? "Red" : "Blue";
        float sign = side == 0 ? -1f : 1f;

        // Floats just past the table's end, off to the right of the thrower,
        // at about hip/hand height for someone standing at that end.
        var spawn = new GameObject($"{team} Ball Spawn").transform;
        spawn.SetParent(root, false);
        spawn.localPosition = new Vector3(-sign * 0.22f, TableHeight + 0.25f, sign * (TableLength / 2f + 0.08f));

        var ball = CreatePrimitive($"{team} Ball", PrimitiveType.Sphere, root, ballMat);
        ball.transform.localPosition = spawn.localPosition;
        ball.transform.localScale = Vector3.one * BallDiameter;
        ball.GetComponent<SphereCollider>().sharedMaterial = physics;

        var rb = ball.AddComponent<Rigidbody>();
        rb.mass = 0.01f;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // stays valid while kinematic, and keeps a fast ball from tunnelling the thin cup walls
        rb.isKinematic = true; // waits at its spawn point until grabbed - see BeerPongBall

        var grab = ball.AddComponent<XRGrabInteractable>();
        var grabSo = new SerializedObject(grab);
        grabSo.FindProperty("m_MovementType").enumValueIndex = 2; // Instantaneous - 1:1 with the hand, best for aiming
        grabSo.FindProperty("m_ThrowOnDetach").boolValue = false; // BeerPongBall computes the throw itself

        grabSo.FindProperty("m_UseDynamicAttach").boolValue = true; // grab it where your hand is instead of snapping to centre
        grabSo.ApplyModifiedPropertiesWithoutUndo();

        var ballSo = new SerializedObject(ball.AddComponent<BeerPongBall>());
        ballSo.FindProperty("spawnPoint").objectReferenceValue = spawn;
        ballSo.ApplyModifiedPropertiesWithoutUndo();

        ball.AddComponent<InteractableHighlight>();
    }

    static string ScoreboardName(int side) => $"Scoreboard ({(side == 0 ? "Red" : "Blue")} view)";

    // (Re)creates both score displays and wires them into BeerPongTable,
    // replacing any earlier ones (older versions were 3D TextMeshPro or
    // world-space canvases, both of which rendered away from their real
    // position in this project). The displays are just opaque quads; the
    // table renders the score text into a texture at runtime and puts it on
    // them - see BeerPongTable.
    static void BuildScoreboards(GameObject root)
    {
        var table = root.GetComponent<BeerPongTable>();
        var mat = CreateOrLoadUnlitMaterial("Scoreboard Screen");
        var displays = new List<Renderer>();
        for (int side = 0; side < 2; side++)
        {
            var old = root.transform.Find(ScoreboardName(side));
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            displays.Add(BuildScoreDisplay(root.transform, side, mat));
        }

        var tableSo = new SerializedObject(table);
        var prop = tableSo.FindProperty("scoreDisplays");
        prop.arraySize = displays.Count;
        for (int i = 0; i < displays.Count; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = displays[i];
        tableSo.ApplyModifiedProperties();
    }

    // Lies flat on the table, on the half nearest the player who reads it,
    // between the centre line and their own cups. A Quad's visible face is
    // its local -Z with the texture's top along local +Y, so: forward = down
    // into the table (face looks up), up = away from the reader. Red stands
    // at -Z, so for Red "away" is +Z. 1024x384 texture -> 0.55 x 0.206 m.
    static Renderer BuildScoreDisplay(Transform root, int side, Material mat)
    {
        float sign = side == 0 ? -1f : 1f;
        float width = TableWidth - 0.06f;

        var quad = CreatePrimitive(ScoreboardName(side), PrimitiveType.Quad, root, mat, collider: false);
        Undo.RegisterCreatedObjectUndo(quad, "Build Beer Pong Scoreboard");
        quad.transform.localPosition = new Vector3(0f, TableHeight + 0.002f, sign * 0.4f);
        quad.transform.localRotation = Quaternion.LookRotation(Vector3.down, side == 0 ? Vector3.forward : Vector3.back);
        quad.transform.localScale = new Vector3(width, width * 384f / 1024f, 1f);
        var renderer = quad.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    static Material CreateOrLoadUnlitMaterial(string name)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        EnsureFolder(Folder);
        EnsureFolder(MaterialsFolder);
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
        mat.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // Lathes a cup profile around Y: a solid-walled, open-topped truncated
    // cone with a real thickness, so a concave MeshCollider made from it
    // gives the ball proper walls to bounce off from either side.
    static Mesh CreateOrLoadCupMesh()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(CupMeshPath);
        if (mesh != null) return mesh;

        Vector2[] profile = // (radius, height), walked outside-bottom -> over the rim -> inside-bottom
        {
            new Vector2(0f, 0f),
            new Vector2(CupBottomRadius, 0f),
            new Vector2(CupTopRadius, CupHeight),
            new Vector2(CupTopRadius - CupWall, CupHeight),
            new Vector2(CupBottomRadius - CupWall, CupWall),
            new Vector2(0f, CupWall),
        };
        mesh = Lathe(profile, 32);
        mesh.name = "Cup Mesh";
        AssetDatabase.CreateAsset(mesh, CupMeshPath);
        return mesh;
    }

    // Each profile segment gets its own ring of vertices so the bottom, walls
    // and rim are hard-edged. Normals are the profile segment's normal rotated
    // round the axis; the winding is picked per-quad to agree with them.
    static Mesh Lathe(Vector2[] profile, int segments)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        for (int p = 0; p < profile.Length - 1; p++)
        {
            Vector2 a = profile[p], b = profile[p + 1];
            Vector2 d = (b - a).normalized;
            var n2 = new Vector2(d.y, -d.x); // outward side of the profile walked in this order

            int start = vertices.Count;
            for (int s = 0; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                vertices.Add(new Vector3(a.x * cos, a.y, a.x * sin));
                vertices.Add(new Vector3(b.x * cos, b.y, b.x * sin));
                var n = new Vector3(n2.x * cos, n2.y, n2.x * sin).normalized;
                normals.Add(n);
                normals.Add(n);
            }

            for (int s = 0; s < segments; s++)
            {
                int i0 = start + s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                // Use the wider edge of the quad (one end can collapse to the axis).
                Vector3 faceNormal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                if (faceNormal.sqrMagnitude < 1e-12f)
                    faceNormal = Vector3.Cross(vertices[i3] - vertices[i1], vertices[i2] - vertices[i1]);
                bool flip = Vector3.Dot(faceNormal, normals[i0] + normals[i2]) < 0f;
                if (flip) triangles.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
                else triangles.AddRange(new[] { i0, i1, i2, i1, i3, i2 });
            }
        }

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static PhysicsMaterial CreateOrLoadBallPhysics()
    {
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallPhysicsPath);
        if (mat != null) return mat;

        mat = new PhysicsMaterial("Ping Pong Ball")
        {
            bounciness = 0.8f,
            bounceCombine = PhysicsMaterialCombine.Maximum, // bouncy against everything, whatever it's made of
            dynamicFriction = 0.2f,
            staticFriction = 0.2f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
        };
        AssetDatabase.CreateAsset(mat, BallPhysicsPath);
        return mat;
    }

    static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Material CreateOrLoadMaterial(string name, Color baseColor, float smoothness)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
