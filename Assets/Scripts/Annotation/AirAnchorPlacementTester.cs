using System.Collections.Generic;
using EchoSpace.LineOfLife;
using EchoSpace.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace EchoSpace.Annotation
{
    public enum XRInteractionMode
    {
        Marking,
        Exploration
    }

    public class AirAnchorPlacementTester : MonoBehaviour
    {
        public Camera targetCamera;
        public MemoryAnchorManager anchorManager;
        public AnnotationPanelController annotationPanel;
        public LineOfLifeManager lineOfLifeManager;
        public ReflectionSummaryController reflectionSummaryController;
        public GameObject previewMarker;
        public float placementDistance = 2.0f;
        public float minDistance = 0.5f;
        public float maxDistance = 5.0f;
        public float mouseDistanceAdjustSpeed = 1.0f;
        public KeyCode confirmKey = KeyCode.Mouse0;
        public KeyCode cancelKey = KeyCode.Escape;
        public bool placementActive = true;

        [Header("XR Controller Free Placement")]
        public bool useXRControllerFreePlacement = true;
        public Transform controllerRayOrigin;
        public float markerDistance = 2.0f;
        public float minMarkerDistance = 0.4f;
        public float maxMarkerDistance = 6.0f;
        public float xrDistanceAdjustSpeed = 1.5f;
        public float xrJoystickDeadzone = 0.15f;
        public bool invertXRJoystickDistance = false;
        public bool alignMarkerToControllerForward = true;
        public bool placeWithControllerTrigger = true;

        [Header("Debug Input")]
        public bool useKeyboardDebugInput = true;
        public KeyCode debugPlaceKey = KeyCode.P;
        public KeyCode debugIncreaseDistanceKey = KeyCode.Equals;
        public KeyCode debugDecreaseDistanceKey = KeyCode.Minus;

        [Header("XR UI Blocking")]
        public bool blockXRPlacementWhenAnnotationPanelOpen = true;
        public bool blockXRPlacementWhenPointerOverUI = true;
        public XRRayInteractor xrRayInteractor;

        [Header("XR Mode Switch")]
        public bool enableXRModeSwitch = true;
        public XRInteractionMode currentXRMode = XRInteractionMode.Marking;
        public bool startInMarkingMode = true;
        public bool hidePreviewMarkerInExplorationMode = true;
        [Tooltip("Drag XR Origin/Locomotion/Turn or SnapTurn/ContinuousTurn objects here.")]
        public GameObject[] turnObjectsToEnableInExplorationMode;

        private bool _warnedMissingCamera;
        private bool _warnedMissingAnchorManager;
        private bool _warnedMissingPreviewMarker;
        private bool _warnedMissingControllerRayOrigin;
        private bool _xrTriggerWasPressed;
        private bool _leftXWasPressed;

        private void OnEnable()
        {
            if (!useXRControllerFreePlacement && previewMarker != null)
            {
                previewMarker.SetActive(placementActive);
            }
        }

        private void Start()
        {
            if (useXRControllerFreePlacement)
            {
                SetXRInteractionMode(startInMarkingMode ? XRInteractionMode.Marking : XRInteractionMode.Exploration);
            }
        }

        private void Update()
        {
            if (anchorManager == null)
            {
                if (!_warnedMissingAnchorManager)
                {
                    Debug.LogWarning("[AirAnchorPlacementTester] anchorManager is not assigned.");
                    _warnedMissingAnchorManager = true;
                }

                return;
            }

            _warnedMissingAnchorManager = false;

            if (previewMarker == null)
            {
                if (!_warnedMissingPreviewMarker)
                {
                    Debug.LogWarning("[AirAnchorPlacementTester] previewMarker is not assigned.");
                    _warnedMissingPreviewMarker = true;
                }

                return;
            }

            _warnedMissingPreviewMarker = false;

            if (Input.GetKeyDown(cancelKey))
            {
                placementActive = !placementActive;
                if (!useXRControllerFreePlacement && previewMarker != null)
                {
                    previewMarker.SetActive(placementActive);
                }
            }

            if (useXRControllerFreePlacement)
            {
                if (enableXRModeSwitch && GetLeftPrimaryButtonDown())
                {
                    if (IsMarkerBlockedBySystem())
                    {
                        SwitchToExplorationIfMarkerBlocked("marker blocked when pressing X");
                    }
                    else
                    {
                        ToggleXRInteractionMode();
                    }
                }

                if (IsMarkerBlockedBySystem())
                {
                    SwitchToExplorationIfMarkerBlocked("marker blocked by system state");
                    return;
                }

                if (currentXRMode == XRInteractionMode.Marking)
                {
                    UpdateXRFreePreviewMarker();
                    HandleXRPlacementInput();
                }
                else
                {
                    if (hidePreviewMarkerInExplorationMode && previewMarker != null)
                    {
                        previewMarker.SetActive(false);
                    }

                    // Exploration mode: no marker distance update, no anchor placement.
                    // Right joystick input is left to XR Turn Provider.
                }

                return;
            }

            if (!placementActive)
            {
                return;
            }

            if (lineOfLifeManager != null && lineOfLifeManager.IsOpen)
            {
                previewMarker.SetActive(false);
                return;
            }

            if (reflectionSummaryController != null && reflectionSummaryController.IsOpen)
            {
                previewMarker.SetActive(false);
                return;
            }

            if (annotationPanel != null && annotationPanel.IsOpen)
            {
                previewMarker.SetActive(false);
                return;
            }

            UpdateMouseMode();
        }

        private bool IsMarkerBlockedBySystem()
        {
            if (!placementActive)
            {
                return true;
            }

            if (annotationPanel != null && annotationPanel.IsOpen)
            {
                return true;
            }

            if (lineOfLifeManager != null && lineOfLifeManager.IsOpen)
            {
                return true;
            }

            if (reflectionSummaryController != null && reflectionSummaryController.IsOpen)
            {
                return true;
            }

            if (useXRControllerFreePlacement && controllerRayOrigin == null)
            {
                return true;
            }

            return false;
        }

        private void SwitchToExplorationIfMarkerBlocked(string reason)
        {
            if (currentXRMode != XRInteractionMode.Exploration)
            {
                SetXRInteractionMode(XRInteractionMode.Exploration);
                Debug.Log("[AirAnchorPlacementTester] Auto switch to Exploration because: " + reason);
            }

            if (previewMarker != null)
            {
                previewMarker.SetActive(false);
            }
        }

        private void UpdateMouseMode()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                if (!_warnedMissingCamera)
                {
                    Debug.LogWarning("[AirAnchorPlacementTester] No camera: assign targetCamera or add a tagged MainCamera.");
                    _warnedMissingCamera = true;
                }

                return;
            }

            _warnedMissingCamera = false;

            previewMarker.SetActive(true);

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            AdjustMousePlacementDistance(Input.mouseScrollDelta.y);
            previewMarker.transform.position = ray.origin + ray.direction * placementDistance;

            if (Input.GetKeyDown(confirmKey))
            {
                if (IsPointerOverBlockingUI())
                {
                    Debug.Log("[AirAnchorPlacementTester] Pointer is over blocking UI, skip anchor placement.");
                    return;
                }

                PlaceAnchorAtPreviewMarker();
            }
        }

        private void UpdateXRFreePreviewMarker()
        {
            if (controllerRayOrigin == null)
            {
                if (!_warnedMissingControllerRayOrigin)
                {
                    Debug.LogWarning("[AirAnchorPlacementTester] controllerRayOrigin is not assigned.");
                    _warnedMissingControllerRayOrigin = true;
                }

                previewMarker.SetActive(false);
                return;
            }

            _warnedMissingControllerRayOrigin = false;

            markerDistance = Mathf.Clamp(markerDistance, minMarkerDistance, maxMarkerDistance);

            Vector3 targetPos = controllerRayOrigin.position + controllerRayOrigin.forward * markerDistance;

            previewMarker.SetActive(true);
            previewMarker.transform.position = targetPos;

            if (alignMarkerToControllerForward)
            {
                previewMarker.transform.rotation = Quaternion.LookRotation(controllerRayOrigin.forward, Vector3.up);
            }

            Debug.DrawRay(controllerRayOrigin.position, controllerRayOrigin.forward * markerDistance, Color.cyan);
        }

        private void AdjustMousePlacementDistance(float scrollInput)
        {
            if (Mathf.Approximately(scrollInput, 0f))
            {
                return;
            }

            float previousDistance = placementDistance;
            placementDistance += scrollInput * mouseDistanceAdjustSpeed;
            placementDistance = Mathf.Clamp(placementDistance, minDistance, maxDistance);

            if (!Mathf.Approximately(previousDistance, placementDistance))
            {
                Debug.Log("[AirAnchorPlacementTester] Mouse placement distance: " + placementDistance);
            }
        }

        private void AdjustXRMarkerDistance(float input)
        {
            if (Mathf.Abs(input) < 0.01f)
            {
                return;
            }

            float previousDistance = markerDistance;
            markerDistance += input * xrDistanceAdjustSpeed * Time.deltaTime;
            markerDistance = Mathf.Clamp(markerDistance, minMarkerDistance, maxMarkerDistance);

            if (!Mathf.Approximately(previousDistance, markerDistance))
            {
                Debug.Log("[AirAnchorPlacementTester] XR marker distance: " + markerDistance);
            }
        }

        private void HandleXRPlacementInput()
        {
            if (useKeyboardDebugInput)
            {
                if (Input.GetKey(debugIncreaseDistanceKey))
                {
                    AdjustXRMarkerDistance(1f);
                }

                if (Input.GetKey(debugDecreaseDistanceKey))
                {
                    AdjustXRMarkerDistance(-1f);
                }

                if (Input.GetKeyDown(debugPlaceKey))
                {
                    if (!ShouldBlockXRAnchorPlacement())
                    {
                        PlaceAnchorAtPreviewMarker();
                    }
                }
            }

            HandleXRJoystickDistanceInput();

            if (placeWithControllerTrigger && GetXRPlaceInputDown())
            {
                if (!ShouldBlockXRAnchorPlacement())
                {
                    Debug.Log("[AirAnchorPlacementTester] Right trigger place anchor.");
                    PlaceAnchorAtPreviewMarker();
                }
            }
        }

        private bool ShouldBlockXRAnchorPlacement()
        {
            if (blockXRPlacementWhenAnnotationPanelOpen && annotationPanel != null && annotationPanel.IsOpen)
            {
                Debug.Log("[AirAnchorPlacementTester] XR placement blocked: annotation panel is open.");
                return true;
            }

            if (blockXRPlacementWhenPointerOverUI && IsXRPointerOverBlockingUI())
            {
                Debug.Log("[AirAnchorPlacementTester] XR placement blocked: pointer over UI.");
                return true;
            }

            return false;
        }

        private bool IsXRPointerOverBlockingUI()
        {
            if (xrRayInteractor == null)
            {
                return false;
            }

            if (xrRayInteractor.TryGetCurrentUIRaycastResult(out RaycastResult result))
            {
                GameObject go = result.gameObject;
                if (go == null)
                {
                    return false;
                }

                if (go.GetComponentInParent<Button>() != null)
                {
                    return true;
                }

                if (go.GetComponentInParent<TMP_InputField>() != null)
                {
                    return true;
                }

                if (go.GetComponentInParent<TMP_Dropdown>() != null)
                {
                    return true;
                }

                if (go.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }

                return true;
            }

            return false;
        }

        private void HandleXRJoystickDistanceInput()
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right,
                devices);

            if (devices.Count == 0)
            {
                return;
            }

            foreach (InputDevice device in devices)
            {
                if (!device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
                {
                    continue;
                }

                float y = axis.y;
                if (Mathf.Abs(y) < xrJoystickDeadzone)
                {
                    return;
                }

                if (invertXRJoystickDistance)
                {
                    y = -y;
                }

                AdjustXRMarkerDistance(y);
                return;
            }
        }

        private void PlaceAnchorAtPreviewMarker()
        {
            if (blockXRPlacementWhenAnnotationPanelOpen && annotationPanel != null && annotationPanel.IsOpen)
            {
                Debug.Log("[AirAnchorPlacementTester] Skip placing anchor because annotation panel is open.");
                return;
            }

            if (previewMarker == null || !previewMarker.activeSelf)
            {
                Debug.LogWarning("[AirAnchorPlacementTester] Cannot place anchor: previewMarker inactive.");
                return;
            }

            Vector3 pos = previewMarker.transform.position;

            MemoryAnchor newAnchor = anchorManager.CreateAnchor(pos);
            if (newAnchor != null)
            {
                Debug.Log("[AirAnchorPlacementTester] Created memory anchor at " + pos);
            }

            if (annotationPanel != null && newAnchor != null)
            {
                annotationPanel.Open(newAnchor);
            }
        }

        private bool GetXRPlaceInputDown()
        {
            bool pressed = IsXRTriggerPressed();
            bool down = pressed && !_xrTriggerWasPressed;
            _xrTriggerWasPressed = pressed;
            return down;
        }

        private static bool IsXRTriggerPressed()
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right,
                devices);

            foreach (InputDevice device in devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed) && triggerPressed)
                {
                    return true;
                }
            }

            return false;
        }

        public void ToggleXRInteractionMode()
        {
            if (currentXRMode == XRInteractionMode.Marking)
            {
                SetXRInteractionMode(XRInteractionMode.Exploration);
            }
            else
            {
                SetXRInteractionMode(XRInteractionMode.Marking);
            }
        }

        public void SetXRInteractionMode(XRInteractionMode mode)
        {
            if (mode == XRInteractionMode.Marking && IsMarkerBlockedBySystem())
            {
                Debug.Log("[AirAnchorPlacementTester] Cannot enter Marking mode: marker blocked.");
                currentXRMode = XRInteractionMode.Exploration;
                SetTurnObjectsActive(true);
                if (previewMarker != null)
                {
                    previewMarker.SetActive(false);
                }

                return;
            }

            currentXRMode = mode;

            bool isMarking = currentXRMode == XRInteractionMode.Marking;

            if (previewMarker != null)
            {
                previewMarker.SetActive(isMarking && placementActive);
            }

            SetTurnObjectsActive(!isMarking);

            Debug.Log("[AirAnchorPlacementTester] XR Mode changed to: " + currentXRMode);
        }

        private void SetTurnObjectsActive(bool active)
        {
            if (turnObjectsToEnableInExplorationMode == null)
            {
                return;
            }

            foreach (GameObject obj in turnObjectsToEnableInExplorationMode)
            {
                if (obj != null)
                {
                    obj.SetActive(active);
                }
            }
        }

        private bool GetLeftPrimaryButtonDown()
        {
            bool pressed = GetLeftControllerButton(CommonUsages.primaryButton);
            bool down = pressed && !_leftXWasPressed;
            _leftXWasPressed = pressed;
            return down;
        }

        private bool GetLeftControllerButton(InputFeatureUsage<bool> usage)
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left,
                devices);

            foreach (InputDevice device in devices)
            {
                if (device.TryGetFeatureValue(usage, out bool pressed) && pressed)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPointerOverBlockingUI()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            PointerEventData eventData = new PointerEventData(EventSystem.current);
            eventData.position = Input.mousePosition;

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            foreach (RaycastResult result in results)
            {
                GameObject go = result.gameObject;

                if (go.GetComponentInParent<Button>() != null)
                {
                    return true;
                }

                if (go.GetComponentInParent<TMP_InputField>() != null)
                {
                    return true;
                }

                if (go.GetComponentInParent<TMP_Dropdown>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
