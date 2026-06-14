using System.Collections.Generic;
using System.Text;
using EchoSpace.Annotation;
using EchoSpace.Core;
using EchoSpace.Reflection;
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
        public Transform unclassifiedCardContainer;
        public LineOfLifeDropZone pastDropZone;
        public LineOfLifeDropZone transitionDropZone;
        public LineOfLifeDropZone presentDropZone;
        public Button finishButton;
        public ReflectionSummaryController reflectionSummaryController;
        public LineOfLife3DViewController lineOfLife3DViewController;
        public bool hideLineOfLifeOnComplete = true;
        public KeyCode toggleKey = KeyCode.L;
        public bool enableToggleHotkey = true;
        [Header("XR UI")]
        [Tooltip("XR main camera for World Space canvas raycasts. Falls back to Camera.main.")]
        public Camera lineOfLifeCamera;

        [Header("XR Controller Toggle")]
        public bool enableXRControllerToggle = true;
        public bool useRightAButtonForLineOfLifePanel = true;
        private bool _rightAWasPressed;

        public bool IsOpen =>
            lineOfLifePanel != null && lineOfLifePanel.activeSelf;

        private void Awake()
        {
            if (finishButton != null)
            {
                finishButton.onClick.AddListener(CompleteLineOfLife);
            }

            EnsureLineOfLifeCanvasForXR();
        }

        private void Update()
        {
            if (enableToggleHotkey && Input.GetKeyDown(toggleKey))
            {
                TogglePanel();
            }

            if (enableXRControllerToggle && useRightAButtonForLineOfLifePanel && GetRightPrimaryButtonDown())
            {
                Debug.Log("[LineOfLifeManager] Right A button toggle LineOfLifePanel.");
                TogglePanel();
            }
        }

        private bool GetRightPrimaryButtonDown()
        {
            bool pressed = GetRightControllerButton(CommonUsages.primaryButton);
            bool down = pressed && !_rightAWasPressed;
            _rightAWasPressed = pressed;
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
                RefreshCards();
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

        public void RefreshCards()
        {
            ClearContainer(unclassifiedCardContainer);
            ClearContainer(pastDropZone != null ? pastDropZone.cardContainer : null);
            ClearContainer(transitionDropZone != null ? transitionDropZone.cardContainer : null);
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

            SetAnchorCategory(card.Data.id, category);
            card.SetParentAndReset(container);
            card.NotifyDropAccepted();
        }

        public void HandleCardDropped(MemoryCardUI card)
        {
            if (card == null)
            {
                return;
            }

            // DropZone.RegisterCardDrop handles successful placement; this is a fallback hook.
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
            sb.AppendLine("你剛剛整理了以下記憶：");
            sb.AppendLine();

            if (anchorManager == null)
            {
                AppendSummarySection(sb, "【過去的我】", null);
                AppendSummarySection(sb, "【轉變中的我】", null);
                AppendSummarySection(sb, "【現在的我】", null);
                AppendSummarySection(sb, "【尚未分類】", null);
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

            AppendSummarySection(sb, "【過去的我】", past);
            AppendSummarySection(sb, "【轉變中的我】", transition);
            AppendSummarySection(sb, "【現在的我】", present);
            AppendSummarySection(sb, "【尚未分類】", unclassified);

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
            LogCompletedCategory("Transition", transition);
            LogCompletedCategory("Present", present);
            LogCompletedCategory("Unclassified", unclassified);

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
                    ? "(尚未輸入文字)"
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
                return "Unknown: (尚未輸入文字)";
            }

            string shortUserText = string.IsNullOrEmpty(data.userText)
                ? "(尚未輸入文字)"
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

            card.Initialize(data, this, lineOfLifeCanvas);
            card.SetParentAndReset(container);
        }

        private Transform GetContainerForCategory(TimeCategory category)
        {
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
