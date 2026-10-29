using System;
using System.Collections.Generic;

namespace EchoSpace.Export
{
    // Export-only data structures. Independent from MemoryAnchorData / GeneratedTransitionCardData.
    // Rules for JsonUtility compatibility: public fields, [Serializable] classes, List<T>, string/number only.
    // Enums are exported as their names (strings), never as ints.
    // Note: JsonUtility writes a null string as "" - SessionExportManager rewrites an empty
    // screenshotPath to JSON null after serialization.

    [Serializable]
    public class SessionExportDto
    {
        public string schemaVersion;
        public string sessionId;
        public string sessionStartedAtUtc;
        public string exportedAtUtc;
        public int exportRevision;
        public string exportTrigger;
        public AppInfoDto app = new AppInfoDto();
        public CountsDto counts = new CountsDto();
        public List<MemoryCardDto> memoryCards = new List<MemoryCardDto>();
        public List<TemporalClassificationDto> temporalClassification = new List<TemporalClassificationDto>();
        public List<ConnectionCardDto> connectionCards = new List<ConnectionCardDto>();

        /// <summary>Phase 2B: one record per card whose PNG was successfully written. Empty when none.</summary>
        public List<ScreenshotRecordDto> screenshots = new List<ScreenshotRecordDto>();
        public List<string> warnings = new List<string>();
    }

    [Serializable]
    public class AppInfoDto
    {
        public string unityVersion;
        public string sceneName;
    }

    [Serializable]
    public class CountsDto
    {
        public int memoryCards;
        public int connectionCards;
        public int skippedUnannotatedAnchors;
    }

    [Serializable]
    public class Vec3Dto
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class QuatDto
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    /// <summary>Normalised image coordinates, origin TOP-left (x right, y down), both in 0..1.</summary>
    [Serializable]
    public class ViewportPointDto
    {
        public float x = -1f;
        public float y = -1f;
    }

    /// <summary>
    /// A successfully written screenshot. relativePath is relative to the session folder
    /// ({dataRoot}/research/{sessionId}/); absolute paths are never exported.
    /// </summary>
    [Serializable]
    public class ScreenshotRecordDto
    {
        public string sessionId;
        public string cardId;
        public string relativePath;
        public int width;
        public int height;

        /// <summary>Vertical field of view (degrees) of the capture camera.</summary>
        public float fieldOfView;

        public Vec3Dto anchorWorldPosition = new Vec3Dto();
        public Vec3Dto cameraPosition = new Vec3Dto();
        public QuatDto cameraRotation = new QuatDto();

        /// <summary>Only recorded when the anchor projects inside the frame; otherwise exported as JSON null.</summary>
        public ViewportPointDto markerViewport = new ViewportPointDto();

        /// <summary>"AnchorPlacement": camera pose recorded when the user placed the anchor.</summary>
        public string poseSource;

        public string capturedAtUtc;
        public string sha256;
        public long sizeBytes;
    }

    [Serializable]
    public class MemoryCardDto
    {
        /// <summary>MemoryAnchorData.id (8 hex chars; unique within one session only).</summary>
        public string cardId;

        /// <summary>0-based position among exported cards, following MemoryAnchorManager list (creation) order.</summary>
        public int createdOrder;

        /// <summary>MemoryType enum name, e.g. "ConcreteMemory".</summary>
        public string memoryType;

        /// <summary>Existing Chinese display name from MemoryTypeDisplay.</summary>
        public string memoryTypeLabel;

        /// <summary>Empty string when the source data has no prompt question.</summary>
        public string promptQuestion;

        public string userText;
        public Vec3Dto worldPosition = new Vec3Dto();

        /// <summary>TimeCategory enum name: Unclassified / Past / Transition / Present.</summary>
        public string timeCategory;

        /// <summary>Session-relative path (e.g. "screenshots/a1b2c3d4.png"), or JSON null when no screenshot exists.</summary>
        public string screenshotPath;
    }

    [Serializable]
    public class TemporalClassificationDto
    {
        /// <summary>TimeCategory enum name.</summary>
        public string category;

        /// <summary>MemoryCardDto.cardId values in createdOrder.</summary>
        public List<string> cardIds = new List<string>();
    }

    [Serializable]
    public class ConnectionCardDto
    {
        /// <summary>GeneratedTransitionCardData.id.</summary>
        public string cardId;

        /// <summary>GeneratedTransitionCardData.groupId (one per card; used for colour grouping).</summary>
        public string groupId;

        /// <summary>0-based position in the generated-card list (generation order).</summary>
        public int createdOrder;

        /// <summary>Id of the single MemoryCardDto this connection card extends. Not an embedded copy.</summary>
        public string sourceCardId;

        /// <summary>"Past" or "Present" (which side of GeneratedTransitionCardData holds the source card).</summary>
        public string sourcePhase;

        public string transitionText;
        public string displayTitle;

        /// <summary>#RRGGBBAA.</summary>
        public string groupColor;
    }
}
