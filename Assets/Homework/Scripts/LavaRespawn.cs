using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Goes on the lava slab under the floating-platform course. If the player ends
// up in it - walked off a platform, or teleported short onto the ground - they
// get teleported back to the last platform pad they reached, so a miss costs
// one hop rather than the whole course. Uses the rig's own
// TeleportationProvider, so the return trip goes through the same locomotion
// pipeline as a normal teleport (CharacterController stays in sync).
[RequireComponent(typeof(Collider))]
public class LavaRespawn : MonoBehaviour
{
    [SerializeField] Transform fallbackRespawn; // used until any course pad has been reached
    [SerializeField] CheckpointPad[] coursePads;
    [SerializeField] float surfaceTolerance = 0.3f; // how far above the lava still counts as "in it"

    Collider _lava;
    XROrigin _player;
    TeleportationProvider _teleporter;
    Transform _respawnPoint;
    float _ignoreUntil;

    void Awake()
    {
        _lava = GetComponent<Collider>();
        _player = FindAnyObjectByType<XROrigin>();
        _teleporter = FindAnyObjectByType<TeleportationProvider>();
        _respawnPoint = fallbackRespawn;
    }

    void OnEnable()
    {
        foreach (var pad in coursePads)
            if (pad != null) pad.Reached += OnPadReached;
    }

    void OnDisable()
    {
        foreach (var pad in coursePads)
            if (pad != null) pad.Reached -= OnPadReached;
    }

    void OnPadReached(CheckpointPad pad) => _respawnPoint = pad.RespawnPoint;

    void Update()
    {
        if (_player == null || _player.Camera == null || _teleporter == null || _respawnPoint == null) return;
        // The queued teleport lands a frame or two later; don't re-queue it
        // every frame while the player is still standing in the lava.
        if (Time.time < _ignoreUntil) return;

        Bounds lava = _lava.bounds;
        Vector3 head = _player.Camera.transform.position;
        bool overLava = head.x > lava.min.x && head.x < lava.max.x && head.z > lava.min.z && head.z < lava.max.z;
        bool inLava = _player.transform.position.y < lava.max.y + surfaceTolerance;

        if (overLava && inLava) Respawn();
    }

    void Respawn()
    {
        _ignoreUntil = Time.time + 1f;
        _teleporter.QueueTeleportRequest(new TeleportRequest
        {
            destinationPosition = _respawnPoint.position,
            destinationRotation = _respawnPoint.rotation,
            matchOrientation = MatchOrientation.TargetUpAndForward,
            requestTime = Time.time,
        });
    }
}
