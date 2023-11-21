using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PicoNet.Sample
{
    public class MessageBubble : MonoBehaviour
    {
        [SerializeField]
        private Color myMessage;
        [SerializeField]
        private Color otherMessage;
        [SerializeField]
        private Image backgroundImage;
        [SerializeField]
        private TMP_Text messageLabel;

        public void SetMessage(bool isMine, string message)
        {
            messageLabel.text = message;
            backgroundImage.color = isMine ? myMessage : otherMessage;
        }
    }
}
