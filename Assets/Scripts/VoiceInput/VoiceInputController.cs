using System.Collections;
using System.Collections.Generic;
using System.Text;
using EchoSpace.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace EchoSpace.VoiceInput
{
    public class VoiceInputController : MonoBehaviour
    {
        [SerializeField] Button micButton;
        [SerializeField] TMP_Text micButtonText;
        [SerializeField] TMP_InputField targetInputField;
        [Tooltip("Maximum length of a single recording session (seconds). Recording auto-stops at this limit.")]
        [SerializeField] float maxTotalRecordSeconds = 180f;
        [Tooltip("Target length of each chunk sent to STT while recording continues (seconds).")]
        [SerializeField] float chunkSeconds = 25f;
        [Tooltip("Chunk boundary is moved to the quietest spot within this window before the target length (seconds).")]
        [SerializeField] float chunkCutSearchSeconds = 2f;
        [Tooltip("Show the remaining-time hint when this many seconds are left.")]
        [SerializeField] float remainingWarningSeconds = 20f;
        [SerializeField] string remainingWarningText = "剩餘 20 秒，可以慢慢做個總結。";
        [Tooltip("Per-chunk HTTP timeout (seconds).")]
        [SerializeField] int requestTimeoutSeconds = 60;
        [Tooltip("Extra attempts for a chunk whose request failed.")]
        [SerializeField] int chunkRetryCount = 1;
        [SerializeField] int sampleRate = 16000;
        [SerializeField] string microphoneDevice = null;
        [SerializeField] string transcribeUrl = "http://127.0.0.1:8000/transcribe";
        [SerializeField] bool appendToExistingText = true;
        [Tooltip("If on, append transcript on a new line instead of a space.")]
        [SerializeField] bool appendOnNewLine = false;
        [Tooltip("Optional label used in Debug.Log, e.g. PastReflection.")]
        [SerializeField] string contextName = "";
        [Header("Annotation Mic Visual")]
        [SerializeField] Transform micButtonRoot;
        [SerializeField] Button hitBoxButton;
        [SerializeField] GameObject circleVisual;
        [SerializeField] GameObject squareIcon;
        [SerializeField] GameObject recordingIcon;
        [Tooltip("If off, STT still runs but text is not written into the input field.")]
        [SerializeField] bool applyTranscriptToInputField = true;
        [Header("Where to Use")]
        [SerializeField] bool useForAnnotationPanel = true;

        public event System.Action<string> TranscriptReceived;
        public string LastTranscript { get; private set; }

        private const int RingBufferSeconds = 60;

        private sealed class ChunkResult
        {
            public bool Done;
            public bool Ok;
            public string Text;
        }

        private AudioClip recordingClip;
        private bool isRecording;
        private bool isTranscribing;

        // Chunked recording state
        private int _ringFrames;
        private int _channels = 1;
        private int _actualRate = 16000;
        private int _lastReadFrame;
        private long _totalFrames;
        private bool _warningShown;
        private bool _stopRequested;
        private int _pendingChunks;
        private int _sessionId;
        private readonly List<float> _chunkBuffer = new List<float>();
        private readonly List<ChunkResult> _chunkResults = new List<ChunkResult>();
        private bool _useIconVisual;
        private bool _skipShowingTranscript;
        private bool _cachedHitBoxColors;
        private ColorBlock _hitBoxColors;
        private Image _recordingHoverOverlay;

        private void Awake()
        {
            ResolveMicVisuals();
            _skipShowingTranscript = _skipShowingTranscript
                || !applyTranscriptToInputField
                || GetComponentInParent<AnnotationPanelController>() != null;

            Button clickButton = hitBoxButton != null ? hitBoxButton : micButton;
            if (clickButton == null)
            {
                Debug.LogError(LogPrefix() + " micButton / HitBoxButton is not assigned.");
                return;
            }

            if (targetInputField == null)
            {
                Debug.LogWarning(LogPrefix() + " targetInputField is not assigned.");
            }

            clickButton.onClick.AddListener(OnMicButtonClicked);
            UiSfx.BindClick(clickButton);
            if (micButton != null && micButton != clickButton)
            {
                micButton.onClick.AddListener(OnMicButtonClicked);
                UiSfx.BindClick(micButton);
            }

            ResetToIdle();
        }

        private void OnEnable()
        {
            if (!isRecording && !isTranscribing)
            {
                ResetToIdle();
            }
        }

        private void OnDisable()
        {
            // Coroutines die with the object; drop any in-flight session so the state cannot get stuck.
            if (isRecording || isTranscribing)
            {
                CancelSession();
            }
        }

        private void Update()
        {
            if (isRecording)
            {
                PollRecording();
            }
        }

        private void CancelSession()
        {
            if (isRecording)
            {
                Microphone.End(microphoneDevice);
                isRecording = false;
            }

            _sessionId++;
            _stopRequested = false;
            _pendingChunks = 0;
            _warningShown = false;
            _totalFrames = 0;
            _lastReadFrame = 0;
            _chunkBuffer.Clear();
            _chunkResults.Clear();
            recordingClip = null;
            isTranscribing = false;
        }

        public void ResetToIdle()
        {
            CancelSession();
            ResolveMicVisuals();
            SetIdleVisual();
            SetButtonText(GetIdleButtonText());
            Debug.Log(
                LogPrefix()
                + " Mic reset to idle. root=" + (micButtonRoot != null ? micButtonRoot.name : "null")
                + " square=" + (squareIcon != null ? squareIcon.activeSelf.ToString() : "null")
                + " recording=" + (recordingIcon != null ? recordingIcon.activeSelf.ToString() : "null")
                + " text=" + (micButtonText != null ? micButtonText.text : "null"));
        }

        private string GetIdleButtonText()
        {
            return "語音輸入請按右邊按鈕";
        }

        private void OnDestroy()
        {
            Button clickButton = hitBoxButton != null ? hitBoxButton : micButton;
            if (clickButton != null)
            {
                clickButton.onClick.RemoveListener(OnMicButtonClicked);
            }

            if (micButton != null && micButton != clickButton)
            {
                micButton.onClick.RemoveListener(OnMicButtonClicked);
            }

            CancelSession();
        }

        private void OnMicButtonClicked()
        {
            if (isTranscribing)
            {
                Debug.Log(LogPrefix() + " Still transcribing, ignore click.");
                return;
            }

            if (!isRecording)
            {
                StartRecording();
            }
            else
            {
                StopRecordingAndTranscribe();
            }
        }

        private void StartRecording()
        {
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Debug.LogError(LogPrefix() + " No microphone devices found.");
                SetButtonText("無麥克風");
                SetIdleVisual();
                return;
            }

            if (string.IsNullOrEmpty(microphoneDevice))
            {
                microphoneDevice = Microphone.devices[0];
            }

            // Fresh session (also invalidates results of any older in-flight requests).
            CancelSession();

            Debug.Log(LogPrefix() + " Recording started on device: " + microphoneDevice
                + " (chunked, max " + maxTotalRecordSeconds + "s, chunk ~" + chunkSeconds + "s)");

            // Ring buffer (loop=true) so recording never silently stops; we drain it every frame.
            recordingClip = Microphone.Start(microphoneDevice, true, RingBufferSeconds, sampleRate);
            if (recordingClip == null)
            {
                Debug.LogError(LogPrefix() + " Microphone.Start returned null.");
                SetIdleVisual();
                SetButtonText("再試一次");
                return;
            }

            _ringFrames = Mathf.Max(1, recordingClip.samples);
            _channels = Mathf.Max(1, recordingClip.channels);
            _actualRate = Mathf.Max(1, recordingClip.frequency);
            _lastReadFrame = 0;
            _totalFrames = 0;
            _warningShown = false;
            _stopRequested = false;
            _chunkBuffer.Clear();
            _chunkResults.Clear();

            isRecording = true;
            SetRecordingVisual();
            SetButtonText("錄音中…");
        }

        // ---- Chunked recording ------------------------------------------------

        private void PollRecording()
        {
            if (!Microphone.IsRecording(microphoneDevice))
            {
                Debug.LogWarning(LogPrefix() + " Microphone stopped unexpectedly; finishing recording.");
                StopRecordingAndTranscribe();
                return;
            }

            ReadAvailableSamples();
            FlushFullChunks();

            float elapsed = (float)_totalFrames / _actualRate;
            float limit = Mathf.Max(5f, maxTotalRecordSeconds);

            if (!_warningShown
                && remainingWarningSeconds > 0f
                && limit - elapsed <= remainingWarningSeconds)
            {
                _warningShown = true;
                SetButtonText(remainingWarningText);
            }

            if (elapsed >= limit)
            {
                Debug.Log(LogPrefix() + " Reached max recording length (" + limit + "s); auto stop.");
                StopRecordingAndTranscribe();
            }
        }

        private void ReadAvailableSamples()
        {
            if (recordingClip == null || _ringFrames <= 0)
            {
                return;
            }

            int pos = Microphone.GetPosition(microphoneDevice);
            if (pos < 0)
            {
                return;
            }

            pos %= _ringFrames;
            if (pos == _lastReadFrame)
            {
                return;
            }

            int frames = pos > _lastReadFrame
                ? pos - _lastReadFrame
                : _ringFrames - _lastReadFrame + pos;
            if (frames <= 0)
            {
                return;
            }

            int firstFrames = Mathf.Min(frames, _ringFrames - _lastReadFrame);
            float[] first = new float[firstFrames * _channels];
            recordingClip.GetData(first, _lastReadFrame);
            _chunkBuffer.AddRange(first);

            int secondFrames = frames - firstFrames;
            if (secondFrames > 0)
            {
                float[] second = new float[secondFrames * _channels];
                recordingClip.GetData(second, 0);
                _chunkBuffer.AddRange(second);
            }

            _lastReadFrame = pos;
            _totalFrames += frames;
        }

        private void FlushFullChunks()
        {
            float target = Mathf.Clamp(chunkSeconds, 5f, 40f);
            float search = Mathf.Clamp(chunkCutSearchSeconds, 0f, target * 0.5f);
            int triggerFrames = Mathf.RoundToInt(target * _actualRate);
            int searchFrames = Mathf.RoundToInt(search * _actualRate);
            int bufferFrames = _chunkBuffer.Count / _channels;

            // Wait until the buffer is a bit longer than the target, then cut at the quietest spot
            // inside [target - search, target + search] so words are rarely split.
            while (bufferFrames >= triggerFrames + searchFrames)
            {
                int cutFrame = FindQuietCutFrame(triggerFrames - searchFrames, triggerFrames + searchFrames);
                SendBufferedFrames(cutFrame);
                bufferFrames = _chunkBuffer.Count / _channels;
            }
        }

        private int FindQuietCutFrame(int fromFrame, int toFrame)
        {
            int window = Mathf.Max(1, _actualRate / 20); // 50 ms
            int maxStart = toFrame - window;
            if (maxStart <= fromFrame)
            {
                return Mathf.Max(1, toFrame);
            }

            float bestEnergy = float.MaxValue;
            int bestStart = fromFrame;
            for (int start = fromFrame; start <= maxStart; start += window)
            {
                float energy = 0f;
                for (int i = 0; i < window; i++)
                {
                    int idx = (start + i) * _channels;
                    float s = _chunkBuffer[idx];
                    energy += s * s;
                }

                if (energy < bestEnergy)
                {
                    bestEnergy = energy;
                    bestStart = start;
                }
            }

            return bestStart + window / 2;
        }

        private void SendBufferedFrames(int frames)
        {
            frames = Mathf.Clamp(frames, 1, _chunkBuffer.Count / _channels);
            int floatCount = frames * _channels;
            float[] samples = _chunkBuffer.GetRange(0, floatCount).ToArray();
            _chunkBuffer.RemoveRange(0, floatCount);

            byte[] wavBytes = WavUtility.FromSamples(samples, _channels, _actualRate);
            if (wavBytes == null || wavBytes.Length == 0)
            {
                Debug.LogError(LogPrefix() + " Failed to convert chunk to WAV.");
                return;
            }

            int index = _chunkResults.Count;
            _chunkResults.Add(new ChunkResult());
            _pendingChunks++;
            Debug.Log(LogPrefix() + " Chunk #" + index + " queued: " + wavBytes.Length + " bytes, "
                + (frames / (float)_actualRate).ToString("F1") + "s");
            StartCoroutine(PostChunkToStt(_sessionId, index, wavBytes));
        }

        private void StopRecordingAndTranscribe()
        {
            if (!isRecording)
            {
                return;
            }

            ReadAvailableSamples();
            Microphone.End(microphoneDevice);
            isRecording = false;
            recordingClip = null;

            int remainingFrames = _chunkBuffer.Count / _channels;
            float remainingSeconds = remainingFrames / (float)_actualRate;
            float minSeconds = _chunkResults.Count > 0 ? 0.4f : 0.2f;
            if (remainingFrames > 0 && remainingSeconds >= minSeconds)
            {
                SendBufferedFrames(remainingFrames);
            }

            _chunkBuffer.Clear();

            if (_chunkResults.Count == 0)
            {
                Debug.LogError(LogPrefix() + " Recording failed or empty.");
                SetIdleVisual();
                SetButtonText("再試一次");
                return;
            }

            Debug.Log(LogPrefix() + " Recording stopped. total=" + ((float)_totalFrames / _actualRate).ToString("F1")
                + "s, chunks=" + _chunkResults.Count + ", pending=" + _pendingChunks);

            _stopRequested = true;
            isTranscribing = true;
            SetTranscribingVisual();
            SetButtonText("辨識中…");
            TryFinalizeTranscription();
        }

        private IEnumerator PostChunkToStt(int session, int index, byte[] wavBytes)
        {
            int attempts = 1 + Mathf.Max(0, chunkRetryCount);
            bool ok = false;
            string text = string.Empty;

            for (int attempt = 0; attempt < attempts && !ok; attempt++)
            {
                WWWForm form = new WWWForm();
                form.AddBinaryData("file", wavBytes, "recording.wav", "audio/wav");

                using (UnityWebRequest request = UnityWebRequest.Post(transcribeUrl, form))
                {
                    request.timeout = Mathf.Max(5, requestTimeoutSeconds);
                    Debug.Log(LogPrefix() + " POST chunk #" + index + " (attempt " + (attempt + 1) + ") " + transcribeUrl);
                    yield return request.SendWebRequest();

                    if (session != _sessionId)
                    {
                        yield break;
                    }

#if UNITY_2020_2_OR_NEWER
                    bool failed = request.result != UnityWebRequest.Result.Success;
#else
                    bool failed = request.isNetworkError || request.isHttpError;
#endif
                    if (failed)
                    {
                        Debug.LogError(LogPrefix() + " STT request failed (chunk #" + index + "): "
                            + request.error + " (" + request.responseCode + ")");
                        continue;
                    }

                    string json = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    Debug.Log(LogPrefix() + " STT raw response (chunk #" + index + "): " + json);

                    try
                    {
                        SttResponse response = JsonUtility.FromJson<SttResponse>(json);
                        text = response != null && response.text != null ? response.text.Trim() : string.Empty;
                        ok = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(LogPrefix() + " Failed to parse STT JSON (chunk #" + index + "): " + ex.Message);
                    }
                }
            }

            if (session != _sessionId || index >= _chunkResults.Count)
            {
                yield break;
            }

            ChunkResult result = _chunkResults[index];
            result.Done = true;
            result.Ok = ok;
            result.Text = text;
            _pendingChunks = Mathf.Max(0, _pendingChunks - 1);
            TryFinalizeTranscription();
        }

        private void TryFinalizeTranscription()
        {
            if (!_stopRequested || _pendingChunks > 0)
            {
                return;
            }

            _stopRequested = false;

            StringBuilder joined = new StringBuilder();
            int failedCount = 0;
            for (int i = 0; i < _chunkResults.Count; i++)
            {
                ChunkResult r = _chunkResults[i];
                if (!r.Ok)
                {
                    failedCount++;
                    continue;
                }

                AppendJoined(joined, r.Text);
            }

            int total = _chunkResults.Count;
            _chunkResults.Clear();

            string transcript = joined.ToString().Trim();
            if (failedCount > 0)
            {
                Debug.LogWarning(LogPrefix() + " " + failedCount + "/" + total + " chunk(s) failed; using the rest.");
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                Debug.LogWarning(LogPrefix() + " STT returned empty text (chunks=" + total + ", failed=" + failedCount + ").");
                SetIdleVisual();
                SetButtonText("再試一次");
                isTranscribing = false;
                return;
            }

            SetRecordingCompleteVisual();
            HandleTranscriptResult(transcript);
            isTranscribing = false;
        }

        private static void AppendJoined(StringBuilder sb, string part)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                return;
            }

            part = part.Trim();
            if (sb.Length > 0)
            {
                char last = sb[sb.Length - 1];
                char first = part[0];
                bool needSpace = IsAsciiWordChar(last) && IsAsciiWordChar(first);
                if (needSpace || (!char.IsWhiteSpace(last) && char.IsPunctuation(last) && last < 128 && first < 128))
                {
                    sb.Append(' ');
                }
            }

            sb.Append(part);
        }

        private static bool IsAsciiWordChar(char c)
        {
            return c < 128 && char.IsLetterOrDigit(c);
        }

        public void SetSkipShowingTranscript(bool skip)
        {
            _skipShowingTranscript = skip;
        }

        private void HandleTranscriptResult(string transcript)
        {
            LastTranscript = transcript ?? string.Empty;

            if (TranscriptReceived != null)
            {
                TranscriptReceived.Invoke(LastTranscript);
            }

            if (_skipShowingTranscript)
            {
                Debug.Log(LogPrefix() + " Transcript ready, skip showing in input field: \"" + transcript + "\"");
                NotifyAnnotationPanelTranscriptApplied(transcript);
                return;
            }

            ApplyTranscript(transcript);
        }

        private void ApplyTranscript(string transcript)
        {
            if (targetInputField == null)
            {
                Debug.LogWarning(LogPrefix() + " targetInputField is not assigned; cannot apply transcript.");
                return;
            }

            if (!targetInputField.isActiveAndEnabled || !targetInputField.gameObject.activeInHierarchy)
            {
                Debug.LogWarning(LogPrefix() + " Target input field is inactive; transcript stored but may not be visible.");
            }

            string oldText = NormalizeInputText(targetInputField.text);
            bool shouldAppend = appendToExistingText && oldText.Length > 0;
            string separator = appendOnNewLine ? "\n" : " ";
            string newText = shouldAppend ? oldText + separator + transcript : transcript;

            targetInputField.SetTextWithoutNotify(newText);
            targetInputField.text = newText;

            TMP_Text textComponent = targetInputField.textComponent;
            if (textComponent != null)
            {
                textComponent.text = newText;
                textComponent.raycastTarget = false;
                textComponent.SetAllDirty();
                textComponent.ForceMeshUpdate(true);
            }

            TMP_Text placeholder = targetInputField.placeholder as TMP_Text;
            if (placeholder != null)
            {
                placeholder.raycastTarget = false;
                placeholder.enabled = newText.Length == 0;
            }

            targetInputField.caretPosition = newText.Length;
            targetInputField.selectionAnchorPosition = newText.Length;
            targetInputField.selectionFocusPosition = newText.Length;
            targetInputField.ForceLabelUpdate();
            targetInputField.onValueChanged?.Invoke(newText);
            Canvas.ForceUpdateCanvases();

            Debug.Log(LogPrefix() + " Applied transcript length=" + transcript.Length
                + " old=" + oldText.Length + " new=" + newText.Length
                + " fieldText=\"" + targetInputField.text + "\"");

            NotifyAnnotationPanelTranscriptApplied(transcript);
        }

        private void NotifyAnnotationPanelTranscriptApplied(string transcript = null)
        {
            AnnotationPanelController panel = GetComponentInParent<AnnotationPanelController>();
            if (panel == null)
            {
                return;
            }

            Debug.Log("[VoiceInput] Transcript applied, keep selected category: " + panel.SelectedMemoryType);
            if (!string.IsNullOrEmpty(transcript))
            {
                panel.NotifyTranscriptApplied(transcript);
            }
            else
            {
                panel.NotifyTranscriptApplied();
            }
        }

        private void ResolveMicVisuals()
        {
            if (micButtonRoot == null)
            {
                micButtonRoot = FindChildByNames(transform, "MicButtonRoot");
            }

            Transform searchRoot = micButtonRoot != null ? micButtonRoot : transform;

            if (hitBoxButton == null)
            {
                Transform hitBox = FindChildByNames(searchRoot, "HitBoxButton", "HitBox");
                if (hitBox != null)
                {
                    hitBoxButton = hitBox.GetComponent<Button>();
                    if (hitBoxButton == null)
                    {
                        hitBoxButton = hitBox.gameObject.AddComponent<Button>();
                    }

                    Image hitImage = hitBox.GetComponent<Image>();
                    if (hitImage == null)
                    {
                        hitImage = hitBox.gameObject.AddComponent<Image>();
                        hitImage.color = new Color(1f, 1f, 1f, 0.01f);
                    }

                    hitImage.raycastTarget = true;
                    if (hitBoxButton.targetGraphic == null)
                    {
                        hitBoxButton.targetGraphic = hitImage;
                    }

                    // Keep Inspector ColorTint / highlight colors. Do not force Transition.None.
                }
            }

            CacheHitBoxColorTint();

            if (circleVisual == null)
            {
                Transform circle = FindChildByNames(
                    searchRoot,
                    "CircleVisual",
                    "circilevisula",
                    "circilevisual",
                    "CircleVisula",
                    "Circle");
                if (circle != null)
                {
                    circleVisual = circle.gameObject;
                }
            }

            if (squareIcon == null)
            {
                Transform square = FindChildByNames(searchRoot, "SquareIcon", "squareicon", "Square");
                if (square != null)
                {
                    squareIcon = square.gameObject;
                }
            }

            if (recordingIcon == null)
            {
                Transform recording = FindChildByNames(
                    searchRoot,
                    "RecordingIcon",
                    "recordingicon",
                    "Recording");
                if (recording != null)
                {
                    recordingIcon = recording.gameObject;
                }
            }

            _useIconVisual = squareIcon != null || recordingIcon != null;
            HideCircleVisual();
            if (_useIconVisual)
            {
                DisableGraphicRaycast(circleVisual);
                DisableGraphicRaycast(squareIcon);
                DisableGraphicRaycast(recordingIcon);
                Debug.Log(LogPrefix()
                    + " Mic visual resolved. root=" + (micButtonRoot != null ? micButtonRoot.name : "null")
                    + " hitBox=" + (hitBoxButton != null ? hitBoxButton.name : "null")
                    + " square=" + (squareIcon != null ? squareIcon.name : "null")
                    + " recording=" + (recordingIcon != null ? recordingIcon.name : "null"));
            }
        }

        private static void DisableGraphicRaycast(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            foreach (Graphic graphic in graphics)
            {
                if (graphic != null)
                {
                    graphic.raycastTarget = false;
                }
            }
        }

        private void SetIdleVisual()
        {
            HideCircleVisual();

            if (squareIcon != null)
            {
                squareIcon.SetActive(true);
            }

            if (recordingIcon != null)
            {
                recordingIcon.SetActive(false);
            }

            ApplyIdleHitBoxTint();
        }

        private void SetTranscribingVisual()
        {
            HideCircleVisual();

            if (squareIcon != null)
            {
                squareIcon.SetActive(false);
            }

            if (recordingIcon != null)
            {
                recordingIcon.SetActive(false);
            }

            ApplyIdleHitBoxTint();
        }

        private void SetRecordingVisual()
        {
            HideCircleVisual();

            if (!_useIconVisual)
            {
                ClearHitBoxSelection();
                return;
            }

            if (squareIcon != null)
            {
                squareIcon.SetActive(false);
            }

            if (recordingIcon != null)
            {
                recordingIcon.SetActive(true);
            }

            ApplyRecordingHitBoxTint();
        }

        private void HideCircleVisual()
        {
            if (circleVisual != null)
            {
                circleVisual.SetActive(false);
            }
        }

        private void CacheHitBoxColorTint()
        {
            if (hitBoxButton == null || _cachedHitBoxColors)
            {
                return;
            }

            Navigation navigation = hitBoxButton.navigation;
            navigation.mode = Navigation.Mode.None;
            hitBoxButton.navigation = navigation;

            _hitBoxColors = hitBoxButton.colors;
            _cachedHitBoxColors = true;

            ColorBlock colors = _hitBoxColors;
            colors.selectedColor = colors.highlightedColor;
            hitBoxButton.colors = colors;
            hitBoxButton.transition = Selectable.Transition.ColorTint;
        }

        private void ApplyIdleHitBoxTint()
        {
            ApplyHitBoxHoverOverlay(false);
        }

        private void ApplyRecordingHitBoxTint()
        {
            ApplyHitBoxHoverOverlay(true);
        }

        // Hover tint stays behind SquareIcon / RecordingIcon. CircleVisual is unused.
        private void ApplyHitBoxHoverOverlay(bool recording)
        {
            if (hitBoxButton == null)
            {
                return;
            }

            Image overlay = GetOrCreateRecordingHoverOverlay();
            overlay.gameObject.SetActive(true);
            overlay.raycastTarget = false;
            EnsureHoverOverlayBehindIcons(overlay);
            overlay.color = recording ? new Color(1f, 1f, 1f, 0.7f) : Color.white;
            hitBoxButton.targetGraphic = overlay;
            hitBoxButton.transition = Selectable.Transition.ColorTint;

            Color highlighted = _cachedHitBoxColors
                ? _hitBoxColors.highlightedColor
                : hitBoxButton.colors.highlightedColor;
            ColorBlock colors = _cachedHitBoxColors ? _hitBoxColors : hitBoxButton.colors;
            colors.normalColor = new Color(highlighted.r, highlighted.g, highlighted.b, 0f);
            colors.highlightedColor = highlighted;
            colors.selectedColor = highlighted;
            colors.pressedColor = recording
                ? highlighted
                : (_cachedHitBoxColors ? _hitBoxColors.pressedColor : colors.pressedColor);
            hitBoxButton.colors = colors;

            ClearHitBoxSelection();
        }

        private Image GetOrCreateRecordingHoverOverlay()
        {
            if (_recordingHoverOverlay != null)
            {
                EnsureHoverOverlayBehindIcons(_recordingHoverOverlay);
                return _recordingHoverOverlay;
            }

            GameObject overlayObject = new GameObject(
                "RecordingHoverOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            overlayObject.transform.SetParent(hitBoxButton.transform, false);

            RectTransform rect = overlayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image overlay = overlayObject.GetComponent<Image>();
            Image hitImage = hitBoxButton.GetComponent<Image>();
            if (hitImage != null)
            {
                overlay.sprite = hitImage.sprite;
                overlay.type = hitImage.type;
                overlay.preserveAspect = hitImage.preserveAspect;
                if (hitImage.material != null)
                {
                    overlay.material = hitImage.material;
                }
            }

            overlay.raycastTarget = false;
            overlay.color = Color.white;
            _recordingHoverOverlay = overlay;
            EnsureHoverOverlayBehindIcons(overlay);
            return overlay;
        }

        private void EnsureHoverOverlayBehindIcons(Image overlay)
        {
            if (overlay == null)
            {
                return;
            }

            HideCircleVisual();

            Canvas overlayCanvas = overlay.GetComponent<Canvas>();
            if (overlayCanvas != null)
            {
                overlayCanvas.overrideSorting = false;
                overlayCanvas.enabled = false;
            }

            RectTransform rect = overlay.rectTransform;
            Vector3 local = rect.localPosition;
            rect.localPosition = new Vector3(local.x, local.y, 0f);
            overlay.transform.SetAsFirstSibling();

            if (squareIcon != null)
            {
                squareIcon.transform.SetAsLastSibling();
            }

            if (recordingIcon != null)
            {
                recordingIcon.transform.SetAsLastSibling();
            }
        }

        private void ClearHitBoxSelection()
        {
            if (hitBoxButton == null || EventSystem.current == null)
            {
                return;
            }

            if (EventSystem.current.currentSelectedGameObject == hitBoxButton.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private static Transform FindChildByNames(Transform root, params string[] names)
        {
            if (root == null || names == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                if (string.Equals(root.name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByNames(root.GetChild(i), names);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void SetRecordingCompleteVisual()
        {
            if (squareIcon != null)
            {
                squareIcon.SetActive(true);
            }

            if (recordingIcon != null)
            {
                recordingIcon.SetActive(false);
            }

            ApplyIdleHitBoxTint();

            if (micButtonText == null)
            {
                Button clickButton = hitBoxButton != null ? hitBoxButton : micButton;
                if (clickButton != null)
                {
                    micButtonText = clickButton.GetComponentInChildren<TMP_Text>(true);
                }
            }

            if (micButtonText != null)
            {
                micButtonText.gameObject.SetActive(true);
                micButtonText.raycastTarget = false;
                if (useForAnnotationPanel)
                {
                    micButtonText.text = EchoSpace.UI.ControllerTutorialGate.IsTutorialActive
                        ? "錄音結束，此為練習模式，可按關閉"
                        : "錄音完畢，請按儲存~";
                }
                else
                {
                    micButtonText.text = "錄音完畢，請按生成~";
                }
            }
            else
            {
                Debug.LogWarning(LogPrefix() + " MicButton has no TMP_Text; cannot show 錄音完畢.");
            }

            Debug.Log(LogPrefix() + " Mic button shows 錄音完畢");
        }

        private static string NormalizeInputText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\u200B", string.Empty).Trim();
        }

        private void SetButtonText(string text)
        {
            if (micButtonText != null)
            {
                micButtonText.raycastTarget = false;
                micButtonText.text = text;
            }
        }

        private string LogPrefix()
        {
            if (string.IsNullOrEmpty(contextName))
            {
                return "[VoiceInput]";
            }

            return "[VoiceInput][" + contextName + "]";
        }
    }
}
