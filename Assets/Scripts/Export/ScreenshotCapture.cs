using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EchoSpace.Annotation;
using EchoSpace.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;

namespace EchoSpace.Export
{
    /// <summary>
    /// Phase 2B: environment screenshot for each saved Memory Card.
    ///
    /// Rendering: an independent, disabled, mono Camera renders into its own RenderTexture
    /// (UnityGaussianSplatting draws for every non-preview Camera via Camera.onPreCull + a
    /// per-camera command buffer in the Built-in pipeline, so this also renders the splats).
    /// The HMD camera, XR rendering and Camera.main are never touched.
    ///
    /// Timing (autoCapture = true):
    ///   1. MemoryAnchorManager.AnchorCreated   -> only the camera POSE is remembered (no rendering).
    ///   2. AnnotationPanelController.AnnotationSaved -> the shot is rendered from the remembered pose,
    ///      after the annotation panel is already closed. Cancelled anchors (AnchorRemoved) are never rendered.
    ///   3. PNG is encoded, written atomically on a worker thread, and only then registered with
    ///      SessionExportManager (which puts it into session.json on the next export).
    ///
    /// Everything is wrapped so that a failure here can never interrupt the VR flow.
    /// </summary>
    public class ScreenshotCapture : MonoBehaviour
    {
        private static readonly Regex SafeIdPattern = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

        [Header("References (auto-found if empty)")]
        [SerializeField] private SessionExportManager exportManager;
        [SerializeField] private MemoryAnchorManager anchorManager;
        [SerializeField] private AnnotationPanelController annotationPanel;
        [Tooltip("Pose source and camera settings template. Empty = Camera.main.")]
        [SerializeField] private Camera sourceCamera;

        [Header("Capture")]
        [SerializeField] private int captureWidth = 1280;
        [SerializeField] private int captureHeight = 720;
        [Tooltip("Vertical field of view of the screenshot (degrees). Independent from the HMD projection.")]
        [Range(30f, 110f)]
        [SerializeField] private float fieldOfView = 70f;
        [Tooltip("Layers the capture camera never draws (default: UI).")]
        [SerializeField] private LayerMask excludedLayers = 1 << 5;
        [Tooltip("Non-blocking GPU readback. Falls back to ReadPixels if unsupported or failed.")]
        [SerializeField] private bool useAsyncReadback = true;

        [Header("Hidden only while the screenshot is rendered (same frame, restored immediately)")]
        [Tooltip("Keep the blue triangle MemoryAnchor markers visible in the screenshot. " +
                 "Untick to hide them (the web can still draw a marker from markerViewport).")]
        [SerializeField] private bool showMemoryAnchorMarkers = true;
        [Tooltip("Paint the marker that belongs to THIS card in a highlight colour (default yellow) so it can be told " +
                 "apart from the other blue markers in the same shot. Other cards' markers stay blue. " +
                 "Only applies to card screenshots, not to manual test images.")]
        [SerializeField] private bool highlightCurrentMarker = true;
        [SerializeField] private Color currentMarkerColor = new Color(1f, 0.92f, 0.2f, 1f);
        [Tooltip("Hide controller models and ray / line visuals (XRBaseController, XRBaseInteractor).")]
        [SerializeField] private bool hideXrControllerVisuals = true;
        [Tooltip("Disable every active Canvas (annotation panel, reflection UI, guides, tutorial hints).")]
        [SerializeField] private bool hideWorldSpaceCanvases = true;
        [Tooltip("Extra objects whose renderers should be hidden, e.g. PreviewMarker.")]
        [SerializeField] private GameObject[] extraHideObjects = new GameObject[0];

        [Header("Automatic capture - keep OFF until the manual test image looks right")]
        [SerializeField] private bool autoCapture = false;

        private struct PoseSnapshot
        {
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public Vector3 anchorWorldPosition;
        }

        private sealed class CaptureJob
        {
            public string cardId;            // null for manual test images
            public string sessionId;
            public string relativePath;      // "screenshots/xxx.png"
            public string absolutePath;      // never exported
            public Vector3 anchorWorldPosition;
            public bool hasAnchor;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public int width;
            public int height;
            public float fov;
            public Vector2? markerViewport;  // top-left origin, only when inside the frame
            public RenderTexture rt;
        }

        private struct WriteResult
        {
            public bool ok;
            public string sha256;
            public string error;
        }

