using EchoSpace.Core;
using EchoSpace.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// 3D timeline memory card: shows short preview; click / XR UI select opens detail panel.
    /// Prefab uses World Space Canvas, so UI raycast (IPointerClickHandler) is the primary path.
    /// </summary>
    public class MemoryCard3DUI : MonoBehaviour, IPointerClickHandler
    {
        public TMP_Text typeText;
        public TMP_Text contentText;

        private const int MaxUserTextLength = 35;

        private MemoryAnchorData _data;
        private GeneratedTransitionCardData _generated;
        private MemoryCardDetailPanelController _detailPanel;
        private LineOfLife3DViewController _timelineView;
        private XRSimpleInteractable _interactable;
        private bool _loggedMissingPanel;
        private bool _pairSelected;
        private bool _usedInGeneratedPair;
        private Color _defaultTypeColor = Color.white;
        private bool _hasCachedDefaultTypeColor;

        public MemoryAnchorData Data => _data;
        public GeneratedTransitionCardData GeneratedData => _generated;
        public bool IsPairSelected => _pairSelected;
        public bool IsUsedInGeneratedPair => _usedInGeneratedPair;

        public void Initialize(
            MemoryAnchorData data,
            MemoryCardDetailPanelController detailPanel = null,
            LineOfLife3DViewController timelineView = null)
        {
            _data = data;
            _detailPanel = detailPanel;
            _timelineView = timelineView;
            _loggedMissingPanel = false;
            _generated = null;
            _pairSelected = false;
            _usedInGeneratedPair = false;
            CacheDefaultTypeColor();

            if (data == null)
            {
                if (typeText != null)
                {
                    typeText.text = string.Empty;
                }

                if (contentText != null)
                {
                    contentText.text = "（尚未輸入文字）";
                }

                EnsureClickable();
                RefreshSelectForPairButtonVisibility();
                return;
            }

            if (typeText != null)
            {
                typeText.text = MemoryTypeDisplay.GetDisplayName(data.memoryType);
            }

            if (contentText != null)
            {
                contentText.text = FormatUserText(data.userText);
            }

            EnsureClickable();
            RefreshSelectForPairButtonVisibility();

            if (_detailPanel == null)
            {
                Debug.LogWarning(
                    "[MemoryCard3DUI] detailPanel is NULL after Initialize. " +
                    "Assign Memory Card Detail Panel on LineOfLife3DViewController.");
            }
            else
            {
                Debug.Log("[MemoryCard3DUI] Initialized with detailPanel=" + _detailPanel.name);
            }
        }

        public void InitializeGenerated(
            GeneratedTransitionCardData generated,
            MemoryCardDetailPanelController detailPanel = null,
            LineOfLife3DViewController timelineView = null)
        {
            MemoryAnchorData display = generated == null
                ? null
                : new MemoryAnchorData
                {
                    id = generated.id,
                    locationName = generated.displayTitle,
                    memoryType = MemoryType.TransitionMoment,
                    promptQuestion = string.Empty,
                    userText = generated.transitionText,
                    timeCategory = TimeCategory.Transition,
                    isAnnotated = true
                };

            Initialize(display, detailPanel, timelineView);
            _generated = generated;

            if (typeText != null && generated != null)
            {
                typeText.text = generated.displayTitle;
            }

            if (generated != null)
            {
                SetGroupColor(generated.groupColor);
            }

            RefreshSelectForPairButtonVisibility();
        }

        public void SelectForTransitionPair()
        {
            if (_timelineView == null)
            {
                Debug.LogWarning("[MemoryCard3DUI] Cannot select for pair: timeline view is not assigned.");
                return;
            }

            _timelineView.SelectForTransitionPair(this);
        }

        public void SetPairSelected(bool selected)
        {
            _pairSelected = selected;
            if (!selected && !_usedInGeneratedPair)
            {
                RestoreDefaultTypeColor();
            }

            ApplyPairVisual();
        }

        public void SetUsedInGeneratedPair(bool used)
        {
            _usedInGeneratedPair = used;
            RefreshSelectForPairButtonVisibility();
        }

        public void SetGroupColor(Color color)
        {
            CacheDefaultTypeColor();
            if (typeText != null)
            {
                typeText.color = color;
            }

            ApplyPairVisual();
        }

        private void CacheDefaultTypeColor()
        {
            if (_hasCachedDefaultTypeColor)
            {
                return;
            }

            if (typeText != null)
            {
                _defaultTypeColor = typeText.color;
            }

            _hasCachedDefaultTypeColor = true;
        }

        private void RestoreDefaultTypeColor()
        {
            if (typeText != null)
            {
                typeText.color = _defaultTypeColor;
            }
        }

        private void ApplyPairVisual()
        {
            if (contentText != null)
            {
                contentText.fontStyle = _pairSelected ? FontStyles.Bold : FontStyles.Normal;
            }

            RefreshSelectForPairButtonVisibility();
        }

        public void RefreshSelectForPairButtonVisibility()
        {
            Transform button = FindChildByName(transform, "SelectForPairButton");
            if (button == null)
            {
                return;
            }

            bool hide = _generated != null
                || _pairSelected
                || _usedInGeneratedPair
                || _timelineView == null
                || !_timelineView.IsPairSelectionEnabled;
            button.gameObject.SetActive(!hide);

            Button uiButton = button.GetComponent<Button>();
            if (uiButton == null)
            {
                uiButton = button.GetComponentInChildren<Button>(true);
            }

            UiSfx.BindClick(uiButton);
        }

        private void OnEnable()
        {
            EnsureClickable();
            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnSelectEntered);
                _interactable.selectEntered.AddListener(OnSelectEntered);
            }
        }

        private void OnDisable()
        {
            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnSelectEntered);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log("[MemoryCard3DUI] OnPointerClick");
            UiSfx.PlayClick();
            OpenDetailPanel();
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            Debug.Log("[MemoryCard3DUI] OnSelectEntered (XR)");
            UiSfx.PlayClick();
            OpenDetailPanel();
        }

        private void OnMouseUpAsButton()
        {
            Debug.Log("[MemoryCard3DUI] OnMouseUpAsButton");
            UiSfx.PlayClick();
            OpenDetailPanel();
        }

        private void OpenDetailPanel()
        {
            if (_detailPanel == null)
            {
                if (!_loggedMissingPanel)
                {
                    Debug.LogWarning(
                        "[MemoryCard3DUI] Cannot open detail: detailPanel is not assigned. " +
                        "Drag your 3DMemoryCardDetailPanel onto LineOfLife3DView → Memory Card Detail Panel.");
                    _loggedMissingPanel = true;
                }

                return;
            }

            _detailPanel.Open(_data, _generated);
        }

        private void EnsureClickable()
        {
            EnsureUiRaycasters();
            EnsureCollider();

            if (_interactable == null)
            {
                _interactable = GetComponent<XRSimpleInteractable>();
                if (_interactable == null)
                {
                    _interactable = gameObject.AddComponent<XRSimpleInteractable>();
                }
            }
        }

        private void EnsureUiRaycasters()
        {
            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null)
                {
                    continue;
                }

                if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }

                // Required for XR Interaction Toolkit UI rays on World Space canvases.
                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                }
            }
        }

        private void EnsureCollider()
        {
            Collider existing = GetComponent<Collider>();
            if (existing != null)
            {
                return;
            }

            // Match CardCanvas world size approx: 280x150 * scale 0.002
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.56f, 0.30f, 0.05f);
            box.center = Vector3.zero;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByName(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string FormatUserText(string userText)
        {
            if (string.IsNullOrEmpty(userText))
            {
                return "（尚未輸入文字）";
            }

            if (userText.Length <= MaxUserTextLength)
            {
                return userText;
            }

            return userText.Substring(0, MaxUserTextLength) + "...";
        }
    }
}
