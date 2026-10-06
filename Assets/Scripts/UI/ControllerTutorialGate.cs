using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;

namespace EchoSpace.UI
{
    /// <summary>
    /// First-run controller tutorial gate shown on scene start, before VR intro text.
    /// Does not change EchoSpace button bindings. Finishing the tutorial starts intro via UnityEvent.
    /// </summary>
    public class ControllerTutorialGate : MonoBehaviour
    {
        public static bool IsTutorialActive { get; private set; }

        public static bool IsBlockingGameplay
        {
            get
            {
                if (VRIntroGuideController.IsIntroActive)
                {
                    return true;
                }

                // Overlay hints stay interactive during the first-run controller tutorial
                // so users can practice the listed bindings. After intro, overlay mode
                // still pauses EchoSpace as before.
                return TutorialOverlayController.IsTutorialModeActive && !IsTutorialActive;
            }
        }

        [SerializeField] private GameObject tutorialRoot;
        [SerializeField] private GameObject practiceObjects;
        [SerializeField] private GameObject[] objectsDisabledDuringTutorial;
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text instructionText;
        [SerializeField] private bool startOnSceneStart = true;
        [SerializeField] private bool startAfterIntro = false;
        [SerializeField] private bool allowTriggerToContinue = true;
        [SerializeField] private bool allowPracticeDuringTutorial = true;
        [SerializeField] private UnityEvent onTutorialStarted;
        [SerializeField] private UnityEvent onTutorialFinished;

        [Header("Placement")]
        [SerializeField] private Transform playerCamera;
        [SerializeField] private float distanceFromCamera = 1.55f;
        [SerializeField] private float heightOffset = 0.12f;
        [SerializeField] private bool keepInFrontOfPlayer = false;
        [SerializeField] private bool parentToCamera = false;
        [SerializeField] private float extraYRotation = 180f;
        [SerializeField] private int canvasSortingOrder = 1100;
        [SerializeField] private TutorialOverlayController tutorialOverlay;
        [SerializeField] private KeyCode skipKey = KeyCode.Space;

        [TextArea(8, 16)]
        [SerializeField] private string defaultInstructionText =
            "左手 X：切換標記 / 探索模式\n" +
            "左手 Grip 長按：顯示 / 隱藏操作提示\n" +
            "右手 Trigger：放置記憶點 / 點擊 UI\n" +
            "右手 Joystick：標記模式調整距離 / 探索模式轉向\n" +
            "右手 A：開啟記憶整理面板\n" +
            "右手 Grip 長按：開啟 / 關閉 3D Timeline";

        [SerializeField] private string defaultTitleText = "手把操作說明";

        private bool _rightTriggerWasPressed;
        private bool[] _disabledObjectWasActive;
        private bool _fixedInWorld;
        private readonly List<XRInputDevice> _devices = new List<XRInputDevice>();

        private void Awake()
        {
            IsTutorialActive = false;

            if (startButton != null)
            {
                startButton.onClick.RemoveListener(FinishTutorial);
                startButton.onClick.AddListener(FinishTutorial);
            }

            if (tutorialRoot != null)
            {
                tutorialRoot.SetActive(false);
            }

            ResolvePracticeObjects();
            SetPracticeObjectsVisible(false);
        }

        private void Start()
        {
            if (startOnSceneStart)
            {
                StartTutorial();
            }
            else if (startAfterIntro)
            {
                Debug.Log("[ControllerTutorialGate] Waiting for external StartTutorial() call.");
            }
        }

        private void OnDestroy()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(FinishTutorial);
            }

