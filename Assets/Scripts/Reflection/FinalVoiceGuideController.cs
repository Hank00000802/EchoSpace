using System.Collections;
using EchoSpace.Core;
using EchoSpace.LineOfLife;
using EchoSpace.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EchoSpace.Reflection
{
    public class FinalVoiceGuideController : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip guideClip;
        [SerializeField] private AudioClip secondClip;
        [Tooltip("Unused. The second clip now starts when NextButton is pressed.")]
        [SerializeField] private float secondClipDelay = 30f;
        [SerializeField] private float playDelay = 0.8f;
        [SerializeField] private bool restartIfAlreadyPlaying = true;
        [SerializeField] private GameObject subtitleRoot;
        [SerializeField] private bool showSubtitleWhilePlaying = false;
        [TextArea(4, 10)]
        [SerializeField] private string secondClipPromptText =
            "上述這些只是幫你提示~\n接下來，你可以試試說說看自己是如何經歷這段生活轉變，一路從轉變前走到現在的。這段過程，對你來說又帶來了什麼意義？\n這也沒有標準答案，只需要想到什麼說什麼就好~";

        [Header("After Switch Panels")]
        [Tooltip("Shown instead of AfterSwitchViewRoot. Timing starts when Switch01 begins.")]
        [SerializeField] private GameObject continuityRoot;
        [SerializeField] private GameObject differenceRoot;
        [Tooltip("Seconds after Switch01 starts.")]
        [SerializeField] private float continuityShowDelay = 0f;
        [SerializeField] private float differenceShowDelay = 0f;
        [Tooltip("Offset from the default in-front-of-camera spot. X = right, Y = up, Z = farther.")]
        [SerializeField] private Vector3 continuityOffset = Vector3.zero;
        [SerializeField] private Vector3 differenceOffset = Vector3.zero;
        [Tooltip("Yaw the Continuity / Difference panels 180 degrees when they appear.")]
        [SerializeField] private bool panelFlip180 = true;

        [Header("Photobook")]
        [Tooltip("Photobook Canvas / root to show, hide, and place. Drag Photobook_Canvas here.")]
        [SerializeField] private GameObject photobookRoot;
        [Tooltip("Optional inner Photobook Panel. Leave empty to use Photobook Root only.")]
        [SerializeField] private GameObject photobookPanel;
        [Tooltip("World-space height above the midpoint of Continuity / Difference.")]
        [SerializeField] private float photobookHeightOffset = 0.45f;
        [Tooltip("How far in front of the player the Photobook Canvas is placed.")]
        [SerializeField] private float photobookDistanceFromPlayer = 1.2f;
        [SerializeField] private Button continuityButton;
        [SerializeField] private Button differenceButton;
        [Tooltip("Legacy fallback. Prefer Continuity / Difference CardAnchorRoot.")]
        [SerializeField] private Transform photobookCardAnchorRoot;
        [Tooltip("Present cards spawn here when Continuity is pressed.")]
        [SerializeField] private Transform continuityCardAnchorRoot;
        [Tooltip("Past cards spawn here when Difference is pressed.")]
        [SerializeField] private Transform differenceCardAnchorRoot;
        [SerializeField] private LineOfLife3DViewController lineOfLife3DView;

        [Header("Subtitle Placement")]
        [SerializeField] private Transform playerCamera;
        [Tooltip("Optional. Usually SwitchView Position / targetViewTransform.")]
        [SerializeField] private Transform subtitleAnchor;
        [SerializeField] private bool placeSubtitleInFrontOfPlayer = true;
        [SerializeField] private float subtitleDistanceFromCamera = 1.2f;
        [SerializeField] private float subtitleVerticalOffset = -0.25f;
        [SerializeField] private bool subtitleFaceCamera = true;
        [SerializeField] private bool subtitleLockYOnly = true;
        [SerializeField] private bool subtitleFlip180 = true;
        [Tooltip("Draw AfterSwitchViewRoot in front of the 3D timeline.")]
        [SerializeField] private int afterSwitchViewSortingOrder = 2500;

        [Header("Next Voice")]
        [Tooltip("AfterSwitchViewRoot / Prompt / Canvas / NextButton. Shown after Switch01 finishes.")]
        [SerializeField] private Button nextButton;
        [Tooltip("AfterSwitchViewRoot / Prompt / Canvas / 3DLineButton. Opens the 3D timeline.")]
        [SerializeField] private Button lineOfLife3DButton;
        [Tooltip("AfterSwitchViewRoot / Prompt / Canvas / CloseButton. Hides the Prompt panel.")]
        [SerializeField] private Button afterSwitchCloseButton;
        [Tooltip("AfterSwitchViewRoot / OpenPromptCanvas / OpenPromptButton. Shown only while Prompt is hidden.")]
        [SerializeField] private Button openPromptButton;
        [Tooltip("Meters toward the player from the Prompt. Positive = closer, negative = farther. 0 keeps the Prompt position.")]
        [SerializeField] private float openPromptOffsetTowardPlayer = 0f;
        [Tooltip("OpenPromptButton will not come closer than this many meters.")]
        [SerializeField] private float openPromptMinDistanceFromPlayer = 0.4f;

        private Coroutine playRoutine;
        private bool _secondClipPlayed;
        private bool _hasPanelPose;
        private Vector3 _panelOrigin;
        private Vector3 _panelForward;
        private Vector3 _panelRight;
        private float _panelY;
        private TMP_Text subtitlePromptText;
        private string originalPromptText = "";
        private bool hasOriginalPrompt;
        private enum PhotobookCardSet
        {
            None,
            Present,
            Past
        }

        private PhotobookCardSet _activePhotobookSet = PhotobookCardSet.None;
        private Transform _presentCardGroup;
        private Transform _pastCardGroup;
        private bool _presentCardsSpawned;
        private bool _pastCardsSpawned;
        private int _lastPhotobookToggleFrame = -1;
        private bool _photobookPlaced;
        private bool _openedTimelineFrom3DLineButton;

        private void Awake()
        {
            HideAfterSwitchPanels();
            CacheSubtitlePrompt();
            PreparePhotobookButtonCanvases();
            BindPhotobookButtons();
            BindNextVoiceButton();
            Bind3DLineButton();
            BindAfterSwitchPromptToggleButtons();
            SetNextButtonVisible(false);
            Set3DLineButtonVisible(false);
            RefreshOpenPromptButtonVisibility();
            if (subtitleRoot != null)
            {
                subtitleRoot.SetActive(false);
            }
        }

        private void Start()
        {
            PreparePhotobookButtonCanvases();
            BindPhotobookButtons();
            BindNextVoiceButton();
            Bind3DLineButton();
            BindAfterSwitchPromptToggleButtons();
            SetNextButtonVisible(false);
            Set3DLineButtonVisible(false);
            RefreshOpenPromptButtonVisibility();
        }

        private void OnDisable()
        {
            StopGuide();
        }

        public void PlayGuide()
        {
            if (audioSource == null || guideClip == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] audioSource or guideClip is not assigned.");
                return;
            }

            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            playRoutine = StartCoroutine(PlayGuideRoutine());
        }

        public void StopGuide()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            if (audioSource != null)
            {
                audioSource.Stop();
            }

            if (subtitleRoot != null)
            {
                subtitleRoot.SetActive(false);
            }

            RestoreSubtitlePrompt();
            HideAfterSwitchPanels();
            SetNextButtonVisible(false);
            Set3DLineButtonVisible(false);
            RestoreAfterSwitchPrompt();
            _openedTimelineFrom3DLineButton = false;
            LineOfLife3DViewController view = ResolveLineOfLife3DView();
            if (view != null)
            {
                view.Closed -= HandleLineOfLife3DClosed;
            }
            _secondClipPlayed = false;
            _hasPanelPose = false;
        }

        public void ReplayGuide()
        {
            StopGuide();
            PlayGuide();
        }

        private IEnumerator PlayGuideRoutine()
        {
            float delay = Mathf.Clamp(playDelay, 0.5f, 1f);
            yield return new WaitForSeconds(delay);

            if (restartIfAlreadyPlaying && audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            audioSource.clip = guideClip;
            audioSource.Play();
            CacheSharedPanelPose();
            _secondClipPlayed = false;
            BindNextVoiceButton();
            Bind3DLineButton();
            BindAfterSwitchPromptToggleButtons();
            SetNextButtonVisible(false);
            Set3DLineButtonVisible(false);
            RefreshOpenPromptButtonVisibility();
            Debug.Log("[FinalVoiceGuide] Playing guide clip: " + guideClip.name);

            if (showSubtitleWhilePlaying && subtitleRoot != null)
            {
                if (placeSubtitleInFrontOfPlayer)
                {
                    PlacePanelInFrontOfPlayer(subtitleRoot.transform, Vector3.zero, subtitleFlip180);
                }

                subtitleRoot.SetActive(true);
                PreparePhotobookButtonCanvas(subtitleRoot);
                BringAfterSwitchViewToFront();
                RefreshOpenPromptButtonVisibility();
            }

            HideAfterSwitchPanels();

            bool secondPlayed = secondClip == null;
            bool nextShown = false;

            while (!secondPlayed)
            {
                if (!_secondClipPlayed)
                {
                    bool firstClipFinished = audioSource == null || !audioSource.isPlaying;
                    if (firstClipFinished && !nextShown)
                    {
                        SetNextButtonVisible(true);
                        Set3DLineButtonVisible(true);
                        nextShown = true;
                        Debug.Log("[FinalVoiceGuide] Switch01 finished; waiting for NextButton.");
                    }
                }
                else
                {
                    secondPlayed = true;
                    SetNextButtonVisible(false);
                }

                if (secondPlayed)
                {
                    break;
                }

                yield return null;
            }

            playRoutine = null;
        }

        public void On3DLineButtonClicked()
        {
            LineOfLife3DViewController view = ResolveLineOfLife3DView();
            if (view == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] LineOfLife3DViewController is not assigned; 3D timeline cannot open after switch.");
                return;
            }

            view.Closed -= HandleLineOfLife3DClosed;
            view.Closed += HandleLineOfLife3DClosed;
            ShowLineOfLife3DAfterSwitch();
            if (!view.IsVisible)
            {
                return;
            }

            _openedTimelineFrom3DLineButton = true;
            SetAfterSwitchPromptVisible(false);
            Clear3DLineButtonSelection();
            Debug.Log("[FinalVoiceGuide] 3DLineButton opened 3D timeline and hid AfterSwitch Prompt.");
        }

        public void HandleLineOfLife3DClosed()
        {
            if (!_openedTimelineFrom3DLineButton)
            {
                return;
            }

            _openedTimelineFrom3DLineButton = false;
            SetAfterSwitchPromptVisible(true);
            BringAfterSwitchViewToFront();
            Debug.Log("[FinalVoiceGuide] Restored AfterSwitch Prompt after 3D timeline CloseButton.");
        }

        private void ShowLineOfLife3DAfterSwitch()
        {
            LineOfLife3DViewController view = ResolveLineOfLife3DView();
            if (view == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] LineOfLife3DViewController is not assigned; 3D timeline cannot open after switch.");
                return;
            }

            view.OpenAfterSwitchView();
            BringAfterSwitchViewToFront();
            Debug.Log("[FinalVoiceGuide] 3DLineButton opened 3D timeline.");
        }

        private void Bind3DLineButton()
        {
            Resolve3DLineButton();
            if (lineOfLife3DButton == null)
            {
                Debug.LogWarning(
                    "[FinalVoiceGuide] 3DLineButton was not found under AfterSwitchViewRoot / Prompt / Canvas.");
                return;
            }

            BindPhotobookButton(lineOfLife3DButton);
            Navigation navigation = lineOfLife3DButton.navigation;
            navigation.mode = Navigation.Mode.None;
            lineOfLife3DButton.navigation = navigation;
            if (subtitleRoot != null)
            {
                PreparePhotobookButtonCanvas(subtitleRoot);
            }
        }

        private void BindAfterSwitchPromptToggleButtons()
        {
            ResolveAfterSwitchCloseButton();
            ResolveOpenPromptButton();

            if (afterSwitchCloseButton != null)
            {
                BindPhotobookButton(afterSwitchCloseButton);
                Navigation closeNavigation = afterSwitchCloseButton.navigation;
                closeNavigation.mode = Navigation.Mode.None;
                afterSwitchCloseButton.navigation = closeNavigation;
                afterSwitchCloseButton.onClick.RemoveListener(OnAfterSwitchCloseClicked);
                afterSwitchCloseButton.onClick.AddListener(OnAfterSwitchCloseClicked);
            }
            else
            {
                Debug.LogWarning(
                    "[FinalVoiceGuide] CloseButton was not found under AfterSwitchViewRoot / Prompt / Canvas.");
            }

            if (openPromptButton != null)
            {
                BindPhotobookButton(openPromptButton);
                Navigation openNavigation = openPromptButton.navigation;
                openNavigation.mode = Navigation.Mode.None;
                openPromptButton.navigation = openNavigation;
                openPromptButton.onClick.RemoveListener(OnOpenPromptClicked);
                openPromptButton.onClick.AddListener(OnOpenPromptClicked);
                Canvas openPromptCanvas = openPromptButton.GetComponentInParent<Canvas>();
                if (openPromptCanvas != null)
                {
                    PreparePhotobookButtonCanvas(openPromptCanvas.gameObject);
                }
            }
            else
            {
                Debug.LogWarning(
                    "[FinalVoiceGuide] OpenPromptButton was not found under AfterSwitchViewRoot / OpenPromptCanvas.");
            }

            if (subtitleRoot != null)
            {
                PreparePhotobookButtonCanvas(subtitleRoot);
            }

            RefreshOpenPromptButtonVisibility();
        }

        private void ResolveAfterSwitchCloseButton()
        {
            if (afterSwitchCloseButton != null)
            {
                return;
            }

            GameObject prompt = ResolveAfterSwitchPrompt();
            Transform searchRoot = prompt != null ? prompt.transform : subtitleRoot != null ? subtitleRoot.transform : null;
            Transform found = FindChildNamed(searchRoot, "CloseButton");
            if (found == null)
            {
                return;
            }

            afterSwitchCloseButton = found.GetComponent<Button>();
            if (afterSwitchCloseButton == null)
            {
                afterSwitchCloseButton = found.GetComponentInChildren<Button>(true);
            }
        }

        private void ResolveOpenPromptButton()
        {
            if (openPromptButton != null)
            {
                return;
            }

            Transform searchRoot = subtitleRoot != null ? subtitleRoot.transform : null;
            Transform found = FindChildNamed(searchRoot, "OpenPromptButton");
            if (found == null)
            {
                return;
            }

            openPromptButton = found.GetComponent<Button>();
            if (openPromptButton == null)
            {
                openPromptButton = found.GetComponentInChildren<Button>(true);
            }
        }

        private void RefreshOpenPromptButtonVisibility()
        {
            ResolveOpenPromptButton();
            if (openPromptButton == null)
            {
                return;
            }

            GameObject prompt = ResolveAfterSwitchPrompt();
            bool promptVisible = prompt != null && prompt.activeSelf;
            bool rootVisible = subtitleRoot == null || subtitleRoot.activeSelf;
            bool show = rootVisible && !promptVisible;
            openPromptButton.gameObject.SetActive(show);
            if (show)
            {
                PlaceOpenPromptAtPromptPose();
            }
        }

        private void PlaceOpenPromptAtPromptPose()
        {
            GameObject prompt = ResolveAfterSwitchPrompt();
            ResolveOpenPromptButton();
            if (prompt == null || openPromptButton == null)
            {
                return;
            }

            Transform canvas = openPromptButton.transform.parent;
            Transform target = canvas != null ? canvas : openPromptButton.transform;
            target.position = GetOpenPromptPosition(prompt.transform.position);
            FaceWorldUiTowardCamera(target);
            EnsureOpenPromptBillboard(target);

            RectTransform buttonRect = openPromptButton.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchoredPosition3D = Vector3.zero;
                buttonRect.localRotation = Quaternion.identity;
                Vector3 scale = buttonRect.localScale;
                buttonRect.localScale = new Vector3(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z));
            }
            else
            {
                openPromptButton.transform.localPosition = Vector3.zero;
                openPromptButton.transform.localRotation = Quaternion.identity;
            }
        }

        private Vector3 GetOpenPromptPosition(Vector3 promptPosition)
        {
            EnsurePlayerCamera();
            if (playerCamera == null)
            {
                return promptPosition;
            }

            Vector3 fromPromptToPlayer = playerCamera.position - promptPosition;
            fromPromptToPlayer.y = 0f;
            float currentDistance = fromPromptToPlayer.magnitude;
            if (currentDistance < 0.0001f)
            {
                return promptPosition;
            }

            float minDistance = Mathf.Max(0.05f, openPromptMinDistanceFromPlayer);
            float maxToward = Mathf.Max(0f, currentDistance - minDistance);
            float toward = Mathf.Clamp(openPromptOffsetTowardPlayer, -10f, maxToward);
            Vector3 position = promptPosition + fromPromptToPlayer / currentDistance * toward;
            position.y = promptPosition.y;
            return position;
        }

        private void FaceWorldUiTowardCamera(Transform target)
        {
            if (target == null)
            {
                return;
            }

            EnsurePlayerCamera();
            Transform cam = playerCamera;
            if (cam == null)
            {
                return;
            }

            Vector3 toCamera = cam.position - target.position;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                return;
            }

            target.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            target.Rotate(0f, 180f, 0f, Space.Self);
        }

        private void EnsureOpenPromptBillboard(Transform target)
        {
            if (target == null)
            {
                return;
            }

            BillboardToCamera billboard = target.GetComponent<BillboardToCamera>();
            if (billboard == null)
            {
                billboard = target.gameObject.AddComponent<BillboardToCamera>();
            }

            EnsurePlayerCamera();
            billboard.targetCamera = playerCamera;
            billboard.lockYOnly = false;
            billboard.flip180 = true;
        }

        private void Clear3DLineButtonSelection()
        {
            if (lineOfLife3DButton != null)
            {
                lineOfLife3DButton.OnDeselect(null);
            }

            if (EventSystem.current != null
                && (lineOfLife3DButton == null
                    || EventSystem.current.currentSelectedGameObject == lineOfLife3DButton.gameObject))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            if (isActiveAndEnabled)
            {
                StartCoroutine(Clear3DLineButtonSelectionNextFrame());
            }
        }

        private IEnumerator Clear3DLineButtonSelectionNextFrame()
        {
            yield return null;
            if (EventSystem.current != null
                && lineOfLife3DButton != null
                && EventSystem.current.currentSelectedGameObject == lineOfLife3DButton.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void Resolve3DLineButton()
        {
            if (lineOfLife3DButton != null)
            {
                return;
            }

            Transform searchRoot = subtitleRoot != null ? subtitleRoot.transform : transform;
            Transform found = FindChildNamed(searchRoot, "3DLineButton");
            if (found == null)
            {
                found = FindChildNamed(searchRoot, "3DLineButon");
            }

            if (found == null)
            {
                return;
            }

            lineOfLife3DButton = found.GetComponent<Button>();
            if (lineOfLife3DButton == null)
            {
                lineOfLife3DButton = found.GetComponentInChildren<Button>(true);
            }
        }

        private void BringAfterSwitchViewToFront()
        {
            if (subtitleRoot == null)
            {
                return;
            }

            int order = afterSwitchViewSortingOrder;
            Canvas[] canvases = subtitleRoot.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null)
                {
                    continue;
                }

                canvas.overrideSorting = true;
                canvas.sortingOrder = order;
            }

            Renderer[] renderers = subtitleRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.sortingOrder = order;
            }

            Debug.Log("[FinalVoiceGuide] AfterSwitchViewRoot sortingOrder=" + order);
        }

        public void OnNextVoiceClicked()
        {
            PlaySecondClip();
        }

        private void PlaySecondClip()
        {
            if (_secondClipPlayed || secondClip == null || audioSource == null)
            {
                return;
            }

            audioSource.Stop();
            audioSource.clip = secondClip;
            audioSource.Play();
            _secondClipPlayed = true;
            SetNextButtonVisible(false);
            ApplySecondClipPrompt();
            Debug.Log("[FinalVoiceGuide] Playing second clip: " + secondClip.name);
        }

        private void BindNextVoiceButton()
        {
            ResolveNextButton();
            if (nextButton == null)
            {
                Debug.LogWarning(
                    "[FinalVoiceGuide] NextButton was not found under AfterSwitchViewRoot / Prompt / Canvas.");
                return;
            }

            BindPhotobookButton(nextButton);
            UiSfx.BindClick(nextButton);
            if (subtitleRoot != null)
            {
                PreparePhotobookButtonCanvas(subtitleRoot);
            }
            else
            {
                PreparePhotobookButtonCanvas(nextButton.gameObject);
            }
        }

        private void ResolveNextButton()
        {
            if (nextButton != null)
            {
                return;
            }

            Transform searchRoot = subtitleRoot != null ? subtitleRoot.transform : transform;
            Transform found = FindChildNamed(searchRoot, "NextButton");
            if (found == null)
            {
                return;
            }

            nextButton = found.GetComponent<Button>();
            if (nextButton == null)
            {
                nextButton = found.GetComponentInChildren<Button>(true);
            }
        }

        private void SetNextButtonVisible(bool visible)
        {
            ResolveNextButton();
            if (nextButton == null)
            {
                return;
            }

            if (visible && subtitleRoot != null && !subtitleRoot.activeSelf)
            {
                subtitleRoot.SetActive(true);
            }

            nextButton.gameObject.SetActive(visible);
            RefreshOpenPromptButtonVisibility();
        }

        private void Set3DLineButtonVisible(bool visible)
        {
            Resolve3DLineButton();
            if (lineOfLife3DButton == null)
            {
                return;
            }

            if (visible && subtitleRoot != null && !subtitleRoot.activeSelf)
            {
                subtitleRoot.SetActive(true);
            }

            lineOfLife3DButton.gameObject.SetActive(visible);
            RefreshOpenPromptButtonVisibility();
        }

        private static Transform FindChildNamed(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            if (string.Equals(root.name, childName, System.StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildNamed(root.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void ShowTimedPanelIfDue(
            GameObject root,
            float showDelay,
            Vector3 offset,
            float elapsed,
            ref bool shown)
        {
            if (shown || root == null || elapsed + 0.0001f < Mathf.Max(0f, showDelay))
            {
                return;
            }

            if (placeSubtitleInFrontOfPlayer)
            {
                PlacePanelInFrontOfPlayer(root.transform, offset, panelFlip180);
            }

            root.SetActive(true);
            shown = true;
            PreparePhotobookButtonCanvas(root);
            BindPhotobookButtons();
            Debug.Log("[FinalVoiceGuide] Shown " + root.name + " at t=" + elapsed.ToString("0.00") + "s after Switch01");
        }

        private void ApplySecondClipPrompt()
        {
            CacheSubtitlePrompt();
            if (subtitlePromptText != null && !string.IsNullOrEmpty(secondClipPromptText))
            {
                subtitlePromptText.text = secondClipPromptText;
            }

            if (subtitleRoot == null)
            {
                return;
            }

            if (placeSubtitleInFrontOfPlayer)
            {
                PlacePanelInFrontOfPlayer(subtitleRoot.transform, Vector3.zero, subtitleFlip180);
            }

            subtitleRoot.SetActive(true);
        }

        private void CacheSubtitlePrompt()
        {
            if (subtitlePromptText != null || subtitleRoot == null)
            {
                return;
            }

            Transform prompt = subtitleRoot.transform.Find("Prompt");
            if (prompt != null)
            {
                subtitlePromptText = prompt.GetComponent<TMP_Text>();
            }

            if (subtitlePromptText == null)
            {
                subtitlePromptText = subtitleRoot.GetComponentInChildren<TMP_Text>(true);
            }

            if (subtitlePromptText != null && !hasOriginalPrompt)
            {
                originalPromptText = subtitlePromptText.text;
                hasOriginalPrompt = true;
            }
        }

        private void RestoreSubtitlePrompt()
        {
            if (subtitlePromptText != null && hasOriginalPrompt)
            {
                subtitlePromptText.text = originalPromptText;
            }
        }

        private void RestoreAfterSwitchPrompt()
        {
            SetAfterSwitchPromptVisible(true);
        }

        public void OnAfterSwitchCloseClicked()
        {
            GameObject prompt = ResolveAfterSwitchPrompt();
            bool wasVisible = prompt != null && prompt.activeSelf;
            SetAfterSwitchPromptVisible(false);
            if (wasVisible)
            {
                UiSfx.PlayExit();
            }
        }

        public void OnOpenPromptClicked()
        {
            SetAfterSwitchPromptVisible(true);
        }

        private void SetAfterSwitchPromptVisible(bool visible)
        {
            GameObject prompt = ResolveAfterSwitchPrompt();
            if (prompt == null)
            {
                RefreshOpenPromptButtonVisibility();
                return;
            }

            bool wasVisible = prompt.activeSelf;
            prompt.SetActive(visible);
            if (visible && !wasVisible)
            {
                UiSfx.PlayPopup();
            }

            RefreshOpenPromptButtonVisibility();
        }

        private GameObject ResolveAfterSwitchPrompt()
        {
            if (subtitleRoot == null)
            {
                return null;
            }

            Transform prompt = subtitleRoot.transform.Find("Prompt");
            if (prompt != null)
            {
                return prompt.gameObject;
            }

            if (subtitlePromptText != null)
            {
                return subtitlePromptText.gameObject;
            }

            return null;
        }

        private void HideAfterSwitchPanels()
        {
            if (continuityRoot != null)
            {
                continuityRoot.SetActive(false);
            }

            if (differenceRoot != null)
            {
                differenceRoot.SetActive(false);
            }

            if (photobookRoot != null)
            {
                photobookRoot.SetActive(false);
            }

            if (photobookPanel != null)
            {
                photobookPanel.SetActive(false);
            }

            _activePhotobookSet = PhotobookCardSet.None;
            _photobookPlaced = false;
        }

        private void PreparePhotobookButtonCanvases()
        {
            PreparePhotobookButtonCanvas(continuityRoot);
            PreparePhotobookButtonCanvas(differenceRoot);
        }

        private void PreparePhotobookButtonCanvas(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.GetComponentInChildren<Canvas>(true);
            }

            if (canvas == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] No Canvas on " + root.name + "; XR rays cannot hit the button.");
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;
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

            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                Debug.Log("[FinalVoiceGuide] Added TrackedDeviceGraphicRaycaster on " + canvas.name);
            }
        }

        private void BindPhotobookButtons()
        {
            if (continuityButton == null)
            {
                continuityButton = FindRootButton(continuityRoot);
            }

            if (differenceButton == null)
            {
                differenceButton = FindRootButton(differenceRoot);
            }

            BindPhotobookButton(continuityButton);
            BindPhotobookButton(differenceButton);
            UiSfx.BindClick(continuityButton);
            UiSfx.BindClick(differenceButton);

            if (continuityButton == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] Continuity Button was not found.");
            }

            if (differenceButton == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] Difference Button was not found.");
            }
        }

        private void BindPhotobookButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.interactable = true;

            Graphic graphic = button.targetGraphic;
            if (graphic == null)
            {
                graphic = button.GetComponent<Graphic>();
            }

            if (graphic == null)
            {
                graphic = button.GetComponentInChildren<Graphic>(true);
            }

            if (graphic == null)
            {
                Image image = button.gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.01f);
                graphic = image;
            }

            graphic.raycastTarget = true;
            button.targetGraphic = graphic;

            TMP_Text[] texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    texts[i].raycastTarget = false;
                }
            }
        }

        private static Button FindRootButton(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            Transform named = root.transform.Find("Button");
            if (named != null)
            {
                Button namedButton = named.GetComponent<Button>();
                if (namedButton != null)
                {
                    return namedButton;
                }
            }

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].gameObject.activeSelf)
                {
                    return buttons[i];
                }
            }

            return buttons.Length > 0 ? buttons[0] : null;
        }

        public void ShowPhotobook()
        {
            TogglePhotobook();
        }

        public void TogglePhotobook()
        {
            if (!ConsumePhotobookToggle())
            {
                return;
            }

            if (IsPhotobookVisible())
            {
                HidePhotobook();
                return;
            }

            ShowPhotobookSet(PhotobookCardSet.Present);
        }

        public void OnContinuityPhotobookClicked()
        {
            TogglePhotobookSet(PhotobookCardSet.Present);
        }

        public void OnDifferencePhotobookClicked()
        {
            TogglePhotobookSet(PhotobookCardSet.Past);
        }

        private bool ConsumePhotobookToggle()
        {
            if (_lastPhotobookToggleFrame == Time.frameCount)
            {
                return false;
            }

            _lastPhotobookToggleFrame = Time.frameCount;
            return true;
        }

        private void TogglePhotobookSet(PhotobookCardSet set)
        {
            if (!ConsumePhotobookToggle())
            {
                return;
            }

            if (IsPhotobookVisible() && _activePhotobookSet == set)
            {
                HidePhotobook();
                return;
            }

            ShowPhotobookSet(set);
        }

        private void HidePhotobook()
        {
            SetPhotobookVisible(false);
            _activePhotobookSet = PhotobookCardSet.None;
            Debug.Log("[FinalVoiceGuide] Hidden Photobook.");
        }

        private void ShowPhotobookSet(PhotobookCardSet set)
        {
            GameObject root = ResolvePhotobookRoot();
            if (root == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] Photobook Root / Panel is not assigned.");
                return;
            }

            PreparePhotobookButtonCanvas(root);
            if (!_photobookPlaced)
            {
                if (!_hasPanelPose)
                {
                    CacheSharedPanelPose();
                }

                PlacePhotobookAboveButtons();
                _photobookPlaced = true;
            }

            SetPhotobookVisible(true);
            ApplyPhotobookCardSet(set);
            _activePhotobookSet = set;
            Debug.Log("[FinalVoiceGuide] Shown Photobook with " + set + " cards.");
        }

        private bool IsPhotobookVisible()
        {
            GameObject root = ResolvePhotobookRoot();
            return root != null && root.activeInHierarchy;
        }

        private void SetPhotobookVisible(bool visible)
        {
            bool wasVisible = IsPhotobookVisible();
            GameObject root = ResolvePhotobookRoot();
            if (root != null)
            {
                root.SetActive(visible);
            }

            if (photobookPanel != null && photobookPanel != root)
            {
                photobookPanel.SetActive(visible);
            }

            if (visible && !wasVisible)
            {
                UiSfx.PlayPopup();
            }
            else if (!visible && wasVisible)
            {
                UiSfx.PlayExit();
            }
        }

        private GameObject ResolvePhotobookRoot()
        {
            if (photobookRoot != null)
            {
                return photobookRoot;
            }

            Transform canvas = GetPhotobookCanvasRoot();
            if (canvas != null)
            {
                photobookRoot = canvas.gameObject;
                return photobookRoot;
            }

            return photobookPanel;
        }

        private void ApplyPhotobookCardSet(PhotobookCardSet set)
        {
            EnsurePhotobookCardGroups();
            Transform continuityAnchor = ResolveContinuityCardAnchor();
            Transform differenceAnchor = ResolveDifferenceCardAnchor();
            if (set == PhotobookCardSet.Present)
            {
                SpawnPhotobookCardsIfNeeded(PhotobookCardSet.Present);
            }
            else if (set == PhotobookCardSet.Past)
            {
                SpawnPhotobookCardsIfNeeded(PhotobookCardSet.Past);
            }

            if (continuityAnchor != null)
            {
                continuityAnchor.gameObject.SetActive(set == PhotobookCardSet.Present);
            }

            if (differenceAnchor != null)
            {
                differenceAnchor.gameObject.SetActive(set == PhotobookCardSet.Past);
            }

            if (_presentCardGroup != null)
            {
                _presentCardGroup.gameObject.SetActive(set == PhotobookCardSet.Present);
            }

            if (_pastCardGroup != null)
            {
                _pastCardGroup.gameObject.SetActive(set == PhotobookCardSet.Past);
            }
        }

        private void SpawnPhotobookCardsIfNeeded(PhotobookCardSet set)
        {
            LineOfLife3DViewController view = ResolveLineOfLife3DView();
            if (view == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] LineOfLife3DViewController is not assigned; Photobook cards cannot spawn.");
                return;
            }

            if (set == PhotobookCardSet.Present)
            {
                if (_presentCardGroup != null
                    && _presentCardsSpawned
                    && _presentCardGroup.childCount > 0)
                {
                    return;
                }

                view.SpawnCardsIntoRoot(TimeCategory.Present, _presentCardGroup, true);
                _presentCardsSpawned = _presentCardGroup != null && _presentCardGroup.childCount > 0;
                Debug.Log("[FinalVoiceGuide] Present photobook cards: " + (_presentCardGroup != null ? _presentCardGroup.childCount : 0));
            }
            else if (set == PhotobookCardSet.Past)
            {
                if (_pastCardGroup != null
                    && _pastCardsSpawned
                    && _pastCardGroup.childCount > 0)
                {
                    return;
                }

                view.SpawnCardsIntoRoot(TimeCategory.Past, _pastCardGroup, true);
                _pastCardsSpawned = _pastCardGroup != null && _pastCardGroup.childCount > 0;
                Debug.Log("[FinalVoiceGuide] Past photobook cards: " + (_pastCardGroup != null ? _pastCardGroup.childCount : 0));
            }
        }

        private LineOfLife3DViewController ResolveLineOfLife3DView()
        {
            if (lineOfLife3DView == null)
            {
                lineOfLife3DView = FindObjectOfType<LineOfLife3DViewController>();
            }

            return lineOfLife3DView;
        }

        private void EnsurePhotobookCardGroups()
        {
            Transform continuityAnchor = ResolveContinuityCardAnchor();
            Transform differenceAnchor = ResolveDifferenceCardAnchor();
            _presentCardGroup = EnsureNamedChild(continuityAnchor, "PresentCards");
            _pastCardGroup = EnsureNamedChild(differenceAnchor, "PastCards");
            ResetLocalPose(_presentCardGroup);
            ResetLocalPose(_pastCardGroup);
            CompensateWorldScale(_presentCardGroup);
            CompensateWorldScale(_pastCardGroup);
        }

        private Transform ResolveContinuityCardAnchor()
        {
            if (continuityCardAnchorRoot != null)
            {
                return continuityCardAnchorRoot;
            }

            continuityCardAnchorRoot = FindNamedCardAnchor("Continuity_CardAnchorRoot");
            if (continuityCardAnchorRoot != null)
            {
                return continuityCardAnchorRoot;
            }

            return EnsurePhotobookCardAnchor();
        }

        private Transform ResolveDifferenceCardAnchor()
        {
            if (differenceCardAnchorRoot != null)
            {
                return differenceCardAnchorRoot;
            }

            differenceCardAnchorRoot = FindNamedCardAnchor("Difference_CardAnchorRoot");
            if (differenceCardAnchorRoot != null)
            {
                return differenceCardAnchorRoot;
            }

            return ResolveContinuityCardAnchor();
        }

        private Transform FindNamedCardAnchor(string anchorName)
        {
            if (photobookPanel != null)
            {
                Transform found = photobookPanel.transform.Find(anchorName);
                if (found != null)
                {
                    return found;
                }
            }

            Transform canvasRoot = GetPhotobookCanvasRoot();
            if (canvasRoot != null)
            {
                Transform found = FindChildNamed(canvasRoot, anchorName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void ResetLocalPose(Transform target)
        {
            if (target == null)
            {
                return;
            }

            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
        }

        private Transform EnsurePhotobookCardAnchor()
        {
            if (photobookCardAnchorRoot != null)
            {
                return photobookCardAnchorRoot;
            }

            if (photobookPanel != null)
            {
                Transform found = photobookPanel.transform.Find("CardAnchorRoot");
                if (found != null)
                {
                    photobookCardAnchorRoot = found;
                    return photobookCardAnchorRoot;
                }
            }

            Transform canvasRoot = GetPhotobookCanvasRoot();
            if (canvasRoot != null)
            {
                Transform found = canvasRoot.Find("CardAnchorRoot");
                if (found != null)
                {
                    photobookCardAnchorRoot = found;
                    return photobookCardAnchorRoot;
                }
            }

            if (photobookPanel == null && photobookRoot == null)
            {
                return null;
            }

            Transform parent = photobookPanel != null ? photobookPanel.transform : photobookRoot.transform;
            GameObject anchorObject = new GameObject("CardAnchorRoot", typeof(RectTransform));
            anchorObject.transform.SetParent(parent, false);
            RectTransform rect = anchorObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            photobookCardAnchorRoot = rect;
            return photobookCardAnchorRoot;
        }

        private static Transform EnsureNamedChild(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            Transform existing = parent.Find(childName);
            if (existing != null)
            {
                return existing;
            }

            GameObject child = new GameObject(childName, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void CompensateWorldScale(Transform target)
        {
            if (target == null || target.parent == null)
            {
                return;
            }

            Vector3 parentScale = target.parent.lossyScale;
            float x = Mathf.Abs(parentScale.x) < 0.0001f ? 1f : 1f / parentScale.x;
            float y = Mathf.Abs(parentScale.y) < 0.0001f ? 1f : 1f / parentScale.y;
            float z = Mathf.Abs(parentScale.z) < 0.0001f ? 1f : 1f / parentScale.z;
            target.localScale = new Vector3(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z));
        }

        private void PlacePhotobookAboveButtons()
        {
            Transform placementRoot = GetPhotobookPlacementRoot();
            if (placementRoot == null)
            {
                return;
            }

            EnsurePlayerCamera();
            if (!_hasPanelPose)
            {
                CacheSharedPanelPose();
            }

            if (playerCamera == null)
            {
                Debug.LogWarning("[FinalVoiceGuide] playerCamera is not assigned; Photobook stays at original position.");
                return;
            }

            Vector3 forward = _hasPanelPose ? _panelForward : playerCamera.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 right = _hasPanelPose ? _panelRight : Vector3.Cross(Vector3.up, forward).normalized;
            float distance = Mathf.Max(0.1f, photobookDistanceFromPlayer);

            Vector3 targetPos = playerCamera.position + forward * distance;
            if (continuityRoot != null
                && differenceRoot != null
                && continuityRoot.activeInHierarchy
                && differenceRoot.activeInHierarchy)
            {
                Vector3 mid = (continuityRoot.transform.position + differenceRoot.transform.position) * 0.5f;
                float rightOffset = Vector3.Dot(mid - playerCamera.position, right);
                targetPos = playerCamera.position
                    + forward * distance
                    + right * rightOffset;
                targetPos.y = mid.y + photobookHeightOffset;
            }
            else
            {
                targetPos.y = playerCamera.position.y + photobookHeightOffset;
            }

            placementRoot.position = targetPos;

            Vector3 faceDir = forward;
            if (panelFlip180)
            {
                faceDir = -faceDir;
            }

            if (subtitleFaceCamera && faceDir.sqrMagnitude > 0.001f)
            {
                placementRoot.rotation = Quaternion.LookRotation(faceDir, Vector3.up);
            }
            else if (continuityRoot != null && differenceRoot != null)
            {
                placementRoot.rotation = Quaternion.Slerp(
                    continuityRoot.transform.rotation,
                    differenceRoot.transform.rotation,
                    0.5f);
            }
        }

        private Transform GetPhotobookPlacementRoot()
        {
            GameObject root = ResolvePhotobookRoot();
            return root != null ? root.transform : null;
        }

        private Transform GetPhotobookCanvasRoot()
        {
            if (photobookRoot != null)
            {
                Canvas rootCanvas = photobookRoot.GetComponent<Canvas>();
                if (rootCanvas == null)
                {
                    rootCanvas = photobookRoot.GetComponentInChildren<Canvas>(true);
                }

                if (rootCanvas != null)
                {
                    return rootCanvas.transform;
                }

                return photobookRoot.transform;
            }

            if (photobookPanel == null)
            {
                return null;
            }

            Canvas canvas = photobookPanel.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = photobookPanel.GetComponentInParent<Canvas>();
            }

            return canvas != null ? canvas.transform : photobookPanel.transform;
        }

        private void CacheSharedPanelPose()
        {
            EnsurePlayerCamera();
            _hasPanelPose = false;
            if (playerCamera == null)
            {
                return;
            }

            Vector3 forward = playerCamera.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            _panelForward = forward.normalized;
            _panelRight = Vector3.Cross(Vector3.up, _panelForward).normalized;
            _panelY = playerCamera.position.y + subtitleVerticalOffset;
            if (subtitleAnchor != null)
            {
                _panelY = subtitleAnchor.position.y + subtitleVerticalOffset;
            }

            _panelOrigin = playerCamera.position
                + _panelForward * subtitleDistanceFromCamera;
            _panelOrigin.y = _panelY;
            _hasPanelPose = true;
        }

        private void PlacePanelInFrontOfPlayer(Transform target, Vector3 extraOffset, bool flip180)
        {
            if (target == null)
            {
                return;
            }

            if (!_hasPanelPose)
            {
                CacheSharedPanelPose();
            }

            if (!_hasPanelPose)
            {
                Debug.LogWarning("[FinalVoiceGuide] playerCamera is not assigned and Camera.main is null; panel stays at original position.");
                return;
            }

            Vector3 targetPos = _panelOrigin
                + _panelRight * extraOffset.x
                + Vector3.up * extraOffset.y
                + _panelForward * extraOffset.z;
            targetPos.y = _panelY + extraOffset.y;
            target.position = targetPos;

            Vector3 faceDir = _panelForward;
            if (flip180)
            {
                faceDir = -faceDir;
            }

            if (subtitleFaceCamera && faceDir.sqrMagnitude > 0.001f)
            {
                target.rotation = Quaternion.LookRotation(faceDir, Vector3.up);
            }
        }

        private void EnsurePlayerCamera()
        {
            if (playerCamera == null && Camera.main != null)
            {
                playerCamera = Camera.main.transform;
            }
        }
    }
}
