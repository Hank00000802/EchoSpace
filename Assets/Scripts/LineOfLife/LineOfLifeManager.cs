using System.Collections.Generic;
using System.Text;
using EchoSpace.Annotation;
using EchoSpace.Core;
using EchoSpace.Reflection;
using EchoSpace.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EchoSpace.LineOfLife
{
    public class LineOfLifeManager : MonoBehaviour
    {
        public MemoryAnchorManager anchorManager;
        public GameObject lineOfLifePanel;
        public Canvas lineOfLifeCanvas;
        public GameObject memoryCardPrefab;
        public MemoryCardDetailPanelController memoryCardDetailPanel;
        public Transform unclassifiedCardContainer;
        public LineOfLifeDropZone pastDropZone;
        public LineOfLifeDropZone transitionDropZone;
        public LineOfLifeDropZone presentDropZone;
        public Button finishButton;
        public ReflectionSummaryController reflectionSummaryController;
        public LineOfLife3DViewController lineOfLife3DViewController;
        public bool hideLineOfLifeOnComplete = true;
        [Tooltip("Shown only during the opening practice phase.")]
        public TMP_Text practiceText;
        [Header("Generated Transition Flow")]
        [Tooltip("If true, 2D classification is Past / Present / Uncertain. Transition is generated later in 3D.")]
        public bool useGeneratedTransitionFlow = true;
        public KeyCode toggleKey = KeyCode.L;
        public bool enableToggleHotkey = true;
        [Header("XR UI")]
        [Tooltip("XR main camera for World Space canvas raycasts. Falls back to Camera.main.")]
        public Camera lineOfLifeCamera;

        [Header("XR Placement")]
        [Tooltip("Place the panel in front of the player when opened, then leave it world-fixed.")]
        public bool placeInFrontOfPlayerOnOpen = true;
        public float panelDistanceFromCamera = 1.2f;
        public float panelHeightOffset = 0f;
        public bool panelFaceCamera = true;
        public bool panelLockYOnly = true;
        public bool panelFlip180 = true;
        [Tooltip("Must be above ControllerTutorial (1100) and the intro black sphere, or the panel is covered.")]
        public int panelCanvasSortingOrder = 1200;

        [Header("XR Controller Toggle")]
        public bool enableXRControllerToggle = true;
        public bool useRightAButtonForLineOfLifePanel = true;

        [Header("XR Button Mapping")]
        public XRBoolButtonUsage rightLineOfLifeToggleButton = XRBoolButtonUsage.PrimaryButton;

        private bool _rightLineOfLifeToggleWasPressed;
        private bool _practiceTextWasActive;

        public bool IsOpen =>
            lineOfLifePanel != null && lineOfLifePanel.activeSelf;

        private void Awake()
        {
            if (finishButton != null)
            {
                finishButton.onClick.AddListener(CompleteLineOfLife);
            }

            EnsureLineOfLifeCanvasForXR();
            ApplyGeneratedTransitionFlowLayout();
            ApplyPracticeTextVisibility(true);
            if (useGeneratedTransitionFlow)
            {
                Debug.Log("[GeneratedTransitionFlow] Enabled");
            }
        }

        private void Update()
        {
            ApplyPracticeTextVisibility(false);

            bool tutorialMode = EchoSpace.UI.ControllerTutorialGate.IsBlockingGameplay;
            if (tutorialMode)
            {
                return;
            }

            if (enableToggleHotkey && Input.GetKeyDown(toggleKey))
            {
                TogglePanel();
            }

            if (enableXRControllerToggle && useRightAButtonForLineOfLifePanel)
            {
                bool down = GetRightLineOfLifeToggleButtonDown();
                if (down)
                {
                    Debug.Log("[LineOfLifeManager] Right button toggle LineOfLifePanel: " + rightLineOfLifeToggleButton);
                    TogglePanel();
                }
            }
        }

        private bool GetRightLineOfLifeToggleButtonDown()
        {
            bool pressed = GetControllerButton(InputDeviceCharacteristics.Right, rightLineOfLifeToggleButton);
            bool down = pressed && !_rightLineOfLifeToggleWasPressed;
            _rightLineOfLifeToggleWasPressed = pressed;
            return down;
        }

        private bool GetControllerButton(
            InputDeviceCharacteristics hand,
            XRBoolButtonUsage buttonUsage)
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | hand,
                devices);

            InputFeatureUsage<bool> usage = GetBoolUsage(buttonUsage);

            foreach (InputDevice device in devices)
            {
                if (device.TryGetFeatureValue(usage, out bool pressed) && pressed)
                {
                    return true;
                }
            }

            return false;
        }

        private static InputFeatureUsage<bool> GetBoolUsage(XRBoolButtonUsage buttonUsage)
        {
            switch (buttonUsage)
            {
                case XRBoolButtonUsage.PrimaryButton:
                    return CommonUsages.primaryButton;
                case XRBoolButtonUsage.SecondaryButton:
                    return CommonUsages.secondaryButton;
                case XRBoolButtonUsage.GripButton:
                    return CommonUsages.gripButton;
                case XRBoolButtonUsage.TriggerButton:
                    return CommonUsages.triggerButton;
                case XRBoolButtonUsage.MenuButton:
                    return CommonUsages.menuButton;
                case XRBoolButtonUsage.PrimaryTouch:
                    return CommonUsages.primaryTouch;
                case XRBoolButtonUsage.SecondaryTouch:
                    return CommonUsages.secondaryTouch;
                case XRBoolButtonUsage.Primary2DAxisClick:
                    return CommonUsages.primary2DAxisClick;
                case XRBoolButtonUsage.Secondary2DAxisClick:
                    return CommonUsages.secondary2DAxisClick;
                case XRBoolButtonUsage.Primary2DAxisTouch:
                    return CommonUsages.primary2DAxisTouch;
                case XRBoolButtonUsage.Secondary2DAxisTouch:
                    return CommonUsages.secondary2DAxisTouch;
                default:
                    return CommonUsages.primaryButton;
            }
        }

        public void TogglePanel()
        {
            if (lineOfLifePanel == null)
            {
                Debug.LogWarning("[LineOfLifeManager] lineOfLifePanel is not assigned.");
                return;
            }

            bool willOpen = !lineOfLifePanel.activeSelf;
            lineOfLifePanel.SetActive(willOpen);

            if (willOpen)
            {
                EnsureLineOfLifeCanvasForXR();
                PlaceLineOfLifePanelInFrontOfPlayer();
                ApplyPracticeTextVisibility(true);
                RefreshCards();
            }
            else
            {
                ShowExplorationModeGuideAfterClose();
            }
        }

        private void ShowExplorationModeGuideAfterClose()
        {
            AirAnchorPlacementTester tester = FindObjectOfType<AirAnchorPlacementTester>();
            if (tester != null)
            {
                tester.SetXRInteractionMode(XRInteractionMode.Exploration);
            }

            TutorialOverlayController overlay = FindObjectOfType<TutorialOverlayController>();
            if (overlay != null)
            {
                overlay.ShowCurrentModeGuide();
            }
        }

        public void OpenPanelAndRefresh()
        {
            if (lineOfLifePanel == null)
            {
                Debug.LogWarning("[LineOfLifeManager] lineOfLifePanel is not assigned.");
                return;
            }

            lineOfLifePanel.SetActive(true);
            EnsureLineOfLifeCanvasForXR();
            PlaceLineOfLifePanelInFrontOfPlayer();
            ApplyPracticeTextVisibility(true);
            RefreshCards();
            Debug.Log("[LineOfLifeManager] OpenPanelAndRefresh called.");
        }

        /// <summary>
        /// Ensures LineOfLifeCanvas supports mouse and XR controller UI interaction.
        /// AirAnchorPlacementTester already blocks anchor placement when pointer is over UI;
        /// card drag should not trigger new anchor placement while raycasting this canvas.
        /// </summary>
        public void EnsureLineOfLifeCanvasForXR()
        {
            if (lineOfLifeCanvas == null)
            {
                return;
            }

            lineOfLifeCanvas.renderMode = RenderMode.WorldSpace;
            lineOfLifeCanvas.overrideSorting = true;
            lineOfLifeCanvas.sortingOrder = panelCanvasSortingOrder;

            Camera cam = lineOfLifeCamera != null ? lineOfLifeCamera : Camera.main;
            if (cam != null)
            {
                lineOfLifeCanvas.worldCamera = cam;
            }
            else
            {
                Debug.LogWarning("[LineOfLifeManager] No camera found for LineOfLifeCanvas.worldCamera.");
            }

            if (lineOfLifeCanvas.GetComponent<GraphicRaycaster>() == null)
            {
                lineOfLifeCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (lineOfLifeCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                lineOfLifeCanvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                Debug.Log("[LineOfLifeManager] Added TrackedDeviceGraphicRaycaster to LineOfLifeCanvas.");
            }
        }

        private void PlaceLineOfLifePanelInFrontOfPlayer()
        {
            if (!placeInFrontOfPlayerOnOpen)
            {
                return;
            }

            Transform target = ResolvePanelPlacementTransform();
            Transform cam = ResolvePlayerCamera();
            if (target == null || cam == null)
            {
                Debug.LogWarning("[LineOfLifeManager] Cannot place panel in front of player; missing canvas or camera.");
                return;
            }

            target.position = cam.position
                + cam.forward * panelDistanceFromCamera
                + cam.up * panelHeightOffset;

            if (panelFaceCamera)
            {
                Vector3 dir = target.position - cam.position;
                if (panelLockYOnly)
                {
                    dir.y = 0f;
                }

                if (dir.sqrMagnitude > 0.001f)
                {
                    target.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
            }

            if (panelFlip180)
            {
                target.Rotate(0f, 180f, 0f, Space.Self);
            }
        }

        private Transform ResolvePanelPlacementTransform()
        {
            if (lineOfLifeCanvas != null)
            {
                return lineOfLifeCanvas.transform;
            }

            if (lineOfLifePanel != null)
            {
                return lineOfLifePanel.transform;
            }

            return null;
        }

        private Transform ResolvePlayerCamera()
        {
            if (lineOfLifeCamera != null)
            {
                return lineOfLifeCamera.transform;
            }

            if (Camera.main != null)
            {
                return Camera.main.transform;
            }

            return null;
        }

        private void ApplyPracticeTextVisibility(bool force)
        {
            ResolvePracticeText();
            bool practice = ControllerTutorialGate.IsTutorialActive;
            if (!force && practice == _practiceTextWasActive)
            {
                return;
            }

            _practiceTextWasActive = practice;
            if (practiceText == null)
            {
                return;
            }

            practiceText.gameObject.SetActive(practice);
        }

        private void ResolvePracticeText()
        {
            if (practiceText != null)
            {
                return;
            }

            Transform searchRoot = lineOfLifePanel != null
                ? lineOfLifePanel.transform
                : (lineOfLifeCanvas != null ? lineOfLifeCanvas.transform : transform);
            Transform found = FindChildByName(searchRoot, "practiceText", "PracticeText");
            if (found == null && lineOfLifeCanvas != null && searchRoot != lineOfLifeCanvas.transform)
            {
                found = FindChildByName(lineOfLifeCanvas.transform, "practiceText", "PracticeText");
            }

            if (found != null)
            {
                practiceText = found.GetComponent<TMP_Text>();
                if (practiceText == null)
                {
                    practiceText = found.GetComponentInChildren<TMP_Text>(true);
                }
            }
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

        public void RefreshCards()
        {
            ApplyGeneratedTransitionFlowLayout();

            ClearContainer(unclassifiedCardContainer);
            ClearContainer(pastDropZone != null ? pastDropZone.cardContainer : null);
            if (!useGeneratedTransitionFlow)
            {
                ClearContainer(transitionDropZone != null ? transitionDropZone.cardContainer : null);
            }

            ClearContainer(presentDropZone != null ? presentDropZone.cardContainer : null);

            if (anchorManager == null)
            {
                Debug.LogWarning("[LineOfLifeManager] anchorManager is not assigned.");
                return;
            }

            if (memoryCardPrefab == null)
            {
                Debug.LogWarning("[LineOfLifeManager] memoryCardPrefab is not assigned.");
                return;
            }

            List<MemoryAnchorData> allData = anchorManager.GetAnnotatedAnchorData();
            if (allData == null || allData.Count == 0)
            {
                Debug.Log("[LineOfLifeManager] No annotated anchors found.");
                return;
            }

            foreach (MemoryAnchorData data in allData)
            {
                if (data == null)
                {
                    continue;
                }

                Transform container = GetContainerForCategory(data.timeCategory);
                if (container == null)
                {
                    Debug.LogWarning("[LineOfLifeManager] No container for category: " + data.timeCategory);
                    continue;
                }

                CreateCard(data, container);
            }
        }

        public void SetAnchorCategory(string id, TimeCategory category)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning("[LineOfLifeManager] SetAnchorCategory called with empty id.");
                return;
            }

            if (anchorManager == null)
            {
                Debug.LogWarning("[LineOfLifeManager] anchorManager is not assigned.");
                return;
            }

            if (anchorManager.SetTimeCategory(id, category))
            {
                Debug.Log("[LineOfLifeManager] Updated anchor " + id + " timeCategory to " + category);
            }
            else
            {
                Debug.LogWarning("[LineOfLifeManager] Could not find anchor with id: " + id);
            }
        }

        public void RegisterCardDrop(MemoryCardUI card, TimeCategory category, Transform container)
        {
            if (card == null || card.Data == null)
            {
                return;
            }

            if (useGeneratedTransitionFlow && category == TimeCategory.Transition)
            {
                Debug.Log("[GeneratedTransitionFlow] Ignore Transition drop; use Uncertain / unclassified instead.");
                category = TimeCategory.Unclassified;
                container = unclassifiedCardContainer != null ? unclassifiedCardContainer : container;
            }

            if (container == null)
            {
                Debug.LogError("[MemoryCardDrag] ERROR parent null / card inactive card=" + card.Data.id);
                card.RestoreToOriginal();
                return;
            }

            card.SetParentAndReset(container);
            if (card.transform.parent != container)
            {
                Debug.LogError("[MemoryCardDrag] ERROR parent null / card inactive card=" + card.Data.id);
                card.RestoreToOriginal();
                return;
            }

            SetAnchorCategory(card.Data.id, category);
            card.NotifyDropAccepted();
        }

        public Transform GetFallbackCardContainer()
        {
            return unclassifiedCardContainer;
        }

        public void HandleCardDropped(MemoryCardUI card)
        {
            if (card == null)
            {
                return;
            }

            // Successful drops are applied in RegisterCardDrop / DropZone.
            // Invalid drops are restored by MemoryCardUI.OnEndDrag.
        }

        public void DebugLoadAnchorsForTimeline()
        {
            if (anchorManager == null)
            {
                Debug.LogWarning("[LineOfLifeManager] anchorManager is not assigned.");
                return;
            }

            List<MemoryAnchorData> allData = anchorManager.GetAnnotatedAnchorData();
            if (allData == null || allData.Count == 0)
            {
                Debug.Log("[LineOfLifeManager] No annotated anchors found.");
                return;
            }

            Debug.Log("[LineOfLifeManager] ===== Timeline Anchor Debug =====");
            Debug.Log("[LineOfLifeManager] Anchor Count: " + allData.Count);

            for (int i = 0; i < allData.Count; i++)
            {
                MemoryAnchorData data = allData[i];
                if (data == null)
                {
                    continue;
                }

                Debug.Log(
                    "[LineOfLifeManager] Anchor " + (i + 1)
                    + " | id=" + data.id
                    + " | memoryType=" + data.memoryType
                    + " | userText=" + data.userText
                    + " | timeCategory=" + data.timeCategory
                    + " | isAnnotated=" + data.isAnnotated);
            }
        }

        public string BuildLineOfLifeSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("你整理了以下回憶：");
            sb.AppendLine();

            if (anchorManager == null)
            {
                AppendSummarySection(sb, "【過往自己】", null);
                AppendSummarySection(sb, "【過渡中的自己】", null);
                AppendSummarySection(sb, "【現在的自己】", null);
                AppendSummarySection(sb, "【未分類】", null);
                return sb.ToString().TrimEnd();
            }

            List<MemoryAnchorData> allData = anchorManager.GetAnnotatedAnchorData();
            if (allData == null)
            {
                allData = new List<MemoryAnchorData>();
            }

            var past = new List<MemoryAnchorData>();
            var transition = new List<MemoryAnchorData>();
            var present = new List<MemoryAnchorData>();
            var unclassified = new List<MemoryAnchorData>();

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
                    default:
                        unclassified.Add(data);
                        break;
                }
            }

            AppendSummarySection(sb, "【過往自己】", past);
            AppendSummarySection(sb, "【過渡中的自己】", transition);
            AppendSummarySection(sb, "【現在的自己】", present);
            AppendSummarySection(sb, "【未分類】", unclassified);

            return sb.ToString().TrimEnd();
        }

        public void CompleteLineOfLife()
        {
            Debug.Log("[LineOfLifeManager] ===== Line of Life Completed =====");

            if (anchorManager == null)
            {
                Debug.LogWarning("[LineOfLifeManager] CompleteLineOfLife: anchorManager is not assigned.");
                LogCompletedCategoryHeader("Past");
                Debug.Log("(none)");
                LogCompletedCategoryHeader("Transition");
                Debug.Log("(none)");
                LogCompletedCategoryHeader("Present");
                Debug.Log("(none)");
                LogCompletedCategoryHeader("Unclassified");
                Debug.Log("(none)");

                if (TryOpenLineOfLife3DView())
                {
                    return;
                }

                ApplyReflectionSummaryAfterComplete();
                return;
            }

            List<MemoryAnchorData> allData = anchorManager.GetAnnotatedAnchorData();
            if (allData == null)
            {
                allData = new List<MemoryAnchorData>();
            }

            var past = new List<MemoryAnchorData>();
            var transition = new List<MemoryAnchorData>();
            var present = new List<MemoryAnchorData>();
            var unclassified = new List<MemoryAnchorData>();

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
                    default:
                        unclassified.Add(data);
                        break;
                }
            }

            LogCompletedCategory("Past", past);
            if (!useGeneratedTransitionFlow)
            {
                LogCompletedCategory("Transition", transition);
            }
            else
            {
                Debug.Log("[GeneratedTransitionFlow] Skip requiring Transition cards at 2D complete.");
            }

            LogCompletedCategory("Present", present);
            LogCompletedCategory("Unclassified", unclassified);

            if (useGeneratedTransitionFlow)
            {
                if (past.Count < 1 || present.Count < 1)
                {
                    Debug.LogWarning(
                        "[GeneratedTransitionFlow] Need at least one Past and one Present card before opening 3D timeline. "
                        + "Past=" + past.Count + " Present=" + present.Count);
                    return;
                }
            }

            if (TryOpenLineOfLife3DView())
            {
                return;
            }

            ApplyReflectionSummaryAfterComplete();
        }

        private bool TryOpenLineOfLife3DView()
        {
            if (lineOfLife3DViewController == null)
            {
                return false;
            }

            if (hideLineOfLifeOnComplete && lineOfLifePanel != null)
            {
                lineOfLifePanel.SetActive(false);
            }

            lineOfLife3DViewController.Open();
            return true;
        }

        private void ApplyReflectionSummaryAfterComplete()
        {
            string summary = BuildLineOfLifeSummary();

            if (reflectionSummaryController != null)
            {
                if (hideLineOfLifeOnComplete && lineOfLifePanel != null)
                {
                    lineOfLifePanel.SetActive(false);
                }

                reflectionSummaryController.Open(summary);
            }
            else
            {
                Debug.LogWarning("[LineOfLifeManager] reflectionSummaryController is not assigned.");
            }
        }

        private static void AppendSummarySection(StringBuilder sb, string sectionTitle, List<MemoryAnchorData> items)
        {
            sb.AppendLine(sectionTitle);
            if (items == null || items.Count == 0)
            {
                sb.AppendLine("（無）");
                sb.AppendLine();
                return;
            }

            foreach (MemoryAnchorData data in items)
            {
                if (data == null)
                {
                    continue;
                }

                string userPart = string.IsNullOrEmpty(data.userText)
                    ? "（尚未輸入文字）"
                    : data.userText;
                sb.Append("- ");
                sb.Append(MemoryTypeDisplay.GetDisplayName(data.memoryType));
                sb.Append(": ");
                sb.AppendLine(userPart);
            }

            sb.AppendLine();
        }

        private static void LogCompletedCategoryHeader(string label)
        {
            Debug.Log("[LineOfLifeManager] " + label + ":");
        }

        private static void LogCompletedCategory(string label, List<MemoryAnchorData> items)
        {
            LogCompletedCategoryHeader(label);
            if (items == null || items.Count == 0)
            {
                Debug.Log("(none)");
                return;
            }

            foreach (MemoryAnchorData data in items)
            {
                if (data == null)
                {
                    continue;
                }

                Debug.Log("- " + FormatAnchorSummary(data));
            }
        }

        private static string FormatAnchorSummary(MemoryAnchorData data)
        {
            if (data == null)
            {
                return "未知：（尚未輸入文字）";
            }

            string shortUserText = string.IsNullOrEmpty(data.userText)
                ? "（尚未輸入文字）"
                : (data.userText.Length > 30 ? data.userText.Substring(0, 30) + "..." : data.userText);

            return MemoryTypeDisplay.GetDisplayName(data.memoryType) + ": " + shortUserText;
        }

        private void CreateCard(MemoryAnchorData data, Transform container)
        {
            GameObject cardObject = Instantiate(memoryCardPrefab, container);
            cardObject.transform.SetParent(container, false);

            RectTransform rect = cardObject.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                rect.anchoredPosition3D = Vector3.zero;
                rect.localPosition = Vector3.zero;
            }

            MemoryCardUI card = cardObject.GetComponent<MemoryCardUI>();
            if (card == null)
            {
                Debug.LogWarning("[LineOfLifeManager] memoryCardPrefab is missing MemoryCardUI component.");
                Destroy(cardObject);
                return;
            }

            card.Initialize(data, this, lineOfLifeCanvas, memoryCardDetailPanel);
            card.SetParentAndReset(container);
        }

        public void ApplyGeneratedTransitionFlowLayout()
        {
            if (transitionDropZone == null)
            {
                return;
            }

            transitionDropZone.gameObject.SetActive(!useGeneratedTransitionFlow);
        }

        private Transform GetContainerForCategory(TimeCategory category)
        {
            if (useGeneratedTransitionFlow && category == TimeCategory.Transition)
            {
                return unclassifiedCardContainer;
            }

            switch (category)
            {
                case TimeCategory.Past:
                    return pastDropZone != null ? pastDropZone.cardContainer : null;
                case TimeCategory.Transition:
                    return transitionDropZone != null ? transitionDropZone.cardContainer : null;
                case TimeCategory.Present:
                    return presentDropZone != null ? presentDropZone.cardContainer : null;
                case TimeCategory.Unclassified:
                default:
                    return unclassifiedCardContainer;
            }
        }

        private static void ClearContainer(Transform container)
        {
            if (container == null)
            {
                return;
            }

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }
    }
}
