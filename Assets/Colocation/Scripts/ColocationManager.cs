#if FUSION2

using Fusion;
using System;
using System.Text;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;

public class ColocationManager : NetworkBehaviour
{
    [SerializeField] private AlignmentManager alignmentManager;

    [Networked] public NetworkString<_64> RoomName { get; set; }
    [Networked] public NetworkString<_64> GroupUuidString { get; set; }

    private Guid _sharedAnchorGroupId;

    public override void Spawned()
    {
        base. Spawned();
    
#if UNITY_ANDROID && !UNITY_EDITOR
        // Real colocation on Quest device
        PrepareColocation();
#else
        // Simulated colocation for Editor testing
        Debug.Log("Colocation: Running in SIMULATION mode (Editor)");
        SimulateColocation();
#endif
    }

    // Add this new method at the bottom of the class
#if UNITY_EDITOR
    private void SimulateColocation()
    {
        if (Object.HasStateAuthority)
        {
            // Simulate HOST
            _sharedAnchorGroupId = System.Guid.NewGuid();
            Debug.Log($"[SIMULATED] Colocation: HOST session started.  Group UUID: {_sharedAnchorGroupId}");
        }
        else
        {
            // Simulate CLIENT
            Debug.Log($"[SIMULATED] Colocation: CLIENT discovery started");
        }
    }
#endif

    private void PrepareColocation()
    {
        if (Object.HasStateAuthority)
        {
            Debug.Log("Colocation: Starting advertisement...");
            AdvertiseColocationSession();
        }
        else
        {
            // CHANGED: Instead of DiscoverNearbySession, wait for host's UUID
            Debug.Log("Colocation: Waiting for host's Group UUID...");
            StartCoroutine(WaitForHostUuid());
        }
    }

    private System. Collections.IEnumerator WaitForHostUuid()
    {
        // Wait until host shares the UUID via network
        while (string.IsNullOrEmpty(GroupUuidString.Value))
        {
            yield return new WaitForSeconds(0.5f);
        }

        Debug.Log($"Colocation: Received Group UUID from host: {GroupUuidString}");
    
        if (Guid.TryParse(GroupUuidString.Value, out Guid groupUuid))
        {
            _sharedAnchorGroupId = groupUuid;
            LoadAndAlignToAnchor(_sharedAnchorGroupId);
        }
        else
        {
            Debug. LogError($"Colocation: Invalid UUID received: {GroupUuidString}");
        }
    }

