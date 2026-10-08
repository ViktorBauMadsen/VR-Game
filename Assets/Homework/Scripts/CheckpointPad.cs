using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Sits on a TeleportationAnchor. Lights the pad up the first time the player
// stands on it - whether they teleported there or just walked over it - and
// raises Reached so LavaRespawn knows where to send the player back to.
// Detected by position rather than the anchor's teleporting event, since that
// event never fires for a player who walks onto the pad.
[RequireComponent(typeof(TeleportationAnchor))]
public class CheckpointPad : MonoBehaviour
{
    [SerializeField] Renderer padRenderer;
    [SerializeField] Color reachedColor = new Color(0.3f, 1f, 0.4f);
    [SerializeField] GameObject showWhenReached; // optional, e.g. the goal's "you escaped" banner
    [SerializeField] float reachRadius = 0.6f;

    public event Action<CheckpointPad> Reached;

    public bool IsReached { get; private set; }

    // Where the anchor puts the player - also where LavaRespawn sends them.
    public Transform RespawnPoint => _anchor.teleportAnchorTransform != null ? _anchor.teleportAnchorTransform : transform;

    TeleportationAnchor _anchor;
    XROrigin _player;

    void Awake()
    {
        _anchor = GetComponent<TeleportationAnchor>();
        _player = FindAnyObjectByType<XROrigin>();
        if (showWhenReached != null) showWhenReached.SetActive(false);
    }

    void Update()
    {
        if (IsReached || _player == null || _player.Camera == null) return;

        // Head for horizontal position (room-scale walking moves the head, not
        // the rig root), rig root for height (it's the player's feet).
        Vector3 head = _player.Camera.transform.position;
        Vector3 pad = RespawnPoint.position;
        float horizontalSqr = new Vector2(head.x - pad.x, head.z - pad.z).sqrMagnitude;
        bool standingOnPad = Mathf.Abs(_player.transform.position.y - pad.y) < 0.3f;

        if (horizontalSqr < reachRadius * reachRadius && standingOnPad) MarkReached();
    }

    void MarkReached()
    {
        IsReached = true;

        if (padRenderer != null)
        {
            var block = new MaterialPropertyBlock();
            padRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", reachedColor);
            block.SetColor("_EmissionColor", reachedColor * 0.6f);
            padRenderer.SetPropertyBlock(block);
        }

        if (showWhenReached != null) showWhenReached.SetActive(true);
        Reached?.Invoke(this);
    }
}
