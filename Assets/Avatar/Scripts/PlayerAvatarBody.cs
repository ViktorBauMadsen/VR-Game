using Unity.XR.CoreUtils;
using UnityEngine;

// The player's own character model: a head that follows the headset, a
// torso that hangs under it (turning with a little lag, like
// PlayerTorsoAnchor), arms stretched from the shoulders to the two
// HandPoser hands, and legs stretched down to the floor.
//
// It lives on the "PlayerBody" layer, which is removed from the player
// camera's culling mask - so you never see the inside of your own head or
// have your torso block the chip pockets - but the Mirror's cameras render
// every layer, so you see the whole character in the mirror. (The hands are
// not on that layer; you see those directly.)
public class PlayerAvatarBody : MonoBehaviour
{
    public const string LayerName = "PlayerBody";

    [SerializeField] Transform head;   // pivots at the neck
    [SerializeField] Transform body;   // torso group, origin at floor level, neck at neckHeight
    [SerializeField] float neckHeight = 1.47f;
    [Tooltip("Where the eyes are, relative to the head pivot.")]
    [SerializeField] Vector3 eyeOffset = new Vector3(0f, 0.16f, 0.1f);

    [Header("Right, then left")]
    [SerializeField] Transform[] shoulders = new Transform[2];
    [SerializeField] Transform[] upperArms = new Transform[2];
    [SerializeField] Transform[] forearms = new Transform[2];
    [SerializeField] Transform[] hips = new Transform[2];
    [SerializeField] Transform[] legs = new Transform[2];
    [SerializeField] Transform[] feet = new Transform[2];
    [SerializeField] float armThickness = 0.085f;
    [SerializeField] float legThickness = 0.14f;
    [SerializeField] float maxYawDegreesPerSecond = 300f;

    Transform _camera;
    Transform _rig;
    readonly Transform[] _wrists = new Transform[2];
    float _yaw;
    float _nextSearch;
    int _layer;

    void Awake()
    {
        _layer = LayerMask.NameToLayer(LayerName);
        if (_layer < 0)
        {
            // Without the layer the head would sit right over the camera.
            Debug.LogWarning($"PlayerAvatarBody: no '{LayerName}' layer - run Tools > Avatar > Build to add it. Avatar disabled.");
            gameObject.SetActive(false);
            return;
        }
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _layer;
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    void FindRig()
    {
        if (Time.unscaledTime < _nextSearch) return;
        _nextSearch = Time.unscaledTime + 0.5f;

        if (_camera == null && Camera.main != null)
        {
            var cam = Camera.main;
            cam.cullingMask &= ~(1 << _layer);
            _camera = cam.transform;
            _yaw = _camera.eulerAngles.y;
        }
        if (_rig == null)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null) _rig = origin.transform;
        }
        if (_wrists[0] == null || _wrists[1] == null)
            foreach (var hand in FindObjectsByType<HandPoser>())
                _wrists[hand.HandSide == HandPoser.Side.Right ? 0 : 1] = hand.Wrist;
    }

    void LateUpdate()
    {
        if (_camera == null || _rig == null || _wrists[0] == null || _wrists[1] == null) FindRig();
        if (_camera == null) return;

        // Head: put the model's eyes exactly where the headset is.
        Quaternion headRotation = _camera.rotation;
        head.SetPositionAndRotation(_camera.position - headRotation * eyeOffset, headRotation);

        // Torso hangs from the neck, upright, turning toward where the head faces.
        _yaw = Mathf.MoveTowardsAngle(_yaw, _camera.eulerAngles.y, maxYawDegreesPerSecond * Time.deltaTime);
        Quaternion bodyRotation = Quaternion.Euler(0f, _yaw, 0f);
        body.SetPositionAndRotation(head.position - Vector3.up * neckHeight, bodyRotation);

        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;

            bool hasHand = _wrists[i] != null;
            if (upperArms[i] != null) upperArms[i].gameObject.SetActive(hasHand);
            if (forearms[i] != null) forearms[i].gameObject.SetActive(hasHand);
            if (hasHand && shoulders[i] != null)
                CharacterLimbs.PoseArm(upperArms[i], forearms[i], shoulders[i].position, _wrists[i].position, bodyRotation, side, armThickness);

            // Legs reach from the hips down to the floor the rig stands on.
            if (hips[i] == null) continue;
            Vector3 hip = hips[i].position;
            float floor = _rig != null ? _rig.position.y : hip.y - 0.8f;
            Vector3 foot = new Vector3(hip.x, Mathf.Min(floor + 0.06f, hip.y - 0.1f), hip.z);
            CharacterLimbs.Stretch(legs[i], hip, foot, legThickness);
            if (feet[i] != null)
                feet[i].SetPositionAndRotation(foot + bodyRotation * new Vector3(0f, -0.02f, 0.05f), bodyRotation);
        }
    }
}
