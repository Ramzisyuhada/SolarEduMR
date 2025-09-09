using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public class Planet : NetworkBehaviour
{
    [Header("Info")]
    public string PlanetName;
    [Tooltip("Urutan benar dari Matahari (1..8)")]
    public int IdUrutanBenar = 1;

    [Header("Networked State")]
    public NetworkVariable<int> CurrentOrbitIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Vector3> NetPos = new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Quaternion> NetRot = new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Refs")]
    public SolarGameManager manager;

    [Header("Snap Settings")]
    [Tooltip("Maksimal jarak untuk fallback snap ke orbit terdekat (kalau tidak ada kandidat).")]
    public float maxSnapDistance = 0.5f;
    [Tooltip("Rotasi planet mengikuti SnapPoint.")]
    public bool orientToSnapPoint = true;

    // ===== Orbit movement (server-authoritative) =====
    [Header("Orbit Motion")]
    [Tooltip("Aktifkan supaya planet mengelilingi orbit setelah tersnap.")]
    public bool orbitWhenSnapped = true;
    [Tooltip("Kecepatan mengelilingi orbit (derajat/detik).")]
    public float orbitSpeedDeg = 20f;
    [Tooltip("Rotasi diri (spin) derajat/detik.")]
    public float selfSpinDeg = 50f;
    [Tooltip("Kemiringan bidang orbit (derajat). 0 = datar.")]
    public float orbitPlaneTiltDeg = 0f;

    [HideInInspector] public OrbitSlot currentSlot;
    float _orbitAngleDeg;
    float _orbitRadius;
    bool _isGrabbed;

    // Kandidat index slot dari trigger child
    private readonly HashSet<int> _candidateOrbitIndices = new();

    Rigidbody _rb;

    void Awake()
    {
        if (!manager) manager = FindObjectOfType<SolarGameManager>();
        _rb = GetComponent<Rigidbody>();
        if (_rb) { _rb.useGravity = false; _rb.isKinematic = false; }
    }

    void Update()
    {
        // Klien non-server/non-owner: smooth ke nilai network
        if (!IsServer)
        {
            transform.position = Vector3.Lerp(transform.position, NetPos.Value, 0.35f);
            transform.rotation = Quaternion.Slerp(transform.rotation, NetRot.Value, 0.35f);
        }
    }

    void LateUpdate()
    {
        if (!IsServer) return;                 // hanya server yang menggerakkan
        if (!orbitWhenSnapped || currentSlot == null || _isGrabbed) return;

        // advance sudut orbit
        _orbitAngleDeg += orbitSpeedDeg * Time.deltaTime;

        // offset pada bidang XZ
        float rad = _orbitAngleDeg * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _orbitRadius;

        // penerapan tilt (opsional)
        if (Mathf.Abs(orbitPlaneTiltDeg) > 0.001f)
            offset = Quaternion.Euler(orbitPlaneTiltDeg, 0f, 0f) * offset;

        // pusat orbit = posisi root OrbitSlot
        Vector3 center = currentSlot.transform.position;
        // ikuti yaw orbit parent (kalau orbits diputar)
        Quaternion orbitYaw = Quaternion.Euler(0f, currentSlot.transform.eulerAngles.y, 0f);
        Vector3 worldPos = center + (orbitYaw * offset);

        // spin diri
        Quaternion worldRot = transform.rotation * Quaternion.Euler(0f, selfSpinDeg * Time.deltaTime, 0f);

        transform.SetPositionAndRotation(worldPos, worldRot);
        NetPos.Value = worldPos;
        NetRot.Value = worldRot;
    }

    // ====== Dipanggil GameManager saat reset ronde ======
    public void ResetServer(Vector3 worldPos)
    {
        if (!IsServer) return;
        currentSlot = null;
        _candidateOrbitIndices.Clear();
        _isGrabbed = false;

        transform.SetPositionAndRotation(worldPos, Quaternion.identity);
        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
        CurrentOrbitIndex.Value = 0;

        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
    }

    public void SetOrbitIndex(int idx)
    {
        if (!IsServer) return;
        CurrentOrbitIndex.Value = idx;
    }

    // ====== Dipanggil dari sistem input (XR/HVR) ======
    public void OnGrabbedByClient()
    {
        _isGrabbed = true;
        if (!IsOwner) RequestOwnershipServerRpc(NetworkManager.LocalClientId);
    }

    public void OnReleasedByClient()
    {
        _isGrabbed = false;
        // Snap saat rilis: pilih kandidat/nearest di server
        TrySnapToCandidateOrNearestServerRpc(transform.position);
    }

    // ====== Server RPCs ======
    [ServerRpc(RequireOwnership = false)]
    void RequestOwnershipServerRpc(ulong clientId)
    {
        if (IsSpawned) NetworkObject.ChangeOwnership(clientId);
    }

    // Kandidat dari trigger child (OrbitSlot forwarder)
    [ServerRpc(RequireOwnership = false)]
    public void RegisterCandidateServerRpc(int orbitIndex, bool add)
    {
        RegisterCandidate(orbitIndex, add);
    }

    public void RegisterCandidate(int orbitIndex, bool add)
    {
        if (!IsServer) return;
        if (add) _candidateOrbitIndices.Add(orbitIndex);
        else _candidateOrbitIndices.Remove(orbitIndex);
    }

    // Dipanggil OrbitSlot saat snap-on-enter (otomatis saat masuk trigger)
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToSpecificSlotServerRpc(int slotIndex)
    {
        if (!manager || manager.slots == null) return;
        var slot = manager.slots.FirstOrDefault(s => s && s.Index == slotIndex);
        if (!slot || !slot.SnapPoint) { SyncTransformOnly(); return; }

        // tempatkan
        transform.position = slot.SnapPoint.position;
        if (orientToSnapPoint) transform.rotation = slot.SnapPoint.rotation;

        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
        CurrentOrbitIndex.Value = slot.Index;

        // mulai orbit
        StartOrbitAround(slot);

        // efek orbit (opsional)
        slot.BlinkFeedback();

        // pastikan server own
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        _candidateOrbitIndices.Clear();
    }

    // Snap saat rilis: pilih kandidat terdekat; jika tak ada, nearest global dengan batas jarak
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToCandidateOrNearestServerRpc(Vector3 worldPos)
    {
        if (!manager || manager.slots == null || manager.slots.Length == 0)
        { SyncTransformOnly(); return; }

        OrbitSlot target = null; float best = float.MaxValue;

        // 1) pilih dari kandidat trigger
        if (_candidateOrbitIndices.Count > 0)
        {
            foreach (int idx in _candidateOrbitIndices)
            {
                var s = manager.slots.FirstOrDefault(o => o && o.Index == idx);
                if (s == null || !s.SnapPoint) continue;
                float d = (s.SnapPoint.position - worldPos).sqrMagnitude;
                if (d < best) { best = d; target = s; }
            }
        }

        // 2) fallback: nearest global (dengan batas jarak)
        if (target == null)
        {
            foreach (var s in manager.slots)
            {
                if (!s || !s.SnapPoint) continue;
                float d = (s.SnapPoint.position - worldPos).sqrMagnitude;
                if (d < best) { best = d; target = s; }
            }
            if (target == null || (target.SnapPoint.position - worldPos).sqrMagnitude > maxSnapDistance * maxSnapDistance)
            { SyncTransformOnly(); return; }
        }

        // SNAP
        transform.position = target.SnapPoint.position;
        if (orientToSnapPoint) transform.rotation = target.SnapPoint.rotation;

        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
        CurrentOrbitIndex.Value = target.Index;

        // mulai orbit
        StartOrbitAround(target);
        target.BlinkFeedback();

        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        _candidateOrbitIndices.Clear();
    }

    void SyncTransformOnly()
    {
        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
    }

    // ===== Orbit helpers =====
    /// <summary> Panggil setelah planet disnap ke slot. </summary>
    public void StartOrbitAround(OrbitSlot slot)
    {
        currentSlot = slot;
        Vector3 center = slot.transform.position;
        _orbitRadius = Vector3.Distance(center, transform.position);

        // sudut awal dari posisi sekarang relatif ke pusat (di plane XZ)
        Vector3 local = (transform.position - center);
        _orbitAngleDeg = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;

        if (IsServer) CurrentOrbitIndex.Value = slot.Index;
    }

    public void StopOrbit()
    {
        currentSlot = null;
    }
}
