using System.Collections;
using System.Collections.Generic;
using EchoSpace.Annotation;
using EchoSpace.Core;
using EchoSpace.SwitchView;
using EchoSpace.VoiceInput;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;
using XRInputFeatureUsageBool = UnityEngine.XR.InputFeatureUsage<bool>;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// 3D Line of Life summary: three main nodes with memory cards and a connecting curve.
    /// Attach to LineOfLife3DView root; wire TimelineRoot, nodes, card roots, and prefab in Inspector.
    /// LineOfLife3DView 只是 MemoryAnchorData 的視覺化；快捷鍵只控制 runtime UI 顯示，不保存狀態。
    /// </summary>
    public class LineOfLife3DViewController : MonoBehaviour
    {
        [Header("View")]
        public GameObject viewRoot;
        public Transform timelineRoot;
        public Transform playerCamera;
        public float distanceFromCamera = 1.8f;
        public float verticalOffset = -0.1f;

        [Header("Rotation Offset")]
        public Vector3 rotationOffsetEuler = Vector3.zero;

        [Header("Data")]
        public MemoryAnchorManager anchorManager;
        public TimelineCurveRenderer curveRenderer;

        [Header("Timeline Nodes")]
        public Transform pastNode;
        public Transform transitionNode;
        public Transform presentNode;
        public Transform pastNodeOrb;
        public Transform transitionNodeOrb;
        public Transform presentNodeOrb;
        public Transform pastNodeLabel;
        public Transform transitionNodeLabel;
        public Transform presentNodeLabel;

        [Header("Card Anchors (CardAnchorRoot under each node)")]
        public Transform pastCardRoot;
        public Transform transitionCardRoot;
        public Transform presentCardRoot;

        [Header("Node Prompts")]
        public GameObject pastPrompt;
        public GameObject transitionPrompt;
        public GameObject presentPrompt;
        public bool hidePromptsOnOpen = true;
        public bool toggleSamePrompt = true;

        [Header("Cross-temporal Reflection Prompts")]
        public GameObject pastToTransitionPrompt;
        public GameObject transitionToPresentPrompt;
        public GameObject finalReflectionPrompt;

        [Header("Legacy (unused)")]
        public GameObject crossLinkPrompt;

        [Header("Prompt Buttons")]
        public Button pastButton;
        public Button transitionButton;
        public Button presentButton;
        public Button closeTimelineButton;
        public Button leftCrossLinkButton;
        public Button rightCrossLinkButton;
        public Button finalReflectionButton;

        [Header("Progressive Reflection Unlock")]
        public bool enableProgressiveUnlock = true;
        public bool resetReflectionProgressOnOpen = false;

        private bool hasViewedPastPrompt = false;
        private bool hasViewedTransitionPrompt = false;
        private bool hasViewedPresentPrompt = false;
        private bool hasViewedPastToTransitionPrompt = false;
        private bool hasViewedTransitionToPresentPrompt = false;

        [Header("Switch View")]
        public SwitchViewController switchViewController;
        public Button finalReflectionSwitchButton;
        public bool showFinalReflectionSwitchButton = true;
        public bool hideFinalReflectionSwitchButtonOnOtherPrompts = true;
        public bool hideLineOfLifeWhenOpeningSwitchView = true;
        public bool hideSwitchViewOnLineOfLifeClose = false;
        [Tooltip("Used only when the 3D timeline opens after Switch View. Does not change the original Open() offset.")]
        public float afterSwitchDistanceFromCamera = 1f;
        public float afterSwitchVerticalOffset = -0.5f;
        public Vector3 afterSwitchRotationOffsetEuler = new Vector3(0f, 180f, 0f);

        [Header("Prefabs & Layout")]
        public GameObject memoryCard3DPrefab;
        public MemoryCardDetailPanelController memoryCardDetailPanel;
        public int maxCardsPerRow = 3;
        public float cardSpacingX = 0.38f;
        public float cardSpacingY = 0.42f;

        [Header("Debug Toggle")]
        public bool enableKeyboardToggle = true;
        public KeyCode toggleKey = KeyCode.T;
        public bool refreshOnOpenByHotkey = true;

        [Header("XR Controller Toggle")]
        public bool enableXRControllerToggle = true;
        public bool useRightBButtonFor3DView = true;

        [Header("XR Button Mapping")]
        public XRBoolButtonUsage right3DViewToggleButton = XRBoolButtonUsage.SecondaryButton;

        [Header("XR Button Debug")]
        public bool logRightControllerAllBoolFeatures = false;

        [Header("XR Input Action Toggle")]
        public bool enableInputActionToggleFor3DView = true;
        public InputActionProperty right3DViewToggleAction;
        [Tooltip("Hold right grip this long to open or close the 3D timeline.")]
        public float rightGripHoldSeconds = 0.8f;

        [Header("Generated Transition Flow")]
        [Tooltip("If assigned, uses LineOfLifeManager.useGeneratedTransitionFlow. Otherwise uses the flag below.")]
        public LineOfLifeManager lineOfLifeManager;
        public bool useGeneratedTransitionFlow = true;
        public bool hideTransitionNodeUntilGenerated = true;
        public GameObject transitionGenerationPromptRoot;
        public TMP_InputField transitionGenerationInputField;
        public Button generateTransitionCardButton;
        [Tooltip("Height above the Transition node when the generate panel opens. Rotation faces the player.")]
        public float generatePanelDistanceFromCamera = 1.2f;
        public float generatePanelHeightOffset = 0.45f;
        public bool generatePanelFaceCamera = true;
        public bool generatePanelLockYOnly = true;
        public bool generatePanelFlip180 = true;
        public Transform generatedTransitionCardAnchor;
        public Transform generatedTransitionNode;
        public GameObject generatedTransitionCardPrefab;
        [Tooltip("Local offset under CardAnchorRoot. Matches Past/Present 3D cards (y = -0.35).")]
        public Vector3 generatedTransitionCardLocalOffset = new Vector3(0f, -0.35f, 0f);
        public Color[] generatedPairColors = new Color[]
        {
            new Color(0.95f, 0.72f, 0.35f, 1f),
            new Color(0.45f, 0.75f, 0.95f, 1f),
            new Color(0.75f, 0.55f, 0.90f, 1f)
        };
        public TMP_Text pairSelectionInstructionText;
        public Transform pairSelectionInstructionRoot;
        public Button nextCardButton;
        [Tooltip("Count-up timer under pairSelectionInstructionText. Auto-finds a child named Timer.")]
        public TMP_Text pairInstructionTimerText;
        public float pairInstructionDistanceFromCamera = 1.45f;
        public float pairInstructionHeightOffset = 0.38f;
        [Tooltip("First placement uses TransitionNode as origin. Later pair prompts keep that world pose and only change text.")]
        public bool placePairInstructionAtTransitionNode = true;
        [Tooltip("Local offset from TransitionNode. Applied once on first show.")]
        public Vector3 pairInstructionOffsetFromTransitionNode = new Vector3(0f, 0.45f, 0f);
        [Tooltip("Fallback first placement if TransitionNode is missing.")]
        public bool keepPairInstructionInFrontOfUser = true;
        public bool pairInstructionFaceCamera = true;
        [Tooltip("Draw pair instruction in front of MemoryAnchor overlay meshes (Overlay queue 4000).")]
        public int pairInstructionSortingOrder = 2600;
        public float generatedInstructionAutoHideSeconds = 10f;
        public float nextCardButtonDelaySeconds = 30f;
        public Button confirmPastPresentPairButton;
        [TextArea(3, 8)]
        public string pairInstructionLookText =
            "這邊先讓你看一下剛才放在「過去」和「現在」的卡片。這些卡片裡，有沒有哪一些讓你感覺：「以前的我」和「現在的我」好像有些地方不太一樣了？\n想好後可再按下一步";
        [TextArea(3, 8)]
        public string pairInstructionSelectText =
            "接下來，請回想你一開始提到的那段生活轉變：\n上大學搬離老家\n請在「過去」和「現在」各選一張，讓你覺得和這段生活轉變有關的卡片。只要能讓你想到與這段轉變相關的皆可d(`･∀･)b";
        [TextArea(3, 8)]
        public string transitionGenerationPromptText =
            "你選的這兩張卡是否有讓你覺得從那段生活轉變前到現在，自己有哪些地方有變化？\n可以簡短說說你發現到的變化來生成轉變卡~";

        private MemoryAnchorData selectedPastCard;
        private MemoryAnchorData selectedPresentCard;
        private GeneratedTransitionCardData generatedTransitionCard;
        private readonly List<GeneratedTransitionCardData> _generatedTransitionCards = new List<GeneratedTransitionCardData>();
        private MemoryCard3DUI selectedPastCardUi;
        private MemoryCard3DUI selectedPresentCardUi;
        private GameObject generatedTransitionCardObject;
        private readonly List<GameObject> _generatedTransitionCardObjects = new List<GameObject>();
        private readonly List<MemoryCard3DUI> _spawnedCards = new List<MemoryCard3DUI>();
        private int _pairColorIndex;
        private string pendingVoiceText = string.Empty;
        private VoiceInputController _transitionVoiceInput;
        private bool _pairInstructionVisible;
        private int _pairInstructionStep;
        private bool _pairSelectButtonsEnabled;
        public bool IsPairSelectionEnabled => UseGeneratedFlow && _pairSelectButtonsEnabled;
        private Coroutine _generatedInstructionAutoHideRoutine;
        private Coroutine _nextCardButtonRoutine;
        private float _pairLookElapsedSeconds;
        private bool _pairInstructionPlaced;
        private Vector3 _pairInstructionWorldPosition;
        private Quaternion _pairInstructionWorldRotation;
        private Material _pairInstructionUiNoZTestMaterial;
        private const int PairInstructionRenderQueue = 4001;
        private const string GeneratedTransitionCardInstruction =
            "已生成「我注意到的轉變」卡。可以繼續按下一步從轉變前/現在各選一張來生成轉變卡，\n或是直接按下方切換視角按鈕，進入下一階段";

        private float _right3DViewHoldTimer;
        private bool _right3DViewHoldTriggered;
        private readonly Dictionary<string, bool> _rightBoolFeaturePreviousStates = new Dictionary<string, bool>();
        private bool _warnedNoRightController;
        private bool _warnedNoRightBoolFeatures;
        private bool _warnedSteamVRBButtonMapping;
        private bool _loggedRightBoolFeatureCatalog;

        private void Awake()
        {
            if (lineOfLifeManager == null)
            {
                lineOfLifeManager = FindObjectOfType<LineOfLifeManager>();
            }

            ApplyTimelineNodeLayout();
            ResolvePromptReferences();
            RegisterPromptButtons();
            RegisterGeneratedTransitionUi();

            if (viewRoot != null)
            {
                viewRoot.SetActive(false);
            }

            if (transitionGenerationPromptRoot != null)
            {
                transitionGenerationPromptRoot.SetActive(false);
            }

            HidePairSelectionInstruction();

            if (finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (right3DViewToggleAction.action != null)
            {
                right3DViewToggleAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (right3DViewToggleAction.action != null)
            {
                right3DViewToggleAction.action.Disable();
            }
        }

        private void OnDestroy()
        {
            StopGeneratedInstructionAutoHide();
            StopNextCardButtonRoutine();
            UnbindNextCardButton();
            UnbindCloseTimelineButton();
            UnbindTransitionVoiceInput();
        }

        private void LateUpdate()
        {
            if (!_pairInstructionVisible)
            {
                return;
            }

            if (viewRoot != null && !viewRoot.activeInHierarchy)
            {
                return;
            }

            if (_pairInstructionPlaced)
            {
                ApplyCachedPairInstructionPose();
            }
        }

        private void Update()
        {
            DebugRightControllerBoolFeatures();

            bool tutorialMode = EchoSpace.UI.ControllerTutorialGate.IsBlockingGameplay;
            if (tutorialMode)
            {
                return;
            }

            if (enableKeyboardToggle && Input.GetKeyDown(toggleKey))
            {
                ToggleViewByHotkey();
            }

            UpdateRightGripHoldToggle();
        }

        private void UpdateRightGripHoldToggle()
        {
            if (!enableInputActionToggleFor3DView && !(enableXRControllerToggle && useRightBButtonFor3DView))
            {
                return;
            }

            if (IsRight3DViewHoldPressed())
            {
                _right3DViewHoldTimer += Time.deltaTime;
                float holdSeconds = rightGripHoldSeconds > 0f ? rightGripHoldSeconds : 0.8f;
                if (_right3DViewHoldTimer >= holdSeconds && !_right3DViewHoldTriggered)
                {
                    Debug.Log("[LineOfLife3DView] Right grip hold toggle 3D view.");
                    ToggleViewByHotkey();
                    _right3DViewHoldTriggered = true;
                }
            }
            else
            {
                _right3DViewHoldTimer = 0f;
                _right3DViewHoldTriggered = false;
            }
        }

        private bool IsRight3DViewHoldPressed()
        {
            bool pressed = false;
            if (enableInputActionToggleFor3DView)
            {
                pressed = IsRight3DViewInputActionPressed();
            }

            if (!pressed && enableXRControllerToggle && useRightBButtonFor3DView)
            {
                pressed = GetControllerButton(XRInputDeviceCharacteristics.Right, right3DViewToggleButton);
            }

            return pressed;
        }

        private bool IsRight3DViewInputActionPressed()
        {
            InputAction action = right3DViewToggleAction.action;
            if (action == null)
            {
                return false;
            }

            return action.ReadValue<float>() > 0.5f;
        }

        private bool GetControllerButton(
            XRInputDeviceCharacteristics hand,
            XRBoolButtonUsage buttonUsage)
        {
            var devices = new List<XRInputDevice>();
            XRInputDevices.GetDevicesWithCharacteristics(
                XRInputDeviceCharacteristics.Controller | hand,
                devices);

            XRInputFeatureUsageBool usage = GetBoolUsage(buttonUsage);

            foreach (XRInputDevice device in devices)
            {
                if (device.TryGetFeatureValue(usage, out bool pressed) && pressed)
                {
                    return true;
                }
            }

            return false;
        }

        private static XRInputFeatureUsageBool GetBoolUsage(XRBoolButtonUsage buttonUsage)
        {
            switch (buttonUsage)
            {
                case XRBoolButtonUsage.PrimaryButton:
                    return XRCommonUsages.primaryButton;
                case XRBoolButtonUsage.SecondaryButton:
                    return XRCommonUsages.secondaryButton;
                case XRBoolButtonUsage.GripButton:
                    return XRCommonUsages.gripButton;
                case XRBoolButtonUsage.TriggerButton:
                    return XRCommonUsages.triggerButton;
                case XRBoolButtonUsage.MenuButton:
                    return XRCommonUsages.menuButton;
                case XRBoolButtonUsage.PrimaryTouch:
                    return XRCommonUsages.primaryTouch;
                case XRBoolButtonUsage.SecondaryTouch:
                    return XRCommonUsages.secondaryTouch;
                case XRBoolButtonUsage.Primary2DAxisClick:
                    return XRCommonUsages.primary2DAxisClick;
                case XRBoolButtonUsage.Secondary2DAxisClick:
                    return XRCommonUsages.secondary2DAxisClick;
                case XRBoolButtonUsage.Primary2DAxisTouch:
                    return XRCommonUsages.primary2DAxisTouch;
                case XRBoolButtonUsage.Secondary2DAxisTouch:
                    return XRCommonUsages.secondary2DAxisTouch;
                default:
                    return XRCommonUsages.primaryButton;
            }
        }

        private void DebugRightControllerBoolFeatures()
        {
            if (!logRightControllerAllBoolFeatures)
            {
                return;
            }

            if (!_warnedSteamVRBButtonMapping)
            {
                _warnedSteamVRBButtonMapping = true;
                Debug.LogWarning(
                    "[LineOfLife3DView] Right controller bool feature debug is on. Press Right B and look for "
                    + "'[XRButtonDebug] Right bool feature pressed' logs. "
                    + "If B produces no log, SteamVR may not expose it as a UnityEngine.XR bool feature; "
                    + "a future fix may require Input Action Reference binding via XRI RightHand.");
            }

            var devices = new List<XRInputDevice>();
            XRInputDevices.GetDevicesWithCharacteristics(
                XRInputDeviceCharacteristics.Controller | XRInputDeviceCharacteristics.Right,
                devices);

            if (devices.Count == 0)
            {
                if (!_warnedNoRightController)
                {
                    _warnedNoRightController = true;
                    Debug.LogWarning("[LineOfLife3DView] No right XR controller device found for bool feature debug.");
                }

                return;
            }

            bool foundAnyBoolFeature = false;
            var catalogNames = !_loggedRightBoolFeatureCatalog ? new List<string>() : null;

            foreach (XRInputDevice device in devices)
            {
                var usages = new List<InputFeatureUsage>();
                if (!device.TryGetFeatureUsages(usages))
                {
                    continue;
                }

                foreach (InputFeatureUsage usage in usages)
                {
                    if (usage.type != typeof(bool))
                    {
                        continue;
                    }

                    foundAnyBoolFeature = true;
                    catalogNames?.Add(usage.name);

                    XRInputFeatureUsageBool boolUsage = new XRInputFeatureUsageBool(usage.name);

                    if (!device.TryGetFeatureValue(boolUsage, out bool pressed))
                    {
                        continue;
                    }

                    string key = device.name + "/" + usage.name;

                    bool wasPressed = _rightBoolFeaturePreviousStates.ContainsKey(key)
                        ? _rightBoolFeaturePreviousStates[key]
                        : false;

                    if (pressed && !wasPressed)
                    {
                        Debug.Log("[XRButtonDebug] Right bool feature pressed: " + usage.name + " on device: " + device.name);
                    }

                    _rightBoolFeaturePreviousStates[key] = pressed;
                }
            }

            if (!_loggedRightBoolFeatureCatalog && catalogNames != null && catalogNames.Count > 0)
            {
                _loggedRightBoolFeatureCatalog = true;
                Debug.Log("[XRButtonDebug] Right controller bool features available: " + string.Join(", ", catalogNames));
            }

            if (!_warnedNoRightBoolFeatures && !foundAnyBoolFeature)
            {
                _warnedNoRightBoolFeatures = true;
                Debug.LogWarning(
                    "[LineOfLife3DView] Right controller exposes no bool XR features via TryGetFeatureUsages. "
                    + "SteamVR may not map physical buttons (e.g. B) to UnityEngine.XR bool features. "
                    + "A future fix may require Input Action Reference binding via XRI RightHand.");
            }
        }

        private void ResolvePromptReferences()
        {
            if (pastButton == null)
            {
                pastButton = FindNodeButton(pastNode);
            }

            if (transitionButton == null)
            {
                transitionButton = FindNodeButton(transitionNode);
            }

            if (presentButton == null)
            {
                presentButton = FindNodeButton(presentNode);
            }

            if (pastPrompt == null)
            {
                pastPrompt = FindNodePrompt(pastNode);
            }

            if (transitionPrompt == null)
            {
                transitionPrompt = FindNodePrompt(transitionNode);
            }

            if (presentPrompt == null)
            {
                presentPrompt = FindNodePrompt(presentNode);
            }
        }

        private static Button FindNodeButton(Transform node)
        {
            if (node == null)
            {
                return null;
            }

            Transform buttonTransform = node.Find("NodeButtonCanvas/NodeButton");
            if (buttonTransform != null)
            {
                Button button = buttonTransform.GetComponent<Button>();
                if (button != null)
                {
                    return button;
                }
            }

            buttonTransform = node.Find("NodeButton");
            if (buttonTransform != null)
            {
                Button button = buttonTransform.GetComponent<Button>();
                if (button != null)
                {
                    return button;
                }
            }

            return node.GetComponentInChildren<Button>(true);
        }

        private static GameObject FindNodePrompt(Transform node)
        {
            if (node == null)
            {
                return null;
            }

            Transform promptTransform = node.Find("Prompt");
            return promptTransform != null ? promptTransform.gameObject : null;
        }

        private void RegisterPromptButtons()
        {
            if (pastButton != null)
            {
                pastButton.onClick.RemoveListener(ShowPastPrompt);
                pastButton.onClick.AddListener(ShowPastPrompt);
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] pastButton is not assigned.");
            }

            if (transitionButton != null)
            {
                transitionButton.onClick.RemoveListener(ShowTransitionPrompt);
                transitionButton.onClick.AddListener(ShowTransitionPrompt);
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] transitionButton is not assigned.");
            }

            if (presentButton != null)
            {
                presentButton.onClick.RemoveListener(ShowPresentPrompt);
                presentButton.onClick.AddListener(ShowPresentPrompt);
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] presentButton is not assigned.");
            }

            RegisterCloseTimelineButton();

            if (leftCrossLinkButton != null)
            {
                leftCrossLinkButton.onClick.RemoveListener(ShowPastToTransitionPrompt);
                leftCrossLinkButton.onClick.AddListener(ShowPastToTransitionPrompt);
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] leftCrossLinkButton is not assigned.");
            }

            if (rightCrossLinkButton != null)
            {
                rightCrossLinkButton.onClick.RemoveListener(ShowTransitionToPresentPrompt);
                rightCrossLinkButton.onClick.AddListener(ShowTransitionToPresentPrompt);
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] rightCrossLinkButton is not assigned.");
            }

            if (finalReflectionButton != null)
            {
                finalReflectionButton.onClick.RemoveListener(ShowFinalReflectionPrompt);
                finalReflectionButton.onClick.AddListener(ShowFinalReflectionPrompt);
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] finalReflectionButton is not assigned.");
            }

            if (finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.onClick.RemoveListener(ShowSwitchViewPromptFromFinalReflection);
                finalReflectionSwitchButton.onClick.AddListener(ShowSwitchViewPromptFromFinalReflection);
            }
        }

        private void RegisterCloseTimelineButton()
        {
            ResolveCloseTimelineButton();
            if (closeTimelineButton == null)
            {
                Debug.LogWarning(
                    "[LineOfLife3DView] CloseButton was not found under PresentNode / NodeButtonCanvas.");
                return;
            }

            closeTimelineButton.enabled = true;
            closeTimelineButton.interactable = true;
            Navigation navigation = closeTimelineButton.navigation;
            navigation.mode = Navigation.Mode.None;
            closeTimelineButton.navigation = navigation;
            EnsureButtonRaycastable(closeTimelineButton);
            closeTimelineButton.onClick.RemoveListener(Close);
            closeTimelineButton.onClick.AddListener(Close);
            Debug.Log("[LineOfLife3DView] CloseButton bound on " + closeTimelineButton.name);
        }

        private void ResolveCloseTimelineButton()
        {
            if (closeTimelineButton != null)
            {
                return;
            }

            if (presentNode == null)
            {
                return;
            }

            Transform found = presentNode.Find("NodeButtonCanvas/CloseButton");
            if (found == null)
            {
                found = presentNode.Find("CloseButton");
            }

            if (found == null)
            {
                return;
            }

            closeTimelineButton = found.GetComponent<Button>();
            if (closeTimelineButton == null)
            {
                closeTimelineButton = found.GetComponentInChildren<Button>(true);
            }
        }

        private void UnbindCloseTimelineButton()
        {
            if (closeTimelineButton != null)
            {
                closeTimelineButton.onClick.RemoveListener(Close);
            }
        }

        private void RegisterGeneratedTransitionUi()
        {
            ResolveGeneratedTransitionButton();
            BindTransitionVoiceInput();
            EnsureGeneratedTransitionUiRaycast();

            if (generateTransitionCardButton != null)
            {
                generateTransitionCardButton.interactable = true;
                generateTransitionCardButton.onClick.RemoveListener(OnGenerateTransitionCardClicked);
                generateTransitionCardButton.onClick.AddListener(OnGenerateTransitionCardClicked);
                EnsureButtonRaycastable(generateTransitionCardButton);
                Debug.Log("[GeneratedTransitionFlow] Generate button bound: " + generateTransitionCardButton.name);
            }
            else
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] generateTransitionCardButton is not assigned. " +
                    "Drag the 生成轉變卡 Button onto LineOfLife3DViewController, or name it GenerateTransitionCardButton.");
            }

            if (confirmPastPresentPairButton != null)
            {
                confirmPastPresentPairButton.onClick.RemoveListener(ShowTransitionGenerationPrompt);
                confirmPastPresentPairButton.onClick.AddListener(ShowTransitionGenerationPrompt);
            }

            RegisterNextCardButton();
        }

        private void ResolveGeneratedTransitionButton()
        {
            if (generateTransitionCardButton != null || transitionGenerationPromptRoot == null)
            {
                return;
            }

            Button[] buttons = transitionGenerationPromptRoot.GetComponentsInChildren<Button>(true);
            Button fallback = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                if (button.GetComponent<VoiceInputController>() != null
                    || button.GetComponentInParent<VoiceInputController>() != null
                        && button.name.IndexOf("Mic", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string name = button.name;
                if (name.IndexOf("Generate", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("轉變", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Transition", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    generateTransitionCardButton = button;
                    Debug.Log("[GeneratedTransitionFlow] Auto-found generate button: " + button.name);
                    return;
                }

                if (fallback == null)
                {
                    fallback = button;
                }
            }

            if (generateTransitionCardButton == null && fallback != null && buttons.Length <= 2)
            {
                generateTransitionCardButton = fallback;
                Debug.Log("[GeneratedTransitionFlow] Auto-bound fallback generate button: " + fallback.name);
            }
        }

        private void BindTransitionVoiceInput()
        {
            VoiceInputController voice = null;
            if (transitionGenerationPromptRoot != null)
            {
                voice = transitionGenerationPromptRoot.GetComponent<VoiceInputController>();
                if (voice == null)
                {
                    voice = transitionGenerationPromptRoot.GetComponentInChildren<VoiceInputController>(true);
                }

                if (voice == null)
                {
                    voice = transitionGenerationPromptRoot.GetComponentInParent<VoiceInputController>(true);
                }
            }

            if (voice == _transitionVoiceInput)
            {
                if (voice != null)
                {
                    voice.SetSkipShowingTranscript(true);
                }

                return;
            }

            UnbindTransitionVoiceInput();
            _transitionVoiceInput = voice;
            if (_transitionVoiceInput == null)
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] No VoiceInputController found under transitionGenerationPromptRoot. " +
                    "Mic clicks will not be handled until you add the same VoiceInput setup as AnnotationPanel.");
                return;
            }

            _transitionVoiceInput.SetSkipShowingTranscript(true);
            _transitionVoiceInput.TranscriptReceived -= NotifyTranscriptApplied;
            _transitionVoiceInput.TranscriptReceived += NotifyTranscriptApplied;
            Debug.Log("[GeneratedTransitionFlow] Bound VoiceInputController on " + _transitionVoiceInput.name);
        }

        private void UnbindTransitionVoiceInput()
        {
            if (_transitionVoiceInput != null)
            {
                _transitionVoiceInput.TranscriptReceived -= NotifyTranscriptApplied;
                _transitionVoiceInput = null;
            }
        }

        private void EnsureGeneratedTransitionUiRaycast()
        {
            EnsureWorldUiRaycast(transitionGenerationPromptRoot);
        }

        private void EnsureWorldUiRaycast(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                canvas = root.GetComponentInChildren<Canvas>(true);
            }

            if (canvas == null)
            {
                Debug.LogWarning("[LineOfLife3DView] No Canvas on " + root.name + "; XR rays cannot hit buttons.");
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            if (canvas.sortingOrder < 1200)
            {
                canvas.sortingOrder = 1200;
            }

            Camera cam = ResolvePlayerCameraComponent();
            if (cam != null)
            {
                canvas.worldCamera = cam;
            }

            GraphicRaycaster graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
            if (graphicRaycaster == null)
            {
                graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            graphicRaycaster.ignoreReversedGraphics = false;
            graphicRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            TrackedDeviceGraphicRaycaster trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (trackedRaycaster == null)
            {
                trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            }

            trackedRaycaster.ignoreReversedGraphics = false;
            trackedRaycaster.checkFor3DOcclusion = false;

            CanvasGroup[] groups = canvas.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i].interactable = true;
                groups[i].blocksRaycasts = true;
                if (groups[i].alpha <= 0f)
                {
                    groups[i].alpha = 1f;
                }
            }

            TMP_Text[] texts = canvas.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].GetComponentInParent<Button>() == null
                    && texts[i].GetComponentInParent<TMP_InputField>() == null)
                {
                    texts[i].raycastTarget = false;
                }
            }

            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                EnsureButtonRaycastable(buttons[i]);
            }
        }

        private void EnsureWorldTextVisible(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            TMP_Text text = root.GetComponent<TMP_Text>();
            if (text != null)
            {
                text.enabled = true;
                text.alpha = 1f;
                text.raycastTarget = false;

                Shader overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay");
                Material fontMaterial = text.fontMaterial;
                if (fontMaterial != null)
                {
                    if (overlayShader != null && fontMaterial.shader != overlayShader)
                    {
                        fontMaterial.shader = overlayShader;
                    }

                    if (fontMaterial.HasProperty("_CullMode"))
                    {
                        fontMaterial.SetFloat("_CullMode", 0f);
                    }

                    fontMaterial.renderQueue = 4000;
                }

                text.ForceMeshUpdate();
            }

            Renderer renderer = root.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
                if (renderer.sortingOrder < 1300)
                {
                    renderer.sortingOrder = 1300;
                }
            }
        }

        private static void EnsureButtonRaycastable(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.interactable = true;
            Graphic graphic = button.targetGraphic;
            if (graphic == null)
            {
                graphic = button.GetComponent<Image>();
            }

            if (graphic == null)
            {
                Image image = button.gameObject.GetComponent<Image>();
                if (image == null)
                {
                    image = button.gameObject.AddComponent<Image>();
                    image.color = new Color(1f, 1f, 1f, 0.01f);
                }

                graphic = image;
                button.targetGraphic = image;
            }

            graphic.raycastTarget = true;

            TMP_Text[] texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i].raycastTarget = false;
            }
        }

        private bool UseGeneratedFlow
        {
            get
            {
                if (lineOfLifeManager != null)
                {
                    return lineOfLifeManager.useGeneratedTransitionFlow;
                }

                return useGeneratedTransitionFlow;
            }
        }

        private bool HasGeneratedTransitionCards =>
            generatedTransitionCard != null || _generatedTransitionCards.Count > 0;

        public void ShowSwitchViewPromptFromFinalReflection()
        {
            if (switchViewController == null)
            {
                Debug.LogWarning("[LineOfLife3DView] switchViewController is not assigned.");
                return;
            }

            switchViewController.SwitchToTargetView();
            HidePairSelectionInstruction();

            if (hideLineOfLifeWhenOpeningSwitchView && viewRoot != null)
            {
                viewRoot.SetActive(false);
                Debug.Log("[LineOfLife3DView] Hide LineOfLife3DView after switching view.");
            }

            Debug.Log("[LineOfLife3DView] Switch view from TransitionNode SwitchButton, skip SwitchViewRoot.");
        }

        private void HideFinalReflectionSwitchButtonIfNeeded()
        {
            if (hideFinalReflectionSwitchButtonOnOtherPrompts && finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.gameObject.SetActive(false);
            }
        }

        private void ResetReflectionProgress()
        {
            hasViewedPastPrompt = false;
            hasViewedTransitionPrompt = false;
            hasViewedPresentPrompt = false;
            hasViewedPastToTransitionPrompt = false;
            hasViewedTransitionToPresentPrompt = false;
            UpdateProgressiveButtons();
        }

        private void UpdateProgressiveButtons()
        {
            if (UseGeneratedFlow)
            {
                bool hasGenerated = HasGeneratedTransitionCards;

                SetButtonVisible(transitionButton, hasGenerated);
                SetButtonVisible(leftCrossLinkButton, false);
                SetButtonVisible(rightCrossLinkButton, false);
                SetButtonVisible(finalReflectionButton, hasGenerated);
                SetAllButtonCanvasVisible(hasGenerated);
                Debug.Log(
                    "[LineOfLife3DView] Generated-flow progress: Past=" + hasViewedPastPrompt
                    + ", Present=" + hasViewedPresentPrompt
                    + ", generated=" + hasGenerated
                    + ", switchViewButton=" + hasGenerated);
                return;
            }

            if (!enableProgressiveUnlock)
            {
                SetButtonVisible(leftCrossLinkButton, true);
                SetButtonVisible(rightCrossLinkButton, true);
                SetButtonVisible(finalReflectionButton, true);
                return;
            }

            bool allNodePromptsViewed =
                hasViewedPastPrompt &&
                hasViewedTransitionPrompt &&
                hasViewedPresentPrompt;

            bool allLegacyCrossLinksViewed =
                hasViewedPastToTransitionPrompt &&
                hasViewedTransitionToPresentPrompt;

            SetButtonVisible(leftCrossLinkButton, allNodePromptsViewed);
            SetButtonVisible(rightCrossLinkButton, allNodePromptsViewed);
            SetButtonVisible(finalReflectionButton, allLegacyCrossLinksViewed);

            Debug.Log(
                "[LineOfLife3DView] Progress: Past=" + hasViewedPastPrompt
                + ", Transition=" + hasViewedTransitionPrompt
                + ", Present=" + hasViewedPresentPrompt
                + ", P2T=" + hasViewedPastToTransitionPrompt
                + ", T2P=" + hasViewedTransitionToPresentPrompt);
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private void SetAllButtonCanvasVisible(bool visible)
        {
            if (finalReflectionButton == null)
            {
                return;
            }

            Transform canvas = finalReflectionButton.transform.parent;
            if (canvas != null)
            {
                canvas.gameObject.SetActive(visible);
            }
        }

        public void Open()
        {
            if (viewRoot != null)
            {
                viewRoot.SetActive(true);
            }

            ResolvePromptReferences();
            RegisterPromptButtons();

            ApplyTimelineNodeLayout();
            PositionTimelineInFrontOfPlayer();
            ResetPairInstructionPose();
            ClearCardRoots();

            if (anchorManager == null)
            {
                Debug.LogWarning("[LineOfLife3DView] anchorManager is not assigned.");
            }
            else
            {
            List<MemoryAnchorData> allData = anchorManager.GetAnnotatedAnchorData();
            if (allData == null)
            {
                allData = new List<MemoryAnchorData>();
            }

            var past = new List<MemoryAnchorData>();
            var transition = new List<MemoryAnchorData>();
            var present = new List<MemoryAnchorData>();

            foreach (MemoryAnchorData data in allData)
            {
                if (data == null)
                {
                    continue;
                }

                switch (data.timeCategory)
                {
                    case TimeCategory.Past:
                        past.Add(data);
                        break;
                    case TimeCategory.Transition:
                        transition.Add(data);
                        break;
                    case TimeCategory.Present:
                        present.Add(data);
                        break;
                    case TimeCategory.Unclassified:
                        Debug.LogWarning(
                            "[LineOfLife3DView] Anchor id=" + data.id
                            + " is Unclassified and will not appear in the 3D summary.");
                        break;
                    default:
                        Debug.LogWarning(
                            "[LineOfLife3DView] Unknown timeCategory for anchor id=" + data.id);
                        break;
                }
            }

            PopulateCardRegion("Past", past, pastCardRoot);
            if (UseGeneratedFlow)
            {
                Debug.Log("[GeneratedTransitionFlow] Enabled");
                ApplyGeneratedTransitionNodeVisibility();
                Debug.Log("[GeneratedTransitionFlow] 3D timeline shows Past / Present only until a transition card is generated.");
            }
            else
            {
                if (transitionNode != null)
                {
                    transitionNode.gameObject.SetActive(true);
                }

            PopulateCardRegion("Transition", transition, transitionCardRoot);
            }

            PopulateCardRegion("Present", present, presentCardRoot);
            }

            if (curveRenderer != null)
            {
                curveRenderer.RenderCurve();
                Debug.Log("[LineOfLife3DView] Called curveRenderer.RenderCurve().");
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] curveRenderer is null.");
            }

            if (hidePromptsOnOpen)
            {
                HideAllPrompts();
            }

            if (resetReflectionProgressOnOpen)
            {
                ResetReflectionProgress();
            }
            else
            {
                UpdateProgressiveButtons();
            }

            if (UseGeneratedFlow)
            {
                RestoreGeneratedTransitionProgress();
                RegisterGeneratedTransitionUi();
                RestoreGeneratedFlowUi();
            }

            Debug.Log("[LineOfLife3DView] Opened 3D timeline summary.");
        }

        public bool IsVisible => viewRoot != null && viewRoot.activeSelf;
        public event System.Action Closed;

        public void OpenAfterSwitchView()
        {
            Open();
            PositionTimelineInFrontOfPlayer(
                afterSwitchDistanceFromCamera,
                afterSwitchVerticalOffset,
                afterSwitchRotationOffsetEuler);
            HidePairSelectionInstruction();
            if (transitionGenerationPromptRoot != null)
            {
                transitionGenerationPromptRoot.SetActive(false);
            }

            HideAllPrompts();
            SetAllButtonCanvasVisible(false);
            if (finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.gameObject.SetActive(false);
            }

            Debug.Log(
                "[LineOfLife3DView] Opened 3D timeline after switch view. distance="
                + afterSwitchDistanceFromCamera
                + " vertical=" + afterSwitchVerticalOffset
                + " rotation=" + afterSwitchRotationOffsetEuler);
        }

        public void ShowPastPrompt()
        {
            if (toggleSamePrompt && pastPrompt != null && pastPrompt.activeSelf)
            {
                HideAllPrompts();
                return;
            }

            HideAllPrompts();

            if (pastPrompt != null)
            {
                pastPrompt.SetActive(true);
                hasViewedPastPrompt = true;
                UpdateProgressiveButtons();
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] pastPrompt is not assigned.");
            }

            Debug.Log("[LineOfLife3DView] Show Past Prompt");
            HideFinalReflectionSwitchButtonIfNeeded();
        }

        public void ShowTransitionPrompt()
        {
            if (UseGeneratedFlow && !HasGeneratedTransitionCards)
            {
                Debug.Log("[GeneratedTransitionFlow] Transition node prompt locked until a transition card is generated.");
                return;
            }

            if (toggleSamePrompt && transitionPrompt != null && transitionPrompt.activeSelf)
            {
                HideAllPrompts();
                return;
            }

            HideAllPrompts();

            if (transitionPrompt != null)
            {
                transitionPrompt.SetActive(true);
                hasViewedTransitionPrompt = true;
                UpdateProgressiveButtons();
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] transitionPrompt is not assigned.");
            }

            Debug.Log("[LineOfLife3DView] Show Transition Prompt");
            HideFinalReflectionSwitchButtonIfNeeded();
        }

        public void ShowPresentPrompt()
        {
            if (toggleSamePrompt && presentPrompt != null && presentPrompt.activeSelf)
            {
                HideAllPrompts();
                return;
            }

            HideAllPrompts();

            if (presentPrompt != null)
            {
                presentPrompt.SetActive(true);
                hasViewedPresentPrompt = true;
                UpdateProgressiveButtons();
            }
            else
            {
                Debug.LogWarning("[LineOfLife3DView] presentPrompt is not assigned.");
            }

            Debug.Log("[LineOfLife3DView] Show Present Prompt");
            HideFinalReflectionSwitchButtonIfNeeded();
        }

        public void ShowPastToTransitionPrompt()
        {
            if (UseGeneratedFlow && !HasGeneratedTransitionCards)
            {
                Debug.Log("[GeneratedTransitionFlow] Cross-link locked until a transition card is generated.");
                return;
            }
            if (toggleSamePrompt && pastToTransitionPrompt != null && pastToTransitionPrompt.activeSelf)
            {
                HideAllPrompts();
                return;
            }

            HideAllPrompts();

            if (pastToTransitionPrompt != null)
            {
                pastToTransitionPrompt.SetActive(true);
                hasViewedPastToTransitionPrompt = true;
                UpdateProgressiveButtons();
            }

            Debug.Log("[LineOfLife3DView] Show Past-To-Transition Prompt");
            HideFinalReflectionSwitchButtonIfNeeded();
        }

        public void ShowTransitionToPresentPrompt()
        {
            if (UseGeneratedFlow && !HasGeneratedTransitionCards)
            {
                Debug.Log("[GeneratedTransitionFlow] Cross-link locked until a transition card is generated.");
                return;
            }
            if (toggleSamePrompt && transitionToPresentPrompt != null && transitionToPresentPrompt.activeSelf)
            {
                HideAllPrompts();
                return;
            }

            HideAllPrompts();

            if (transitionToPresentPrompt != null)
            {
                transitionToPresentPrompt.SetActive(true);
                hasViewedTransitionToPresentPrompt = true;
                UpdateProgressiveButtons();
            }

            Debug.Log("[LineOfLife3DView] Show Transition-To-Present Prompt");
            HideFinalReflectionSwitchButtonIfNeeded();
        }

        public void ShowFinalReflectionPrompt()
        {
            HidePairSelectionInstruction();

            if (toggleSamePrompt && finalReflectionPrompt != null && finalReflectionPrompt.activeSelf)
            {
                HideAllPrompts();
                return;
            }

            HideAllPrompts();

            if (finalReflectionPrompt != null)
            {
                finalReflectionPrompt.SetActive(true);
                EnsureWorldUiRaycast(finalReflectionPrompt);
                EnsureWorldTextVisible(finalReflectionPrompt);
            }

            if (showFinalReflectionSwitchButton && finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.gameObject.SetActive(true);
                EnsureButtonRaycastable(finalReflectionSwitchButton);
            }

            Debug.Log("[LineOfLife3DView] Show Final Reflection Prompt");
        }

        public void HideAllPrompts()
        {
            if (pastPrompt != null)
            {
                pastPrompt.SetActive(false);
            }

            if (transitionPrompt != null)
            {
                transitionPrompt.SetActive(false);
            }

            if (presentPrompt != null)
            {
                presentPrompt.SetActive(false);
            }

            if (pastToTransitionPrompt != null)
            {
                pastToTransitionPrompt.SetActive(false);
            }

            if (transitionToPresentPrompt != null)
            {
                transitionToPresentPrompt.SetActive(false);
            }

            if (finalReflectionPrompt != null)
            {
                finalReflectionPrompt.SetActive(false);
            }

            if (crossLinkPrompt != null)
            {
                crossLinkPrompt.SetActive(false);
            }

            if (transitionGenerationPromptRoot != null)
            {
                transitionGenerationPromptRoot.SetActive(false);
            }

            if (finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.gameObject.SetActive(false);
            }

            Debug.Log("[LineOfLife3DView] Hide All Prompts");
        }

        public void ToggleViewByHotkey()
        {
            if (viewRoot != null && viewRoot.activeSelf)
            {
                Close();
                Debug.Log("[LineOfLife3DView] Hotkey close 3D view.");
                return;
            }

            if (refreshOnOpenByHotkey)
            {
                Open();
            }
            else if (viewRoot != null)
            {
                viewRoot.SetActive(true);
            }

            Debug.Log("[LineOfLife3DView] Hotkey open 3D view.");
        }

        public void Close()
        {
            HideAllPrompts();
            HidePairSelectionInstruction();

            if (viewRoot != null)
            {
                viewRoot.SetActive(false);
            }

            if (hideSwitchViewOnLineOfLifeClose && switchViewController != null)
            {
                switchViewController.HideSwitchViewPrompt();
            }

            Debug.Log("[LineOfLife3DView] Closed.");
            if (Closed != null)
            {
                Closed();
            }
        }

        private void PositionTimelineInFrontOfPlayer()
        {
            PositionTimelineInFrontOfPlayer(distanceFromCamera, verticalOffset, rotationOffsetEuler);
        }

        private void PositionTimelineInFrontOfPlayer(float distance, float height, Vector3 rotationEuler)
        {
            if (timelineRoot == null || playerCamera == null)
            {
                Debug.LogWarning("[LineOfLife3DView] timelineRoot or playerCamera is not assigned.");
                return;
            }

            timelineRoot.position = playerCamera.position + playerCamera.forward * distance;
            timelineRoot.position += Vector3.up * height;

            Vector3 toCamera = playerCamera.position - timelineRoot.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.0001f)
            {
                timelineRoot.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }

            timelineRoot.rotation *= Quaternion.Euler(rotationEuler);
            Debug.Log("[LineOfLife3DView] Applied timeline offset: distance=" + distance
                + " vertical=" + height
                + " rotation=" + rotationEuler);
        }

        private void ApplyTimelineNodeLayout()
        {
            SetNodeLocalPosition(pastNode, new Vector3(-1.2f, 0f, 0f), "PastNode");
            SetNodeLocalPosition(transitionNode, new Vector3(0f, 0f, -0.55f), "TransitionNode");
            SetNodeLocalPosition(presentNode, new Vector3(1.2f, 0f, 0f), "PresentNode");

            SetOptionalLocalPosition(pastNodeOrb, Vector3.zero);
            SetOptionalLocalPosition(transitionNodeOrb, Vector3.zero);
            SetOptionalLocalPosition(presentNodeOrb, Vector3.zero);

            SetOptionalLocalPosition(pastNodeLabel, new Vector3(0f, 0.1f, 0f));
            SetOptionalLocalPosition(transitionNodeLabel, new Vector3(0f, 0.1f, 0f));
            SetOptionalLocalPosition(presentNodeLabel, new Vector3(0f, 0.1f, 0f));

            SetOptionalLocalPosition(pastCardRoot, new Vector3(0f, -0.1f, 0f));
            SetOptionalLocalPosition(transitionCardRoot, new Vector3(0f, -0.1f, 0f));
            SetOptionalLocalPosition(presentCardRoot, new Vector3(0f, -0.1f, 0f));
        }

        private static void SetNodeLocalPosition(Transform node, Vector3 localPosition, string nodeLabel)
        {
            if (node == null)
            {
                Debug.LogWarning("[LineOfLife3DView] " + nodeLabel + " is not assigned.");
                return;
            }

            node.localPosition = localPosition;
        }

        private static void SetOptionalLocalPosition(Transform target, Vector3 localPosition)
        {
            if (target == null)
            {
                return;
            }

            target.localPosition = localPosition;
        }

        private void ClearCardRoots()
        {
            _spawnedCards.Clear();
            DestroyGeneratedTransitionCardVisual();
            ClearCardRoot(pastCardRoot);
            ClearCardRoot(transitionCardRoot);
            ClearCardRoot(presentCardRoot);
        }

        private static void ClearCardRoot(Transform cardRoot)
        {
            if (cardRoot == null)
            {
                return;
            }

            for (int i = cardRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(cardRoot.GetChild(i).gameObject);
            }
        }

        public List<MemoryAnchorData> GetAnnotatedCardsByCategory(TimeCategory category)
        {
            var result = new List<MemoryAnchorData>();
            if (anchorManager == null)
            {
                return result;
            }

            List<MemoryAnchorData> allData = anchorManager.GetAnnotatedAnchorData();
            if (allData == null)
            {
                return result;
            }

            for (int i = 0; i < allData.Count; i++)
            {
                MemoryAnchorData data = allData[i];
                if (data != null && data.timeCategory == category)
                {
                    result.Add(data);
                }
            }

            return result;
        }

        public void SpawnCardsIntoRoot(TimeCategory category, Transform cardRoot)
        {
            SpawnCardsIntoRoot(category, cardRoot, false);
        }

        public void SpawnCardsIntoRoot(TimeCategory category, Transform cardRoot, bool alignToAnchorOrigin)
        {
            if (cardRoot == null)
            {
                Debug.LogWarning("[LineOfLife3DView] SpawnCardsIntoRoot ignored: cardRoot is null.");
                return;
            }

            float verticalOrigin = alignToAnchorOrigin ? 0f : -0.35f;
            List<MemoryAnchorData> items = GetAnnotatedCardsByCategory(category);
            if (items.Count == 0)
            {
                Transform sourceRoot = category == TimeCategory.Past
                    ? pastCardRoot
                    : category == TimeCategory.Present
                        ? presentCardRoot
                        : null;
                if (sourceRoot != null && sourceRoot.childCount > 0)
                {
                    CloneTimelineCardsInto(sourceRoot, cardRoot, verticalOrigin);
                    Debug.Log("[LineOfLife3DView] Cloned " + sourceRoot.childCount + " " + category + " cards into Photobook.");
                    return;
                }
            }

            PopulateCardRegion(category.ToString(), items, cardRoot, false, verticalOrigin);
        }

        private void CloneTimelineCardsInto(Transform sourceRoot, Transform cardRoot, float verticalOrigin)
        {
            int count = sourceRoot.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform source = sourceRoot.GetChild(i);
                if (source == null)
                {
                    continue;
                }

                GameObject clone = Instantiate(source.gameObject, cardRoot);
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = Vector3.one;
                clone.transform.localPosition = GetCardLocalPosition(i, count, verticalOrigin);

                MemoryCard3DUI sourceUi = source.GetComponent<MemoryCard3DUI>();
                MemoryCard3DUI cloneUi = clone.GetComponent<MemoryCard3DUI>();
                if (cloneUi != null)
                {
                    cloneUi.Initialize(
                        sourceUi != null ? sourceUi.Data : cloneUi.Data,
                        memoryCardDetailPanel,
                        null);
                }

            }
        }

        private void PopulateCardRegion(string regionLabel, List<MemoryAnchorData> items, Transform cardRoot)
        {
            PopulateCardRegion(regionLabel, items, cardRoot, true, -0.35f);
        }

        private void PopulateCardRegion(
            string regionLabel,
            List<MemoryAnchorData> items,
            Transform cardRoot,
            bool registerForPairSelection,
            float verticalOrigin)
        {
            if (cardRoot == null)
            {
                Debug.LogWarning("[LineOfLife3DView] Card root for " + regionLabel + " is not assigned.");
                return;
            }

            if (items == null || items.Count == 0)
            {
                Debug.Log("[LineOfLife3DView] " + regionLabel + " has no memory cards.");
                return;
            }

            if (memoryCard3DPrefab == null)
            {
                Debug.LogWarning("[LineOfLife3DView] memoryCard3DPrefab is not assigned.");
                return;
            }

            if (memoryCardDetailPanel == null)
            {
                Debug.LogWarning(
                    "[LineOfLife3DView] memoryCardDetailPanel is NOT assigned. " +
                    "3D card clicks will not open any detail UI. " +
                    "Assign your 3DMemoryCardDetailPanel on LineOfLife3DViewController.");
            }

            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                GameObject cardObject = Instantiate(memoryCard3DPrefab, cardRoot);
                cardObject.transform.localScale = Vector3.one;
                cardObject.transform.localPosition = GetCardLocalPosition(i, count, verticalOrigin);

                MemoryCard3DUI cardUi = cardObject.GetComponent<MemoryCard3DUI>();
                if (cardUi == null)
                {
                    Debug.LogWarning("[LineOfLife3DView] memoryCard3DPrefab is missing MemoryCard3DUI.");
                    Destroy(cardObject);
                    continue;
                }

                LineOfLife3DViewController pairHost = registerForPairSelection ? this : null;
                cardUi.Initialize(items[i], memoryCardDetailPanel, pairHost);
                if (registerForPairSelection)
                {
                    _spawnedCards.Add(cardUi);
                }
            }

            if (registerForPairSelection)
            {
                RefreshAllSelectForPairButtons();
            }
        }

        private Vector3 GetCardLocalPosition(int index, int count, float verticalOrigin)
        {
            int row = index / maxCardsPerRow;
            int col = index % maxCardsPerRow;
                int itemsInRow = Mathf.Min(maxCardsPerRow, count - row * maxCardsPerRow);
                float x = (col - (itemsInRow - 1) / 2f) * cardSpacingX;
            float y = verticalOrigin - row * cardSpacingY;
            return new Vector3(x, y, 0f);
        }

        public void SelectForTransitionPair(MemoryAnchorData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[GeneratedTransitionFlow] SelectForTransitionPair ignored: data is null.");
                return;
            }

            MemoryCard3DUI match = null;
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                MemoryCard3DUI card = _spawnedCards[i];
                if (card != null && card.Data != null && card.Data.id == data.id)
                {
                    match = card;
                    break;
                }
            }

            if (match == null)
            {
                Debug.LogWarning("[GeneratedTransitionFlow] SelectForTransitionPair: no 3D card found for id=" + data.id);
                return;
            }

            SelectForTransitionPair(match);
        }

        public void SelectForTransitionPair(MemoryCard3DUI card)
        {
            if (!UseGeneratedFlow)
            {
                return;
            }

            if (card == null || card.Data == null)
            {
                Debug.LogWarning("[GeneratedTransitionFlow] SelectForTransitionPair ignored: card is null.");
                return;
            }

            if (card.GeneratedData != null)
            {
                Debug.Log("[GeneratedTransitionFlow] Generated transition card cannot be selected as a pair endpoint.");
                return;
            }

            if (card.IsUsedInGeneratedPair)
            {
                Debug.Log("[GeneratedTransitionFlow] Card already used in a generated pair, selection ignored.");
                return;
            }

            if (!IsPairSelectionEnabled)
            {
                Debug.Log("[GeneratedTransitionFlow] Pair selection ignored until NextButton.");
                return;
            }

            TimeCategory category = card.Data.timeCategory;
            Color previewColor = PeekNextPairColor();
            if (category == TimeCategory.Past)
            {
                if (selectedPastCardUi != null && selectedPastCardUi != card)
                {
                    selectedPastCardUi.SetPairSelected(false);
                }

                selectedPastCard = card.Data;
                selectedPastCardUi = card;
                card.SetPairSelected(true);
                card.SetGroupColor(previewColor);
                Debug.Log("[GeneratedTransitionFlow] Past selected: " + FormatCardLog(card.Data));
            }
            else if (category == TimeCategory.Present)
            {
                if (selectedPresentCardUi != null && selectedPresentCardUi != card)
                {
                    selectedPresentCardUi.SetPairSelected(false);
                }

                selectedPresentCard = card.Data;
                selectedPresentCardUi = card;
                card.SetPairSelected(true);
                card.SetGroupColor(previewColor);
                Debug.Log("[GeneratedTransitionFlow] Present selected: " + FormatCardLog(card.Data));
            }
            else
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] Only Past / Present cards can be selected for a pair. category="
                    + category);
                return;
            }

            RefreshPairSelectionInstruction();

            if (selectedPastCard != null && selectedPresentCard != null)
            {
                ShowTransitionGenerationPrompt();
            }
        }

        public void ShowTransitionGenerationPrompt()
        {
            ShowTransitionGenerationPrompt(true);
        }

        private void ShowTransitionGenerationPrompt(bool clearPendingInput)
        {
            if (!UseGeneratedFlow)
            {
                return;
            }

            if (selectedPastCard == null || selectedPresentCard == null)
            {
                Debug.LogWarning("[GeneratedTransitionFlow] Need one Past and one Present card before showing the generation prompt.");
                RefreshPairSelectionInstruction();
                return;
            }

            HideAllPrompts();
            if (clearPendingInput)
            {
                pendingVoiceText = string.Empty;
                if (transitionGenerationInputField != null)
                {
                    transitionGenerationInputField.text = string.Empty;
                }

                if (_transitionVoiceInput != null)
                {
                    _transitionVoiceInput.ResetToIdle();
                }
            }
            else if (transitionGenerationInputField != null
                && string.IsNullOrEmpty(transitionGenerationInputField.text)
                && !string.IsNullOrEmpty(pendingVoiceText))
            {
                transitionGenerationInputField.text = pendingVoiceText;
            }

            RegisterGeneratedTransitionUi();
            EnsureGeneratedTransitionUiRaycast();

            if (transitionGenerationPromptRoot != null)
            {
                if (!transitionGenerationPromptRoot.activeInHierarchy
                    && transitionGenerationPromptRoot.transform.parent != null
                    && !transitionGenerationPromptRoot.transform.parent.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning(
                        "[GeneratedTransitionFlow] transitionGenerationPromptRoot parent is inactive, so buttons cannot receive clicks. " +
                        "Do not parent this panel under hidden TransitionNode.");
                }

                transitionGenerationPromptRoot.SetActive(true);
                PlaceTransitionGeneratePanelAboveTransitionNode();
                BindTransitionVoiceInput();
            }
            else
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] transitionGenerationPromptRoot is not assigned. " +
                    "Create the prompt UI in Unity and drag it here.");
            }

            ApplyTransitionGenerationPromptText();
            HidePairSelectionInstruction();
            SetPairSelectButtonsEnabled(false);
            Debug.Log("[GeneratedTransitionFlow] Show transition generation prompt");
        }

        public void GenerateTransitionCardFromSelectedPair(string transitionText)
        {
            if (!UseGeneratedFlow)
            {
                return;
            }

            if (selectedPastCard == null || selectedPresentCard == null)
            {
                Debug.LogWarning("[GeneratedTransitionFlow] Cannot generate: Past / Present pair is incomplete.");
                return;
            }

            if (string.IsNullOrWhiteSpace(transitionText))
            {
                transitionText = "（尚未輸入文字）";
            }

            Color groupColor = GetNextPairColor();
            generatedTransitionCard = GeneratedTransitionCardData.Create(
                selectedPastCard,
                selectedPresentCard,
                transitionText.Trim(),
                groupColor);
            _generatedTransitionCards.Add(generatedTransitionCard);

            SpawnGeneratedTransitionCardVisual(generatedTransitionCard);
            ApplyPairGroupColor(groupColor);
            ApplyGeneratedTransitionNodeVisibility();

            if (curveRenderer != null)
            {
                curveRenderer.RenderCurve();
            }

            if (transitionGenerationPromptRoot != null)
            {
                transitionGenerationPromptRoot.SetActive(false);
            }

            hasViewedTransitionPrompt = true;
            pendingVoiceText = string.Empty;
            ClearCurrentPairSelection();
            UpdateProgressiveButtons();
            ShowGeneratedCardInstructionThenAutoHide();

            Debug.Log(
                "[GeneratedTransitionFlow] Generated transition card: "
                + generatedTransitionCard.id
                + " groupId=" + generatedTransitionCard.groupId
                + " text=" + generatedTransitionCard.transitionText);
            Debug.Log("[GeneratedTransitionFlow] Switch-view button unlocked");
        }

        public void NotifyTranscriptApplied(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            string incoming = transcript.Trim();
            if (string.IsNullOrEmpty(pendingVoiceText))
            {
                pendingVoiceText = incoming;
            }
            else
            {
                pendingVoiceText = pendingVoiceText + " " + incoming;
            }

            Debug.Log("[GeneratedTransitionFlow] Voice transcript stored for transition card, pendingLength=" + pendingVoiceText.Length + " text=\"" + incoming + "\"");
        }

        public void OnGenerateTransitionCardClicked()
        {
            Debug.Log("[GeneratedTransitionFlow] Generate button clicked");
            BindTransitionVoiceInput();

            string transcript = pendingVoiceText != null ? pendingVoiceText.Trim() : string.Empty;
            if (string.IsNullOrEmpty(transcript) && _transitionVoiceInput != null)
            {
                transcript = _transitionVoiceInput.LastTranscript != null
                    ? _transitionVoiceInput.LastTranscript.Trim()
                    : string.Empty;
                if (!string.IsNullOrEmpty(transcript))
                {
                    NotifyTranscriptApplied(transcript);
                }
            }

            if (string.IsNullOrEmpty(transcript))
            {
                transcript = "（尚未輸入文字）";
                Debug.Log("[GeneratedTransitionFlow] No transcript recognized; generating with default empty-card text.");
            }

            GenerateTransitionCardFromSelectedPair(transcript);
        }

        private void ResetGeneratedTransitionSession()
        {
            selectedPastCard = null;
            selectedPresentCard = null;
            selectedPastCardUi = null;
            selectedPresentCardUi = null;
            generatedTransitionCard = null;
            _generatedTransitionCards.Clear();
            pendingVoiceText = string.Empty;
            _pairInstructionStep = 0;
            _pairSelectButtonsEnabled = false;
            _pairLookElapsedSeconds = 0f;
            ResetPairInstructionPose();
            StopNextCardButtonRoutine();
            SetNextCardButtonVisible(false);

            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                MemoryCard3DUI card = _spawnedCards[i];
                if (card != null)
                {
                    card.SetPairSelected(false);
                    card.SetUsedInGeneratedPair(false);
                }
            }

            DestroyGeneratedTransitionCardVisual();

            if (transitionGenerationPromptRoot != null)
            {
                transitionGenerationPromptRoot.SetActive(false);
            }

            if (transitionGenerationInputField != null)
            {
                transitionGenerationInputField.text = string.Empty;
            }

            ApplyGeneratedTransitionNodeVisibility();
            RefreshPairSelectionInstruction();
            UpdateProgressiveButtons();
        }

        private void ApplyGeneratedTransitionNodeVisibility()
        {
            if (!UseGeneratedFlow)
            {
                if (transitionNode != null)
                {
                    transitionNode.gameObject.SetActive(true);
                }

                return;
            }

            bool showTransition = HasGeneratedTransitionCards || !hideTransitionNodeUntilGenerated;
            if (transitionNode != null)
            {
                transitionNode.gameObject.SetActive(showTransition);
            }

            if (generatedTransitionNode != null)
            {
                generatedTransitionNode.gameObject.SetActive(showTransition);
            }
        }

        private void RestoreGeneratedTransitionProgress()
        {
            selectedPastCardUi = null;
            selectedPresentCardUi = null;

            for (int i = 0; i < _generatedTransitionCards.Count; i++)
            {
                SpawnGeneratedTransitionCardVisual(_generatedTransitionCards[i]);
            }

            if (_generatedTransitionCards.Count > 0)
            {
                generatedTransitionCard = _generatedTransitionCards[_generatedTransitionCards.Count - 1];
                hasViewedTransitionPrompt = true;
            }

            RestoreUsedGeneratedPairVisuals();
            RelinkInProgressPairSelection();
            ApplyGeneratedTransitionNodeVisibility();
            UpdateProgressiveButtons();
            RefreshAllSelectForPairButtons();
            Debug.Log(
                "[LineOfLife3DView] Restored generated progress: cards="
                + _generatedTransitionCards.Count
                + ", pairStep=" + _pairInstructionStep);
        }

        private void RestoreUsedGeneratedPairVisuals()
        {
            for (int i = 0; i < _generatedTransitionCards.Count; i++)
            {
                GeneratedTransitionCardData generated = _generatedTransitionCards[i];
                if (generated == null)
                {
                    continue;
                }

                MemoryCard3DUI pastUi = FindSpawnedMemoryCard(generated.pastCard);
                MemoryCard3DUI presentUi = FindSpawnedMemoryCard(generated.presentCard);
                if (pastUi != null)
                {
                    pastUi.SetUsedInGeneratedPair(true);
                    pastUi.SetGroupColor(generated.groupColor);
                }

                if (presentUi != null)
                {
                    presentUi.SetUsedInGeneratedPair(true);
                    presentUi.SetGroupColor(generated.groupColor);
                }
            }
        }

        private void RelinkInProgressPairSelection()
        {
            if (selectedPastCard != null && selectedPastCard.timeCategory != TimeCategory.Past)
            {
                selectedPastCard = null;
            }

            if (selectedPresentCard != null && selectedPresentCard.timeCategory != TimeCategory.Present)
            {
                selectedPresentCard = null;
            }

            selectedPastCardUi = FindSpawnedMemoryCard(selectedPastCard);
            selectedPresentCardUi = FindSpawnedMemoryCard(selectedPresentCard);
            Color previewColor = PeekNextPairColor();
            if (selectedPastCardUi != null)
            {
                selectedPastCardUi.SetPairSelected(true);
                selectedPastCardUi.SetGroupColor(previewColor);
            }

            if (selectedPresentCardUi != null)
            {
                selectedPresentCardUi.SetPairSelected(true);
                selectedPresentCardUi.SetGroupColor(previewColor);
            }
        }

        private void RestoreGeneratedFlowUi()
        {
            if (selectedPastCard != null && selectedPresentCard != null)
            {
                ShowTransitionGenerationPrompt(false);
                return;
            }

            ShowPairSelectionInstruction();
            RefreshPairSelectionInstruction();
        }

        private MemoryCard3DUI FindSpawnedMemoryCard(MemoryAnchorData data)
        {
            if (data == null)
            {
                return null;
            }

            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                MemoryCard3DUI card = _spawnedCards[i];
                if (card == null || card.GeneratedData != null || card.Data == null)
                {
                    continue;
                }

                if (card.Data == data)
                {
                    return card;
                }

                if (!string.IsNullOrEmpty(data.id) && card.Data.id == data.id)
                {
                    return card;
                }
            }

            return null;
        }

        private void SpawnGeneratedTransitionCardVisual()
        {
            SpawnGeneratedTransitionCardVisual(generatedTransitionCard);
        }

        private void SpawnGeneratedTransitionCardVisual(GeneratedTransitionCardData data)
        {
            if (data == null)
            {
                return;
            }

            Transform parent = ResolveGeneratedCardParent();
            if (parent == null)
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] No generated card parent. Assign generatedTransitionCardAnchor, generatedTransitionNode, or reuse TransitionNode.");
                return;
            }

            GameObject prefab = generatedTransitionCardPrefab != null
                ? generatedTransitionCardPrefab
                : memoryCard3DPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[GeneratedTransitionFlow] No prefab for generated transition card.");
                return;
            }

            generatedTransitionCardObject = Instantiate(prefab, parent);
            generatedTransitionCardObject.transform.localRotation = Quaternion.identity;
            generatedTransitionCardObject.transform.localScale = Vector3.one;
            _generatedTransitionCardObjects.Add(generatedTransitionCardObject);
            LayoutGeneratedTransitionCards();
            Debug.Log(
                "[GeneratedTransitionFlow] Spawned transition card under "
                + parent.name
                + " count=" + _generatedTransitionCardObjects.Count);

            MemoryCard3DUI cardUi = generatedTransitionCardObject.GetComponent<MemoryCard3DUI>();
                if (cardUi == null)
                {
                Debug.LogWarning("[GeneratedTransitionFlow] Generated card prefab is missing MemoryCard3DUI.");
                return;
            }

            cardUi.InitializeGenerated(data, memoryCardDetailPanel, this);
            _spawnedCards.Add(cardUi);
        }

        private void LayoutGeneratedTransitionCards()
        {
            int count = _generatedTransitionCardObjects.Count;
            int columns = maxCardsPerRow > 0 ? maxCardsPerRow : 3;
            for (int i = 0; i < count; i++)
            {
                GameObject cardObject = _generatedTransitionCardObjects[i];
                if (cardObject == null)
                {
                    continue;
                }

                int row = i / columns;
                int col = i % columns;
                int itemsInRow = Mathf.Min(columns, count - row * columns);
                float x = (col - (itemsInRow - 1) / 2f) * cardSpacingX;
                float y = generatedTransitionCardLocalOffset.y - row * cardSpacingY;
                cardObject.transform.localPosition = new Vector3(
                    x,
                    y,
                    generatedTransitionCardLocalOffset.z);
            }
        }

        private Transform ResolveGeneratedCardParent()
        {
            if (transitionCardRoot != null)
            {
                return transitionCardRoot;
            }

            if (generatedTransitionCardAnchor != null)
            {
                return generatedTransitionCardAnchor;
            }

            if (generatedTransitionNode != null)
            {
                return generatedTransitionNode;
            }

            return transitionNode;
        }

        private void DestroyGeneratedTransitionCardVisual()
        {
            for (int i = 0; i < _generatedTransitionCardObjects.Count; i++)
            {
                GameObject cardObject = _generatedTransitionCardObjects[i];
                if (cardObject == null)
                {
                    continue;
                }

                MemoryCard3DUI ui = cardObject.GetComponent<MemoryCard3DUI>();
                if (ui != null)
                {
                    _spawnedCards.Remove(ui);
                }

                    Destroy(cardObject);
            }

            _generatedTransitionCardObjects.Clear();
            generatedTransitionCardObject = null;
        }

        private void ClearCurrentPairSelection()
        {
            if (selectedPastCardUi != null)
            {
                selectedPastCardUi.SetPairSelected(false);
            }

            if (selectedPresentCardUi != null)
            {
                selectedPresentCardUi.SetPairSelected(false);
            }

            selectedPastCard = null;
            selectedPresentCard = null;
            selectedPastCardUi = null;
            selectedPresentCardUi = null;
        }

        private void ApplyPairGroupColor(Color groupColor)
        {
            if (selectedPastCardUi != null)
            {
                selectedPastCardUi.SetGroupColor(groupColor);
                selectedPastCardUi.SetUsedInGeneratedPair(true);
                selectedPastCardUi.SetPairSelected(true);
            }

            if (selectedPresentCardUi != null)
            {
                selectedPresentCardUi.SetGroupColor(groupColor);
                selectedPresentCardUi.SetUsedInGeneratedPair(true);
                selectedPresentCardUi.SetPairSelected(true);
            }
        }

        private Color GetNextPairColor()
        {
            if (generatedPairColors == null || generatedPairColors.Length == 0)
            {
                return new Color(0.95f, 0.72f, 0.35f, 1f);
            }

            Color color = generatedPairColors[_pairColorIndex % generatedPairColors.Length];
            _pairColorIndex++;
            return color;
        }

        private Color PeekNextPairColor()
        {
            if (generatedPairColors == null || generatedPairColors.Length == 0)
            {
                return new Color(0.95f, 0.72f, 0.35f, 1f);
            }

            return generatedPairColors[_pairColorIndex % generatedPairColors.Length];
        }

        private void RefreshPairSelectionInstruction()
        {
            if (!UseGeneratedFlow)
            {
                return;
            }

            if (pairSelectionInstructionText == null)
            {
                return;
            }

            if (_pairInstructionStep < 2)
            {
                return;
            }

            if (selectedPastCard != null && selectedPresentCard != null)
            {
                HidePairSelectionInstruction();
                return;
            }

            StopGeneratedInstructionAutoHide();
            _pairInstructionStep = 2;
            StopNextCardButtonRoutine();
            SetNextCardButtonVisible(false);
            SetPairInstructionTimerVisible(false);

            if (selectedPastCard != null)
            {
                pairSelectionInstructionText.text = "已選 Past。請再從「現在」選 1 張卡。";
                EnsurePairSelectionInstructionVisible();
                return;
            }

            if (selectedPresentCard != null)
            {
                pairSelectionInstructionText.text = "已選 Present。請再從「過去」選 1 張卡。";
                EnsurePairSelectionInstructionVisible();
                return;
            }

            ShowPairInstructionSelectStep();
        }

        private void EnsurePairSelectionInstructionVisible()
        {
            Transform root = ResolvePairInstructionRoot();
            if (root != null)
            {
                root.gameObject.SetActive(true);
            }

            if (pairSelectionInstructionText != null)
            {
                pairSelectionInstructionText.gameObject.SetActive(true);
            }

            _pairInstructionVisible = true;
            PlacePairInstructionIfNeeded();
        }

        private void ShowPairInstructionLookStep()
        {
            _pairInstructionStep = 1;
            _pairLookElapsedSeconds = 0f;
            SetPairSelectButtonsEnabled(false);
            BeginPairInstructionLookStep();
        }

        private void ResumePairInstructionLookStep()
        {
            _pairInstructionStep = 1;
            BeginPairInstructionLookStep();
        }

        private void BeginPairInstructionLookStep()
        {
            StopGeneratedInstructionAutoHide();
            StopNextCardButtonRoutine();
            if (pairSelectionInstructionText != null)
            {
                pairSelectionInstructionText.text = pairInstructionLookText;
            }

            EnsurePairSelectionInstructionVisible();
            SetPairInstructionTimerVisible(false);
            SetNextCardButtonVisible(true);
            SetPairSelectButtonsEnabled(false);
            RefreshAllSelectForPairButtons();
        }

        private void ShowPairInstructionSelectStep()
        {
            _pairInstructionStep = 2;
            StopNextCardButtonRoutine();
            SetNextCardButtonVisible(false);
            SetPairInstructionTimerVisible(false);
            if (pairSelectionInstructionText != null)
            {
                pairSelectionInstructionText.text = pairInstructionSelectText;
            }

            EnsurePairSelectionInstructionVisible();
            SetPairSelectButtonsEnabled(true);
            RefreshAllSelectForPairButtons();
        }

        private void RefreshAllSelectForPairButtons()
        {
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                MemoryCard3DUI card = _spawnedCards[i];
                if (card != null)
                {
                    card.RefreshSelectForPairButtonVisibility();
                }
            }
        }

        private void SetPairSelectButtonsEnabled(bool enabled)
        {
            _pairSelectButtonsEnabled = enabled;
            RefreshAllSelectForPairButtons();
        }

        private void ApplyTransitionGenerationPromptText()
        {
            TMP_Text prompt = ResolveTransitionGenerationPromptLabel();
            if (prompt == null || string.IsNullOrEmpty(transitionGenerationPromptText))
            {
                return;
            }

            prompt.text = transitionGenerationPromptText;
        }

        private TMP_Text ResolveTransitionGenerationPromptLabel()
        {
            if (transitionGenerationPromptRoot == null)
            {
                return null;
            }

            Transform found = FindChildByName(
                transitionGenerationPromptRoot.transform,
                "TransitionGeneratePrompt");
            if (found == null)
            {
                return null;
            }

            return found.GetComponent<TMP_Text>();
        }

        private void RegisterNextCardButton()
        {
            ResolveNextCardButton();
            ResolvePairInstructionTimer();
            SetPairInstructionTimerVisible(false);
            if (nextCardButton == null)
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] NextcardButton is not assigned. " +
                    "Name it NextcardButton under the pair instruction, or drag it onto LineOfLife3DViewController.");
                return;
            }

            nextCardButton.onClick.RemoveListener(OnNextCardButtonClicked);
            nextCardButton.onClick.AddListener(OnNextCardButtonClicked);
            SetNextCardButtonVisible(false);
        }

        private void UnbindNextCardButton()
        {
            if (nextCardButton != null)
            {
                nextCardButton.onClick.RemoveListener(OnNextCardButtonClicked);
            }
        }

        private void ResolveNextCardButton()
        {
            if (nextCardButton != null)
            {
                return;
            }

            Transform searchRoot = ResolvePairInstructionRoot();
            Transform found = FindChildByName(searchRoot, "NextcardButton", "NextCardButton", "NextCard");
            if (found == null)
            {
                found = FindChildByName(viewRoot != null ? viewRoot.transform : transform, "NextcardButton", "NextCardButton", "NextCard");
            }

            if (found != null)
            {
                nextCardButton = found.GetComponent<Button>();
                if (nextCardButton == null)
                {
                    nextCardButton = found.GetComponentInChildren<Button>(true);
                }
            }
        }

        private void OnNextCardButtonClicked()
        {
            Debug.Log("[GeneratedTransitionFlow] NextcardButton clicked");
            ShowPairInstructionSelectStep();
        }

        private IEnumerator ShowNextCardButtonAfterDelay()
        {
            float delay = nextCardButtonDelaySeconds > 0f ? nextCardButtonDelaySeconds : 30f;
            float elapsed = Mathf.Max(0f, _pairLookElapsedSeconds);
            bool nextShown = elapsed >= delay;
            SetPairInstructionTimerVisible(true);
            SetPairInstructionTimerSeconds(elapsed);
            if (nextShown)
            {
                SetNextCardButtonVisible(true);
            }

            while (_pairInstructionStep == 1 && _pairInstructionVisible)
            {
                SetPairInstructionTimerSeconds(elapsed);
                _pairLookElapsedSeconds = elapsed;
                if (!nextShown && elapsed >= delay)
                {
                    SetNextCardButtonVisible(true);
                    nextShown = true;
                }

                yield return null;
                elapsed += Time.deltaTime;
            }

            _pairLookElapsedSeconds = elapsed;
            _nextCardButtonRoutine = null;
        }

        private void ResolvePairInstructionTimer()
        {
            if (pairInstructionTimerText != null)
            {
                if (pairInstructionTimerText == pairSelectionInstructionText)
                {
                    pairInstructionTimerText = null;
                }
                else
                {
                    return;
                }
            }

            Transform searchRoot = ResolvePairInstructionRoot();
            Transform found = FindChildByName(searchRoot, "Timer", "TimerText", "PairInstructionTimer");
            if (found == null && pairSelectionInstructionText != null)
            {
                found = FindChildByName(pairSelectionInstructionText.transform, "Timer", "TimerText", "PairInstructionTimer");
            }

            if (found == null)
            {
                found = FindPairInstructionTimerByDisplayText(searchRoot);
            }

            if (found == null)
            {
                return;
            }

            pairInstructionTimerText = found.GetComponent<TMP_Text>();
            if (pairInstructionTimerText == null)
            {
                pairInstructionTimerText = found.GetComponentInChildren<TMP_Text>(true);
            }

            if (pairInstructionTimerText == pairSelectionInstructionText)
            {
                pairInstructionTimerText = null;
            }
        }

        private static Transform FindPairInstructionTimerByDisplayText(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null)
                {
                    continue;
                }

                string value = text.text;
                if (!string.IsNullOrEmpty(value) && value.Trim().StartsWith("00:00"))
                {
                    return text.transform;
                }
            }

            return null;
        }

        private void SetPairInstructionTimerVisible(bool visible)
        {
            ResolvePairInstructionTimer();
            if (pairInstructionTimerText == null)
            {
                return;
            }

            if (!visible)
            {
                SetPairInstructionTimerSeconds(0f);
            }

            pairInstructionTimerText.gameObject.SetActive(visible);
        }

        private void SetPairInstructionTimerSeconds(float seconds)
        {
            if (pairInstructionTimerText == null)
            {
                return;
            }

            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int minutes = total / 60;
            int remainder = total % 60;
            pairInstructionTimerText.text = string.Format("{0:00}:{1:00}", minutes, remainder);
        }

        private void StopNextCardButtonRoutine()
        {
            if (_nextCardButtonRoutine == null)
            {
                return;
            }

            StopCoroutine(_nextCardButtonRoutine);
            _nextCardButtonRoutine = null;
        }

        private void SetNextCardButtonVisible(bool visible)
        {
            ResolveNextCardButton();
            if (nextCardButton == null)
            {
                return;
            }

            nextCardButton.gameObject.SetActive(visible);
        }

        private static Transform FindChildByName(Transform root, params string[] names)
        {
            if (root == null || names == null)
            {
                return null;
            }

            for (int n = 0; n < names.Length; n++)
            {
                if (string.Equals(root.name, names[n], System.StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByName(root.GetChild(i), names);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void HidePairSelectionInstruction()
        {
            StopGeneratedInstructionAutoHide();
            StopNextCardButtonRoutine();
            SetNextCardButtonVisible(false);
            SetPairInstructionTimerVisible(false);
            _pairInstructionVisible = false;
            SetPairSelectButtonsEnabled(false);
            Transform root = ResolvePairInstructionRoot();
            if (root != null)
            {
                root.gameObject.SetActive(false);
            }
        }

        private void ShowGeneratedCardInstructionThenAutoHide()
        {
            Transform root = ResolvePairInstructionRoot();
            if (root != null)
            {
                root.gameObject.SetActive(true);
            }

            if (pairSelectionInstructionText != null)
            {
                pairSelectionInstructionText.gameObject.SetActive(true);
                pairSelectionInstructionText.text = GeneratedTransitionCardInstruction;
            }

            StopNextCardButtonRoutine();
            SetNextCardButtonVisible(true);
            SetPairInstructionTimerVisible(false);

            _pairInstructionStep = 1;
            _pairInstructionVisible = true;
            SetPairSelectButtonsEnabled(false);
            PlacePairInstructionIfNeeded();
            StopGeneratedInstructionAutoHide();
        }

        private void RestartGeneratedInstructionAutoHide()
        {
            StopGeneratedInstructionAutoHide();
            if (!isActiveAndEnabled)
            {
                return;
            }

            _generatedInstructionAutoHideRoutine = StartCoroutine(AutoHideGeneratedInstructionAfterDelay());
        }

        private void StopGeneratedInstructionAutoHide()
        {
            if (_generatedInstructionAutoHideRoutine == null)
            {
                return;
            }

            StopCoroutine(_generatedInstructionAutoHideRoutine);
            _generatedInstructionAutoHideRoutine = null;
        }

        private IEnumerator AutoHideGeneratedInstructionAfterDelay()
        {
            float seconds = generatedInstructionAutoHideSeconds > 0f
                ? generatedInstructionAutoHideSeconds
                : 10f;
            yield return new WaitForSeconds(seconds);
            _generatedInstructionAutoHideRoutine = null;
            HidePairSelectionInstruction();
        }

        private Transform ResolvePairInstructionRoot()
        {
            if (pairSelectionInstructionRoot != null)
            {
                return pairSelectionInstructionRoot;
            }

            if (pairSelectionInstructionText != null)
            {
                return pairSelectionInstructionText.transform;
            }

            return null;
        }

        private void ShowPairSelectionInstruction()
        {
            Transform root = ResolvePairInstructionRoot();
            if (root == null)
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] pairSelectionInstructionText / pairSelectionInstructionRoot is not assigned. " +
                    "Create the instruction UI and drag it onto LineOfLife3DViewController.");
                return;
            }

            if (transitionGenerationPromptRoot != null
                && root.IsChildOf(transitionGenerationPromptRoot.transform))
            {
                Debug.LogWarning(
                    "[GeneratedTransitionFlow] Pair instruction is inside Transition generate panel. " +
                    "Move it to a separate object so it can appear right after 2D classification.");
            }

            root.gameObject.SetActive(true);
            if (pairSelectionInstructionText != null)
            {
                pairSelectionInstructionText.gameObject.SetActive(true);
            }

            _pairInstructionVisible = true;
            if (_pairInstructionStep >= 2 && selectedPastCard != null && selectedPresentCard != null)
            {
                ShowTransitionGenerationPrompt(false);
            }
            else if (_pairSelectButtonsEnabled || _pairInstructionStep >= 2)
            {
                ShowPairInstructionSelectStep();
            }
            else if (_pairInstructionStep == 1)
            {
                ResumePairInstructionLookStep();
            }
            else
            {
                ShowPairInstructionLookStep();
            }

            PlacePairInstructionIfNeeded();
            Debug.Log("[GeneratedTransitionFlow] Pair instruction shown in front of user");
        }

        private void PlacePairInstructionIfNeeded()
        {
            Transform target = ResolvePairInstructionRoot();
            if (target == null)
            {
                return;
            }

            if (_pairInstructionPlaced)
            {
                ApplyCachedPairInstructionPose();
                BringPairInstructionToFront();
                return;
            }

            bool placedAtNode = false;
            if (placePairInstructionAtTransitionNode)
            {
                placedAtNode = PositionPairInstructionAtTransitionNode();
            }

            if (!placedAtNode && keepPairInstructionInFrontOfUser)
            {
                PositionPairInstructionInFrontOfUser();
            }

            CachePairInstructionPose();
            BringPairInstructionToFront();
        }

        private void BringPairInstructionToFront()
        {
            Transform root = ResolvePairInstructionRoot();
            if (root == null)
            {
                return;
            }

            int order = pairInstructionSortingOrder;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.sortingOrder = order;
            }

            Shader overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay");
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null)
                {
                    continue;
                }

                text.raycastTarget = false;
                Material fontMaterial = text.fontMaterial;
                if (fontMaterial == null)
                {
                    continue;
                }

                if (overlayShader != null && fontMaterial.shader != overlayShader)
                {
                    fontMaterial.shader = overlayShader;
                }

                fontMaterial.renderQueue = PairInstructionRenderQueue;
            }

            Material uiMaterial = ResolvePairInstructionUiNoZTestMaterial();
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || graphic is TMP_Text)
                {
                    continue;
                }

                bool isButtonGraphic = graphic.GetComponent<Button>() != null;
                graphic.raycastTarget = isButtonGraphic;
                if (uiMaterial != null)
                {
                    graphic.material = uiMaterial;
                }
            }
        }

        private Material ResolvePairInstructionUiNoZTestMaterial()
        {
            if (_pairInstructionUiNoZTestMaterial != null)
            {
                return _pairInstructionUiNoZTestMaterial;
            }

            Shader shader = Shader.Find("UI/NoZTest");
            if (shader == null)
            {
                return null;
            }

            _pairInstructionUiNoZTestMaterial = new Material(shader);
            _pairInstructionUiNoZTestMaterial.name = "PairInstruction-UI-NoZTest";
            _pairInstructionUiNoZTestMaterial.renderQueue = PairInstructionRenderQueue;
            return _pairInstructionUiNoZTestMaterial;
        }

        private void CachePairInstructionPose()
        {
            Transform target = ResolvePairInstructionRoot();
            if (target == null)
            {
                return;
            }

            _pairInstructionWorldPosition = target.position;
            _pairInstructionWorldRotation = target.rotation;
            _pairInstructionPlaced = true;
        }

        private void ApplyCachedPairInstructionPose()
        {
            Transform target = ResolvePairInstructionRoot();
            if (target == null || !_pairInstructionPlaced)
            {
                return;
            }

            target.position = _pairInstructionWorldPosition;
            target.rotation = _pairInstructionWorldRotation;
        }

        private void ResetPairInstructionPose()
        {
            _pairInstructionPlaced = false;
        }

        private bool PositionPairInstructionAtTransitionNode()
        {
            Transform node = ResolveTransitionNodeTransform();
            Transform target = ResolvePairInstructionRoot();
            if (node == null || target == null)
            {
                return false;
            }

            target.position = node.TransformPoint(pairInstructionOffsetFromTransitionNode);
            FacePairInstructionToCameraIfNeeded(target);
            return true;
        }

        private void PositionPairInstructionInFrontOfUser()
        {
            Transform cam = ResolvePlayerCameraTransform();
            Transform target = ResolvePairInstructionRoot();
            if (cam == null || target == null)
            {
                return;
            }

            Vector3 targetPos = cam.position
                + cam.forward * pairInstructionDistanceFromCamera
                + cam.up * pairInstructionHeightOffset;
            target.position = targetPos;
            FacePairInstructionToCameraIfNeeded(target);
        }

        private void FacePairInstructionToCameraIfNeeded(Transform target)
        {
            if (!pairInstructionFaceCamera || target == null)
            {
                return;
            }

            Transform cam = ResolvePlayerCameraTransform();
            if (cam == null)
            {
                return;
            }

            Vector3 dir = target.position - cam.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                target.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }

        private void PlaceTransitionGeneratePanelAboveTransitionNode()
        {
            Transform target = ResolveTransitionGeneratePanelTransform();
            Transform cam = ResolvePlayerCameraTransform();
            Transform node = ResolveTransitionNodeTransform();
            if (target == null || cam == null || node == null)
            {
                Debug.LogWarning("[GeneratedTransitionFlow] Cannot place TransitionGeneratePanel; missing panel, camera, or Transition node.");
                return;
            }

            target.position = node.position + Vector3.up * generatePanelHeightOffset;

            if (generatePanelFaceCamera)
            {
                Vector3 dir = target.position - cam.position;
                if (generatePanelLockYOnly)
                {
                    dir.y = 0f;
                }

                if (dir.sqrMagnitude > 0.001f)
                {
                    target.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
            }

            if (generatePanelFlip180)
            {
                target.Rotate(0f, 180f, 0f, Space.Self);
            }
        }

        private Transform ResolveTransitionNodeTransform()
        {
            if (transitionNode != null)
            {
                return transitionNode;
            }

            return generatedTransitionNode;
        }

        private Transform ResolveTransitionGeneratePanelTransform()
        {
            if (transitionGenerationPromptRoot == null)
            {
                return null;
            }

            Transform root = transitionGenerationPromptRoot.transform;
            Transform namedPanel = root.Find("TransitionGeneratePanel");
            if (namedPanel != null)
            {
                return namedPanel;
            }

            if (root.childCount > 0)
            {
                return root.GetChild(0);
            }

            return root;
        }

        private Transform ResolvePlayerCameraTransform()
        {
            if (playerCamera != null)
            {
                return playerCamera;
            }

            if (Camera.main != null)
            {
                playerCamera = Camera.main.transform;
                return playerCamera;
            }

            return null;
        }

        private Camera ResolvePlayerCameraComponent()
        {
            Transform camTransform = ResolvePlayerCameraTransform();
            if (camTransform == null)
            {
                return Camera.main;
            }

            Camera cam = camTransform.GetComponent<Camera>();
            if (cam == null)
            {
                cam = camTransform.GetComponentInChildren<Camera>();
            }

            return cam != null ? cam : Camera.main;
        }

        private static string FormatCardLog(MemoryAnchorData data)
        {
            if (data == null)
            {
                return "(null)";
            }

            string text = string.IsNullOrEmpty(data.userText) ? "（尚未輸入文字）" : data.userText;
            if (text.Length > 40)
            {
                text = text.Substring(0, 40) + "...";
            }

            return "id=" + data.id + " text=" + text;
        }

    }
}
