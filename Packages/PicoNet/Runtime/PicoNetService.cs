using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.Events;

namespace PicoNet
{
    public class PicoNetService : MonoBehaviour
    {
        public static PicoNetService Instance { get; private set; }
        
        public event EventHandler<NetPeer> OnPeerConnected;
        public event EventHandler<int> OnConnectedPeersChanged;
        public event EventHandler OnPeerDisconnected;

        /// <summary>
        /// Is Service active state
        /// </summary>
        public bool IsRunning { get; private set; }
        /// <summary>
        /// The App Id used for identifying this client with other peers
        /// </summary>
        public string AppId { get; set; }
        /// <summary>
        /// Amount of connected peers in the network
        /// </summary>
        public List<NetPeer> ConnectedPeers => _netManager.ConnectedPeerList;
        /// <summary>
        /// Interval in milliseconds to check for incoming messages
        /// </summary>
        public int PoolingInterval { get; set; }
        /// <summary>
        /// Used network port for when starting the service.
        /// </summary>
        public int NetworkPort { get; set; }

        [Header("Settings")]
        [SerializeField]
        private string appId = "PROVIDE_UNIQUE_ID";
        [SerializeField]
        private int poolingInterval = 32;
        [SerializeField]
        private int networkPort = 10515;
        [SerializeField]
        private bool ipV6Mode = false;
        [SerializeField]
        private bool autoStartService = true;
        [Header("Events")]
        [SerializeField]
        private UnityEvent onServiceStarted;
        [SerializeField]
        private UnityEvent onServiceStopped;
        [SerializeField]
        private UnityEvent onPeerConnected;
        [SerializeField]
        private UnityEvent onPeerDisconnected;
        
        private static readonly Queue<Action> Queue = new Queue<Action>(8);
        
        private NetManager _netManager;
        private EventBasedNetListener _listener;
        private PeerDiscoverySession _peerDiscoverySession;
        private Dictionary<string, List<Action<string>>> _eventHandlersLookup = new Dictionary<string, List<Action<string>>>();
        private NetDataWriter _dataWriter;
        private Task _poolingTask;
        private bool _isInFocus;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }
            
            AppId = appId;
            PoolingInterval = poolingInterval;
            NetworkPort = networkPort;
            
            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener)
            {
                BroadcastReceiveEnabled = true,
                UnconnectedMessagesEnabled = true,
                NatPunchEnabled = true,
                UseSafeMtu = true,
                IPv6Enabled = ipV6Mode
            };
            
            _dataWriter = new NetDataWriter();
            _listener.NetworkReceiveEvent += HandleOnNetworkReceiveEvent;
            _listener.PeerConnectedEvent += HandleOnPeerConnectedEvent;
            _listener.PeerDisconnectedEvent += HandleOnPeerDisconnectedEvent;
            _peerDiscoverySession = new PeerDiscoverySession(AppId, NetworkPort, _netManager, _listener);
            _isInFocus = Application.isFocused;
            Application.focusChanged += hasFocus => _isInFocus = hasFocus; 

            if (autoStartService)
            {
                StartService();
            }
        }

        private void OnDestroy()
        {
            StopService();
            _listener.NetworkReceiveEvent -= HandleOnNetworkReceiveEvent;
            _listener.PeerConnectedEvent -= HandleOnPeerConnectedEvent;
            _listener.PeerDisconnectedEvent -= HandleOnPeerDisconnectedEvent;
            _peerDiscoverySession.Dispose();
            _eventHandlersLookup.Clear();
            Queue.Clear();
        }

        public void StartService()
        {
            if (IsRunning)
            {
                return;
            }
            IsRunning = true;

            StartCoroutine(MainThreadInvokerCoroutine());
            _netManager.Start(NetworkPort);
            _poolingTask = new Task(async () =>
            {
                await Task.Delay(250);
                while (IsRunning)
                {
                    await Task.Delay(PoolingInterval);
                    if (_isInFocus)
                    {
                        Queue.Enqueue(() => _netManager.PollEvents());
                    }
                }
            });
            _poolingTask.Start();
            _peerDiscoverySession.StartDiscovery();
            onServiceStarted?.Invoke();
        }

        public void StopService()
        {
            if (!IsRunning)
            {
                return;
            }
            IsRunning = false;

            _poolingTask.Dispose();
            _poolingTask = null;
            _netManager.Stop(true);
            _peerDiscoverySession.StopDiscovery();
            onServiceStopped?.Invoke();
        }
        
        private IEnumerator MainThreadInvokerCoroutine()
        {
            Queue.Clear();
            while (IsRunning)
            {
                yield return null;
                while (Queue.Count > 0)
                {
                    Action action;
                    lock (Queue)
                    {
                        action = Queue.Dequeue();
                    }
                    action();
                }
            }
        }

        public void SubscribeHandler(string eventId, Action<string> messageHandler)
        {
            if (_eventHandlersLookup.ContainsKey(eventId))
            {
                _eventHandlersLookup[eventId].Add(messageHandler);
            }
            else
            {
                _eventHandlersLookup[eventId] = new List<Action<string>> { messageHandler };
            }
        }
        
        public void UnsubscribeHandler(string eventId, Action<string> messageHandler)
        {
            if (_eventHandlersLookup.ContainsKey(eventId))
            {
                _eventHandlersLookup[eventId].Remove(messageHandler);
            }
        }

        public void SendToAll(string eventId, string data)
        {
            _dataWriter.Reset();
            _dataWriter.PutArray(new [] { AppId, eventId, data });
            _netManager.SendToAll(_dataWriter, DeliveryMethod.ReliableUnordered);
        }
        
        public void SendToPeer(NetPeer peer, string eventId, string data)
        {
            _dataWriter.Reset();
            _dataWriter.PutArray(new [] { AppId, eventId, data });
            peer.Send(_dataWriter, DeliveryMethod.ReliableUnordered);
        }

        private void HandleOnPeerConnectedEvent(NetPeer peer)
        {
            OnPeerConnected?.Invoke(this, peer);
            onPeerConnected?.Invoke();
            OnConnectedPeersChanged?.Invoke(this, ConnectedPeers.Count);
        }

        private void HandleOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            OnPeerDisconnected?.Invoke(this, EventArgs.Empty);
            onPeerDisconnected?.Invoke();
            OnConnectedPeersChanged?.Invoke(this, ConnectedPeers.Count);
        }
        
        private void HandleOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            var data = reader.GetStringArray();
            if (data.Length != 3 || data[0] != AppId
                                 || string.IsNullOrWhiteSpace(data[1]))
            {
                return;
            }
            if (!_eventHandlersLookup.ContainsKey(data[1]))
            {
                return;
            }

            for (var i = 0; i < _eventHandlersLookup[data[1]].Count; i++)
            {
                var networkMessageHandler = _eventHandlersLookup[data[1]][i];
                networkMessageHandler.Invoke(data[2] ?? string.Empty);
            }
        }
    }
}
