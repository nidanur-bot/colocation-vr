using UnityEngine;

/// <summary>
/// Attaches a racket visual to the controller when grip is pressed.
/// Since controllers are already visible and synced via colocation alignment,
/// this just swaps the controller visual with a racket.
/// </summary>
public class ControllerRacket : MonoBehaviour
{
    [Header("Racket Prefab (optional - will auto-find if not set)")]
    [SerializeField] private GameObject racketPrefab; // Racket model to show on controller
    
    [Header("Settings")]
    [SerializeField] private OVRInput.Button rightActivateButton = OVRInput.Button.Two; // B button for right controller (Button.Two = B when using RTouch)
    [SerializeField] private OVRInput.Button leftActivateButton = OVRInput.Button.Two; // Y button for left controller (Button.Two = Y when using LTouch)
    [SerializeField] private Vector3 racketOffset = new Vector3(0f, 0.03f, 0.04f); // Position offset from controller
    [SerializeField] private Vector3 racketRotation = new Vector3(-33f, 241f, 42f); // Rotation to align handle with controller grip
    [SerializeField] private float racketScale = 10f; // 10x scale for visibility
    
    [Header("Rotation Adjustment (Thumbsticks)")]
    [SerializeField] private float rotationSpeed = 90f; // Degrees per second
    [Tooltip("Left stick horizontal = Y rotation, Right stick horizontal = Z rotation")]
    [SerializeField] private bool enableRotationAdjustment = false; // Disabled - rotation is set
    
    [Header("Offset Adjustment (Hold grip + right stick)")]
    [SerializeField] private float offsetSpeed = 0.1f; // Meters per second (slower for fine adjustment)
    [Tooltip("Hold grip + right stick: vertical = Y offset, horizontal = Z offset")]
    [SerializeField] private bool enableOffsetAdjustment = true;
    
    [Header("Debug")]
    [Tooltip("DEBUG: Show both controller AND racket at the same time for alignment")]
    [SerializeField] private bool debugShowBothVisuals = true; // SET TO FALSE AFTER DEBUGGING
    
    // Controller references
    private Transform leftController;
    private Transform rightController;
    
    // Controller visual components (to hide when racket is shown)
    private GameObject leftControllerVisual;
    private GameObject rightControllerVisual;
    
    // Racket instances attached to controllers
    private GameObject leftRacket;
    private GameObject rightRacket;
    
    // Toggle states
    private bool leftActive = false;
    private bool rightActive = false;
    private bool leftWasPressed = false;
    private bool rightWasPressed = false;
    private bool racketsCreated = false;
    
    private void Start()
    {
        Debug.Log($"[RACKET_DEBUG] ControllerRacket Start - Rotation: {racketRotation}, Offset: {racketOffset}, Scale: {racketScale}");
        FindControllers();
        
        // Auto-find racket prefab if not assigned
        if (racketPrefab == null)
        {
            FindRacketTemplate();
        }
        
        // Create rackets attached to controllers (hidden initially)
        if (racketPrefab != null)
        {
            CreateControllerRackets();
            // Only mark as created if both rackets were actually created
            racketsCreated = (leftRacket != null) && (rightRacket != null);
            Debug.Log($"[ControllerRacket] Initial racket creation - Left: {leftRacket != null}, Right: {rightRacket != null}, Complete: {racketsCreated}");
        }
        else
        {
            Debug.LogWarning("[ControllerRacket] Racket prefab not found on Start - will retry in Update");
        }
        
        Debug.Log("[ControllerRacket] Initialized - press B/Y to show racket on controller");
    }
    
