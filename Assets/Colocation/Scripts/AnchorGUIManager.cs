using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

#if FUSION2
using Fusion;
#endif

/// <summary>
/// GUI Manager for spatial anchor creation, saving, loading, and sharing in VR.
/// Integrates with ColocationManager and AlignmentManager.
/// </summary>
public class AnchorGUIManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("Manager References")]
    [Tooltip("Reference to the ColocationManager script")]
    [SerializeField] private ColocationManager colocationManager;

    [Tooltip("Reference to the AlignmentManager script")]
    [SerializeField] private AlignmentManager alignmentManager;

    [Header("Main Action Buttons")]
    [SerializeField] private Button hostSessionButton;
    [SerializeField] private Button joinSessionButton;
    [SerializeField] private Button createAnchorButton;
    [SerializeField] private Button saveAnchorButton;
    [SerializeField] private Button loadAnchorsButton;
    [SerializeField] private Button shareAnchorsButton;
    [SerializeField] private Button clearAnchorsButton;

    [Header("Manual UUID Input")]
    [SerializeField] private TMP_InputField groupUuidInputField;
    [SerializeField] private Button loadFromUuidButton;
    [SerializeField] private Button copyUuidButton;

    [Header("Status Display")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI groupUuidText;
    [SerializeField] private TextMeshProUGUI anchorCountText;
    [SerializeField] private TextMeshProUGUI connectionStateText;
    [SerializeField] private Image statusIndicator;

    [Header("Anchor List Panel")]
    [SerializeField] private GameObject anchorListPanel;
    [SerializeField] private Transform anchorListContainer;
    [SerializeField] private GameObject anchorListItemPrefab;
    [SerializeField] private Button toggleAnchorListButton;
    [SerializeField] private Button closeAnchorListButton;

    [Header("UI Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject statusPanel;
    [SerializeField] private GameObject debugPanel;

    [Header("Debug Panel")]
    [SerializeField] private TextMeshProUGUI debugLogText;
    [SerializeField] private Toggle showDebugToggle;
    [SerializeField] private Button clearDebugButton;
    [SerializeField] private ScrollRect debugScrollRect;

    [Header("Confirmation Dialog")]
    [SerializeField] private GameObject confirmationDialog;
    [SerializeField] private TextMeshProUGUI confirmationText;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;

    [Header("Settings")]
    [SerializeField] private float anchorCreationDistance = 1.5f;
    [SerializeField] private bool autoAlignOnLoad = true;
    [SerializeField] private Color hostColor = Color.green;
    [SerializeField] private Color clientColor = Color.cyan;
    [SerializeField] private Color idleColor = Color.gray;

    #endregion

    #region Private Fields

    private List<OVRSpatialAnchor> currentAnchors = new List<OVRSpatialAnchor>();
    private List<GameObject> anchorListItems = new List<GameObject>();
    private bool isHost = false;
    private bool isInitialized = false;
    private Guid currentGroupUuid = Guid.Empty;
    private Transform cameraTransform;
    private Action pendingConfirmationAction;

    // State tracking
    private enum SessionState
    {
        Idle,
        Hosting,
        Discovering,
        Loading,
        Sharing,
        Aligning
    }

    private SessionState currentState = SessionState.Idle;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        InitializeGUI();
    }

    private void OnDestroy()
    {
        CleanupGUI();
    }

    private void Update()
    {
        // Update UI elements that need real-time updates
        if (isInitialized)
        {
            UpdateStatusIndicator();
        }
    }

    #endregion

    #region Initialization

    private void InitializeGUI()
    {
        // Get camera reference
        cameraTransform = Camera.main?.transform;
        if (cameraTransform == null)
        {
            Debug.LogError("[AnchorGUI] No main camera found!");
            return;
        }

        // Setup button listeners
        SetupButtonListeners();

        // Subscribe to Unity log messages for debug panel
        Application.logMessageReceived += OnLogMessageReceived;

        // Subscribe to ColocationManager events if available
        if (colocationManager != null)
        {
            // Note: You'll need to add these events to ColocationManager
            // colocationManager.OnSessionStarted += OnSessionStarted;
            // colocationManager.OnAnchorCreated += OnAnchorCreated;
            // colocationManager. OnAnchorsLoaded += OnAnchorsLoaded;
        }

        // Initial UI state
        UpdateAllUI();
        SetSessionState(SessionState.Idle);

        // Hide panels initially
        if (anchorListPanel != null) anchorListPanel.SetActive(false);
        if (debugPanel != null) debugPanel.SetActive(false);
        if (confirmationDialog != null) confirmationDialog.SetActive(false);

        isInitialized = true;
        LogStatus("Anchor GUI initialized.  Ready to use.");
    }

    private void CleanupGUI()
    {
        Application.logMessageReceived -= OnLogMessageReceived;

        // Unsubscribe from ColocationManager events
        if (colocationManager != null)
        {
            // colocationManager.OnSessionStarted -= OnSessionStarted;
            // etc.
        }

        // Clear anchor list
        ClearAnchorListUI();
    }

    private void SetupButtonListeners()
    {
        // Main action buttons
        if (hostSessionButton != null)
            hostSessionButton.onClick.AddListener(OnHostSessionClicked);

        if (joinSessionButton != null)
            joinSessionButton.onClick.AddListener(OnJoinSessionClicked);

        if (createAnchorButton != null)
            createAnchorButton.onClick.AddListener(OnCreateAnchorClicked);

        if (saveAnchorButton != null)
            saveAnchorButton.onClick.AddListener(OnSaveAnchorClicked);

        if (loadAnchorsButton != null)
            loadAnchorsButton.onClick.AddListener(OnLoadAnchorsClicked);

        if (shareAnchorsButton != null)
            shareAnchorsButton.onClick.AddListener(OnShareAnchorsClicked);

        if (clearAnchorsButton != null)
            clearAnchorsButton.onClick.AddListener(OnClearAnchorsClicked);

        // Manual UUID input
        if (loadFromUuidButton != null)
            loadFromUuidButton.onClick.AddListener(OnLoadFromUuidClicked);

        if (copyUuidButton != null)
            copyUuidButton.onClick.AddListener(OnCopyUuidClicked);

        // Anchor list panel
        if (toggleAnchorListButton != null)
            toggleAnchorListButton.onClick.AddListener(OnToggleAnchorListClicked);

        if (closeAnchorListButton != null)
            closeAnchorListButton.onClick.AddListener(() => anchorListPanel.SetActive(false));

        // Debug panel
        if (showDebugToggle != null)
            showDebugToggle.onValueChanged.AddListener(OnDebugToggleChanged);

        if (clearDebugButton != null)
            clearDebugButton.onClick.AddListener(OnClearDebugClicked);

        // Confirmation dialog
        if (confirmYesButton != null)
            confirmYesButton.onClick.AddListener(OnConfirmationYes);

        if (confirmNoButton != null)
            confirmNoButton.onClick.AddListener(OnConfirmationNo);
    }

    #endregion

    #region Button Handlers - Session Management

    private void OnHostSessionClicked()
    {
        if (colocationManager == null)
        {
            LogStatus("ColocationManager not assigned!", true);
            return;
        }

        LogStatus("Starting HOST session...");
        SetSessionState(SessionState.Hosting);
        isHost = true;

        // This assumes you've added a public method to ColocationManager
        // colocationManager.StartHostSession();

        // For now, simulate the host workflow
        StartHostSessionSimulated();
    }

    private void OnJoinSessionClicked()
    {
        if (colocationManager == null)
        {
            LogStatus("ColocationManager not assigned!", true);
            return;
        }

        LogStatus("Searching for nearby sessions...");
        SetSessionState(SessionState.Discovering);
        isHost = false;

        // This assumes you've added a public method to ColocationManager
        // colocationManager.StartClientDiscovery();

        StartClientDiscoverySimulated();
    }

    #endregion

    #region Button Handlers - Anchor Operations

    private async void OnCreateAnchorClicked()
    {
        if (cameraTransform == null)
        {
            LogStatus("Camera not found!", true);
            return;
        }

        LogStatus("Creating spatial anchor...");

        try
        {
            // Calculate anchor position (in front of user)
            Vector3 anchorPosition = cameraTransform.position +
                                    cameraTransform.forward * anchorCreationDistance;

            // Use ground level (Y = 0) for consistency
            anchorPosition.y = 0;

            // Create the anchor
            var anchor = await CreateAnchorAtPosition(anchorPosition, Quaternion.identity);

            if (anchor != null)
            {
                currentAnchors.Add(anchor);
                LogStatus($"✓ Anchor created!  UUID: {anchor.Uuid.ToString().Substring(0, 8)}.. .");
                RefreshAnchorList();
                UpdateAllUI();
            }
            else
            {
                LogStatus("✗ Failed to create anchor", true);
            }
        }
        catch (Exception e)
        {
            LogStatus($"✗ Error creating anchor: {e.Message}", true);
            Debug.LogError($"[AnchorGUI] Exception in CreateAnchor: {e}");
        }
    }

    private async void OnSaveAnchorClicked()
    {
        if (currentAnchors.Count == 0)
        {
            LogStatus("No anchors to save!", true);
            return;
        }

        LogStatus($"Saving {currentAnchors.Count} anchor(s)...");

        try
        {
            int savedCount = 0;
            int failedCount = 0;

            foreach (var anchor in currentAnchors)
            {
                if (anchor == null) continue;

                var saveResult = await anchor.SaveAnchorAsync();

                if (saveResult.Success)
                {
                    savedCount++;
                    Debug.Log($"[AnchorGUI] Saved anchor: {anchor.Uuid}");
                }
                else
                {
                    failedCount++;
                    Debug.LogWarning($"[AnchorGUI] Failed to save anchor {anchor.Uuid}: {saveResult.Status}");
                }
            }

            if (failedCount == 0)
            {
                LogStatus($"✓ All {savedCount} anchor(s) saved successfully!");
            }
            else
            {
                LogStatus($"⚠ Saved {savedCount}, Failed {failedCount}", true);
            }

            UpdateAllUI();
        }
        catch (Exception e)
        {
            LogStatus($"✗ Error saving anchors: {e.Message}", true);
            Debug.LogError($"[AnchorGUI] Exception in SaveAnchors: {e}");
        }
    }

    private async void OnLoadAnchorsClicked()
    {
        if (currentGroupUuid == Guid.Empty)
        {
            LogStatus("No Group UUID!  Host or join a session first.", true);
            return;
        }

        LogStatus($"Loading anchors from group.. .");
        SetSessionState(SessionState.Loading);

        try
        {
            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadResult = await OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(
                currentGroupUuid,
                unboundAnchors
            );

            if (!loadResult.Success)
            {
                LogStatus($"✗ Failed to load anchors: {loadResult.Status}", true);
                SetSessionState(SessionState.Idle);
                return;
            }

            if (unboundAnchors.Count == 0)
            {
                LogStatus("No anchors found in this group", true);
                SetSessionState(SessionState.Idle);
                return;
            }

            LogStatus($"Found {unboundAnchors.Count} anchor(s).  Localizing...");

            int localizedCount = 0;
            foreach (var unboundAnchor in unboundAnchors)
            {
                bool localized = await unboundAnchor.LocalizeAsync();

                if (localized)
                {
                    // Create GameObject and bind anchor
                    var anchorGO = new GameObject($"LoadedAnchor_{unboundAnchor.Uuid.ToString().Substring(0, 8)}");
                    var spatialAnchor = anchorGO.AddComponent<OVRSpatialAnchor>();
                    unboundAnchor.BindTo(spatialAnchor);

                    currentAnchors.Add(spatialAnchor);
                    localizedCount++;

                    Debug.Log($"[AnchorGUI] Localized anchor: {unboundAnchor.Uuid}");

                    // Auto-align to first anchor if enabled
                    if (autoAlignOnLoad && localizedCount == 1 && alignmentManager != null)
                    {
                        LogStatus("Auto-aligning to anchor...");
                        SetSessionState(SessionState.Aligning);
                        alignmentManager.AlignUserToAnchor(spatialAnchor);
                    }
                }
                else
                {
                    Debug.LogWarning($"[AnchorGUI] Failed to localize anchor: {unboundAnchor.Uuid}");
                }
            }

            RefreshAnchorList();
            LogStatus($"✓ Loaded and localized {localizedCount}/{unboundAnchors.Count} anchors");
            SetSessionState(SessionState.Idle);
            UpdateAllUI();
        }
        catch (Exception e)
        {
            LogStatus($"✗ Error loading anchors: {e.Message}", true);
            Debug.LogError($"[AnchorGUI] Exception in LoadAnchors: {e}");
            SetSessionState(SessionState.Idle);
        }
    }

    private async void OnShareAnchorsClicked()
    {
        if (currentAnchors.Count == 0)
        {
            LogStatus("No anchors to share!", true);
            return;
        }

        if (currentGroupUuid == Guid.Empty)
        {
            LogStatus("No Group UUID! Please host a session first.", true);
            return;
        }

        LogStatus($"Sharing {currentAnchors.Count} anchor(s)...");
        SetSessionState(SessionState.Sharing);

        try
        {
            // Filter out null anchors
            var validAnchors = new List<OVRSpatialAnchor>();
            foreach (var anchor in currentAnchors)
            {
                if (anchor != null && anchor.Localized)
                {
                    validAnchors.Add(anchor);
                }
            }

            if (validAnchors.Count == 0)
            {
                LogStatus("No valid anchors to share!", true);
                SetSessionState(SessionState.Idle);
                return;
            }

            var shareResult = await OVRSpatialAnchor.ShareAsync(validAnchors, currentGroupUuid);

            if (shareResult.Success)
            {
                LogStatus($"✓ Successfully shared {validAnchors.Count} anchor(s)!");
            }
            else
            {
                LogStatus($"✗ Failed to share anchors: {shareResult.Status}", true);
            }

            SetSessionState(SessionState.Idle);
            UpdateAllUI();
        }
        catch (Exception e)
        {
            LogStatus($"✗ Error sharing anchors: {e.Message}", true);
            Debug.LogError($"[AnchorGUI] Exception in ShareAnchors: {e}");
            SetSessionState(SessionState.Idle);
        }
    }

    private void OnClearAnchorsClicked()
    {
        if (currentAnchors.Count == 0)
        {
            LogStatus("No anchors to clear!", true);
            return;
        }

        // Show confirmation dialog
        ShowConfirmationDialog(
            $"Are you sure you want to clear {currentAnchors.Count} anchor(s)?",
            () => {
                // Destroy all anchor GameObjects
                foreach (var anchor in currentAnchors)
                {
                    if (anchor != null && anchor.gameObject != null)
                    {
                        Destroy(anchor.gameObject);
                    }
                }

                currentAnchors.Clear();
                RefreshAnchorList();
                LogStatus("✓ All anchors cleared");
                UpdateAllUI();
            }
        );
    }

    #endregion

    #region Button Handlers - Manual UUID

    private void OnLoadFromUuidClicked()
    {
        if (groupUuidInputField == null)
        {
            LogStatus("UUID input field not assigned!", true);
            return;
        }

        string uuidString = groupUuidInputField.text.Trim();

        if (string.IsNullOrEmpty(uuidString))
        {
            LogStatus("Please enter a Group UUID", true);
            return;
        }

        if (Guid.TryParse(uuidString, out Guid parsedGuid))
        {
            currentGroupUuid = parsedGuid;
            LogStatus($"✓ Group UUID set: {currentGroupUuid.ToString().Substring(0, 13)}...");
            UpdateAllUI();

            // Automatically trigger load
            OnLoadAnchorsClicked();
        }
        else
        {
            LogStatus("✗ Invalid UUID format", true);
        }
    }

    private void OnCopyUuidClicked()
    {
        if (currentGroupUuid == Guid.Empty)
        {
            LogStatus("No UUID to copy!", true);
            return;
        }

        GUIUtility.systemCopyBuffer = currentGroupUuid.ToString();
        LogStatus("✓ UUID copied to clipboard!");
    }

    #endregion

    #region Button Handlers - UI Controls

    private void OnToggleAnchorListClicked()
    {
        if (anchorListPanel != null)
        {
            bool newState = !anchorListPanel.activeSelf;
            anchorListPanel.SetActive(newState);

            if (newState)
            {
                RefreshAnchorList();
            }
        }
    }

    private void OnDebugToggleChanged(bool isOn)
    {
        if (debugPanel != null)
        {
            debugPanel.SetActive(isOn);
        }
    }

    private void OnClearDebugClicked()
    {
        if (debugLogText != null)
        {
            debugLogText.text = "[Debug Log Cleared]\n";
        }
    }

    #endregion

    #region Anchor List Management

    private void RefreshAnchorList()
    {
        if (anchorListContainer == null || anchorListItemPrefab == null)
        {
            return;
        }

        // Clear existing items
        ClearAnchorListUI();

        // Create new items for each anchor
        for (int i = 0; i < currentAnchors.Count; i++)
        {
            var anchor = currentAnchors[i];
            if (anchor == null) continue;

            GameObject listItem = Instantiate(anchorListItemPrefab, anchorListContainer);
            anchorListItems.Add(listItem);

            // Find components in the prefab
            var texts = listItem.GetComponentsInChildren<TextMeshProUGUI>();
            var buttons = listItem.GetComponentsInChildren<Button>();

            // Set anchor info text
            if (texts.Length > 0)
            {
                string anchorInfo = $"Anchor #{i + 1}\n" +
                                  $"UUID: {anchor.Uuid.ToString().Substring(0, 13)}...\n" +
                                  $"Localized: {(anchor.Localized ? "✓" : "✗")}\n" +
                                  $"Position: ({anchor.transform.position.x:F2}, " +
                                  $"{anchor.transform.position.y:F2}, " +
                                  $"{anchor.transform.position.z:F2})";

                texts[0].text = anchorInfo;
            }

            // Setup buttons
            if (buttons.Length > 0)
            {
                // Align button
                OVRSpatialAnchor capturedAnchor = anchor; // Capture for closure
                int capturedIndex = i;

                buttons[0].onClick.RemoveAllListeners();
                buttons[0].onClick.AddListener(() => AlignToAnchor(capturedAnchor, capturedIndex));

                // Delete button (if there's a second button)
                if (buttons.Length > 1)
                {
                    buttons[1].onClick.RemoveAllListeners();
                    buttons[1].onClick.AddListener(() => DeleteAnchor(capturedIndex));
                }
            }
        }
    }

    private void ClearAnchorListUI()
    {
        foreach (var item in anchorListItems)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
        anchorListItems.Clear();
    }

    private void AlignToAnchor(OVRSpatialAnchor anchor, int index)
    {
        if (anchor == null)
        {
            LogStatus($"Anchor #{index + 1} is null!", true);
            return;
        }

        if (!anchor.Localized)
        {
            LogStatus($"Anchor #{index + 1} is not localized yet!", true);
            return;
        }

        if (alignmentManager == null)
        {
            LogStatus("AlignmentManager not assigned!", true);
            return;
        }

        LogStatus($"Aligning to Anchor #{index + 1}.. .");
        SetSessionState(SessionState.Aligning);
        alignmentManager.AlignUserToAnchor(anchor);

        // Reset state after a delay
        Invoke(nameof(ResetToIdleState), 2f);
    }

    private void DeleteAnchor(int index)
    {
        if (index < 0 || index >= currentAnchors.Count)
        {
            return;
        }

        ShowConfirmationDialog(
            $"Delete Anchor #{index + 1}? ",
            () => {
                var anchor = currentAnchors[index];
                if (anchor != null && anchor.gameObject != null)
                {
                    Destroy(anchor.gameObject);
                }

                currentAnchors.RemoveAt(index);
                RefreshAnchorList();
                LogStatus($"✓ Anchor #{index + 1} deleted");
                UpdateAllUI();
            }
        );
    }

    #endregion

    #region UI Update Methods

    private void UpdateAllUI()
    {
        UpdateConnectionState();
        UpdateGroupUuidDisplay();
        UpdateAnchorCount();
        UpdateButtonStates();
    }

    private void UpdateConnectionState()
    {
        if (connectionStateText == null) return;

        string stateText = "IDLE";

        switch (currentState)
        {
            case SessionState.Idle:
                stateText = isHost ? "HOST (Idle)" : "CLIENT (Idle)";
                break;
            case SessionState.Hosting:
                stateText = "HOST (Advertising)";
                break;
            case SessionState.Discovering:
                stateText = "CLIENT (Discovering)";
                break;
            case SessionState.Loading:
                stateText = "Loading Anchors...";
                break;
            case SessionState.Sharing:
                stateText = "Sharing Anchors...";
                break;
            case SessionState.Aligning:
                stateText = "Aligning... ";
                break;
        }

        connectionStateText.text = stateText;
    }

    private void UpdateGroupUuidDisplay()
    {
        if (groupUuidText == null) return;

        if (currentGroupUuid == Guid.Empty)
        {
            groupUuidText.text = "Group UUID: None";
        }
        else
        {
            // Show truncated UUID for readability
            string shortUuid = currentGroupUuid.ToString().Substring(0, 13) + "...";
            groupUuidText.text = $"Group: {shortUuid}";
        }
    }

    private void UpdateAnchorCount()
    {
        if (anchorCountText == null) return;

        int localizedCount = 0;
        foreach (var anchor in currentAnchors)
        {
            if (anchor != null && anchor.Localized)
            {
                localizedCount++;
            }
        }

        anchorCountText.text = $"Anchors: {currentAnchors.Count} ({localizedCount} localized)";
    }

    private void UpdateButtonStates()
    {
        // Host/Join buttons
        bool canStartSession = (currentState == SessionState.Idle);
        if (hostSessionButton != null) hostSessionButton.interactable = canStartSession;
        if (joinSessionButton != null) joinSessionButton.interactable = canStartSession;

        // Create anchor - always available
        if (createAnchorButton != null) createAnchorButton.interactable = true;

        // Save - only if we have anchors
        if (saveAnchorButton != null)
            saveAnchorButton.interactable = currentAnchors.Count > 0;

        // Share - only if host and have anchors
        if (shareAnchorsButton != null)
            shareAnchorsButton.interactable = isHost && currentAnchors.Count > 0 && currentGroupUuid != Guid.Empty;

        // Load - only if we have a group UUID
        if (loadAnchorsButton != null)
            loadAnchorsButton.interactable = currentGroupUuid != Guid.Empty;

        // Clear - only if we have anchors
        if (clearAnchorsButton != null)
            clearAnchorsButton.interactable = currentAnchors.Count > 0;

        // Copy UUID - only if we have a UUID
        if (copyUuidButton != null)
            copyUuidButton.interactable = currentGroupUuid != Guid.Empty;
    }

    private void UpdateStatusIndicator()
    {
        if (statusIndicator == null) return;

        Color indicatorColor = idleColor;

        switch (currentState)
        {
            case SessionState.Idle:
                indicatorColor = idleColor;
                break;
            case SessionState.Hosting:
            case SessionState.Sharing:
                indicatorColor = hostColor;
                break;
            case SessionState.Discovering:
            case SessionState.Loading:
            case SessionState.Aligning:
                indicatorColor = clientColor;
                break;
        }

        statusIndicator.color = indicatorColor;
    }

    #endregion

    #region State Management

    private void SetSessionState(SessionState newState)
    {
        currentState = newState;
        UpdateAllUI();
    }

    private void ResetToIdleState()
    {
        SetSessionState(SessionState.Idle);
    }

    #endregion

    #region Status and Logging

    private void LogStatus(string message, bool isError = false)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = isError ? Color.red : Color.white;
        }

        string logPrefix = isError ? "[AnchorGUI ERROR]" : "[AnchorGUI]";
        if (isError)
        {
            Debug.LogWarning($"{logPrefix} {message}");
        }
        else
        {
            Debug.Log($"{logPrefix} {message}");
        }
    }
    private void OnLogMessageReceived(string message, string stackTrace, UnityEngine.LogType type)
    {
        // Only log messages containing "Colocation" or "Anchor"
        if (!message.Contains("Colocation") && !message.Contains("Anchor"))
        {
            return;
        }

        if (debugLogText != null && debugPanel != null && debugPanel.activeSelf)
        {
            string colorCode = type switch
            {
                UnityEngine.LogType.Error => "<color=red>",
                UnityEngine.LogType.Warning => "<color=yellow>",
                UnityEngine.LogType.Log => "<color=white>",
                _ => "<color=gray>"
            };

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            debugLogText.text += $"\n{colorCode}[{timestamp}] {message}</color>";

            // Limit log length to prevent memory issues
            if (debugLogText.text.Length > 10000)
            {
                debugLogText.text = debugLogText.text.Substring(debugLogText.text.Length - 8000);
            }

            // Auto-scroll to bottom
            if (debugScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                debugScrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
    #endregion

    #region Confirmation Dialog

    private void ShowConfirmationDialog(string message, Action onConfirm)
    {
        if (confirmationDialog == null)
        {
            onConfirm?.Invoke();
            return;
        }

        confirmationDialog.SetActive(true);

        if (confirmationText != null)
        {
            confirmationText.text = message;
        }

        pendingConfirmationAction = onConfirm;
    }

    private void OnConfirmationYes()
    {
        if (confirmationDialog != null)
        {
            confirmationDialog.SetActive(false);
        }

        pendingConfirmationAction?.Invoke();
        pendingConfirmationAction = null;
    }

    private void OnConfirmationNo()
    {
        if (confirmationDialog != null)
        {
            confirmationDialog.SetActive(false);
        }

        pendingConfirmationAction = null;
    }

    #endregion

    #region Helper Methods - Anchor Operations

    private async System.Threading.Tasks.Task<OVRSpatialAnchor> CreateAnchorAtPosition(
        Vector3 position,
        Quaternion rotation)
    {
        try
        {
            // Create GameObject for anchor
            var anchorGameObject = new GameObject($"Anchor_{DateTime.Now:HHmmss}")
            {
                transform =
                {
                    position = position,
                    rotation = rotation
                }
            };

            // Add OVRSpatialAnchor component
            var spatialAnchor = anchorGameObject.AddComponent<OVRSpatialAnchor>();

            // Wait for anchor creation
            int timeout = 100; // 100 frames ~= 1. 67 seconds at 60fps
            while (!spatialAnchor.Created && timeout > 0)
            {
                await System.Threading.Tasks.Task.Yield();
                timeout--;
            }

            if (!spatialAnchor.Created)
            {
                Debug.LogError("[AnchorGUI] Anchor creation timed out!");
                Destroy(anchorGameObject);
                return null;
            }

            Debug.Log($"[AnchorGUI] Anchor created successfully.  UUID: {spatialAnchor.Uuid}");
            return spatialAnchor;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AnchorGUI] Error during anchor creation: {e.Message}");
            return null;
        }
    }

    #endregion

    #region Simulated Methods (Replace with actual ColocationManager calls)

    // These are temporary methods to simulate functionality
    // Replace these with actual calls to ColocationManager once you add public methods

    private async void StartHostSessionSimulated()
    {
        // Simulate async operation
        await System.Threading.Tasks.Task.Delay(500);

        // Generate a new Group UUID
        currentGroupUuid = Guid.NewGuid();

        LogStatus($"✓ HOST session started!\nGroup UUID: {currentGroupUuid.ToString().Substring(0, 13)}...");

        // Update input field
        if (groupUuidInputField != null)
        {
            groupUuidInputField.text = currentGroupUuid.ToString();
        }

        SetSessionState(SessionState.Idle);
        UpdateAllUI();

        // TODO: Replace with actual ColocationManager call:
        // colocationManager.StartHostSession();
    }

    private async void StartClientDiscoverySimulated()
    {
        // Simulate async operation
        await System.Threading.Tasks.Task.Delay(1000);

        LogStatus("⚠ Discovery simulation - enter UUID manually or implement real discovery");
        SetSessionState(SessionState.Idle);

        // TODO: Replace with actual ColocationManager call:
        // colocationManager.StartClientDiscovery();
    }

    #endregion

    #region Public API (for external scripts to call)

    /// <summary>
    /// Manually set the Group UUID (useful when receiving it from networking)
    /// </summary>
    public void SetGroupUuid(Guid uuid)
    {
        currentGroupUuid = uuid;
        LogStatus($"Group UUID set externally: {uuid.ToString().Substring(0, 13)}.. .");

        if (groupUuidInputField != null)
        {
            groupUuidInputField.text = uuid.ToString();
        }

        UpdateAllUI();
    }

    /// <summary>
    /// Get the current Group UUID
    /// </summary>
    public Guid GetGroupUuid()
    {
        return currentGroupUuid;
    }

    /// <summary>
    /// Get list of current anchors
    /// </summary>
    public List<OVRSpatialAnchor> GetCurrentAnchors()
    {
        return new List<OVRSpatialAnchor>(currentAnchors);
    }

    /// <summary>
    /// Add an anchor created externally
    /// </summary>
    public void AddAnchor(OVRSpatialAnchor anchor)
    {
        if (anchor != null && !currentAnchors.Contains(anchor))
        {
            currentAnchors.Add(anchor);
            RefreshAnchorList();
            UpdateAllUI();
        }
    }

    #endregion
}