        private readonly Dictionary<string, PoseSnapshot> _pendingPoses = new Dictionary<string, PoseSnapshot>();
        private readonly HashSet<string> _inFlight = new HashSet<string>();
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

        // Marker highlight (applied and undone inside the same frame, around Camera.Render).
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private Renderer _highlightRenderer;
        private MaterialPropertyBlock _highlightPreviousBlock;

        private Camera _cam;
        private MemoryAnchorManager _boundAnchorManager;
        private AnnotationPanelController _boundPanel;

        // ------------------------------------------------------------------ lifecycle / wiring

        private void OnEnable()
        {
            Bind();
        }

        private void Start()
        {
            Bind(); // second chance: other objects may not have existed during OnEnable
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
            if (_cam != null)
            {
                Destroy(_cam.gameObject);
            }
        }

        private void Bind()
        {
            if (exportManager == null)
            {
                exportManager = FindObjectOfType<SessionExportManager>();
            }

            if (anchorManager == null)
            {
                anchorManager = FindObjectOfType<MemoryAnchorManager>();
            }

            if (annotationPanel == null)
            {
                annotationPanel = FindObjectOfType<AnnotationPanelController>();
            }

            if (anchorManager != null && _boundAnchorManager != anchorManager)
            {
                Unbind();
                anchorManager.AnchorCreated += HandleAnchorCreated;
                anchorManager.AnchorRemoved += HandleAnchorRemoved;
                _boundAnchorManager = anchorManager;
            }

            if (annotationPanel != null && _boundPanel != annotationPanel)
            {
                annotationPanel.AnnotationSaved += HandleAnnotationSaved;
                _boundPanel = annotationPanel;
            }
        }

        private void Unbind()
        {
            if (_boundAnchorManager != null)
            {
                _boundAnchorManager.AnchorCreated -= HandleAnchorCreated;
                _boundAnchorManager.AnchorRemoved -= HandleAnchorRemoved;
            }

            _boundAnchorManager = null;

            if (_boundPanel != null)
            {
                _boundPanel.AnnotationSaved -= HandleAnnotationSaved;
            }

            _boundPanel = null;
        }

        // ------------------------------------------------------------------ manual test

        /// <summary>
        /// Renders the current view of the source camera to screenshots/_test_*.png in the session
        /// folder. Not linked to any card and never written to session.json.
        /// </summary>
        [ContextMenu("Capture Test Image (current view)")]
        public void CaptureTestImage()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[ScreenshotCapture] Enter Play Mode first.");
                return;
            }

            Camera src = ResolveSourceCamera();
            if (src == null)
            {
                Debug.LogWarning("[ScreenshotCapture] No source camera (assign one or tag a MainCamera).");
                return;
            }

