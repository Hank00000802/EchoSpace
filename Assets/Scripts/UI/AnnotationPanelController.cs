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
                titleText.text = "這個地方讓你想到什麼？";
            }

            if (promptText != null)
            {
                promptText.text = "請先選擇一張卡片";
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
            titleText.text = "這個地方讓你想到什麼？";
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
                return "這​裡讓​你​想到​以前​的​某種​生活、​日常​或​習慣嗎？​​那​時候​的​生活​樣貌​是​什麼​樣子​?";
            case MemoryType.PastSelf:
                return "這裡​是否​讓​你​想​起​當時​的​自己？​那​時候​的​你​大概​是​什麼​狀態、​感受​或​樣子？​";
            case MemoryType.ConcreteMemory:
                return "這裡​是否​讓​你​想​起​一​段​具體發生過​的​經驗？​那​件​事​大概​是​什麼樣​?";
            case MemoryType.Relationship:
                return "​這裡​是否​讓​你​想​起​某​個人​或​一段​關係？​ 這個人​或​這段​關係​和​這裡​有​什麼樣​的​關聯​嗎?";
            case MemoryType.TransitionMoment:
                return "這裡讓​你​想到​某次​改變、​離開、​適應​的​過程​或​轉折嗎？​那​次​轉變​大概​和​什麼​有​關?​";
            case MemoryType.FamiliarFeeling:
                return "這裡​是否​讓​你​感到​熟悉？​這種熟悉​感來​自什​麼?";
            case MemoryType.StrangeFeeling:
                return "這​裡讓​你​感到​陌生、​有​距離​或​不​太​一​樣​嗎？​這種​感覺​來​自什​麼?";
            case MemoryType.Uncertain:
            default:
                return "你​是否​還​不​確定​這裡​代表​什麼，​但​仍​想​先​標記​下來？​ ​你​可以​留下​一​個​關​鍵字​或​一句​簡短​描述。​";
        }
    }
}
