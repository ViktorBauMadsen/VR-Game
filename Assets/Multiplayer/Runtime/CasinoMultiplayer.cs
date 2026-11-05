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
    [DefaultExecutionOrder(10000)]
    public sealed class CasinoMultiplayer : MonoBehaviour
    {
        const string PoseMessage = "Casino.Pose.v1";
        const ushort Port = 7777;
        NetworkManager network;
        UnityTransport transport;
        XROrigin rig;
        Transform head, left, right;
        SimpleAvatar localAvatar, remoteAvatar;
        Material avatarMaterial;
        float nextSend, joinStarted;
        bool joining, stopping;
        ulong? guest;
        Vector3 originalRigPosition;
        readonly List<Renderer> hiddenControllerRenderers = new List<Renderer>();
        public string Status { get; private set; } = "Offline — choose Host or enter the host IP and Join.";
        public bool Busy => joining || stopping || network.IsListening || network.ShutdownInProgress;
        public string LocalAddresses { get; private set; }
        public Transform Head => head;

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
                enabled = false;
                return;
            }
            avatarMaterial = Resources.Load<Material>("Multiplayer/Avatar");
            if (avatarMaterial == null)
            {
                Debug.LogError("Missing avatar material. Run Tools > Multiplayer > Prepare and Validate.");
                enabled = false;
                return;
            }
            localAvatar = CreateAvatar(true);
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
                ConnectionApproval = true, TickRate = 30, ProtocolVersion = 1
            };
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
            network.OnTransportFailure += TransportFailed;
            try
            {
                LocalAddresses = string.Join(" / ", Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString()));
            }
            catch (SocketException) { LocalAddresses = "Check headset Wi-Fi details for its IPv4 address"; }
            if (string.IsNullOrEmpty(LocalAddresses)) LocalAddresses = "Check headset Wi-Fi details for its IPv4 address";
            gameObject.AddComponent<LobbyPanel>().Initialize(this);
        }

        SimpleAvatar CreateAvatar(bool local)
        {
            var avatar = new GameObject(local ? "Your hands" : "Other player").AddComponent<SimpleAvatar>();
            avatar.transform.SetParent(transform, false);
            avatar.Initialize(avatarMaterial, local ? new Color(.2f, .75f, 1f) : new Color(1f, .65f, .2f), local);
            return avatar;
        }

        public void Host()
        {
            if (Busy) return;
            guest = null;
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            StartSession(true);
        }

        public void Join(string address)
        {
            if (Busy) return;
            if (!IPAddress.TryParse(address.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
                || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast))
            {
                Status = "Enter the host's IPv4 address, for example 192.168.1.20.";
                return;
            }
            transport.SetConnectionData(ip.ToString(), Port);
            network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes("Casino.v1:" + SceneManager.GetActiveScene().name);
            joining = true;
            joinStarted = Time.unscaledTime;
            Status = "Connecting to " + ip + "…";
            StartSession(false);
        }

        void StartSession(bool host)
        {
            try
            {
                bool started = host ? network.StartHost() : network.StartClient();
                if (!started) { Leave(); Status = "Could not start networking. Check the log and port 7777."; return; }
                network.CustomMessagingManager.RegisterNamedMessageHandler(PoseMessage, ReceivePose);
                if (host) Status = "Hosting — 1/2 players. Share your IP below.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Leave();
                Status = "Network start failed: " + ex.Message;
            }
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool isHost = request.ClientNetworkId == NetworkManager.ServerClientId;
            bool compatible = isHost || Encoding.UTF8.GetString(request.Payload) == "Casino.v1:" + SceneManager.GetActiveScene().name;
            response.Approved = compatible && (isHost || !guest.HasValue);
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = compatible ? "Lobby full (2 players maximum)." : "Game version or scene mismatch.";
            if (response.Approved && !isHost) guest = request.ClientNetworkId;
        }

        void Connected(ulong id)
        {
            joining = false;
            if (!network.IsServer && id == network.LocalClientId)
                rig.transform.position = originalRigPosition + rig.transform.right * 1.2f;
            Status = network.IsHost && network.ConnectedClientsIds.Count < 2
                ? "Hosting — 1/2 players." : "Connected — 2/2 players.";
        }

        void Disconnected(ulong id)
        {
            // A rejected third player must not remove the connected guest's avatar/status.
            if (network.IsServer && id != network.LocalClientId && id != guest) return;
            if (guest == id) guest = null;
            ClearRemote();
            if (stopping) return;
            if (network.IsServer && id != network.LocalClientId)
                Status = "Guest left — hosting, 1/2 players.";
            else
            {
                joining = false;
                Status = string.IsNullOrEmpty(network.DisconnectReason)
                    ? "Disconnected. Check Wi-Fi and the host IP, then retry."
                    : "Disconnected: " + network.DisconnectReason;
            }
        }

        void TransportFailed() { Leave(); Status = "Network transport failed. Check Wi-Fi, then retry."; }

        public void Leave()
        {
            joining = false;
            guest = null;
            ClearRemote();
            if (network != null && (network.IsListening || network.ShutdownInProgress))
            {
                stopping = true;
                network.Shutdown();
            }
            Status = "Offline — you can host or join again.";
        }

        void ClearRemote()
        {
            if (remoteAvatar != null) Destroy(remoteAvatar.gameObject);
            remoteAvatar = null;
        }

        void Update()
        {
            if (stopping && !network.IsListening && !network.ShutdownInProgress) stopping = false;
            if (joining && Time.unscaledTime - joinStarted > 15f)
            {
                Leave();
                Status = "Timed out. Use the same private Wi-Fi/hotspot; campus Wi-Fi may block peers.";
            }
        }

        void LateUpdate()
        {
            var pose = new AvatarPose
            {
                HeadPosition = head.position, HeadRotation = head.rotation,
                LeftPosition = left.position, LeftRotation = left.rotation,
                RightPosition = right.position, RightRotation = right.rotation
            };
            localAvatar.Apply(pose, true);
            if (!network.IsConnectedClient || stopping || Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + .05f;
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(pose);
            if (network.IsServer)
            {
                foreach (var id in network.ConnectedClientsIds)
                    if (id != NetworkManager.ServerClientId)
                        network.CustomMessagingManager.SendNamedMessage(PoseMessage, id, writer, NetworkDelivery.UnreliableSequenced);
            }
            else network.CustomMessagingManager.SendNamedMessage(PoseMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
        }

        void ReceivePose(ulong sender, FastBufferReader reader)
        {
            if (stopping || (network.IsServer ? sender != guest : sender != NetworkManager.ServerClientId)) return;
            // Exactly three Vector3/Quaternion pairs; reject truncated or unexpected payloads.
            if (reader.Length - reader.Position != 84) return;
            reader.ReadNetworkSerializable(out AvatarPose pose);
            if (!pose.IsValid) return;
            if (remoteAvatar == null) remoteAvatar = CreateAvatar(false);
            remoteAvatar.Apply(pose, false);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && network != null) { Leave(); Status = "Headset paused — host or join again to reconnect."; }
        }

        void OnDestroy()
        {
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
