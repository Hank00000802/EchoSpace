using System.Collections;
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
        [SerializeField] int maxRecordSeconds = 30;
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

        private AudioClip recordingClip;
        private bool isRecording;
        private bool isTranscribing;
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

        public void ResetToIdle()
        {
            if (isRecording)
            {
                Microphone.End(microphoneDevice);
                isRecording = false;
            }

            isTranscribing = false;
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

            if (isRecording)
            {
                Microphone.End(microphoneDevice);
                isRecording = false;
            }
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

            Debug.Log(LogPrefix() + " Recording started on device: " + microphoneDevice);
            recordingClip = Microphone.Start(microphoneDevice, false, maxRecordSeconds, sampleRate);
            isRecording = true;
            SetRecordingVisual();
            SetButtonText("錄音中…");
        }

        private void StopRecordingAndTranscribe()
        {
            if (!isRecording)
            {
                return;
            }

            int position = Microphone.GetPosition(microphoneDevice);
            Microphone.End(microphoneDevice);
            isRecording = false;

            if (recordingClip == null || position <= 0)
            {
                Debug.LogError(LogPrefix() + " Recording failed or empty.");
                SetIdleVisual();
                SetButtonText("再試一次");
                return;
            }

            AudioClip trimmed = TrimClip(recordingClip, position);
            recordingClip = null;

            byte[] wavBytes = WavUtility.FromAudioClip(trimmed);
            if (wavBytes == null || wavBytes.Length == 0)
            {
                Debug.LogError(LogPrefix() + " Failed to convert AudioClip to WAV.");
                SetIdleVisual();
                SetButtonText("再試一次");
                return;
            }

            Debug.Log(LogPrefix() + " WAV size: " + wavBytes.Length + " bytes, samples: " + position);
            StartCoroutine(PostWavToStt(wavBytes));
        }

        private AudioClip TrimClip(AudioClip source, int samplePosition)
        {
            int channels = source.channels;
            int samples = Mathf.Clamp(samplePosition, 1, source.samples);
            float[] data = new float[samples * channels];
            source.GetData(data, 0);

            AudioClip trimmed = AudioClip.Create(
                "VoiceInputTrimmed",
                samples,
                channels,
                source.frequency,
                false);
            trimmed.SetData(data, 0);
            return trimmed;
        }

        private IEnumerator PostWavToStt(byte[] wavBytes)
        {
            isTranscribing = true;
            SetTranscribingVisual();
            SetButtonText("辨識中…");

            WWWForm form = new WWWForm();
            form.AddBinaryData("file", wavBytes, "recording.wav", "audio/wav");

            using (UnityWebRequest request = UnityWebRequest.Post(transcribeUrl, form))
            {
                request.timeout = 60;
                Debug.Log(LogPrefix() + " POST " + transcribeUrl);
                yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool failed = request.result != UnityWebRequest.Result.Success;
#else
                bool failed = request.isNetworkError || request.isHttpError;
#endif
                if (failed)
                {
                    Debug.LogError(LogPrefix() + " STT request failed: " + request.error + " (" + request.responseCode + ")");
                    SetIdleVisual();
                    SetButtonText("再試一次");
                    isTranscribing = false;
                    yield break;
                }

                string json = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Debug.Log(LogPrefix() + " STT raw response: " + json);

                SttResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<SttResponse>(json);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(LogPrefix() + " Failed to parse STT JSON: " + ex.Message);
                    SetIdleVisual();
                    SetButtonText("再試一次");
                    isTranscribing = false;
                    yield break;
                }

                if (response == null || string.IsNullOrWhiteSpace(response.text))
                {
                    Debug.LogWarning(LogPrefix() + " STT returned empty text.");
                    SetIdleVisual();
                    SetButtonText("再試一次");
                    isTranscribing = false;
                    yield break;
                }

                string transcript = response.text.Trim();
                SetRecordingCompleteVisual();
                HandleTranscriptResult(transcript);
            }

            isTranscribing = false;
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
