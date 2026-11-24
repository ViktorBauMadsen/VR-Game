using System.Collections.Generic;
using Casino.Multiplayer;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Lets friends see the things you pick up: chips in your hand and on the
// table, beer pong balls, darts, drinks - anything with an XR Grab
// Interactable and a Rigidbody.
//
// Two kinds of object:
//
//   Scene props   exist on every machine from the start (beer pong balls,
//                 darts). Whoever grabs one "owns" it: their machine streams
//                 where it is, everyone else's copy turns kinematic, can't be
//                 grabbed and just follows along. Once it has been let go and
//                 has come to rest, ownership is handed back and everyone's
//                 copy is a normal physics object again, lying where the
//                 owner's did. The last owner keeps streaming it whenever it
//                 moves by itself afterwards (a ball returning to its spawn
//                 point, darts flying back to the holder).
//
//   Spawned props only exist where they were made (chips out of a pocket,
//                 drinks from the bar). The others see a "ghost": a copy of
//                 just the meshes, with no physics or scripts, that follows
//                 the stream and disappears when the real one is destroyed.
//                 A chip placed as a bet is drawn beside the spot's centre (a
//                 different side for each player) so it doesn't overlap your
//                 own stack on the same number.
//
// Scene props are matched between machines by where they were when the scene
// started, so everyone must run the same build of the same scene.
[DefaultExecutionOrder(10006)]
public sealed class CasinoProps : MonoBehaviour
{
    const string Message = "Casino.Prop.v1";
    const byte KindPose = 0, KindRest = 1, KindRelease = 2, KindGone = 3;
    const byte FlagRuntime = 1, FlagHeld = 2, FlagPlaced = 4;
    const float SendInterval = 1f / 20f;
    const float RestAfter = 0.3f;      // still this long: send a reliable final pose
    const float ReleaseAfter = 0.8f;   // let go and still this long: hand ownership back
    const float ScanInterval = 0.3f;
    const int MaxGhosts = 200;
    const int MaxKeyLength = 40;
    static readonly ulong Nobody = ulong.MaxValue;

    class Prop
    {
        public ushort Id;
        public XRGrabInteractable Grab;
        public Rigidbody Body;
        public Transform T;
        public bool Runtime;
        public string Key;
        public CasinoChip Chip;
        // Scene props: who moves it right now, and who moved it last.
        public ulong Owner = Nobody, LastOwner = Nobody;
        // Streaming (props this machine moves).
        public Vector3 SentPos = new Vector3(1e6f, 1e6f, 1e6f);
        public Quaternion SentRot = Quaternion.identity;
        public float SentAt = -10f, StillSince, ReleasedAt = -10f;
        public bool Dirty, ForceRest;
        // Following another player's stream.
        public bool Following, WasKinematic, GrabWasEnabled;
        public Vector3 TargetPos;
        public Quaternion TargetRot;
    }

    class Ghost
    {
        public GameObject Go;
        public Vector3 TargetPos, RawPos;
        public Quaternion TargetRot;
        public bool Placed, Held;
        public string Key;
    }

    CasinoMultiplayer multiplayer;
    bool built;
    float nextScan;
    ushort nextRuntimeId = 1;
    readonly List<Prop> sceneProps = new List<Prop>();
    readonly Dictionary<XRGrabInteractable, Prop> byGrab = new Dictionary<XRGrabInteractable, Prop>();
    readonly List<Prop> runtimeProps = new List<Prop>();
    readonly Dictionary<(ulong, ushort), Ghost> ghosts = new Dictionary<(ulong, ushort), Ghost>();
    readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
    readonly RaycastHit[] hits = new RaycastHit[8];
    Transform ghostRoot;

    bool Connected => multiplayer != null && multiplayer.InSession && multiplayer.Network != null
        && multiplayer.Network.CustomMessagingManager != null && multiplayer.Network.IsConnectedClient;
    ulong Me => multiplayer != null ? multiplayer.LocalId : 0UL;

    // ------------------------------------------------------------ set-up

