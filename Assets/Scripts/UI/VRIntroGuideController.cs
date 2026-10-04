using System.Collections;
using System.Collections.Generic;
using EchoSpace.Annotation;
using EchoSpace.LineOfLife;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace EchoSpace.UI
{
    /// <summary>
    /// Head-locked intro text + world-fixed 360 black sphere environment.
    /// While active, forces Exploration mode (turn OK, annotate blocked).
    ///
    /// IntroBlackSphere needs EchoSpace/IntroBlackUnlit (Cull Off + alpha) so the
    /// interior is visible and can fade out. If not, invert sphere scale.
    /// </summary>
    public class VRIntroGuideController : MonoBehaviour
    {
        /// <summary>
        /// True only while intro text is playing. Controller tutorial keeps this
        /// false so users can practice bindings before intro starts.
        /// </summary>
        public static bool IsIntroActive { get; private set; }

        [SerializeField] private GameObject introRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [Tooltip("Legacy flat black panel. Leave unused when useBlackSphereIntro is on.")]
        [SerializeField] private Image blackBackground;
        [SerializeField] private TMP_Text introText;
        [SerializeField] private TMP_Text skipHintText;

        // Not serialized — edit here only; scene Inspector will not override.
        private readonly string[] introLines =
        {
            "歡迎來到 EchoSpace。",
            "這是一個個人化的 VR 反思系統，\n它會帶你重新進入一個熟悉的空間，",
            "並透過記憶標記與時間整理，\n來協助你回想...",
            "在經歷一段生活轉變後，\n與這個空間相關的過去",
            "接下來的體驗中，\n你不需要說出完整的人生故事",
            "只要依照自己的感受，\n簡短記錄想到的內容就可以~",
            "如果有任何問題讓你不想回答，\n可以選擇簡短回答、跳過或隨時暫停",
            "現在可以先慢慢自由地探索~\n然後開始標記在這環境中的記憶吧~"
        };

        [SerializeField] private float initialDelay = 0.5f;
        [SerializeField] private float fadeDuration = 0.8f;
        [SerializeField] private float lineDuration = 2.5f;
        [SerializeField] private bool allowSkip = true;
        [SerializeField] private KeyCode skipKey = KeyCode.Space;
        [SerializeField] private bool skipWithRightTrigger = true;
        [SerializeField] private bool playOnStart = false;
        [SerializeField] private UnityEvent onIntroFinished;

        [Header("360 Black Environment")]
        [Tooltip("World-space sphere/dome. Do NOT parent under Main Camera.")]
        [SerializeField] private GameObject introBlackSphere;
        [Tooltip("3DGS / room roots hidden while the black sphere is active. After intro, each room restores its original active state.")]
        [SerializeField] private GameObject[] roomRootsToHideDuringIntro;
        [Tooltip("Rooms that start inactive (or stay hidden during intro) and turn on only after intro ends.")]
        [SerializeField] private GameObject[] roomRootsToEnableAfterIntro;
        [Tooltip("Stay active from black-environment start through after intro. Typical: Room02_Floor. Unparented at runtime if a hidden ancestor would hide them.")]
        [SerializeField] private GameObject[] objectsToKeepVisibleDuringIntro;
        [SerializeField] private bool hideRoomDuringIntro = true;
        [SerializeField] private bool useBlackSphereIntro = true;
        [SerializeField] private bool centerSphereOnPlayerAtStart = true;
        [Tooltip("IntroText + SkipHint fade out first.")]
        [SerializeField] private float outroTextFadeDuration = 1.2f;
        [Tooltip("Black sphere fade after texts (slower).")]
        [SerializeField] private float sphereFadeDuration = 2.0f;

        [Header("Exploration During Intro")]
        [Tooltip("Forced into Exploration while intro plays so right trigger only skips.")]
        [SerializeField] private AirAnchorPlacementTester placementTester;
        [SerializeField] private bool restoreMarkingModeAfterIntro = true;

        [Header("Head Lock")]
        [SerializeField] private Transform headCamera;
        [SerializeField] private Vector3 headLocalPosition = new Vector3(0f, 0f, 1.5f);
        [SerializeField] private Vector3 headLocalScale = new Vector3(0.001f, 0.001f, 0.001f);
        [SerializeField] private bool attachToHeadOnAwake = true;

        private bool _isPlaying;
        private bool _skipRequested;
        private bool _finished;
        private bool _wasTriggerPressed;
        private Coroutine _playRoutine;
        private Material _sphereMaterialInstance;
        private bool[] _hiddenRoomWasActive;
        private bool _hiddenRoomStatesCached;
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private readonly List<XRInputDevice> _rightHandDevices = new List<XRInputDevice>();

        private void Awake()
        {
            IsIntroActive = false;

            if (introRoot == null)
            {
                introRoot = gameObject;
            }

            if (canvasGroup == null)
            {
                canvasGroup = introRoot.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = introRoot.AddComponent<CanvasGroup>();
                }
            }

            if (placementTester == null)
            {
                placementTester = FindObjectOfType<AirAnchorPlacementTester>();
            }

            if (attachToHeadOnAwake)
            {
                AttachToHeadCamera();
            }

            ConfigureCanvas();
            PrepareBlackEnvironmentOnly();
        }

        private void OnDestroy()
        {
            if (IsIntroActive)
            {
                IsIntroActive = false;
            }

            if (_sphereMaterialInstance != null)
            {
                Destroy(_sphereMaterialInstance);
                _sphereMaterialInstance = null;
            }
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayIntro();
            }
        }

        private void Update()
        {
            if (!_isPlaying || _finished || !allowSkip)
            {
                return;
            }

            if (Input.GetKeyDown(skipKey))
            {
                SkipIntro();
                return;
            }

            if (skipWithRightTrigger && WasRightTriggerPressedThisFrame())
            {
                SkipIntro();
            }
        }

        public void PlayIntro()
        {
            if (_finished)
            {
                return;
            }

            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
            }

            _skipRequested = false;
            _isPlaying = true;
            IsIntroActive = true;
            CloseOpenPracticeUi();

            if (introRoot != null)
            {
                introRoot.SetActive(true);
            }

            BeginIntroEnvironment();
            ApplyExplorationModeForIntro();
            PrepareVisualsForStart();
            _playRoutine = StartCoroutine(PlayIntroRoutine());
            Debug.Log("[StartupFlow] VR intro started");
        }

        public void SkipIntro()
        {
            if (!_isPlaying || _finished)
            {
                return;
            }

            _skipRequested = true;

            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
                _playRoutine = null;
            }

            _playRoutine = StartCoroutine(SkipAndFinishRoutine());
        }

        private IEnumerator SkipAndFinishRoutine()
        {
            // Same outro path as natural end: texts first, then slow sphere fade.
            yield return FadeOutIntroMask();
            FinishIntro();
        }

        /// <summary>
        /// Ends intro after fade: restores rooms, clears intro lock, restores marking if configured.
        /// </summary>
        public void FinishIntro()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _isPlaying = false;

            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
                _playRoutine = null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            RestoreRoomRoots();
            SetSphereAlpha(0f);

            if (introBlackSphere != null)
            {
                introBlackSphere.SetActive(false);
            }

            if (introRoot != null)
            {
                introRoot.SetActive(false);
            }

            // Clear before restoring Marking so placement is no longer blocked.
            IsIntroActive = false;
            RestoreInteractionModeAfterIntro();

            if (onIntroFinished != null)
            {
                onIntroFinished.Invoke();
            }

            Debug.Log("[StartupFlow] VR intro finished, EchoSpace begins");
            Debug.Log("[VRIntroGuide] Intro finished.");
        }

        private void PrepareBlackEnvironmentOnly()
        {
            if (blackBackground != null && useBlackSphereIntro)
            {
                blackBackground.gameObject.SetActive(false);
            }

            BeginIntroEnvironment();

            if (introRoot != null && introRoot != gameObject)
            {
                introRoot.SetActive(false);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            if (introText != null)
            {
                introText.text = string.Empty;
            }

            Debug.Log("[StartupFlow] Black environment ready");
        }

        private void BeginIntroEnvironment()
        {
            if (useBlackSphereIntro && introBlackSphere != null)
            {
                if (centerSphereOnPlayerAtStart)
                {
                    EnsurePlayerCamera();
                    if (headCamera != null)
                    {
                        introBlackSphere.transform.position = headCamera.position;
                    }
                }

                EnsureSphereMaterialInstance();
                SetSphereAlpha(1f);
                introBlackSphere.SetActive(true);
            }

            HideRoomsForIntro();
        }

        private void HideRoomsForIntro()
        {
            if (!hideRoomDuringIntro)
            {
                return;
            }

            CacheAndHideRoomRoots();
            SetRoomArrayActive(roomRootsToEnableAfterIntro, false);
            KeepVisibleObjectsForIntro();
        }

        private void CacheAndHideRoomRoots()
        {
            if (roomRootsToHideDuringIntro == null)
            {
                _hiddenRoomWasActive = null;
                _hiddenRoomStatesCached = false;
                return;
            }

            bool captureOriginal = !_hiddenRoomStatesCached
                || _hiddenRoomWasActive == null
                || _hiddenRoomWasActive.Length != roomRootsToHideDuringIntro.Length;

            if (captureOriginal)
            {
                _hiddenRoomWasActive = new bool[roomRootsToHideDuringIntro.Length];
            }

            for (int i = 0; i < roomRootsToHideDuringIntro.Length; i++)
            {
                GameObject root = roomRootsToHideDuringIntro[i];
                if (root == null)
                {
                    if (captureOriginal)
                    {
                        _hiddenRoomWasActive[i] = false;
                    }

                    continue;
                }

                if (captureOriginal)
                {
                    _hiddenRoomWasActive[i] = root.activeSelf;
                }

                root.SetActive(false);
            }

            _hiddenRoomStatesCached = true;
        }

        private void RestoreRoomRoots()
        {
            RestoreHiddenRoomOriginalStates();
            SetRoomArrayActive(roomRootsToEnableAfterIntro, true);
            KeepVisibleObjectsForIntro();
        }

        private void RestoreHiddenRoomOriginalStates()
        {
            if (!hideRoomDuringIntro || roomRootsToHideDuringIntro == null || _hiddenRoomWasActive == null)
            {
                return;
            }

            int count = Mathf.Min(roomRootsToHideDuringIntro.Length, _hiddenRoomWasActive.Length);
            for (int i = 0; i < count; i++)
            {
                GameObject root = roomRootsToHideDuringIntro[i];
                if (root != null)
                {
                    root.SetActive(_hiddenRoomWasActive[i]);
                }
            }
        }

        private static void SetRoomArrayActive(GameObject[] rooms, bool active)
        {
            if (rooms == null)
            {
                return;
            }

            for (int i = 0; i < rooms.Length; i++)
            {
                GameObject root = rooms[i];
                if (root != null)
                {
                    root.SetActive(active);
                }
            }
        }

        private void KeepVisibleObjectsForIntro()
        {
            if (objectsToKeepVisibleDuringIntro == null)
            {
                return;
            }

            for (int i = 0; i < objectsToKeepVisibleDuringIntro.Length; i++)
            {
                GameObject obj = objectsToKeepVisibleDuringIntro[i];
                if (obj == null)
                {
                    continue;
                }

                Transform t = obj.transform;
                if (t.parent != null && !t.parent.gameObject.activeInHierarchy)
                {
                    t.SetParent(null, true);
                }

                if (!obj.activeSelf)
                {
                    obj.SetActive(true);
                }
            }
        }

        private void CloseOpenPracticeUi()
        {
            ControllerTutorialGate tutorialGate = FindObjectOfType<ControllerTutorialGate>();
            if (tutorialGate != null)
            {
                tutorialGate.HidePracticeObjects();
            }

            TutorialOverlayController overlay = FindObjectOfType<TutorialOverlayController>();
            if (overlay != null && TutorialOverlayController.IsTutorialModeActive)
            {
                overlay.Hide();
            }

            LineOfLifeManager lineOfLife = FindObjectOfType<LineOfLifeManager>();
            if (lineOfLife != null && lineOfLife.IsOpen)
            {
                lineOfLife.TogglePanel();
            }

            LineOfLife3DViewController timeline = FindObjectOfType<LineOfLife3DViewController>();
            if (timeline != null)
            {
                timeline.Close();
            }

            AnnotationPanelController annotation = FindObjectOfType<AnnotationPanelController>();
            if (annotation != null && annotation.IsOpen)
            {
                annotation.ForceCloseWithoutSaving();
            }
        }

        private void ApplyExplorationModeForIntro()
        {
            if (placementTester == null)
            {
                return;
            }

            placementTester.SetXRInteractionMode(XRInteractionMode.Exploration);
        }

        private void RestoreInteractionModeAfterIntro()
        {
            if (placementTester == null)
            {
                return;
            }

            if (restoreMarkingModeAfterIntro && placementTester.startInMarkingMode)
            {
                placementTester.SetXRInteractionMode(XRInteractionMode.Marking);
            }
            else
            {
                placementTester.SetXRInteractionMode(XRInteractionMode.Exploration);
            }
        }

        private IEnumerator PlayIntroRoutine()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = false;
            }

            if (initialDelay > 0f)
            {
                float wait = 0f;
                while (wait < initialDelay && !_skipRequested)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (introLines != null)
            {
                for (int i = 0; i < introLines.Length; i++)
                {
                    if (_skipRequested)
                    {
                        break;
                    }

                    string line = introLines[i];
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    if (introText != null)
                    {
                        introText.text = line;
                    }

                    yield return FadeTextAlpha(0f, 1f);
                    if (_skipRequested)
                    {
                        break;
                    }

                    float hold = 0f;
                    while (hold < lineDuration && !_skipRequested)
                    {
                        hold += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (_skipRequested)
                    {
                        break;
                    }

                    yield return FadeTextAlpha(1f, 0f);
                }
            }

            yield return FadeOutIntroMask();
            FinishIntro();
        }

        /// <summary>
        /// Unified outro: IntroText + SkipHint fade together first, then black sphere
        /// fades more slowly while rooms are revealed underneath.
        /// </summary>
        private IEnumerator FadeOutIntroMask()
        {
            float introFrom = introText != null ? introText.color.a : 0f;
            float skipFrom = skipHintText != null ? skipHintText.color.a : 0f;

            // Keep skip hint visible if it was shown; if intro line already faded, still fade skip.
            if (skipFrom <= 0.01f && allowSkip && skipHintText != null)
            {
                skipFrom = 0.75f;
                SetTextAlpha(skipHintText, skipFrom);
            }

            float textDuration = Mathf.Max(0.01f, outroTextFadeDuration);
            float textElapsed = 0f;
            while (textElapsed < textDuration)
            {
                textElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(textElapsed / textDuration);
                SetTextAlpha(introText, Mathf.Lerp(introFrom, 0f, t));
                SetTextAlpha(skipHintText, Mathf.Lerp(skipFrom, 0f, t));
                yield return null;
            }

            SetTextAlpha(introText, 0f);
            SetTextAlpha(skipHintText, 0f);

            RestoreRoomRoots();

            float sphereDuration = Mathf.Max(0.01f, sphereFadeDuration);
            float sphereFrom = GetSphereAlpha();
            float canvasFrom = canvasGroup != null ? canvasGroup.alpha : 1f;
            float sphereElapsed = 0f;
            while (sphereElapsed < sphereDuration)
            {
                sphereElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(sphereElapsed / sphereDuration);
                SetSphereAlpha(Mathf.Lerp(sphereFrom, 0f, t));
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(canvasFrom, 0f, t);
                }

                yield return null;
            }

            SetSphereAlpha(0f);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private void EnsureSphereMaterialInstance()
        {
            if (introBlackSphere == null || _sphereMaterialInstance != null)
            {
                return;
            }

            MeshRenderer renderer = introBlackSphere.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null)
            {
                Debug.LogWarning("[VRIntroGuide] IntroBlackSphere needs a MeshRenderer with IntroBlackUnlit material.");
                return;
            }

            _sphereMaterialInstance = new Material(renderer.sharedMaterial);
            renderer.material = _sphereMaterialInstance;
        }

        private float GetSphereAlpha()
        {
            if (_sphereMaterialInstance == null)
            {
                return 0f;
            }

            return _sphereMaterialInstance.GetColor(ColorPropertyId).a;
        }

        private void SetSphereAlpha(float alpha)
        {
            EnsureSphereMaterialInstance();
            if (_sphereMaterialInstance == null)
            {
                return;
            }

            Color c = _sphereMaterialInstance.GetColor(ColorPropertyId);
            c.a = Mathf.Clamp01(alpha);
            _sphereMaterialInstance.SetColor(ColorPropertyId, c);
        }

        private void AttachToHeadCamera()
        {
            EnsurePlayerCamera();
            if (headCamera == null)
            {
                Debug.LogWarning("[VRIntroGuide] No head camera found; intro stays at current parent.");
                return;
            }

            Transform rootTransform = introRoot != null ? introRoot.transform : transform;
            if (rootTransform.parent != headCamera)
            {
                rootTransform.SetParent(headCamera, false);
            }

            rootTransform.localPosition = headLocalPosition;
            rootTransform.localRotation = Quaternion.identity;
            rootTransform.localScale = headLocalScale;
        }

        private void ConfigureCanvas()
        {
            Canvas canvas = introRoot != null
                ? introRoot.GetComponent<Canvas>()
                : GetComponent<Canvas>();

            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;

            Camera cam = null;
            if (headCamera != null)
            {
                cam = headCamera.GetComponent<Camera>();
            }

            if (cam == null && Camera.main != null)
            {
                cam = Camera.main;
            }

            if (cam != null)
            {
                canvas.worldCamera = cam;
            }
        }

        private void PrepareVisualsForStart()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = false;
            }

            if (blackBackground != null)
            {
                bool usePanel = !useBlackSphereIntro;
                blackBackground.gameObject.SetActive(usePanel);
                if (usePanel)
                {
                    Color c = blackBackground.color;
                    c.a = 1f;
                    blackBackground.color = c;
                    blackBackground.raycastTarget = true;
                }
            }

            if (introText != null)
            {
                introText.text = string.Empty;
                SetTextAlpha(introText, 0f);
                introText.raycastTarget = false;
            }

            if (skipHintText != null)
            {
                if (string.IsNullOrEmpty(skipHintText.text))
                {
                    skipHintText.text = "按 Space 或右手 Trigger 跳過";
                }

                SetTextAlpha(skipHintText, allowSkip ? 0.75f : 0f);
                skipHintText.raycastTarget = false;
            }
        }

        private void EnsurePlayerCamera()
        {
            if (headCamera == null && Camera.main != null)
            {
                headCamera = Camera.main.transform;
            }
        }

        private IEnumerator FadeTextAlpha(float from, float to)
        {
            if (introText == null || fadeDuration <= 0f)
            {
                SetTextAlpha(introText, to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < fadeDuration && !_skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                SetTextAlpha(introText, Mathf.Lerp(from, to, t));
                yield return null;
            }

            SetTextAlpha(introText, _skipRequested ? 0f : to);
        }

        private static void SetTextAlpha(TMP_Text text, float alpha)
        {
            if (text == null)
            {
                return;
            }

            Color c = text.color;
            c.a = alpha;
            text.color = c;
        }

        private bool WasRightTriggerPressedThisFrame()
        {
            _rightHandDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                _rightHandDevices);

            bool isPressed = false;
            for (int i = 0; i < _rightHandDevices.Count; i++)
            {
                XRInputDevice device = _rightHandDevices[i];
                if (!device.isValid)
                {
                    continue;
                }

                bool triggerValue = false;
                if (device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerValue) && triggerValue)
                {
                    isPressed = true;
                    break;
                }
            }

            bool justPressed = isPressed && !_wasTriggerPressed;
            _wasTriggerPressed = isPressed;
            return justPressed;
        }
    }
}
