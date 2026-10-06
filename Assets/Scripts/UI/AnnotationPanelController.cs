using System.Collections.Generic;
using EchoSpace.Annotation;
using EchoSpace.Core;
using EchoSpace.VoiceInput;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class AnnotationPanelController : MonoBehaviour
{
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text promptText;
    public TMP_InputField userInputField;
    public Button pastLifeButton;
    public Button pastSelfButton;
    public Button concreteMemoryButton;
    public Button relationshipButton;
    public Button transitionMomentButton;
    public Button familiarFeelingButton;
    public Button strangeFeelingButton;
    public Button uncertainButton;
    public Button saveButton;
    public Button cancelButton;
    public MemoryAnchorManager anchorManager;

    public Transform panelWorldRoot;
    public Camera targetCamera;
    public Canvas panelCanvas;
    public Vector3 panelOffset = new Vector3(0f, 0.2f, 0f);
    public float pullTowardCamera = 0.2f;
    [Tooltip("Must be above ControllerTutorial (1100) and the intro black sphere, or the panel is covered.")]
    public int panelCanvasSortingOrder = 1200;
    public bool faceCamera = true;
    public bool yawOnly = true;
    public float extraYRotation = 180f;

    public Color normalButtonColor = Color.white;
    public Color selectedButtonColor = new Color(0.25f, 0.65f, 1f, 1f);
    public Color normalTextColor = Color.black;
    public Color selectedTextColor = Color.black;

    [Header("Button Hover Visual")]
    public Color highlightedButtonColor = new Color(0.2f, 0.85f, 1f, 1f);
    public Color pressedButtonColor = new Color(0.1f, 0.6f, 0.9f, 1f);
    public Color disabledButtonColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
    public float buttonColorFadeDuration = 0.08f;

    private MemoryAnchor currentAnchor;
    private MemoryType selectedMemoryType;
    private string selectedPromptQuestion;
    private Button selectedButton;
    private bool hasExplicitCategorySelection;

    private MemoryType snapshotMemoryType;
    private string snapshotPromptQuestion;
    private string snapshotUserText;
    private TimeCategory snapshotTimeCategory;
    private bool snapshotIsAnnotated;
    private string snapshotAnchorId;
    private bool hasSnapshot;

    private bool hasCommittedSaveThisSession;
    private string pendingVoiceText;
    private bool alwaysOnTopApplied;
    private static Material uiNoZTestMaterial;
    private readonly List<Collider> practiceCollidersDisabledForPanel = new List<Collider>();

    public bool IsOpen =>
        panelRoot != null && panelRoot.activeInHierarchy;

    public MemoryType SelectedMemoryType => selectedMemoryType;

    public bool ContainsUIObject(GameObject uiObject)
    {
        if (!IsOpen || uiObject == null || panelRoot == null)
        {
            return false;
        }

        Transform root = panelRoot.transform;
        return uiObject.transform == root || uiObject.transform.IsChildOf(root);
    }

    public bool IsRayOverPanel(Ray ray)
    {
        if (!IsOpen || panelRoot == null)
        {
            return false;
        }

        RectTransform rect = panelRoot.transform as RectTransform;
        if (rect == null)
        {
            return false;
        }

        Plane plane = new Plane(rect.forward, rect.position);
        if (!plane.Raycast(ray, out float distance))
        {
            plane = new Plane(-rect.forward, rect.position);
            if (!plane.Raycast(ray, out distance))
            {
                return false;
            }
        }

        Vector3 localPoint = rect.InverseTransformPoint(ray.GetPoint(distance));
        return rect.rect.Contains(new Vector2(localPoint.x, localPoint.y));
    }

    public bool HasUnsavedSession =>
        IsOpen && currentAnchor != null && !hasCommittedSaveThisSession;

    private void Awake()
    {
        EnsurePanelUiRaycastSetup();

        RegisterMemoryTypeButton(pastLifeButton, MemoryType.PastLife, () =>
        {
            SelectMemoryType(MemoryType.PastLife);
            SetSelectedButton(pastLifeButton);
        });

        RegisterMemoryTypeButton(pastSelfButton, MemoryType.PastSelf, () =>
        {
            SelectMemoryType(MemoryType.PastSelf);
            SetSelectedButton(pastSelfButton);
        });

        RegisterMemoryTypeButton(concreteMemoryButton, MemoryType.ConcreteMemory, () =>
        {
            SelectMemoryType(MemoryType.ConcreteMemory);
            SetSelectedButton(concreteMemoryButton);
        });

        RegisterMemoryTypeButton(relationshipButton, MemoryType.Relationship, () =>
        {
            SelectMemoryType(MemoryType.Relationship);
            SetSelectedButton(relationshipButton);
        });

        RegisterMemoryTypeButton(transitionMomentButton, MemoryType.TransitionMoment, () =>
        {
            SelectMemoryType(MemoryType.TransitionMoment);
            SetSelectedButton(transitionMomentButton);
        });

        RegisterMemoryTypeButton(familiarFeelingButton, MemoryType.FamiliarFeeling, () =>
        {
            SelectMemoryType(MemoryType.FamiliarFeeling);
            SetSelectedButton(familiarFeelingButton);
        });

        RegisterMemoryTypeButton(strangeFeelingButton, MemoryType.StrangeFeeling, () =>
        {
            SelectMemoryType(MemoryType.StrangeFeeling);
            SetSelectedButton(strangeFeelingButton);
        });

        RegisterMemoryTypeButton(uncertainButton, MemoryType.Uncertain, () =>
        {
            SelectMemoryType(MemoryType.Uncertain);
            SetSelectedButton(uncertainButton);
        });

        SetupAllButtonHoverVisuals();

        if (saveButton != null)
        {
            saveButton.onClick.AddListener(Save);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(Close);
        }

        ApplyTutorialPracticeChrome();
    }

    private void ApplyTutorialPracticeChrome()
    {
        bool hideSave = EchoSpace.UI.ControllerTutorialGate.IsTutorialActive;
        if (saveButton != null)
        {
            saveButton.gameObject.SetActive(!hideSave);
        }
    }

    private void RegisterMemoryTypeButton(Button button, MemoryType type, System.Action onSelect)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.AddListener(() =>
        {
            Debug.Log("[AnnotationPanel] Category selected: " + type);
            onSelect();
        });
    }

    public void Open(MemoryAnchor anchor)
    {
        if (anchor == null)
        {
            Debug.LogWarning("[AnnotationPanelController] Open called with null MemoryAnchor.");
            return;
        }

        if (IsOpen && currentAnchor != null && currentAnchor == anchor)
        {
            RefreshCategoryButtonVisuals();
            return;
        }

        if (currentAnchor != null && currentAnchor != anchor)
        {
            Debug.Log(
                "[AnnotationPanel] Close previous panel without saving before opening new anchor. "
                + "oldId=" + GetAnchorId(currentAnchor)
                + " newId=" + GetAnchorId(anchor));
            HandleCloseOrSwitchCurrentAnchor();
            ClearEditingState();
        }

        currentAnchor = anchor;
        SnapshotCurrentAnchorData();
        hasCommittedSaveThisSession = false;
        pendingVoiceText = string.Empty;

        EnsurePanelUiRaycastSetup();
        PositionPanelNearAnchor(anchor);

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[AnnotationPanelController] panelRoot is not assigned.");
        }

        if (anchor.Data != null && anchor.Data.isAnnotated)
        {
            ApplyAnnotatedDataToUi(anchor.Data);
            hasExplicitCategorySelection = true;
        }
        else
        {
            selectedMemoryType = MemoryType.Uncertain;
            selectedPromptQuestion = string.Empty;
            hasExplicitCategorySelection = false;
            if (userInputField != null)
            {
                userInputField.text = string.Empty;
            }

            if (promptText != null)
            {
                promptText.text = EchoSpace.UI.ControllerTutorialGate.IsTutorialActive
                    ? "這是練習預覽模式。\n你可以先看看標記記憶時會出現的提示卡，看完後請按「關閉」繼續。"
                    : "請先選擇一張卡片看看這邊讓你想起什麼~";
            }
        }

        RefreshCategoryButtonVisuals();
        ResetVoiceInputToIdle();
        ApplyTutorialPracticeChrome();

        IgnorePracticeObjectsWhilePanelOpen();

        string id = anchor.Data != null ? anchor.Data.id : "(no data)";
        Debug.Log("[AnnotationPanelController] Open — editing anchor: " + anchor.gameObject.name + ", id=" + id);
    }

    public void ForceCloseWithoutSaving()
    {
        HandleCloseOrSwitchCurrentAnchor();
        ClearEditingState();

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        RestorePracticeObjectColliders();
        Debug.Log("[AnnotationPanelController] Force closed panel without saving.");
    }

    public void Close()
    {
        HandleCloseOrSwitchCurrentAnchor();
        ClearEditingState();

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        RestorePracticeObjectColliders();
        ShowCurrentModeGuideAfterClose();
        Debug.Log("[AnnotationPanelController] Closed without saving (cancel).");
    }

    public void Save()
    {
        if (currentAnchor == null)
        {
            Debug.LogWarning("[AnnotationPanelController] Save called but currentAnchor is null.");
            return;
        }

        MemoryAnchorData data = currentAnchor.Data;
        if (data == null)
        {
            Debug.LogWarning("[AnnotationPanelController] Save called but currentAnchor.Data is null.");
            return;
        }

        data.memoryType = selectedMemoryType;
        data.promptQuestion = selectedPromptQuestion;
        data.userText = ResolveUserTextToSave();
        data.isAnnotated = true;

        hasCommittedSaveThisSession = true;

        currentAnchor.UpdateVisualLabel();

        Debug.Log(
            "[AnnotationPanelController] Saved — id=" + data.id
            + ", memoryType=" + data.memoryType
            + ", userText=" + data.userText
            + ", isAnnotated=" + data.isAnnotated);

        ClearEditingState();

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        RestorePracticeObjectColliders();
        ShowCurrentModeGuideAfterClose();
        Debug.Log("[AnnotationPanelController] Save complete, panel closed.");
    }

    private void ShowCurrentModeGuideAfterClose()
    {
        EchoSpace.UI.TutorialOverlayController overlay =
            FindObjectOfType<EchoSpace.UI.TutorialOverlayController>();
        if (overlay != null)
        {
            overlay.ShowCurrentModeGuide();
        }
    }

    private void HandleCloseOrSwitchCurrentAnchor()
    {
        if (currentAnchor == null || currentAnchor.Data == null)
        {
            return;
        }

        MemoryAnchorData data = currentAnchor.Data;

        if (!data.isAnnotated)
        {
            if (anchorManager != null)
            {
                anchorManager.RemoveAnchor(currentAnchor);
            }
            else
            {
                Debug.LogWarning("[AnnotationPanelController] anchorManager is not assigned; cannot remove unannotated anchor.");
                Destroy(currentAnchor.gameObject);
            }

            return;
        }

        if (!hasCommittedSaveThisSession)
        {
            RestoreCurrentAnchorSnapshotIfNeeded();
        }
    }

    private void SnapshotCurrentAnchorData()
    {
        if (currentAnchor == null || currentAnchor.Data == null)
        {
            hasSnapshot = false;
            snapshotAnchorId = string.Empty;
            return;
        }

        MemoryAnchorData data = currentAnchor.Data;
        snapshotMemoryType = data.memoryType;
        snapshotPromptQuestion = data.promptQuestion ?? string.Empty;
        snapshotUserText = data.userText ?? string.Empty;
        snapshotTimeCategory = data.timeCategory;
        snapshotIsAnnotated = data.isAnnotated;
        snapshotAnchorId = data.id ?? string.Empty;
        hasSnapshot = true;
    }

    private void RestoreCurrentAnchorSnapshotIfNeeded()
    {
        if (!hasSnapshot)
        {
            return;
        }

        if (currentAnchor == null || currentAnchor.Data == null)
        {
            return;
        }

        MemoryAnchorData data = currentAnchor.Data;

        if (data.id != snapshotAnchorId)
        {
            Debug.LogWarning("[AnnotationPanelController] Snapshot anchor id mismatch. Restore skipped.");
            return;
        }

        data.memoryType = snapshotMemoryType;
        data.promptQuestion = snapshotPromptQuestion;
        data.userText = snapshotUserText;
        data.timeCategory = snapshotTimeCategory;
        data.isAnnotated = snapshotIsAnnotated;

        Debug.Log("[AnnotationPanelController] Restored snapshot for anchor id=" + data.id);
    }

    private void ClearEditingState()
    {
        currentAnchor = null;
        hasSnapshot = false;
        snapshotAnchorId = string.Empty;
        hasCommittedSaveThisSession = false;
        selectedPromptQuestion = string.Empty;
        selectedMemoryType = MemoryType.Uncertain;
        hasExplicitCategorySelection = false;
        selectedButton = null;
        pendingVoiceText = string.Empty;

        if (userInputField != null)
        {
            userInputField.text = string.Empty;
        }

        ResetVoiceInputToIdle();
    }

    private void ResetVoiceInputToIdle()
    {
        var voices = new System.Collections.Generic.List<VoiceInputController>();
        AddVoiceInputs(voices, GetComponents<VoiceInputController>());
        AddVoiceInputs(voices, GetComponentsInChildren<VoiceInputController>(true));
        if (panelRoot != null)
        {
            AddVoiceInputs(voices, panelRoot.GetComponentsInChildren<VoiceInputController>(true));
            AddVoiceInputs(voices, panelRoot.GetComponentsInParent<VoiceInputController>(true));
        }

        if (voices.Count == 0)
        {
            Debug.LogWarning("[AnnotationPanel] No VoiceInputController found to reset mic state.");
            return;
        }

        for (int i = 0; i < voices.Count; i++)
        {
            voices[i].ResetToIdle();
        }
    }

    private static void AddVoiceInputs(
        System.Collections.Generic.List<VoiceInputController> list,
        VoiceInputController[] found)
    {
        if (found == null)
        {
            return;
        }

        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && !list.Contains(found[i]))
            {
                list.Add(found[i]);
            }
        }
    }

    private void ApplyAnnotatedDataToUi(MemoryAnchorData data)
    {
        selectedMemoryType = data.memoryType;
        selectedPromptQuestion = data.promptQuestion ?? string.Empty;

        if (userInputField != null)
        {
            userInputField.text = data.userText ?? string.Empty;
        }

        if (promptText != null)
        {
            promptText.text = string.IsNullOrEmpty(selectedPromptQuestion)
                ? GetPromptForMemoryType(selectedMemoryType)
                : selectedPromptQuestion;
        }
    }

    private void PositionPanelNearAnchor(MemoryAnchor anchor)
    {
        if (anchor == null)
        {
            return;
        }

        Transform root = panelWorldRoot != null ? panelWorldRoot : transform;
        Camera cam = GetTargetCamera();

        Vector3 targetPos = anchor.transform.position + Vector3.up * panelOffset.y;
        root.position = targetPos;
        FaceCamera(root, cam);
    }

    private void FaceCamera(Transform root, Camera cam)
    {
        if (!faceCamera || cam == null || root == null)
        {
            return;
        }

        if (yawOnly)
        {
            Vector3 direction = cam.transform.position - root.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                root.rotation = Quaternion.LookRotation(-direction);
                root.rotation *= Quaternion.Euler(0f, extraYRotation, 0f);
            }
        }
        else
        {
            Vector3 direction = cam.transform.position - root.position;
            root.rotation = Quaternion.LookRotation(-direction);
            root.rotation *= Quaternion.Euler(0f, extraYRotation, 0f);
        }
    }

    private Camera GetTargetCamera()
    {
        if (targetCamera != null)
        {
            return targetCamera;
        }

        return Camera.main;
    }

    private Canvas ResolvePanelCanvas()
    {
        if (panelCanvas != null)
        {
            return panelCanvas;
        }

        if (panelRoot != null)
        {
            panelCanvas = panelRoot.GetComponentInParent<Canvas>();
        }

        if (panelCanvas == null)
        {
            panelCanvas = GetComponentInChildren<Canvas>(true);
        }

        return panelCanvas;
    }

    private void EnsurePanelUiRaycastSetup()
    {
        Canvas canvas = ResolvePanelCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[AnnotationPanelController] panelCanvas is not assigned and could not be resolved.");
            return;
        }

        GraphicRaycaster graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
        if (graphicRaycaster == null)
        {
            graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        graphicRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        TrackedDeviceGraphicRaycaster trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (trackedRaycaster == null)
        {
            trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        trackedRaycaster.checkFor3DOcclusion = false;

        ApplyAlwaysOnTopDrawOrder(canvas);
        ConfigureCanvasGroups(canvas.transform);
        ConfigureDisplayTextRaycasts();
        SetupAllButtonHoverVisuals();
        ConfigureInputFieldRaycasts();
    }

    private void ApplyAlwaysOnTopDrawOrder(Canvas canvas)
    {
        if (canvas == null || alwaysOnTopApplied)
        {
            return;
        }

        Material uiMaterial = ResolveUiNoZTestMaterial();
        Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null || graphic is TMP_Text)
            {
                continue;
            }

            if (uiMaterial != null)
            {
                graphic.material = uiMaterial;
            }
        }

        Shader overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay");
        TMP_Text[] texts = canvas.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
            {
                continue;
            }

            Material fontMaterial = text.fontMaterial;
            if (overlayShader != null && fontMaterial != null && fontMaterial.shader != overlayShader)
            {
                fontMaterial.shader = overlayShader;
            }
        }

        alwaysOnTopApplied = true;
    }

    private static Material ResolveUiNoZTestMaterial()
    {
        if (uiNoZTestMaterial != null)
        {
            return uiNoZTestMaterial;
        }

        Shader shader = Shader.Find("UI/NoZTest");
        if (shader == null)
        {
            Debug.LogWarning("[AnnotationPanel] UI/NoZTest shader not found; panel may stay behind 3D practice objects.");
            return null;
        }

        uiNoZTestMaterial = new Material(shader);
        uiNoZTestMaterial.name = "AnnotationPanel-UI-NoZTest";
        uiNoZTestMaterial.renderQueue = 4000;
        return uiNoZTestMaterial;
    }

    private void IgnorePracticeObjectsWhilePanelOpen()
    {
        RestorePracticeObjectColliders();

        GameObject practiceRoot = FindPracticeObjectsRoot();
        if (practiceRoot == null || !practiceRoot.activeInHierarchy)
        {
            return;
        }

        Collider[] colliders = practiceRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            collider.enabled = false;
            practiceCollidersDisabledForPanel.Add(collider);
        }
    }

    private void RestorePracticeObjectColliders()
    {
        for (int i = 0; i < practiceCollidersDisabledForPanel.Count; i++)
        {
            Collider collider = practiceCollidersDisabledForPanel[i];
            if (collider != null)
            {
                collider.enabled = true;
            }
        }

        practiceCollidersDisabledForPanel.Clear();
    }

    private static GameObject FindPracticeObjectsRoot()
    {
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return null;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root != null && root.name == "PracticeObjects")
            {
                return root;
            }
        }

        return null;
    }

    private void OnDisable()
    {
        RestorePracticeObjectColliders();
    }

    private void SetupAllButtonHoverVisuals()
    {
        SetupButtonHoverVisual(pastLifeButton);
        SetupButtonHoverVisual(pastSelfButton);
        SetupButtonHoverVisual(concreteMemoryButton);
        SetupButtonHoverVisual(relationshipButton);
        SetupButtonHoverVisual(transitionMomentButton);
        SetupButtonHoverVisual(familiarFeelingButton);
        SetupButtonHoverVisual(strangeFeelingButton);
        SetupButtonHoverVisual(uncertainButton);
        SetupButtonHoverVisual(saveButton);
        SetupButtonHoverVisual(cancelButton);
    }

    private void SetupButtonHoverVisual(Button button)
    {
        if (button == null)
        {
            return;
        }

        button.interactable = true;

        Image image = button.GetComponent<Image>();
        if (image == null)
        {
            image = button.gameObject.AddComponent<Image>();
        }

        image.raycastTarget = true;
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = normalButtonColor;
        colors.highlightedColor = highlightedButtonColor;
        colors.pressedColor = pressedButtonColor;
        colors.selectedColor = selectedButtonColor;
        colors.disabledColor = disabledButtonColor;
        colors.fadeDuration = buttonColorFadeDuration;
        colors.colorMultiplier = 1f;
        button.colors = colors;

        TMP_Text[] texts = button.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            text.raycastTarget = false;
        }

        Debug.Log("[AnnotationPanel] Setup hover visual: " + button.name);
    }

    private void ConfigureCanvasGroups(Transform root)
    {
        CanvasGroup[] groups = root.GetComponentsInChildren<CanvasGroup>(true);
        foreach (CanvasGroup group in groups)
        {
            group.interactable = true;
            group.blocksRaycasts = true;

            if (group.alpha <= 0f)
            {
                group.alpha = 1f;
            }
        }
    }

    private void ConfigureDisplayTextRaycasts()
    {
        SetRaycastTarget(titleText, false);
        SetRaycastTarget(promptText, false);

        if (panelRoot == null)
        {
            return;
        }

        TMP_Text[] texts = panelRoot.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text == null)
            {
                continue;
            }

            if (IsUnderButton(text.transform) || IsPartOfInputField(text))
            {
                continue;
            }

            SetRaycastTarget(text, false);
        }

        Image[] images = panelRoot.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image == null)
            {
                continue;
            }

            if (image.gameObject == panelRoot)
            {
                image.raycastTarget = true;
                continue;
            }

            if (image.GetComponent<Button>() != null || IsPartOfInputField(image))
            {
                continue;
            }

            SetRaycastTarget(image, false);
        }
    }

    private void ConfigureInputFieldRaycasts()
    {
        if (userInputField == null)
        {
            return;
        }

        Graphic[] graphics = userInputField.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic != null)
            {
                graphic.raycastTarget = true;
            }
        }
    }

    private void ApplyNormalButtonVisual(Button button)
    {
        if (button == null)
        {
            return;
        }

        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = normalButtonColor;
        }

        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.color = normalTextColor;
        }
    }

    private static bool IsUnderButton(Transform target)
    {
        return target.GetComponentInParent<Button>() != null;
    }

    private static bool IsPartOfInputField(Component component)
    {
        return component.GetComponentInParent<TMP_InputField>() != null;
    }

    private static void SetRaycastTarget(Graphic graphic, bool enabled)
    {
        if (graphic != null)
        {
            graphic.raycastTarget = enabled;
        }
    }

    public void NotifyTranscriptApplied()
    {
        RefreshCategoryButtonVisuals();
    }

    public void NotifyTranscriptApplied(string transcript)
    {
        AppendPendingVoiceText(transcript);
        RefreshCategoryButtonVisuals();
        Debug.Log("[AnnotationPanel] Recording complete. Waiting for Save.");
    }

    private void AppendPendingVoiceText(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        string incoming = transcript.Trim();
        if (string.IsNullOrEmpty(pendingVoiceText))
        {
            pendingVoiceText = incoming;
        }
        else
        {
            pendingVoiceText = pendingVoiceText + " " + incoming;
        }

        Debug.Log("[AnnotationPanel] Stored voice transcript for card, pendingLength=" + pendingVoiceText.Length);
    }

    private string ResolveUserTextToSave()
    {
        string typed = userInputField != null ? (userInputField.text ?? string.Empty).Replace("\u200B", string.Empty).Trim() : string.Empty;
        string voice = pendingVoiceText != null ? pendingVoiceText.Trim() : string.Empty;

        if (typed.Length > 0 && voice.Length > 0)
        {
            if (typed.Contains(voice))
            {
                return typed;
            }

            return typed + " " + voice;
        }

        if (voice.Length > 0)
        {
            return voice;
        }

        return typed;
    }

    public void RefreshCategoryButtonVisuals()
    {
        selectedButton = hasExplicitCategorySelection ? GetButtonForMemoryType(selectedMemoryType) : null;
        RefreshMemoryTypeButtonVisuals();
    }

    private void SelectMemoryType(MemoryType type)
    {
        selectedMemoryType = type;
        hasExplicitCategorySelection = true;
        selectedPromptQuestion = GetPromptForMemoryType(type);

        if (promptText != null)
        {
            promptText.text = selectedPromptQuestion;
        }
    }

    private void SetSelectedButton(Button button)
    {
        selectedButton = button;
        RefreshCategoryButtonVisuals();
    }

    private void RefreshMemoryTypeButtonVisuals()
    {
        ApplyCategoryButtonVisual(pastLifeButton, pastLifeButton == selectedButton);
        ApplyCategoryButtonVisual(pastSelfButton, pastSelfButton == selectedButton);
        ApplyCategoryButtonVisual(concreteMemoryButton, concreteMemoryButton == selectedButton);
        ApplyCategoryButtonVisual(relationshipButton, relationshipButton == selectedButton);
        ApplyCategoryButtonVisual(transitionMomentButton, transitionMomentButton == selectedButton);
        ApplyCategoryButtonVisual(familiarFeelingButton, familiarFeelingButton == selectedButton);
        ApplyCategoryButtonVisual(strangeFeelingButton, strangeFeelingButton == selectedButton);
        ApplyCategoryButtonVisual(uncertainButton, uncertainButton == selectedButton);
    }

    private void ApplyCategoryButtonVisual(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        Color targetColor = selected ? selectedButtonColor : normalButtonColor;
        Color targetTextColor = selected ? selectedTextColor : normalTextColor;

        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = targetColor;
        }

        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.color = targetTextColor;
        }

        ColorBlock colors = button.colors;
        colors.normalColor = targetColor;
        colors.selectedColor = selectedButtonColor;
        colors.highlightedColor = selected ? selectedButtonColor : highlightedButtonColor;
        button.colors = colors;
    }

    private Button GetButtonForMemoryType(MemoryType type)
    {
        switch (type)
        {
            case MemoryType.PastLife:
                return pastLifeButton;
            case MemoryType.PastSelf:
                return pastSelfButton;
            case MemoryType.ConcreteMemory:
                return concreteMemoryButton;
            case MemoryType.Relationship:
                return relationshipButton;
            case MemoryType.TransitionMoment:
                return transitionMomentButton;
            case MemoryType.FamiliarFeeling:
                return familiarFeelingButton;
            case MemoryType.StrangeFeeling:
                return strangeFeelingButton;
            case MemoryType.Uncertain:
            default:
                return uncertainButton;
        }
    }

    private static string GetAnchorId(MemoryAnchor anchor)
    {
        if (anchor == null || anchor.Data == null || string.IsNullOrEmpty(anchor.Data.id))
        {
            return "(no id)";
        }

        return anchor.Data.id;
    }

    private static string GetPromptForMemoryType(MemoryType type)
    {
        switch (type)
        {
            case MemoryType.PastLife:
                return "這​裡讓​你​想到​以前​某​種​生活、​日常​或​習慣嗎？​那時候​的​生活​大概​是​什麼​樣子？​";
            case MemoryType.PastSelf:
                return "​這裡​是否​讓​你​想到​當時​的​自己？​那時候​的​你​大概​是​什麼​狀態、​感受​或​角色？";
            case MemoryType.ConcreteMemory:
                return "這裡​是否​讓​你​想到​某​一​件​具體發生過​的​事？​可以​簡短​記錄​那​件​事​大概​是​什麼。";
            case MemoryType.Relationship:
                return "這裡​是否​讓​你​想到​某個人，​或​一段​和​他​人​的​關係？​這個人​或​這段​關係​和​這裡​有​什麼​關聯？​";
            case MemoryType.TransitionMoment:
                return "這個地方是否讓你想起改變、離開、適應過程或轉折點？那段過渡與什麼有關？";
            case MemoryType.FamiliarFeeling:
                return "這個地方對你來說是否感到熟悉？這種熟悉感從何而來？";
            case MemoryType.StrangeFeeling:
                return "這個地方對你來說是否感到陌生、遙遠或不同？那種感覺從何而來？";
            case MemoryType.Uncertain:
            default:
                return "你還不確定這個地方的意義，但想先標記嗎？可以留下關鍵字或簡短描述。";
        }
    }
}
