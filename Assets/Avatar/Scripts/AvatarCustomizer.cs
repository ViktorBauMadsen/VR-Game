using System;
using System.Collections.Generic;
using UnityEngine;

// Lets the player change how their character looks: hat, hair style and
// colour, facial hair, glasses, jacket colour, neckwear and skin tone.
// The MirrorWardrobe panel next to the mirror cycles through the options;
// the choices are remembered between sessions (PlayerPrefs).
//
// Added automatically to the "Player Avatar" by PlayerAvatarBody. It takes
// over the parts the avatar was built with (replacing the fixed fedora, tie
// and sideburns with switchable ones), builds every option out of the same
// smooth primitives, and just shows the selected one in each category.
public class AvatarCustomizer : MonoBehaviour
{
    class Category
    {
        public string Name;
        public string[] Options;
        public Action<int> Apply;
        public int Selected;
    }

    readonly List<Category> _categories = new List<Category>();
    Transform _head, _body;
    Material _skin, _hair, _jacket, _dark, _gold, _red, _lens;
    int _layer;
    bool _handsTinted;
    float _nextHandSearch;

    public event Action Changed;
    public int CategoryCount => _categories.Count;
    public string CategoryName(int i) => _categories[i].Name;
    public string SelectedName(int i) => _categories[i].Options[_categories[i].Selected];

    public void Cycle(int category, int direction)
    {
        var c = _categories[category];
        c.Selected = (c.Selected + direction + c.Options.Length) % c.Options.Length;
        c.Apply(c.Selected);
        PlayerPrefs.SetInt("Avatar." + c.Name, c.Selected);
        Changed?.Invoke();
    }

    void Awake()
    {
        _layer = gameObject.layer;
        _head = transform.Find("Head");
        _body = transform.Find("Body");
        if (_head == null || _body == null)
        {
            Debug.LogWarning("AvatarCustomizer: 'Player Avatar' has no Head/Body - rebuild it with Tools > Avatar > Build.");
            enabled = false;
            return;
        }

        // Parts the avatar was built with that are now switchable options.
        foreach (var n in new[] { "Fedora", "Sideburn L", "Sideburn R" }) Remove(_head, n);
        foreach (var n in new[] { "Tie", "Tie Knot" }) Remove(_body, n);

        // Own copies of the materials, so colours can change without
        // touching the assets (or the croupier, who shares some of them).
        _skin = Copy(_head, "Skull", new Color(0.9f, 0.68f, 0.55f));
        _hair = Copy(_head, "Hair", new Color(0.36f, 0.2f, 0.08f));
        _jacket = Copy(_body, "Jacket", new Color(0.08f, 0.13f, 0.32f));
        _dark = Copy(_head, "Eye L", new Color(0.04f, 0.04f, 0.05f));
        _gold = Copy(_body, "Button 1", new Color(0.9f, 0.68f, 0.2f));
        _red = new Material(_dark) { color = new Color(0.7f, 0.06f, 0.08f) };
        _lens = new Material(_dark) { color = new Color(0.02f, 0.02f, 0.03f) };
        _lens.SetFloat("_Smoothness", 0.95f);

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            string n = r.name;
            if (n == "Skull" || n.StartsWith("Ear") || n == "Nose" || n == "Neck") r.sharedMaterial = _skin;
            else if (n == "Hair" || n.StartsWith("Brow")) r.sharedMaterial = _hair;
            else if (n == "Jacket" || n == "Joint" || n.EndsWith("Upper Arm") || n.EndsWith("Forearm")) r.sharedMaterial = _jacket;
        }

