using System.Collections.Generic;
using Casino.Multiplayer;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;

// Where the mafia takes you when a run is lost: a dead-end canyon in the
// desert at sunset - red rock walls on three sides, cacti and boulders, the
// low sun at the open end - with a black car, a freshly dug pit and the whole
// team lined up in front of it. CasinoRunHud moves everyone here behind the
// hood (Enter) and back to where they were taken from at the end (Exit).
//
// The spot is far below and away from the casino, and while you're here the
// camera only sees Sight metres (hazy sunset fog before that), the sky is a
// sunset sky lit by its own low sun and the casino's sun is off, so nothing
// of the casino is drawn (or visible) - it really is somewhere else.
//
// Every headset builds the same spot and lines the players up in the same
// order (by player id), so each sees the others' real avatars in the line.
// When the hitman shoots someone else, their avatar is swapped for a body
// on the ground.
public static class KidnapScene
{
    public static readonly Vector3 Origin = new Vector3(-2.9f, -200f, 200f);
    const float Spacing = 1.5f;     // between the players in the line
    const float StandOff = 1.35f;   // where the hitman stands to shoot, in front of each
    const float Sight = 185f;       // camera range while here (the casino is ~280 m away)
    // Sunset: where the sun is (from the scene, toward the open end of the
    // canyon, a little to the right) and the colours of the light.
    static readonly Vector3 SunDirection = new Vector3(0.42f, 0.11f, 1f).normalized;
    static readonly Color SunColour = new Color(1f, 0.6f, 0.34f);
    static readonly Color SkyAmbient = new Color(0.55f, 0.47f, 0.55f);
    static readonly Color HorizonAmbient = new Color(0.72f, 0.47f, 0.33f);
    static readonly Color GroundAmbient = new Color(0.34f, 0.22f, 0.15f);
    static readonly Color Haze = new Color(0.93f, 0.6f, 0.42f);

    public static bool Active { get; private set; }
    // Everyone in the line, left to right as you face the car; and which one is you.
    public static IReadOnlyList<ulong> Order => _order;
    public static int LocalIndex { get; private set; }
    public static Vector3 HitmanStart => Origin + new Vector3(0f, 0f, 4.2f);
    public static Vector3 LineCentre => Origin + Vector3.up * 1.6f;

    static readonly List<ulong> _order = new List<ulong>();
    static readonly List<Material> _materials = new List<Material>();
    static readonly List<Mesh> _meshes = new List<Mesh>();
    static readonly List<SimpleAvatar> _hidden = new List<SimpleAvatar>();
    static GameObject _root;
    static Material _source;

    // What Enter changed, to put back.
    static bool _rigSaved, _nightSaved, _sunWasOn;
    static Vector3 _rigPosition;
    static Quaternion _rigRotation;
    static Material _skybox, _sunsetSky;
    static bool _ownSky;
    static AmbientMode _ambientMode;
    static Color _ambientLight, _ambientSky, _ambientEquator, _ambientGround;
    static SphericalHarmonicsL2 _ambientProbe;
    static bool _fog;
    static FogMode _fogMode;
    static Color _fogColour;
    static float _fogStart, _fogEnd, _fogDensity;
    static Light _renderSun, _sunset;
    static Camera _camera;
    static CameraClearFlags _clearFlags;
    static Color _background;
    static float _farClip;
    static Light _sun;

