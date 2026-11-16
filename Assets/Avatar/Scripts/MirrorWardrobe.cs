using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// The panel beside the mirror for customising your character: one row per
// AvatarCustomizer category (hat, hair, beard, ...), each with a left and a
// right arrow. Point at an arrow (or touch it) and press grab to cycle.
//
// Added automatically by Mirror, which sits on the mirror's glass quad; the
// whole panel is built at runtime to the right of the glass, so nothing in
// the scene needs setting up. Untick "Wardrobe" on the Mirror to remove it.
public class MirrorWardrobe : MonoBehaviour
{
    const float RowHeight = 0.2f;
    const float PanelWidth = 0.66f;

    AvatarCustomizer _avatar;
    WorldTextPanel[] _labels;
    Transform _panel;
    float _nextSearch;

    void Update()
    {
        if (_panel != null || Time.unscaledTime < _nextSearch) return;
        _nextSearch = Time.unscaledTime + 0.5f;
        _avatar = FindAnyObjectByType<AvatarCustomizer>();
        if (_avatar != null && _avatar.CategoryCount > 0) Build();
    }

    void OnDestroy()
    {
        if (_avatar != null) _avatar.Changed -= Refresh;
        if (_panel != null) Destroy(_panel.gameObject);
    }

    // The glass quad's -Z faces the viewer and its +X is the viewer's right,
    // so the panel simply shares the quad's rotation.
    void Build()
    {
        float glassWidth = Mathf.Abs(transform.lossyScale.x);
        int rows = _avatar.CategoryCount;
        float height = rows * RowHeight + 0.34f;

        _panel = new GameObject("Wardrobe Panel").transform;
        _panel.SetPositionAndRotation(
            transform.position + transform.right * (glassWidth * 0.5f + 0.16f + PanelWidth * 0.5f) - transform.forward * 0.02f,
            transform.rotation);

        var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.16f, 0.08f, 0.04f) };
        var gold = new Material(wood) { color = new Color(0.9f, 0.68f, 0.2f) };
        gold.SetFloat("_Smoothness", 0.8f);

        var board = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        board.name = "Board";
        board.SetParent(_panel, false);
        board.localPosition = new Vector3(0f, 0f, 0.025f);
        board.localScale = new Vector3(PanelWidth, height, 0.04f);
        board.GetComponent<Renderer>().sharedMaterial = wood;

        var dark = new Color(0.05f, 0.03f, 0.02f);
        WorldTextPanel.Create("Title", _panel, new Vector3(0f, height * 0.5f - 0.12f, 0f), new Vector2(0.56f, 0.14f),
            new Vector2Int(512, 128), new Color(1f, 0.82f, 0.29f), dark).SetText("YOUR LOOK");

        var arrowMesh = ArrowMesh();
        _labels = new WorldTextPanel[rows];
        for (int i = 0; i < rows; i++)
        {
            float y = height * 0.5f - 0.32f - i * RowHeight;
            _labels[i] = WorldTextPanel.Create(_avatar.CategoryName(i), _panel, new Vector3(0f, y, 0f), new Vector2(0.38f, 0.16f),
                new Vector2Int(384, 160), Color.white, dark, 70f);
            Arrow(i, -1, new Vector3(-0.26f, y, -0.01f), arrowMesh, gold);
            Arrow(i, 1, new Vector3(0.26f, y, -0.01f), arrowMesh, gold);
        }

        _avatar.Changed += Refresh;
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < _labels.Length; i++)
            _labels[i].SetText($"<size=55%><color=#FFD24A>{_avatar.CategoryName(i).ToUpper()}</color></size>\n{_avatar.SelectedName(i)}");
    }

    void Arrow(int category, int direction, Vector3 localPosition, Mesh mesh, Material material)
    {
        var go = new GameObject(direction < 0 ? "Previous" : "Next");
        go.transform.SetParent(_panel, false);
        go.transform.localPosition = localPosition;
        // The mesh points along +X; the panel's +X is the viewer's right.
        go.transform.localRotation = direction < 0 ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        var box = go.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(0.13f, 0.15f, 0.06f);

        var interactable = go.AddComponent<XRSimpleInteractable>();
        var arrow = go.transform;
        interactable.selectEntered.AddListener(args =>
        {
            _avatar.Cycle(category, direction);
            if (args.interactorObject is XRBaseInputInteractor input) input.SendHapticImpulse(0.3f, 0.06f);
            _pressed = arrow;
            _pressTime = Time.time;
        });
        interactable.hoverEntered.AddListener(_ => arrow.localScale = Vector3.one * 1.25f);
        interactable.hoverExited.AddListener(_ => arrow.localScale = Vector3.one);
    }

    Transform _pressed;
    float _pressTime;

    // A quick squash on the arrow that was just pressed.
    void LateUpdate()
    {
        if (_pressed == null) return;
        float t = (Time.time - _pressTime) / 0.15f;
        if (t >= 1f) { _pressed.localScale = Vector3.one; _pressed = null; return; }
        _pressed.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.25f, t);
    }

    // A flat triangular prism pointing along +X, about 9 cm across.
    static Mesh ArrowMesh()
    {
        const float w = 0.05f, h = 0.06f, d = 0.012f;
        Vector3[] corners = { new Vector3(-w, h, 0f), new Vector3(w, 0f, 0f), new Vector3(-w, -h, 0f) };
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int n = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
        }

        Vector3 front = Vector3.back * d, back = Vector3.forward * d;
        // Both windings of every face, so it's visible whichever way it's turned.
        Tri(corners[0] + front, corners[1] + front, corners[2] + front);
        Tri(corners[2] + front, corners[1] + front, corners[0] + front);
        Tri(corners[0] + back, corners[1] + back, corners[2] + back);
        Tri(corners[2] + back, corners[1] + back, corners[0] + back);
        for (int i = 0; i < 3; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % 3];
            Tri(a + front, a + back, b + back);
            Tri(b + back, a + back, a + front);
            Tri(a + front, b + back, b + front);
            Tri(b + front, b + back, a + front);
        }

        var mesh = new Mesh { name = "Arrow" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
