using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// The hitman: a rigged man in a black suit, fedora and sunglasses (the
// Resources/Mafia Hitman prefab, made by Tools > Casino > Mafia > Build Mafia
// Assets from the same character pack as the guests), spawned when he is
// needed and removed again afterwards, so nothing has to be placed in the
// scene. If the prefab is missing he falls back to a figure built from
// primitives. CasinoRunHud directs him when a run is lost (see KidnapScene):
//   Approach  - appears a few metres in front of wherever you are looking
//               and walks up to you (you are not moved; if you back away he
//               keeps coming),
//   WalkTo    - walks to a point, head turned toward someone,
//   Act       - eases his arms between at his sides, the pistol raised
//               (aim) and both hands up at someone's face holding a hood
//               (reach), turning to face them,
//   Fire      - the muzzle flash, at the same moment as the bang.
public static class MafiaHit
{
    const float ArmThickness = 0.085f, LegThickness = 0.14f;
    const float WalkSpeed = 1.4f;

    class Parts
    {
        public Transform Root, Head, Torso, Gun, Flash, Hood;
        public MafiaHitmanRig Rig; // set for the rigged look; the limb arrays are then unused
        public readonly Transform[] UpperArms = new Transform[2], Forearms = new Transform[2], Hands = new Transform[2];
        public readonly Transform[] Legs = new Transform[2], Feet = new Transform[2];
        public Material[] Materials;
    }

    static Parts _parts;
    static float _walked, _aim, _reach;
    static GameObject _prefab;
    static bool _prefabLoaded;

    public static bool Present => _parts != null && _parts.Root != null;