    private void FindRacketTemplate()
    {
        Debug.Log("[ControllerRacket] Looking for racket template...");
        
        // Try to find by tag
        var taggedRackets = GameObject.FindGameObjectsWithTag("Racket");
        Debug.Log($"[ControllerRacket] Found {taggedRackets.Length} objects with 'Racket' tag");
        if (taggedRackets.Length > 0)
        {
            racketPrefab = taggedRackets[0];
            Debug.Log($"[ControllerRacket] Found racket by tag: {racketPrefab.name}");
            return;
        }
        
        // Try to find by name under pingpong parent
        var pingPongParent = GameObject.Find("pingpong") ?? GameObject.Find("PingPong") ?? GameObject.Find("PingPongTable");
        Debug.Log($"[ControllerRacket] PingPong parent found: {pingPongParent != null}");
        if (pingPongParent != null)
        {
            foreach (Transform child in pingPongParent.GetComponentsInChildren<Transform>())
            {
                if (child.name.ToLower().Contains("racket") || child.name.ToLower().Contains("paddle"))
                {
                    racketPrefab = child.gameObject;
                    Debug.Log($"[ControllerRacket] Found racket by name: {racketPrefab.name}");
                    return;
                }
            }
        }
        
        Debug.LogWarning("[ControllerRacket] Could not find racket template in scene! Make sure rackets have 'Racket' tag or are under a pingpong parent.");
        
        Debug.LogWarning("[ControllerRacket] Could not find racket template in scene!");
    }
    
    private void FindControllers()
    {
        var cameraRig = FindObjectOfType<OVRCameraRig>(true);
        if (cameraRig != null)
        {
            leftController = cameraRig.leftControllerAnchor;
            rightController = cameraRig.rightControllerAnchor;
            Debug.Log($"[ControllerRacket] Found controllers - Left: {leftController != null}, Right: {rightController != null}");
            
            // Find controller visuals (OVRControllerHelper or child renderers)
            if (leftController != null)
            {
                leftControllerVisual = FindControllerVisual(leftController);
            }
            if (rightController != null)
            {
                rightControllerVisual = FindControllerVisual(rightController);
            }
        }
        else
        {
            Debug.LogWarning("[ControllerRacket] OVRCameraRig not found!");
        }
    }
    
    private GameObject FindControllerVisual(Transform controllerAnchor)
    {
        // Try to find OVRControllerHelper component
        var controllerHelper = controllerAnchor.GetComponentInChildren<OVRControllerHelper>(true);
        if (controllerHelper != null)
        {
            Debug.Log($"[ControllerRacket] Found OVRControllerHelper on {controllerAnchor.name}");
            return controllerHelper.gameObject;
        }
        
        // Try to find by common names
        foreach (Transform child in controllerAnchor.GetComponentsInChildren<Transform>(true))
        {
            string nameLower = child.name.ToLower();
            if (nameLower.Contains("controller") || nameLower.Contains("model") || nameLower.Contains("visual"))
            {
                if (child.GetComponent<Renderer>() != null || child.GetComponentInChildren<Renderer>() != null)
                {
                    Debug.Log($"[ControllerRacket] Found controller visual: {child.name}");
                    return child.gameObject;
                }
            }
        }
        
        return null;
    }
    
    private void CreateControllerRackets()
    {
        if (racketPrefab == null)
        {
            Debug.LogError("[ControllerRacket] Racket prefab not assigned and couldn't be found!");
            return;
        }
        
        // Hide the original racket(s) on the table
        racketPrefab.SetActive(false);
        
        // Also hide any other rackets in the scene
        var allRackets = GameObject.FindGameObjectsWithTag("Racket");
        foreach (var r in allRackets)
        {
            r.SetActive(false);
        }
        
        // Find and hide rackets by name too
        var pingPongParent = GameObject.Find("pingpong") ?? GameObject.Find("PingPong");
        if (pingPongParent != null)
        {
            foreach (Transform child in pingPongParent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.ToLower().Contains("racket") || child.name.ToLower().Contains("paddle"))
                {
                    if (child.gameObject != leftRacket && child.gameObject != rightRacket)
                    {
                        child.gameObject.SetActive(false);
                        Debug.Log($"[ControllerRacket] Hiding scene racket: {child.name}");
                    }
                }
            }
        }
        
