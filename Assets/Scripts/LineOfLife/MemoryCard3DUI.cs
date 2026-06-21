using EchoSpace.Core;
using TMPro;
using UnityEngine;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// Summary label for a single 3D memory card in the Line of Life timeline view.
    /// </summary>
    public class MemoryCard3DUI : MonoBehaviour
    {
        public TMP_Text typeText;
        public TMP_Text contentText;

        private const int MaxUserTextLength = 35;

        public void Initialize(MemoryAnchorData data)
        {
            if (data == null)
            {
                if (typeText != null)
                {
                    typeText.text = string.Empty;
                }

                if (contentText != null)
                {
                    contentText.text = "(No text entered)";
                }

                return;
            }

            if (typeText != null)
            {
                typeText.text = MemoryTypeDisplay.GetDisplayName(data.memoryType);
            }

            if (contentText != null)
            {
                contentText.text = FormatUserText(data.userText);
            }
        }

        private static string FormatUserText(string userText)
        {
            if (string.IsNullOrEmpty(userText))
            {
                return "(No text entered)";
            }

            if (userText.Length <= MaxUserTextLength)
            {
                return userText;
            }

            return userText.Substring(0, MaxUserTextLength) + "...";
        }
    }
}