    // Play mode starts without a domain reload in this project, so forget
    // the last session's objects (the prefab may have been rebuilt since).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _parts = null;
        _prefab = null;
        _prefabLoaded = false;
        _source = null;
    }

    // Puts him on the floor at `feet`, facing `toward`.
    public static void Spawn(Vector3 feet, Vector3 toward, bool gun)
    {
        Dismiss();
        _parts = BuildRigged() ?? BuildPrimitive();
        _walked = _aim = _reach = 0f;
        _parts.Root.position = feet;
        Vector3 to = toward - feet;
        to.y = 0f;
        if (to.sqrMagnitude > 0.001f) _parts.Root.rotation = Quaternion.LookRotation(to, Vector3.up);
        _parts.Gun.gameObject.SetActive(gun);
        Pose(toward);
    }

    // Appears 4.5 m in front of where the player is looking and walks up to arm's length.
    public static IEnumerator Approach(Transform player, float floorY)
    {
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
        Vector3 feet = player.position + forward * 4.5f;
        feet.y = floorY;
        Spawn(feet, player.position, false);
        yield return WalkTo(() => player.position, 1.45f, 6f, () => player.position);
    }

    // Walks until he is `stop` metres (across the floor) from target(), or
    // for at most maxSeconds, his head turned toward look().
    public static IEnumerator WalkTo(Func<Vector3> target, float stop, float maxSeconds, Func<Vector3> look)
    {
        for (float t = 0f; t < maxSeconds && Present; t += Time.unscaledDeltaTime)
        {
            Vector3 to = target() - _parts.Root.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance <= stop + 0.01f) break;
            Vector3 direction = to / distance;
            float step = Mathf.Min(WalkSpeed * Time.unscaledDeltaTime, distance - stop);
            _parts.Root.position += direction * step;
            _walked += step;
            _parts.Root.rotation = Quaternion.Slerp(_parts.Root.rotation, Quaternion.LookRotation(direction, Vector3.up),
                1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            SetWalking(true);
            Pose(look());
            yield return null;
        }
        SetWalking(false);
    }

    static void SetWalking(bool on)
    {
        if (Present && _parts.Rig != null) _parts.Rig.Walking = on;
    }

    // Over `seconds`, turns to face look() and eases the arms to the given
    // aim (0 at his side .. 1 pistol on them) and reach (0 .. 1 hood over their head).
    public static IEnumerator Act(Func<Vector3> look, float seconds, float aim, float reach)
    {
        float aim0 = _aim, reach0 = _reach;
        for (float t = 0f; t < seconds && Present; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            _aim = Mathf.Lerp(aim0, aim, k);
            _reach = Mathf.Lerp(reach0, reach, k);
            Face(look());
            Pose(look());
            yield return null;
        }
        _aim = aim;
        _reach = reach;
        if (Present) Pose(look());
    }

    // The muzzle flash. Call at the moment of the bang, and StopFlash a
    // moment later if he is going to shoot again.
    public static void Fire() => SetFlash(true);

    // Plays a voice line from his head, in 3D.
    public static void Speak(AudioClip clip, float volume = 1f)
    {
        if (!Present || clip == null) return;
        var source = _parts.Head.GetComponent<AudioSource>();
        if (source == null) source = CharacterVoice.Create(_parts.Head, 20f / 3f);
        CharacterVoice.Play(source, clip, volume);
    }
    public static void StopFlash() => SetFlash(false);

    static void SetFlash(bool on)
    {
        if (!Present || _parts.Flash == null) return;
        _parts.Flash.gameObject.SetActive(on);
        var light = _parts.Flash.GetComponent<Light>();
        if (light != null) light.enabled = on;
    }

    public static void Dismiss()
    {
        if (_parts != null && _parts.Root != null) Object.Destroy(_parts.Root.gameObject);
        if (_parts != null && _parts.Materials != null)
            foreach (var material in _parts.Materials)
                if (material != null) Object.Destroy(material);
        _parts = null;
    }

    // -------------------------------------------------------------- figure

    // Out in the desert the baked light probes (all in the casino) are wrong,
    // so he is lit by the scene's ambient light there instead.
    static LightProbeUsage Probes => KidnapScene.Active ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;

    // The character from the pack, with a pistol and a hood made here.
    static Parts BuildRigged()
    {
        if (!_prefabLoaded)
        {
            _prefabLoaded = true;
            _prefab = Resources.Load<GameObject>("Mafia Hitman");
            if (_prefab == null) Debug.LogWarning("MafiaHit: Resources/Mafia Hitman missing - run Tools > Casino > Mafia > Build Mafia Assets. Using the primitive hitman.");
        }
        if (_prefab == null) return null;

        var go = Object.Instantiate(_prefab);
        go.name = "Mafia Hitman";
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.lightProbeUsage = Probes;
            r.shadowCastingMode = KidnapScene.Active ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        SkinnedBounds.Encompass(go.transform, 2.1f, 1.2f);
        var rig = go.GetComponent<MafiaHitmanRig>();
        if (rig == null) rig = go.AddComponent<MafiaHitmanRig>();
        if (!rig.Init())
        {
            Debug.LogWarning("MafiaHit: the Mafia Hitman prefab has no humanoid animator/controller - using the primitive hitman.");
            Object.Destroy(go);
            return null;
        }

        var parts = new Parts { Root = go.transform, Rig = rig, Head = rig.Head, Torso = go.transform };
        var metal = MakeMaterial(new Color(0.09f, 0.09f, 0.1f), 0.75f);
        var wood = MakeMaterial(new Color(0.22f, 0.12f, 0.07f), 0.4f);
        var sack = MakeMaterial(new Color(0.36f, 0.27f, 0.17f), 0f);
        var flash = MakeFlashMaterial();
        parts.Materials = new[] { metal, wood, sack, flash };
        BuildPistol(parts, go.transform, metal, wood, flash);
        parts.Hood = Part("Hood", PrimitiveType.Sphere, go.transform, sack, Vector3.zero, new Vector3(0.3f, 0.36f, 0.3f));
        parts.Hood.gameObject.SetActive(false);
        rig.Gun = parts.Gun;
        rig.Hood = parts.Hood;
        rig.WalkSpeed = WalkSpeed;
        return parts;
    }

    static Material _source;
    static Material MakeMaterial(Color colour, float smoothness)
    {
        if (_source == null) _source = Resources.Load<Material>("Multiplayer/Avatar");
        var material = _source != null ? new Material(_source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = colour;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    static Material MakeFlashMaterial()
    {
        var flash = MakeMaterial(new Color(1f, 0.85f, 0.4f), 0f);
        flash.EnableKeyword("_EMISSION");
        flash.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.3f) * 6f);
        return flash;
    }

    // A snub revolver-ish pistol; its barrel points along +Z from the grip.
    static void BuildPistol(Parts parts, Transform parent, Material metal, Material grip, Material flash)
    {
        parts.Gun = new GameObject("Pistol").transform;
        parts.Gun.SetParent(parent, false);
        Part("Slide", PrimitiveType.Cube, parts.Gun, metal, new Vector3(0f, 0.045f, 0.07f), new Vector3(0.028f, 0.032f, 0.17f));
        Part("Frame", PrimitiveType.Cube, parts.Gun, metal, new Vector3(0f, 0.022f, 0.06f), new Vector3(0.025f, 0.02f, 0.14f));
        Part("Muzzle", PrimitiveType.Cylinder, parts.Gun, metal, new Vector3(0f, 0.045f, 0.16f), new Vector3(0.016f, 0.01f, 0.016f))
            .localRotation = Quaternion.Euler(90f, 0f, 0f);
        // Raked back like a real grip; MafiaHitmanRig closes the hand round it.
        Part("Grip", PrimitiveType.Cube, parts.Gun, grip, MafiaHitmanRig.GripCentre, new Vector3(0.027f, 0.09f, 0.04f))
            .localRotation = Quaternion.Euler(MafiaHitmanRig.GripRake, 0f, 0f);
        Part("Trigger Guard", PrimitiveType.Cube, parts.Gun, metal, new Vector3(0f, -0.002f, 0.04f), new Vector3(0.008f, 0.03f, 0.035f));
        parts.Flash = Part("Muzzle Flash", PrimitiveType.Sphere, parts.Gun, flash, new Vector3(0f, 0.045f, 0.24f), new Vector3(0.14f, 0.14f, 0.2f));
        var light = parts.Flash.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.4f);
        light.intensity = 8f;
        light.range = 6f;
        parts.Flash.gameObject.SetActive(false);
    }

    // The old look, built from primitives (only if the prefab is missing).
    static Parts BuildPrimitive()
    {
        var parts = new Parts();
        var suit = MakeMaterial(new Color(0.05f, 0.05f, 0.06f), 0f);
        var shirt = MakeMaterial(new Color(0.92f, 0.92f, 0.9f), 0f);
        var skin = MakeMaterial(new Color(0.86f, 0.68f, 0.55f), 0f);
        var tie = MakeMaterial(new Color(0.6f, 0.05f, 0.07f), 0f);
        var metal = MakeMaterial(new Color(0.2f, 0.2f, 0.22f), 0.6f);
        var sack = MakeMaterial(new Color(0.32f, 0.24f, 0.15f), 0f);
        var flash = MakeFlashMaterial();
        parts.Materials = new[] { suit, shirt, skin, tie, metal, sack, flash };

        parts.Root = new GameObject("Mafia Hitman").transform;
        var r = parts.Root;

        parts.Torso = Part("Jacket", PrimitiveType.Capsule, r, suit, new Vector3(0f, 1.16f, 0f), new Vector3(0.46f, 0.33f, 0.3f));
        Part("Shirt", PrimitiveType.Sphere, r, shirt, new Vector3(0f, 1.3f, 0.1f), new Vector3(0.14f, 0.3f, 0.12f));
        Part("Tie", PrimitiveType.Cube, r, tie, new Vector3(0f, 1.27f, 0.155f), new Vector3(0.045f, 0.24f, 0.02f));
        Part("Hips", PrimitiveType.Sphere, r, suit, new Vector3(0f, 0.86f, 0f), new Vector3(0.4f, 0.24f, 0.28f));

        parts.Head = new GameObject("Head").transform;
        parts.Head.SetParent(r, false);
        parts.Head.localPosition = new Vector3(0f, 1.5f, 0f);
        Part("Face", PrimitiveType.Sphere, parts.Head, skin, new Vector3(0f, 0.14f, 0f), new Vector3(0.21f, 0.25f, 0.22f));
        Part("Shades", PrimitiveType.Cube, parts.Head, suit, new Vector3(0f, 0.17f, 0.095f), new Vector3(0.19f, 0.04f, 0.04f));
        Part("Hat Brim", PrimitiveType.Cylinder, parts.Head, suit, new Vector3(0f, 0.235f, 0f), new Vector3(0.36f, 0.008f, 0.36f));
        Part("Hat Crown", PrimitiveType.Cylinder, parts.Head, suit, new Vector3(0f, 0.3f, 0f), new Vector3(0.21f, 0.06f, 0.21f));
        Part("Hat Band", PrimitiveType.Cylinder, parts.Head, tie, new Vector3(0f, 0.255f, 0f), new Vector3(0.215f, 0.014f, 0.215f));

        for (int i = 0; i < 2; i++)
        {
            string n = i == 0 ? "Right" : "Left";
            parts.UpperArms[i] = Part(n + " Upper Arm", PrimitiveType.Capsule, r, suit, Vector3.zero, Vector3.one * ArmThickness);
            parts.Forearms[i] = Part(n + " Forearm", PrimitiveType.Capsule, r, suit, Vector3.zero, Vector3.one * ArmThickness);
            parts.Hands[i] = Part(n + " Hand", PrimitiveType.Sphere, r, skin, Vector3.zero, Vector3.one * 0.09f);
            parts.Legs[i] = Part(n + " Leg", PrimitiveType.Capsule, r, suit, Vector3.zero, Vector3.one * LegThickness);
            parts.Feet[i] = Part(n + " Shoe", PrimitiveType.Cube, r, suit, Vector3.zero, new Vector3(0.1f, 0.06f, 0.26f));
        }

        BuildPistol(parts, r, metal, metal, flash);
        parts.Hood = Part("Hood", PrimitiveType.Sphere, r, sack, Vector3.zero, new Vector3(0.3f, 0.34f, 0.3f));
        parts.Hood.gameObject.SetActive(false);
        return parts;
    }

    static Transform Part(string name, PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale)
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
        renderer.lightProbeUsage = Probes;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    static void Face(Vector3 target)
    {
        Vector3 to = target - _parts.Root.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.001f)
            _parts.Root.rotation = Quaternion.Slerp(_parts.Root.rotation, Quaternion.LookRotation(to, Vector3.up), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
    }

    // Poses the figure from the stride (metres walked), _aim and _reach;
    // `target` is the head he is looking / aiming / reaching at.
    static void Pose(Vector3 target)
    {
        var parts = _parts;
        if (parts == null || parts.Root == null) return;
        if (parts.Rig != null)
        {
            // The rig poses itself after the animation (LateUpdate).
            parts.Rig.Target = target;
            parts.Rig.Aim = _aim;
            parts.Rig.Reach = _reach;
            return;
        }

        Transform r = parts.Root;
        Quaternion rotation = r.rotation;
        float stride = Mathf.Sin(_walked * 4.2f);
        float moving = 1f - Mathf.Max(_aim, _reach);

        // A little bob while walking; the head watches the target.
        parts.Torso.localPosition = new Vector3(0f, 1.16f + Mathf.Abs(stride) * 0.015f * moving, 0f);
        Vector3 look = target - parts.Head.position;
        if (look.sqrMagnitude > 0.01f)
            parts.Head.rotation = Quaternion.Slerp(parts.Head.rotation, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));

        Vector3 handsMid = Vector3.zero;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            Vector3 shoulder = r.TransformPoint(new Vector3(side * 0.27f, 1.38f, 0f));

            // Arms swing opposite to the legs; the right one comes up to aim,
            // or both come up to either side of the target's face.
            Vector3 rest = r.TransformPoint(new Vector3(side * 0.3f, 0.86f, -side * stride * 0.16f * moving + 0.04f));
            Vector3 hand = rest;
            if (i == 0)
            {
                Vector3 toward = target - shoulder;
                Vector3 aimPoint = shoulder + toward.normalized * 0.58f;
                hand = Vector3.Lerp(rest, aimPoint, _aim);
                parts.Gun.position = hand;
                Quaternion down = rotation * Quaternion.Euler(80f, 0f, 0f);
                parts.Gun.rotation = Quaternion.Slerp(down, Quaternion.LookRotation(toward, Vector3.up), _aim);
            }
            Vector3 atFace = target + rotation * new Vector3(side * 0.14f, -0.02f, -0.13f);
            hand = Vector3.Lerp(hand, atFace, _reach);
            handsMid += hand * 0.5f;
            parts.Hands[i].position = hand;
            CharacterLimbs.PoseArm(parts.UpperArms[i], parts.Forearms[i], shoulder, hand, rotation, side, ArmThickness);

            Vector3 hip = r.TransformPoint(new Vector3(side * 0.1f, 0.84f, 0f));
            float swing = side * stride * 0.22f * moving;
            float lift = Mathf.Max(0f, side * Mathf.Cos(_walked * 4.2f)) * 0.07f * moving;
            Vector3 foot = r.TransformPoint(new Vector3(side * 0.1f, 0.06f + lift, swing));
            CharacterLimbs.Stretch(parts.Legs[i], hip, foot, LegThickness);
            parts.Feet[i].SetPositionAndRotation(foot + rotation * new Vector3(0f, -0.02f, 0.05f), rotation);
        }

        bool hood = _reach > 0.05f;
        if (parts.Hood.gameObject.activeSelf != hood) parts.Hood.gameObject.SetActive(hood);
        if (hood) parts.Hood.SetPositionAndRotation(handsMid + Vector3.up * 0.05f, rotation);
    }
}
