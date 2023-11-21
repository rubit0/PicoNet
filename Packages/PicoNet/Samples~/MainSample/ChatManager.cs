using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PicoNet.Sample
{
    public class ChatManager : MonoBehaviour
    {
        private const string MESSAGE_EVENT = "MESSAGE_EVENT";
        
        [SerializeField]
        private TMP_Text textConnectionsCounter;
        [SerializeField]
        private TMP_InputField inputFieldMessage;
        [SerializeField]
        private Button sendMessageButton;
        [SerializeField]
        private MessageBubble messageBubblePrefab;
        [SerializeField]
        private Transform listViewRoot;

        private void Start()
        {
            // Handle peers counter
            PicoNetService.Instance.OnConnectedPeersChanged += 
                (sender, count) => textConnectionsCounter.text = count.ToString();
            textConnectionsCounter.text = PicoNetService.Instance.ConnectedPeers.Count.ToString();
            
            // Handle sending message
            sendMessageButton.onClick.AddListener(OnButtonSendMessage);
            
            // Handle receiving message
            PicoNetService.Instance.SubscribeHandler(MESSAGE_EVENT, HandleReceivedMessage);
        }

        private void HandleReceivedMessage(string message)
        {
            var bubble = Instantiate(messageBubblePrefab, listViewRoot);
            bubble.SetMessage(false, message);
        }

        private void OnButtonSendMessage()
        {
            if (string.IsNullOrWhiteSpace(inputFieldMessage.text))
            {
                return;
            }
            
            PicoNetService.Instance.SendToAll(MESSAGE_EVENT, inputFieldMessage.text);
            var bubble = Instantiate(messageBubblePrefab, listViewRoot);
            bubble.SetMessage(true, inputFieldMessage.text);
            inputFieldMessage.text = string.Empty;
        }
    }
}
