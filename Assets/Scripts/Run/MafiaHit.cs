using System.Collections;
using UnityEngine;

// The hit: when a run is lost, a man in a black suit and a fedora walks up
// to you, says his piece, raises a pistol and fires. CasinoRunHud plays this
// and blacks the screen out at the shot.
//
// The hitman is built from primitives when he is needed and removed again
// afterwards, so nothing has to be placed in the scene. He appears a few
// metres in front of wherever you are looking and walks to you - you are
// not moved, and you can back away, but he keeps coming.
public static class MafiaHit
{
    const float ArmThickness = 0.085f, LegThickness = 0.14f;

    // Runs the walk-up. When it finishes, the gun is pointing at the player
    // and it is time for the shot (Fire), then Dismiss.
    public static IEnumerator WalkUp(Transform player, float floorY, System.Action<string> say)
    {
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;

        Build(out var parts);
        _parts = parts;
        Vector3 position = player.position + forward * 4.5f;
        position.y = floorY;
        parts.Root.position = position;

        say?.Invoke("\"THE BOSS SENDS HIS REGARDS.\"");

        // Walk up to arm's length, wherever the player goes.
        float walked = 0f;
        for (float t = 0f; t < 6f; t += Time.unscaledDeltaTime)
        {
            Vector3 to = player.position - parts.Root.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance < 1.5f && t > 0.6f) break;
            Vector3 direction = distance > 0.001f ? to / distance : forward;
            float step = Mathf.Min(1.5f * Time.unscaledDeltaTime, Mathf.Max(0f, distance - 1.45f));
            parts.Root.position += direction * step;
            walked += step;
            parts.Root.rotation = Quaternion.LookRotation(direction, Vector3.up);
            Pose(parts, walked, 0f, player.position);
            yield return null;
        }

