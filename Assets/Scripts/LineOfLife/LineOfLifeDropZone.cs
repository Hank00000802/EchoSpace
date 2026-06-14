using EchoSpace.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// Drop target for Past / Transition / Present memory cards.
    /// Inspector setup (required for mouse and XR ray drops):
    /// - Assign an Image on this GameObject (or via backgroundImage).
    /// - Image.raycastTarget must be true.
    /// - If a CanvasGroup exists on this object, blocksRaycasts must be true.
    /// - Do not cover this zone with transparent UI that has raycastTarget enabled.
    /// </summary>
    public class LineOfLifeDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public TimeCategory category;
        public Transform cardContainer;
        public Image backgroundImage;
        public Color normalColor = Color.white;
        public Color highlightColor = new Color(0.85f, 0.92f, 1f, 1f);
        public LineOfLifeManager manager;

        private void Awake()
        {
            if (manager == null)
            {
                manager = FindObjectOfType<LineOfLifeManager>();
            }

            ValidateRaycastSetup();
            ApplyBackgroundColor(normalColor);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            ApplyBackgroundColor(highlightColor);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ApplyBackgroundColor(normalColor);
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData == null || eventData.pointerDrag == null)
            {
                return;
            }

            MemoryCardUI card = eventData.pointerDrag.GetComponent<MemoryCardUI>();
            if (card == null || card.Data == null)
            {
                return;
            }

            Transform targetContainer = cardContainer != null ? cardContainer : transform;

            if (manager != null)
            {
                manager.RegisterCardDrop(card, category, targetContainer);
            }
            else
            {
                card.Data.timeCategory = category;
                card.SetParentAndReset(targetContainer);
                card.NotifyDropAccepted();
            }

            string id = card.Data.id ?? "(no id)";
            Debug.Log("[LineOfLifeDropZone] Drop card to category: " + category + " (id=" + id + ")");
        }

        private void ValidateRaycastSetup()
        {
            Image image = backgroundImage != null ? backgroundImage : GetComponent<Image>();
            if (image == null)
            {
                Debug.LogWarning(
                    "[LineOfLifeDropZone] " + name
                    + " needs an Image with raycastTarget=true for drop detection.");
                return;
            }

            if (!image.raycastTarget)
            {
                Debug.LogWarning(
                    "[LineOfLifeDropZone] " + name
                    + " Image.raycastTarget is false; enable it for mouse/XR drops.");
            }

            CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null && !canvasGroup.blocksRaycasts)
            {
                Debug.LogWarning(
                    "[LineOfLifeDropZone] " + name
                    + " CanvasGroup.blocksRaycasts is false; enable it for drop detection.");
            }
        }

        private void ApplyBackgroundColor(Color color)
        {
            if (backgroundImage != null)
            {
                backgroundImage.color = color;
            }
        }
    }
}
