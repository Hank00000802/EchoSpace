using EchoSpace.Core;
using EchoSpace.LineOfLife;
using EchoSpace.UI;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace EchoSpace.Annotation
{
    public class MemoryAnchor : MonoBehaviour
    {
        [Tooltip("Optional. Auto-finds the scene 3DMemoryCardDetailPanel if left empty.")]
        [SerializeField] private MemoryCardDetailPanelController detailPanel;
        [SerializeField] private Color hoverColor = new Color(1f, 0.92f, 0.2f, 1f);

        public MemoryAnchorData Data { get; private set; }

        private XRSimpleInteractable _interactable;
        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;
        private bool _hasCachedBaseColor;
        private float _lastOpenTime = -1f;
        private bool _loggedMissingPanel;

        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private const float OpenDebounceSeconds = 0.25f;

        public void Initialize(MemoryAnchorData data)
        {
            if (data == null)
            {
                Debug.LogError("[MemoryAnchor] Initialize called with null MemoryAnchorData.");
                return;
            }

            if (string.IsNullOrEmpty(data.id))
            {
                Debug.LogWarning("[MemoryAnchor] MemoryAnchorData.id is empty; GameObject name may be incomplete.");
            }

            Data = data;
            transform.position = data.worldPosition;
            gameObject.name = "MemoryAnchor_" + (data.id ?? string.Empty);
            EnsureClickable();
            LogLiveDataBinding("Initialize");
        }

        public void UpdateVisualLabel()
        {
            if (Data == null)
            {
                Debug.LogWarning("[MemoryAnchor] UpdateVisualLabel called but Data is null.");
                return;
            }

            Debug.Log(
                "[MemoryAnchor] UpdateVisualLabel (placeholder) id="
                + Data.id
                + ", memoryType="
                + Data.memoryType
                + ", userText="
                + Data.userText
                + ", isAnnotated="
                + Data.isAnnotated);
        }

        private void OnEnable()
        {
            EnsureClickable();
            BindInteractable();
        }

        private void OnDisable()
        {
            UnbindInteractable();
            ApplyHoverHighlight(false);
        }

        private void OnMouseEnter()
        {
            ApplyHoverHighlight(true);
        }

        private void OnMouseExit()
        {
            ApplyHoverHighlight(false);
        }

        private void OnMouseUpAsButton()
        {
            OpenStoredMemoryDetail();
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            OpenStoredMemoryDetail();
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            ApplyHoverHighlight(true);
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (_interactable != null && _interactable.isHovered)
            {
                return;
            }

            ApplyHoverHighlight(false);
        }

        /// <summary>
        /// Opens the existing memory-detail panel with this triangle's live MemoryAnchorData.
        /// </summary>
        public bool OpenStoredMemoryDetail()
        {
            if (Time.unscaledTime - _lastOpenTime < OpenDebounceSeconds)
            {
                return false;
            }

            if (Data == null)
            {
                Debug.LogWarning("[MemoryAnchor] Cannot open detail: Data is null.");
                return false;
            }

            if (VRIntroGuideController.IsIntroActive || ControllerTutorialGate.IsBlockingGameplay)
            {
                return false;
            }

            AnnotationPanelController annotationPanel = FindObjectOfType<AnnotationPanelController>();
            if (annotationPanel != null && annotationPanel.IsOpen)
            {
                Debug.Log("[MemoryAnchor] Skip detail while AnnotationPanel is open.");
                return false;
            }

            MemoryCardDetailPanelController panel = ResolveDetailPanel();
            if (panel == null)
            {
                if (!_loggedMissingPanel)
                {
                    Debug.LogWarning(
                        "[MemoryAnchor] Cannot open detail: no MemoryCardDetailPanelController found. " +
                        "Confirm 3DMemoryCardDetailPanel is assigned on LineOfLife3DViewController.");
                    _loggedMissingPanel = true;
                }

                return false;
            }

            _lastOpenTime = Time.unscaledTime;
            LogLiveDataBinding("OpenDetail");
            UiSfx.PlayClick();
            panel.Open(Data);
            return true;
        }

        private void EnsureClickable()
        {
            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere == null)
            {
                sphere = gameObject.AddComponent<SphereCollider>();
            }

            sphere.enabled = true;
            sphere.isTrigger = false;

            if (_interactable == null)
            {
                _interactable = GetComponent<XRSimpleInteractable>();
                if (_interactable == null)
                {
                    _interactable = gameObject.AddComponent<XRSimpleInteractable>();
                }
            }

            // Refresh attached colliders if the prefab SphereCollider started disabled.
            if (_interactable.enabled)
            {
                _interactable.enabled = false;
                _interactable.enabled = true;
            }
        }

        private void BindInteractable()
        {
            if (_interactable == null)
            {
                _interactable = GetComponent<XRSimpleInteractable>();
            }

            if (_interactable == null)
            {
                return;
            }

            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectEntered.AddListener(OnSelectEntered);
            _interactable.hoverEntered.RemoveListener(OnHoverEntered);
            _interactable.hoverEntered.AddListener(OnHoverEntered);
            _interactable.hoverExited.RemoveListener(OnHoverExited);
            _interactable.hoverExited.AddListener(OnHoverExited);
        }

        private void UnbindInteractable()
        {
            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnSelectEntered);
                _interactable.hoverEntered.RemoveListener(OnHoverEntered);
                _interactable.hoverExited.RemoveListener(OnHoverExited);
            }
        }

        private void ApplyHoverHighlight(bool hovered)
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<Renderer>();
            }

            if (_renderer == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            if (!_hasCachedBaseColor)
            {
                Material shared = _renderer.sharedMaterial;
                if (shared != null && shared.HasProperty(ColorPropertyId))
                {
                    _baseColor = shared.GetColor(ColorPropertyId);
                }

                _hasCachedBaseColor = true;
            }

            Color color = hovered ? hoverColor : _baseColor;
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(ColorPropertyId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        private MemoryCardDetailPanelController ResolveDetailPanel()
        {
            if (detailPanel != null)
            {
                return detailPanel;
            }

            LineOfLife3DViewController view3d = FindObjectOfType<LineOfLife3DViewController>();
            if (view3d != null && view3d.memoryCardDetailPanel != null)
            {
                detailPanel = view3d.memoryCardDetailPanel;
                return detailPanel;
            }

            LineOfLifeManager lineOfLife = FindObjectOfType<LineOfLifeManager>();
            if (lineOfLife != null && lineOfLife.memoryCardDetailPanel != null)
            {
                detailPanel = lineOfLife.memoryCardDetailPanel;
                return detailPanel;
            }

            detailPanel = FindObjectOfType<MemoryCardDetailPanelController>();
            return detailPanel;
        }

        private void LogLiveDataBinding(string reason)
        {
            if (Data == null)
            {
                return;
            }

            MemoryAnchorManager manager = GetComponentInParent<MemoryAnchorManager>();
            MemoryAnchorData managed = manager != null ? manager.FindAnchorById(Data.id) : null;
            bool sameRef = managed != null && ReferenceEquals(managed, Data);

            Debug.Log(
                "[MemoryAnchor] " + reason
                + " id=" + Data.id
                + ", isAnnotated=" + Data.isAnnotated
                + ", memoryType=" + Data.memoryType
                + ", timeCategory=" + Data.timeCategory
                + ", userText=" + Data.userText
                + ", liveManagerRef=" + sameRef);
        }
    }
}