            var job = new CaptureJob
            {
                relativePath = "screenshots/_test_" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png",
                cameraPosition = src.transform.position,
                cameraRotation = src.transform.rotation
            };
            SafeBegin(job, "ManualTest");
        }

        // ------------------------------------------------------------------ automatic capture (event driven)

        private void HandleAnchorCreated(MemoryAnchor anchor)
        {
            if (!autoCapture || anchor == null || anchor.Data == null)
            {
                return;
            }

            try
            {
                string id = anchor.Data.id;
                Camera cam = ResolveSourceCamera();
                if (!IsSafeId(id) || cam == null)
                {
                    return;
                }

                // Pose only. Nothing is rendered until the annotation is actually saved.
                _pendingPoses[id] = new PoseSnapshot
                {
                    cameraPosition = cam.transform.position,
                    cameraRotation = cam.transform.rotation,
                    anchorWorldPosition = anchor.Data.worldPosition
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ScreenshotCapture] Could not record placement pose: " + ex.Message);
            }
        }

        private void HandleAnchorRemoved(MemoryAnchorData data)
        {
            if (data != null && !string.IsNullOrEmpty(data.id))
            {
                _pendingPoses.Remove(data.id); // cancelled anchor: no screenshot, no association
            }
        }

        private void HandleAnnotationSaved(MemoryAnchorData data)
        {
            if (!autoCapture || data == null || !IsSafeId(data.id))
            {
                return;
            }

            if (exportManager == null || exportManager.HasScreenshot(data.id) || _inFlight.Contains(data.id))
            {
                return; // already captured (re-edit / repeated save): never re-shoot
            }

            PoseSnapshot pose;
            if (!_pendingPoses.TryGetValue(data.id, out pose))
            {
                Debug.LogWarning("[ScreenshotCapture] No placement pose for card " + data.id + "; no screenshot (null).");
                return;
            }

            var job = new CaptureJob
            {
                cardId = data.id,
                relativePath = "screenshots/" + data.id + ".png",
                anchorWorldPosition = pose.anchorWorldPosition,
                hasAnchor = true,
                cameraPosition = pose.cameraPosition,
                cameraRotation = pose.cameraRotation
            };
            SafeBegin(job, "AnchorPlacement");
        }

        // ------------------------------------------------------------------ capture pipeline

        private void SafeBegin(CaptureJob job, string trigger)
        {
            try
            {
                if (!BeginCapture(job) && job.cardId != null)
                {
                    _inFlight.Remove(job.cardId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[ScreenshotCapture] Capture failed (" + trigger + "): " + ex);
                FailJob(job);
            }
        }

        private bool BeginCapture(CaptureJob job)
        {
            if (exportManager == null)
            {
                exportManager = FindObjectOfType<SessionExportManager>();
            }

            string sessionDir;
            if (exportManager == null || !exportManager.TryGetSessionDirectory(out sessionDir))
            {
                Debug.LogWarning("[ScreenshotCapture] SessionExportManager / session folder unavailable; skipped.");
                return false;
            }

            job.sessionId = exportManager.CurrentSessionId;
            job.absolutePath = Path.Combine(sessionDir, job.relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(job.absolutePath))
            {
                Debug.Log("[ScreenshotCapture] File already exists, not overwritten: " + job.relativePath);
                return false;
            }

            if (job.cardId != null)
            {
                _inFlight.Add(job.cardId);
            }

            Camera src = ResolveSourceCamera();
            EnsureCaptureCamera(src);

            job.width = Mathf.Clamp(captureWidth, 64, 4096);
            job.height = Mathf.Clamp(captureHeight, 64, 4096);
            job.fov = fieldOfView;

            job.rt = new RenderTexture(job.width, job.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            job.rt.antiAliasing = 1;
            if (!job.rt.Create())
            {
                throw new InvalidOperationException("RenderTexture.Create() failed.");
            }

            ConfigureCamera(job, src);

            HideForCapture(job.cardId);
            try
            {
                _cam.Render();
            }
            finally
            {
                RestoreAfterCapture();
                _cam.targetTexture = null;
            }

            if (job.hasAnchor)
            {
                Vector3 vp = _cam.WorldToViewportPoint(job.anchorWorldPosition);
                bool inFrame = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                if (inFrame)
                {
                    job.markerViewport = new Vector2(vp.x, 1f - vp.y); // convert to top-left origin
                }
            }

            if (useAsyncReadback && SystemInfo.supportsAsyncGPUReadback)
            {
                AsyncGPUReadback.Request(job.rt, 0, TextureFormat.RGBA32, request => OnReadback(job, request));
            }
            else
            {
                FinishCapture(job, ReadPixelsSync(job.rt, job.width, job.height));
            }

            return true;
        }

        private void OnReadback(CaptureJob job, AsyncGPUReadbackRequest request)
        {
            if (this == null)
            {
                ReleaseRt(job);
                return;
            }

            try
            {
                byte[] pixels;
                if (request.hasError)
                {
                    Debug.LogWarning("[ScreenshotCapture] Async readback failed; using ReadPixels fallback.");
                    pixels = ReadPixelsSync(job.rt, job.width, job.height);
                }
                else
                {
                    pixels = request.GetData<byte>().ToArray();
                }

                FinishCapture(job, pixels);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ScreenshotCapture] Readback/encode failed: " + ex);
                FailJob(job);
            }
        }

        private void FinishCapture(CaptureJob job, byte[] rgba)
        {
            ReleaseRt(job);

            int expected = job.width * job.height * 4;
            if (rgba == null || rgba.Length != expected)
            {
                throw new InvalidOperationException("Unexpected pixel buffer size.");
            }

            // The splat composite blends alpha, so the RT alpha is not reliably 1. Force opaque.
            for (int i = 3; i < rgba.Length; i += 4)
            {
                rgba[i] = 255;
            }

            byte[] png;
            var tex = new Texture2D(job.width, job.height, TextureFormat.RGBA32, false);
            try
            {
                tex.LoadRawTextureData(rgba);
                tex.Apply(false);
                png = tex.EncodeToPNG();
            }
            finally
            {
                Destroy(tex);
            }

            if (png == null || png.Length == 0)
            {
                throw new InvalidOperationException("PNG encoding returned no data.");
            }

            string path = job.absolutePath;
            Task<WriteResult> task = Task.Run(() => WritePng(path, png));
            if (isActiveAndEnabled)
            {
                StartCoroutine(WaitForWrite(job, png.Length, task));
            }
            else
            {
                task.Wait();
                OnWriteDone(job, png.Length, task.Result);
            }
        }

        private IEnumerator WaitForWrite(CaptureJob job, int sizeBytes, Task<WriteResult> task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            OnWriteDone(job, sizeBytes, task.Result);
        }

        // Runs on a worker thread: File IO + hashing only, no Unity API.
        private static WriteResult WritePng(string path, byte[] png)
        {
            try
            {
                SessionExportManager.WriteBytesNewFileAtomic(path, png);
                return new WriteResult { ok = true, sha256 = SessionExportManager.Sha256Hex(png) };
            }
            catch (Exception ex)
            {
                return new WriteResult { ok = false, error = ex.Message };
            }
        }

        private void OnWriteDone(CaptureJob job, int sizeBytes, WriteResult result)
        {
            if (job.cardId != null)
            {
                _inFlight.Remove(job.cardId);
            }

            if (!result.ok)
            {
                Debug.LogError("[ScreenshotCapture] PNG write failed for " + job.relativePath + ": " + result.error);
                return; // no record -> screenshotPath stays null
            }

            if (job.cardId == null)
            {
                Debug.Log("[ScreenshotCapture] Test image written (not linked to any card):\n  " + job.absolutePath);
                return;
            }

            // The file is fully on disk: only now create the reference.
            var record = new ScreenshotRecordDto
            {
                sessionId = job.sessionId,
                cardId = job.cardId,
                relativePath = job.relativePath,
                width = job.width,
                height = job.height,
                fieldOfView = job.fov,
                poseSource = "AnchorPlacement",
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                sha256 = result.sha256,
                sizeBytes = sizeBytes
            };
            SetVec(record.anchorWorldPosition, job.anchorWorldPosition);
            SetVec(record.cameraPosition, job.cameraPosition);
            record.cameraRotation.x = job.cameraRotation.x;
            record.cameraRotation.y = job.cameraRotation.y;
            record.cameraRotation.z = job.cameraRotation.z;
            record.cameraRotation.w = job.cameraRotation.w;
            if (job.markerViewport.HasValue)
            {
                record.markerViewport.x = Round4(job.markerViewport.Value.x);
                record.markerViewport.y = Round4(job.markerViewport.Value.y);
            }

            exportManager.RegisterScreenshot(record);
            _pendingPoses.Remove(job.cardId);

            Debug.Log("[ScreenshotCapture] Screenshot saved for card " + job.cardId
                + " | markerInFrame=" + job.markerViewport.HasValue
                + "\n  " + job.absolutePath);
        }

        // ------------------------------------------------------------------ camera / hiding helpers

        private Camera ResolveSourceCamera()
        {
            return sourceCamera != null ? sourceCamera : Camera.main;
        }

        private void EnsureCaptureCamera(Camera src)
        {
            if (_cam == null)
            {
                var go = new GameObject("EchoSpace_ScreenshotCamera");
                go.hideFlags = HideFlags.HideAndDontSave;
                _cam = go.AddComponent<Camera>();
            }

            if (src != null)
            {
                _cam.CopyFrom(src); // keeps clear flags / background / clip planes of the real view
            }
            else
            {
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
            }

            _cam.enabled = false; // rendered manually only
        }

        private void ConfigureCamera(CaptureJob job, Camera src)
        {
            _cam.stereoTargetEye = StereoTargetEyeMask.None; // plain mono camera, independent of the HMD
            _cam.targetTexture = job.rt;
            _cam.cullingMask = (src != null ? src.cullingMask : ~0) & ~excludedLayers.value;
            _cam.usePhysicalProperties = false;
            _cam.ResetProjectionMatrix();
            _cam.fieldOfView = job.fov;
            _cam.aspect = job.width / (float)job.height;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.allowDynamicResolution = false;
            _cam.useOcclusionCulling = false;
            _cam.transform.SetPositionAndRotation(job.cameraPosition, job.cameraRotation);
        }

        private void HideForCapture(string cardId)
        {
            _hiddenRenderers.Clear();
            _hiddenCanvases.Clear();
            _highlightRenderer = null;
            _highlightPreviousBlock = null;

            if (!showMemoryAnchorMarkers)
            {
                foreach (MemoryAnchor anchor in FindObjectsOfType<MemoryAnchor>())
                {
                    HideRenderersUnder(anchor.gameObject);
                }
            }
            else if (highlightCurrentMarker && !string.IsNullOrEmpty(cardId))
            {
                HighlightMarker(cardId);
            }

            if (hideXrControllerVisuals)
            {
                foreach (XRBaseController controller in FindObjectsOfType<XRBaseController>())
                {
                    HideRenderersUnder(controller.gameObject);
                }

                foreach (XRBaseInteractor interactor in FindObjectsOfType<XRBaseInteractor>())
                {
                    HideRenderersUnder(interactor.gameObject);
                }
            }

            if (extraHideObjects != null)
            {
                for (int i = 0; i < extraHideObjects.Length; i++)
                {
                    if (extraHideObjects[i] != null)
                    {
                        HideRenderersUnder(extraHideObjects[i]);
                    }
                }
            }

            if (hideWorldSpaceCanvases)
            {
                foreach (Canvas canvas in FindObjectsOfType<Canvas>())
                {
                    if (canvas.enabled)
                    {
                        canvas.enabled = false;
                        _hiddenCanvases.Add(canvas);
                    }
                }
            }
        }

        /// <summary>
        /// Temporarily tints the marker whose MemoryAnchorData.id == cardId. Uses a MaterialPropertyBlock
        /// (the same mechanism as MemoryAnchor's hover colour), so the shared material / prefab are untouched.
        /// The previous block (e.g. an active hover colour) is kept and put back by RestoreAfterCapture.
        /// </summary>
        private void HighlightMarker(string cardId)
        {
            foreach (MemoryAnchor anchor in FindObjectsOfType<MemoryAnchor>())
            {
                if (anchor == null || anchor.Data == null || anchor.Data.id != cardId)
                {
                    continue;
                }

                Renderer r = anchor.GetComponent<Renderer>(); // MemoryAnchor tints this same renderer on hover
                if (r == null || !r.enabled)
                {
                    return;
                }

                _highlightPreviousBlock = new MaterialPropertyBlock();
                r.GetPropertyBlock(_highlightPreviousBlock);

                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor(ColorPropertyId, currentMarkerColor);
                r.SetPropertyBlock(block);
                _highlightRenderer = r;
                return;
            }
        }

        private void HideRenderersUnder(GameObject root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r != null && r.enabled)
                {
                    r.enabled = false;
                    _hiddenRenderers.Add(r);
                }
            }
        }

        private void RestoreAfterCapture()
        {
            if (_highlightRenderer != null)
            {
                _highlightRenderer.SetPropertyBlock(_highlightPreviousBlock);
            }

            _highlightRenderer = null;
            _highlightPreviousBlock = null;

            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                if (_hiddenRenderers[i] != null)
                {
                    _hiddenRenderers[i].enabled = true;
                }
            }

            for (int i = 0; i < _hiddenCanvases.Count; i++)
            {
                if (_hiddenCanvases[i] != null)
                {
                    _hiddenCanvases[i].enabled = true;
                }
            }

            _hiddenRenderers.Clear();
            _hiddenCanvases.Clear();
        }

        // ------------------------------------------------------------------ misc

        private static byte[] ReadPixelsSync(RenderTexture rt, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                return tex.GetRawTextureData();
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(tex);
            }
        }

        private void FailJob(CaptureJob job)
        {
            ReleaseRt(job);
            if (job.cardId != null)
            {
                _inFlight.Remove(job.cardId);
            }
        }

        private static void ReleaseRt(CaptureJob job)
        {
            if (job.rt != null)
            {
                job.rt.Release();
                Destroy(job.rt);
                job.rt = null;
            }
        }

        private static bool IsSafeId(string id)
        {
            return !string.IsNullOrEmpty(id) && SafeIdPattern.IsMatch(id);
        }

        private static void SetVec(Vec3Dto dto, Vector3 v)
        {
            dto.x = v.x;
            dto.y = v.y;
            dto.z = v.z;
        }

        private static float Round4(float v)
        {
            return Mathf.Round(v * 10000f) / 10000f;
        }
    }
}
