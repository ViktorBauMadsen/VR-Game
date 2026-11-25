using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Draw only the side of the casino wall you can actually see. Unity only
// skips what is outside the view, not what is hidden behind a wall, so from
// inside the hall the whole facade, forecourt and desert (~1,700 meshes) was
// drawn behind the walls every frame, and from outside the whole interior
// (~1,300). On a Quest 2 those draw calls are most of the frame.
//
// At start every scene mesh is sorted by where it is: wholly inside the hall,
// wholly outside the building, or in between (the shell, the doors - always
// drawn). Then each frame:
//   inside the hall  -> the outside is drawn only while the doorway is in view
//   outside          -> the inside is drawn only while the doorway is in view
//   near the doorway -> everything
//
// Shadows: the interior sits in the roof's shadow, so the sun's shadows of
// things inside it can never be seen - yet each of them was drawn into the
// shadow map every frame. Interior meshes stop casting sun shadows (the
// building shell still does, so the look is unchanged).
//
// The sun: for the same reason, while you're inside and the outside isn't
// drawn, the directional light is switched off - no shadow map is rendered
// and no sun lighting is computed for anything. The hall is in the roof's
// shadow anyway, so it looks the same (except the far end of the hall, past
// the 20 m shadow range, which the sun used to light straight through the
// roof). It comes back the moment the doorway is in view.
//
// Only meshes that exist at start and can't be picked up are managed;
// anything spawned later (NPCs, drinks, other players) is always drawn.
// Installs itself in any scene with an "Interior Decor" root.
public sealed class HallCulling : MonoBehaviour
{
    // The hall's inside (from DecorateInterior) and the building's outer shell (DecorateExterior).
    public static readonly Bounds Hall = MinMax(new Vector3(-9.97f, 0.3f, -0.35f), new Vector3(4.18f, 5.4f, 28.09f));
    public static readonly Bounds Shell = MinMax(new Vector3(-10.12f, -1f, -0.5f), new Vector3(4.32f, 7.05f, 28.28f));

    public enum Zone { Inside, Outside, Between }

    // Which list a mesh with these bounds ends up in (Merge Decor keeps
    // merged meshes from straddling two zones).
    public static Zone ZoneOf(Bounds b)
    {
        if (Hall.Contains(b.min) && Hall.Contains(b.max)) return Zone.Inside;
        return Shell.Intersects(b) ? Zone.Between : Zone.Outside;
    }
    // The front door's opening, generously padded, and how close counts as "at the door".
    static readonly Bounds Doorway = MinMax(new Vector3(-4.75f, 0.2f, 27.6f), new Vector3(-0.63f, 4.5f, 28.6f));
    const float NearDoor = 3f, HideDelay = 0.4f;

    readonly List<Renderer> _inside = new List<Renderer>(), _outside = new List<Renderer>();
    readonly Plane[] _planes = new Plane[6];
    bool _ready, _wantInside = true, _wantOutside = true, _shownInside = true, _shownOutside = true;
    Light _sun;
    bool _sunShown = true;
    float _insideOffAt, _outsideOffAt;

    static Bounds MinMax(Vector3 min, Vector3 max)
    {
        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode) => Install(scene);

    static void Install(Scene scene)
    {
        if (!scene.IsValid() || FindAnyObjectByType<HallCulling>() != null) return;
        bool casino = false;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Interior Decor") { casino = true; break; }
        if (!casino) return;
        var go = new GameObject("Hall Culling");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<HallCulling>();
    }

    IEnumerator Start()
    {
        // Let the tables, slots and bar finish building themselves first.
        for (int i = 0; i < 10; i++) yield return null;
        Sort();
        _ready = true;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        Show(_inside, true);
        Show(_outside, true);
        if (_sun != null) _sun.enabled = true;
    }

    void Sort()
    {
        Transform rig = Camera.main != null ? Camera.main.transform.root : null;
        int shadowsOff = 0;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (rig != null && root.transform == rig) continue;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<Rigidbody>(true) != null) continue; // can be picked up / thrown
                Bounds b = r.bounds;
                if (b.size == Vector3.zero) continue;
                var zone = ZoneOf(b);
                if (zone == Zone.Inside)
                {
                    _inside.Add(r);
                    if (r.shadowCastingMode == ShadowCastingMode.On || r.shadowCastingMode == ShadowCastingMode.TwoSided)
                    {
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        shadowsOff++;
                    }
                }
                else if (zone == Zone.Outside) _outside.Add(r);
            }
        }
        // The scene's sun (Lighting settings), else its first switched-on directional light.
        _sun = RenderSettings.sun != null && RenderSettings.sun.enabled ? RenderSettings.sun : null;
        if (_sun == null)
            foreach (var light in FindObjectsByType<Light>())
                if (light.type == LightType.Directional && light.enabled && light.gameObject.scene == gameObject.scene) { _sun = light; break; }

        Debug.Log($"HallCulling: {_inside.Count} meshes inside the hall ({shadowsOff} no longer cast sun shadows), {_outside.Count} outside. Sun: {(_sun != null ? _sun.name : "none")}.");
    }

    void Update()
    {
        if (!_ready) return;
        var cam = Camera.main;
        if (cam == null) { _wantInside = _wantOutside = true; return; }

        Vector3 eye = cam.transform.position;
        bool inHall = eye.x > Hall.min.x && eye.x < Hall.max.x && eye.z > Hall.min.z && eye.z < Hall.max.z && eye.y < Shell.max.y;
        Vector3 toDoor = Doorway.center - eye;
        toDoor.y = 0f;
        bool atDoor = toDoor.sqrMagnitude < NearDoor * NearDoor;

        // A wider frustum than the headset's, so nothing pops in at the edge of view.
        var proj = Matrix4x4.Perspective(120f, 1.3f, 0.05f, cam.farClipPlane);
        GeometryUtility.CalculateFrustumPlanes(proj * cam.worldToCameraMatrix, _planes);
        bool doorInView = atDoor || GeometryUtility.TestPlanesAABB(_planes, Doorway);

        // Showing is immediate; hiding waits a moment so a glance back and forth doesn't flicker work.
        float now = Time.unscaledTime;
        bool insideNeeded = inHall || doorInView, outsideNeeded = !inHall || doorInView;
        if (insideNeeded) { _wantInside = true; _insideOffAt = now + HideDelay; }
        else if (now >= _insideOffAt) _wantInside = false;
        if (outsideNeeded) { _wantOutside = true; _outsideOffAt = now + HideDelay; }
        else if (now >= _outsideOffAt) _wantOutside = false;
    }

    // Per camera, so the editor's Scene view always shows everything.
    void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        bool all = camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Preview;
        bool inside = all || _wantInside, outside = all || _wantOutside;
        if (inside != _shownInside) { Show(_inside, inside); _shownInside = inside; }
        if (outside != _shownOutside) { Show(_outside, outside); _shownOutside = outside; }
        if (_sun != null && outside != _sunShown) { _sun.enabled = outside; _sunShown = outside; }
    }

    static void Show(List<Renderer> renderers, bool show)
    {
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i] != null) renderers[i].forceRenderingOff = !show;
    }
}
