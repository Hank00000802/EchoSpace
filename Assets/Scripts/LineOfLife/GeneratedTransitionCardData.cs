using System;
using EchoSpace.Core;
using UnityEngine;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// Transition card created by comparing one Past card and one Present card.
    /// Not stored as a MemoryAnchorData category; existing TimeCategory.Transition remains for the legacy flow.
    /// </summary>
    [Serializable]
    public class GeneratedTransitionCardData
    {
        public string id;
        public string groupId;
        public MemoryAnchorData pastCard;
        public MemoryAnchorData presentCard;
        public string transitionText;
        public Color groupColor;
        public string displayTitle = "我注意到的轉變";

        public static GeneratedTransitionCardData Create(
            MemoryAnchorData past,
            MemoryAnchorData present,
            string text,
            Color color)
        {
            return new GeneratedTransitionCardData
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                groupId = Guid.NewGuid().ToString("N").Substring(0, 8),
                pastCard = past,
                presentCard = present,
                transitionText = text ?? string.Empty,
                groupColor = color,
                displayTitle = "我注意到的轉變"
            };
        }
    }
}