    // Play mode starts without a domain reload in this project: start clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = false;
        _rigSaved = _nightSaved = _ownSky = false;
        _sunsetSky = null;
        _sunset = null;
        _sun = null;
        _root = null;
        _camera = null;
        _order.Clear();
        _materials.Clear();
        _meshes.Clear();
        _hidden.Clear();
        _thump = _door = _engine = _crickets = null;
    }

    public static Vector3 Spot(int index)
    {
        int n = Mathf.Max(1, _order.Count);
        return Origin + Vector3.right * ((index - (n - 1) * 0.5f) * Spacing);
    }

    // ------------------------------------------------------------ in / out

    public static void Enter()
    {
        Exit();
        MakeOrder();
        Build();
        SetSunset(true);

        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin != null)
        {
            _rigPosition = origin.transform.position;
            _rigRotation = origin.transform.rotation;
            _rigSaved = true;
            WithoutController(origin, () =>
            {
                origin.MatchOriginUpCameraForward(Vector3.up, Vector3.forward);
                origin.MoveCameraToWorldLocation(Spot(LocalIndex) + Vector3.up * origin.CameraInOriginSpaceHeight);
            });
        }
        Active = true;
    }

    // Puts everything back (safe to call any time).
    public static void Exit()
    {
        if (_rigSaved)
        {
            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin != null) WithoutController(origin, () => origin.transform.SetPositionAndRotation(_rigPosition, _rigRotation));
            _rigSaved = false;
        }
        SetSunset(false);
        foreach (var avatar in _hidden)
            if (avatar != null) avatar.Hidden = false;
        _hidden.Clear();
        if (_root != null) Object.Destroy(_root);
        _root = null;
        foreach (var material in _materials)
            if (material != null) Object.Destroy(material);
        _materials.Clear();
        foreach (var mesh in _meshes)
            if (mesh != null) Object.Destroy(mesh);
        _meshes.Clear();
        Active = false;
    }

    // A CharacterController would drag the rig straight back.
    static void WithoutController(XROrigin origin, System.Action move)
    {
        var controller = origin.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        move();
        if (wasEnabled) controller.enabled = true;
    }

    // Everyone this headset can see, in player-id order (the same on every headset).
    static void MakeOrder()
    {
        _order.Clear();
        var mp = CasinoMultiplayer.Instance;
        ulong me = mp != null ? mp.LocalId : 0UL;
        _order.Add(me);
        if (mp != null && mp.InSession)
            foreach (ulong id in mp.RemoteIds)
            {
                var avatar = mp.RemoteAvatar(id);
                if (avatar != null && avatar.gameObject.activeSelf && !_order.Contains(id)) _order.Add(id);
            }
        _order.Sort();
        LocalIndex = _order.IndexOf(me);
    }

    // ------------------------------------------------------------ victims

    // Where someone's head is now (they may have shuffled about).
    public static Vector3 HeadOf(int index)
    {
        if (index == LocalIndex && Camera.main != null) return Camera.main.transform.position;
        var avatar = Avatar(index);
        if (avatar != null && avatar.gameObject.activeInHierarchy && avatar.Head != null) return avatar.Head.position;
        return Spot(index) + Vector3.up * 1.6f;
    }

    // Where the hitman stands to shoot them: a step in front, on the car's side.
    public static Vector3 StandPoint(int index)
    {
        Vector3 head = HeadOf(index);
        return new Vector3(head.x, Origin.y, Mathf.Max(head.z, Origin.z - 1f) + StandOff);
    }

    // Someone else was shot: their avatar makes way for a body on the ground.
    public static void Fell(int index)
    {
        if (index < 0 || index >= _order.Count || index == LocalIndex) return;
        Vector3 head = HeadOf(index);
        var avatar = Avatar(index);
        if (avatar != null)
        {
            avatar.Hidden = true;
            _hidden.Add(avatar);
        }
        Body(new Vector3(head.x, Origin.y, head.z), CasinoMultiplayer.ColorFor(_order[index]));
    }

    static SimpleAvatar Avatar(int index)
    {
        if (index < 0 || index >= _order.Count || index == LocalIndex) return null;
        var mp = CasinoMultiplayer.Instance;
        return mp != null ? mp.RemoteAvatar(_order[index]) : null;
    }

    // ------------------------------------------------------------ sunset

    // The sunset sky: Resources/Mafia Sunset Sky (made by Tools > Casino >
    // Mafia > Build Mafia Assets, so its shader is in the build); in the
    // editor it can be made on the spot, and failing both the sky is a flat
    // evening colour.
    static Material SunsetSky()
    {
        if (_sunsetSky != null) return _sunsetSky;
        _sunsetSky = Resources.Load<Material>("Mafia Sunset Sky");
        _ownSky = false;
        if (_sunsetSky == null)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            _sunsetSky = new Material(shader) { name = "Sunset Sky (runtime)" };
            _sunsetSky.SetFloat("_SunDisk", 2f);
            _sunsetSky.SetFloat("_SunSize", 0.06f);
            _sunsetSky.SetFloat("_SunSizeConvergence", 3f);
            _sunsetSky.SetFloat("_AtmosphereThickness", 2.1f);
            _sunsetSky.SetColor("_SkyTint", new Color(0.6f, 0.45f, 0.55f));
            _sunsetSky.SetColor("_GroundColor", new Color(0.55f, 0.36f, 0.26f));
            _sunsetSky.SetFloat("_Exposure", 1.25f);
            _ownSky = true;
        }
        return _sunsetSky;
    }

    static void SetSunset(bool on)
    {
        if (on && !_nightSaved)
        {
            _skybox = RenderSettings.skybox;
            _ambientMode = RenderSettings.ambientMode;
            _ambientLight = RenderSettings.ambientLight;
            _ambientSky = RenderSettings.ambientSkyColor;
            _ambientEquator = RenderSettings.ambientEquatorColor;
            _ambientGround = RenderSettings.ambientGroundColor;
            _ambientProbe = RenderSettings.ambientProbe;
            _fog = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColour = RenderSettings.fogColor;
            _fogStart = RenderSettings.fogStartDistance;
            _fogEnd = RenderSettings.fogEndDistance;
            _fogDensity = RenderSettings.fogDensity;
            _renderSun = RenderSettings.sun;

            // HallCulling owns the casino's sun; elsewhere switch it off here.
            HallCulling.ForceSunOff = true;
            _sun = Object.FindAnyObjectByType<HallCulling>() == null ? RenderSettings.sun : null;
            _sunWasOn = _sun != null && _sun.enabled;
            if (_sun != null) _sun.enabled = false;

            // The low evening sun at the open end of the canyon.
            _sunset = new GameObject("Sunset Sun").AddComponent<Light>();
            _sunset.transform.SetParent(_root != null ? _root.transform : null, false);
            _sunset.transform.rotation = Quaternion.LookRotation(-SunDirection, Vector3.up);
            _sunset.type = LightType.Directional;
            _sunset.color = SunColour;
            _sunset.intensity = 1.35f;
            _sunset.shadows = LightShadows.Soft;
            _sunset.shadowStrength = 0.7f;
            RenderSettings.sun = _sunset;

            var sky = SunsetSky();
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = SkyAmbient;
            RenderSettings.ambientEquatorColor = HorizonAmbient;
            RenderSettings.ambientGroundColor = GroundAmbient;
            RenderSettings.ambientLight = HorizonAmbient;
            // Unity only rebuilds the ambient probe when lighting is baked, so set it by hand:
            // warm all round, a little bluer from above, browner from below.
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(HorizonAmbient * 0.85f);
            probe.AddDirectionalLight(Vector3.up, (SkyAmbient - HorizonAmbient * 0.5f) * 0.6f, 1f);
            probe.AddDirectionalLight(Vector3.down, GroundAmbient * 0.25f, 1f);
            probe.AddDirectionalLight(SunDirection, SunColour * 0.25f, 1f);
            RenderSettings.ambientProbe = probe;

            // A warm haze, so the far end fades into the evening instead of stopping.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Haze;
            RenderSettings.fogStartDistance = 50f;
            RenderSettings.fogEndDistance = Sight + 5f;

            _camera = Camera.main;
            if (_camera != null)
            {
                _clearFlags = _camera.clearFlags;
                _background = _camera.backgroundColor;
                _farClip = _camera.farClipPlane;
                _camera.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                _camera.backgroundColor = Haze;
                _camera.farClipPlane = Mathf.Min(_farClip, Sight);
            }
            _nightSaved = true;
        }
        else if (!on && _nightSaved)
        {
            RenderSettings.skybox = _skybox;
            RenderSettings.fog = _fog;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColour;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
            RenderSettings.fogDensity = _fogDensity;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.ambientSkyColor = _ambientSky;
            RenderSettings.ambientEquatorColor = _ambientEquator;
            RenderSettings.ambientGroundColor = _ambientGround;
            RenderSettings.ambientProbe = _ambientProbe;
            RenderSettings.sun = _renderSun;
            if (_sunset != null) Object.Destroy(_sunset.gameObject);
            _sunset = null;
            if (_ownSky && _sunsetSky != null) Object.Destroy(_sunsetSky);
            if (_ownSky) _sunsetSky = null;
            if (_camera != null)
            {
                _camera.clearFlags = _clearFlags;
                _camera.backgroundColor = _background;
                _camera.farClipPlane = _farClip;
            }
            HallCulling.ForceSunOff = false;
            if (_sun != null) _sun.enabled = _sunWasOn;
            _sun = null;
            _nightSaved = false;
        }
    }

    // ------------------------------------------------------------ the spot

    static void Build()
    {
        _source = Resources.Load<Material>("Multiplayer/Avatar");
        _root = new GameObject("Kidnap Scene");
        var root = _root.transform;
        root.position = Origin;

        var sand = Make(new Color(0.5f, 0.38f, 0.24f));
        var dirt = Make(new Color(0.27f, 0.2f, 0.14f));
        var hole = Make(new Color(0.03f, 0.022f, 0.016f));
        var rock = Make(new Color(0.3f, 0.26f, 0.22f));
        var paint = Make(new Color(0.03f, 0.03f, 0.035f), 0.7f);
        var glass = Make(new Color(0.01f, 0.012f, 0.016f), 0.9f);
        var chrome = Make(new Color(0.55f, 0.55f, 0.58f), 0.8f);
        var tyre = Make(new Color(0.015f, 0.015f, 0.015f));
        var wood = Make(new Color(0.35f, 0.24f, 0.13f));
        var steel = Make(new Color(0.25f, 0.25f, 0.27f));
        var lamp = Make(new Color(1f, 0.95f, 0.8f));
        lamp.EnableKeyword("_EMISSION");
        lamp.SetColor("_EmissionColor", new Color(1f, 0.92f, 0.75f) * 4f);
        var tail = Make(new Color(0.6f, 0.02f, 0.02f));
        tail.EnableKeyword("_EMISSION");
        tail.SetColor("_EmissionColor", new Color(1f, 0.05f, 0.03f) * 1.5f);

        // Ground: sand, flat where everyone stands, rolling elsewhere, dunes past the canyon's mouth.
        MeshObject("Sand", root, FloorMesh(), new[] { sand }, false);
        var floor = _root.AddComponent<BoxCollider>();
        floor.center = new Vector3(0f, -0.5f, 85f);
        floor.size = new Vector3(150f, 1f, 230f);

        // The pit behind the line, its dirt heaped beside it, the shovel left in.
        int n = Mathf.Max(1, _order.Count);
        float width = Mathf.Max(2.4f, n * Spacing + 0.4f);
        Prim("Pit", PrimitiveType.Cube, root, hole, new Vector3(0f, 0.004f, -2.4f), new Vector3(width, 0.01f, 1.1f));
        Prim("Pit Edge", PrimitiveType.Cube, root, dirt, new Vector3(0f, 0.002f, -2.4f), new Vector3(width + 0.3f, 0.01f, 1.4f));
        Prim("Dirt Heap", PrimitiveType.Sphere, root, dirt, new Vector3(width * 0.5f + 1.1f, 0f, -2.5f), new Vector3(1.7f, 0.65f, 1.1f));
        var shovel = new GameObject("Shovel").transform;
        shovel.SetParent(root, false);
        shovel.localPosition = new Vector3(width * 0.5f + 1.0f, 0.2f, -2.4f);
        shovel.localRotation = Quaternion.Euler(-12f, 20f, 8f);
        Prim("Handle", PrimitiveType.Cylinder, shovel, wood, new Vector3(0f, 0.55f, 0f), new Vector3(0.035f, 0.55f, 0.035f));
        Prim("Blade", PrimitiveType.Cube, shovel, steel, new Vector3(0f, -0.05f, 0f), new Vector3(0.22f, 0.28f, 0.02f));

        // The car, nose toward the line, headlights on you.
        var car = new GameObject("Car").transform;
        car.SetParent(root, false);
        car.localPosition = new Vector3(0.4f, 0f, 8.2f);
        car.localRotation = Quaternion.Euler(0f, 186f, 0f);
        Prim("Body", PrimitiveType.Cube, car, paint, new Vector3(0f, 0.62f, 0f), new Vector3(1.85f, 0.56f, 4.7f), true);
        Prim("Cabin", PrimitiveType.Cube, car, paint, new Vector3(0f, 1.13f, -0.35f), new Vector3(1.62f, 0.48f, 2.2f), true);
        Prim("Windscreen", PrimitiveType.Cube, car, glass, new Vector3(0f, 1.12f, 0.76f), new Vector3(1.5f, 0.4f, 0.03f));
        Prim("Side Windows", PrimitiveType.Cube, car, glass, new Vector3(0f, 1.15f, -0.35f), new Vector3(1.64f, 0.3f, 1.9f));
        Prim("Bumper", PrimitiveType.Cube, car, chrome, new Vector3(0f, 0.42f, 2.37f), new Vector3(1.9f, 0.1f, 0.06f));
        Prim("Grille", PrimitiveType.Cube, car, chrome, new Vector3(0f, 0.66f, 2.355f), new Vector3(0.7f, 0.22f, 0.02f));
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * 0.9f, z = (i < 2 ? 1f : -1f) * 1.5f;
            Prim("Wheel", PrimitiveType.Cylinder, car, tyre, new Vector3(x, 0.34f, z), new Vector3(0.68f, 0.12f, 0.68f)).localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -0.66f : 0.66f;
            Prim("Headlight", PrimitiveType.Sphere, car, lamp, new Vector3(x, 0.68f, 2.34f), new Vector3(0.24f, 0.16f, 0.06f));
            Prim("Tail Light", PrimitiveType.Cube, car, tail, new Vector3(x, 0.72f, -2.36f), new Vector3(0.3f, 0.1f, 0.03f));

            var beam = new GameObject("Headlight Beam").transform;
            beam.SetParent(car, false);
            beam.localPosition = new Vector3(x, 0.7f, 2.45f);
            beam.rotation = Quaternion.LookRotation(Origin + new Vector3(x * 0.6f, 1.1f, 0f) - beam.position, Vector3.up);
            var spot = beam.gameObject.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 60f;
            spot.innerSpotAngle = 25f;
            spot.range = 24f;
            spot.intensity = 2f;
            spot.color = new Color(1f, 0.93f, 0.78f);
            spot.shadows = LightShadows.None;
        }

        BuildCanyon(root, rock);
    }

    // ------------------------------------------------------------ canyon
    // Everything is laid out the same on every headset (fixed seeds). The
    // line stands at z = 0 facing +z (the car, the open end, the sun); the
    // canyon is closed behind the pit at z = -9 and opens up past z = 50.

    const float MouthZ = 80f;

    static float Noise(float x, float y) => Mathf.PerlinNoise(x + 31.7f, y + 12.3f);

    // Half the canyon's width at z, on the left (-1) or right (+1) side.
    static float HalfWidth(float z, float side)
    {
        float wobble = (Noise(z * 0.07f, side * 5f) - 0.5f) * 3.5f;
        return 8f + Mathf.Max(0f, z - 8f) * 0.2f + wobble;
    }

    // Nothing gets in the way of the line, the pit, the hitman's walk or the car.
    static bool KeepClear(Vector3 p) => Mathf.Abs(p.x) < 5f && p.z > -6.5f && p.z < 13f;

    // Inside the canyon, at least `margin` from its walls.
    static bool InCanyon(Vector3 p, float margin)
    {
        if (p.z > MouthZ + 10f) return true;
        if (p.z < -4f) return p.x * p.x / 49f + (p.z + 4f) * (p.z + 4f) / 25f < 1f - margin * 0.12f;
        return Mathf.Abs(p.x) < HalfWidth(p.z, Mathf.Sign(p.x)) - margin;
    }

    static void BuildCanyon(Transform root, Material rockMaterial)
    {
        var bands = new[]
        {
            Make(new Color(0.71f, 0.37f, 0.22f)),
            Make(new Color(0.62f, 0.3f, 0.19f)),
            Make(new Color(0.78f, 0.47f, 0.29f)),
            Make(new Color(0.66f, 0.34f, 0.24f)),
        };

        // ---- The walls: one ribbon of rock from the left side's far end, round
        // the dead end behind the pit and back up the right side.
        // At the mouth the walls turn outward, so their ends aren't seen edge-on.
        var path = new List<Vector3>();
        for (int k = 5; k >= 1; k--) path.Add(new Vector3(-HalfWidth(MouthZ, -1f) - k * 5f, 0f, MouthZ + k * 1.5f));
        for (float z = MouthZ; z > -4f; z -= 2.5f) path.Add(new Vector3(-HalfWidth(z, -1f), 0f, z));
        float backLeft = HalfWidth(-4f, -1f), backRight = HalfWidth(-4f, 1f);
        for (int i = 1; i < 12; i++)
        {
            float a = Mathf.PI + Mathf.PI * i / 12f;
            float rx = Mathf.Cos(a) < 0f ? backLeft : backRight;
            path.Add(new Vector3(Mathf.Cos(a) * rx, 0f, -4f + Mathf.Sin(a) * 5.5f));
        }
        for (float z = -4f; z <= MouthZ; z += 2.5f) path.Add(new Vector3(HalfWidth(z, 1f), 0f, z));
        for (int k = 1; k <= 5; k++) path.Add(new Vector3(HalfWidth(MouthZ, 1f) + k * 5f, 0f, MouthZ + k * 1.5f));

        var away = new Vector3[path.Count];
        var heights = new float[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 tangent = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)];
            away[i] = Vector3.Cross(Vector3.up, tangent).normalized; // to the walker's right = out of the canyon
            float open = Mathf.Clamp01((path[i].z - 30f) / (MouthZ + 8f - 30f));
            heights[i] = Mathf.Lerp(19f, 5f, open * open) + (Noise(i * 0.21f, 2f) - 0.5f) * 7f * (1f - open * 0.6f);
        }
        var canyon = MeshObject("Canyon Walls", root, CliffMesh(path, away, heights, 10, true, bands.Length, 0f), bands, true);
        canyon.AddComponent<MeshCollider>().sharedMesh = canyon.GetComponent<MeshFilter>().sharedMesh;

        // ---- Mesas on the horizon, through the canyon's mouth (left of the sun, and right).
        Mesa(root, bands, new Vector3(-42f, 0f, 135f), 22f, 13f, 34f, 1f);
        Mesa(root, bands, new Vector3(-8f, 0f, 172f), 30f, 10f, 22f, 2f);
        Mesa(root, bands, new Vector3(70f, 0f, 150f), 18f, 14f, 40f, 3f);
        Mesa(root, bands, new Vector3(40f, 0f, 185f), 26f, 9f, 18f, 4f);

        // ---- Rocks: fallen boulders along the foot of the walls, a few big ones
        // out on the floor, pebbles everywhere.
        var rockMeshes = new Mesh[6];
        for (int i = 0; i < rockMeshes.Length; i++) rockMeshes[i] = RockMesh(i);
        var rockTones = new[] { rockMaterial, bands[1], bands[3] };
        var random = new System.Random(4321);
        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);
        Transform decor = new GameObject("Decor").transform;
        decor.SetParent(root, false);
        void Rock(Vector3 p, float size, float squash)
        {
            if (KeepClear(p)) return;
            var t = MeshObject("Rock", decor, rockMeshes[random.Next(rockMeshes.Length)], new[] { rockTones[random.Next(rockTones.Length)] }, size > 0.5f).transform;
            t.localPosition = p + Vector3.down * size * 0.25f;
            t.localRotation = Quaternion.Euler(Range(-15f, 15f), Range(0f, 360f), Range(-15f, 15f));
            t.localScale = new Vector3(size * Range(0.9f, 1.4f), size * squash, size * Range(0.9f, 1.3f));
        }
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].z > MouthZ - 5f) continue;
            int count = random.Next(0, 3);
            for (int k = 0; k < count; k++)
                Rock(path[i] - away[i] * Range(0.3f, 2.6f) + new Vector3(Range(-1f, 1f), 0f, Range(-1f, 1f)), Range(0.5f, 2.2f), Range(0.55f, 0.9f));
        }
        for (int i = 0; i < 18; i++)
        {
            var p = new Vector3(Range(-45f, 45f), 0f, Range(20f, 150f));
            if (p.z < MouthZ && !InCanyon(p, 2f)) continue;
            Rock(p, Range(1f, 3.5f), Range(0.5f, 0.85f));
        }
        for (int i = 0; i < 70; i++)
        {
            var p = new Vector3(Range(-14f, 14f), 0f, Range(-8f, 60f));
            if (!InCanyon(p, 0.5f)) continue;
            Rock(p, Range(0.12f, 0.4f), Range(0.5f, 0.8f));
        }

        // ---- Cacti and scrub.
        var green = Make(new Color(0.33f, 0.47f, 0.26f));
        var darkGreen = Make(new Color(0.25f, 0.38f, 0.21f));
        var paleGreen = Make(new Color(0.45f, 0.55f, 0.3f));
        var flower = Make(new Color(0.95f, 0.35f, 0.45f));
        var yellow = Make(new Color(0.95f, 0.75f, 0.25f));
        var scrub = Make(new Color(0.5f, 0.45f, 0.27f));
        var deadScrub = Make(new Color(0.55f, 0.42f, 0.28f));

        // Saguaros: a few along the canyon walls, more out past the mouth against the sky.
        var saguaros = new[]
        {
            new Vector3(-6.2f, 0f, 3.5f), new Vector3(6.8f, 0f, 9f), new Vector3(-7.5f, 0f, 19f), new Vector3(8.5f, 0f, 27f),
            new Vector3(-10f, 0f, 38f), new Vector3(3f, 0f, 46f), new Vector3(-4f, 0f, 63f), new Vector3(14f, 0f, 58f),
            new Vector3(-20f, 0f, 92f), new Vector3(12f, 0f, 97f), new Vector3(-6f, 0f, 108f), new Vector3(26f, 0f, 112f),
            new Vector3(-32f, 0f, 118f), new Vector3(4f, 0f, 125f),
        };
        for (int i = 0; i < saguaros.Length; i++)
        {
            Vector3 p = saguaros[i];
            if (p.z < MouthZ && !InCanyon(p, 0.6f)) p.x *= 0.8f;
            Saguaro(decor, p, Range(3.2f, 5.5f), i % 3 == 2 ? 1 : random.Next(2, 4), random, i % 2 == 0 ? green : darkGreen, flower);
        }

        for (int i = 0; i < 26; i++)
        {
            var p = new Vector3(Range(-16f, 16f), 0f, Range(-7f, 70f));
            if (KeepClear(p) || !InCanyon(p, 0.8f)) continue;
            switch (i % 3)
            {
                case 0: Barrel(decor, p, Range(0.35f, 0.6f), darkGreen, i % 2 == 0 ? yellow : flower); break;
                case 1: PricklyPear(decor, p, Range(0.7f, 1.1f), random, paleGreen, flower); break;
                default: Bush(decor, p, Range(0.5f, 1f), random, i % 2 == 0 ? scrub : deadScrub); break;
            }
        }
        for (int i = 0; i < 22; i++)
        {
            var p = new Vector3(Range(-40f, 40f), 0f, Range(MouthZ - 10f, 140f));
            if (p.z < MouthZ && !InCanyon(p, 1f)) continue;
            if (i % 2 == 0) Bush(decor, p, Range(0.6f, 1.2f), random, i % 4 == 0 ? scrub : deadScrub);
            else Barrel(decor, p, Range(0.4f, 0.7f), darkGreen, yellow);
        }

        // Fewer draw calls: everything that doesn't move becomes one batch per material.
        StaticBatchingUtility.Combine(decor.gameObject);
    }

    // Flat-topped table mountain on the horizon.
    static void Mesa(Transform root, Material[] bands, Vector3 centre, float radiusX, float radiusZ, float height, float seed)
    {
        var path = new List<Vector3>();
        var away = new List<Vector3>();
        var heights = new List<float>();
        const int sides = 18;
        for (int i = 0; i <= sides; i++)
        {
            float a = -Mathf.PI * 2f * i / sides;
            float wobble = 1f + (Noise(i * 0.4f, seed * 7f) - 0.5f) * 0.35f;
            var p = centre + new Vector3(Mathf.Cos(a) * radiusX * wobble, 0f, Mathf.Sin(a) * radiusZ * wobble);
            path.Add(p);
            away.Add((centre - p).normalized);
            heights.Add(height * (0.92f + Noise(i * 0.3f, seed) * 0.16f));
        }
        var mesa = MeshObject("Mesa", root, CliffMesh(path, away.ToArray(), heights.ToArray(), 6, false, bands.Length, seed), bands, false);
        // Its top, so it's solid against the sky from any angle.
        var top = new Mesh { name = "Mesa Top" };
        var verts = new List<Vector3> { centre + Vector3.up * height * 0.98f };
        var tris = new List<int>();
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 lean = away[i] * heights[i] * 0.25f;
            verts.Add(path[i] + lean + Vector3.up * heights[i] * 0.98f);
            if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
        }
        top.SetVertices(verts);
        top.SetTriangles(tris, 0);
        top.RecalculateNormals();
        if (Vector3.Dot(top.normals[0], Vector3.up) < 0f) { tris.Reverse(); top.SetTriangles(tris, 0); top.RecalculateNormals(); }
        top.RecalculateBounds();
        MeshObject("Mesa Top", mesa.transform, top, new[] { bands[2] }, false);
    }

    // A wall of layered rock standing on `path`, leaning back (along `away`)
    // as it rises in terraces, faceted, its face toward the viewer (-away).
    // Each triangle takes one of `bandCount` sub-meshes by height: strata.
    static Mesh CliffMesh(List<Vector3> path, Vector3[] away, float[] heights, int rows, bool jaggedTop, int bandCount, float seed)
    {
        int columns = path.Count;
        var grid = new Vector3[columns, rows + 1];
        for (int c = 0; c < columns; c++)
            for (int r = 0; r <= rows; r++)
            {
                float t = (float)r / rows;
                float y = r == 0 ? -0.6f : heights[c] * t;
                if (r == rows && jaggedTop) y += (Noise(c * 0.9f, seed + 9f) - 0.5f) * 5f;
                float terrace = Mathf.Floor(y / 3.4f) * 0.7f;
                float rough = (Noise(c * 0.45f + seed, y * 0.3f) - 0.5f) * 2.4f;
                float bulge = (Noise(c * 0.12f, seed + 3f) - 0.5f) * 3f;
                float offset = r == 0 ? -0.4f : y * 0.16f + terrace + rough + bulge * Mathf.Min(1f, t * 2f);
                grid[c, r] = path[c] + away[c] * offset + Vector3.up * y;
            }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var bands = new List<int>[bandCount];
        for (int b = 0; b < bandCount; b++) bands[b] = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, facing) < 0f) { (b, c) = (c, b); n = -n; }
            n.Normalize();
            float h = (a.y + b.y + c.y) / 3f;
            int band = Mathf.Abs(Mathf.FloorToInt(h / 2.7f + Noise(a.x * 0.05f, a.z * 0.05f) * 1.5f)) % bandCount;
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            bands[band].Add(start); bands[band].Add(start + 1); bands[band].Add(start + 2);
        }
        for (int c = 0; c < columns - 1; c++)
        {
            Vector3 facing = -(away[c] + away[c + 1]);
            for (int r = 0; r < rows; r++)
            {
                Vector3 p00 = grid[c, r], p10 = grid[c + 1, r], p01 = grid[c, r + 1], p11 = grid[c + 1, r + 1];
                Tri(p00, p01, p11, facing);
                Tri(p00, p11, p10, facing);
            }
        }

        var mesh = new Mesh { name = "Cliff" };
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = bandCount;
        for (int b = 0; b < bandCount; b++) mesh.SetTriangles(bands[b], b);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh FloorMesh()
    {
        const float x0 = -75f, x1 = 75f, z0 = -30f, z1 = 200f, cell = 3f;
        int nx = Mathf.RoundToInt((x1 - x0) / cell) + 1, nz = Mathf.RoundToInt((z1 - z0) / cell) + 1;
        var vertices = new Vector3[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float x = x0 + i * cell, z = z0 + j * cell;
                // Flat around the line, the pit and the car; gentle ripples elsewhere, dunes out past the mouth.
                float dx = Mathf.Max(0f, Mathf.Abs(x) - 6f), dz = Mathf.Max(0f, Mathf.Max(z - 14f, -7f - z));
                float rough = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / 10f);
                float y = rough * (Noise(x * 0.06f, z * 0.06f) - 0.5f) * 1.2f;
                float dunes = Mathf.Clamp01((z - MouthZ) / 40f);
                y += dunes * (Mathf.Sin(x * 0.07f + z * 0.045f) * 0.5f + 0.5f) * Noise(x * 0.02f, z * 0.02f) * 4f;
                vertices[j * nx + i] = new Vector3(x, y, z);
            }
        var tris = new int[(nx - 1) * (nz - 1) * 6];
        int k = 0;
        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }
        var mesh = new Mesh { name = "Desert Floor" };
        mesh.vertices = vertices;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // A faceted boulder: an icosahedron, split once, pushed in and out.
    static Mesh RockMesh(int seed)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        var mid = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (mid.TryGetValue(key, out int i)) return i;
            v.Add((v[a] + v[b]) * 0.5f);
            return mid[key] = v.Count - 1;
        }
        var faces = new List<int>();
        for (int i = 0; i < f.Length; i += 3)
        {
            int a = f[i], b = f[i + 1], c = f[i + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            faces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 n = v[i].normalized;
            float bump = 0.75f + Noise(n.x * 1.7f + seed * 3.1f, n.y * 1.7f + n.z * 1.3f) * 0.5f;
            Vector3 p = n * bump * 0.5f;
            if (p.y < -0.15f) p.y = -0.15f + (p.y + 0.15f) * 0.3f; // flatter underneath
            v[i] = p;
        }
        var vertices = new Vector3[faces.Count];
        var tris = new int[faces.Count];
        for (int i = 0; i < faces.Count; i++) { vertices[i] = v[faces[i]]; tris[i] = i; }
        var mesh = new Mesh { name = "Rock" };
        mesh.vertices = vertices;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        // Facing out, whichever way round the faces were wound.
        var normals = mesh.normals;
        if (Vector3.Dot(normals[0], vertices[0] + vertices[1] + vertices[2]) < 0f)
        {
            System.Array.Reverse(tris);
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    // A saguaro: a tall ribless trunk and a few arms that bend up.
    static void Saguaro(Transform parent, Vector3 feet, float height, int arms, System.Random random, Material skin, Material bloom)
    {
        var cactus = new GameObject("Saguaro").transform;
        cactus.SetParent(parent, false);
        cactus.localPosition = feet;
        cactus.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
        float r = 0.2f + height * 0.03f;
        Prim("Trunk", PrimitiveType.Capsule, cactus, skin, new Vector3(0f, height * 0.5f, 0f), new Vector3(r * 2f, height * 0.5f, r * 2f), true);
        for (int i = 0; i < arms; i++)
        {
            float a = i * (360f / arms) + (float)random.NextDouble() * 50f;
            float at = height * (0.35f + (float)random.NextDouble() * 0.25f);
            float outLength = 0.45f + (float)random.NextDouble() * 0.3f;
            float upLength = height * (0.25f + (float)random.NextDouble() * 0.2f);
            var arm = new GameObject("Arm").transform;
            arm.SetParent(cactus, false);
            arm.localPosition = new Vector3(0f, at, 0f);
            arm.localRotation = Quaternion.Euler(0f, a, 0f);
            float ar = r * 0.75f;
            Prim("Elbow", PrimitiveType.Capsule, arm, skin, new Vector3(0f, 0.05f, outLength * 0.5f), new Vector3(ar * 2f, outLength * 0.5f + ar, ar * 2f), true)
                .localRotation = Quaternion.Euler(70f, 0f, 0f);
            Prim("Up", PrimitiveType.Capsule, arm, skin, new Vector3(0f, upLength * 0.5f + 0.15f, outLength), new Vector3(ar * 2f, upLength * 0.5f, ar * 2f), true);
        }
        if (random.NextDouble() < 0.4)
            Prim("Bloom", PrimitiveType.Sphere, cactus, bloom, new Vector3(0f, height + r * 0.6f, 0f), Vector3.one * r * 0.7f);
    }

    static void Barrel(Transform parent, Vector3 feet, float size, Material skin, Material bloom)
    {
        var cactus = new GameObject("Barrel Cactus").transform;
        cactus.SetParent(parent, false);
        cactus.localPosition = feet;
        Prim("Body", PrimitiveType.Sphere, cactus, skin, new Vector3(0f, size * 0.45f, 0f), new Vector3(size, size * 1.2f, size), true);
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f;
            Prim("Flower", PrimitiveType.Sphere, cactus, bloom, new Vector3(Mathf.Cos(a) * size * 0.12f, size * 1.03f, Mathf.Sin(a) * size * 0.12f), Vector3.one * size * 0.16f);
        }
    }

    static void PricklyPear(Transform parent, Vector3 feet, float size, System.Random random, Material skin, Material bloom)
    {
        var cactus = new GameObject("Prickly Pear").transform;
        cactus.SetParent(parent, false);
        cactus.localPosition = feet;
        cactus.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
        var pads = new[] { new Vector3(0f, 0.25f, 0f), new Vector3(0.22f, 0.55f, 0.02f), new Vector3(-0.2f, 0.5f, 0.05f), new Vector3(0.05f, 0.82f, -0.03f), new Vector3(0.38f, 0.28f, 0.1f) };
        for (int i = 0; i < pads.Length; i++)
        {
            var pad = Prim("Pad", PrimitiveType.Sphere, cactus, skin, pads[i] * size, new Vector3(0.36f, 0.44f, 0.09f) * size, true);
            pad.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 60f - 30f, (float)random.NextDouble() * 50f - 25f);
            if (i >= 3) Prim("Fruit", PrimitiveType.Sphere, cactus, bloom, pads[i] * size + Vector3.up * 0.22f * size, Vector3.one * 0.08f * size);
        }
    }

    static void Bush(Transform parent, Vector3 feet, float size, System.Random random, Material leaves)
    {
        var bush = new GameObject("Bush").transform;
        bush.SetParent(parent, false);
        bush.localPosition = feet;
        int blobs = 3 + random.Next(3);
        for (int i = 0; i < blobs; i++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f, d = (float)random.NextDouble() * 0.35f * size;
            float s = size * (0.45f + (float)random.NextDouble() * 0.35f);
            Prim("Leaves", PrimitiveType.Sphere, bush, leaves, new Vector3(Mathf.Cos(a) * d, s * 0.3f, Mathf.Sin(a) * d), new Vector3(s, s * 0.7f, s), false);
        }
    }

    static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material[] materials, bool castShadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _meshes.Add(mesh);
        return go;
    }

    // Someone who was shot, fallen backwards toward the pit.
    static void Body(Vector3 feet, Color jacket)
    {
        if (_root == null) return;
        var body = new GameObject("Body").transform;
        body.SetParent(_root.transform, false);
        body.position = feet;
        var coat = Make(jacket);
        var skin = Make(new Color(0.93f, 0.76f, 0.63f));
        var dark = Make(new Color(0.07f, 0.07f, 0.09f));
        var blood = Make(new Color(0.25f, 0.01f, 0.01f));
        var lying = Quaternion.Euler(90f, 0f, 0f);
        Prim("Torso", PrimitiveType.Capsule, body, coat, new Vector3(0f, 0.13f, -0.45f), new Vector3(0.36f, 0.3f, 0.23f)).localRotation = lying;
        Prim("Head", PrimitiveType.Sphere, body, skin, new Vector3(0.03f, 0.12f, -0.98f), new Vector3(0.21f, 0.22f, 0.25f));
        Prim("Cap", PrimitiveType.Sphere, body, coat, new Vector3(0.03f, 0.13f, -1.04f), new Vector3(0.22f, 0.2f, 0.2f));
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Prim("Leg", PrimitiveType.Capsule, body, dark, new Vector3(side * 0.1f + side * 0.05f, 0.08f, 0.22f), new Vector3(0.13f, 0.4f, 0.13f))
                .localRotation = Quaternion.Euler(90f, 0f, side * -8f);
            Prim("Arm", PrimitiveType.Capsule, body, coat, new Vector3(side * 0.36f, 0.06f, -0.55f), new Vector3(0.08f, 0.3f, 0.08f))
                .localRotation = Quaternion.Euler(90f, side * 25f, 0f);
        }
        Prim("Blood", PrimitiveType.Cylinder, body, blood, new Vector3(0.05f, 0.006f, -0.95f), new Vector3(0.75f, 0.004f, 0.6f));
    }

    static Material Make(Color colour, float smoothness = 0f)
    {
        var material = _source != null ? new Material(_source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = colour;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        _materials.Add(material);
        return material;
    }

    static Transform Prim(string name, PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale, bool shadows = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var collider = go.GetComponent<Collider>();
        collider.enabled = false;
        Object.Destroy(collider);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go.transform;
    }

    // ------------------------------------------------------------ sounds
    // Made in code, like the rest of the run's sounds.

    static AudioClip _thump, _door, _engine, _crickets;
    public static AudioClip Thump => _thump != null ? _thump : (_thump = MakeThump());
    public static AudioClip DoorSlam => _door != null ? _door : (_door = MakeDoor());
    public static AudioClip Engine => _engine != null ? _engine : (_engine = MakeEngine());
    public static AudioClip Crickets => _crickets != null ? _crickets : (_crickets = MakeCrickets());

    const int Rate = 44100;

    static AudioClip Clip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // The hood going on: a dull, close thump.
    static AudioClip MakeThump()
    {
        var data = new float[(int)(Rate * 0.4f)];
        var random = new System.Random(3);
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / Rate;
            low += (((float)random.NextDouble() * 2f - 1f) - low) * 0.06f;
            data[i] = Mathf.Clamp(Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 16f) * 0.9f + low * Mathf.Exp(-t * 20f) * 2.5f, -1f, 1f);
        }
        return Clip("Hood", data);
    }

    // A car door slamming, heard through the hood.
    static AudioClip MakeDoor()
    {
        var data = new float[(int)(Rate * 0.6f)];
        var random = new System.Random(5);
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / Rate;
            low += (((float)random.NextDouble() * 2f - 1f) - low) * 0.1f;
            float body = Mathf.Sin(2f * Mathf.PI * 58f * t) * Mathf.Exp(-t * 11f);
            float rattle = Mathf.Sin(2f * Mathf.PI * 410f * t) * Mathf.Exp(-t * 35f) * 0.25f;
            data[i] = Mathf.Clamp(body + rattle + low * Mathf.Exp(-t * 28f) * 2f, -1f, 1f) * 0.9f;
        }
        return Clip("Car Door", data);
    }

    // An old engine pulling away and driving for a while, muffled.
    static AudioClip MakeEngine()
    {
        const float seconds = 5f;
        var data = new float[(int)(Rate * seconds)];
        var random = new System.Random(11);
        float phase = 0f, low = 0f, smooth = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / Rate;
            float revs = 30f + 14f * Mathf.SmoothStep(0f, 1f, t / 1.5f) + 3f * Mathf.Sin(t * 1.3f);
            phase += revs / Rate;
            float saw = (phase % 1f) * 2f - 1f;
            low += (((float)random.NextDouble() * 2f - 1f) - low) * 0.03f;
            float raw = saw * 0.6f + Mathf.Sin(2f * Mathf.PI * phase * 2f) * 0.3f + low * 1.2f;
            smooth += (raw - smooth) * 0.07f;
            float envelope = Mathf.Clamp01(t / 0.5f) * Mathf.Clamp01((seconds - t) / 1.5f);
            data[i] = Mathf.Clamp(smooth * envelope * 1.4f, -1f, 1f);
        }
        return Clip("Engine", data);
    }

    // A loop of crickets for the desert at night.
    static AudioClip MakeCrickets()
    {
        const float seconds = 3f;
        var data = new float[(int)(Rate * seconds)];
        // (period, carrier Hz, offset, loudness) - every period divides the loop.
        var crickets = new[] { (0.75f, 4300f, 0.3f, 0.12f), (1f, 4700f, 0.5f, 0.07f), (0.6f, 3900f, 0.25f, 0.05f) };
        foreach (var (period, carrier, offset, loudness) in crickets)
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float inPeriod = (t + offset) % period;
                // Three quick pulses at the start of each period.
                float pulse = inPeriod < 0.13f && (inPeriod % 0.045f) < 0.028f ? Mathf.Sin(Mathf.PI * (inPeriod % 0.045f) / 0.028f) : 0f;
                data[i] += Mathf.Sin(2f * Mathf.PI * carrier * t) * pulse * loudness;
            }
        return Clip("Crickets", data);
    }
}
