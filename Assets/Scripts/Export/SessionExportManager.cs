using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EchoSpace.Annotation;
using EchoSpace.Core;
using EchoSpace.LineOfLife;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoSpace.Export
{
    /// <summary>
    /// Phase 2A: exports a read-only snapshot of the current session to JSON.
    /// Reads existing data only (MemoryAnchorManager clones + generated connection cards);
    /// never modifies cards, classification or any VR flow. Trigger manually via the
    /// component context menu ("Export Session Now") or call ExportNow() from a UnityEvent.
    ///
    /// Output (outside the Git repo):
    ///   {dataRoot}/research/{sessionId}/session.json                      (latest)
    ///   {dataRoot}/research/{sessionId}/revisions/session_r001.json ...   (immutable history)
    /// </summary>
    public class SessionExportManager : MonoBehaviour
    {
        // 1.1: adds top-level "screenshots" and populated memoryCards[].screenshotPath (Phase 2B). Additive only.
        public const string SchemaVersion = "1.1";
        private const string DataRootEnvVar = "ECHOSPACE_DATA_DIR";

        [Header("Sources (read-only). Auto-found at export time if left empty.")]
        [SerializeField] private SessionContext sessionContext;
        [SerializeField] private MemoryAnchorManager anchorManager;
        [SerializeField] private LineOfLife3DViewController lineOfLife3DView;

        [Header("Storage")]
        [Tooltip("Absolute folder OUTSIDE the Git repo. Empty = env var ECHOSPACE_DATA_DIR, " +
                 "then Application.persistentDataPath/EchoSpaceData.")]
        [SerializeField] private string dataRootOverride = string.Empty;
        [Tooltip("Skip writing a new revision when nothing changed since the last successful export.")]
        [SerializeField] private bool skipIfUnchanged = true;
        [Tooltip("Add a warning to the export for every memory card that has no screenshot.")]
        [SerializeField] private bool warnOnMissingScreenshot = true;

        private readonly Dictionary<string, ScreenshotRecordDto> _screenshots =
            new Dictionary<string, ScreenshotRecordDto>();

        private int _revision;
        private bool _revisionInitialized;
        private string _lastContentHash;

        public string LastSessionFilePath { get; private set; }
        public int LastRevision { get { return _revision; } }

        /// <summary>Inspector / UnityEvent friendly entry point (void).</summary>
        [ContextMenu("Export Session Now")]
        public void ExportNow()
        {
            Export("Manual");
        }

        /// <summary>Returns true when a file was written or the content was unchanged (skipped).</summary>
        public bool Export(string trigger)
        {
            try
            {
                return ExportInternal(string.IsNullOrEmpty(trigger) ? "Unknown" : trigger);
            }
            catch (Exception ex)
            {
                // Never let an export problem break the VR experience.
                Debug.LogError("[SessionExport] Export failed: " + ex);
                return false;
            }
        }

        private bool ExportInternal(string trigger)
        {
            if (!ResolveSources())
            {
                return false;
            }

            if (!TryGetSessionDirectory(out string sessionDir))
            {
                return false;
            }

            string revisionsDir = Path.Combine(sessionDir, "revisions");
            Directory.CreateDirectory(revisionsDir);

            if (!_revisionInitialized)
            {
                _revision = FindHighestRevision(revisionsDir);
                _revisionInitialized = true;
            }

            SessionExportDto dto = BuildSnapshot(trigger, sessionDir);

            // Content hash ignores export time / revision so repeated exports can be detected.
            dto.exportedAtUtc = string.Empty;
            dto.exportRevision = 0;
            string contentHash = Sha256Hex(Serialize(dto));
            if (skipIfUnchanged && contentHash == _lastContentHash && !string.IsNullOrEmpty(LastSessionFilePath))
            {
                Debug.Log("[SessionExport] No changes since revision " + _revision
                    + "; nothing written. Latest: " + LastSessionFilePath);
                return true;
            }

            int nextRevision = _revision + 1;
            dto.exportRevision = nextRevision;
            dto.exportedAtUtc = DateTime.UtcNow.ToString("o");
            string json = Serialize(dto);

            string revisionPath = Path.Combine(revisionsDir, "session_r" + nextRevision.ToString("D3") + ".json");
            string latestPath = Path.Combine(sessionDir, "session.json");

            // Immutable revision first, "latest" pointer last: an interruption never leaves a half-written session.json.
            WriteNewFileAtomic(revisionPath, json);
            WriteReplaceAtomic(latestPath, json);

            _revision = nextRevision;
            _lastContentHash = contentHash;
            LastSessionFilePath = latestPath;

            Debug.Log("[SessionExport] Exported revision " + nextRevision
                + " | memoryCards=" + dto.counts.memoryCards
                + " | connectionCards=" + dto.counts.connectionCards
                + " | warnings=" + dto.warnings.Count
                + "\n  " + latestPath);
            for (int i = 0; i < dto.warnings.Count; i++)
            {
                Debug.LogWarning("[SessionExport] " + dto.warnings[i]);
            }

            return true;
        }

        // ---------------------------------------------------------------- snapshot

        private SessionExportDto BuildSnapshot(string trigger, string sessionDir)
        {
            var dto = new SessionExportDto
            {
                schemaVersion = SchemaVersion,
                sessionId = sessionContext.SessionId,
                sessionStartedAtUtc = sessionContext.StartedAtUtc.ToString("o"),
                exportTrigger = trigger
            };
            dto.app.unityVersion = Application.unityVersion;
            dto.app.sceneName = SceneManager.GetActiveScene().name;

            // GetAllAnchorData() returns clones, so this snapshot cannot mutate live data.
            List<MemoryAnchorData> anchors = anchorManager.GetAllAnchorData();
            var exportedIds = new HashSet<string>();
            var idsByCategory = new Dictionary<TimeCategory, List<string>>();
            foreach (TimeCategory category in Enum.GetValues(typeof(TimeCategory)))
            {
                idsByCategory[category] = new List<string>();
            }

            int skipped = 0;
            foreach (MemoryAnchorData data in anchors)
            {
                if (data == null)
                {
                    continue;
                }

                if (!data.isAnnotated)
                {
                    skipped++;
                    continue;
                }

                if (string.IsNullOrEmpty(data.id))
                {
                    dto.warnings.Add("A memory card has an empty id.");
                }
                else if (!exportedIds.Add(data.id))
                {
                    dto.warnings.Add("Duplicate memory card id: " + data.id);
                }

                var card = new MemoryCardDto
                {
                    cardId = data.id,
                    createdOrder = dto.memoryCards.Count,
                    memoryType = data.memoryType.ToString(),
                    memoryTypeLabel = MemoryTypeDisplay.GetDisplayName(data.memoryType),
                    promptQuestion = data.promptQuestion ?? string.Empty,
                    userText = data.userText ?? string.Empty,
                    timeCategory = data.timeCategory.ToString(),
                    screenshotPath = null // Phase 2B
                };
                card.worldPosition.x = data.worldPosition.x;
                card.worldPosition.y = data.worldPosition.y;
                card.worldPosition.z = data.worldPosition.z;
                dto.memoryCards.Add(card);
                idsByCategory[data.timeCategory].Add(data.id);
            }

            foreach (TimeCategory category in Enum.GetValues(typeof(TimeCategory)))
            {
                var group = new TemporalClassificationDto { category = category.ToString() };
                group.cardIds.AddRange(idsByCategory[category]);
                dto.temporalClassification.Add(group);
            }

            AppendConnectionCards(dto, exportedIds);
            AppendScreenshots(dto, sessionDir);

            dto.counts.memoryCards = dto.memoryCards.Count;
            dto.counts.connectionCards = dto.connectionCards.Count;
            dto.counts.skippedUnannotatedAnchors = skipped;
            return dto;
        }

        private void AppendConnectionCards(SessionExportDto dto, HashSet<string> exportedIds)
        {
            if (lineOfLife3DView == null)
            {
                dto.warnings.Add("LineOfLife3DViewController not found; connectionCards were not exported.");
                return;
            }

            IReadOnlyList<GeneratedTransitionCardData> generated = lineOfLife3DView.GeneratedTransitionCards;
            for (int i = 0; i < generated.Count; i++)
            {
                GeneratedTransitionCardData source = generated[i];
                if (source == null)
                {
                    continue;
                }

                MemoryAnchorData sourceCard = source.SourceCard;
                string sourcePhase = source.pastCard != null
                    ? TimeCategory.Past.ToString()
                    : (source.presentCard != null ? TimeCategory.Present.ToString() : string.Empty);
                string sourceCardId = sourceCard != null ? sourceCard.id : string.Empty;

                if (string.IsNullOrEmpty(sourceCardId))
                {
                    dto.warnings.Add("Connection card " + source.id + " has no source card.");
                }
                else if (!exportedIds.Contains(sourceCardId))
                {
                    dto.warnings.Add("Connection card " + source.id + " references source card "
                        + sourceCardId + " which is not in memoryCards.");
                }
                else if (sourceCard.timeCategory.ToString() != sourcePhase)
                {
                    dto.warnings.Add("Connection card " + source.id + ": source card " + sourceCardId
                        + " is now " + sourceCard.timeCategory + " but was generated from " + sourcePhase + ".");
                }

                dto.connectionCards.Add(new ConnectionCardDto
                {
                    cardId = source.id,
                    groupId = source.groupId,
                    createdOrder = dto.connectionCards.Count,
                    sourceCardId = sourceCardId,
                    sourcePhase = sourcePhase,
                    transitionText = source.transitionText ?? string.Empty,
                    displayTitle = source.displayTitle ?? string.Empty,
                    groupColor = "#" + ColorUtility.ToHtmlStringRGBA(source.groupColor)
                });
            }
        }

        /// <summary>
        /// Phase 2B: sets MemoryCardDto.screenshotPath only for cards that have a registered record
        /// AND whose PNG still exists on disk. Everything else stays null (with a warning).
        /// </summary>
        private void AppendScreenshots(SessionExportDto dto, string sessionDir)
        {
            for (int i = 0; i < dto.memoryCards.Count; i++)
            {
                MemoryCardDto card = dto.memoryCards[i];
                ScreenshotRecordDto record;
                if (string.IsNullOrEmpty(card.cardId) || !_screenshots.TryGetValue(card.cardId, out record))
                {
                    if (warnOnMissingScreenshot)
                    {
                        dto.warnings.Add("Memory card " + card.cardId + " has no screenshot (screenshotPath = null).");
                    }

                    continue;
                }

                string fullPath = Path.Combine(sessionDir, record.relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    dto.warnings.Add("Screenshot file missing for card " + card.cardId + ": " + record.relativePath);
                    continue;
                }

                card.screenshotPath = record.relativePath;
                dto.screenshots.Add(CloneRecord(record));
            }
        }

        private static ScreenshotRecordDto CloneRecord(ScreenshotRecordDto r)
        {
            // JsonUtility round-trip: cheap, and keeps the registry immune to later edits.
            return JsonUtility.FromJson<ScreenshotRecordDto>(JsonUtility.ToJson(r));
        }

        // ---------------------------------------------------------------- Phase 2B API (used by ScreenshotCapture)

        /// <summary>Resolves {dataRoot}/research/{sessionId}. Does not create the folder.</summary>
        public bool TryGetSessionDirectory(out string sessionDir)
        {
            sessionDir = null;
            if (sessionContext == null)
            {
                sessionContext = GetComponent<SessionContext>();
            }

            if (sessionContext == null)
            {
                sessionContext = FindObjectOfType<SessionContext>();
            }

            if (sessionContext == null)
            {
                Debug.LogError("[SessionExport] SessionContext not found. Add one to the scene and assign it.");
                return false;
            }

            string sessionId = sessionContext.SessionId;
            if (!SessionContext.IsValidSessionId(sessionId))
            {
                Debug.LogError("[SessionExport] Invalid sessionId: " + sessionId);
                return false;
            }

            string dataRoot = ResolveDataRoot(out string rootError);
            if (dataRoot == null)
            {
                Debug.LogError("[SessionExport] " + rootError);
                return false;
            }

            sessionDir = Path.Combine(dataRoot, "research", sessionId);
            return true;
        }

        public string CurrentSessionId
        {
            get { return sessionContext != null ? sessionContext.SessionId : null; }
        }

        public bool HasScreenshot(string cardId)
        {
            return !string.IsNullOrEmpty(cardId) && _screenshots.ContainsKey(cardId);
        }

        /// <summary>Call only AFTER the PNG has been fully written to disk.</summary>
        public void RegisterScreenshot(ScreenshotRecordDto record)
        {
            if (record == null || string.IsNullOrEmpty(record.cardId) || string.IsNullOrEmpty(record.relativePath))
            {
                return;
            }

            _screenshots[record.cardId] = record;
        }

        private bool ResolveSources()
        {
            if (sessionContext == null)
            {
                sessionContext = GetComponent<SessionContext>();
            }

            if (sessionContext == null)
            {
                sessionContext = FindObjectOfType<SessionContext>();
            }

            if (sessionContext == null)
            {
                Debug.LogError("[SessionExport] SessionContext not found. Add one to the scene and assign it.");
                return false;
            }

            if (anchorManager == null)
            {
                anchorManager = FindObjectOfType<MemoryAnchorManager>();
            }

            if (anchorManager == null)
            {
                Debug.LogError("[SessionExport] MemoryAnchorManager not found; nothing to export.");
                return false;
            }

            if (lineOfLife3DView == null)
            {
                lineOfLife3DView = FindObjectOfType<LineOfLife3DViewController>();
            }

            return true;
        }

        // ---------------------------------------------------------------- serialization

        private static string Serialize(SessionExportDto dto)
        {
            string json = JsonUtility.ToJson(dto, true);

            // JsonUtility cannot emit null strings; an empty screenshotPath means "no screenshot" -> JSON null.
            json = Regex.Replace(json, "(\"screenshotPath\"\\s*:\\s*)\"\"", "$1null");

            // JsonUtility cannot emit null objects; markerViewport (-1,-1) means "not in frame" -> JSON null.
            // Valid values are always within 0..1, so -1 can never be real data.
            return Regex.Replace(
                json,
                "\"markerViewport\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*-1(?:\\.0+)?\\s*,\\s*\"y\"\\s*:\\s*-1(?:\\.0+)?\\s*\\}",
                "\"markerViewport\": null");
        }

        private static string Sha256Hex(string text)
        {
            return Sha256Hex(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Thread-safe.</summary>
        public static string Sha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        // ---------------------------------------------------------------- storage

        private string ResolveDataRoot(out string error)
        {
            error = null;
            string root = dataRootOverride;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Environment.GetEnvironmentVariable(DataRootEnvVar);
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(Application.persistentDataPath, "EchoSpaceData");
            }

            string full;
            try
            {
                full = Path.GetFullPath(root.Trim());
            }
            catch (Exception ex)
            {
                error = "Invalid data root path '" + root + "': " + ex.Message;
                return null;
            }

            if (IsInsideProjectOrGitRepo(full))
            {
                error = "Data root must be OUTSIDE the Unity project / Git repo. Refusing to write to: " + full
                    + "  (set dataRootOverride or env " + DataRootEnvVar + ")";
                return null;
            }

            return full;
        }

        private static bool IsInsideProjectOrGitRepo(string fullPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedProject = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (normalizedPath.StartsWith(normalizedProject, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            for (DirectoryInfo dir = new DirectoryInfo(fullPath); dir != null; dir = dir.Parent)
            {
                string git = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindHighestRevision(string revisionsDir)
        {
            int highest = 0;
            foreach (string file in Directory.GetFiles(revisionsDir, "session_r*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file); // session_r001
                if (name.Length > "session_r".Length
                    && int.TryParse(name.Substring("session_r".Length), out int n)
                    && n > highest)
                {
                    highest = n;
                }
            }

            return highest;
        }

        /// <summary>
        /// Thread-safe (File IO only). Writes bytes to *.tmp then moves into place.
        /// Never overwrites: throws IOException if the target already exists.
        /// </summary>
        public static void WriteBytesNewFileAtomic(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = WriteTempBytes(path, bytes);
            try
            {
                File.Move(tmp, path);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        /// <summary>Writes to *.tmp then moves into place. Fails (does not overwrite) if the target exists.</summary>
        private static void WriteNewFileAtomic(string path, string content)
        {
            string tmp = WriteTempFile(path, content);
            try
            {
                File.Move(tmp, path);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        /// <summary>Writes to *.tmp then atomically replaces the target (or moves it into place if new).</summary>
        private static void WriteReplaceAtomic(string path, string content)
        {
            string tmp = WriteTempFile(path, content);
            try
            {
                if (!File.Exists(path))
                {
                    File.Move(tmp, path);
                    return;
                }

                try
                {
                    File.Replace(tmp, path, null);
                }
                catch (IOException)
                {
                    // Fallback for file systems without atomic replace; tmp is already complete.
                    File.Copy(tmp, path, true);
                    TryDelete(tmp);
                }
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        private static string WriteTempFile(string path, string content)
        {
            return WriteTempBytes(path, new UTF8Encoding(false).GetBytes(content));
        }

        private static string WriteTempBytes(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            return tmp;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
