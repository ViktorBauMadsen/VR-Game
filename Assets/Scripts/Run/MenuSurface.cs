using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// A flat menu panel floating in the world.
//
// World-space canvases render away from where they really are in this
// project (see WorldTextPanel), which is what made the old lobby panel so
// hard to hit. So this does what the text quads do: the whole page is laid
// out on an offscreen canvas, drawn by a small camera into a RenderTexture
// and shown on a plain quad. The buttons you actually interact with are
// invisible box colliders with an XR Simple Interactable in front of the
// quad - the same kind of object as the blackjack buttons - so they sit
// exactly where they are drawn.
//
// A button is pressed by pointing at it (or touching it) and pulling the
// trigger or squeezing the grip. In the editor a mouse click works too.
//
// Layout is in pixels, origin top-left, on a Width x Height page.
public sealed class MenuSurface : MonoBehaviour
{
    public const float Width = 1024f, Height = 1280f;
    const float WorldWidth = 0.74f;
    const float Scale = WorldWidth / Width;
    public static readonly Color Background = new Color(0.045f, 0.055f, 0.085f);

    class Hit
    {
        public GameObject Object;
        public XRSimpleInteractable Interactable;
        public Collider Collider;
        public Image Face;
        public Color Normal;
        public Action Click;
    }

    static Sprite roundedSprite;

    RenderTexture texture;
    Camera pageCamera;
    RectTransform page;
    Transform hitRoot;
    Material quadMaterial;
    InputAction trigger;
    readonly List<Hit> hits = new List<Hit>();
    int renderFramesLeft;
    float lastClick;

