using EchoSpace.LineOfLife;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EchoSpace.Reflection
{
    public class ReflectionSummaryController : MonoBehaviour
    {
        public GameObject panelRoot;
        public TMP_Text titleText;
        public TMP_Text summaryText;
        public Button closeButton;
        public Button backToLineOfLifeButton;
        public LineOfLifeManager lineOfLifeManager;
        public RectTransform summaryContentRect;
        public RectTransform summaryTextRect;
        public ScrollRect summaryScrollRect;
        public float minSummaryHeight = 1000f;
        public float extraPadding = 80f;

        public bool IsOpen =>
            panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            if (backToLineOfLifeButton != null)
            {
                backToLineOfLifeButton.onClick.AddListener(BackToLineOfLife);
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        public void Open(string summary)
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            if (titleText != null)
            {
                titleText.text = "整理摘要";
            }

            if (summaryText != null)
            {
                summaryText.text = summary ?? string.Empty;
                Canvas.ForceUpdateCanvases();

                float targetHeight = Mathf.Max(minSummaryHeight, summaryText.preferredHeight + extraPadding);

                if (summaryContentRect != null)
                {
                    summaryContentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);
                }

                if (summaryTextRect != null)
                {
                    summaryTextRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);
                }

                Canvas.ForceUpdateCanvases();

                if (summaryScrollRect != null)
                {
                    summaryScrollRect.verticalNormalizedPosition = 1f;
                }
            }

            Debug.Log("[ReflectionSummaryController] Open summary panel.");
        }

        public void Close()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            Debug.Log("[ReflectionSummaryController] Close summary panel.");
        }

        public void BackToLineOfLife()
        {
            Close();

            if (lineOfLifeManager != null)
            {
                lineOfLifeManager.OpenPanelAndRefresh();
            }
            else
            {
                Debug.LogWarning("[ReflectionSummaryController] lineOfLifeManager is not assigned.");
            }
        }
    }
}