        BuildCategories();
        foreach (var c in _categories)
        {
            c.Selected = Mathf.Clamp(PlayerPrefs.GetInt("Avatar." + c.Name, c.Selected), 0, c.Options.Length - 1);
            c.Apply(c.Selected);
        }
    }

    // The hands are separate objects (so you can see them), tinted to match.
    void Update()
    {
        if (_handsTinted || Time.unscaledTime < _nextHandSearch) return;
        _nextHandSearch = Time.unscaledTime + 0.5f;
        var hands = FindObjectsByType<HandPoser>();
        if (hands.Length < 2) return;
        foreach (var hand in hands)
            foreach (var r in hand.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = _skin;
        _handsTinted = true;
    }

    // --------------------------------------------------------- categories

    void BuildCategories()
    {
        // ----- Hat
        var fedora = Group("Fedora", _head, new Vector3(0f, 0.245f, -0.01f));
        fedora.localRotation = Quaternion.Euler(-6f, 0f, 0f);
        Part(PrimitiveType.Sphere, fedora, _dark, Vector3.zero, new Vector3(0.38f, 0.022f, 0.4f));
        Part(PrimitiveType.Capsule, fedora, _dark, new Vector3(0f, 0.05f, 0f), new Vector3(0.255f, 0.105f, 0.265f));
        Part(PrimitiveType.Cylinder, fedora, _gold, new Vector3(0f, 0.025f, 0f), new Vector3(0.262f, 0.014f, 0.272f));

        var topHat = Group("Top Hat", _head, new Vector3(0f, 0.245f, -0.01f));
        topHat.localRotation = Quaternion.Euler(-6f, 0f, 0f);
        Part(PrimitiveType.Cylinder, topHat, _dark, Vector3.zero, new Vector3(0.38f, 0.008f, 0.38f));
        Part(PrimitiveType.Cylinder, topHat, _dark, new Vector3(0f, 0.12f, 0f), new Vector3(0.25f, 0.12f, 0.25f));
        Part(PrimitiveType.Cylinder, topHat, _red, new Vector3(0f, 0.03f, 0f), new Vector3(0.256f, 0.02f, 0.256f));

        var tan = new Material(_dark) { color = new Color(0.62f, 0.44f, 0.24f) };
        var cowboy = Group("Cowboy Hat", _head, new Vector3(0f, 0.245f, -0.01f));
        cowboy.localRotation = Quaternion.Euler(-6f, 0f, 0f);
        Part(PrimitiveType.Sphere, cowboy, tan, Vector3.zero, new Vector3(0.52f, 0.03f, 0.44f));
        Part(PrimitiveType.Sphere, cowboy, tan, new Vector3(-0.22f, 0.025f, 0f), new Vector3(0.12f, 0.06f, 0.36f));
        Part(PrimitiveType.Sphere, cowboy, tan, new Vector3(0.22f, 0.025f, 0f), new Vector3(0.12f, 0.06f, 0.36f));
        Part(PrimitiveType.Capsule, cowboy, tan, new Vector3(0f, 0.055f, 0f), new Vector3(0.25f, 0.11f, 0.27f));
        Part(PrimitiveType.Cylinder, cowboy, _dark, new Vector3(0f, 0.025f, 0f), new Vector3(0.258f, 0.012f, 0.278f));

        var cap = Group("Cap", _head, Vector3.zero);
        Part(PrimitiveType.Sphere, cap, _red, new Vector3(0f, 0.2f, -0.012f), new Vector3(0.285f, 0.215f, 0.285f));
        Part(PrimitiveType.Sphere, cap, _red, new Vector3(0f, 0.205f, 0.16f), new Vector3(0.21f, 0.02f, 0.2f));
        Part(PrimitiveType.Sphere, cap, _dark, new Vector3(0f, 0.31f, -0.012f), Vector3.one * 0.03f);

        var crown = Group("Crown", _head, new Vector3(0f, 0.3f, -0.01f));
        Part(PrimitiveType.Cylinder, crown, _gold, Vector3.zero, new Vector3(0.23f, 0.035f, 0.23f));
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            var at = new Vector3(Mathf.Sin(a) * 0.105f, 0.05f, Mathf.Cos(a) * 0.105f);
            Part(PrimitiveType.Sphere, crown, _gold, at, new Vector3(0.04f, 0.07f, 0.04f));
            Part(PrimitiveType.Sphere, crown, _red, at + Vector3.up * 0.04f, Vector3.one * 0.022f);
        }

        var wool = new Material(_dark) { color = new Color(0.15f, 0.4f, 0.55f) };
        var beanie = Group("Beanie", _head, Vector3.zero);
        Part(PrimitiveType.Sphere, beanie, wool, new Vector3(0f, 0.21f, -0.012f), new Vector3(0.29f, 0.23f, 0.29f));
        Part(PrimitiveType.Cylinder, beanie, wool, new Vector3(0f, 0.2f, -0.012f), new Vector3(0.295f, 0.022f, 0.295f));
        Part(PrimitiveType.Sphere, beanie, new Material(_dark) { color = Color.white }, new Vector3(0f, 0.335f, -0.012f), Vector3.one * 0.07f);

        Add("Hat", 1, new[] { "None", "Fedora", "Top Hat", "Cowboy", "Cap", "Crown", "Beanie" },
            new[] { null, fedora, topHat, cowboy, cap, crown, beanie });

        // ----- Hair style (the built-in "Hair" sphere is the short cut)
        var shortHair = _head.Find("Hair");
        var longHair = Group("Long Hair", _head, Vector3.zero);
        Part(PrimitiveType.Sphere, longHair, _hair, new Vector3(0f, 0.2f, -0.022f), new Vector3(0.272f, 0.2f, 0.265f));
        Part(PrimitiveType.Sphere, longHair, _hair, new Vector3(0f, 0.09f, -0.075f), new Vector3(0.27f, 0.3f, 0.17f));
        var mohawk = Group("Mohawk", _head, Vector3.zero);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Lerp(-55f, 95f, i / 5f) * Mathf.Deg2Rad; // forehead, over the top, down the back
            Part(PrimitiveType.Sphere, mohawk, _hair, new Vector3(0f, 0.14f + Mathf.Cos(a) * 0.15f, -Mathf.Sin(a) * 0.14f), new Vector3(0.045f, 0.1f, 0.09f));
        }
        var afro = Group("Afro", _head, Vector3.zero);
        Part(PrimitiveType.Sphere, afro, _hair, new Vector3(0f, 0.22f, -0.03f), new Vector3(0.37f, 0.3f, 0.35f));
        var quiff = Group("Quiff", _head, Vector3.zero);
        Part(PrimitiveType.Sphere, quiff, _hair, new Vector3(0f, 0.2f, -0.022f), new Vector3(0.272f, 0.2f, 0.265f));
        Part(PrimitiveType.Sphere, quiff, _hair, new Vector3(0f, 0.27f, 0.07f), new Vector3(0.17f, 0.1f, 0.13f));

        Add("Hair", 0, new[] { "Short", "Quiff", "Long", "Afro", "Mohawk", "Bald" },
            new[] { shortHair, quiff, longHair, afro, mohawk, null });

        AddColours("Hair Colour", _hair, 0,
            ("Brown", new Color(0.36f, 0.2f, 0.08f)), ("Black", new Color(0.05f, 0.04f, 0.04f)),
            ("Blond", new Color(0.9f, 0.75f, 0.35f)), ("Ginger", new Color(0.75f, 0.32f, 0.08f)),
            ("Grey", new Color(0.6f, 0.6f, 0.62f)), ("Pink", new Color(0.95f, 0.35f, 0.65f)),
            ("Blue", new Color(0.15f, 0.45f, 0.9f)));

        // ----- Facial hair
        var sideburns = Group("Sideburns", _head, Vector3.zero);
        Part(PrimitiveType.Sphere, sideburns, _hair, new Vector3(-0.118f, 0.12f, 0.04f), new Vector3(0.03f, 0.09f, 0.045f));
        Part(PrimitiveType.Sphere, sideburns, _hair, new Vector3(0.118f, 0.12f, 0.04f), new Vector3(0.03f, 0.09f, 0.045f));
        var mustache = Mustache("Mustache");
        var goatee = Group("Goatee", _head, Vector3.zero);
        Part(PrimitiveType.Sphere, goatee, _hair, new Vector3(0f, 0.03f, 0.1f), new Vector3(0.06f, 0.06f, 0.05f));
        var vanDyke = Mustache("Mustache and Goatee");
        Part(PrimitiveType.Sphere, vanDyke, _hair, new Vector3(0f, -0.065f, -0.025f), new Vector3(0.06f, 0.06f, 0.05f));
        var beard = Mustache("Full Beard");
        Part(PrimitiveType.Sphere, beard, _hair, new Vector3(0f, -0.02f, -0.105f), new Vector3(0.25f, 0.17f, 0.24f));

        Add("Beard", 1, new[] { "None", "Sideburns", "Mustache", "Goatee", "Mustache + Goatee", "Full Beard" },
            new[] { null, sideburns, mustache, goatee, vanDyke, beard });

        // ----- Glasses
        var shades = Group("Shades", _head, new Vector3(0f, 0.16f, 0.122f));
        Part(PrimitiveType.Sphere, shades, _lens, new Vector3(-0.047f, 0f, 0f), new Vector3(0.07f, 0.05f, 0.025f));
        Part(PrimitiveType.Sphere, shades, _lens, new Vector3(0.047f, 0f, 0f), new Vector3(0.07f, 0.05f, 0.025f));
        Part(PrimitiveType.Cube, shades, _lens, new Vector3(0f, 0.008f, 0f), new Vector3(0.04f, 0.008f, 0.01f));
        Part(PrimitiveType.Cube, shades, _lens, new Vector3(-0.105f, 0.005f, -0.06f), new Vector3(0.006f, 0.008f, 0.13f));
        Part(PrimitiveType.Cube, shades, _lens, new Vector3(0.105f, 0.005f, -0.06f), new Vector3(0.006f, 0.008f, 0.13f));

        var aviators = Group("Aviators", _head, new Vector3(0f, 0.16f, 0.122f));
        foreach (float x in new[] { -0.047f, 0.047f })
        {
            Part(PrimitiveType.Sphere, aviators, _gold, new Vector3(x, -0.004f, -0.002f), new Vector3(0.08f, 0.07f, 0.02f));
            Part(PrimitiveType.Sphere, aviators, _lens, new Vector3(x, -0.004f, 0.003f), new Vector3(0.068f, 0.058f, 0.02f));
        }
        Part(PrimitiveType.Cube, aviators, _gold, new Vector3(0f, 0.012f, 0f), new Vector3(0.04f, 0.006f, 0.008f));

        var monocle = Group("Monocle", _head, new Vector3(0.047f, 0.16f, 0.124f));
        Part(PrimitiveType.Sphere, monocle, _gold, Vector3.zero, new Vector3(0.068f, 0.068f, 0.014f));
        Part(PrimitiveType.Sphere, monocle, new Material(_lens) { color = new Color(0.75f, 0.88f, 0.95f) }, new Vector3(0f, 0f, 0.003f), new Vector3(0.054f, 0.054f, 0.014f));
        Part(PrimitiveType.Sphere, monocle, _dark, new Vector3(0f, 0f, 0.008f), new Vector3(0.022f, 0.03f, 0.01f));
        Part(PrimitiveType.Cylinder, monocle, _gold, new Vector3(0.03f, -0.1f, -0.02f), new Vector3(0.004f, 0.08f, 0.004f));

        Add("Glasses", 0, new[] { "None", "Shades", "Aviators", "Monocle" },
            new[] { null, shades, aviators, monocle });

        // ----- Clothes
        AddColours("Jacket", _jacket, 0,
            ("Navy", new Color(0.08f, 0.13f, 0.32f)), ("Black Tux", new Color(0.04f, 0.04f, 0.05f)),
            ("Burgundy", new Color(0.4f, 0.05f, 0.1f)), ("Emerald", new Color(0.04f, 0.33f, 0.18f)),
            ("Royal Purple", new Color(0.3f, 0.1f, 0.45f)), ("White", new Color(0.93f, 0.93f, 0.9f)),
            ("Gold", new Color(0.85f, 0.62f, 0.15f)), ("Hot Pink", new Color(0.9f, 0.2f, 0.5f)));

        var tieMaterial = new Material(_gold);
        Transform Tie(string n)
        {
            var g = Group(n, _body, Vector3.zero);
            Part(PrimitiveType.Sphere, g, tieMaterial, new Vector3(0f, 1.41f, 0.135f), new Vector3(0.045f, 0.04f, 0.035f));
            Part(PrimitiveType.Sphere, g, tieMaterial, new Vector3(0f, 1.27f, 0.15f), new Vector3(0.055f, 0.27f, 0.04f));
            return g;
        }
        Transform Bow(string n, Material m)
        {
            var g = Group(n, _body, new Vector3(0f, 1.415f, 0.13f));
            Part(PrimitiveType.Sphere, g, m, new Vector3(-0.04f, 0f, 0f), new Vector3(0.075f, 0.05f, 0.03f));
            Part(PrimitiveType.Sphere, g, m, new Vector3(0.04f, 0f, 0f), new Vector3(0.075f, 0.05f, 0.03f));
            Part(PrimitiveType.Sphere, g, m, new Vector3(0f, 0f, 0.008f), Vector3.one * 0.032f);
            return g;
        }
        var tie = Tie("Tie");
        var blackBow = Bow("Black Bow Tie", _dark);
        var redBow = Bow("Red Bow Tie", _red);
        Color[] tieColours = { new Color(0.9f, 0.68f, 0.2f), new Color(0.7f, 0.06f, 0.08f), new Color(0.05f, 0.05f, 0.06f), new Color(0.1f, 0.35f, 0.8f) };
        Transform[] neckwear = { tie, tie, tie, tie, blackBow, redBow, null };
        _categories.Add(new Category
        {
            Name = "Neckwear", Selected = 0,
            Options = new[] { "Gold Tie", "Red Tie", "Black Tie", "Blue Tie", "Black Bow Tie", "Red Bow Tie", "None" },
            Apply = i =>
            {
                foreach (var t in neckwear) if (t != null) t.gameObject.SetActive(false);
                if (neckwear[i] != null) neckwear[i].gameObject.SetActive(true);
                if (i < tieColours.Length) tieMaterial.color = tieColours[i];
            },
        });

        AddColours("Skin", _skin, 1,
            ("Fair", new Color(0.97f, 0.8f, 0.7f)), ("Light", new Color(0.9f, 0.68f, 0.55f)),
            ("Tan", new Color(0.78f, 0.55f, 0.38f)), ("Brown", new Color(0.52f, 0.34f, 0.22f)),
            ("Dark", new Color(0.3f, 0.19f, 0.13f)), ("Zombie", new Color(0.5f, 0.7f, 0.45f)),
            ("Alien", new Color(0.4f, 0.6f, 0.9f)));
    }

    // Handlebar mustache, the same shape the bartender wears.
    Transform Mustache(string groupName)
    {
        var g = Group(groupName, _head, new Vector3(0f, 0.095f, 0.125f));
        for (int side = -1; side <= 1; side += 2)
        {
            Part(PrimitiveType.Capsule, g, _hair, new Vector3(side * 0.04f, -0.004f, -0.004f), new Vector3(0.03f, 0.046f, 0.03f))
                .localRotation = Quaternion.Euler(0f, 0f, side * 75f);
            Part(PrimitiveType.Sphere, g, _hair, new Vector3(side * 0.083f, 0.006f, -0.018f), Vector3.one * 0.026f);
        }
        return g;
    }

    // A category where exactly one of several objects is shown.
    void Add(string categoryName, int defaultIndex, string[] names, Transform[] objects)
    {
        _categories.Add(new Category
        {
            Name = categoryName, Options = names, Selected = defaultIndex,
            Apply = i =>
            {
                for (int k = 0; k < objects.Length; k++)
                    if (objects[k] != null) objects[k].gameObject.SetActive(k == i);
            },
        });
    }

    // A category that recolours one material.
    void AddColours(string categoryName, Material material, int defaultIndex, params (string name, Color colour)[] options)
    {
        var names = new string[options.Length];
        for (int i = 0; i < options.Length; i++) names[i] = options[i].name;
        _categories.Add(new Category
        {
            Name = categoryName, Options = names, Selected = defaultIndex,
            Apply = i => material.color = options[i].colour,
        });
    }

    // ------------------------------------------------------------ helpers

    static void Remove(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        if (child != null) Destroy(child.gameObject);
    }

    Material Copy(Transform parent, string childName, Color fallback)
    {
        var child = parent.Find(childName);
        var renderer = child != null ? child.GetComponent<Renderer>() : null;
        if (renderer != null && renderer.sharedMaterial != null) return new Material(renderer.sharedMaterial);
        return new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = fallback };
    }

    Transform Group(string groupName, Transform parent, Vector3 localPosition)
    {
        var go = new GameObject(groupName) { layer = _layer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    Transform Part(PrimitiveType type, Transform parent, Material material, Vector3 localPosition, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.layer = _layer;
        var collider = go.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }
}
