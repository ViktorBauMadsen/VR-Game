using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Casino.Multiplayer
{
    // Finds games on the same Wi-Fi so nobody has to type an IP address: a
    // host shouts a tiny UDP broadcast once a second, everyone else listens
    // and lists whoever they heard recently. Purely a convenience - if the
    // network blocks broadcasts, joining by IP still works.
    public sealed class LanDiscovery : IDisposable
    {
        public struct Host
        {
            public string Address;
            public int Players;
            public float Seen;
        }

        const int Port = 7778;
        const string Magic = "CASINOVR2";
        const float Lifetime = 4f;

        readonly Dictionary<string, Host> hosts = new Dictionary<string, Host>();
        readonly List<Host> list = new List<Host>();
        readonly HashSet<string> ownAddresses;
        UdpClient listener, sender;
        float nextBeacon, nextListenAttempt;

        // Bumped whenever the visible list changes, so UI knows to redraw.
        public int Version { get; private set; }
        public IReadOnlyList<Host> Hosts => list;

        public LanDiscovery(IEnumerable<string> localAddresses)
        {
            ownAddresses = new HashSet<string>(localAddresses);
        }

        public void Tick(bool hosting, int players, bool inSession)
        {
            float now = Time.unscaledTime;
            if (hosting && now >= nextBeacon)
            {
                nextBeacon = now + 1f;
                Shout(players);
            }
            if (!inSession) Listen(now);
            else if (hosts.Count > 0) { hosts.Clear(); Rebuild(); }
        }

        void Shout(int players)
        {
            try
            {
                if (sender == null) sender = new UdpClient { EnableBroadcast = true };
                byte[] data = Encoding.ASCII.GetBytes(Magic + "|" + players);
                sender.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, Port));
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                sender?.Close();
                sender = null;
            }
        }

        void Listen(float now)
        {
            if (listener == null)
            {
                if (now < nextListenAttempt) return;
                nextListenAttempt = now + 5f;
                try
                {
                    listener = new UdpClient();
                    listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
                {
                    listener?.Close();
                    listener = null;
                    return;
                }
            }

            bool changed = false;
            try
            {
                // Bounded so a flood of packets can't stall a frame.
                for (int i = 0; i < 16 && listener.Available > 0; i++)
                {
                    IPEndPoint from = null;
                    byte[] data = listener.Receive(ref from);
                    if (from == null || data.Length > 32) continue;
                    string[] parts = Encoding.ASCII.GetString(data).Split('|');
                    if (parts.Length != 2 || parts[0] != Magic || !int.TryParse(parts[1], out int players)) continue;
                    string address = from.Address.ToString();
                    if (ownAddresses.Contains(address)) continue;
                    if (!hosts.TryGetValue(address, out var known) || known.Players != players)
                    {
                        if (!hosts.ContainsKey(address) && hosts.Count >= 8) continue;
                        changed = true;
                    }
                    hosts[address] = new Host { Address = address, Players = Mathf.Clamp(players, 1, CasinoMultiplayer.MaxPlayers), Seen = now };
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
            {
                listener?.Close();
                listener = null;
            }

            List<string> stale = null;
            foreach (var pair in hosts)
                if (now - pair.Value.Seen > Lifetime) (stale ??= new List<string>()).Add(pair.Key);
            if (stale != null)
            {
                foreach (string address in stale) hosts.Remove(address);
                changed = true;
            }
            if (changed) Rebuild();
        }

        void Rebuild()
        {
            list.Clear();
            list.AddRange(hosts.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));
            Version++;
        }

        public void Dispose()
        {
            listener?.Close();
            sender?.Close();
            listener = sender = null;
        }
    }
}
