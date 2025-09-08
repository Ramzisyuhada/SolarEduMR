using UnityEngine;
using Unity.Netcode;

public class Planets : NetworkBehaviour
{
    [Header("Data")]
    public string PlanetName;
    [Tooltip("Urutan benar dari Matahari (1..8)")]
    public int IdUrutanBenar;

    [Header("Networked State")]
    // Orbit yang sedang ditempati (0 = belum ditempatkan)
    public NetworkVariable<int> CurrentOrbitIndex = new(0);
    public NetworkVariable<Vector3> NetPos = new(Vector3.zero);
    public NetworkVariable<Quaternion> NetRot = new(Quaternion.identity);

    [Header("Refs")]
    public SolarGameManager manager;
    [Header("Snap Settings")]
    public float maxSnapDistance = 0.35f;        // meter: hanya snap jika cukup dekat
    public bool orientToSnapPoint = true;        // ikut rotasi SnapPoint
    void Awake()
    {
        if (!manager) manager = FindObjectOfType<SolarGameManager>();
    }

    void Update()
    {
        // Klien non-owner mengikuti nilai network
        if (!IsServer && !IsOwner)
        {
            transform.position = Vector3.Lerp(transform.position, NetPos.Value, 0.35f);
            transform.rotation = Quaternion.Slerp(transform.rotation, NetRot.Value, 0.35f);
        }
    }

    /// <summary>
    /// Dipanggil server saat ronde di-reset untuk memindahkan planet ke posisi awal.
    /// </summary>
    public void ResetServer(Vector3 worldPos)
    {
        if (!IsServer) return;

        transform.position = worldPos;
        transform.rotation = Quaternion.identity;

        NetPos.Value = worldPos;
        NetRot.Value = transform.rotation;

        CurrentOrbitIndex.Value = 0;

        // pastikan ownership balik ke server agar state konsisten
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
    }

    /// <summary>
    /// Server-side: set indeks orbit saat planet ditempatkan pada orbit tertentu.
    /// </summary>
    public void SetOrbitIndex(int index)
    {
        if (!IsServer) return;
        CurrentOrbitIndex.Value = index;
    }

    // ================== Interaksi (dipanggil dari bridge HVR/XRI) ==================

    public void OnGrabbedByClient()
    {
        // minta ownership supaya pergerakan di sisi pemegang halus

        if (!IsOwner)
            RequestOwnershipServerRpc(NetworkManager.LocalClientId);
    }

    public void OnReleasedByClient()
    {
        // saat dilepas, server tentukan snap ke orbit terdekat dan set CurrentOrbitIndex
        TrySnapToNearestSlotServerRpc(transform.position);
    }

    // ================== Server RPCs ==================

    [ServerRpc(RequireOwnership = false)]
    void RequestOwnershipServerRpc(ulong clientId)
    {
        if (IsSpawned) NetworkObject.ChangeOwnership(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    void TrySnapToNearestSlotServerRpc(Vector3 worldPos)
    {
        //if (!manager || manager.slots == null || manager.slots.Length == 0) return;
        Debug.Log("Hello world");

        OrbitSlot nearest = null; float best = float.MaxValue;
        foreach (var s in manager.slots)
        {
            if (!s || !s.SnapPoint) continue;
            float d = (s.SnapPoint.position - worldPos).sqrMagnitude;
            if (d < best) { best = d; nearest = s; }
        }

        // cek jarak maksimum agar tidak snap dari jauh
        if (nearest == null) { SyncTransformOnly(); return; }
        if (best > maxSnapDistance * maxSnapDistance) { SyncTransformOnly(); return; }

        // lakukan snap
        transform.position = nearest.SnapPoint.position;
        transform.rotation = orientToSnapPoint ? nearest.SnapPoint.rotation : transform.rotation;

        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;

        CurrentOrbitIndex.Value = nearest.Index;
        nearest.BlinkFeedback();

        // kembalikan ownership ke server biar rapi
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
    }

    void SyncTransformOnly()
    {
        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
    }
}
