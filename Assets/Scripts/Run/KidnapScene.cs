using System.Collections.Generic;
using Casino.Multiplayer;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;

// Where the mafia takes you when a run is lost: a patch of desert at night,
// a black car with its headlights on, a freshly dug pit - and the whole team
// lined up in front of it. CasinoRunHud moves everyone here behind the hood
// (Enter) and back to where they were taken from at the end (Exit).
//
// The spot is far below and away from the casino, and while you're here the
// camera only sees 60 m, the sky is black and the sun is off, so nothing of
// the casino is drawn (or visible) - it really is somewhere else.
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
    const float Sight = 60f;        // camera range while here
    static readonly Color Night = new Color(0.045f, 0.05f, 0.075f);
    static readonly Color Sky = new Color(0.006f, 0.008f, 0.016f);

    public static bool Active { get; private set; }
    // Everyone in the line, left to right as you face the car; and which one is you.
    public static IReadOnlyList<ulong> Order => _order;
    public static int LocalIndex { get; private set; }
    public static Vector3 HitmanStart => Origin + new Vector3(0f, 0f, 4.2f);
    public static Vector3 LineCentre => Origin + Vector3.up * 1.6f;

    static readonly List<ulong> _order = new List<ulong>();
    static readonly List<Material> _materials = new List<Material>();
    static readonly List<SimpleAvatar> _hidden = new List<SimpleAvatar>();
    static GameObject _root;
    static Material _source;

    // What Enter changed, to put back.
    static bool _rigSaved, _nightSaved, _sunWasOn;
    static Vector3 _rigPosition;
    static Quaternion _rigRotation;
    static Material _skybox;
    static AmbientMode _ambientMode;
    static Color _ambientLight;
    static SphericalHarmonicsL2 _ambientProbe;
    static bool _fog;
    static Camera _camera;
    static CameraClearFlags _clearFlags;
    static Color _background;
    static float _farClip;
    static Light _sun;

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
        SetNight(true);

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
        SetNight(false);
        foreach (var avatar in _hidden)
            if (avatar != null) avatar.Hidden = false;
        _hidden.Clear();
        if (_root != null) Object.Destroy(_root);
        _root = null;
        foreach (var material in _materials)
            if (material != null) Object.Destroy(material);
        _materials.Clear();
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

    // ------------------------------------------------------------ night

    static void SetNight(bool on)
    {
        if (on && !_nightSaved)
        {
            _skybox = RenderSettings.skybox;
            _ambientMode = RenderSettings.ambientMode;
            _ambientLight = RenderSettings.ambientLight;
            _ambientProbe = RenderSettings.ambientProbe;
            _fog = RenderSettings.fog;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Night;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(Night);
            RenderSettings.ambientProbe = probe;

            _camera = Camera.main;
            if (_camera != null)
            {
                _clearFlags = _camera.clearFlags;
                _background = _camera.backgroundColor;
                _farClip = _camera.farClipPlane;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Sky;
                _camera.farClipPlane = Mathf.Min(_farClip, Sight);
            }

            // HallCulling owns the sun in the casino; elsewhere switch it off here.
            HallCulling.ForceSunOff = true;
            _sun = Object.FindAnyObjectByType<HallCulling>() == null ? RenderSettings.sun : null;
            _sunWasOn = _sun != null && _sun.enabled;
            if (_sun != null) _sun.enabled = false;
            _nightSaved = true;
        }
        else if (!on && _nightSaved)
        {
            RenderSettings.skybox = _skybox;
            RenderSettings.fog = _fog;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.ambientProbe = _ambientProbe;
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

        // Ground: a wide disc of sand (it fades into the dark long before its edge).
        Prim("Sand", PrimitiveType.Cylinder, root, sand, new Vector3(0f, -0.1f, 0f), new Vector3(70f, 0.1f, 70f));
        var floor = _root.AddComponent<BoxCollider>();
        floor.center = new Vector3(0f, -0.5f, 0f);
        floor.size = new Vector3(80f, 1f, 80f);

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
        Prim("Body", PrimitiveType.Cube, car, paint, new Vector3(0f, 0.62f, 0f), new Vector3(1.85f, 0.56f, 4.7f));
        Prim("Cabin", PrimitiveType.Cube, car, paint, new Vector3(0f, 1.13f, -0.35f), new Vector3(1.62f, 0.48f, 2.2f));
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
            spot.intensity = 3.5f;
            spot.color = new Color(1f, 0.93f, 0.78f);
            spot.shadows = LightShadows.None;
        }

        // A little moonlight, so the dark has shapes in it.
        var moon = new GameObject("Moon").AddComponent<Light>();
        moon.transform.SetParent(root, false);
        moon.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
        moon.type = LightType.Directional;
        moon.intensity = 0.07f;
        moon.color = new Color(0.6f, 0.68f, 0.95f);
        moon.shadows = LightShadows.None;

        // Rocks scattered about, the same every time.
        var random = new System.Random(1234);
        for (int i = 0; i < 14; i++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float d = 6f + (float)random.NextDouble() * 18f;
            var p = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            if (Mathf.Abs(p.x) < 4f && p.z > -4f && p.z < 11f) continue; // keep the line and the car clear
            float s = 0.3f + (float)random.NextDouble() * (i % 4 == 0 ? 1.6f : 0.6f);
            Prim("Rock", PrimitiveType.Sphere, root, rock, p, new Vector3(s * 1.3f, s * 0.7f, s))
                .localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
        }
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

    static Transform Prim(string name, PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale)
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
        renderer.shadowCastingMode = ShadowCastingMode.Off;
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
