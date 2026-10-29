using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// The small "[-]  BET 50  [+]" panel under a slot machine's reels. Built
// at runtime by SlotMachine (so every machine gets one with no scene
// setup), placed just below the bottom row of the reel screen and facing
// the player. The buttons are XR Simple Interactables like the roulette
// spin button: poke them, or point and press trigger/grip.
public class SlotBetPanel : MonoBehaviour
{
    const float ButtonSize = 0.055f;
    const float ButtonDepth = 0.02f;
    const float ButtonGap = 0.13f;   // center of panel to center of each button
    const float PressDepth = 0.008f;

    static readonly Color PlateColor = new Color(0.08f, 0.08f, 0.1f);
    static readonly Color MinusColor = new Color(0.75f, 0.2f, 0.2f);
    static readonly Color PlusColor = new Color(0.2f, 0.65f, 0.25f);
    static readonly Color DisabledColor = new Color(0.3f, 0.3f, 0.3f);
    static readonly Color TextColor = new Color(1f, 0.85f, 0.3f);
    static readonly Color ErrorColor = new Color(1f, 0.25f, 0.25f);

    SlotMachine _machine;
    TextMeshPro _label;
    Button _minus, _plus;
    Coroutine _flash;

    class Button
    {
        public Transform t;
        public Renderer r;
        public Vector3 restLocal;
        public Color baseColor;
        public bool hovered, pressed;
    }

    public static SlotBetPanel Build(SlotMachine machine, SlotMachineReels reels, Vector3 offset)
    {
        if (!TryComputePose(machine, reels, offset, out Vector3 pos, out Quaternion facing))
        {
            Debug.LogWarning($"SlotBetPanel: couldn't work out where {machine.name}'s reel screen is - no bet panel built.");
            return null;
        }

        // Unparented on purpose: machines are often scaled non-uniformly,
        // which would squash the panel. The machines never move anyway.
        var go = new GameObject($"{machine.name} BetPanel (runtime)");
        go.transform.SetPositionAndRotation(pos, facing);
        var panel = go.AddComponent<SlotBetPanel>();
        panel.Init(machine, FindLitShader(machine));
        return panel;
    }

    // Where the panel goes: centered just below the bottom reel row, facing
    // the player, then pushed toward the player until it's clear of the
    // machine's own colliders (the reel screen sits recessed inside the
    // cabinet; inside a collider the controller ray couldn't reach it).
    // Also used by SlotMachine's editor gizmo.
    public static bool TryComputePose(SlotMachine machine, SlotMachineReels reels, Vector3 offset, out Vector3 pos, out Quaternion facing)
    {
        pos = Vector3.zero;
        if (!reels.TryGetScreenFrame(out Vector3 center, out facing, out float rowSpacing, out _)) return false;

        Vector3 up = facing * Vector3.up, toPlayer = facing * Vector3.back, right = facing * Vector3.right;
        pos = center - up * (rowSpacing * 1.75f) + toPlayer * 0.015f + right * offset.x + up * offset.y;

        const float step = 0.01f, margin = 0.015f;
        var colliders = machine.GetComponentsInChildren<Collider>();
        for (int i = 0; i < 100 && InsideAny(colliders, pos); i++) pos += toPlayer * step;
        pos += toPlayer * (margin + offset.z);
        return true;
    }

    static bool InsideAny(Collider[] colliders, Vector3 p)
    {
        foreach (var c in colliders)
        {
            if (c == null || !c.enabled || c.isTrigger) continue;
            // ClosestPoint only works on primitive and convex mesh colliders.
            if (c is MeshCollider mc && !mc.convex) continue;
            if ((c.ClosestPoint(p) - p).sqrMagnitude < 1e-8f) return true;
        }
        return false;
    }

    void Init(SlotMachine machine, Shader shader)
    {
        _machine = machine;

        MakeBox("Plate", new Vector3(0f, 0f, 0.012f), new Vector3(ButtonGap * 2f + ButtonSize + 0.03f, ButtonSize + 0.03f, 0.008f), PlateColor, shader, false);

        _label = MakeText("Bet", Vector3.zero, 0.45f, new Vector2(0.2f, 0.07f));
        _minus = MakeButton("BetDown", -ButtonGap, MinusColor, "-", shader, -1);
        _plus = MakeButton("BetUp", ButtonGap, PlusColor, "+", shader, +1);

        _machine.BetChanged += HandleBetChanged;
        _machine.SpinStateChanged += Refresh;
        _machine.SpinRejected += FlashNoFunds;
        Refresh();
    }