        // Now create the controller rackets (only once, after hiding scene rackets)
        CreateRacketOnController();
    }
    
    private void CreateRacketOnController()
    {
        Debug.Log($"[RACKET_DEBUG] CreateRacketOnController - Prefab: {racketPrefab?.name}, LeftCtrl: {leftController != null}, RightCtrl: {rightController != null}");
        Debug.Log($"[RACKET_DEBUG] Prefab original rotation: {racketPrefab.transform.eulerAngles}");
        
        // Create left controller racket
        if (leftController != null && leftRacket == null)
        {
            // Instantiate without parent first to avoid inheriting rotations
            leftRacket = Instantiate(racketPrefab);
            leftRacket.name = "LeftControllerRacket";
            
            // Reset to identity, then set parent
            leftRacket.transform.SetParent(leftController, false);
            leftRacket.transform.localPosition = racketOffset;
            leftRacket.transform.localRotation = Quaternion.identity; // Reset first
            leftRacket.transform.localRotation = Quaternion.Euler(racketRotation); // Then apply our rotation
            leftRacket.transform.localScale = Vector3.one * racketScale;
            leftRacket.SetActive(false); // Hidden until activated
            
            Debug.Log($"[RACKET_DEBUG] LEFT racket CREATED with rotation: {leftRacket.transform.localEulerAngles}, parent: {leftController.name}");
            
            // Remove any physics/grab components
            CleanupRacketComponents(leftRacket);
        }
        else
        {
            Debug.LogWarning($"[RACKET_DEBUG] LEFT racket NOT created - leftController: {leftController != null}, leftRacket already exists: {leftRacket != null}");
        }
        
        // Create right controller racket
        if (rightController != null && rightRacket == null)
        {
            // Instantiate without parent first to avoid inheriting rotations
            rightRacket = Instantiate(racketPrefab);
            rightRacket.name = "RightControllerRacket";
            
            // Reset to identity, then set parent
            rightRacket.transform.SetParent(rightController, false);
            rightRacket.transform.localPosition = racketOffset;
            rightRacket.transform.localRotation = Quaternion.identity; // Reset first
            rightRacket.transform.localRotation = Quaternion.Euler(racketRotation); // Then apply our rotation
            rightRacket.transform.localScale = Vector3.one * racketScale;
            rightRacket.SetActive(false); // Hidden until activated
            
            Debug.Log($"[RACKET_DEBUG] RIGHT racket CREATED with rotation: {rightRacket.transform.localEulerAngles}, parent: {rightController.name}");
            
            // Remove any physics/grab components
            CleanupRacketComponents(rightRacket);
        }
        else
        {
            Debug.LogWarning($"[RACKET_DEBUG] RIGHT racket NOT created - rightController: {rightController != null}, rightRacket already exists: {rightRacket != null}");
        }
        
        Debug.Log($"[ControllerRacket] Racket creation done - Left: {leftRacket != null}, Right: {rightRacket != null}");
    }
    
    private void CleanupRacketComponents(GameObject racket)
    {
        // Remove rigidbody - we don't want physics on controller-attached racket
        var rb = racket.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
        
        // Set tag to "Racket" for ball collision detection
        racket.tag = "Racket";
        
        // Ensure there's a collider for ball hits
        var collider = racket.GetComponent<Collider>();
        if (collider == null)
        {
            // Try to find collider in children
            collider = racket.GetComponentInChildren<Collider>();
        }
        
        if (collider == null)
        {
            // Add a box collider if none exists
            var boxCollider = racket.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(0.15f, 0.01f, 0.15f); // Paddle face size
            boxCollider.center = new Vector3(0, 0, 0.1f); // Offset to paddle face
            Debug.Log("[ControllerRacket] Added BoxCollider to racket");
        }
        else
        {
            // Make sure it's not a trigger (we want collision detection)
            collider.isTrigger = false;
        }
        
        // Also tag all children
        foreach (Transform child in racket.GetComponentsInChildren<Transform>())
        {
            child.gameObject.tag = "Racket";
        }
        
        Debug.Log($"[ControllerRacket] Racket setup complete - Tag: {racket.tag}, HasCollider: {racket.GetComponent<Collider>() != null || racket.GetComponentInChildren<Collider>() != null}");
    }
    
    private void Update()
    {
        // Always try to find controllers if missing
        if (leftController == null || rightController == null)
        {
            FindControllers();
        }
        
        // Retry creating rackets if not done yet (in case racket prefab wasn't found on Start)
        if (racketPrefab == null)
        {
            FindRacketTemplate();
        }
        
        // Check if we need to create/retry racket creation
        // Create rackets if prefab exists and either racket is missing (and its controller is available)
        bool needsLeftRacket = leftController != null && leftRacket == null;
        bool needsRightRacket = rightController != null && rightRacket == null;
        
        if (racketPrefab != null && (needsLeftRacket || needsRightRacket))
        {
            Debug.Log($"[ControllerRacket] Creating missing rackets - Left needed: {needsLeftRacket}, Right needed: {needsRightRacket}");
            CreateRacketOnController();
            
            // Only mark as fully created if both rackets exist now
            racketsCreated = (leftRacket != null) && (rightRacket != null);
            if (racketsCreated)
            {
                Debug.Log("[ControllerRacket] Both rackets now created successfully");
            }
        }
        
        // Skip input handling if controllers aren't ready yet
        if (leftController == null || rightController == null)
        {
            return;
        }
        
        // Check for toggle on left controller (Y button)
        bool leftPressed = OVRInput.Get(leftActivateButton, OVRInput.Controller.LTouch);
        if (leftPressed)
        {
            Debug.Log($"[RACKET_DEBUG] Left button PRESSED - wasPressed: {leftWasPressed}");
        }
        if (leftPressed && !leftWasPressed)
        {
            // If left is already active, deactivate it
            // If left is not active, activate it and deactivate right
            leftActive = !leftActive;
            
            if (leftActive && rightActive)
            {
                // Deactivate right racket when activating left
                rightActive = false;
                if (rightRacket != null)
                {
                    rightRacket.SetActive(false);
                    SetControllerVisualActive(rightControllerVisual, true);
                }
            }
            
            if (leftRacket != null)
            {
                leftRacket.SetActive(leftActive);
                // Hide/show controller visual
                SetControllerVisualActive(leftControllerVisual, !leftActive);
                Debug.Log($"[RACKET_DEBUG] Left racket: {(leftActive ? "SHOWN" : "HIDDEN")}");
            }
        }
        leftWasPressed = leftPressed;
        
        // Check for toggle on right controller (B button)
        bool rightPressed = OVRInput.Get(rightActivateButton, OVRInput.Controller.RTouch);
        if (rightPressed && !rightWasPressed)
        {
            // If right is already active, deactivate it
            // If right is not active, activate it and deactivate left
            rightActive = !rightActive;
            
            if (rightActive && leftActive)
            {
                // Deactivate left racket when activating right
                leftActive = false;
                if (leftRacket != null)
                {
                    leftRacket.SetActive(false);
                    SetControllerVisualActive(leftControllerVisual, true);
                }
            }
            
            if (rightRacket != null)
            {
                rightRacket.SetActive(rightActive);
                // Hide/show controller visual
                SetControllerVisualActive(rightControllerVisual, !rightActive);
                Debug.Log($"[RACKET_DEBUG] Right racket: {(rightActive ? "SHOWN" : "HIDDEN")}");
            }
        }
        rightWasPressed = rightPressed;
        
        // Thumbstick rotation adjustment (if enabled)
        if (enableRotationAdjustment && (leftActive || rightActive))
        {
            Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            float rightStickX = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x;
            
            if (Mathf.Abs(leftStick.x) > 0.1f || Mathf.Abs(leftStick.y) > 0.1f || Mathf.Abs(rightStickX) > 0.1f)
            {
                racketRotation.x += leftStick.y * rotationSpeed * Time.deltaTime;
                racketRotation.y += leftStick.x * rotationSpeed * Time.deltaTime;
                racketRotation.z += rightStickX * rotationSpeed * Time.deltaTime;
                
                if (leftActive && leftRacket != null)
                    leftRacket.transform.localRotation = Quaternion.Euler(racketRotation);
                if (rightActive && rightRacket != null)
                    rightRacket.transform.localRotation = Quaternion.Euler(racketRotation);
                
                Debug.Log($"[RACKET_DEBUG] Rotation: X={racketRotation.x:F1}, Y={racketRotation.y:F1}, Z={racketRotation.z:F1}");
            }
        }
        
        // Offset adjustment (separate from rotation)
        if (enableOffsetAdjustment && (leftActive || rightActive))
        {
            Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
            
            if (Mathf.Abs(rightStick.x) > 0.1f || Mathf.Abs(rightStick.y) > 0.1f)
            {
                // Right stick vertical = Y offset (up/down)
                racketOffset.y += rightStick.y * offsetSpeed * Time.deltaTime;
                // Right stick horizontal = Z offset (forward/back)
                racketOffset.z += rightStick.x * offsetSpeed * Time.deltaTime;
                
                if (leftActive && leftRacket != null)
                    leftRacket.transform.localPosition = racketOffset;
                if (rightActive && rightRacket != null)
                    rightRacket.transform.localPosition = racketOffset;
                
                Debug.Log($"[RACKET_DEBUG] Offset: Y={racketOffset.y:F3}, Z={racketOffset.z:F3}");
            }
        }
    }
    
    private void SetControllerVisualActive(GameObject controllerVisual, bool active)
    {
        // DEBUG: If debug mode is on, always show controller visual
        if (debugShowBothVisuals)
        {
            active = true;
        }
        
        if (controllerVisual != null)
        {
            // Disable all renderers in the controller visual hierarchy
            var renderers = controllerVisual.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.enabled = active;
            }
            
            // Also try disabling the OVRControllerHelper component itself
            var controllerHelper = controllerVisual.GetComponent<OVRControllerHelper>();
            if (controllerHelper != null)
            {
                controllerHelper.enabled = active;
            }
            
            Debug.Log($"[RACKET_DEBUG] Controller visual {(active ? "SHOWN" : "HIDDEN")} - disabled {renderers.Length} renderers");
        }
        
        // Disable ray/pointer visuals
        DisableRayVisuals(!active);
    }
    
    private void DisableRayVisuals(bool hide)
    {
        // Find and disable common ray/pointer components
        var cameraRig = FindObjectOfType<OVRCameraRig>(true);
        if (cameraRig == null) return;
        
        // Try to find OVRRayHelper components
        var rayHelpers = cameraRig.GetComponentsInChildren<OVRRayHelper>(true);
        foreach (var rayHelper in rayHelpers)
        {
            rayHelper.enabled = !hide;
            Debug.Log($"[RACKET_DEBUG] OVRRayHelper {rayHelper.name}: {(hide ? "DISABLED" : "ENABLED")}");
        }
        
        // Try to find LineRenderer components (commonly used for rays)
        var lineRenderers = cameraRig.GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lineRenderers)
        {
            lr.enabled = !hide;
            Debug.Log($"[RACKET_DEBUG] LineRenderer {lr.name}: {(hide ? "DISABLED" : "ENABLED")}");
        }
        
        // Try to find UIRaycastr or similar pointer components by name
        foreach (Transform child in cameraRig.GetComponentsInChildren<Transform>(true))
        {
            string nameLower = child.name.ToLower();
            if (nameLower.Contains("ray") || nameLower.Contains("pointer") || nameLower.Contains("laser") || 
                nameLower.Contains("cursor") || nameLower.Contains("line"))
            {
                // Disable the entire GameObject
                if (hide)
                {
                    child.gameObject.SetActive(false);
                    Debug.Log($"[RACKET_DEBUG] Disabled ray object: {child.name}");
                }
                else
                {
                    child.gameObject.SetActive(true);
                }
            }
        }
        
        // Also search in the entire scene for ray-related objects
        var allObjects = FindObjectsOfType<GameObject>(true);
        foreach (var obj in allObjects)
        {
            string nameLower = obj.name.ToLower();
            if ((nameLower.Contains("laserpointer") || nameLower.Contains("uiray") || nameLower.Contains("handray")) 
                && !nameLower.Contains("racket"))
            {
                obj.SetActive(!hide);
                Debug.Log($"[RACKET_DEBUG] Scene ray object {obj.name}: {(hide ? "DISABLED" : "ENABLED")}");
            }
        }
        
        if (hide)
        {
            Debug.Log("[RACKET_DEBUG] Attempted to disable all ray/pointer visuals");
        }
    }
    
    /// <summary>
    /// Check if a controller has an active racket (for ball collision)
    /// </summary>
    public bool IsRacketActive(OVRInput.Controller controller)
    {
        if (controller == OVRInput.Controller.LTouch) return leftActive;
        if (controller == OVRInput.Controller.RTouch) return rightActive;
        return false;
    }
    
    /// <summary>
    /// Get the racket GameObject for a controller
    /// </summary>
    public GameObject GetRacket(OVRInput.Controller controller)
    {
        if (controller == OVRInput.Controller.LTouch) return leftRacket;
        if (controller == OVRInput.Controller.RTouch) return rightRacket;
        return null;
    }
    
    /// <summary>
    /// Check if left racket is active (for network sync)
    /// </summary>
    public bool IsLeftRacketActive() => leftActive;
    
    /// <summary>
    /// Check if right racket is active (for network sync)
    /// </summary>
    public bool IsRightRacketActive() => rightActive;
}
