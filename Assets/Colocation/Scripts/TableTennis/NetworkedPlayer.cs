using UnityEngine;
using Fusion;
using System.Collections;
using TMPro;

#if FUSION2
/// <summary>
/// Networked player representation for colocation VR.
/// Syncs head and hand positions relative to the shared spatial anchor.
/// Remote players see visual representations (spheres/controllers/rackets) and a name tag.
/// </summary>
public class NetworkedPlayer : NetworkBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private float headSize = 0.15f;
    [SerializeField] private float handSize = 0.08f;
    [SerializeField] private Color playerColor = Color.cyan;
    
    [Header("Name Tag Settings")]
    [SerializeField] private float nameTagHeight = 0.3f; // Height above head
    [SerializeField] private float nameTagScale = 0.005f; // Scale for world-space text
    
    [Header("Sync Settings")]
    [SerializeField] private float syncRate = 30f; // Updates per second
    
    // Networked state - positions relative to shared anchor
    [Networked] private Vector3 AnchorRelativeHeadPos { get; set; }
    [Networked] private Quaternion AnchorRelativeHeadRot { get; set; }
    [Networked] private Vector3 AnchorRelativeLeftHandPos { get; set; }
    [Networked] private Quaternion AnchorRelativeLeftHandRot { get; set; }
    [Networked] private Vector3 AnchorRelativeRightHandPos { get; set; }
    [Networked] private Quaternion AnchorRelativeRightHandRot { get; set; }
    
    // Networked racket state
    [Networked] private NetworkBool LeftRacketActive { get; set; }
    [Networked] private NetworkBool RightRacketActive { get; set; }
    
    // Networked player name
    [Networked, OnChangedRender(nameof(OnPlayerNameChanged))] 
    private NetworkString<_32> PlayerName { get; set; }
    
    // Local references
    private Transform sharedAnchor;
    private Transform localHead;
    private Transform localLeftHand;
    private Transform localRightHand;
    
    // Visual representations (for remote players)
    private GameObject headVisual;
    private GameObject leftHandVisual;
    private GameObject rightHandVisual;
    private GameObject leftRacketVisual;
    private GameObject rightRacketVisual;
    private GameObject nameTagObject;
    private TextMeshPro nameTagText;
    
    // Racket prefab reference
    private GameObject racketTemplate;
    
    private float lastSyncTime;
    private bool isInitialized;
    
    // Interpolation for smooth remote player movement
    private Vector3 targetHeadPos;
    private Quaternion targetHeadRot;
    private Vector3 targetLeftHandPos;
    private Quaternion targetLeftHandRot;
    private Vector3 targetRightHandPos;
    private Quaternion targetRightHandRot;
    
    public override void Spawned()
    {
        Debug.Log($"[NetworkedPlayer] Spawned - HasInputAuthority: {Object.HasInputAuthority}, StateAuthority: {Object.HasStateAuthority}, Runner.LocalPlayer: {Runner.LocalPlayer}");
        
        if (Object.HasInputAuthority)
        {
            // This is the local player - find OVR camera rig
            Debug.Log("[NetworkedPlayer] This is LOCAL player - will sync positions to network");
            
            // Request state authority so we can modify networked properties
            if (!Object.HasStateAuthority)
            {
                Debug.Log("[NetworkedPlayer] Requesting StateAuthority for local player...");
                Object.RequestStateAuthority();
            }
            
            StartCoroutine(InitializeLocalPlayer());
        }
        else
        {
            // This is a remote player - create visual representation
            Debug.Log("[NetworkedPlayer] This is REMOTE player - will create visual representation");
            StartCoroutine(InitializeRemotePlayer());
        }
    }
    
    private IEnumerator InitializeLocalPlayer()
    {
        // Wait for camera rig to be ready
        OVRCameraRig cameraRig = null;
        while (cameraRig == null)
        {
            cameraRig = FindObjectOfType<OVRCameraRig>();
            yield return null;
        }
        
        localHead = cameraRig.centerEyeAnchor;
        localLeftHand = cameraRig.leftControllerAnchor;
        localRightHand = cameraRig.rightControllerAnchor;
        
        Debug.Log($"[NetworkedPlayer] Found camera rig - Head: {localHead != null}, LeftHand: {localLeftHand != null}, RightHand: {localRightHand != null}");
        
        // Wait for shared anchor
        yield return StartCoroutine(WaitForAnchor());
        
        if (sharedAnchor == null)
        {
            Debug.LogError("[NetworkedPlayer] LOCAL PLAYER: Failed to find shared anchor! Positions will NOT sync!");
        }
        else
        {
            Debug.Log($"[NetworkedPlayer] LOCAL PLAYER: Found anchor at {sharedAnchor.position}. Ready to sync positions.");
        }
        
        // Set player name (Host = Player 1, Client = Player 2)
        if (Object.HasStateAuthority)
        {
            PlayerName = "Player 1 (Host)";
        }
        else
        {
            PlayerName = $"Player {Runner.LocalPlayer.PlayerId}";
        }
        
        // Find local racket controller for state sync
        var racketController = FindObjectOfType<ControllerRacket>();
        
        isInitialized = true;
        Debug.Log($"[NetworkedPlayer] Local player initialized as {PlayerName}");
    }
    
    private IEnumerator InitializeRemotePlayer()
    {
        Debug.Log("[NetworkedPlayer] InitializeRemotePlayer started...");
        
        // Wait for shared anchor
        yield return StartCoroutine(WaitForAnchor());
        
        if (sharedAnchor == null)
        {
            Debug.LogError("[NetworkedPlayer] FAILED: No shared anchor found for remote player!");
        }
        else
        {
            Debug.Log($"[NetworkedPlayer] Remote player found anchor at: {sharedAnchor.position}");
        }
        
        // Create visual representations for remote player
        CreateRemoteVisuals();
        
        // Try to find racket template for remote racket visuals
        FindRacketTemplate();
        
        isInitialized = true;
        Debug.Log($"[NetworkedPlayer] Remote player initialized. Anchor: {sharedAnchor != null}, HeadVisual: {headVisual != null}");
    }
    
    private IEnumerator WaitForAnchor()
    {
        float timeout = 30f;
        float elapsed = 0f;
        
        while (sharedAnchor == null && elapsed < timeout)
        {
            // Try to find anchor via various methods
            var guiManager = FindObjectOfType<AnchorAutoGUIManager>();
            if (guiManager != null)
            {
                var anchor = guiManager.GetLocalizedAnchor();
                if (anchor != null && anchor.Localized)
                {
                    sharedAnchor = anchor.transform;
                    Debug.Log("[NetworkedPlayer] Found anchor via AnchorAutoGUIManager");
                    yield break;
                }
            }
            
            // Try TableTennisManager
            var ttManager = FindObjectOfType<TableTennisManager>();
            if (ttManager != null)
            {
                var anchor = ttManager.GetSharedAnchor();
                if (anchor != null)
                {
                    sharedAnchor = anchor;
                    Debug.Log("[NetworkedPlayer] Found anchor via TableTennisManager");
                    yield break;
                }
            }
            
            // Try finding any localized OVRSpatialAnchor
            foreach (var anchor in FindObjectsOfType<OVRSpatialAnchor>())
            {
                if (anchor.Localized)
                {
                    sharedAnchor = anchor.transform;
                    Debug.Log("[NetworkedPlayer] Found anchor via OVRSpatialAnchor search");
                    yield break;
                }
            }
            
            elapsed += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        
        if (sharedAnchor == null)
        {
            Debug.LogWarning("[NetworkedPlayer] Could not find shared anchor after timeout!");
        }
    }
    
    private void FindRacketTemplate()
    {
        // Try to find racket template in scene
        var taggedRackets = GameObject.FindGameObjectsWithTag("Racket");
        if (taggedRackets.Length > 0)
        {
            racketTemplate = taggedRackets[0];
            return;
        }
        
        // Try to find by name
        var pingPongParent = GameObject.Find("pingpong") ?? GameObject.Find("PingPong");
        if (pingPongParent != null)
        {
            foreach (Transform child in pingPongParent.GetComponentsInChildren<Transform>())
            {
                if (child.name.ToLower().Contains("racket") || child.name.ToLower().Contains("paddle"))
                {
                    racketTemplate = child.gameObject;
                    return;
                }
            }
        }
    }
    
    private void CreateRemoteVisuals()
    {
        // Create head visual (sphere)
        headVisual = CreateSphereVisual("RemoteHead", headSize, playerColor);
        
        // Create name tag above head
        CreateNameTag();
        
        // Create hand visuals (smaller spheres or controller models)
        leftHandVisual = CreateSphereVisual("RemoteLeftHand", handSize, playerColor);
        rightHandVisual = CreateSphereVisual("RemoteRightHand", handSize, playerColor);
        
        Debug.Log("[NetworkedPlayer] Created remote player visuals");
    }
    
    private GameObject CreateSphereVisual(string name, float size, Color color)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.localScale = Vector3.one * size;
        
        // Remove collider (visual only)
        var collider = sphere.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        
        // Set color - try URP shader first, fallback to Standard
        var renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = color;
                renderer.material = mat;
            }
            else
            {
                // Just set color on existing material
                renderer.material.color = color;
            }
        }
        
        return sphere;
    }
    
    private GameObject CreateRacketVisual(string name)
    {
        if (racketTemplate == null) return null;
        
        var racket = Instantiate(racketTemplate);
        racket.name = name;
        racket.transform.localScale = racketTemplate.transform.localScale;
        
        // Remove any physics/interaction components
        var rb = racket.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
        var colliders = racket.GetComponentsInChildren<Collider>();
        foreach (var col in colliders) Destroy(col);
        
        return racket;
    }
    
    private void CreateNameTag()
    {
        try
        {
            // Create name tag container
            nameTagObject = new GameObject("PlayerNameTag");
            
            // Add TextMeshPro component - this may fail if TMP essentials not imported
            nameTagText = nameTagObject.AddComponent<TextMeshPro>();
            if (nameTagText == null)
            {
                Debug.LogWarning("[NetworkedPlayer] Failed to add TextMeshPro component - TMP may not be set up");
                Destroy(nameTagObject);
                nameTagObject = null;
                return;
            }
            
            nameTagText.text = PlayerName.ToString();
            nameTagText.fontSize = 36;
            nameTagText.alignment = TextAlignmentOptions.Center;
            nameTagText.color = Color.white;
            
            // Set scale for world space
            nameTagObject.transform.localScale = Vector3.one * nameTagScale;
            
            Debug.Log($"[NetworkedPlayer] Created name tag: {PlayerName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NetworkedPlayer] Error creating name tag: {e.Message}");
            if (nameTagObject != null)
            {
                Destroy(nameTagObject);
                nameTagObject = null;
            }
        }
    }
    
    private void OnPlayerNameChanged()
    {
        if (nameTagText != null)
        {
            nameTagText.text = PlayerName.ToString();
            Debug.Log($"[NetworkedPlayer] Name tag updated: {PlayerName}");
        }
    }
    
    public override void FixedUpdateNetwork()
    {
        if (!isInitialized || sharedAnchor == null) return;
        
        // Must have StateAuthority to modify [Networked] properties
        if (Object.HasInputAuthority && Object.HasStateAuthority)
        {
            // Local player: sync positions to network
            SyncLocalToNetwork();
        }
        else if (Object.HasInputAuthority && !Object.HasStateAuthority)
        {
            // Still waiting for StateAuthority - request again
            if (Time.frameCount % 100 == 0)
            {
                Debug.LogWarning("[NetworkedPlayer] Have InputAuthority but no StateAuthority yet - requesting...");
                Object.RequestStateAuthority();
            }
        }
    }
    
    private void Update()
    {
        if (!isInitialized || sharedAnchor == null) return;
        
        if (Object.HasInputAuthority)
        {
            // Sync racket state from local ControllerRacket
            SyncRacketState();
        }
        else
        {
            // Remote player: update visual positions from network
            UpdateRemoteVisuals();
        }
    }
    
    private void SyncLocalToNetwork()
    {
        if (Time.time - lastSyncTime < 1f / syncRate) return;
        lastSyncTime = Time.time;
        
        if (sharedAnchor == null)
        {
            if (Time.frameCount % 100 == 0)
            {
                Debug.LogWarning("[NetworkedPlayer] SyncLocalToNetwork: sharedAnchor is null! Cannot sync positions.");
            }
            return;
        }
        
        if (localHead == null)
        {
            if (Time.frameCount % 100 == 0)
            {
                Debug.LogWarning("[NetworkedPlayer] SyncLocalToNetwork: localHead is null!");
            }
            return;
        }
        
        Vector3 newHeadPos = sharedAnchor.InverseTransformPoint(localHead.position);
        Quaternion newHeadRot = Quaternion.Inverse(sharedAnchor.rotation) * localHead.rotation;
        
        // Only log if position actually changed (shows sync is working)
        if (Time.frameCount % 100 == 0)
        {
            Debug.Log($"[NetworkedPlayer] LOCAL SYNC: head world={localHead.position}, anchorRel={newHeadPos}, anchor at {sharedAnchor.position}");
        }
        
        AnchorRelativeHeadPos = newHeadPos;
        AnchorRelativeHeadRot = newHeadRot;
        
        if (localLeftHand != null)
        {
            AnchorRelativeLeftHandPos = sharedAnchor.InverseTransformPoint(localLeftHand.position);
            AnchorRelativeLeftHandRot = Quaternion.Inverse(sharedAnchor.rotation) * localLeftHand.rotation;
        }
        
        if (localRightHand != null)
        {
            AnchorRelativeRightHandPos = sharedAnchor.InverseTransformPoint(localRightHand.position);
            AnchorRelativeRightHandRot = Quaternion.Inverse(sharedAnchor.rotation) * localRightHand.rotation;
        }
    }
    
    private void SyncRacketState()
    {
        // Get local racket state from ControllerRacket
        var racketController = FindObjectOfType<ControllerRacket>();
        if (racketController != null)
        {
            LeftRacketActive = racketController.IsLeftRacketActive();
            RightRacketActive = racketController.IsRightRacketActive();
        }
    }
    
    private void UpdateRemoteVisuals()
    {
        if (sharedAnchor == null)
        {
            if (Time.frameCount % 100 == 0)
            {
                Debug.LogWarning("[NetworkedPlayer] UpdateRemoteVisuals: sharedAnchor is null! Cannot display remote player.");
            }
            return;
        }
        
        // Log more frequently to see if networked values are updating
        if (Time.frameCount % 100 == 0)
        {
            Debug.Log($"[NetworkedPlayer] REMOTE UPDATE: anchorRelHeadPos={AnchorRelativeHeadPos}, anchor={sharedAnchor.position}, computed world pos={sharedAnchor.TransformPoint(AnchorRelativeHeadPos)}");
        }
        
        // Calculate world positions from anchor-relative positions
        targetHeadPos = sharedAnchor.TransformPoint(AnchorRelativeHeadPos);
        targetHeadRot = sharedAnchor.rotation * AnchorRelativeHeadRot;
        targetLeftHandPos = sharedAnchor.TransformPoint(AnchorRelativeLeftHandPos);
        targetLeftHandRot = sharedAnchor.rotation * AnchorRelativeLeftHandRot;
        targetRightHandPos = sharedAnchor.TransformPoint(AnchorRelativeRightHandPos);
        targetRightHandRot = sharedAnchor.rotation * AnchorRelativeRightHandRot;
        
        // Update head visual
        if (headVisual != null)
        {
            headVisual.transform.position = Vector3.Lerp(headVisual.transform.position, targetHeadPos, Time.deltaTime * 15f);
            headVisual.transform.rotation = Quaternion.Slerp(headVisual.transform.rotation, targetHeadRot, Time.deltaTime * 15f);
        }
        
        // Update name tag position (above head, facing local player)
        if (nameTagObject != null)
        {
            Vector3 nameTagPos = targetHeadPos + Vector3.up * nameTagHeight;
            nameTagObject.transform.position = Vector3.Lerp(nameTagObject.transform.position, nameTagPos, Time.deltaTime * 15f);
            
            // Make name tag face the local player's camera
            var localCamera = Camera.main;
            if (localCamera != null)
            {
                nameTagObject.transform.LookAt(localCamera.transform);
                nameTagObject.transform.Rotate(0, 180, 0); // Flip to face camera correctly
            }
        }
        
        // Update left hand
        UpdateHandVisual(leftHandVisual, ref leftRacketVisual, targetLeftHandPos, targetLeftHandRot, LeftRacketActive);
        
        // Update right hand
        UpdateHandVisual(rightHandVisual, ref rightRacketVisual, targetRightHandPos, targetRightHandRot, RightRacketActive);
    }
    
    private void UpdateHandVisual(GameObject handVisual, ref GameObject racketVisual, Vector3 pos, Quaternion rot, bool racketActive)
    {
        if (handVisual == null) return;
        
        // Smooth interpolation
        handVisual.transform.position = Vector3.Lerp(handVisual.transform.position, pos, Time.deltaTime * 15f);
        handVisual.transform.rotation = Quaternion.Slerp(handVisual.transform.rotation, rot, Time.deltaTime * 15f);
        
        // Toggle between hand and racket visual based on networked state
        if (racketActive)
        {
            // Create racket visual if needed
            if (racketVisual == null && racketTemplate != null)
            {
                racketVisual = CreateRacketVisual("RemoteRacket");
            }
            
            if (racketVisual != null)
            {
                racketVisual.SetActive(true);
                racketVisual.transform.position = pos;
                racketVisual.transform.rotation = rot;
                // Apply same offset/rotation as local racket
                racketVisual.transform.localPosition += rot * new Vector3(0f, 0.03f, 0.04f);
                racketVisual.transform.rotation *= Quaternion.Euler(0, 270, 40);
            }
            
            handVisual.SetActive(false);
        }
        else
        {
            handVisual.SetActive(true);
            if (racketVisual != null)
            {
                racketVisual.SetActive(false);
            }
        }
    }
    
    private void OnDestroy()
    {
        // Cleanup visuals
        if (headVisual != null) Destroy(headVisual);
        if (leftHandVisual != null) Destroy(leftHandVisual);
        if (rightHandVisual != null) Destroy(rightHandVisual);
        if (leftRacketVisual != null) Destroy(leftRacketVisual);
        if (rightRacketVisual != null) Destroy(rightRacketVisual);
        if (nameTagObject != null) Destroy(nameTagObject);
    }
}
#endif
