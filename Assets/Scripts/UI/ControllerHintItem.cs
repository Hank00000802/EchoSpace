using TMPro;
using UnityEngine;

namespace EchoSpace.UI
{
    /// <summary>
    /// One on-demand controller hint: billboard label, line, and a dot on a button anchor.
    /// Visual-only — no collider / raycast, so it does not affect anchors or UI.
    /// </summary>
    public class ControllerHintItem : MonoBehaviour
    {
        [SerializeField] private Transform targetAnchor;
        [SerializeField] private Transform labelRoot;
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private Transform hintDot;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private string hintLabel;
        [Tooltip("World-space offset from the button anchor so the label sits beside the controller.")]
        [SerializeField] private Vector3 labelOffsetFromAnchor = new Vector3(0.08f, 0.05f, 0f);

        private bool _isVisible;
        private bool _warnedMissingRefs;

        public bool IsVisible => _isVisible;

        public void SetHintLabel(string label)
        {
            hintLabel = label;
            if (labelText != null && !string.IsNullOrEmpty(hintLabel))
            {
                labelText.text = hintLabel;
            }
        }

        public void Show()
        {
            _isVisible = true;
            SetVisualsActive(true);
            Refresh();
        }

        public void Hide()
        {
            _isVisible = false;
            SetVisualsActive(false);
        }

        public void Toggle()
        {
            if (_isVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Refresh()
        {
            EnsureLineRendererSetup();
            if (_isVisible)
            {
                ApplyHint();
            }
        }

        private void Update()
        {
            if (!_isVisible || !isActiveAndEnabled)
            {
                return;
            }

            ApplyHint();
        }

        private void SetVisualsActive(bool active)
        {
            if (labelRoot != null)
            {
                labelRoot.gameObject.SetActive(active);
            }

            if (lineRenderer != null)
            {
                lineRenderer.enabled = active;
            }

            if (hintDot != null)
            {
                hintDot.gameObject.SetActive(active);
            }
        }

        private void ApplyHint()
        {
            if (targetAnchor == null || labelRoot == null || lineRenderer == null)
            {
                if (!_warnedMissingRefs)
                {
                    Debug.LogWarning("[ControllerHint] Missing targetAnchor / labelRoot / lineRenderer on " + name);
                    _warnedMissingRefs = true;
                }

                return;
            }

            if (labelText != null)
            {
                if (!string.IsNullOrEmpty(hintLabel) && labelText.text != hintLabel)
                {
                    labelText.text = hintLabel;
                }

                labelText.raycastTarget = false;
            }

            labelRoot.position = targetAnchor.TransformPoint(labelOffsetFromAnchor);
            FaceCamera();

            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, labelRoot.position);
            lineRenderer.SetPosition(1, targetAnchor.position);

            if (hintDot != null)
            {
                hintDot.position = targetAnchor.position;
            }
        }

        private void FaceCamera()
        {
            Camera cam = playerCamera;
            if (cam == null)
            {
                cam = Camera.main;
            }

            if (cam == null)
            {
                return;
            }

            Vector3 toCamera = cam.transform.position - labelRoot.position;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                return;
            }

            labelRoot.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            labelRoot.Rotate(0f, 180f, 0f, Space.Self);
        }

        private void EnsureLineRendererSetup()
        {
            if (lineRenderer == null)
            {
                return;
            }

            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.allowOcclusionWhenDynamic = false;
        }
    }
}
