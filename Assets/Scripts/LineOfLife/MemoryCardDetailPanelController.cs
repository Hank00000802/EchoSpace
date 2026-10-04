using EchoSpace.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EchoSpace.LineOfLife
{
    public class MemoryCardDetailPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text contentText;
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private Button closeButton;
        [SerializeField] private ScrollRect contentScrollRect;

        [Header("3D Placement (optional)")]
        [Tooltip("Enable for the 3D timeline detail panel so it appears in front of the player.")]
        [SerializeField] private bool placeInFrontOfPlayer = false;
        [SerializeField] private Transform playerCamera;
        [SerializeField] private float distanceFromCamera = 1.2f;
        [SerializeField] private float verticalOffset = -0.1f;
        [SerializeField] private bool faceCamera = true;
        [SerializeField] private bool lockYOnly = true;
        [SerializeField] private bool flip180 = true;

        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            EnsureUiRaycastSetup();

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
        }

        public void Open(MemoryAnchorData data)
        {
            if (panelRoot == null)
            {
                Debug.LogWarning("[MemoryCardDetail] panelRoot is not assigned.");
                return;
            }

            string fullContent = data != null && !string.IsNullOrEmpty(data.userText)
                ? data.userText
                : "（尚未輸入文字）";

            if (titleText != null)
            {
                titleText.text = "記憶詳情";
            }

            if (typeText != null)
            {
                typeText.text = data != null
                    ? MemoryTypeDisplay.GetDisplayName(data.memoryType)
                    : string.Empty;
            }

            if (phaseText != null)
            {
                phaseText.text = data != null
                    ? GetPhaseDisplayName(data.timeCategory)
                    : string.Empty;
            }

            if (contentText != null)
            {
                contentText.text = fullContent;
            }

            EnsureUiRaycastSetup();

            if (placeInFrontOfPlayer)
            {
                PlacePanelInFrontOfPlayer();
            }

            // Activate first so Viewport width is valid for preferredHeight.
            panelRoot.SetActive(true);
            RefreshScrollableContent();

            int length = data != null && data.userText != null ? data.userText.Length : 0;
            Debug.Log("[MemoryCardDetail] Open content length=" + length);
        }

        public void Close()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            Debug.Log("[MemoryCardDetail] Close");
        }

        /// <summary>
        /// Match AnnotationPanel XR UI setup: World Space canvas needs
        /// TrackedDeviceGraphicRaycaster + Event Camera for controller rays.
        /// </summary>
        private void EnsureUiRaycastSetup()
        {
            Canvas canvas = ResolveCanvas();
            if (canvas == null)
            {
                Debug.LogWarning("[MemoryCardDetail] No Canvas found for panel UI raycasts.");
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;

            EnsurePlayerCamera();
            if (playerCamera != null)
            {
                Camera cam = playerCamera.GetComponent<Camera>();
                if (cam == null)
                {
                    cam = playerCamera.GetComponentInChildren<Camera>();
                }

                if (cam != null)
                {
                    canvas.worldCamera = cam;
                }
            }
            else if (Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }

            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                Debug.Log("[MemoryCardDetail] Added TrackedDeviceGraphicRaycaster on " + canvas.name);
            }

            // Colliders make XR rays treat the panel as a 3D object (red reticle)
            // instead of UI — remove them from this panel hierarchy.
            Collider[] colliders = canvas.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    Debug.LogWarning("[MemoryCardDetail] Disabling collider on UI: " + colliders[i].name);
                    colliders[i].enabled = false;
                }
            }

            ConfigureDisplayTextRaycasts();
            ConfigureCloseButtonRaycast();
        }

        private Canvas ResolveCanvas()
        {
            if (panelRoot != null)
            {
                Canvas onRoot = panelRoot.GetComponent<Canvas>();
                if (onRoot != null)
                {
                    return onRoot;
                }

                Canvas inParent = panelRoot.GetComponentInParent<Canvas>();
                if (inParent != null)
                {
                    return inParent;
                }
            }

            return GetComponentInParent<Canvas>();
        }

        private void ConfigureDisplayTextRaycasts()
        {
            // Text should not steal clicks from Close / Scroll.
            SetRaycastTarget(titleText, false);
            SetRaycastTarget(typeText, false);
            SetRaycastTarget(phaseText, false);
            SetRaycastTarget(contentText, false);

            if (panelRoot == null)
            {
                return;
            }

            TMP_Text[] texts = panelRoot.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null)
                {
                    continue;
                }

                if (text.GetComponentInParent<Button>() != null)
                {
                    // Keep button label from blocking the button image.
                    text.raycastTarget = false;
                    continue;
                }

                text.raycastTarget = false;
            }
        }

        private void ConfigureCloseButtonRaycast()
        {
            if (closeButton == null)
            {
                return;
            }

            closeButton.interactable = true;

            Image image = closeButton.GetComponent<Image>();
            if (image == null)
            {
                image = closeButton.gameObject.AddComponent<Image>();
            }

            image.raycastTarget = true;
            closeButton.targetGraphic = image;

            TMP_Text label = closeButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.raycastTarget = false;
            }
        }

        private static void SetRaycastTarget(Graphic graphic, bool enabled)
        {
            if (graphic != null)
            {
                graphic.raycastTarget = enabled;
            }
        }

        private void PlacePanelInFrontOfPlayer()
        {
            EnsurePlayerCamera();
            if (playerCamera == null)
            {
                Debug.LogWarning("[MemoryCardDetail] playerCamera is not assigned and Camera.main is null; panel stays at original position.");
                return;
            }

            // Move panelRoot only — DeatailCanva uses a large World Space parent
            // with a tiny-scaled child; moving the Canvas would blow up the size.
            Transform moveTarget = panelRoot.transform;

            Vector3 targetPos = playerCamera.position + playerCamera.forward * distanceFromCamera;
            targetPos += Vector3.up * verticalOffset;
            moveTarget.position = targetPos;

            if (faceCamera)
            {
                Vector3 dir = moveTarget.position - playerCamera.position;
                if (lockYOnly)
                {
                    dir.y = 0f;
                }

                if (dir.sqrMagnitude > 0.001f)
                {
                    moveTarget.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
            }

            if (flip180)
            {
                moveTarget.Rotate(0f, 180f, 0f, Space.Self);
            }

            Debug.Log("[MemoryCardDetail] Panel placed at " + moveTarget.position);
        }

        private void EnsurePlayerCamera()
        {
            if (playerCamera == null && Camera.main != null)
            {
                playerCamera = Camera.main.transform;
            }
        }

        /// <summary>
        /// Grow ContentText + ScrollRect Content to preferred text height so long
        /// notes can scroll instead of being clipped by the Viewport mask.
        /// </summary>
        private void RefreshScrollableContent()
        {
            if (contentText == null)
            {
                ResetScrollToTop();
                return;
            }

            const float sidePadding = 8f;
            const float verticalPadding = 8f;
            const float minHeight = 50f;

            contentText.enableWordWrapping = true;
            contentText.overflowMode = TextOverflowModes.Overflow;
            contentText.alignment = TextAlignmentOptions.TopLeft;
            contentText.margin = Vector4.zero;

            RectTransform textRect = contentText.rectTransform;
            textRect.localScale = Vector3.one;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -verticalPadding);
            textRect.sizeDelta = new Vector2(-(sidePadding * 2f), minHeight);

            Canvas.ForceUpdateCanvases();
            contentText.ForceMeshUpdate();

            float preferredHeight = contentText.preferredHeight;
            float textHeight = Mathf.Max(preferredHeight, minHeight);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);

            if (contentScrollRect != null)
            {
                contentScrollRect.horizontal = false;
                contentScrollRect.vertical = true;

                RectTransform contentRect = contentScrollRect.content;
                if (contentRect != null)
                {
                    float contentHeight = textHeight + (verticalPadding * 2f);
                    contentRect.anchorMin = new Vector2(0f, 1f);
                    contentRect.anchorMax = new Vector2(1f, 1f);
                    contentRect.pivot = new Vector2(0.5f, 1f);
                    contentRect.anchoredPosition = Vector2.zero;
                    contentRect.sizeDelta = new Vector2(0f, contentHeight);
                }
            }

            Canvas.ForceUpdateCanvases();
            ResetScrollToTop();
        }

        private void ResetScrollToTop()
        {
            if (contentScrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            contentScrollRect.verticalNormalizedPosition = 1f;
            contentScrollRect.velocity = Vector2.zero;
        }

        private static string GetPhaseDisplayName(TimeCategory category)
        {
            switch (category)
            {
                case TimeCategory.Past:
                    return "階段：過往自己";
                case TimeCategory.Transition:
                    return "階段：過渡";
                case TimeCategory.Present:
                    return "階段：現在的自己";
                case TimeCategory.Unclassified:
                default:
                    return "階段：未分類";
            }
        }
    }
}
