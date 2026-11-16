using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the "How to play" board: a framed sign with the controls, how a
// game (the quota run) works and how each table is played. It is an ordinary
// scene object, so it is there in the hub and during a run alike.
//
//   Tools > Casino > Build Tutorial Wall
//
// Look at the wall you want it on in the Scene view (wall in the middle of
// the view) and run it: the board is hung flat on that wall at reading
// height. If there is nothing solid in the middle of the view it is stood on
// the floor under the Scene view's focus point instead, facing the camera.
// Either way, move/rotate the "Tutorial Wall" root freely afterwards - its
// +Z side is the side you read. Delete it and re-run to rebuild (the text
// lives in this file).
public static class SetupTutorialWall
{
    const string RootName = "Tutorial Wall";
    const float Centre = 1.55f;   // height of the middle of the board above the floor
    const float BoardWidth = 2.5f, BoardHeight = 1.42f;

    const string Controls =
        "<align=left><color=#FFD24A><b>CONTROLS</b></color>\n"
        + "<size=72%><b>LEFT STICK</b>  walk\n"
        + "<b>RIGHT STICK</b>  turn\n"
        + "<b>GRIP</b>  grab things, press table buttons (point at or touch them)\n"
        + "<b>TRIGGER</b>  press menu buttons\n"
        + "<b>Y</b> (left hand)  open / close the menu\n\n"
        + "<color=#FFD24A><b>YOUR MONEY</b></color>\n"
        + "Top left of your view. Chips are in the pockets at your waist: grab one, drop it on a bet.</size>";

    const string Game =
        "<align=left><color=#FFD24A><b>THE GAME</b></color>\n"
        + "<size=72%>The hub is play money. Open the menu (Y):\n"
        + "<b>FRIENDS</b>  host or join a lobby\n"
        + "<b>RUN</b>  start the game\n\n"
        + "You owe the family. Every day has a <b>quota</b> and a <b>clock</b>. "
        + "Have the quota in cash when time runs out and the collector takes his cut - then a bigger quota tomorrow.\n\n"
        + "<color=#FF6A5C><b>Come up short and everyone gets shot.</b></color>\n\n"
        + "Friends share one quota and one pool of cash. Beat the quota to earn <b>tokens</b> - the further over, the more. Spend them on powerups in <b>SHOP</b>.</size>";

    const string Tables =
        "<align=left><color=#FFD24A><b>THE TABLES</b></color>\n"
        + "<size=72%><b>ROULETTE</b>  drop chips on numbers or colours before the countdown ends - everyone bets on the same spin. Alone, the spin button skips the wait.\n\n"
        + "<b>BLACKJACK</b>  - / + sets the bet, then DEAL. With friends, everyone who presses DEAL shares one hand: HIT, STAND or DOUBLE together.\n\n"
        + "<b>SLOTS</b>  set the bet, grab the lever and pull.\n\n"
        + "<b>BAR</b>  order at the counter. Drinks cost money - and aim.\n\n"
        + "Point at a dealer and squeeze grip for tips.</size>";

    [MenuItem("Tools/Casino/Build Tutorial Wall")]
    static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log($"SetupTutorialWall: '{RootName}' already exists (selected it). Delete it and re-run to rebuild.");
            return;
        }

        SetupAvatar.EnsureFolder("Assets/Avatar");
        SetupAvatar.EnsureFolder(SetupAvatar.MaterialsFolder);
        var wood = SetupAvatar.Lit("Blackjack Wood", new Color(0.2f, 0.1f, 0.05f), 0.6f);
        var gold = SetupAvatar.Lit("Blackjack Gold", new Color(0.9f, 0.68f, 0.2f), 0.8f, 0.8f);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build " + RootName);
        bool onWall = PlaceOnWallInView(root.transform);
        if (!onWall) SetupAvatar.PlaceAtSceneFocus(root.transform);
        var r = root.transform;

        // Frame and backing. Everything sits in front of z = 0 (the wall).
        SetupAvatar.Box("Frame", r, gold, new Vector3(0f, Centre, 0.015f), new Vector3(BoardWidth + 0.08f, BoardHeight + 0.08f, 0.03f), false);
        SetupAvatar.Box("Backing", r, wood, new Vector3(0f, Centre, 0.025f), new Vector3(BoardWidth, BoardHeight, 0.03f), false);

        var paper = new Color(0.05f, 0.06f, 0.09f);
        // A Quad shows on its -Z side, so each is turned to face the board's +Z.
        var facing = Quaternion.LookRotation(Vector3.back, Vector3.up);
        const float z = 0.043f;

        var title = SetupAvatar.TextQuad("Title", r, "HOW TO PLAY", new Color(1f, 0.82f, 0.29f), paper, new Vector2Int(2048, 200));
        title.transform.localPosition = new Vector3(0f, Centre + 0.56f, z);
        title.transform.localRotation = facing;
        title.transform.localScale = new Vector3(BoardWidth - 0.1f, 0.2f, 1f);

        // The reader looks along -Z, so their left is the board's +X.
        Column(r, "Controls", Controls, 0.8f, paper, facing, z);
        Column(r, "The Game", Game, 0f, paper, facing, z);
        Column(r, "The Tables", Tables, -0.8f, paper, facing, z);

        if (!onWall)
        {
            // Free-standing: give it two legs so it doesn't float.
            SetupAvatar.Box("Leg L", r, wood, new Vector3(BoardWidth * 0.4f, (Centre - BoardHeight * 0.5f) * 0.5f, 0.015f), new Vector3(0.06f, Centre - BoardHeight * 0.5f, 0.03f), false);
            SetupAvatar.Box("Leg R", r, wood, new Vector3(-BoardWidth * 0.4f, (Centre - BoardHeight * 0.5f) * 0.5f, 0.015f), new Vector3(0.06f, Centre - BoardHeight * 0.5f, 0.03f), false);
        }

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupTutorialWall: built '{RootName}' at {root.transform.position} "
            + (onWall ? "on the wall in the middle of the Scene view" : "standing on the floor (no wall found in the middle of the Scene view)")
            + ". Move/rotate the root to adjust - you read it from its +Z side. Save the scene to keep it.");
    }

    static void Column(Transform root, string name, string text, float x, Color background, Quaternion facing, float z)
    {
        var panel = SetupAvatar.TextQuad(name, root, text, Color.white, background, new Vector2Int(800, 1100), 64f);
        panel.transform.localPosition = new Vector3(x, Centre - 0.11f, z);
        panel.transform.localRotation = facing;
        panel.transform.localScale = new Vector3(0.78f, 1.07f, 1f);
    }

    // Hangs the root on whatever upright surface is in the middle of the
    // Scene view: origin at floor level against the wall, +Z out of the wall.
    static bool PlaceOnWallInView(Transform root)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null) return false;
        var cam = view.camera.transform;
        if (!Physics.Raycast(cam.position, cam.forward, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (Mathf.Abs(hit.normal.y) > 0.3f) return false; // floor, ceiling or a slope - not a wall

        Vector3 outward = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
        Vector3 foot = hit.point + outward * 0.3f;
        float floor = Physics.Raycast(foot + Vector3.up * 0.2f, Vector3.down, out var ground, 20f, ~0, QueryTriggerInteraction.Ignore)
            ? ground.point.y : hit.point.y - Centre;
        root.SetPositionAndRotation(new Vector3(hit.point.x, floor, hit.point.z) + outward * 0.005f,
            Quaternion.LookRotation(outward, Vector3.up));
        return true;
    }
}
