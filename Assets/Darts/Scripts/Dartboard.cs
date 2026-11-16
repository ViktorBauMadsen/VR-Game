using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A dartboard, just for fun - no money involved.
//
// Scoring is the real thing: the twenty numbered wedges in their proper
// order, the thin outer ring counts double, the thin inner ring treble, the
// outer bull is 25 and the bullseye 50. A round is three darts; the
// scoreboard shows the last dart, the round so far and your best round.
// After the third dart the darts fly back to the holder by themselves, and
// the button next to the board brings them back at any time.
//
// The face of the board (the coloured wedges and the numbers) is painted
// into a texture when the game starts, so it always matches the scoring.
// The board's +Z side is the side you throw at.
//
// Built by Tools > Casino > Build Darts.
public class Dartboard : MonoBehaviour
{
    // Clockwise from the top, as you look at the board.
    static readonly int[] Numbers = { 20, 1, 18, 4, 13, 6, 10, 15, 2, 17, 3, 19, 7, 16, 8, 11, 14, 9, 12, 5 };

    // Ring edges as a fraction of the scoring radius (real board: 170 mm).
    const float Bull = 0.0374f, OuterBull = 0.0935f, TrebleIn = 0.582f, TrebleOut = 0.629f, DoubleIn = 0.953f;
    // How much of the painted face is the scoring area; the rest is the number ring.
    const float FaceFill = 0.8f;

    [Tooltip("Radius of the scoring area (to the outside of the double ring). A real board is 0.17; bigger is friendlier in VR.")]
    [SerializeField] float radius = 0.26f;
    [SerializeField] Dart[] darts = new Dart[0];
    [SerializeField] WorldTextPanel scoreboard;
    [Tooltip("A dart slower than this (m/s) bounces off instead of sticking.")]
    [SerializeField] float minimumStickSpeed = 1.5f;

    int _thrown, _round, _best;
    string _last = "";
    float _returnAt = -1f;
    static AudioClip _thud;

    void Awake()
    {
        BuildFace();
        foreach (var button in GetComponentsInChildren<XRSimpleInteractable>(true))
            button.selectEntered.AddListener(OnResetPressed);
    }

