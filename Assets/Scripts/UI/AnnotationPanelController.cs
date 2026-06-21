using EchoSpace.Annotation;
using EchoSpace.Core;
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
    public Vector3 panelOffset = new Vector3(0f, 0.35f, 0f);
    public float pullTowardCamera = 0.2f;
    public bool faceCamera = true;
    public bool yawOnly = true;
    public float extraYRotation = 180f;

    public Color normalButtonColor = Color.black;
    public Color selectedButtonColor = new Color(0.25f, 0.65f, 1f, 1f);
    public Color normalTextColor = Color.white;
    public Color selectedTextColor = Color.white;

    [Header("Button Hover Visual")]
    public Color highlightedButtonColor = new Color(0.2f, 0.85f, 1f, 1f);
    public Color pressedButtonColor = new Color(0.1f, 0.6f, 0.9f, 1f);
    public Color disabledButtonColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
    public float buttonColorFadeDuration = 0.08f;

    private MemoryAnchor currentAnchor;
    private MemoryType selectedMemoryType;
    private string selectedPromptQuestion;
    private Button selectedButton;

    private MemoryType snapshotMemoryType;
    private string snapshotPromptQuestion;
    private string snapshotUserText;
    private TimeCategory snapshotTimeCategory;
    private bool snapshotIsAnnotated;
    private string snapshotAnchorId;
    private bool hasSnapshot;

    private bool hasCommittedSaveThisSession;

    public bool IsOpen =>
        panelRoot != null && panelRoot.activeSelf && currentAnchor != null;

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
    }

    private void RegisterMemoryTypeButton(Button button, MemoryType type, System.Action onSelect)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.AddListener(() =>
        {
            Debug.Log("[AnnotationPanel] Memory type button clicked: " + type);
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

        if (currentAnchor != null && currentAnchor != anchor)
        {
            Debug.Log("[AnnotationPanelController] Switching annotation target from old anchor to new anchor.");
            HandleCloseOrSwitchCurrentAnchor();
            ClearEditingState();
        }

        currentAnchor = anchor;
        SnapshotCurrentAnchorData();
        hasCommittedSaveThisSession = false;

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

        ResetAllButtonVisuals();

        if (anchor.Data != null && anchor.Data.isAnnotated)
        {
            ApplyAnnotatedDataToUi(anchor.Data);
        }
        else
        {
            selectedMemoryType = MemoryType.Uncertain;
            selectedPromptQuestion = string.Empty;
            if (userInputField != null)
            {
                userInputField.text = string.Empty;
            }

            if (titleText != null)
            {
                titleText.text = "What does this place remind you of?";
            }

            if (promptText != null)
            {
                promptText.text = "Please select a card first.";
            }
        }

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
        data.userText = userInputField != null ? userInputField.text : string.Empty;
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

        Debug.Log("[AnnotationPanelController] Save complete, panel closed.");
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

        if (userInputField != null)
        {
            userInputField.text = string.Empty;
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

        if (titleText != null)
        {
            titleText.text = "What does this place remind you of?";
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

        Vector3 anchorPos = anchor.transform.position;
        Vector3 targetPos = anchorPos + panelOffset;

        if (cam != null)
        {
            Vector3 toCamera = (cam.transform.position - targetPos).normalized;
            targetPos += toCamera * pullTowardCamera;
        }

        root.position = targetPos;

        if (faceCamera && cam != null)
        {
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

        canvas.renderMode = RenderMode.WorldSpace;

        Camera cam = GetTargetCamera();
        if (cam != null)
        {
            canvas.worldCamera = cam;
        }

        if (canvas.GetComponent<GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        ConfigureCanvasGroups(canvas.transform);
        ConfigureDisplayTextRaycasts();
        SetupAllButtonHoverVisuals();
        ConfigureInputFieldRaycasts();
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

    private void SelectMemoryType(MemoryType type)
    {
        selectedMemoryType = type;
        selectedPromptQuestion = GetPromptForMemoryType(type);

        if (promptText != null)
        {
            promptText.text = selectedPromptQuestion;
        }
    }

    private void SetSelectedButton(Button button)
    {
        selectedButton = button;
        RefreshMemoryTypeButtonVisuals();
    }

    private void RefreshMemoryTypeButtonVisuals()
    {
        ApplyNormalButtonVisual(pastLifeButton);
        ApplyNormalButtonVisual(pastSelfButton);
        ApplyNormalButtonVisual(concreteMemoryButton);
        ApplyNormalButtonVisual(relationshipButton);
        ApplyNormalButtonVisual(transitionMomentButton);
        ApplyNormalButtonVisual(familiarFeelingButton);
        ApplyNormalButtonVisual(strangeFeelingButton);
        ApplyNormalButtonVisual(uncertainButton);

        if (selectedButton == null)
        {
            return;
        }

        Image image = selectedButton.GetComponent<Image>();
        if (image != null)
        {
            image.color = selectedButtonColor;
        }

        TMP_Text text = selectedButton.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.color = selectedTextColor;
        }
    }

    private void ResetAllButtonVisuals()
    {
        selectedButton = null;
        RefreshMemoryTypeButtonVisuals();
    }

    private static string GetPromptForMemoryType(MemoryType type)
    {
        switch (type)
        {
            case MemoryType.PastLife:
                return "Does this place remind you of a past lifestyle, daily routine, or habit? What was life like at that time?";
            case MemoryType.PastSelf:
                return "Does this place remind you of who you were at that time? What was your state, feeling, or self-image like?";
            case MemoryType.ConcreteMemory:
                return "Does this place remind you of a specific experience? What happened?";
            case MemoryType.Relationship:
                return "Does this place remind you of a person or a relationship? How is that person or relationship connected to this place?";
            case MemoryType.TransitionMoment:
                return "Does this place remind you of a change, departure, adaptation process, or turning point? What was that transition related to?";
            case MemoryType.FamiliarFeeling:
                return "Does this place feel familiar to you? Where does this familiarity come from?";
            case MemoryType.StrangeFeeling:
                return "Does this place feel unfamiliar, distant, or different to you? Where does that feeling come from?";
            case MemoryType.Uncertain:
            default:
                return "Are you still unsure what this place means, but would like to mark it first? You may leave a keyword or a short description.";
        }
    }
}
