using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace PicoNet
{
    public class PeerDiscoverySession : IDisposable
    {
        public bool DiscoveryActive { get; private set; }
        public int DiscoveryBroadcastInterval { get; set; } = 3000;

        private const string Handshake = "HANDSHAKE";
        private readonly string _appId;
        private readonly int _port;
        private readonly IPAddress[] _localAddress;
        private readonly NetManager _netManager;
        private readonly EventBasedNetListener _listener;
        private readonly NetDataWriter _dataWriter = new NetDataWriter();
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly List<IPAddress> _pendingPeers;

        public PeerDiscoverySession(string appId, int port, NetManager netManager, EventBasedNetListener netListener)
        {
            _appId = appId;
            _port = port;
            _localAddress = GetLocalIPAddresses();
            _pendingPeers = new List<IPAddress>();
            _netManager = netManager;
            _listener = netListener;
            _listener.NetworkReceiveUnconnectedEvent += HandleOnNetworkReceiveUnconnectedEvent;
            _listener.ConnectionRequestEvent += HandleOnConnectionRequestEvent;
            _listener.PeerDisconnectedEvent += HandleOnPeerDisconnectedEvent;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void StartDiscovery()
        {
            if (DiscoveryActive)
            {
                return;
            }
            DiscoveryActive = true;
            
            Task.Run(async () =>
            {
                var dataWriter = new NetDataWriter();
                dataWriter.PutArray(new [] { _appId, Handshake });
                
                while (DiscoveryActive)
                {
                    await Task.Delay(DiscoveryBroadcastInterval).ConfigureAwait(false);
                    _netManager.SendBroadcast(dataWriter, _port);
                }

                dataWriter.Reset();
            }, _cancellationTokenSource.Token);
        }

        public void StopDiscovery()
        {
            if (!DiscoveryActive)
            {
                return;
            }
            DiscoveryActive = false;
            
            _cancellationTokenSource.Cancel();
        }

        private void HandleOnNetworkReceiveUnconnectedEvent(IPEndPoint remoteEndpoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // Reject if its from own network adapter
            if (_localAddress.Any(a => Equals(remoteEndpoint.Address, a)))
            {
                return;
            }
            // Reject if its from already connected peer
            if (_netManager.ConnectedPeerList.Any(netPeer => Equals(remoteEndpoint.Address, netPeer.EndPoint.Address)))
            {
                return;
            }
            // Reject if payload is empty or incorrect
            if (reader.IsNull)
            {
                return;
            }

            if (!reader.TryGetStringArray(out var data))
            {
                Debug.LogWarning("Bad data thingy");
                return;
            }
            if (data.Length != 2 || data[0] != _appId 
                                 || data[1] != Handshake)
            {
                return;
            }
            
            // Add to pending and send connection request
            if (!_pendingPeers.Any(ipAddress => Equals(remoteEndpoint.Address, ipAddress)))
            {
                _pendingPeers.Add(remoteEndpoint.Address);
            }
            _dataWriter.Reset();
            _dataWriter.PutArray(new [] { _appId, Handshake });
            _netManager.Connect(remoteEndpoint, _dataWriter);
        }

        private void HandleOnConnectionRequestEvent(ConnectionRequest request)
        {
            // Reject if its from own network adapter
            if (_localAddress.Any(a => Equals(request.RemoteEndPoint.Address, a)))
            {
                return;
            }
            // Reject if its from already connected peer
            if (_netManager.ConnectedPeerList.Any(netPeer => Equals(request.RemoteEndPoint.Address, netPeer.EndPoint.Address)))
            {
                return;
            }
            // Reject if payload is empty or incorrect
            if (request.Data.IsNull)
            {
                request.Reject();
                return;
            }
            var data = request.Data.GetStringArray();
            if (data.Length != 2 || data[0] != _appId
                                 || data[1] != Handshake)
            {
                Debug.Log($"[PeerDiscoverySession] Rejected connection request.");
                request.Reject();
                return;
            }
            
            // Remove from pending and accept request
            var pendingPeer =
                _pendingPeers.SingleOrDefault(ipAddress => Equals(request.RemoteEndPoint.Address, ipAddress));
            if (pendingPeer != null)
            {
                _pendingPeers.Remove(pendingPeer);
            }
            request.Accept();
            Debug.Log($"[PeerDiscoverySession] Accepted connection request.");
        }
        
        private void HandleOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectinfo)
        {
            var pendingPeer =
                _pendingPeers.SingleOrDefault(ipAddress => Equals(peer.EndPoint.Address, ipAddress));
            if (pendingPeer != null)
            {
                _pendingPeers.Remove(pendingPeer);
            }
        }
        
        private static IPAddress[] GetLocalIPAddresses()
        {
            var addresses = Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();

            if (!addresses.Any())
            {
                return null;
            }

            return addresses;
        }

        public void Dispose()
        {
            StopDiscovery();
            _listener.NetworkReceiveUnconnectedEvent -= HandleOnNetworkReceiveUnconnectedEvent;
            _listener.ConnectionRequestEvent -= HandleOnConnectionRequestEvent;
            _listener.PeerDisconnectedEvent -= HandleOnPeerDisconnectedEvent;
        }
    }
}
