using UnityEngine;
using Fusion;
using System.Collections;

/// <summary>
/// Manages the table tennis game setup and spawns the networked ball.
/// Attach to a GameObject in the TableTennis scene.
/// </summary>
public class TableTennisManager : NetworkBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private NetworkPrefabRef ballPrefab;
    [SerializeField] private NetworkPrefabRef playerPrefab; // Networked player representation
    [SerializeField] private GameObject racketPrefab; // Local prefab, not networked
    
    [Header("Table Placement (relative to anchor)")]
    [SerializeField] private Vector3 tablePositionOffset = Vector3.zero; // Position offset from anchor
    [SerializeField] private float tableXRotationOffset = 180f; // X rotation offset in degrees (180 to flip upside-down table)
    [SerializeField] private float tableYRotationOffset = 0f; // Y rotation offset in degrees
    
    [Header("Runtime Adjustment Controls")]
    [SerializeField] private float moveSpeed = 2.0f; // Meters per second
    [SerializeField] private float rotateSpeed = 90f; // Degrees per second
    [SerializeField] private bool showAdjustmentInstructions = true;
    
    // Networked table position/rotation for syncing across players
    // These are ANCHOR-RELATIVE so both users see table at same physical location
    [Networked] private Vector3 NetworkedAnchorRelativeTablePos { get; set; }
    [Networked] private float NetworkedTableYRotation { get; set; }
    [Networked] private float NetworkedFloorOffset { get; set; } // Shared floor level adjustment
    
    // Runtime adjustment state
    private GameObject tableRoot;
    private bool isInAdjustMode = false;
    private OVRCameraRig cameraRig;
    
    [Header("Table Setup")]
    [SerializeField] private Transform tableTransform;
    [SerializeField] private Vector3 racket1Position = new Vector3(-0.3f, 0.1f, 0f); // On table surface, player 1 side
    [SerializeField] private Vector3 racket2Position = new Vector3(0.3f, 0.1f, 0f);  // On table surface, player 2 side
    [SerializeField] private Vector3 racketRotation = new Vector3(0f, 0f, 0f); // Handle up
    
    [Header("Ball Spawn")]
    [SerializeField] private Vector3 ballSpawnOffset = new Vector3(0f, 0.5f, 0f); // Above table center
    
    // References
    private NetworkedBall spawnedBall;
    private NetworkObject spawnedPlayer; // Local player's networked representation
    private Transform sharedAnchor;
    private GameObject[] localRackets = new GameObject[2];
    
    /// <summary>
    /// Get the shared anchor transform for other scripts to reference
    /// </summary>
    public Transform GetSharedAnchor() => sharedAnchor;
    
    public override void Spawned()
    {
        Debug.Log($"[TableTennisManager] Spawned. HasStateAuthority: {Object.HasStateAuthority}");
        
        StartCoroutine(InitializeGame());
        
        if (showAdjustmentInstructions)
        {
            Debug.Log("[TableTennisManager] TABLE ADJUSTMENT CONTROLS:");
            Debug.Log("  - Press A button to TOGGLE adjust mode ON/OFF");
            Debug.Log("  - LEFT THUMBSTICK: Move table (X/Z)");
            Debug.Log("  - RIGHT THUMBSTICK X: Rotate table");
            Debug.Log("  - RIGHT THUMBSTICK Y: Move table up/down");
        }
    }
    
    private void Update()
    {
        HandleTableAdjustment();
    }
    
    // Fusion calls this every network tick - apply networked table state
    public override void FixedUpdateNetwork()
    {
        // Apply networked table state to local table object
        ApplyNetworkedTableState();
    }
    
    /// <summary>
    /// Apply the networked table position/rotation to the local table object
    /// Converts anchor-relative position to world position for this user
    /// </summary>
    private void ApplyNetworkedTableState()
    {
        if (tableRoot == null)
        {
            // Try to find table if not set
            tableRoot = GameObject.Find("PingPongTable") ?? GameObject.Find("pingpongtable") 
                        ?? GameObject.Find("pingpong") ?? GameObject.Find("PingPong") ?? GameObject.Find("TableTennis");
            if (tableRoot == null) return;
        }
        
        if (sharedAnchor == null)
        {
            // Can't position table without anchor
            return;
        }
        
        // Convert anchor-relative position to world position
        Vector3 worldPos = sharedAnchor.TransformPoint(NetworkedAnchorRelativeTablePos);
        worldPos.y += NetworkedFloorOffset; // Apply floor offset
        
        // Only log if position changed significantly
        if (Vector3.Distance(tableRoot.transform.position, worldPos) > 0.001f)
        {
            Debug.Log($"[TableTennisManager] Applying table: anchorRel={NetworkedAnchorRelativeTablePos}, world={worldPos}, rot Y={NetworkedTableYRotation}");
        }
        
        tableRoot.transform.position = worldPos;
        
        // Apply networked rotation (relative to anchor rotation)
        tableRoot.transform.rotation = sharedAnchor.rotation * Quaternion.Euler(tableXRotationOffset, NetworkedTableYRotation, 0);
    }
    
    /// <summary>
    /// Handle runtime table position/rotation adjustment via controller
    /// Only the state authority (host) can adjust
    /// </summary>
    private void HandleTableAdjustment()
    {
        // Try to find tableRoot if not set yet
        if (tableRoot == null)
        {
            tableRoot = GameObject.Find("PingPongTable") ?? GameObject.Find("pingpongtable")
                        ?? GameObject.Find("pingpong") ?? GameObject.Find("PingPong") ?? GameObject.Find("TableTennis");
            
            if (tableRoot == null) return;
        }
        
        // Find camera rig for height adjustment
        if (cameraRig == null)
        {
            cameraRig = FindObjectOfType<OVRCameraRig>();
        }
        
        // Toggle adjust mode with A button (Button.One) - check both controllers
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch) ||
            OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch) ||
            OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch))
        {
            isInAdjustMode = !isInAdjustMode;
            Debug.Log($"[TableTennisManager] Adjust mode: {(isInAdjustMode ? "ON" : "OFF")}");
        }
        
        if (!isInAdjustMode) return;
        
        // Only state authority can adjust table
        if (!Object.HasStateAuthority)
        {
            // Request adjustment from host via RPC
            HandleClientAdjustmentInput();
            return;
        }
        
        // Host: directly adjust networked values
        HandleHostAdjustmentInput();
    }
    
    /// <summary>
    /// Host directly modifies networked table state
    /// </summary>
    private void HandleHostAdjustmentInput()
    {
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        
        // Move table with left thumbstick (X/Z movement in anchor-relative space)
        if (leftStick.magnitude > 0.1f)
        {
            Vector3 movement = new Vector3(leftStick.x, 0, leftStick.y) * moveSpeed * Time.deltaTime;
            movement = Quaternion.Euler(0, NetworkedTableYRotation, 0) * movement;
            NetworkedAnchorRelativeTablePos += movement;
        }
        
        // Rotate table with right thumbstick X axis
        if (Mathf.Abs(rightStick.x) > 0.1f)
        {
            float rotation = rightStick.x * rotateSpeed * Time.deltaTime;
            NetworkedTableYRotation += rotation;
        }
        
        // Adjust floor level with right thumbstick Y axis
        if (Mathf.Abs(rightStick.y) > 0.1f)
        {
            float verticalMove = rightStick.y * moveSpeed * Time.deltaTime;
            NetworkedFloorOffset += verticalMove;
        }
    }
    
    /// <summary>
    /// Client sends adjustment requests to host via RPC
    /// </summary>
    private void HandleClientAdjustmentInput()
    {
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        
        // Send adjustment deltas to host
        if (leftStick.magnitude > 0.1f)
        {
            Vector3 movement = new Vector3(leftStick.x, 0, leftStick.y) * moveSpeed * Time.deltaTime;
            Debug.Log($"[TableTennisManager] CLIENT: Sending move RPC: {movement}");
            RPC_RequestTableMove(movement);
        }
        
        if (Mathf.Abs(rightStick.x) > 0.1f)
        {
            float rotation = rightStick.x * rotateSpeed * Time.deltaTime;
            Debug.Log($"[TableTennisManager] CLIENT: Sending rotate RPC: {rotation}");
            RPC_RequestTableRotate(rotation);
        }
        
        if (Mathf.Abs(rightStick.y) > 0.1f)
        {
            float verticalMove = rightStick.y * moveSpeed * Time.deltaTime;
            Debug.Log($"[TableTennisManager] CLIENT: Sending floor adjust RPC: {verticalMove}");
            RPC_RequestFloorAdjust(verticalMove);
        }
    }
    
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestTableMove(Vector3 movement)
    {
        Debug.Log($"[TableTennisManager] HOST: Received move RPC: {movement}");
        movement = Quaternion.Euler(0, NetworkedTableYRotation, 0) * movement;
        NetworkedAnchorRelativeTablePos += movement;
    }
    
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestTableRotate(float rotation)
    {
        Debug.Log($"[TableTennisManager] HOST: Received rotate RPC: {rotation}");
        NetworkedTableYRotation += rotation;
    }
    
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestFloorAdjust(float verticalMove)
    {
        Debug.Log($"[TableTennisManager] HOST: Received floor adjust RPC: {verticalMove}");
        NetworkedFloorOffset += verticalMove;
    }
    
    private IEnumerator InitializeGame()
    {
        // Wait for anchor to be available
        yield return StartCoroutine(WaitForAnchor());
        
        // Place the table at the anchor position
        PlaceTableAtAnchor();
        
        // Setup controller-based rackets (replaces old grab system)
        SetupControllerRackets();
        
        // Spawn networked player representation so other users can see us
        SpawnPlayer();
        
        // Host spawns the ball
        if (Object.HasStateAuthority)
        {
            yield return new WaitForSeconds(0.5f);
            SpawnBall();
        }
    }
    
    /// <summary>
    /// Place the table/pingpong at the anchor position with adjustable offset and Y rotation
    /// </summary>
    private void PlaceTableAtAnchor()
    {
        if (sharedAnchor == null)
        {
            Debug.LogWarning("[TableTennisManager] No anchor to place table at!");
            return;
        }
        
        // Find the PingPongTable (now a separate root object) or fallback to old names
        tableRoot = GameObject.Find("PingPongTable") ?? GameObject.Find("pingpongtable") 
                    ?? GameObject.Find("pingpong") ?? GameObject.Find("PingPong") ?? GameObject.Find("TableTennis");
        
        if (tableRoot == null && tableTransform != null)
        {
            tableRoot = tableTransform.gameObject;
        }
        
        if (tableRoot != null)
        {
            // Host initializes networked values (anchor-relative)
            if (Object.HasStateAuthority)
            {
                // Table position relative to anchor with configured offset
                Vector3 rotatedOffset = Quaternion.Euler(0, tableYRotationOffset, 0) * tablePositionOffset;
                
                // Store anchor-relative position (not world position)
                NetworkedAnchorRelativeTablePos = rotatedOffset;
                NetworkedTableYRotation = tableYRotationOffset;
                NetworkedFloorOffset = 0f;
                
                Debug.Log($"[TableTennisManager] HOST: Table placed at anchor-relative pos={rotatedOffset}, rotation Y={tableYRotationOffset}");
            }
            else
            {
                // Client: just log, will receive networked values via FixedUpdateNetwork
                Debug.Log($"[TableTennisManager] CLIENT: Waiting for networked table position from host...");
            }
            
            // Apply initial position (will be overridden by networked values)
            ApplyNetworkedTableState();
            
            Debug.Log($"[TableTennisManager] Table at world pos: {tableRoot.transform.position}");
        }
        else
        {
            Debug.LogWarning("[TableTennisManager] Could not find table object to place at anchor");
        }
    }
    
    /// <summary>
    /// Setup ControllerRacket component to show racket on controllers
    /// </summary>
    private void SetupControllerRackets()
    {
        // ControllerRacket will auto-find rackets in the scene
        // Just create the manager if it doesn't exist
        var existingManager = GameObject.Find("ControllerRacketManager");
        if (existingManager == null)
        {
            var manager = new GameObject("ControllerRacketManager");
            // Use string-based AddComponent to avoid compile order issues
            var component = manager.AddComponent(System.Type.GetType("ControllerRacket"));
            if (component != null)
            {
                Debug.Log("[TableTennisManager] Created ControllerRacketManager - press grip to show racket on controller");
            }
            else
            {
                // Fallback: try direct add
                manager.AddComponent<ControllerRacket>();
                Debug.Log("[TableTennisManager] Created ControllerRacketManager (direct)");
            }
        }
    }
    
    /// <summary>
    /// Parent the pingpong/table objects to the anchor so they stay fixed in anchor space
    /// This is critical for colocation - objects must be relative to the shared anchor
    /// </summary>
    private void ParentSceneToAnchor()
    {
        if (sharedAnchor == null)
        {
            Debug.LogWarning("[TableTennisManager] No anchor to parent scene to!");
            return;
        }
        
        // Find the main game parent object
        GameObject gameRoot = null;
        
        // Try to find pingpong parent
        gameRoot = GameObject.Find("pingpong");
        if (gameRoot == null) gameRoot = GameObject.Find("PingPong");
        if (gameRoot == null) gameRoot = GameObject.Find("TableTennis");
        if (gameRoot == null && tableTransform != null) gameRoot = tableTransform.gameObject;
        
        if (gameRoot != null)
        {
            // Store current world position/rotation
            Vector3 worldPos = gameRoot.transform.position;
            Quaternion worldRot = gameRoot.transform.rotation;
            
            // Parent to anchor
            gameRoot.transform.SetParent(sharedAnchor, worldPositionStays: true);
            
            Debug.Log($"[TableTennisManager] Parented '{gameRoot.name}' to anchor. Local pos: {gameRoot.transform.localPosition}");
        }
        else
        {
            Debug.LogWarning("[TableTennisManager] Could not find game root object to parent to anchor");
        }
    }
    
    private IEnumerator WaitForAnchor()
    {
        int attempts = 0;
        Debug.Log("[TableTennisManager] Starting to search for anchor...");
        
        while (sharedAnchor == null && attempts < 50)
        {
            // Look for any OVRSpatialAnchor that was preserved from the previous scene
            var anchors = FindObjectsOfType<OVRSpatialAnchor>(true); // Include inactive
            Debug.Log($"[TableTennisManager] Attempt {attempts}: Found {anchors.Length} OVRSpatialAnchor objects");
            
            foreach (var anchor in anchors)
            {
                Debug.Log($"[TableTennisManager] Checking anchor: {anchor.gameObject.name}, Localized: {anchor.Localized}, UUID: {anchor.Uuid}");
                
                // Check if anchor is localized and valid
                if (anchor != null && anchor.Localized)
                {
                    sharedAnchor = anchor.transform;
                    Debug.Log($"[TableTennisManager] Found localized anchor: {anchor.gameObject.name}, UUID: {anchor.Uuid}");
                    
                    // Don't re-align here - alignment was already done in the first scene
                    // Re-aligning can cause the scene to flip/rotate incorrectly
                    break;
                }
                
                // Fallback: check by name for anchors that might not be fully localized yet
                if (anchor.gameObject.name.Contains("Shared") || 
                    anchor.gameObject.name.Contains("Anchor"))
                {
                    sharedAnchor = anchor.transform;
                    Debug.Log($"[TableTennisManager] Found anchor by name: {anchor.gameObject.name}");
                    break;
                }
            }
            
            attempts++;
            yield return new WaitForSeconds(0.2f);
        }
        
        if (sharedAnchor == null)
        {
            Debug.LogWarning("[TableTennisManager] Could not find shared anchor after 50 attempts");
            
            // Use table as fallback reference
            if (tableTransform != null)
            {
                sharedAnchor = tableTransform;
                Debug.Log("[TableTennisManager] Using table as fallback anchor reference");
            }
        }
        else
        {
            Debug.Log($"[TableTennisManager] Anchor found and set: {sharedAnchor.name} at position {sharedAnchor.position}");
        }
    }
    
    /// <summary>
    /// Find existing rackets in the scene and ensure they have GrabbableRacket component
    /// </summary>
    private void FindExistingRackets()
    {
        // Find rackets by tag or name in the scene
        var allRackets = GameObject.FindGameObjectsWithTag("Racket");
        
        if (allRackets.Length == 0)
        {
            // Try finding by name if not tagged
            var pingPongParent = GameObject.Find("pingpong");
            if (pingPongParent == null)
            {
                pingPongParent = GameObject.Find("PingPong");
            }
            
            if (pingPongParent != null)
            {
                // Find all children that might be rackets
                foreach (Transform child in pingPongParent.GetComponentsInChildren<Transform>())
                {
                    if (child.name.ToLower().Contains("racket") || child.name.ToLower().Contains("paddle"))
                    {
                        EnsureRacketSetup(child.gameObject);
                        
                        // Add to our tracking array
                        if (localRackets[0] == null)
                            localRackets[0] = child.gameObject;
                        else if (localRackets[1] == null)
                            localRackets[1] = child.gameObject;
                    }
                }
            }
        }
        else
        {
            // Found rackets by tag
            for (int i = 0; i < Mathf.Min(allRackets.Length, 2); i++)
            {
                localRackets[i] = allRackets[i];
                EnsureRacketSetup(allRackets[i]);
            }
        }
        
        int racketCount = (localRackets[0] != null ? 1 : 0) + (localRackets[1] != null ? 1 : 0);
        Debug.Log($"[TableTennisManager] Found {racketCount} existing rackets in scene");
    }
    
    private void EnsureRacketSetup(GameObject racket)
    {
        // Ensure it has a collider for ball detection
        if (racket.GetComponent<Collider>() == null)
        {
            var boxCollider = racket.AddComponent<BoxCollider>();
            // Adjust size based on typical racket dimensions
            boxCollider.size = new Vector3(0.15f, 0.01f, 0.17f);
        }
        
        // Ensure tagged for ball collision
        racket.tag = "Racket";
        
        // Add rigidbody for velocity tracking
        var rb = racket.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = racket.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true; // Start kinematic (on table)
        rb.useGravity = false;
    }
    
    private void SpawnBall()
    {
        if (ballPrefab == default)
        {
            Debug.LogError("[TableTennisManager] Ball prefab not assigned!");
            return;
        }
        
        Vector3 spawnPosition = Vector3.zero;
        
        if (tableTransform != null)
        {
            spawnPosition = tableTransform.TransformPoint(ballSpawnOffset);
        }
        else if (sharedAnchor != null)
        {
            spawnPosition = sharedAnchor.TransformPoint(new Vector3(0, 1.2f, 0));
        }
        
        var ballObj = Runner.Spawn(
            ballPrefab,
            spawnPosition,
            Quaternion.identity,
            Object.InputAuthority
        );
        
        if (ballObj != null)
        {
            spawnedBall = ballObj.GetComponent<NetworkedBall>();
            Debug.Log($"[TableTennisManager] Spawned networked ball at {spawnPosition}");
        }
    }
    
    /// <summary>
    /// Spawn networked player representation so other users can see this player
    /// </summary>
    private void SpawnPlayer()
    {
        if (playerPrefab == default)
        {
            Debug.LogWarning("[TableTennisManager] Player prefab not assigned! Other users won't see this player's representation.");
            return;
        }
        
        // Each player spawns their own NetworkedPlayer with input authority
        Vector3 spawnPosition = Vector3.zero;
        if (sharedAnchor != null)
        {
            spawnPosition = sharedAnchor.position;
        }
        
        // Spawn with input authority so this client controls it
        spawnedPlayer = Runner.Spawn(
            playerPrefab,
            spawnPosition,
            Quaternion.identity,
            Runner.LocalPlayer, // Give input authority to local player
            onBeforeSpawned: (runner, obj) =>
            {
                Debug.Log($"[TableTennisManager] NetworkedPlayer spawned for local player");
            }
        );
        
        if (spawnedPlayer != null)
        {
            Debug.Log($"[TableTennisManager] Spawned networked player representation");
        }
    }
    
    /// <summary>
    /// Reset the game - respawn ball, reset rackets
    /// </summary>
    public void ResetGame()
    {
        // Rackets are now attached to controllers via ControllerRacket, no need to reset
        
        // Reset ball (handled by NetworkedBall)
        if (spawnedBall != null)
        {
            spawnedBall.RequestServe(Vector3.forward);
        }
    }
    
    /// <summary>
    /// Serve the ball
    /// </summary>
    public void ServeBall()
    {
        if (spawnedBall != null)
        {
            spawnedBall.RequestServe(Vector3.forward);
        }
    }
    
    private void OnDestroy()
    {
        // Cleanup local rackets
        foreach (var racket in localRackets)
        {
            if (racket != null)
            {
                Destroy(racket);
            }
        }
    }
}
