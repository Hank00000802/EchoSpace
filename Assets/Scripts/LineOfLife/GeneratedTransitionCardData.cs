using System;
using EchoSpace.Core;
using UnityEngine;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// Link card created from one Past or one Present memory card.
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
        public string displayTitle = "連結卡";

        public MemoryAnchorData SourceCard
        {
            get { return pastCard != null ? pastCard : presentCard; }
        }

        public static GeneratedTransitionCardData Create(
            MemoryAnchorData source,
            string text,
            Color color)
        {
            bool fromPast = source != null && source.timeCategory == TimeCategory.Past;
            return new GeneratedTransitionCardData
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                groupId = Guid.NewGuid().ToString("N").Substring(0, 8),
                pastCard = fromPast ? source : null,
                presentCard = fromPast ? null : source,
                transitionText = text ?? string.Empty,
                groupColor = color,
                displayTitle = "連結卡"
            };
        }
    }
}