    private async void AdvertiseColocationSession()
    {
        try
        {
            var advertisementData = Encoding.UTF8.GetBytes("SharedSpatialAnchorSession");
            var startAdvertisementResult = await OVRColocationSession.StartAdvertisementAsync(advertisementData);

            if (startAdvertisementResult.Success)
            {
                _sharedAnchorGroupId = startAdvertisementResult.Value;
            
                // ADD THIS LINE - Share UUID to all clients via network
                RPC_ShareGroupUuid(_sharedAnchorGroupId. ToString());
            
                Debug. Log($"Colocation: Advertisement started successfully. UUID: {_sharedAnchorGroupId}");
                CreateAndShareAlignmentAnchor();
            }
            else
            {
                Debug.LogError($"Colocation: Advertisement failed with status: {startAdvertisementResult.Status}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Colocation: Error during advertisement: {e.Message}");
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShareGroupUuid(string uuidString)
    {
        GroupUuidString = uuidString;
        Debug.Log($"Colocation: Group UUID shared to network: {uuidString. Substring(0, 13)}...");
    }

    //private async void DiscoverNearbySession()
    //{
    //    try
    //    {
    //        OVRColocationSession.ColocationSessionDiscovered += OnColocationSessionDiscovered;

    //        var discoveryResult = await OVRColocationSession.StartDiscoveryAsync();
    //        if (!discoveryResult.Success)
    //        {
    //            Debug.LogError($"Colocation: Discovery failed with status: {discoveryResult.Status}");
    //            return;
    //        }

    //        Debug.Log("Colocation: Discovery started successfully.");
    //    }
    //    catch (Exception e)
    //    {
    //        Debug.LogError($"Colocation: Error during discovery: {e.Message}");
    //    }
    //}

    //private void OnColocationSessionDiscovered(OVRColocationSession.Data session)
    //{
    //    OVRColocationSession.ColocationSessionDiscovered -= OnColocationSessionDiscovered;

    //    _sharedAnchorGroupId = session.AdvertisementUuid;
    //    Debug.Log($"Colocation: Discovered session with UUID: {_sharedAnchorGroupId}");
    //    LoadAndAlignToAnchor(_sharedAnchorGroupId);
    //}

    private async void CreateAndShareAlignmentAnchor()
    {
        try
        {
            Debug.Log("Colocation: Creating alignment anchor.. .");
            var anchor = await CreateAnchor(Vector3.zero, Quaternion.identity);

            if (anchor == null)
            {
                Debug.LogError("Colocation: Failed to create alignment anchor.");
                return;
            }

            // CHANGED: Wait for localization
            Debug.Log("Colocation: Waiting for anchor localization...");
            int timeout = 1000; // ~16 seconds
            while (!anchor.Localized && timeout > 0)
            {
                await Task.Yield();
                timeout--;
            }

            if (!anchor.Localized)
            {
                Debug.LogError("Colocation: Anchor localization timed out.");
                return;
            }

            var saveResult = await anchor.SaveAnchorAsync();
            if (! saveResult.Success)
            {
                Debug.LogError($"Colocation: Failed to save alignment anchor.  Error: {saveResult}");
                return;
            }

            Debug.Log($"Colocation: Alignment anchor saved successfully. UUID: {anchor. Uuid}");
        
            var shareResult = await OVRSpatialAnchor.ShareAsync(new List<OVRSpatialAnchor> { anchor }, _sharedAnchorGroupId);

            if (! shareResult.Success)
            {
                Debug.LogError($"Colocation: Failed to share alignment anchor. Error: {shareResult}");
                return;
            }

            Debug.Log($"Colocation: Alignment anchor shared successfully. Group UUID: {_sharedAnchorGroupId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Colocation: Error during anchor creation and sharing: {e.Message}");
        }
    }

    private async Task<OVRSpatialAnchor> CreateAnchor(Vector3 position, Quaternion rotation)
    {
        try
        {
            var anchorGameObject = new GameObject("Alignment Anchor")
            {
                transform =
                {
                    position = position,
                    rotation = rotation
                }
            };

            var spatialAnchor = anchorGameObject.AddComponent<OVRSpatialAnchor>();
        
            // CHANGED: Add timeout
            int timeout = 1000; // ~16 seconds at 60fps
            while (!spatialAnchor.Created && timeout > 0)
            {
                await Task.Yield();
                timeout--;
            }

            // CHANGED: Check if creation succeeded
            if (!spatialAnchor.Created)
            {
                Debug.LogError("Colocation: Anchor creation timed out!");
                Destroy(anchorGameObject);
                return null;
            }

            Debug.Log($"Colocation: Anchor created successfully. UUID: {spatialAnchor. Uuid}");
            return spatialAnchor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Colocation: Error during anchor creation: {e.Message}");
            return null;
        }
    }

    private async void LoadAndAlignToAnchor(Guid groupUuid)
    {
        try
        {
            Debug.Log($"Colocation: Loading anchors for Group UUID: {groupUuid}...");

            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadResult = await OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(groupUuid, unboundAnchors);

            if (!loadResult.Success || unboundAnchors.Count == 0)
            {
                Debug.LogError($"Colocation: Failed to load anchors. Success: {loadResult.Success}, Count: {unboundAnchors.Count}");
                return;
            }

            foreach (var unboundAnchor in unboundAnchors)
            {
                if (await unboundAnchor.LocalizeAsync())
                {
                    Debug.Log($"Colocation: Anchor localized successfully. UUID: {unboundAnchor.Uuid}");

                    var anchorGameObject = new GameObject($"Anchor_{unboundAnchor.Uuid}");
                    var spatialAnchor = anchorGameObject.AddComponent<OVRSpatialAnchor>();
                    unboundAnchor.BindTo(spatialAnchor);

                    alignmentManager.AlignUserToAnchor(spatialAnchor);
                    return;
                }

                Debug.LogWarning($"Colocation: Failed to localize anchor: {unboundAnchor.Uuid}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Colocation: Error during anchor loading and alignment: {e.Message}");
        }
    }
}
#endif