    void Update()
    {
        if (!built) BuildRegistry();
        if (multiplayer == null)
        {
            multiplayer = CasinoMultiplayer.Instance;
            if (multiplayer != null)
            {
                multiplayer.SessionStarted += OnSessionStarted;
                multiplayer.SessionEnded += OnSessionEnded;
                multiplayer.GuestJoined += OnGuestJoined;
                multiplayer.GuestLeft += OnPlayerLeft;
            }
        }
    }

    void OnDestroy()
    {
        if (multiplayer == null) return;
        multiplayer.SessionStarted -= OnSessionStarted;
        multiplayer.SessionEnded -= OnSessionEnded;
        multiplayer.GuestJoined -= OnGuestJoined;
        multiplayer.GuestLeft -= OnPlayerLeft;
    }

    // Everything grabbable that is in the scene on the first frame, in an
    // order every machine agrees on.
    void BuildRegistry()
    {
        var found = new List<XRGrabInteractable>();
        foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include))
            if (Eligible(grab)) found.Add(grab);
        built = true;
        found.Sort((a, b) =>
        {
            int c = Compare(a.transform.position, b.transform.position);
            return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
        });
        for (int i = 0; i < found.Count && i < ushort.MaxValue; i++)
        {
            var prop = NewProp(found[i], false);
            prop.Id = (ushort)i;
            sceneProps.Add(prop);
            prop.Grab.selectEntered.AddListener(_ => Claim(prop));
            prop.Grab.selectExited.AddListener(_ => prop.ReleasedAt = Time.unscaledTime);
        }
    }

    bool Eligible(XRGrabInteractable grab)
    {
        if (grab == null || byGrab.ContainsKey(grab)) return false;
        if (grab.GetComponent<Rigidbody>() == null) return false;
        // Chips only exist where they were taken from a pocket.
        if (!built && grab.GetComponent<CasinoChip>() != null) return false;
        if (multiplayer != null && grab.transform.IsChildOf(multiplayer.transform)) return false;
        return true;
    }

    Prop NewProp(XRGrabInteractable grab, bool runtime)
    {
        var prop = new Prop
        {
            Grab = grab, Body = grab.GetComponent<Rigidbody>(), T = grab.transform, Runtime = runtime,
            Key = runtime ? KeyOf(grab.name) : null, Chip = grab.GetComponent<CasinoChip>(),
            StillSince = Time.unscaledTime,
        };
        byGrab[grab] = prop;
        return prop;
    }

    static string KeyOf(string name)
    {
        string key = name.Replace("(Clone)", "").Trim();
        return key.Length > MaxKeyLength ? key.Substring(0, MaxKeyLength) : key;
    }

    static int Compare(Vector3 pa, Vector3 pb)
    {
        int x = Mathf.RoundToInt(pa.x * 100f).CompareTo(Mathf.RoundToInt(pb.x * 100f));
        if (x != 0) return x;
        int z = Mathf.RoundToInt(pa.z * 100f).CompareTo(Mathf.RoundToInt(pb.z * 100f));
        return z != 0 ? z : Mathf.RoundToInt(pa.y * 100f).CompareTo(Mathf.RoundToInt(pb.y * 100f));
    }

    // New grabbables that appeared since the scene started (chips, drinks).
    void ScanForSpawned()
    {
        foreach (var grab in FindObjectsByType<XRGrabInteractable>())
        {
            if (!Eligible(grab) || runtimeProps.Count >= MaxGhosts) continue;
            var prop = NewProp(grab, true);
            prop.Id = nextRuntimeId++;
            if (nextRuntimeId == 0) nextRuntimeId = 1;
            runtimeProps.Add(prop);
        }
    }

    // ------------------------------------------------------------ session

    void OnSessionStarted()
    {
        multiplayer.Register(Message, Receive);
        foreach (var prop in runtimeProps) prop.ForceRest = true;
    }

    // A late joiner needs to see what is already lying around.
    void OnGuestJoined(ulong guest)
    {
        foreach (var prop in runtimeProps) prop.ForceRest = true;
        foreach (var prop in sceneProps)
            if (prop.Owner == Me) prop.ForceRest = true;
        foreach (var pair in ghosts)
        {
            var g = pair.Value;
            if (g.Go == null) continue;
            byte flags = (byte)(FlagRuntime | (g.Placed ? FlagPlaced : 0) | (g.Held ? FlagHeld : 0));
            SendTo(guest, KindRest, flags, pair.Key.Item1, pair.Key.Item2, g.RawPos, g.TargetRot, g.Key);
        }
    }

    void OnPlayerLeft(ulong player)
    {
        DropPlayer(player);
    }

    void OnSessionEnded()
    {
        foreach (var g in ghosts.Values) if (g.Go != null) Destroy(g.Go);
        ghosts.Clear();
        foreach (var prop in sceneProps)
        {
            if (prop.Following) EndFollow(prop);
            prop.Owner = prop.LastOwner = Nobody;
        }
    }

    void DropPlayer(ulong player)
    {
        var gone = new List<(ulong, ushort)>();
        foreach (var pair in ghosts)
            if (pair.Key.Item1 == player) { if (pair.Value.Go != null) Destroy(pair.Value.Go); gone.Add(pair.Key); }
        foreach (var key in gone) ghosts.Remove(key);
        foreach (var prop in sceneProps)
        {
            if (prop.Owner == player && prop.Following) EndFollow(prop);
            if (prop.Owner == player) prop.Owner = Nobody;
            if (prop.LastOwner == player) prop.LastOwner = Nobody;
        }
    }

    // ------------------------------------------------------------ local side

    void Claim(Prop prop)
    {
        if (prop.Following) EndFollow(prop);
        prop.Owner = prop.LastOwner = Me;
        prop.SentAt = -10f;
        prop.SentPos = new Vector3(1e6f, 1e6f, 1e6f);
    }

    void LateUpdate()
    {
        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        UpdateGhosts(dt);
        UpdateFollowers(dt);
        if (!Connected) return;

        if (now >= nextScan) { nextScan = now + ScanInterval; ScanForSpawned(); }

        for (int i = runtimeProps.Count - 1; i >= 0; i--)
        {
            var prop = runtimeProps[i];
            if (prop.Grab == null)
            {
                Send(KindGone, FlagRuntime, prop.Id, Vector3.zero, Quaternion.identity, prop.Key);
                byGrab.Remove(prop.Grab);
                runtimeProps.RemoveAt(i);
                continue;
            }
            if (!prop.Grab.gameObject.activeInHierarchy) continue;
            Stream(prop, now);
        }

        foreach (var prop in sceneProps)
        {
            if (prop.Grab == null)
            {
                if (prop.LastOwner == Me) { Send(KindGone, 0, prop.Id, Vector3.zero, Quaternion.identity, null); prop.LastOwner = Nobody; }
                continue;
            }
            if (prop.Following) continue;
            // Something I threw earlier moved again by itself: it's still mine.
            if (prop.Owner == Nobody && prop.LastOwner == Me && Moved(prop)) prop.Owner = Me;
            if (prop.Owner != Me) continue;
            Stream(prop, now);

            bool held = prop.Grab.isSelected;
            if (!held && now - prop.StillSince > ReleaseAfter && now - prop.ReleasedAt > 0.5f)
            {
                Send(KindRelease, 0, prop.Id, prop.T.position, prop.T.rotation, null);
                prop.Owner = Nobody;
                prop.Dirty = false;
            }
        }
    }

    static bool Moved(Prop prop) =>
        (prop.T.position - prop.SentPos).sqrMagnitude > 0.002f * 0.002f || Quaternion.Angle(prop.T.rotation, prop.SentRot) > 1f;

    void Stream(Prop prop, float now)
    {
        bool moved = Moved(prop);
        if (moved) prop.StillSince = now;
        byte flags = Flags(prop);
        Vector3 pos = prop.T.position;
        Quaternion rot = prop.T.rotation;

        if (moved && now - prop.SentAt >= SendInterval)
        {
            Send(KindPose, flags, prop.Id, pos, rot, prop.Key);
            prop.SentPos = pos;
            prop.SentRot = rot;
            prop.SentAt = now;
            prop.Dirty = true;
        }
        else if (prop.ForceRest || (prop.Dirty && !moved && now - prop.StillSince > RestAfter))
        {
            // The unreliable stream may have lost the last few: say where it ended up.
            Send(KindRest, flags, prop.Id, pos, rot, prop.Key);
            prop.SentPos = pos;
            prop.SentRot = rot;
            prop.SentAt = now;
            prop.Dirty = prop.ForceRest = false;
        }
    }

    static byte Flags(Prop prop)
    {
        byte flags = 0;
        if (prop.Runtime) flags |= FlagRuntime;
        if (prop.Grab.isSelected) flags |= FlagHeld;
        if (prop.Chip != null && prop.Chip.CurrentSpot != null) flags |= FlagPlaced;
        return flags;
    }

    // ------------------------------------------------------------ network

    void Send(byte kind, byte flags, ushort id, Vector3 pos, Quaternion rot, string key)
    {
        if (!Connected) return;
        if (multiplayer.IsHost)
        {
            foreach (ulong guest in multiplayer.GuestIds)
                SendTo(guest, kind, flags, Me, id, pos, rot, key);
        }
        else SendTo(NetworkManager.ServerClientId, kind, flags, Me, id, pos, rot, key);
    }

    void SendTo(ulong to, byte kind, byte flags, ulong owner, ushort id, Vector3 pos, Quaternion rot, string key, ulong except = ulong.MaxValue)
    {
        if (to == except) return;
        var network = multiplayer.Network;
        if (network == null || network.CustomMessagingManager == null) return;
        if (multiplayer.IsHost && !network.ConnectedClients.ContainsKey(to)) return;
        using var writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteValueSafe(kind);
        writer.WriteValueSafe(flags);
        writer.WriteValueSafe(owner);
        writer.WriteValueSafe(id);
        writer.WriteValueSafe(pos);
        writer.WriteValueSafe(rot);
        if ((flags & FlagRuntime) != 0)
        {
            var text = new FixedString64Bytes();
            text.CopyFromTruncated(key ?? "");
            writer.WriteValueSafe(text);
        }
        var delivery = kind == KindPose ? NetworkDelivery.UnreliableSequenced : NetworkDelivery.ReliableSequenced;
        network.CustomMessagingManager.SendNamedMessage(Message, to, writer, delivery);
    }

    void Receive(ulong sender, FastBufferReader reader)
    {
        if (!Connected) return;
        bool host = multiplayer.IsHost;
        if (host ? !multiplayer.IsGuestId(sender) : sender != NetworkManager.ServerClientId) return;
        if (reader.Length - reader.Position < 40) return;

        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out byte flags);
        reader.ReadValueSafe(out ulong owner);
        reader.ReadValueSafe(out ushort id);
        reader.ReadValueSafe(out Vector3 pos);
        reader.ReadValueSafe(out Quaternion rot);
        string key = null;
        if ((flags & FlagRuntime) != 0)
        {
            if (reader.Length - reader.Position < 2) return;
            reader.ReadValueSafe(out FixedString64Bytes text);
            key = text.ToString();
        }
        if (kind > KindGone || !Finite(pos) || !Finite(rot)) return;
        if (host) owner = sender; // guests only speak for themselves
        if (owner == Me) return;

        if ((flags & FlagRuntime) != 0) ApplyGhost(kind, flags, owner, id, pos, rot, key);
        else ApplyScene(kind, owner, id, pos, rot);

        if (!host) return;
        foreach (ulong guest in multiplayer.GuestIds)
            SendTo(guest, kind, flags, owner, id, pos, rot, key, sender);
    }

    static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z) && v.sqrMagnitude < 1e8f;
    static bool Finite(Quaternion q)
    {
        float l = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        return !float.IsNaN(l) && Mathf.Abs(l - 1f) < 0.1f;
    }

    // ------------------------------------------------------------ scene props

    void ApplyScene(byte kind, ulong owner, ushort id, Vector3 pos, Quaternion rot)
    {
        if (id >= sceneProps.Count) return;
        var prop = sceneProps[id];
        if (prop.Grab == null) return;
        // I'm holding it myself: mine wins.
        if (prop.Grab.isSelected) return;

        if (kind == KindGone)
        {
            if (prop.Following) EndFollow(prop);
            Destroy(prop.Grab.gameObject);
            return;
        }

        prop.LastOwner = owner;
        prop.TargetPos = pos;
        prop.TargetRot = rot;
        // Anything mine that was still settling is theirs now.
        prop.Dirty = prop.ForceRest = false;
        if (kind == KindRelease)
        {
            prop.Owner = Nobody;
            if (prop.Following) EndFollow(prop);
            else prop.T.SetPositionAndRotation(pos, rot);
            prop.SentPos = pos;
            prop.SentRot = rot;
            return;
        }
        prop.Owner = owner;
        if (!prop.Following) BeginFollow(prop);
    }

    void BeginFollow(Prop prop)
    {
        prop.Following = true;
        prop.WasKinematic = prop.Body.isKinematic;
        if (!prop.Body.isKinematic)
        {
            prop.Body.linearVelocity = Vector3.zero;
            prop.Body.angularVelocity = Vector3.zero;
        }
        prop.Body.isKinematic = true;
        prop.GrabWasEnabled = prop.Grab.enabled;
        prop.Grab.enabled = false;
    }

    void EndFollow(Prop prop)
    {
        prop.Following = false;
        prop.T.SetPositionAndRotation(prop.TargetPos, prop.TargetRot);
        prop.Body.isKinematic = prop.WasKinematic;
        if (!prop.WasKinematic)
        {
            prop.Body.linearVelocity = Vector3.zero;
            prop.Body.angularVelocity = Vector3.zero;
        }
        prop.Grab.enabled = prop.GrabWasEnabled;
        prop.SentPos = prop.TargetPos;
        prop.SentRot = prop.TargetRot;
    }

    void UpdateFollowers(float dt)
    {
        float k = 1f - Mathf.Exp(-22f * dt);
        foreach (var prop in sceneProps)
        {
            if (!prop.Following || prop.T == null) continue;
            Vector3 p = prop.T.position;
            // A teleport (back to the holder, say) snaps instead of flying across the room.
            if ((p - prop.TargetPos).sqrMagnitude > 4f) prop.T.SetPositionAndRotation(prop.TargetPos, prop.TargetRot);
            else prop.T.SetPositionAndRotation(Vector3.Lerp(p, prop.TargetPos, k), Quaternion.Slerp(prop.T.rotation, prop.TargetRot, k));
        }
    }

    // ------------------------------------------------------------ ghosts

    void ApplyGhost(byte kind, byte flags, ulong owner, ushort id, Vector3 pos, Quaternion rot, string key)
    {
        var handle = (owner, id);
        ghosts.TryGetValue(handle, out var ghost);
        if (kind == KindGone)
        {
            if (ghost != null && ghost.Go != null) Destroy(ghost.Go);
            ghosts.Remove(handle);
            return;
        }

        bool fresh = false;
        if (ghost == null || ghost.Go == null)
        {
            if (ghosts.Count >= MaxGhosts) return;
            ghost = new Ghost { Go = BuildGhost(key, owner), Key = key };
            ghosts[handle] = ghost;
            fresh = true;
        }
        ghost.RawPos = pos;
        ghost.Placed = (flags & FlagPlaced) != 0;
        ghost.Held = (flags & FlagHeld) != 0;
        ghost.TargetPos = ghost.Placed ? BesideSpot(pos, owner) : pos;
        ghost.TargetRot = rot;
        if (fresh) ghost.Go.transform.SetPositionAndRotation(ghost.TargetPos, rot);
    }

    void UpdateGhosts(float dt)
    {
        float k = 1f - Mathf.Exp(-22f * dt);
        foreach (var g in ghosts.Values)
        {
            if (g.Go == null) continue;
            var t = g.Go.transform;
            if ((t.position - g.TargetPos).sqrMagnitude > 4f) t.SetPositionAndRotation(g.TargetPos, g.TargetRot);
            else t.SetPositionAndRotation(Vector3.Lerp(t.position, g.TargetPos, k), Quaternion.Slerp(t.rotation, g.TargetRot, k));
        }
    }

    // A friend's bet sits beside the spot's centre (their own side of it),
    // a hair higher than yours so the two never flicker into each other.
    Vector3 BesideSpot(Vector3 pos, ulong owner)
    {
        int count = Physics.RaycastNonAlloc(pos + Vector3.up * 0.05f, Vector3.down, hits, 0.4f, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var spot = hits[i].collider.GetComponentInParent<BettingSpot>();
            if (spot == null) continue;
            Bounds b = hits[i].collider.bounds;
            float angle = (owner % 6) * 60f * Mathf.Deg2Rad + 0.5f;
            float reach = Mathf.Min(b.extents.x, b.extents.z) * 0.55f;
            return new Vector3(b.center.x + Mathf.Cos(angle) * reach, pos.y + 0.001f, b.center.z + Mathf.Sin(angle) * reach);
        }
        return pos;
    }

    // Just the meshes of whatever the other player is holding.
    GameObject BuildGhost(string key, ulong owner)
    {
        if (ghostRoot == null) ghostRoot = new GameObject("Other Players' Props").transform;
        var root = new GameObject("Ghost " + key + " (player " + owner + ")");
        root.transform.SetParent(ghostRoot, false);
        var template = FindTemplate(key);
        if (template == null)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(root.transform, false);
            ball.transform.localScale = Vector3.one * 0.05f;
            var r = ball.GetComponent<Renderer>();
            r.material.color = CasinoMultiplayer.ColorFor(owner);
            return root;
        }
        root.transform.localScale = template.transform.localScale;
        CopyMeshes(template.transform, root.transform);
        return root;
    }

    static void CopyMeshes(Transform from, Transform to)
    {
        var filter = from.GetComponent<MeshFilter>();
        var renderer = from.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null && renderer.enabled && filter.sharedMesh != null)
        {
            to.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = to.gameObject.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
        }
        var skinned = from.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.enabled && skinned.sharedMesh != null)
        {
            to.gameObject.AddComponent<MeshFilter>().sharedMesh = skinned.sharedMesh;
            to.gameObject.AddComponent<MeshRenderer>().sharedMaterials = skinned.sharedMaterials;
        }
        foreach (Transform child in from)
        {
            if (!child.gameObject.activeSelf) continue;
            var next = new GameObject(child.name).transform;
            next.SetParent(to, false);
            next.localPosition = child.localPosition;
            next.localRotation = child.localRotation;
            next.localScale = child.localScale;
            CopyMeshes(child, next);
        }
    }

    // A prefab or template with that name: the chip pockets' chips, the
    // bar's drinks, or anything grabbable in the scene.
    GameObject FindTemplate(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (templates.TryGetValue(key, out var cached) && cached != null) return cached;
        GameObject found = null;
        foreach (var pocket in FindObjectsByType<ChipDispenser>(FindObjectsInactive.Include))
            if (pocket.ChipPrefab != null && KeyOf(pocket.ChipPrefab.name) == key) { found = pocket.ChipPrefab.gameObject; break; }
        if (found == null)
            foreach (var grab in Resources.FindObjectsOfTypeAll<XRGrabInteractable>())
                if (grab != null && KeyOf(grab.name) == key && !byGrab.ContainsKey(grab)) { found = grab.gameObject; break; }
        if (found == null)
            foreach (var grab in Resources.FindObjectsOfTypeAll<XRGrabInteractable>())
                if (grab != null && KeyOf(grab.name) == key) { found = grab.gameObject; break; }
        templates[key] = found;
        return found;
    }
}
