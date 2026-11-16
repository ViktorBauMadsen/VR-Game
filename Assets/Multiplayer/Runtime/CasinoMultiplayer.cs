using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Casino.Multiplayer
{
    // Same-Wi-Fi multiplayer for up to four players. One player hosts, the
    // others join (found automatically through LanDiscovery, or by IP). Every
    // player's head and hands are sent to the host, which relays them to the
    // other guests. Game state on top of that (the daily quota run) uses the
    // same connection through Network.CustomMessagingManager - see CasinoRun.
    //
    // This object has no UI of its own; the Friends tab of CasinoMenu drives it.
    [DefaultExecutionOrder(10000)]
    public sealed class CasinoMultiplayer : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        const string PoseMessage = "Casino.Pose.v2";
        const string Handshake = "Casino.v2:";
        const ushort Port = 7777;
        const int PoseBytes = 84;         // three Vector3/Quaternion pairs
        const int TaggedPoseBytes = 92;   // + the ulong id of whose pose it is

        static readonly Color[] RemoteColors =
        {
            new Color(1f, .65f, .2f), new Color(.45f, .9f, .4f), new Color(.9f, .4f, .85f), new Color(.95f, .9f, .3f)
        };

        public static CasinoMultiplayer Instance { get; private set; }

        NetworkManager network;
        UnityTransport transport;
        XROrigin rig;
        Transform head, left, right;
        SimpleAvatar localAvatar;
        Material avatarMaterial;
        LanDiscovery discovery;
        float nextSend, joinStarted;
        bool joining, stopping;
        Vector3 originalRigPosition;
        readonly HashSet<ulong> guests = new HashSet<ulong>();
        readonly Dictionary<ulong, SimpleAvatar> remotes = new Dictionary<ulong, SimpleAvatar>();
        readonly Dictionary<ulong, AvatarPose> relayPoses = new Dictionary<ulong, AvatarPose>();
        readonly Dictionary<ulong, float> relayTimes = new Dictionary<ulong, float>();
        readonly List<Renderer> hiddenControllerRenderers = new List<Renderer>();

        public string Status { get; private set; } = "Offline.";
        public string LocalAddresses { get; private set; } = "";
        // False if the scene has no usable XR rig - nothing else here works then.
        public bool Ready { get; private set; }
        public bool Joining => joining;
        public bool InSession => Ready && network.IsListening && !stopping;
        public bool Busy => !Ready || joining || stopping || network.IsListening || network.ShutdownInProgress;
        public bool IsHost => InSession && network.IsServer;
        public bool IsGuest => InSession && !network.IsServer && network.IsConnectedClient;
        public int PlayerCount => IsHost ? 1 + guests.Count : IsGuest ? Mathf.Max(2, 1 + remotes.Count) : 1;
        public IReadOnlyCollection<ulong> GuestIds => guests;
        public NetworkManager Network => network;
        public LanDiscovery Discovery => discovery;
        public Transform Head => head;

        // Raised right after hosting/joining starts - the moment to register
        // named message handlers on Network.CustomMessagingManager.
        public event Action SessionStarted;
        public event Action SessionEnded;
        public event Action<ulong> GuestJoined;
        public event Action<ulong> GuestLeft;

        public bool IsGuestId(ulong id) => guests.Contains(id);

        // This player's id in the session (the host is always 0).
        public ulong LocalId => InSession ? network.LocalClientId : 0UL;

        public static Color ColorFor(ulong id) => RemoteColors[(int)(id % (ulong)RemoteColors.Length)];

        // Small helpers for game messages. Call Register from SessionStarted.
        public void Register(string message, CustomMessagingManager.HandleNamedMessageDelegate handler)
        {
            if (Ready && network.CustomMessagingManager != null)
                network.CustomMessagingManager.RegisterNamedMessageHandler(message, handler);
        }

        public void SendToHost(string message, FastBufferWriter writer)
        {
            if (!IsGuest || network.CustomMessagingManager == null) return;
            network.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        public void SendToGuests(string message, FastBufferWriter writer, ulong except = ulong.MaxValue)
        {
            if (!IsHost || network.CustomMessagingManager == null) return;
            foreach (ulong guest in guests)
                if (guest != except && network.ConnectedClients.ContainsKey(guest))
                    network.CustomMessagingManager.SendNamedMessage(message, guest, writer, NetworkDelivery.ReliableSequenced);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
            Install();
        }

        static void SceneLoaded(Scene scene, LoadSceneMode mode) => Install();
        static void Install()
        {
            if (SceneManager.GetActiveScene().name != "SampleScene" || FindAnyObjectByType<CasinoMultiplayer>() != null) return;
            new GameObject("Casino Multiplayer").AddComponent<CasinoMultiplayer>();
        }

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void Start()
        {
            rig = FindAnyObjectByType<XROrigin>();
            if (rig != null)
            {
                head = rig.Camera != null ? rig.Camera.transform : null;
                left = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Left Controller");
                right = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Right Controller");
                originalRigPosition = rig.transform.position;
            }
            if (head == null || left == null || right == null)
            {
                Debug.LogError("Casino multiplayer requires an XR Origin with a camera, Left Controller and Right Controller.");
                Status = "Multiplayer unavailable: no XR rig found.";
                enabled = false;
                return;
            }
            avatarMaterial = Resources.Load<Material>("Multiplayer/Avatar");
            if (avatarMaterial == null)
            {
                Debug.LogError("Missing avatar material. Run Tools > Multiplayer > Prepare and Validate.");
                Status = "Multiplayer unavailable: run Tools > Multiplayer > Prepare and Validate.";
                enabled = false;
                return;
            }

            // The rigged hand models (HandPoser) are the local player's hands
            // when the scene has them; only fall back to the blocky ones if not.
            if (FindAnyObjectByType<HandPoser>(FindObjectsInactive.Include) == null)
                localAvatar = CreateAvatar("Your hands", new Color(.2f, .75f, 1f), true);
            foreach (var visual in rig.GetComponentsInChildren<Transform>(true).Where(t => t.name == "UniversalController"))
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { hiddenControllerRenderers.Add(renderer); renderer.enabled = false; }

            // A separate root lets Netcode own its lifecycle without persisting the local rig/UI.
            var networkObject = new GameObject("Casino Network Manager");
            transport = networkObject.AddComponent<UnityTransport>();
            network = networkObject.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport, EnableSceneManagement = false,
                ConnectionApproval = true, TickRate = 30, ProtocolVersion = 2
            };
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
            network.OnTransportFailure += TransportFailed;

            var addresses = new List<string>();
            try
            {
                addresses.AddRange(Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString()));
            }
            catch (SocketException) { }
            LocalAddresses = string.Join(" / ", addresses);
            discovery = new LanDiscovery(addresses);
            Status = "Offline. Host a game or join a friend's.";
            Ready = true;
        }

        SimpleAvatar CreateAvatar(string label, Color color, bool local)
        {
            var avatar = new GameObject(label).AddComponent<SimpleAvatar>();
            avatar.transform.SetParent(transform, false);
            avatar.Initialize(avatarMaterial, color, local);
            return avatar;
        }

        public void Host()
        {
            if (Busy) return;
            guests.Clear();
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            StartSession(true);
        }

        public void Join(string address)
        {
            if (Busy) return;
            if (address == null || !IPAddress.TryParse(address.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
                || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast) || address.Trim().Split('.').Length != 4)
            {
                Status = "That is not a valid IP. It looks like 192.168.1.20.";
                return;
            }
            transport.SetConnectionData(ip.ToString(), Port);
            network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Handshake + SceneManager.GetActiveScene().name);
            joining = true;
            joinStarted = Time.unscaledTime;
            Status = "Connecting to " + ip + "...";
            StartSession(false);
        }

        void StartSession(bool host)
        {
            try
            {
                bool started = host ? network.StartHost() : network.StartClient();
                if (!started) { Leave(); Status = "Could not start networking. Is port 7777 already in use?"; return; }
                network.CustomMessagingManager.RegisterNamedMessageHandler(PoseMessage, ReceivePose);
                network.CustomMessagingManager.RegisterNamedMessageHandler(LookMessage, ReceiveLook);
                looks.Clear();
                nextLook = 0f;
                if (host) Status = HostStatus();
                SessionStarted?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Leave();
                Status = "Network start failed: " + ex.Message;
            }
        }

        string HostStatus() => "Hosting - " + (1 + guests.Count) + "/" + MaxPlayers + " players.";

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool isHost = request.ClientNetworkId == NetworkManager.ServerClientId;
            bool compatible = isHost || (request.Payload != null
                && Encoding.UTF8.GetString(request.Payload) == Handshake + SceneManager.GetActiveScene().name);
            bool room = isHost || guests.Count < MaxPlayers - 1;
            response.Approved = compatible && room;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = !compatible ? "Game version mismatch - both players need the same build."
                : "Lobby full (" + MaxPlayers + " players maximum).";
            if (response.Approved && !isHost) guests.Add(request.ClientNetworkId);
        }

        void Connected(ulong id)
        {
            if (network.IsServer)
            {
                Status = HostStatus();
                if (id != NetworkManager.ServerClientId && guests.Contains(id)) GuestJoined?.Invoke(id);
                return;
            }
            if (id != network.LocalClientId) return;
            joining = false;
            // Everyone spawns on the same spot; step aside only if we are still standing on it.
            Vector3 offset = rig.transform.position - originalRigPosition;
            offset.y = 0f;
            if (offset.magnitude < 1f) rig.transform.position += rig.transform.right * 1.2f;
            Status = "Connected.";
        }

        void Disconnected(ulong id)
        {
            if (network.IsServer && id != network.LocalClientId)
            {
                // A rejected player never made it into the guest list.
                if (!guests.Remove(id)) return;
                RemoveRemote(id);
                relayPoses.Remove(id);
                relayTimes.Remove(id);
                if (!stopping) Status = "A player left. " + HostStatus();
                GuestLeft?.Invoke(id);
                return;
            }
            if (!network.IsServer && id != network.LocalClientId) { RemoveRemote(id); return; }

            ClearRemotes();
            bool wasStopping = stopping;
            joining = false;
            if (!wasStopping)
                Status = string.IsNullOrEmpty(network.DisconnectReason)
                    ? "Disconnected. Check that you are on the same Wi-Fi as the host."
                    : "Disconnected: " + network.DisconnectReason;
            SessionEnded?.Invoke();
        }

        void TransportFailed() { Leave(); Status = "Network connection failed. Check Wi-Fi, then retry."; }

        public void Leave()
        {
            if (!Ready) return;
            bool wasInSession = network.IsListening;
            joining = false;
            guests.Clear();
            relayPoses.Clear();
            relayTimes.Clear();
            ClearRemotes();
            if (network.IsListening || network.ShutdownInProgress)
            {
                stopping = true;
                network.Shutdown();
            }
            Status = "Offline. Host a game or join a friend's.";
            if (wasInSession) SessionEnded?.Invoke();
        }

        void RemoveRemote(ulong id)
        {
            if (remotes.TryGetValue(id, out var avatar) && avatar != null) Destroy(avatar.gameObject);
            remotes.Remove(id);
            looks.Remove(id);
        }

        void ClearRemotes()
        {
            foreach (var avatar in remotes.Values)
                if (avatar != null) Destroy(avatar.gameObject);
            remotes.Clear();
        }

        void Update()
        {
            if (!Ready) return;
            if (stopping && !network.IsListening && !network.ShutdownInProgress) stopping = false;
            if (joining && Time.unscaledTime - joinStarted > 12f)
            {
                Leave();
                Status = "Could not reach the host. Same Wi-Fi? School/guest networks often block this - try a phone hotspot.";
            }
            discovery.Tick(IsHost, PlayerCount, network.IsListening || stopping);
        }

        void LateUpdate()
        {
            if (!Ready) return;
            var pose = new AvatarPose
            {
                HeadPosition = head.position, HeadRotation = head.rotation,
                LeftPosition = left.position, LeftRotation = left.rotation,
                RightPosition = right.position, RightRotation = right.rotation
            };
            if (localAvatar != null) localAvatar.Apply(pose, true);
            if (!network.IsListening || !network.IsConnectedClient || stopping || Time.unscaledTime < nextSend) return;
            var messaging = network.CustomMessagingManager;
            if (messaging == null) return;
            nextSend = Time.unscaledTime + .05f;
            SendLooks(messaging);

            if (!network.IsServer)
            {
                using var writer = new FastBufferWriter(128, Allocator.Temp);
                writer.WriteNetworkSerializable(pose);
                messaging.SendNamedMessage(PoseMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
                return;
            }

            // Host: everyone gets the host's pose plus every other guest's latest one.
            foreach (ulong guest in guests)
            {
                if (!network.ConnectedClients.ContainsKey(guest)) continue;
                SendTagged(messaging, guest, NetworkManager.ServerClientId, pose);
                foreach (var other in relayPoses)
                    if (other.Key != guest && Time.unscaledTime - relayTimes[other.Key] < 1f)
                        SendTagged(messaging, guest, other.Key, other.Value);
            }
        }

        // ------------------------------------------------------- wardrobe
        // What each player chose at the mirror (AvatarCustomizer) is sent
        // along every couple of seconds, so everyone sees everyone else's
        // real character. It is a handful of bytes: one number per category.

        const string LookMessage = "Casino.Look.v1";
        const int MaxLookBytes = 16;
        readonly Dictionary<ulong, byte[]> looks = new Dictionary<ulong, byte[]>();
        AvatarCustomizer localLook;
        float nextLook;

        void SendLooks(CustomMessagingManager messaging)
        {
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 2f;
            if (localLook == null)
                foreach (var candidate in FindObjectsByType<AvatarCustomizer>())
                    if (!candidate.Remote) { localLook = candidate; break; }
            byte[] mine = localLook != null ? localLook.GetSelections() : null;

            if (!network.IsServer)
            {
                if (mine != null) SendLook(messaging, NetworkManager.ServerClientId, null, mine);
                return;
            }
            foreach (ulong guest in guests)
            {
                if (!network.ConnectedClients.ContainsKey(guest)) continue;
                if (mine != null) SendLook(messaging, guest, NetworkManager.ServerClientId, mine);
                foreach (var other in looks)
                    if (other.Key != guest) SendLook(messaging, guest, other.Key, other.Value);
            }
        }

        // who = null when a guest reports its own look to the host.
        static void SendLook(CustomMessagingManager messaging, ulong to, ulong? who, byte[] look)
        {
            if (look.Length == 0 || look.Length > MaxLookBytes) return;
            using var writer = new FastBufferWriter(64, Allocator.Temp);
            if (who.HasValue) writer.WriteValueSafe(who.Value);
            writer.WriteValueSafe((byte)look.Length);
            foreach (byte choice in look) writer.WriteValueSafe(choice);
            messaging.SendNamedMessage(LookMessage, to, writer, NetworkDelivery.ReliableSequenced);
        }

        void ReceiveLook(ulong sender, FastBufferReader reader)
        {
            if (stopping) return;
            ulong who = sender;
            if (network.IsServer)
            {
                if (!guests.Contains(sender)) return;
            }
            else
            {
                if (sender != NetworkManager.ServerClientId || reader.Length - reader.Position < 9) return;
                reader.ReadValueSafe(out who);
                if (who == network.LocalClientId) return;
            }
            if (reader.Length - reader.Position < 1) return;
            reader.ReadValueSafe(out byte count);
            if (count == 0 || count > MaxLookBytes || reader.Length - reader.Position != count) return;
            var look = new byte[count];
            for (int i = 0; i < count; i++) reader.ReadValueSafe(out look[i]);

            if (!looks.ContainsKey(who) && looks.Count >= MaxPlayers) return;
            looks[who] = look;
            if (remotes.TryGetValue(who, out var avatar) && avatar != null) avatar.SetLook(look);
        }

        static void SendTagged(CustomMessagingManager messaging, ulong to, ulong who, AvatarPose pose)
        {
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteValueSafe(who);
            writer.WriteNetworkSerializable(pose);
            messaging.SendNamedMessage(PoseMessage, to, writer, NetworkDelivery.UnreliableSequenced);
        }

        void ReceivePose(ulong sender, FastBufferReader reader)
        {
            if (stopping) return;
            int remaining = reader.Length - reader.Position;
            ulong who;
            AvatarPose pose;
            if (network.IsServer)
            {
                // Guests only ever speak for themselves.
                if (!guests.Contains(sender) || remaining != PoseBytes) return;
                reader.ReadNetworkSerializable(out pose);
                if (!pose.IsValid) return;
                who = sender;
                relayPoses[who] = pose;
                relayTimes[who] = Time.unscaledTime;
            }
            else
            {
                if (sender != NetworkManager.ServerClientId || remaining != TaggedPoseBytes) return;
                reader.ReadValueSafe(out who);
                reader.ReadNetworkSerializable(out pose);
                if (!pose.IsValid || who == network.LocalClientId) return;
            }

            if (!remotes.TryGetValue(who, out var avatar) || avatar == null)
            {
                if (remotes.Count >= MaxPlayers) return;
                avatar = CreateAvatar("Other player " + who, ColorFor(who), false);
                remotes[who] = avatar;
            }
            avatar.FloorY = rig.transform.position.y;
            avatar.Apply(pose, false);
            if (looks.TryGetValue(who, out var look)) avatar.SetLook(look);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Ready && network.IsListening) { Leave(); Status = "Headset went to sleep - host or join again to reconnect."; }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            discovery?.Dispose();
            foreach (var renderer in hiddenControllerRenderers)
                if (renderer != null) renderer.enabled = true;
            if (network == null) return;
            network.OnClientConnectedCallback -= Connected;
            network.OnClientDisconnectCallback -= Disconnected;
            network.OnTransportFailure -= TransportFailed;
            network.Shutdown();
            Destroy(network.gameObject);
        }
    }
}
