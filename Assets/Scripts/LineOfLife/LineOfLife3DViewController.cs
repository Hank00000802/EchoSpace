using System.Collections.Generic;
using EchoSpace.Annotation;
using EchoSpace.Core;
using EchoSpace.SwitchView;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

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
        public Button leftCrossLinkButton;
        public Button rightCrossLinkButton;
        public Button finalReflectionButton;

        [Header("Progressive Reflection Unlock")]
        public bool enableProgressiveUnlock = true;
        public bool resetReflectionProgressOnOpen = true;

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

        [Header("Prefabs & Layout")]
        public GameObject memoryCard3DPrefab;
        public int maxCardsPerRow = 3;
        public float cardSpacingX = 0.38f;
        public float cardSpacingY = 0.24f;

        [Header("Debug Toggle")]
        public bool enableKeyboardToggle = true;
        public KeyCode toggleKey = KeyCode.T;
        public bool refreshOnOpenByHotkey = true;

        [Header("XR Controller Toggle")]
        public bool enableXRControllerToggle = true;
        public bool useRightBButtonFor3DView = true;
        private bool _rightBWasPressed;

        private void Awake()
        {
            ApplyTimelineNodeLayout();
            ResolvePromptReferences();
            RegisterPromptButtons();

            if (viewRoot != null)
            {
                viewRoot.SetActive(false);
            }

            if (finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (enableKeyboardToggle && Input.GetKeyDown(toggleKey))
            {
                ToggleViewByHotkey();
            }

            if (enableXRControllerToggle && useRightBButtonFor3DView && GetRightSecondaryButtonDown())
            {
                Debug.Log("[LineOfLife3DView] Right B button toggle 3D view.");
                ToggleViewByHotkey();
            }
        }

        private bool GetRightSecondaryButtonDown()
        {
            bool pressed = GetRightControllerButton(CommonUsages.secondaryButton);
            bool down = pressed && !_rightBWasPressed;
            _rightBWasPressed = pressed;
            return down;
        }

        private bool GetRightControllerButton(InputFeatureUsage<bool> usage)
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right,
                devices);

            foreach (InputDevice device in devices)
            {
                if (device.TryGetFeatureValue(usage, out bool pressed) && pressed)
                {
                    return true;
                }
            }

            return false;
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

        public void ShowSwitchViewPromptFromFinalReflection()
        {
            if (switchViewController == null)
            {
                Debug.LogWarning("[LineOfLife3DView] switchViewController is not assigned.");
                return;
            }

            switchViewController.ShowSwitchViewPrompt();

            if (hideLineOfLifeWhenOpeningSwitchView && viewRoot != null)
            {
                viewRoot.SetActive(false);
                Debug.Log("[LineOfLife3DView] Hide LineOfLife3DView after opening SwitchView.");
            }

            Debug.Log("[LineOfLife3DView] Show SwitchView prompt from final reflection.");
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

            bool allCrossLinksViewed =
                hasViewedPastToTransitionPrompt &&
                hasViewedTransitionToPresentPrompt;

            SetButtonVisible(leftCrossLinkButton, allNodePromptsViewed);
            SetButtonVisible(rightCrossLinkButton, allNodePromptsViewed);
            SetButtonVisible(finalReflectionButton, allCrossLinksViewed);

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
            PopulateCardRegion("Transition", transition, transitionCardRoot);
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

            Debug.Log("[LineOfLife3DView] Opened 3D timeline summary.");
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
            if (toggleSamePrompt && finalReflectionPrompt != null && finalReflectionPrompt.activeSelf)
            {
                HideAllPrompts();
                return;
            }

            HideAllPrompts();

            if (finalReflectionPrompt != null)
            {
                finalReflectionPrompt.SetActive(true);
            }

            if (showFinalReflectionSwitchButton && finalReflectionSwitchButton != null)
            {
                finalReflectionSwitchButton.gameObject.SetActive(true);
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

            if (viewRoot != null)
            {
                viewRoot.SetActive(false);
            }

            if (hideSwitchViewOnLineOfLifeClose && switchViewController != null)
            {
                switchViewController.HideSwitchViewPrompt();
            }

            Debug.Log("[LineOfLife3DView] Closed.");
        }

        private void PositionTimelineInFrontOfPlayer()
        {
            if (timelineRoot == null || playerCamera == null)
            {
                Debug.LogWarning("[LineOfLife3DView] timelineRoot or playerCamera is not assigned.");
                return;
            }

            timelineRoot.position = playerCamera.position + playerCamera.forward * distanceFromCamera;
            timelineRoot.position += Vector3.up * verticalOffset;

            Vector3 toCamera = playerCamera.position - timelineRoot.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.0001f)
            {
                timelineRoot.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }

            timelineRoot.rotation *= Quaternion.Euler(rotationOffsetEuler);
            Debug.Log("[LineOfLife3DView] Applied rotation offset: " + rotationOffsetEuler);
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

        private void PopulateCardRegion(string regionLabel, List<MemoryAnchorData> items, Transform cardRoot)
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

            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                GameObject cardObject = Instantiate(memoryCard3DPrefab, cardRoot);
                cardObject.transform.localScale = Vector3.one;

                int row = i / maxCardsPerRow;
                int col = i % maxCardsPerRow;
                int itemsInRow = Mathf.Min(maxCardsPerRow, count - row * maxCardsPerRow);
                float x = (col - (itemsInRow - 1) / 2f) * cardSpacingX;
                float y = -0.35f - row * cardSpacingY;
                cardObject.transform.localPosition = new Vector3(x, y, 0f);

                MemoryCard3DUI cardUi = cardObject.GetComponent<MemoryCard3DUI>();
                if (cardUi == null)
                {
                    Debug.LogWarning("[LineOfLife3DView] memoryCard3DPrefab is missing MemoryCard3DUI.");
                    Destroy(cardObject);
                    continue;
                }

                cardUi.Initialize(items[i]);
            }
        }

    }
}
