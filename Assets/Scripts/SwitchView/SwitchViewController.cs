using System.Collections;
using EchoSpace.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EchoSpace.SwitchView
{
    /// <summary>
    /// Aligns the player camera to a target view transform (e.g. Position under SwitchView).
    /// SwitchViewRoot prompt is shown separately; this script handles the actual view switch.
    /// SwitchView must not be parented under LineOfLife3DView viewRoot, or disabling viewRoot will hide it.
    /// </summary>
    public class SwitchViewController : MonoBehaviour
    {
        [Header("View Objects")]
        public GameObject switchViewRoot;
        public Transform targetViewTransform;

        [Header("Player")]
        public Transform playerRoot;
        public Transform playerCamera;

        [Header("UI")]
        public Button switchViewButton;

        [Header("Movement")]
        public bool matchRotation = true;
        public bool useSmoothMove = false;
        public float smoothMoveDuration = 0.75f;
        public bool hidePromptAfterSwitch = true;

        [Header("Prompt Placement")]
        public bool placePromptInFrontOfPlayer = true;
        public float promptDistanceFromCamera = 1.2f;
        public float promptVerticalOffset = 0f;
        public bool promptFaceCamera = true;
        public bool promptLockYOnly = true;
        public bool promptFlip180 = true;

        [Header("Switch Completed Event")]
        public UnityEvent onSwitchCompleted;

        private Coroutine smoothMoveCoroutine;

        private void Awake()
        {
            RegisterSwitchViewButton();

            if (switchViewRoot != null)
            {
                switchViewRoot.SetActive(false);
            }
        }

        private void RegisterSwitchViewButton()
        {
            if (switchViewButton != null)
            {
                switchViewButton.onClick.RemoveListener(SwitchToTargetView);
                switchViewButton.onClick.AddListener(SwitchToTargetView);
            }
        }

        private void EnsureSwitchViewUiRaycast()
        {
            if (switchViewRoot == null)
            {
                return;
            }

            Canvas canvas = switchViewRoot.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = switchViewRoot.GetComponentInChildren<Canvas>(true);
            }

            if (canvas == null)
            {
                Debug.LogWarning("[SwitchViewController] No Canvas under switchViewRoot; XR rays cannot hit SwitchButton.");
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            if (canvas.sortingOrder < 1300)
            {
                canvas.sortingOrder = 1300;
            }

            EnsurePlayerCamera();
            if (playerCamera != null)
            {
                Camera cam = playerCamera.GetComponent<Camera>();
                if (cam != null)
                {
                    canvas.worldCamera = cam;
                }
            }

            GraphicRaycaster graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
            if (graphicRaycaster == null)
            {
                graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            graphicRaycaster.ignoreReversedGraphics = false;
            graphicRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            TrackedDeviceGraphicRaycaster trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (trackedRaycaster == null)
            {
                trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            }

            trackedRaycaster.ignoreReversedGraphics = false;
            trackedRaycaster.checkFor3DOcclusion = false;

            if (switchViewButton != null)
            {
                switchViewButton.interactable = true;
                Graphic graphic = switchViewButton.targetGraphic;
                if (graphic == null)
                {
                    graphic = switchViewButton.GetComponent<Image>();
                    switchViewButton.targetGraphic = graphic;
                }

                if (graphic != null)
                {
                    graphic.raycastTarget = true;
                }
            }
        }

        public void ShowSwitchViewPrompt()
        {
            if (switchViewRoot == null)
            {
                Debug.LogWarning("[SwitchViewController] switchViewRoot is not assigned.");
                return;
            }

            EnsurePlayerCamera();

            if (placePromptInFrontOfPlayer && playerCamera != null)
            {
                PlacePromptInFrontOfCamera();
            }

            bool wasActive = switchViewRoot.activeSelf;
            switchViewRoot.SetActive(true);
            EnsureSwitchViewUiRaycast();
            if (!wasActive)
            {
                UiSfx.PlayPopup();
            }

            Debug.Log("[SwitchViewController] Show switch view prompt.");
        }

        public void HideSwitchViewPrompt()
        {
            HideSwitchViewPrompt(true);
        }

        public void HideSwitchViewPrompt(bool playExit)
        {
            bool wasActive = switchViewRoot != null && switchViewRoot.activeSelf;
            if (switchViewRoot != null)
            {
                switchViewRoot.SetActive(false);
            }

            if (playExit && wasActive)
            {
                UiSfx.PlayExit();
            }

            Debug.Log("[SwitchViewController] Hide switch view prompt.");
        }

        public void SwitchToTargetView()
        {
            Debug.Log("[SwitchViewController] SwitchToTargetView called.");

            if (playerRoot == null || targetViewTransform == null)
            {
                Debug.LogWarning("[SwitchViewController] playerRoot or targetViewTransform is not assigned.");
                return;
            }

            EnsurePlayerCamera();

            if (playerCamera == null)
            {
                Debug.LogWarning("[SwitchViewController] playerCamera is not assigned and Camera.main is null.");
                return;
            }

            if (smoothMoveCoroutine != null)
            {
                StopCoroutine(smoothMoveCoroutine);
                smoothMoveCoroutine = null;
            }

            UiSfx.PlayPopup();

            if (useSmoothMove && smoothMoveDuration > 0f)
            {
                smoothMoveCoroutine = StartCoroutine(SmoothMoveToTargetView());
            }
            else
            {
                ApplyTargetViewImmediate();
                FinishSwitch();
            }
        }

        private void EnsurePlayerCamera()
        {
            if (playerCamera == null && Camera.main != null)
            {
                playerCamera = Camera.main.transform;
            }
        }

        private void PlacePromptInFrontOfCamera()
        {
            Transform promptTransform = switchViewRoot.transform;

            Vector3 targetPos = playerCamera.position + playerCamera.forward * promptDistanceFromCamera;
            targetPos += Vector3.up * promptVerticalOffset;
            promptTransform.position = targetPos;

            if (promptFaceCamera)
            {
                Vector3 dir = promptTransform.position - playerCamera.position;

                if (promptLockYOnly)
                {
                    dir.y = 0f;
                }

                if (dir.sqrMagnitude > 0.001f)
                {
                    promptTransform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
            }

            if (promptFlip180)
            {
                promptTransform.Rotate(0f, 180f, 0f, Space.Self);
            }
        }

        private IEnumerator SmoothMoveToTargetView()
        {
            ComputeTargetRootState(out Quaternion targetRotation, out Vector3 targetPosition);

            Quaternion startRotation = playerRoot.rotation;
            Vector3 startPosition = playerRoot.position;
            float elapsed = 0f;

            while (elapsed < smoothMoveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / smoothMoveDuration);

                playerRoot.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
                playerRoot.position = Vector3.Lerp(startPosition, targetPosition, t);
                yield return null;
            }

            ApplyTargetViewImmediate();
            FinishSwitch();
            smoothMoveCoroutine = null;
        }

        private void ApplyTargetViewImmediate()
        {
            if (matchRotation)
            {
                Vector3 currentForward = GetHorizontalForward(playerCamera.forward);
                Vector3 targetForward = GetHorizontalForward(targetViewTransform.forward);

                if (currentForward.sqrMagnitude > 0.0001f && targetForward.sqrMagnitude > 0.0001f)
                {
                    Quaternion rotationDelta = Quaternion.FromToRotation(currentForward, targetForward);
                    playerRoot.rotation = rotationDelta * playerRoot.rotation;
                }
            }

            Vector3 cameraOffset = playerCamera.position - playerRoot.position;
            playerRoot.position = targetViewTransform.position - cameraOffset;
        }

        private void ComputeTargetRootState(out Quaternion targetRotation, out Vector3 targetPosition)
        {
            targetRotation = playerRoot.rotation;

            if (matchRotation)
            {
                Vector3 currentForward = GetHorizontalForward(playerCamera.forward);
                Vector3 targetForward = GetHorizontalForward(targetViewTransform.forward);

                if (currentForward.sqrMagnitude > 0.0001f && targetForward.sqrMagnitude > 0.0001f)
                {
                    Quaternion rotationDelta = Quaternion.FromToRotation(currentForward, targetForward);
                    targetRotation = rotationDelta * playerRoot.rotation;
                }
            }

            Quaternion savedRotation = playerRoot.rotation;
            Vector3 savedPosition = playerRoot.position;

            playerRoot.rotation = targetRotation;
            Vector3 cameraOffset = playerCamera.position - playerRoot.position;
            targetPosition = targetViewTransform.position - cameraOffset;

            playerRoot.rotation = savedRotation;
            playerRoot.position = savedPosition;
        }

        private void FinishSwitch()
        {
            Debug.Log("[SwitchViewController] Switched to target view: " + targetViewTransform.name);

            // Always hide large Switch View UI after arriving, to protect immersion.
            HideSwitchViewPrompt(false);

            onSwitchCompleted?.Invoke();
        }

        private static Vector3 GetHorizontalForward(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
        }
    }
}
