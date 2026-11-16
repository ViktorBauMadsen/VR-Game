using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

// A real mirror for VR. Put it on a Quad (visible face = the quad's -Z).
//
// For each eye there is a reflection camera placed at that eye's mirror
// image behind the glass, looking out through the mirror with an off-axis
// projection whose frustum is fitted exactly to the quad's four corners.
// Each renders into its own texture, and the StereoMirror shader shows the
// left texture to the left eye and the right one to the right eye - so the
// reflection has proper depth instead of looking like a flat TV screen.
//
// Two extra scene renders are not free, so the cameras only run while the
// player is in front of the mirror and within maxDistance; otherwise the
// glass just keeps its last image. Without a headset (editor) one camera
// feeds both eyes.
[RequireComponent(typeof(Renderer))]
public class Mirror : MonoBehaviour
{
    [SerializeField] int textureHeight = 1024;
    [SerializeField] float maxDistance = 7f;
    [SerializeField] float farClip = 30f;
    [SerializeField] LayerMask reflectLayers = ~0;
    [Tooltip("Show the arrows panel for customising the player's character.")]
    [SerializeField] bool wardrobe = true;
    [SerializeField] float eyeSeparation = 0.064f; // used when the headset doesn't report eye positions

    static readonly List<XRNodeState> NodeStates = new List<XRNodeState>();

    readonly Camera[] _cameras = new Camera[2];
    readonly RenderTexture[] _textures = new RenderTexture[2];
    Material _material;
    float _width, _height;

    void Start()
    {
        _width = Mathf.Abs(transform.lossyScale.x);
        _height = Mathf.Abs(transform.lossyScale.y);
        int textureWidth = Mathf.Clamp(Mathf.RoundToInt(textureHeight * _width / _height), 16, 4096);

        // Material instance, not a MaterialPropertyBlock (GPU Resident Drawer).
        _material = GetComponent<Renderer>().material;
        for (int i = 0; i < 2; i++)
        {
            _textures[i] = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = $"{name} {(i == 0 ? "Left" : "Right")}",
                antiAliasing = 2,
            };

            var go = new GameObject($"{name} Camera {(i == 0 ? "Left" : "Right")}");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = _textures[i];
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.cullingMask = reflectLayers;
            cam.allowHDR = false;
            cam.depth = -10f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            _cameras[i] = cam;
        }

        _material.SetTexture("_LeftTex", _textures[0]);
        _material.SetTexture("_RightTex", _textures[1]);
        if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _textures[0]);

        // The character-customisation panel beside the glass.
        if (wardrobe && GetComponentInChildren<MirrorWardrobe>() == null)
            gameObject.AddComponent<MirrorWardrobe>();
    }

    void OnEnable() => Application.onBeforeRender += UpdateCameras;

    void OnDisable()
    {
        Application.onBeforeRender -= UpdateCameras;
        foreach (var cam in _cameras)
            if (cam != null) cam.enabled = false;
    }

    void OnDestroy()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_cameras[i] != null) Destroy(_cameras[i].gameObject);
            if (_textures[i] != null) _textures[i].Release();
        }
        if (_material != null) Destroy(_material);
    }

    void LateUpdate() => UpdateCameras();

    // Also runs right before rendering, when the headset pose is freshest.
    void UpdateCameras()
    {
        if (_cameras[0] == null) return;
        var viewer = Camera.main;
        if (viewer == null) { SetActive(false, false); return; }

        Vector3 front = -transform.forward; // the side the glass faces
        Vector3 toViewer = viewer.transform.position - transform.position;
        float distance = Vector3.Dot(toViewer, front);
        bool facing = Vector3.Dot(viewer.transform.forward, -toViewer.normalized) > -0.3f;
        if (distance < 0.05f || toViewer.magnitude > maxDistance || !facing)
        {
            SetActive(false, false);
            return;
        }

        bool stereo = XRSettings.isDeviceActive;
        if (stereo != _wasStereo)
        {
            _wasStereo = stereo;
            _material.SetTexture("_RightTex", _textures[stereo ? 1 : 0]);
        }
        SetActive(true, stereo);

        Vector3 right = viewer.transform.right * (eyeSeparation * 0.5f);
        if (!stereo)
        {
            Place(_cameras[0], viewer, viewer.transform.position);
            return;
        }
        Place(_cameras[0], viewer, EyePosition(viewer, XRNode.LeftEye, viewer.transform.position - right));
        Place(_cameras[1], viewer, EyePosition(viewer, XRNode.RightEye, viewer.transform.position + right));
    }

    bool _wasStereo = true;

    void SetActive(bool left, bool right)
    {
        _cameras[0].enabled = left;
        _cameras[1].enabled = right;
    }

    // Eye positions are reported in tracking space (the camera's parent).
    static Vector3 EyePosition(Camera viewer, XRNode node, Vector3 fallback)
    {
        InputTracking.GetNodeStates(NodeStates);
        foreach (var state in NodeStates)
        {
            if (state.nodeType != node || !state.TryGetPosition(out var local)) continue;
            var parent = viewer.transform.parent;
            Vector3 world = parent != null ? parent.TransformPoint(local) : local;
            if ((world - viewer.transform.position).sqrMagnitude < 0.04f) return world;
        }
        return fallback;
    }

    void Place(Camera cam, Camera viewer, Vector3 eye)
    {
        Vector3 front = -transform.forward;
        Vector3 up = transform.up;
        Vector3 centre = transform.position;

        float d = Vector3.Dot(eye - centre, front);
        if (d < 0.02f) { cam.enabled = false; return; }

        // Mirror image of the eye, behind the glass, looking out the front.
        Vector3 reflected = eye - 2f * d * front;
        Quaternion rotation = Quaternion.LookRotation(front, up);
        cam.transform.SetPositionAndRotation(reflected, rotation);

        // Off-axis frustum through the quad's corners. The near plane sits
        // just in front of the glass so nothing behind the mirror shows up.
        Vector3 camRight = rotation * Vector3.right;
        Vector3 rel = centre - reflected;
        float cx = Vector3.Dot(rel, camRight), cy = Vector3.Dot(rel, up);
        float near = d + 0.01f;
        float s = near / d;
        cam.nearClipPlane = near;
        cam.farClipPlane = near + farClip;
        cam.projectionMatrix = Matrix4x4.Frustum(
            (cx - _width * 0.5f) * s, (cx + _width * 0.5f) * s,
            (cy - _height * 0.5f) * s, (cy + _height * 0.5f) * s,
            near, near + farClip);

        cam.clearFlags = viewer.clearFlags;
        cam.backgroundColor = viewer.backgroundColor;
    }
}
