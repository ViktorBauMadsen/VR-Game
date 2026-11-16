using TMPro;
using UnityEngine;

// Text on a quad. World-space text (3D TextMeshPro and world-space canvases
// alike) renders away from where it actually is in this project, so - same
// fix as the beer pong scoreboard - the text is drawn by a small offscreen
// camera into a RenderTexture that is shown on this object's plain quad.
// The camera only renders for a couple of frames whenever the text changes.
[RequireComponent(typeof(Renderer))]
public class WorldTextPanel : MonoBehaviour
{
    [SerializeField, TextArea] string text = "";
    [SerializeField] Color textColor = Color.white;
    [SerializeField] Color background = new Color(0.05f, 0.03f, 0.02f);
    [SerializeField] Vector2Int textureSize = new Vector2Int(768, 384);
    [SerializeField] float maxFontSize = 150f;

    static int _panelCount;

    Renderer _renderer;
    RenderTexture _texture;
    Camera _camera;
    TextMeshProUGUI _label;
    int _renderFramesLeft;
    bool _built;

    public bool Visible
    {
        get => GetComponent<Renderer>().enabled;
        set => GetComponent<Renderer>().enabled = value;
    }

    void Awake() => EnsureBuilt();

    void OnDestroy()
    {
        if (_camera != null) Destroy(_camera.gameObject);
        if (_texture != null) _texture.Release();
    }

    public void SetText(string value)
    {
        text = value;
        EnsureBuilt();
        _label.text = value;
        _renderFramesLeft = 2;
    }

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        _renderer = GetComponent<Renderer>();

        _texture = new RenderTexture(textureSize.x, textureSize.y, 16, RenderTextureFormat.ARGB32)
        {
            name = name + " Text",
            antiAliasing = 4,
        };

        // Far below the world with a 2 m far plane, each panel in its own
        // spot, so the only thing a camera can see is its own canvas - and
        // the canvas is beyond the player camera's far clip plane.
        var camGo = new GameObject(name + " Text Camera");
        camGo.transform.position = new Vector3(_panelCount++ * 50f, -6000f, 0f);
        _camera = camGo.AddComponent<Camera>();
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = background;
        _camera.orthographic = true;
        _camera.orthographicSize = 1f;
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 2f;
        _camera.stereoTargetEye = StereoTargetEyeMask.None;
        _camera.targetTexture = _texture;
        _camera.aspect = (float)textureSize.x / textureSize.y;
        _camera.enabled = false;

        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(camGo.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _camera;
        canvas.planeDistance = 1f;
        _camera.cullingMask = 1 << canvasGo.layer;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.layer = canvasGo.layer;
        var rect = (RectTransform)textGo.transform;
        rect.SetParent(canvasGo.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(24f, 16f);
        rect.offsetMax = new Vector2(-24f, -16f);

        _label = textGo.AddComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontStyle = FontStyles.Bold;
        _label.color = textColor;
        _label.enableAutoSizing = true;
        _label.fontSizeMin = 18f;
        _label.fontSizeMax = maxFontSize;
        _label.raycastTarget = false;
        _label.text = text;

        // A runtime material copy rather than a MaterialPropertyBlock - this
        // project uses the GPU Resident Drawer, which doesn't honour those.
        var mat = new Material(_renderer.sharedMaterial) { name = name + " (runtime)" };
        mat.SetTexture("_BaseMap", _texture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _texture);
        _renderer.sharedMaterial = mat;

        _renderFramesLeft = 2;
    }

    void LateUpdate()
    {
        if (_camera == null) return;
        _camera.enabled = _renderFramesLeft > 0;
        if (_renderFramesLeft > 0) _renderFramesLeft--;
    }
}