    void OnDestroy()
    {
        if (_machine == null) return;
        _machine.BetChanged -= HandleBetChanged;
        _machine.SpinStateChanged -= Refresh;
        _machine.SpinRejected -= FlashNoFunds;
    }

    void HandleBetChanged(int _) => Refresh();

    void Refresh()
    {
        if (_flash == null)
        {
            _label.text = "BET " + _machine.BetAmount;
            _label.color = TextColor;
        }
        UpdateColor(_minus, _machine.CanBetDown);
        UpdateColor(_plus, _machine.CanBetUp);
    }

    void FlashNoFunds()
    {
        if (_flash != null) StopCoroutine(_flash);
        _flash = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        _label.text = "NO FUNDS";
        _label.color = ErrorColor;
        yield return new WaitForSeconds(1.5f);
        _flash = null;
        Refresh();
    }

    Button MakeButton(string name, float x, Color color, string symbol, Shader shader, int steps)
    {
        var box = MakeBox(name, new Vector3(x, 0f, 0f), new Vector3(ButtonSize, ButtonSize, ButtonDepth), color, shader, true);
        var b = new Button { t = box.transform, r = box.GetComponent<Renderer>(), restLocal = box.transform.localPosition, baseColor = color };

        // Symbol on the button's front face (sibling, not child, so the
        // box's scale doesn't stretch it); it rides along when pressed.
        var text = MakeText(name + "Symbol", new Vector3(x, 0f, -ButtonDepth * 0.5f - 0.001f), 0.7f, new Vector2(ButtonSize, ButtonSize));
        text.color = Color.white;
        text.text = symbol;
        text.transform.SetParent(box.transform, true);

        var interactable = box.AddComponent<XRSimpleInteractable>();
        interactable.hoverEntered.AddListener(_ => { b.hovered = true; UpdateColor(b, IsEnabled(steps)); });
        interactable.hoverExited.AddListener(_ => { b.hovered = false; UpdateColor(b, IsEnabled(steps)); });
        interactable.selectEntered.AddListener(_ =>
        {
            b.pressed = true;
            _machine.ChangeBet(steps);
        });
        interactable.selectExited.AddListener(_ => b.pressed = false);
        return b;
    }

    bool IsEnabled(int steps) => steps < 0 ? _machine.CanBetDown : _machine.CanBetUp;

    void Update()
    {
        AnimatePress(_minus);
        AnimatePress(_plus);
    }

    static void AnimatePress(Button b)
    {
        if (b == null) return;
        Vector3 target = b.restLocal + (b.pressed ? Vector3.forward * PressDepth : Vector3.zero);
        b.t.localPosition = Vector3.MoveTowards(b.t.localPosition, target, 0.1f * Time.deltaTime);
    }

    static void UpdateColor(Button b, bool enabled)
    {
        if (b == null) return;
        Color c = enabled ? b.baseColor : DisabledColor;
        if (enabled && b.hovered) c = Color.Lerp(c, Color.white, 0.35f);
        SetColor(b.r.material, c);
    }

    GameObject MakeBox(string name, Vector3 localPos, Vector3 size, Color color, Shader shader, bool keepCollider)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(transform, false);
        box.transform.localPosition = localPos;
        box.transform.localScale = size;
        if (!keepCollider) Destroy(box.GetComponent<Collider>());

        var r = box.GetComponent<Renderer>();
        if (shader != null) r.material = new Material(shader);
        SetColor(r.material, color);
        return box;
    }

    TextMeshPro MakeText(string name, Vector3 localPos, float fontSize, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = localPos + new Vector3(0f, 0f, -0.001f);
        var text = obj.AddComponent<TextMeshPro>();
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = size;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        return text;
    }

    static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    // Reuse a shader the machine already renders with, so it's guaranteed
    // to be included in the Quest build (a Shader.Find'd one might be stripped).
    static Shader FindLitShader(SlotMachine machine)
    {
        foreach (var r in machine.GetComponentsInChildren<MeshRenderer>())
        {
            var m = r.sharedMaterial;
            if (m != null && m.shader != null && m.shader.name.Contains("Lit") && m.HasProperty("_BaseColor"))
                return m.shader;
        }
        return Shader.Find("Universal Render Pipeline/Lit");
    }
}
