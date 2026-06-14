using EchoSpace.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoSpace.LineOfLife
{
    public class MemoryCardUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public TMP_Text typeText;
        public TMP_Text contentText;
        public Canvas parentCanvas;
        [Tooltip("Optional drag root. Falls back to lineOfLifeCanvas transform when null.")]
        public Transform dragRoot;

        private const float DragAlpha = 0.85f;
        private const float NormalAlpha = 1f;

        private MemoryAnchorData _data;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Transform _originalParent;
        private int _originalSiblingIndex;
        private Vector2 _originalAnchoredPosition;
        private LineOfLifeManager _lineOfLifeManager;
        private bool _dropAccepted;
        private Vector3 _pointerWorldOffset;
        private RectTransform _dragPlane;
        private ScrollRect _parentScrollRect;
        private bool _scrollRectWasEnabled;
        private bool _loggedDragPosition;

        public MemoryAnchorData Data => _data;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        public void Initialize(MemoryAnchorData anchorData, LineOfLifeManager manager, Canvas canvas)
        {
            _data = anchorData;
            _lineOfLifeManager = manager;
            parentCanvas = canvas;
            _dragPlane = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;

            if (typeText != null)
            {
                typeText.text = anchorData != null
                    ? MemoryTypeDisplay.GetDisplayName(anchorData.memoryType)
                    : string.Empty;
            }

            if (contentText != null)
            {
                contentText.text = GetDisplayContent(anchorData);
            }

            ApplyFixedLayoutElement();
        }

        public void SetParentAndReset(Transform newParent)
        {
            if (newParent == null || _rectTransform == null)
            {
                return;
            }

            transform.SetParent(newParent, false);
            _rectTransform.localScale = Vector3.one;
            _rectTransform.localRotation = Quaternion.identity;
            _rectTransform.anchoredPosition3D = Vector3.zero;
            ApplyFixedLayoutElement();
        }

        public void ReturnToOriginalPosition()
        {
            if (_originalParent == null || _rectTransform == null)
            {
                return;
            }

            transform.SetParent(_originalParent, false);
            transform.SetSiblingIndex(_originalSiblingIndex);
            _rectTransform.anchoredPosition = _originalAnchoredPosition;
            _rectTransform.localScale = Vector3.one;
            _rectTransform.localRotation = Quaternion.identity;
        }

        public void NotifyDropAccepted()
        {
            _dropAccepted = true;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null || _rectTransform == null || _canvasGroup == null)
            {
                return;
            }

            _dropAccepted = false;
            _loggedDragPosition = false;
            _originalParent = transform.parent;
            _originalSiblingIndex = transform.GetSiblingIndex();
            _originalAnchoredPosition = _rectTransform.anchoredPosition;

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
                return;
            }

            _pointerWorldOffset = _rectTransform.position - pointerWorldPosition;

            Transform dragParent = ResolveDragParent();
            if (dragParent != null)
            {
                transform.SetParent(dragParent, true);
                transform.SetAsLastSibling();
            }

            _parentScrollRect = GetComponentInParent<ScrollRect>();
            if (_parentScrollRect != null)
            {
                _scrollRectWasEnabled = _parentScrollRect.enabled;
                _parentScrollRect.enabled = false;
            }

            _canvasGroup.alpha = DragAlpha;
            _canvasGroup.blocksRaycasts = false;

            string cardLabel = GetCardLabel();
            Debug.Log("[MemoryCardUI] Begin drag: " + cardLabel);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || _rectTransform == null || _dragPlane == null)
            {
                return;
            }

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
            if (_parentScrollRect != null)
            {
                _parentScrollRect.enabled = _scrollRectWasEnabled;
                _parentScrollRect = null;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = NormalAlpha;
                _canvasGroup.blocksRaycasts = true;
            }

            if (_lineOfLifeManager != null)
            {
                _lineOfLifeManager.HandleCardDropped(this);
            }

            if (!_dropAccepted)
            {
                ReturnToOriginalPosition();
                Debug.Log("[MemoryCardUI] Drop failed, returned to original position: " + GetCardLabel());
            }

            Debug.Log("[MemoryCardUI] End drag");
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
                return "(尚未輸入文字)";
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