    void Start()
    {
        // Darts shouldn't knock each other out of the board.
        for (int i = 0; i < darts.Length; i++)
        {
            if (darts[i] == null) continue;
            darts[i].Owner = this;
            for (int k = i + 1; k < darts.Length; k++)
            {
                if (darts[k] == null) continue;
                foreach (var a in darts[i].GetComponentsInChildren<Collider>())
                    foreach (var b in darts[k].GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(a, b);
            }
        }
        Show();
    }

    void OnResetPressed(SelectEnterEventArgs args) => NewRound();

    void Update()
    {
        if (_returnAt > 0f && Time.time >= _returnAt) NewRound();
    }

    public void NewRound()
    {
        _returnAt = -1f;
        _thrown = 0;
        _round = 0;
        _last = "";
        foreach (var dart in darts)
            if (dart != null) dart.ReturnHome();
        Show();
    }

    // ------------------------------------------------------------ scoring

    // A flying dart touched the board. Returns false if it should bounce off.
    public bool TryStick(Dart dart, Vector3 point, Vector3 velocity)
    {
        float speed = velocity.magnitude;
        if (speed < minimumStickSpeed) return false;
        Vector3 direction = velocity / speed;
        if (Vector3.Dot(direction, -transform.forward) < 0.25f) return false; // from the side or behind

        Vector3 local = transform.InverseTransformPoint(point);
        float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
        float distance = new Vector2(local.x, local.y).magnitude * scale / radius; // 1 = edge of the double ring
        if (distance > 1f / FaceFill) return false; // off the face entirely

        // Stuck on the face, on the surface it actually hit.
        local.z = 0f;
        dart.StickAt(transform.TransformPoint(local), direction);

        int value = Score(local, distance, out string label);
        Register(value, label);
        AudioSource.PlayClipAtPoint(Thud(), point, 0.8f);
        if (value >= 50) BarSounds.Play(BarSounds.Ding, point, 0.7f);
        return true;
    }

    public void Missed(Dart dart) => Register(0, "MISS");

    int Score(Vector3 local, float distance, out string label)
    {
        if (distance <= Bull) { label = "BULLSEYE"; return 50; }
        if (distance <= OuterBull) { label = "OUTER BULL"; return 25; }
        if (distance > 1f) { label = "OFF THE BOARD"; return 0; }

        // You look at the board along its -Z, so your right is its -X.
        float angle = Mathf.Atan2(-local.x, local.y) * Mathf.Rad2Deg; // clockwise from the top
        int number = Numbers[Mathf.FloorToInt(Mathf.Repeat(angle + 9f, 360f) / 18f) % 20];
        if (distance >= DoubleIn) { label = "DOUBLE " + number; return number * 2; }
        if (distance >= TrebleIn && distance <= TrebleOut) { label = "TREBLE " + number; return number * 3; }
        label = number.ToString();
        return number;
    }

    void Register(int value, string label)
    {
        if (_returnAt > 0f) return; // round already over, darts on their way back
        _thrown++;
        _round += value;
        _last = value > 0 && label != value.ToString() ? $"{label} = {value}" : label;
        if (_thrown >= Mathf.Max(1, darts.Length))
        {
            _best = Mathf.Max(_best, _round);
            _returnAt = Time.time + 3.5f;
        }
        Show();
    }

    void Show()
    {
        if (scoreboard == null) return;
        int total = Mathf.Max(1, darts.Length);
        string last = _last.Length > 0 ? _last : "-";
        string round = _returnAt > 0f ? $"<color=#5CFF7A>ROUND: {_round}</color>" : $"ROUND: <color=#FFD24A>{_round}</color>   <size=70%>({_thrown}/{total} darts)</size>";
        scoreboard.SetText($"<color=#FFD24A>DARTS</color>\n<size=60%>LAST: {last}\n{round}\nBEST ROUND: {_best}</size>");
    }

    // --------------------------------------------------------------- face

    void BuildFace()
    {
        const int size = 512;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Dartboard Face", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
        var pixels = new Color32[size * size];
        Color32 black = new Color32(22, 22, 24, 255), cream = new Color32(232, 216, 176, 255);
        Color32 red = new Color32(196, 32, 36, 255), green = new Color32(22, 128, 62, 255), rim = new Color32(12, 12, 14, 255);
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / FaceFill; // 1 = edge of the double ring
                Color32 colour;
                if (d > 1f) colour = d * FaceFill > 1f ? new Color32(0, 0, 0, 0) : rim;
                else if (d <= Bull) colour = red;
                else if (d <= OuterBull) colour = green;
                else
                {
                    float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; // clockwise from the top, as the texture is seen
                    int wedge = Mathf.FloorToInt(Mathf.Repeat(angle + 9f, 360f) / 18f) % 20;
                    bool ring = d >= DoubleIn || (d >= TrebleIn && d <= TrebleOut);
                    colour = wedge % 2 == 0 ? (ring ? red : black) : (ring ? green : cream);
                }
                pixels[y * size + x] = colour;
            }

        // The numbers, in the rim.
        for (int i = 0; i < 20; i++)
        {
            float angle = i * 18f * Mathf.Deg2Rad;
            float r = (1f + FaceFill) * 0.5f * half;
            DrawNumber(pixels, size, Numbers[i], Mathf.RoundToInt(half + Mathf.Sin(angle) * r), Mathf.RoundToInt(half + Mathf.Cos(angle) * r), 4, cream);
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Face";
        var quadCollider = quad.GetComponent<Collider>();
        quadCollider.enabled = false;
        Destroy(quadCollider);
        quad.transform.SetParent(transform, false);
        float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
        float width = radius * 2f / FaceFill / scale;
        quad.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // a Quad shows on its -Z side
        quad.transform.localScale = new Vector3(width, width, 1f);

        var source = Resources.Load<Material>("Multiplayer/Avatar");
        var material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = "Dartboard Face (runtime)";
        material.color = Color.white;
        material.mainTexture = texture;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
        // The corners outside the round board are see-through.
        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        var renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 3x5 pixel digits, top row first, three bits per row.
    static readonly int[][] Digits =
    {
        new[] { 7, 5, 5, 5, 7 }, new[] { 2, 6, 2, 2, 7 }, new[] { 7, 1, 7, 4, 7 }, new[] { 7, 1, 7, 1, 7 }, new[] { 5, 5, 7, 1, 1 },
        new[] { 7, 4, 7, 1, 7 }, new[] { 7, 4, 7, 5, 7 }, new[] { 7, 1, 1, 1, 1 }, new[] { 7, 5, 7, 5, 7 }, new[] { 7, 5, 7, 1, 7 },
    };

    static void DrawNumber(Color32[] pixels, int size, int number, int centreX, int centreY, int pixel, Color32 colour)
    {
        string text = number.ToString();
        int width = (text.Length * 4 - 1) * pixel, height = 5 * pixel;
        int left = centreX - width / 2, top = centreY + height / 2;
        for (int c = 0; c < text.Length; c++)
        {
            int[] rows = Digits[text[c] - '0'];
            for (int row = 0; row < 5; row++)
                for (int column = 0; column < 3; column++)
                {
                    if ((rows[row] & (4 >> column)) == 0) continue;
                    for (int py = 0; py < pixel; py++)
                        for (int px = 0; px < pixel; px++)
                        {
                            int x = left + (c * 4 + column) * pixel + px;
                            int y = top - row * pixel - py - 1;
                            if (x >= 0 && x < size && y >= 0 && y < size) pixels[y * size + x] = colour;
                        }
                }
        }
    }

    // A dull knock: the dart going into the board.
    static AudioClip Thud()
    {
        if (_thud != null) return _thud;
        const int rate = 44100;
        var data = new float[(int)(rate * 0.12f)];
        var random = new System.Random(9);
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / rate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.15f;
            data[i] = (low * 1.5f + Mathf.Sin(2f * Mathf.PI * 160f * t) * 0.7f) * Mathf.Exp(-t * 45f);
        }
        _thud = AudioClip.Create("Dart Thud", data.Length, 1, rate, false);
        _thud.SetData(data, 0);
        return _thud;
    }
}
