using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Safety net for the desert around the casino (added to "Desert Surroundings"
// by Tools > Casino > Desert Surroundings): if the player ever ends up well
// below the ground - squeezed through a gap, or out past the edge of the map -
// they're teleported back to where they started. Uses the rig's own
// TeleportationProvider so the CharacterController stays in sync.
public class PlayerFallGuard : MonoBehaviour
{
    [SerializeField] float fallDepth = 6f; // metres below the start height that counts as "fell off"

    XROrigin _player;
    TeleportationProvider _teleporter;
    Vector3 _home;
    Quaternion _homeRot;
    bool _haveHome;
    float _nextCheck, _ignoreUntil;

    void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + 0.25f;

        if (_player == null)
        {
            _player = FindAnyObjectByType<XROrigin>();
            _teleporter = FindAnyObjectByType<TeleportationProvider>();
            _haveHome = false;
            if (_player == null) return;
        }
        if (!_haveHome)
        {
            _home = _player.transform.position;
            _homeRot = _player.transform.rotation;
            _haveHome = true;
            return;
        }
        // The mafia's desert scene is far below the casino on purpose.
        if (KidnapScene.Active) { _ignoreUntil = Time.time + 1.5f; return; }
        if (Time.time < _ignoreUntil || _player.transform.position.y > _home.y - fallDepth) return;

        _ignoreUntil = Time.time + 1.5f;
        if (_teleporter != null)
        {
            _teleporter.QueueTeleportRequest(new TeleportRequest
            {
                destinationPosition = _home,
                destinationRotation = _homeRot,
                matchOrientation = MatchOrientation.TargetUpAndForward,
                requestTime = Time.time,
            });
        }
        else
        {
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.SetPositionAndRotation(_home, _homeRot);
            if (cc != null) cc.enabled = true;
        }
        var body = _player.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
    }
}
