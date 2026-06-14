using System;
using UnityEngine;

namespace EchoSpace.Core
{
    public enum MemoryType
    {
        PastLife,
        PastSelf,
        ConcreteMemory,
        Relationship,
        TransitionMoment,
        FamiliarFeeling,
        StrangeFeeling,
        Uncertain
    }

    public static class MemoryTypeDisplay
    {
        public static string GetDisplayName(MemoryType type)
        {
            switch (type)
            {
                case MemoryType.PastLife:
                    return "過去生活";
                case MemoryType.PastSelf:
                    return "過去自己";
                case MemoryType.ConcreteMemory:
                    return "具體記憶";
                case MemoryType.Relationship:
                    return "關係";
                case MemoryType.TransitionMoment:
                    return "轉折時刻";
                case MemoryType.FamiliarFeeling:
                    return "熟悉感覺";
                case MemoryType.StrangeFeeling:
                    return "陌生感覺";
                case MemoryType.Uncertain:
                default:
                    return "不確定";
            }
        }
    }

    public enum TimeCategory
    {
        Unclassified,
        Past,
        Transition,
        Present
    }

    [Serializable]
    public class MemoryAnchorData
    {
        public string id;
        public string locationName;
        public Vector3 worldPosition;
        public MemoryType memoryType;
        public string promptQuestion;
        public string userText;
        public TimeCategory timeCategory;
        public bool isAnnotated;

        public static MemoryAnchorData CreateNewAt(Vector3 worldPosition)
        {
            return new MemoryAnchorData
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                locationName = "Memory Point",
                worldPosition = worldPosition,
                memoryType = MemoryType.Uncertain,
                promptQuestion = string.Empty,
                userText = string.Empty,
                timeCategory = TimeCategory.Unclassified,
                isAnnotated = false
            };
        }

        public MemoryAnchorData Clone()
        {
            return new MemoryAnchorData
            {
                id = id,
                locationName = locationName,
                worldPosition = worldPosition,
                memoryType = memoryType,
                promptQuestion = promptQuestion,
                userText = userText,
                timeCategory = timeCategory,
                isAnnotated = isAnnotated
            };
        }
    }
}
