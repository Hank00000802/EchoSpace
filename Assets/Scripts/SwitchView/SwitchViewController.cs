using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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
        public bool hidePromptAfterSwitch = false;

        [Header("Prompt Placement")]
        public bool placePromptInFrontOfPlayer = true;
        public float promptDistanceFromCamera = 1.2f;
        public float promptVerticalOffset = 0f;
        public bool promptFaceCamera = true;
        public bool promptLockYOnly = true;
        public bool promptFlip180 = true;

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

            switchViewRoot.SetActive(true);
            Debug.Log("[SwitchViewController] Show switch view prompt.");
        }

        public void HideSwitchViewPrompt()
        {
            if (switchViewRoot != null)
            {
                switchViewRoot.SetActive(false);
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

            if (hidePromptAfterSwitch)
            {
                HideSwitchViewPrompt();
            }
        }

        private static Vector3 GetHorizontalForward(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
        }
    }
}
