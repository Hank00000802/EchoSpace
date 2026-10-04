using System.Collections;
using System.Collections.Generic;
using EchoSpace.Annotation;
using TMPro;
using UnityEngine;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace EchoSpace.UI
{
    /// <summary>
    /// Interactive VR controller tutorial overlay.
    /// Long-press left grip or keyboard H to enter / leave tutorial mode.
    /// Does not change EchoSpace bindings.
    /// </summary>
    public class TutorialOverlayController : MonoBehaviour
    {
        public static bool IsTutorialModeActive { get; private set; }

        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private KeyCode toggleKey = KeyCode.H;
        [SerializeField] private bool startHidden = true;
        [SerializeField] private ControllerHintItem[] hintItems;

        [Header("Interactive Hints")]
        [SerializeField] private ControllerHintItem leftGripHint;
        [SerializeField] private ControllerHintItem leftXHint;
        [SerializeField] private ControllerHintItem rightTriggerHint;
        [SerializeField] private ControllerHintItem rightJoystickHint;
        [SerializeField] private ControllerHintItem rightAHint;
        [SerializeField] private ControllerHintItem rightGripHint;
        [SerializeField] private float joystickToggleThreshold = 0.6f;

        [Header("VR Toggle")]
        [SerializeField] private bool enableVRGripToggle = true;
        [SerializeField] private bool useLeftGrip = true;
        [SerializeField] private float gripHoldSeconds = 0.8f;

        [Header("Switch Mode Guide")]
        [SerializeField] private GameObject switchModeGuide;
        [SerializeField] private TMP_Text switchModeGuideText;
        [SerializeField] private float switchModeGuideDuration = 5f;
        [SerializeField] private float switchModeGuideDistance = 1.35f;
        [SerializeField] private float switchModeGuideLeftOffset = 0.28f;
        [SerializeField] private float switchModeGuideUpOffset = 0.2f;
        [SerializeField] private bool switchModeGuideFollowView = true;
        [SerializeField] private bool switchModeGuideFlip180 = true;
        [SerializeField] private int switchModeGuideSortingOrder = 1300;
        [SerializeField] private string markingModeLabel = "目前是標記模式";
        [SerializeField] private string explorationModeLabel = "目前是探索模式";
        [SerializeField] private string tutorialModeLabel = "目前是教學模式";

        private float _gripHoldTimer;
        private bool _gripToggleTriggered;
        private bool _leftXWasPressed;
        private bool _modeGuideLeftXWasPressed;
        private bool _rightTriggerWasPressed;
        private bool _rightAWasPressed;
        private bool _rightGripWasPressed;
        private bool _rightJoystickWasPushed;
        private bool _showSwitchModeGuideThisFrame;
        private bool _switchModeGuideVisible;
        private Coroutine _switchModeGuideHideRoutine;
        private Transform _switchModeGuideOriginalParent;
        private AirAnchorPlacementTester _placementTester;
        private readonly List<XRInputDevice> _devices = new List<XRInputDevice>();

        private void Awake()
        {
            if (overlayRoot == null)
            {
                overlayRoot = gameObject;
            }

            IsTutorialModeActive = false;
            ResolveSwitchModeGuide();
            HideSwitchModeGuide();
            HideAllHints();
        }

        private void OnDestroy()
        {
            StopSwitchModeGuideHide();
            if (IsTutorialModeActive)
            {
                IsTutorialModeActive = false;
            }
        }

        private void Start()
        {
            if (leftGripHint != null)
            {
                leftGripHint.SetHintLabel("Left Grip：長按 顯示 / 隱藏操作提示");
            }

            if (rightGripHint != null)
            {
                rightGripHint.SetHintLabel("Right Grip：長按 開啟 / 關閉 3D Timeline");
            }

            if (startHidden)
            {
                ExitTutorialMode();
            }
            else
            {
                EnterTutorialMode();
            }
        }

        private void Update()
        {
            if (VRIntroGuideController.IsIntroActive)
            {
                return;
            }

            if (Input.GetKeyDown(toggleKey))
            {
                Toggle();
            }

            CheckVRGripToggle();
            CheckSwitchModeGuideInput();

            if (IsTutorialModeActive)
            {
                CheckTutorialHintInputs();
            }
        }

        private void LateUpdate()
        {
            if (_showSwitchModeGuideThisFrame)
            {
                _showSwitchModeGuideThisFrame = false;
                ShowSwitchModeGuide();
            }

            if (_switchModeGuideVisible && switchModeGuideFollowView)
            {
                PlaceSwitchModeGuideInView();
            }
        }

        private void CheckVRGripToggle()
        {
            if (!enableVRGripToggle)
            {
                return;
            }

            bool gripPressed = GetControllerBool(
                useLeftGrip ? XRInputDeviceCharacteristics.Left : XRInputDeviceCharacteristics.Right,
                XRCommonUsages.gripButton);

            if (gripPressed)
            {
                _gripHoldTimer += Time.deltaTime;

                if (_gripHoldTimer >= gripHoldSeconds && !_gripToggleTriggered)
                {
                    Toggle();
                    _gripToggleTriggered = true;
                }
            }
            else
            {
                _gripHoldTimer = 0f;
                _gripToggleTriggered = false;
            }
        }

        private void CheckSwitchModeGuideInput()
        {
            if (WasPressedThisFrame(IsLeftXPressed(), ref _modeGuideLeftXWasPressed))
            {
                _showSwitchModeGuideThisFrame = true;
            }
        }

        public void ShowCurrentModeGuide()
        {
            ShowSwitchModeGuide();
        }

        private void ShowSwitchModeGuide()
        {
            ResolveSwitchModeGuide();
            if (switchModeGuide == null)
            {
                Debug.LogWarning("[TutorialOverlay] SwitchModeGuide is not assigned. Create it under TutorialOverlayHost and drag it here.");
                return;
            }

            if (switchModeGuideText == null)
            {
                switchModeGuideText = switchModeGuide.GetComponentInChildren<TMP_Text>(true);
            }

            if (switchModeGuideText != null)
            {
                switchModeGuideText.text = GetCurrentModeLabel();
            }

            if (switchModeGuide.transform.parent != null
                && _switchModeGuideOriginalParent == null)
            {
                _switchModeGuideOriginalParent = switchModeGuide.transform.parent;
            }

            switchModeGuide.SetActive(true);
            EnsureSwitchModeGuideCanvasSorting();
            PlaceSwitchModeGuideInView();
            _switchModeGuideVisible = true;

            StopSwitchModeGuideHide();
            if (!IsAnyTutorialActive())
            {
                float duration = switchModeGuideDuration > 0f ? switchModeGuideDuration : 5f;
                _switchModeGuideHideRoutine = StartCoroutine(HideSwitchModeGuideAfterDelay(duration));
            }
            Debug.Log("[TutorialOverlay] SwitchModeGuide shown: " + (switchModeGuideText != null ? switchModeGuideText.text : "(no text)"));
        }

        private void HideSwitchModeGuide()
        {
            StopSwitchModeGuideHide();
            _switchModeGuideVisible = false;
            _showSwitchModeGuideThisFrame = false;

            if (switchModeGuide == null)
            {
                return;
            }

            switchModeGuide.SetActive(false);

            if (_switchModeGuideOriginalParent != null
                && switchModeGuide.transform.parent != _switchModeGuideOriginalParent)
            {
                switchModeGuide.transform.SetParent(_switchModeGuideOriginalParent, true);
            }
        }

        private IEnumerator HideSwitchModeGuideAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _switchModeGuideHideRoutine = null;
            HideSwitchModeGuide();
        }

        private void StopSwitchModeGuideHide()
        {
            if (_switchModeGuideHideRoutine == null)
            {
                return;
            }

            StopCoroutine(_switchModeGuideHideRoutine);
            _switchModeGuideHideRoutine = null;
        }

        private void PlaceSwitchModeGuideInView()
        {
            if (switchModeGuide == null)
            {
                return;
            }

            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null)
            {
                return;
            }

            Transform root = switchModeGuide.transform;
            root.position = cam.position
                + cam.forward * switchModeGuideDistance
                + cam.up * switchModeGuideUpOffset
                + cam.right * -switchModeGuideLeftOffset;

            Vector3 dir = root.position - cam.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                root.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }

            if (switchModeGuideFlip180)
            {
                root.Rotate(0f, 180f, 0f, Space.Self);
            }
        }

        private void EnsureSwitchModeGuideCanvasSorting()
        {
            if (switchModeGuide == null)
            {
                return;
            }

            Canvas canvas = switchModeGuide.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = switchModeGuide.GetComponentInChildren<Canvas>(true);
            }

            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = switchModeGuideSortingOrder;
            if (Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }
        }

        private string GetCurrentModeLabel()
        {
            if (IsAnyTutorialActive())
            {
                return string.IsNullOrEmpty(tutorialModeLabel) ? "目前是教學模式" : tutorialModeLabel;
            }
            if (_placementTester == null)
            {
                _placementTester = FindObjectOfType<AirAnchorPlacementTester>();
            }

            if (_placementTester != null
                && _placementTester.currentXRMode == XRInteractionMode.Exploration)
            {
                return explorationModeLabel;
            }

            return markingModeLabel;
        }

        private static bool IsAnyTutorialActive()
        {
            return IsTutorialModeActive || ControllerTutorialGate.IsTutorialActive;
        }

        private void ResolveSwitchModeGuide()
        {
            if (switchModeGuide == null)
            {
                Transform found = FindChildByName(transform, "SwitchModeGuide");
                if (found != null)
                {
                    switchModeGuide = found.gameObject;
                }
            }

            if (switchModeGuideText == null && switchModeGuide != null)
            {
                switchModeGuideText = switchModeGuide.GetComponentInChildren<TMP_Text>(true);
            }
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (string.Equals(root.name, name, System.StringComparison.Ordinal))
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

        private void CheckTutorialHintInputs()
        {
            if (WasPressedThisFrame(IsLeftXPressed(), ref _leftXWasPressed))
            {
                ToggleHint(leftXHint);
            }

            if (WasPressedThisFrame(
                GetControllerBool(XRInputDeviceCharacteristics.Right, XRCommonUsages.triggerButton),
                ref _rightTriggerWasPressed))
            {
                ToggleHint(rightTriggerHint);
            }

            if (WasPressedThisFrame(IsRightAPressed(), ref _rightAWasPressed))
            {
                ToggleHint(rightAHint);
            }

            if (WasPressedThisFrame(
                GetControllerBool(XRInputDeviceCharacteristics.Right, XRCommonUsages.gripButton),
                ref _rightGripWasPressed))
            {
                ToggleHint(rightGripHint);
            }

            bool joystickPushed = IsRightJoystickPushed();
            if (WasPressedThisFrame(joystickPushed, ref _rightJoystickWasPushed))
            {
                ToggleHint(rightJoystickHint);
            }
        }

        public void Toggle()
        {
            if (IsTutorialModeActive)
            {
                ExitTutorialMode();
            }
            else
            {
                EnterTutorialMode();
            }
        }

        public void Show()
        {
            EnterTutorialMode();
        }

        public void Hide()
        {
            ExitTutorialMode();
        }

        private void EnterTutorialMode()
        {
            IsTutorialModeActive = true;

            if (overlayRoot != null)
            {
                overlayRoot.SetActive(true);
            }

            ShowOnlyLeftGripHint();
            CaptureHintInputEdges();
            ShowSwitchModeGuide();
            Debug.Log("[TutorialOverlay] Tutorial mode ON");
        }

        private void ExitTutorialMode()
        {
            IsTutorialModeActive = false;
            HideSwitchModeGuide();
            HideAllHints();

            if (overlayRoot != null && overlayRoot != gameObject)
            {
                overlayRoot.SetActive(false);
            }
            else if (overlayRoot == gameObject)
            {
                Debug.LogWarning("[TutorialOverlay] overlayRoot is this GameObject; hiding would disable H toggle. Assign a child instead.");
            }

            CaptureHintInputEdges();
            Debug.Log("[TutorialOverlay] Tutorial mode OFF");
        }

        private void ShowOnlyLeftGripHint()
        {
            HideAllHints();

            if (leftGripHint != null)
            {
                leftGripHint.SetHintLabel("Left Grip：長按 顯示 / 隱藏操作提示");
                leftGripHint.Show();
            }
        }

        public void ShowAllBasicHints()
        {
            if (overlayRoot != null)
            {
                overlayRoot.SetActive(true);
            }

            if (leftGripHint != null)
            {
                leftGripHint.SetHintLabel("左手 Grip 長按：顯示 / 隱藏操作提示");
                leftGripHint.Show();
            }

            if (leftXHint != null)
            {
                leftXHint.SetHintLabel("左手 X：切換標記 / 探索模式");
                leftXHint.Show();
            }

            if (rightTriggerHint != null)
            {
                rightTriggerHint.SetHintLabel("右手 Trigger：放置記憶點 / 點擊 UI");
                rightTriggerHint.Show();
            }

            if (rightJoystickHint != null)
            {
                rightJoystickHint.SetHintLabel("右手 Joystick：標記模式調整距離 / 探索模式轉向");
                rightJoystickHint.Show();
            }

            if (rightAHint != null)
            {
                rightAHint.SetHintLabel("右手 A：開啟記憶整理面板");
                rightAHint.Show();
            }

            if (rightGripHint != null)
            {
                rightGripHint.SetHintLabel("右手 Grip 長按：開啟 / 關閉 3D Timeline");
                rightGripHint.Show();
            }

            Debug.Log("[TutorialOverlay] ShowAllBasicHints");
            ShowSwitchModeGuide();
        }

        public void HideAllHints()
        {
            HideHint(leftGripHint);
            HideHint(leftXHint);
            HideHint(rightTriggerHint);
            HideHint(rightJoystickHint);
            HideHint(rightAHint);
            HideHint(rightGripHint);

            if (hintItems == null)
            {
                return;
            }

            for (int i = 0; i < hintItems.Length; i++)
            {
                HideHint(hintItems[i]);
            }
        }

        private void ToggleHint(ControllerHintItem item)
        {
            if (item == null)
            {
                return;
            }

            item.Toggle();
            Debug.Log("[TutorialOverlay] Toggle hint: " + item.gameObject.name);
        }

        private static void HideHint(ControllerHintItem item)
        {
            if (item != null)
            {
                item.Hide();
            }
        }

        private void CaptureHintInputEdges()
        {
            _leftXWasPressed = IsLeftXPressed();
            _rightTriggerWasPressed = GetControllerBool(XRInputDeviceCharacteristics.Right, XRCommonUsages.triggerButton);
            _rightAWasPressed = IsRightAPressed();
            _rightGripWasPressed = GetControllerBool(XRInputDeviceCharacteristics.Right, XRCommonUsages.gripButton);
            _rightJoystickWasPushed = IsRightJoystickPushed();
        }

        private static bool WasPressedThisFrame(bool pressed, ref bool wasPressed)
        {
            bool down = pressed && !wasPressed;
            wasPressed = pressed;
            return down;
        }

        // EchoSpace already reads Vive Left X / Right A as menuButton in the scene,
        // not primaryButton. Accept either so the tutorial matches working shortcuts.
        private bool IsLeftXPressed()
        {
            return GetControllerBool(XRInputDeviceCharacteristics.Left, XRCommonUsages.primaryButton)
                || GetControllerBool(XRInputDeviceCharacteristics.Left, XRCommonUsages.menuButton);
        }

        private bool IsRightAPressed()
        {
            return GetControllerBool(XRInputDeviceCharacteristics.Right, XRCommonUsages.primaryButton)
                || GetControllerBool(XRInputDeviceCharacteristics.Right, XRCommonUsages.menuButton);
        }

        private bool IsRightJoystickPushed()
        {
            Vector2 axis = GetControllerAxis2D(
                XRInputDeviceCharacteristics.Right,
                XRCommonUsages.primary2DAxis);
            return axis.magnitude >= joystickToggleThreshold;
        }

        private bool GetControllerBool(XRInputDeviceCharacteristics hand, InputFeatureUsage<bool> usage)
        {
            _devices.Clear();
            XRInputDevices.GetDevicesWithCharacteristics(
                hand | XRInputDeviceCharacteristics.Controller,
                _devices);

            for (int i = 0; i < _devices.Count; i++)
            {
                XRInputDevice device = _devices[i];
                if (!device.isValid)
                {
                    continue;
                }

                if (device.TryGetFeatureValue(usage, out bool value) && value)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector2 GetControllerAxis2D(XRInputDeviceCharacteristics hand, InputFeatureUsage<Vector2> usage)
        {
            _devices.Clear();
            XRInputDevices.GetDevicesWithCharacteristics(
                hand | XRInputDeviceCharacteristics.Controller,
                _devices);

            for (int i = 0; i < _devices.Count; i++)
            {
                XRInputDevice device = _devices[i];
                if (!device.isValid)
                {
                    continue;
                }

                if (device.TryGetFeatureValue(usage, out Vector2 value))
                {
                    return value;
                }
            }

            return Vector2.zero;
        }
    }
}