        say?.Invoke("\"NOTHING PERSONAL.\"");
        // Raise the gun.
        for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime)
        {
            Face(parts, player.position);
            Pose(parts, walked, Mathf.SmoothStep(0f, 1f, t / 0.7f), player.position);
            yield return null;
        }
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            Face(parts, player.position);
            Pose(parts, walked, 1f, player.position);
            yield return null;
        }
    }

    // The muzzle flash. Call at the same moment as the gunshot sound.
    public static void Fire()
    {
        if (_parts == null || _parts.Flash == null) return;
        _parts.Flash.gameObject.SetActive(true);
        var light = _parts.Flash.GetComponent<Light>();
        if (light != null) light.enabled = true;
    }

    public static void Dismiss()
    {
        if (_parts != null && _parts.Root != null) Object.Destroy(_parts.Root.gameObject);
        if (_parts != null)
            foreach (var material in _parts.Materials)
                if (material != null) Object.Destroy(material);
        _parts = null;
    }

    // -------------------------------------------------------------- figure

    class Parts
    {
        public Transform Root, Head, Torso, Gun, Flash;
        public readonly Transform[] UpperArms = new Transform[2], Forearms = new Transform[2], Hands = new Transform[2];
        public readonly Transform[] Legs = new Transform[2], Feet = new Transform[2];
        public Material[] Materials;
    }

    static Parts _parts;

    static void Build(out Parts parts)
    {
        Dismiss();
        parts = new Parts();
        var source = Resources.Load<Material>("Multiplayer/Avatar");
        var shader = source != null ? source.shader : Shader.Find("Universal Render Pipeline/Lit");
        Material Make(Color colour)
        {
            var material = source != null ? new Material(source) : new Material(shader);
            material.color = colour;
            return material;
        }
        var suit = Make(new Color(0.05f, 0.05f, 0.06f));
        var shirt = Make(new Color(0.92f, 0.92f, 0.9f));
        var skin = Make(new Color(0.86f, 0.68f, 0.55f));
        var tie = Make(new Color(0.6f, 0.05f, 0.07f));
        var metal = Make(new Color(0.2f, 0.2f, 0.22f));
        var flash = Make(new Color(1f, 0.85f, 0.4f));
        flash.EnableKeyword("_EMISSION");
        flash.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.3f) * 6f);
        parts.Materials = new[] { suit, shirt, skin, tie, metal, flash };

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

        // The pistol rides in the right hand; its barrel points along +Z.
        parts.Gun = new GameObject("Pistol").transform;
        parts.Gun.SetParent(r, false);
        Part("Barrel", PrimitiveType.Cube, parts.Gun, metal, new Vector3(0f, 0.03f, 0.09f), new Vector3(0.03f, 0.04f, 0.2f));
        Part("Grip", PrimitiveType.Cube, parts.Gun, metal, new Vector3(0f, -0.03f, 0.01f), new Vector3(0.03f, 0.1f, 0.045f));
        parts.Flash = Part("Muzzle Flash", PrimitiveType.Sphere, parts.Gun, flash, new Vector3(0f, 0.03f, 0.26f), new Vector3(0.16f, 0.16f, 0.22f));
        var light = parts.Flash.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.4f);
        light.intensity = 8f;
        light.range = 6f;
        parts.Flash.gameObject.SetActive(false);
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
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go.transform;
    }

    static void Face(Parts parts, Vector3 player)
    {
        Vector3 to = player - parts.Root.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.001f)
            parts.Root.rotation = Quaternion.Slerp(parts.Root.rotation, Quaternion.LookRotation(to, Vector3.up), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
    }

    // walked = metres covered (drives the stride), aim = 0 arm at his side .. 1 gun on the player.
    static void Pose(Parts parts, float walked, float aim, Vector3 player)
    {
        Transform r = parts.Root;
        Quaternion rotation = r.rotation;
        float stride = Mathf.Sin(walked * 4.2f);
        float moving = 1f - aim;

        // A little bob while walking; the head watches the player.
        parts.Torso.localPosition = new Vector3(0f, 1.16f + Mathf.Abs(stride) * 0.015f * moving, 0f);
        Vector3 look = player - parts.Head.position;
        if (look.sqrMagnitude > 0.01f)
            parts.Head.rotation = Quaternion.Slerp(parts.Head.rotation, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));

        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            Vector3 shoulder = r.TransformPoint(new Vector3(side * 0.27f, 1.38f, 0f));

            // Arms swing opposite to the legs; the right one comes up to aim.
            Vector3 rest = r.TransformPoint(new Vector3(side * 0.3f, 0.86f, -side * stride * 0.16f * moving + 0.04f));
            Vector3 hand = rest;
            if (i == 0)
            {
                Vector3 toward = player - shoulder;
                Vector3 aimPoint = shoulder + toward.normalized * 0.58f;
                hand = Vector3.Lerp(rest, aimPoint, aim);
                parts.Gun.position = hand;
                Quaternion down = rotation * Quaternion.Euler(80f, 0f, 0f);
                parts.Gun.rotation = Quaternion.Slerp(down, Quaternion.LookRotation(toward, Vector3.up), aim);
            }
            parts.Hands[i].position = hand;
            CharacterLimbs.PoseArm(parts.UpperArms[i], parts.Forearms[i], shoulder, hand, rotation, side, ArmThickness);

            Vector3 hip = r.TransformPoint(new Vector3(side * 0.1f, 0.84f, 0f));
            float swing = side * stride * 0.22f * moving;
            float lift = Mathf.Max(0f, side * Mathf.Cos(walked * 4.2f)) * 0.07f * moving;
            Vector3 foot = r.TransformPoint(new Vector3(side * 0.1f, 0.06f + lift, swing));
            CharacterLimbs.Stretch(parts.Legs[i], hip, foot, LegThickness);
            parts.Feet[i].SetPositionAndRotation(foot + rotation * new Vector3(0f, -0.02f, 0.05f), rotation);
        }
    }
}
