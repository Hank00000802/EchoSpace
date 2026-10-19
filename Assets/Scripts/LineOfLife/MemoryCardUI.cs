using EchoSpace.Core;
using EchoSpace.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoSpace.LineOfLife
{
    public class MemoryCardUI : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler, IPointerDownHandler
    {
        public TMP_Text typeText;
        public TMP_Text contentText;
        public Canvas parentCanvas;
        [Tooltip("Optional drag root. Falls back to lineOfLifeCanvas transform when null.")]
        public Transform dragRoot;

        private const float DragAlpha = 0.85f;
        private const float NormalAlpha = 1f;
        private const float ClickMoveThresholdPixels = 12f;

        private MemoryAnchorData _data;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Transform _originalParent;
        private int _originalSiblingIndex;
        private Vector2 _originalAnchoredPosition;
        private TimeCategory _originalCategory;
        private LineOfLifeManager _lineOfLifeManager;
        private MemoryCardDetailPanelController _detailPanel;
        private bool _dropAccepted;
        private Vector3 _pointerWorldOffset;
        private RectTransform _dragPlane;
        private ScrollRect _parentScrollRect;
        private bool _scrollRectWasEnabled;
        private bool _loggedDragPosition;
        private bool _isDragging;
        private bool _suppressClickAfterDrag;
        private Vector2 _pointerDownPosition;
        private Transform _dragParent;

        public MemoryAnchorData Data => _data;

        public bool DropAccepted => _dropAccepted;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnDisable()
        {
            RestoreScrollRect();
            RestoreCanvasGroup();

            if (_isDragging)
            {
                Debug.Log(
                    "[MemoryCardDrag] OnDisable while dragging card=" + GetCardLabel()
                    + " parent=" + GetTransformName(transform.parent));
            }
        }

        private void OnDestroy()
        {
            Debug.Log(
                "[MemoryCardDrag] OnDestroy card=" + GetCardLabel()
                + " parent=" + GetTransformName(transform.parent)
                + " active=" + gameObject.activeSelf);
        }

        public void Initialize(
            MemoryAnchorData anchorData,
            LineOfLifeManager manager,
            Canvas canvas,
            MemoryCardDetailPanelController detailPanel = null)
        {
            _data = anchorData;
            _lineOfLifeManager = manager;
            parentCanvas = canvas;
            _detailPanel = detailPanel;
            _dragPlane = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;

            if (typeText != null)
            {
                typeText.text = anchorData != null
                    ? MemoryTypeDisplay.GetDisplayName(anchorData.memoryType)
                    : string.Empty;
            }

            if (contentText != null)
            {
                // Preview only — never mutate original MemoryAnchorData.
                contentText.text = GetDisplayContent(anchorData);
            }

            ApplyFixedLayoutElement();
        }

        public void SetDetailPanel(MemoryCardDetailPanelController detailPanel)
        {
            _detailPanel = detailPanel;
        }

        public void SetParentAndReset(Transform newParent)
        {
            if (newParent == null || _rectTransform == null)
            {
                Debug.LogError("[MemoryCardDrag] ERROR parent null / card inactive card=" + GetCardLabel());
                return;
            }

            gameObject.SetActive(true);
            transform.SetParent(newParent, false);
            ResetLocalRect();
            ApplyFixedLayoutElement();
            RestoreCanvasGroup();
            RefreshContainerLayout(newParent);
        }

        public void ReturnToOriginalPosition()
        {
            RestoreToOriginal();
        }

        public void RestoreToOriginal()
        {
            Transform targetParent = ResolveRestoreParent();
            if (targetParent == null || _rectTransform == null)
            {
                Debug.LogError("[MemoryCardDrag] ERROR parent null / card inactive card=" + GetCardLabel());
                EnsureCardVisible();
                RestoreCanvasGroup();
                return;
            }

            gameObject.SetActive(true);
            transform.SetParent(targetParent, false);

            int maxIndex = Mathf.Max(0, targetParent.childCount - 1);
            transform.SetSiblingIndex(Mathf.Clamp(_originalSiblingIndex, 0, maxIndex));

            ResetLocalRect();
            _rectTransform.anchoredPosition = _originalAnchoredPosition;
            _rectTransform.anchoredPosition3D = new Vector3(
                _originalAnchoredPosition.x,
                _originalAnchoredPosition.y,
                0f);

            ApplyFixedLayoutElement();
            RestoreCanvasGroup();
            RefreshContainerLayout(targetParent);
            EnsureCardVisible();

            Debug.Log(
                "[MemoryCardDrag] Restore complete parent=" + GetTransformName(transform.parent)
                + " card=" + GetCardLabel()
                + " sibling=" + transform.GetSiblingIndex());
        }

        public void NotifyDropAccepted()
        {
            _dropAccepted = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            _pointerDownPosition = eventData.position;
            _suppressClickAfterDrag = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            if (_isDragging || _suppressClickAfterDrag)
            {
                return;
            }

            float moveDistance = Vector2.Distance(_pointerDownPosition, eventData.position);
            if (moveDistance > ClickMoveThresholdPixels)
            {
                return;
            }

            UiSfx.PlayClick();
            OpenDetailPanel();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null || _rectTransform == null || _canvasGroup == null)
            {
                return;
            }

            _isDragging = true;
            _suppressClickAfterDrag = true;
            _dropAccepted = false;
            _loggedDragPosition = false;
            CaptureOriginalState();

            Debug.Log(
                "[MemoryCardDrag] BeginDrag card=" + GetCardLabel()
                + ", originalParent=" + GetTransformName(_originalParent)
                + ", sibling=" + _originalSiblingIndex
                + ", zone=" + _originalCategory);

            if (parentCanvas == null)
            {
                parentCanvas = GetComponentInParent<Canvas>();
            }

            if (_dragPlane == null && parentCanvas != null)
            {
                _dragPlane = parentCanvas.GetComponent<RectTransform>();
            }

            if (_dragPlane == null)
            {
                Debug.LogWarning("[MemoryCardUI] dragPlane is null, cannot begin drag.");
                RestoreCanvasGroup();
                _isDragging = false;
                return;
            }

            Camera eventCamera = GetEventCamera(eventData);
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    _dragPlane,
                    eventData.position,
                    eventCamera,
                    out Vector3 pointerWorldPosition))
            {
                Debug.LogWarning("[MemoryCardUI] Failed to get pointer world position on begin drag.");
                RestoreCanvasGroup();
                _isDragging = false;
                return;
            }

            _pointerWorldOffset = _rectTransform.position - pointerWorldPosition;

            // Disable the source ScrollRect before reparenting, otherwise leftover
            // content offset + RectMask2D can hide the card after drop/restore.
            _parentScrollRect = GetComponentInParent<ScrollRect>();
            if (_parentScrollRect != null)
            {
                _scrollRectWasEnabled = _parentScrollRect.enabled;
                _parentScrollRect.enabled = false;
            }

            _dragParent = ResolveDragParent();
            if (_dragParent != null)
            {
                transform.SetParent(_dragParent, true);
                transform.SetAsLastSibling();
            }

            _canvasGroup.alpha = DragAlpha;
            _canvasGroup.blocksRaycasts = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || _rectTransform == null || _dragPlane == null)
            {
                return;
            }

            _isDragging = true;
            _suppressClickAfterDrag = true;

            Camera eventCamera = GetEventCamera(eventData);
            bool success = RectTransformUtility.ScreenPointToWorldPointInRectangle(
                _dragPlane,
                eventData.position,
                eventCamera,
                out Vector3 pointerWorldPosition);

            if (success)
            {
                _rectTransform.position = pointerWorldPosition + _pointerWorldOffset;

                if (!_loggedDragPosition)
                {
                    Debug.Log("[MemoryCardUI] Drag position update: " + _rectTransform.position);
                    _loggedDragPosition = true;
                }
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;
            _suppressClickAfterDrag = true;

            RestoreScrollRect();
            RestoreCanvasGroup();

            if (!_dropAccepted)
            {
                TryAcceptDropFromPointer(eventData);
            }

            if (_lineOfLifeManager != null)
            {
                _lineOfLifeManager.HandleCardDropped(this);
            }

            if (!_dropAccepted)
            {
                Debug.Log("[MemoryCardDrag] Drop invalid, restore card=" + GetCardLabel());
                RestoreToOriginal();
            }
            else
            {
                RestoreCanvasGroup();
                RefreshContainerLayout(transform.parent);
            }

            ValidateCardVisible();
            Debug.Log("[MemoryCardUI] End drag");
        }

        private void OpenDetailPanel()
        {
            if (_detailPanel == null)
            {
                Debug.LogWarning("[MemoryCardUI] detailPanel is not assigned.");
                return;
            }

            _detailPanel.Open(_data);
        }

        private void CaptureOriginalState()
        {
            _originalParent = transform.parent;
            _originalSiblingIndex = transform.GetSiblingIndex();
            _originalAnchoredPosition = _rectTransform != null
                ? _rectTransform.anchoredPosition
                : Vector2.zero;
            _originalCategory = _data != null ? _data.timeCategory : TimeCategory.Unclassified;
        }

        private void TryAcceptDropFromPointer(PointerEventData eventData)
        {
            if (eventData == null || _lineOfLifeManager == null)
            {
                return;
            }

            LineOfLifeDropZone zone = FindDropZone(eventData.pointerEnter);
            if (zone == null)
            {
                zone = FindDropZone(eventData.pointerCurrentRaycast.gameObject);
            }

            if (zone == null)
            {
                return;
            }

            Transform targetContainer = zone.cardContainer != null ? zone.cardContainer : zone.transform;
            _lineOfLifeManager.RegisterCardDrop(this, zone.category, targetContainer);
            if (_dropAccepted)
            {
                Debug.Log(
                    "[MemoryCardDrag] Drop valid zone=" + zone.category
                    + " (end-drag fallback) card=" + GetCardLabel());
            }
        }

        private static LineOfLifeDropZone FindDropZone(GameObject source)
        {
            return source != null ? source.GetComponentInParent<LineOfLifeDropZone>() : null;
        }

        private Transform ResolveRestoreParent()
        {
            if (_originalParent != null)
            {
                return _originalParent;
            }

            Transform fallback = _lineOfLifeManager != null
                ? _lineOfLifeManager.GetFallbackCardContainer()
                : null;

            if (fallback != null)
            {
                Debug.LogError(
                    "[MemoryCardDrag] ERROR parent null / card inactive, fallback="
                    + GetTransformName(fallback)
                    + " card=" + GetCardLabel());
            }

            return fallback;
        }

        private void EnsureCardVisible()
        {
            if (!gameObject.activeSelf)
            {
                Debug.LogError("[MemoryCardDrag] ERROR parent null / card inactive card=" + GetCardLabel());
                gameObject.SetActive(true);
            }

            if (transform.parent == null)
            {
                Debug.LogError("[MemoryCardDrag] ERROR parent null / card inactive card=" + GetCardLabel());
                Transform fallback = ResolveRestoreParent();
                if (fallback != null)
                {
                    transform.SetParent(fallback, false);
                    ResetLocalRect();
                }
            }
        }

        private void ValidateCardVisible()
        {
            bool parentMissing = transform.parent == null;
            bool stuckOnDragRoot = !_dropAccepted && _dragParent != null && transform.parent == _dragParent;

            if (!parentMissing && gameObject.activeSelf && !stuckOnDragRoot)
            {
                return;
            }

            Debug.LogError("[MemoryCardDrag] ERROR parent null / card inactive card=" + GetCardLabel());

            if (_dropAccepted)
            {
                EnsureCardVisible();
                RestoreCanvasGroup();
                return;
            }

            RestoreToOriginal();
        }

        private void RestoreCanvasGroup()
        {
            if (_canvasGroup == null)
            {
                return;
            }

            _canvasGroup.alpha = NormalAlpha;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
        }

        private void RestoreScrollRect()
        {
            if (_parentScrollRect != null)
            {
                _parentScrollRect.enabled = _scrollRectWasEnabled;
                _parentScrollRect = null;
            }
        }

        private void ResetLocalRect()
        {
            if (_rectTransform == null)
            {
                return;
            }

            _rectTransform.localScale = Vector3.one;
            _rectTransform.localRotation = Quaternion.identity;
            _rectTransform.anchoredPosition3D = Vector3.zero;
            Vector3 localPosition = _rectTransform.localPosition;
            localPosition.z = 0f;
            _rectTransform.localPosition = localPosition;
        }

        private static void RefreshContainerLayout(Transform container)
        {
            if (container == null)
            {
                return;
            }

            RectTransform containerRect = container as RectTransform;
            if (containerRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            }

            ScrollRect scrollRect = container.GetComponentInParent<ScrollRect>();
            if (scrollRect != null && scrollRect.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
                Canvas.ForceUpdateCanvases();

                RectTransform viewport = scrollRect.viewport != null
                    ? scrollRect.viewport
                    : scrollRect.transform as RectTransform;

                if (viewport != null && scrollRect.content.rect.width <= viewport.rect.width + 1f)
                {
                    Vector2 contentPosition = scrollRect.content.anchoredPosition;
                    contentPosition.x = 0f;
                    scrollRect.content.anchoredPosition = contentPosition;
                    scrollRect.horizontalNormalizedPosition = 0f;
                }
                else
                {
                    scrollRect.horizontalNormalizedPosition = 1f;
                }
            }
            else
            {
                Canvas.ForceUpdateCanvases();
            }
        }

        private Transform ResolveDragParent()
        {
            if (dragRoot != null)
            {
                return dragRoot;
            }

            if (parentCanvas != null)
            {
                return parentCanvas.transform;
            }

            return null;
        }

        private Camera GetEventCamera(PointerEventData eventData)
        {
            if (eventData != null && eventData.pressEventCamera != null)
            {
                return eventData.pressEventCamera;
            }

            if (eventData != null && eventData.enterEventCamera != null)
            {
                return eventData.enterEventCamera;
            }

            if (parentCanvas != null)
            {
                return parentCanvas.worldCamera;
            }

            return null;
        }

        private string GetCardLabel()
        {
            if (_data == null)
            {
                return name;
            }

            return string.IsNullOrEmpty(_data.id) ? name : _data.id;
        }

        private static string GetTransformName(Transform target)
        {
            return target != null ? target.name : "null";
        }

        private void ApplyFixedLayoutElement()
        {
            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = gameObject.AddComponent<LayoutElement>();
            }

            layoutElement.minWidth = 320f;
            layoutElement.preferredWidth = 320f;
            layoutElement.minHeight = 380f;
            layoutElement.preferredHeight = 380f;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
        }

        private static string GetDisplayContent(MemoryAnchorData anchorData)
        {
            if (anchorData == null || string.IsNullOrEmpty(anchorData.userText))
            {
                return "（尚未輸入文字）";
            }

            string text = anchorData.userText;
            if (text.Length > 25)
            {
                return text.Substring(0, 25) + "...";
            }

            return text;
        }
    }
}