            if (IsTutorialActive)
            {
                IsTutorialActive = false;
            }
        }

        private void Update()
        {
            if (!IsTutorialActive)
            {
                return;
            }

            if (Input.GetKeyDown(skipKey))
            {
                Debug.Log("[ControllerTutorialGate] Skip via keyboard " + skipKey);
                FinishTutorial();
                return;
            }

            if (!allowTriggerToContinue || allowPracticeDuringTutorial)
            {
                return;
            }

            bool triggerPressed = GetRightTriggerPressed();
            bool triggerDown = triggerPressed && !_rightTriggerWasPressed;
            _rightTriggerWasPressed = triggerPressed;
            if (triggerDown)
            {
                Debug.Log("[ControllerTutorialGate] Continue via right trigger");
                FinishTutorial();
            }
        }

        private void LateUpdate()
        {
            if (!IsTutorialActive || !keepInFrontOfPlayer)
            {
                return;
            }

            PlaceTutorialInFrontOfPlayer();
        }

        public void StartTutorial()
        {
            if (IsTutorialActive)
            {
                return;
            }

            IsTutorialActive = true;
            _fixedInWorld = !keepInFrontOfPlayer;

            if (titleText != null && string.IsNullOrWhiteSpace(titleText.text))
            {
                titleText.text = defaultTitleText;
            }

            if (instructionText != null && string.IsNullOrWhiteSpace(instructionText.text))
            {
                instructionText.text = defaultInstructionText;
            }

            if (!allowPracticeDuringTutorial)
            {
                PauseGameplayObjects();
            }

            Debug.Log("[StartupFlow] Controller tutorial started. Practice shortcuts are enabled.");
            SetPracticeObjectsVisible(true);

            if (tutorialRoot != null)
            {
                tutorialRoot.SetActive(true);
                EnsureTutorialCanvasReady();
                Debug.Log(
                    "[ControllerTutorialGate] Tutorial panel uses scene transform. pos="
                    + tutorialRoot.transform.position
                    + " parent=" + (tutorialRoot.transform.parent != null ? tutorialRoot.transform.parent.name : "null"));
            }
            else
            {
                Debug.LogWarning("[ControllerTutorialGate] tutorialRoot is not assigned. Create the UI in Unity and drag it here.");
            }

            if (tutorialOverlay != null)
            {
                tutorialOverlay.Hide();
            }

            CaptureTriggerEdge();

            if (onTutorialStarted != null)
            {
                onTutorialStarted.Invoke();
            }

            Debug.Log("[StartupFlow] Controller tutorial started");
            Debug.Log(
                allowPracticeDuringTutorial
                    ? "[ControllerTutorialGate] Tutorial started. Practice the listed bindings, then confirm with 我了解 / Start or Space."
                    : "[ControllerTutorialGate] Tutorial started. Confirm with Start button, right trigger, or Space.");
        }

        public void FinishTutorial()
        {
            if (!IsTutorialActive)
            {
                return;
            }

            IsTutorialActive = false;
            _fixedInWorld = false;

            if (tutorialOverlay != null)
            {
                tutorialOverlay.Hide();
            }

            if (tutorialRoot != null)
            {
                tutorialRoot.SetActive(false);
            }

            SetPracticeObjectsVisible(false);

            if (!allowPracticeDuringTutorial)
            {
                RestoreGameplayObjects();
            }

            if (onTutorialFinished != null)
            {
                onTutorialFinished.Invoke();
            }

            Debug.Log("[StartupFlow] Controller tutorial finished, starting intro");
            Debug.Log("[ControllerTutorialGate] Tutorial finished. Bind onTutorialFinished → VRIntroGuideController.PlayIntro().");
        }

        private bool PlaceTutorialInFrontOfPlayer()
        {
            if (tutorialRoot == null)
            {
                return false;
            }

            Transform cam = ResolvePlayerCamera();
            if (cam == null)
            {
                Debug.LogWarning("[ControllerTutorialGate] No player camera; tutorial stays at current transform.");
                return false;
            }

            Transform root = tutorialRoot.transform;
            bool attachToHead = keepInFrontOfPlayer && parentToCamera;
            if (attachToHead)
            {
                if (root.parent != cam)
                {
                    root.SetParent(cam, false);
                }

                root.localPosition = new Vector3(0f, heightOffset, distanceFromCamera);
                root.localRotation = Quaternion.Euler(0f, extraYRotation, 0f);
            }
            else
            {
                if (root.parent != null)
                {
                    root.SetParent(null, true);
                }

                Vector3 forward = cam.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
                }

                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.forward;
                }

                forward.Normalize();
                root.position = cam.position + forward * distanceFromCamera + Vector3.up * heightOffset;
                root.rotation = Quaternion.Euler(0f, cam.eulerAngles.y, 0f)
                    * Quaternion.Euler(0f, extraYRotation, 0f);
            }

            EnsureTutorialCanvasReady();
            return true;
        }

        private void EnsureTutorialCanvasReady()
        {
            if (tutorialRoot == null)
            {
                return;
            }

            Canvas canvas = tutorialRoot.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = tutorialRoot.GetComponentInChildren<Canvas>(true);
            }

            if (canvas == null)
            {
                return;
            }

            canvas.overrideSorting = true;
            if (canvas.sortingOrder < canvasSortingOrder)
            {
                canvas.sortingOrder = canvasSortingOrder;
            }

            Transform cam = ResolvePlayerCamera();
            if (cam == null)
            {
                return;
            }

            Camera camComponent = cam.GetComponent<Camera>();
            if (camComponent == null)
            {
                camComponent = cam.GetComponentInChildren<Camera>();
            }

            if (camComponent != null)
            {
                canvas.worldCamera = camComponent;
            }
        }

        private static bool IsCameraTrackingReady(Transform cam)
        {
            if (cam == null)
            {
                return false;
            }

            if (cam.localPosition.y >= 0.4f)
            {
                return true;
            }

            return Time.timeSinceLevelLoad >= 1.2f;
        }

        public void HidePracticeObjects()
        {
            SetPracticeObjectsVisible(false);
        }

        private void SetPracticeObjectsVisible(bool visible)
        {
            ResolvePracticeObjects();
            if (practiceObjects != null)
            {
                practiceObjects.SetActive(visible);
            }
        }

        private void ResolvePracticeObjects()
        {
            if (practiceObjects != null)
            {
                return;
            }

            UnityEngine.SceneManagement.Scene scene = gameObject.scene;
            if (!scene.IsValid())
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root != null && root.name == "PracticeObjects")
                {
                    practiceObjects = root;
                    return;
                }
            }
        }

        private Transform ResolvePlayerCamera()
        {
            if (playerCamera != null)
            {
                return playerCamera;
            }

            if (Camera.main != null)
            {
                playerCamera = Camera.main.transform;
                return playerCamera;
            }

            return null;
        }

        private void PauseGameplayObjects()
        {
            if (objectsDisabledDuringTutorial == null)
            {
                return;
            }

            _disabledObjectWasActive = new bool[objectsDisabledDuringTutorial.Length];
            for (int i = 0; i < objectsDisabledDuringTutorial.Length; i++)
            {
                GameObject obj = objectsDisabledDuringTutorial[i];
                if (obj == null)
                {
                    continue;
                }

                _disabledObjectWasActive[i] = obj.activeSelf;
                obj.SetActive(false);
            }
        }

        public void RestoreGameplayObjects()
        {
            if (objectsDisabledDuringTutorial == null)
            {
                return;
            }

            for (int i = 0; i < objectsDisabledDuringTutorial.Length; i++)
            {
                GameObject obj = objectsDisabledDuringTutorial[i];
                if (obj == null)
                {
                    continue;
                }

                bool restoreActive = true;
                if (_disabledObjectWasActive != null && i < _disabledObjectWasActive.Length)
                {
                    restoreActive = _disabledObjectWasActive[i];
                }

                obj.SetActive(restoreActive);
            }
        }

        private void CaptureTriggerEdge()
        {
            _rightTriggerWasPressed = GetRightTriggerPressed();
        }

        private bool GetRightTriggerPressed()
        {
            _devices.Clear();
            XRInputDevices.GetDevicesWithCharacteristics(
                XRInputDeviceCharacteristics.Controller | XRInputDeviceCharacteristics.Right,
                _devices);

            for (int i = 0; i < _devices.Count; i++)
            {
                XRInputDevice device = _devices[i];
                if (!device.isValid)
                {
                    continue;
                }

                if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