    public void Build()
    {
        texture = new RenderTexture((int)Width, (int)Height, 16, RenderTextureFormat.ARGB32)
        {
            name = "Casino Menu",
            antiAliasing = 4,
            anisoLevel = 4,
        };

        // Far below the world, in its own spot, with a 2 m far plane - the
        // only thing this camera can ever see is its own canvas.
        // (Not a child of the panel: it must stay put when the panel moves.)
        var cameraObject = new GameObject("Casino Menu Camera");
        cameraObject.transform.position = new Vector3(-300f, -6000f, 300f);
        pageCamera = cameraObject.AddComponent<Camera>();
        pageCamera.clearFlags = CameraClearFlags.SolidColor;
        pageCamera.backgroundColor = Background;
        pageCamera.orthographic = true;
        pageCamera.orthographicSize = 1f;
        pageCamera.nearClipPlane = 0.1f;
        pageCamera.farClipPlane = 2f;
        pageCamera.stereoTargetEye = StereoTargetEyeMask.None;
        pageCamera.targetTexture = texture;
        pageCamera.aspect = Width / Height;
        pageCamera.enabled = false;

        var canvasObject = new GameObject("Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(cameraObject.transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = pageCamera;
        canvas.planeDistance = 1f;
        pageCamera.cullingMask = 1 << canvasObject.layer;

        var pageObject = new GameObject("Page", typeof(RectTransform));
        page = (RectTransform)pageObject.transform;
        page.SetParent(canvasObject.transform, false);
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.offsetMin = page.offsetMax = Vector2.zero;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Screen";
        var quadCollider = quad.GetComponent<Collider>();
        quadCollider.enabled = false;
        Destroy(quadCollider);
        quad.transform.SetParent(transform, false);
        quad.transform.localScale = new Vector3(WorldWidth, WorldWidth * Height / Width, 1f);
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        quadMaterial = new Material(shader) { name = "Casino Menu (runtime)" };
        if (quadMaterial.HasProperty("_BaseMap")) quadMaterial.SetTexture("_BaseMap", texture);
        if (quadMaterial.HasProperty("_MainTex")) quadMaterial.SetTexture("_MainTex", texture);
        var quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = quadMaterial;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;

        hitRoot = new GameObject("Buttons").transform;
        hitRoot.SetParent(transform, false);

        trigger = new InputAction("Menu Click", InputActionType.Button);
        trigger.AddBinding("<XRController>{LeftHand}/triggerPressed");
        trigger.AddBinding("<XRController>{RightHand}/triggerPressed");
        trigger.performed += OnTrigger;
        if (isActiveAndEnabled) trigger.Enable();
    }

    void OnEnable()
    {
        trigger?.Enable();
        renderFramesLeft = 3;
    }

    void OnDisable()
    {
        trigger?.Disable();
        if (pageCamera != null) pageCamera.enabled = false;
    }

    void OnDestroy()
    {
        trigger?.Dispose();
        if (pageCamera != null) Destroy(pageCamera.gameObject);
        if (texture != null) texture.Release();
        if (quadMaterial != null) Destroy(quadMaterial);
    }

    // ---------------------------------------------------------- building

    public void Clear()
    {
        for (int i = page.childCount - 1; i >= 0; i--)
        {
            var child = page.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        foreach (var hit in hits)
        {
            hit.Object.SetActive(false);
            Destroy(hit.Object);
        }
        hits.Clear();
        MarkDirty();
    }

    public void MarkDirty() => renderFramesLeft = 2;

    public Image Box(float x, float y, float w, float h, Color colour)
    {
        var image = new GameObject("Box", typeof(RectTransform)).AddComponent<Image>();
        Place(image.rectTransform, x, y, w, h);
        image.sprite = Rounded();
        image.type = Image.Type.Sliced;
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    public TextMeshProUGUI Text(string value, float x, float y, float w, float h, float size, Color colour,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center, bool bold = false)
    {
        var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Place(text.rectTransform, x, y, w, h);
        text.text = value;
        text.fontSize = size;
        text.color = colour;
        text.alignment = alignment;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
        return text;
    }

    // A disabled button is drawn dimmed and has nothing to press.
    public TextMeshProUGUI Button(string label, float x, float y, float w, float h, Color colour, Action onClick,
        bool enabled = true, float fontSize = 38f)
    {
        var shown = enabled ? colour : new Color(0.2f, 0.21f, 0.25f);
        var face = Box(x, y, w, h, shown);
        var text = Text(label, x + 10f, y, w - 20f, h, fontSize, enabled ? Color.white : new Color(1f, 1f, 1f, 0.35f), TextAlignmentOptions.Center, true);
        if (!enabled || onClick == null) return text;

        var go = new GameObject("Button " + label);
        go.transform.SetParent(hitRoot, false);
        go.transform.localPosition = new Vector3((x + w * 0.5f - Width * 0.5f) * Scale, (Height * 0.5f - y - h * 0.5f) * Scale, -0.012f);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(w * Scale, h * Scale, 0.02f);
        var interactable = go.AddComponent<XRSimpleInteractable>();

        var hit = new Hit { Object = go, Interactable = interactable, Collider = box, Face = face, Normal = shown, Click = onClick };
        interactable.hoverEntered.AddListener(_ => Highlight(hit, true));
        interactable.hoverExited.AddListener(_ => Highlight(hit, false));
        interactable.selectEntered.AddListener(args => Press(hit, args.interactorObject as XRBaseInputInteractor));
        hits.Add(hit);
        return text;
    }

    void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.gameObject.layer = page.gameObject.layer;
        rect.SetParent(page, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
    }

    // -------------------------------------------------------- interaction

    void Highlight(Hit hit, bool on)
    {
        if (hit.Face == null) return;
        hit.Face.color = on ? Color.Lerp(hit.Normal, Color.white, 0.3f) : hit.Normal;
        MarkDirty();
    }

    void Press(Hit hit, XRBaseInputInteractor interactor)
    {
        // Grip and trigger together would otherwise count twice.
        if (Time.unscaledTime - lastClick < 0.15f) return;
        lastClick = Time.unscaledTime;
        if (interactor != null) interactor.SendHapticImpulse(0.3f, 0.06f);
        hit.Click?.Invoke();
    }

    // The trigger presses whatever that hand's ray (or finger) is hovering.
    void OnTrigger(InputAction.CallbackContext context)
    {
        bool leftHand = false;
        if (context.control != null && context.control.device != null)
            foreach (var usage in context.control.device.usages)
                if (usage.ToString() == "LeftHand") leftHand = true;
        Hit fallback = null;
        XRBaseInputInteractor fallbackInteractor = null;
        foreach (var hit in hits)
        {
            if (hit.Interactable == null || !hit.Interactable.isHovered) continue;
            foreach (var hovering in hit.Interactable.interactorsHovering)
            {
                var interactor = hovering as XRBaseInputInteractor;
                bool isLeft = hovering.transform != null && InLeftHand(hovering.transform);
                if (isLeft == leftHand) { Press(hit, interactor); return; }
                fallback = hit;
                fallbackInteractor = interactor;
            }
        }
        if (fallback != null) Press(fallback, fallbackInteractor);
    }

    static bool InLeftHand(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            if (t.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        }
        return false;
    }

    void Update()
    {
        // Mouse, for testing in the editor without a headset.
        var mouse = Mouse.current;
        var main = Camera.main;
        if (mouse == null || main == null || !mouse.leftButton.wasPressedThisFrame) return;
        var ray = main.ScreenPointToRay(mouse.position.ReadValue());
        float best = float.MaxValue;
        Hit clicked = null;
        foreach (var hit in hits)
            if (hit.Collider != null && hit.Collider.Raycast(ray, out var info, 10f) && info.distance < best)
            {
                best = info.distance;
                clicked = hit;
            }
        if (clicked != null) Press(clicked, null);
    }

    void LateUpdate()
    {
        if (pageCamera == null) return;
        pageCamera.enabled = renderFramesLeft > 0;
        if (renderFramesLeft > 0) renderFramesLeft--;
    }

    // A white rounded rectangle, 9-sliced so any size keeps its corners.
    static Sprite Rounded()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 64, radius = 18;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Rounded",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius, size - radius), cy = Mathf.Clamp(py, radius, size - radius);
                float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                byte alpha = (byte)(Mathf.Clamp01(radius - distance + 0.5f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return roundedSprite;
    }
}
