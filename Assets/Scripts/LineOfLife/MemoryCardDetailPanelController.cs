using EchoSpace.Core;
using EchoSpace.UI;
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
        [Tooltip("Must be above LineOfLifeCanvas (1200) and 3D timeline / pair instruction (2600).")]
        [SerializeField] private int panelSortingOrder = 2800;

        [Header("Layout (resized panel support)")]
        [Tooltip("The panel was resized by non-uniform Scale on Title / Type / Scroll View. " +
                 "Bake that scale into Width/Height (and font size) at runtime so text wraps and scrolls correctly.")]
        [SerializeField] private bool normalizeNonUniformScale = true;
        [Tooltip("Content text font size. 0 = auto (keeps the previous on-screen size after baking scale).")]
        [SerializeField] private float contentFontSize = 0f;
        [Tooltip("Width of the vertical scrollbar in panel units (bigger = easier to hit with the ray).")]
        [SerializeField] private float scrollbarWidth = 60f;

        private Material _uiNoZTestMaterial;
        private const int DetailRenderQueue = 4002;

        /// <summary>True while the detail panel is shown.</summary>
        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>Unscaled time of the last Close(); lets other systems ignore the click that just closed it.</summary>
        public float LastCloseTime { get; private set; } = -100f;

        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            NormalizeDetailLayout();
            EnsureUiRaycastSetup();

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        /// <summary>
        /// Title / Type / Scroll View were resized with a non-uniform Scale (e.g. 4.57 x 2.38).
        /// TMP then lays out text in the tiny unscaled rect (very narrow wrap) and stretches it afterwards.
        /// Bake the scale into sizeDelta (and font size) so layout happens at the real size.
        /// </summary>
        private void NormalizeDetailLayout()
        {
            if (!normalizeNonUniformScale)
            {
                return;
            }

            if (contentScrollRect != null)
            {
                RectTransform scrollRect = contentScrollRect.transform as RectTransform;
                if (BakeScaleIntoSize(scrollRect, out float scrollScaleY) && contentText != null
                    && !contentText.enableAutoSizing && contentFontSize <= 0f)
                {
                    contentText.fontSize *= scrollScaleY;
                }
            }

            BakeTextScale(titleText);
            BakeTextScale(typeText);
            BakeTextScale(phaseText);

            if (contentText != null && !contentText.enableAutoSizing && contentFontSize > 0f)
            {
                contentText.fontSize = contentFontSize;
            }
        }

        private static void BakeTextScale(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            if (BakeScaleIntoSize(rect, out float scaleY) && !text.enableAutoSizing)
            {
                text.fontSize *= scaleY;
            }
        }

        /// <summary>
        /// Returns true if a non-uniform / non-1 scale was baked. Only for fixed (non-stretch) anchors.
        /// </summary>
        private static bool BakeScaleIntoSize(RectTransform rect, out float scaleY)
        {
            scaleY = 1f;
            if (rect == null)
            {
                return false;
            }

            Vector3 scale = rect.localScale;
            if (Mathf.Abs(scale.x - 1f) < 0.001f && Mathf.Abs(scale.y - 1f) < 0.001f)
            {
                return false;
            }

            if (rect.anchorMin != rect.anchorMax || scale.x <= 0.0001f || scale.y <= 0.0001f)
            {
                return false;
            }

            Vector2 size = rect.sizeDelta;
            rect.sizeDelta = new Vector2(size.x * scale.x, size.y * scale.y);
            rect.localScale = Vector3.one;
            scaleY = scale.y;
            Debug.Log("[MemoryCardDetail] Baked scale " + scale + " into size for " + rect.name
                + " -> " + rect.sizeDelta);
            return true;
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
            Open(data, null);
        }

        public void Open(MemoryAnchorData data, GeneratedTransitionCardData generated)
        {
            if (panelRoot == null)
            {
                Debug.LogWarning("[MemoryCardDetail] panelRoot is not assigned.");
                return;
            }

            string fullContent = data != null && !string.IsNullOrEmpty(data.userText)
                ? data.userText
                : "（尚未輸入文字）";
            if (generated != null && !string.IsNullOrEmpty(generated.transitionText))
            {
                fullContent = generated.transitionText;
            }

            if (generated != null)
            {
                if (titleText != null)
                {
                    titleText.text = string.IsNullOrEmpty(generated.displayTitle)
                        ? "連結卡"
                        : generated.displayTitle;
                }

                string sourcePhase = generated.pastCard != null ? "轉變前" : "現在";
                if (typeText != null)
                {
                    typeText.text = sourcePhase;
                }

                if (phaseText != null)
                {
                    phaseText.text = "階段：" + sourcePhase;
                }
            }
            else
            {
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
            }

            if (contentText != null)
            {
                EnsureContentTextReadable();
                contentText.enabled = true;
                contentText.alpha = 1f;
                contentText.gameObject.SetActive(true);
                contentText.text = fullContent;
                contentText.ForceMeshUpdate();
            }

            EnsureUiRaycastSetup();

            if (placeInFrontOfPlayer)
            {
                PlacePanelInFrontOfPlayer();
            }

            bool wasOpen = panelRoot.activeSelf;
            // Activate first so Viewport width is valid for preferredHeight.
            panelRoot.SetActive(true);
            RefreshScrollableContent();
            if (!wasOpen)
            {
                UiSfx.PlayPopup();
            }

            int length = data != null && data.userText != null ? data.userText.Length : 0;
            Debug.Log("[MemoryCardDetail] Open content length=" + length);
        }

        public void Close()
        {
            bool wasOpen = panelRoot != null && panelRoot.activeSelf;
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            if (wasOpen)
            {
                LastCloseTime = Time.unscaledTime;
                UiSfx.PlayExit();
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

            GraphicRaycaster graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
            if (graphicRaycaster != null)
            {
                graphicRaycaster.ignoreReversedGraphics = false;
                graphicRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            }

            TrackedDeviceGraphicRaycaster trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (trackedRaycaster != null)
            {
                trackedRaycaster.ignoreReversedGraphics = false;
                trackedRaycaster.checkFor3DOcclusion = false;
            }

            BringDetailCanvasToFront(canvas);

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

        private void BringDetailCanvasToFront(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.overrideSorting = true;
            if (canvas.sortingOrder < panelSortingOrder)
            {
                canvas.sortingOrder = panelSortingOrder;
            }

            Material uiMaterial = ResolveUiNoZTestMaterial();
            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || graphic is TMP_Text || IsMaskGraphic(graphic))
                {
                    continue;
                }

                if (uiMaterial != null)
                {
                    graphic.material = uiMaterial;
                }
            }

            Shader overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay");
            TMP_Text[] texts = canvas.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null)
                {
                    continue;
                }

                ApplyDetailTextMaterial(text, overlayShader, IsUnderScrollOrMask(text));
            }
        }

        private static bool IsMaskGraphic(Graphic graphic)
        {
            return graphic != null
                && (graphic.GetComponent<Mask>() != null
                    || graphic.GetComponent<RectMask2D>() != null);
        }

        private static bool IsUnderScrollOrMask(Component component)
        {
            return component != null
                && (component.GetComponentInParent<ScrollRect>() != null
                    || component.GetComponentInParent<Mask>() != null
                    || component.GetComponentInParent<RectMask2D>() != null);
        }

        private void ApplyDetailTextMaterial(TMP_Text text, Shader overlayShader, bool insideScroll)
        {
            Material fontMaterial = text.fontMaterial;
            if (fontMaterial == null)
            {
                return;
            }

            if (insideScroll)
            {
                // Keep maskable: RectMask2D clips via _ClipRect (no stencil needed).
                text.maskable = true;
                if (fontMaterial.HasProperty("_ZTestMode"))
                {
                    fontMaterial.SetFloat("_ZTestMode", 8f);
                }

                if (fontMaterial.HasProperty("_CullMode"))
                {
                    fontMaterial.SetFloat("_CullMode", 0f);
                }
            }
            else if (overlayShader != null && fontMaterial.shader != overlayShader)
            {
                fontMaterial.shader = overlayShader;
            }

            fontMaterial.renderQueue = DetailRenderQueue;
            text.ForceMeshUpdate();
        }

        private Material ResolveUiNoZTestMaterial()
        {
            if (_uiNoZTestMaterial != null)
            {
                return _uiNoZTestMaterial;
            }

            Shader shader = Shader.Find("UI/NoZTest");
            if (shader == null)
            {
                return null;
            }

            _uiNoZTestMaterial = new Material(shader);
            _uiNoZTestMaterial.name = "MemoryCardDetail-UI-NoZTest";
            _uiNoZTestMaterial.renderQueue = DetailRenderQueue;
            return _uiNoZTestMaterial;
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

        private void EnsureContentTextReadable()
        {
            if (contentText == null)
            {
                return;
            }

            TMP_Text fontSource = titleText != null && titleText.font != null
                ? titleText
                : typeText;
            if (fontSource != null && fontSource.font != null && contentText.font != fontSource.font)
            {
                contentText.font = fontSource.font;
                if (fontSource.fontSharedMaterial != null)
                {
                    contentText.fontSharedMaterial = fontSource.fontSharedMaterial;
                }
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

            NormalizeDetailLayout();
            StretchDetailViewport();
            ConfigureViewportClipping();
            EnsureVerticalScrollbarHandle();

            // Must stay maskable so RectMask2D can clip it to the Viewport.
            contentText.maskable = true;
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

            // Measure with the width the text will really have once the scrollbar takes its space
            // (ScrollRect shrinks the Viewport one frame later), so the last lines are not cut off.
            float availableWidth = GetContentTextWidth(sidePadding);
            float preferredHeight = availableWidth > 1f
                ? contentText.GetPreferredValues(contentText.text, availableWidth, 0f).y
                : contentText.preferredHeight;
            preferredHeight += contentText.fontSize * 0.75f; // bottom breathing room
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
            contentText.ForceMeshUpdate();
            ResetScrollToTop();
        }

        private float GetContentTextWidth(float sidePadding)
        {
            if (contentScrollRect == null)
            {
                return 0f;
            }

            RectTransform scrollRect = contentScrollRect.transform as RectTransform;
            if (scrollRect == null)
            {
                return 0f;
            }

            float width = scrollRect.rect.width;
            Scrollbar bar = contentScrollRect.verticalScrollbar;
            if (bar != null)
            {
                RectTransform barRect = bar.transform as RectTransform;
                float barWidth = barRect != null ? barRect.sizeDelta.x : 0f;
                width -= barWidth + contentScrollRect.verticalScrollbarSpacing;
            }

            return width - sidePadding * 2f - 2f;
        }

        private void StretchDetailViewport()
        {
            if (contentScrollRect == null)
            {
                return;
            }

            RectTransform viewport = contentScrollRect.viewport;
            if (viewport == null && contentScrollRect.transform.childCount > 0)
            {
                Transform named = contentScrollRect.transform.Find("Viewport");
                viewport = named != null
                    ? named as RectTransform
                    : contentScrollRect.transform.GetChild(0) as RectTransform;
                contentScrollRect.viewport = viewport;
            }

            if (viewport == null)
            {
                return;
            }

            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 1f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.SetActive(true);
        }

        /// <summary>
        /// The scene's "Scrollbar Vertical" has no Handle (m_HandleRect is empty and
        /// Sliding Area has no child), so clicking / dragging it does nothing.
        /// Build a handle at runtime and assign it so the scrollbar becomes interactive.
        /// </summary>
        private void EnsureVerticalScrollbarHandle()
        {
            if (contentScrollRect == null)
            {
                return;
            }

            Scrollbar bar = contentScrollRect.verticalScrollbar;
            if (bar == null)
            {
                return;
            }

            bar.interactable = true;

            // Wider track = easier to hit with the XR ray on the resized panel.
            RectTransform barRect = bar.transform as RectTransform;
            if (barRect != null && scrollbarWidth > 0f
                && Mathf.Abs(barRect.sizeDelta.x - scrollbarWidth) > 0.01f)
            {
                barRect.sizeDelta = new Vector2(scrollbarWidth, barRect.sizeDelta.y);
            }

            Image barImage = bar.GetComponent<Image>();
            if (barImage != null)
            {
                barImage.raycastTarget = true;
            }

            if (bar.handleRect != null)
            {
                Image existing = bar.handleRect.GetComponent<Image>();
                if (existing != null)
                {
                    existing.raycastTarget = true;
                }

                return;
            }

            RectTransform area = null;
            for (int i = 0; i < bar.transform.childCount; i++)
            {
                area = bar.transform.GetChild(i) as RectTransform;
                if (area != null)
                {
                    break;
                }
            }

            if (area == null)
            {
                GameObject areaObj = new GameObject("Sliding Area", typeof(RectTransform));
                area = areaObj.GetComponent<RectTransform>();
                area.SetParent(bar.transform, false);
                area.anchorMin = Vector2.zero;
                area.anchorMax = Vector2.one;
                area.offsetMin = new Vector2(10f, 10f);
                area.offsetMax = new Vector2(-10f, -10f);
            }

            Transform existingHandle = area.Find("Handle");
            RectTransform handleRect = existingHandle != null ? existingHandle as RectTransform : null;
            if (handleRect == null)
            {
                GameObject handleObj = new GameObject("Handle", typeof(RectTransform), typeof(Image));
                handleRect = handleObj.GetComponent<RectTransform>();
                handleRect.SetParent(area, false);
            }

            // Same layout Unity's default scrollbar handle uses; Scrollbar drives the anchors along its axis.
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(20f, 20f);
            handleRect.localScale = Vector3.one;

            Image handleImage = handleRect.GetComponent<Image>();
            if (handleImage == null)
            {
                handleImage = handleRect.gameObject.AddComponent<Image>();
            }

            handleImage.color = new Color(0.35f, 0.35f, 0.38f, 1f);
            handleImage.raycastTarget = true;
            Material uiMaterial = ResolveUiNoZTestMaterial();
            if (uiMaterial != null)
            {
                handleImage.material = uiMaterial;
            }

            bar.handleRect = handleRect;
            bar.targetGraphic = handleImage;
            bar.direction = Scrollbar.Direction.BottomToTop;
            Debug.Log("[MemoryCardDetail] Created missing vertical scrollbar handle.");
        }

        /// <summary>
        /// Clip the scroll content with RectMask2D (shader clip-rect, no stencil).
        /// The stencil-based Mask on Viewport hid all text when combined with the
        /// no-ZTest / overlay materials used in World Space, so it stays disabled;
        /// RectMask2D clips (and blocks raycasts of) anything outside the Viewport.
        /// </summary>
        private void ConfigureViewportClipping()
        {
            if (contentScrollRect == null)
            {
                return;
            }

            Mask[] masks = contentScrollRect.GetComponentsInChildren<Mask>(true);
            for (int i = 0; i < masks.Length; i++)
            {
                if (masks[i] != null)
                {
                    masks[i].enabled = false;
                }
            }

            RectTransform viewport = contentScrollRect.viewport;
            if (viewport == null)
            {
                Debug.LogWarning("[MemoryCardDetail] Scroll viewport missing; cannot clip content.");
                return;
            }

            RectMask2D rectMask = viewport.GetComponent<RectMask2D>();
            if (rectMask == null)
            {
                rectMask = viewport.gameObject.AddComponent<RectMask2D>();
            }

            rectMask.enabled = true;
            rectMask.padding = Vector4.zero;
            rectMask.softness = Vector2Int.zero;

            // Any other RectMask2D inside the scroll hierarchy (e.g. on the Scroll View root)
            // would clip against the wrong rect; keep only the Viewport one active.
            RectMask2D[] rectMasks = contentScrollRect.GetComponentsInChildren<RectMask2D>(true);
            for (int i = 0; i < rectMasks.Length; i++)
            {
                if (rectMasks[i] != null && rectMasks[i] != rectMask)
                {
                    rectMasks[i].enabled = false;
                }
            }
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
