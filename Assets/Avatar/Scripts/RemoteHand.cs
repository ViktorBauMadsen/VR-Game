using UnityEngine;

// Another player's hand: a copy of the local rigged hand model (made by
// HandPoser.CreateRemoteCopy) whose fingers follow the curls that player
// sends, smoothed the same way as your own.
public sealed class RemoteHand : MonoBehaviour
{
    FingerRig _rig;
    float _relaxed, _speed, _index, _grip, _thumb;
    Renderer[] _renderers;
    Material _skin;

    public Transform Wrist => _rig != null ? _rig.Wrist : null;

    public void Init(FingerRig rig, float relaxed, float speed)
    {
        _rig = rig;
        _relaxed = relaxed;
        _speed = speed;
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    public void SetCurls(float index, float grip, float thumb)
    {
        _index = index;
        _grip = grip;
        _thumb = thumb;
    }

    // Tinted to that player's skin tone (their own material instance).
    public void SetSkin(Color colour)
    {
        if (_renderers == null || _renderers.Length == 0) return;
        if (_skin == null)
        {
            var source = _renderers[0].sharedMaterial;
            if (source == null) return;
            _skin = new Material(source);
            foreach (var r in _renderers) r.sharedMaterial = _skin;
        }
        if (_skin.color != colour) _skin.color = colour;
    }

    void Update()
    {
        if (_rig != null) _rig.Pose(_index, _grip, _thumb, _relaxed, _speed, Time.deltaTime);
    }

    void OnDestroy()
    {
        if (_skin != null) Destroy(_skin);
    }
}
