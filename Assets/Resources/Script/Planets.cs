using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public class Planet : NetworkBehaviour
{
    // ===== Info & Network =====
    [Header("Info")]
    public string PlanetName = "Planet";
    [Tooltip("Urutan benar dari Matahari (1..N)")]
    public int IdUrutanBenar = 1;

    [Header("Networked State")]
    public NetworkVariable<int> CurrentOrbitIndex =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Vector3> NetPos =
        new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Quaternion> NetRot =
        new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Refs")]
    public SolarGameManager manager;

    // ===== Snap =====
    [Header("Snap Settings")]
    [Tooltip("Batas jarak fallback snap ke slot terdekat ketika tidak ada kandidat.")]
    public float maxSnapDistance = 0.35f;
    public bool orientToSnapPoint = true;

    // ===== Orbit Motion =====
    [Header("Orbit Motion")]
    public bool orbitWhenSnapped = true;
    public float orbitSpeedDeg = 20f;   // derajat per detik
    public float selfSpinDeg = 50f;     // derajat per detik
    public float orbitPlaneTiltDeg = 0f;

    // runtime
    Rigidbody _rb;
    bool _isGrabbed;
    OrbitSlot _currentSlot;
    float _orbitAngleDeg;
    float _orbitRadius;

    // kandidat slot dari trigger
    readonly HashSet<int> _candidateIndices = new();

    void Awake()
    {
        if (!manager) manager = FindObjectOfType<SolarGameManager>();
        _rb = GetComponent<Rigidbody>();
        if (_rb) { _rb.useGravity = false; _rb.isKinematic = false; }
    }

    void Update()
    {
        // klien non-server: lerp ke nilai network
        if (!IsServer)
        {
            transform.position = Vector3.Lerp(transform.position, NetPos.Value, 0.35f);
            transform.rotation = Quaternion.Slerp(transform.rotation, NetRot.Value, 0.35f);
        }
    }

    void LateUpdate()
    {
        if (!IsServer) return;
        if (!orbitWhenSnapped || _currentSlot == null || _isGrabbed) return;

        _orbitAngleDeg += orbitSpeedDeg * Time.deltaTime;

        // pos di bidang XZ
        float rad = _orbitAngleDeg * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _orbitRadius;

        if (Mathf.Abs(orbitPlaneTiltDeg) > 0.001f)
            offset = Quaternion.Euler(orbitPlaneTiltDeg, 0f, 0f) * offset;

        Vector3 center = _currentSlot.transform.position;
        Quaternion yaw = Quaternion.Euler(0f, _currentSlot.transform.eulerAngles.y, 0f);
        Vector3 worldPos = center + (yaw * offset);

        Quaternion worldRot = transform.rotation * Quaternion.Euler(0f, selfSpinDeg * Time.deltaTime, 0f);

        transform.SetPositionAndRotation(worldPos, worldRot);
        NetPos.Value = worldPos;
        NetRot.Value = worldRot;
    }

    // ===== Public hooks untuk sistem input =====
    public void OnGrabbedByClient()
    {
        _isGrabbed = true;
        if (!IsOwner) RequestOwnershipServerRpc(NetworkManager.LocalClientId);
        // saat dipegang, hentikan orbit sementara
    }

    public void OnReleasedByClient()
    {
        _isGrabbed = false;
        // minta server memilih slot terbaik (dari kandidat/terdekat)
        TrySnapToCandidateOrNearestServerRpc(transform.position);
    }

    // ===== Server-side reset (dipanggil GameManager di awal ronde) =====
    public void ResetServer(Vector3 worldPos)
    {
        if (!IsServer) return;

        _candidateIndices.Clear();
        _currentSlot = null;
        _isGrabbed = false;

        transform.SetPositionAndRotation(worldPos, Quaternion.identity);
        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
        CurrentOrbitIndex.Value = 0;

        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
    }

    // ======= KANDIDAT (dipanggil OrbitSlot via trigger) =======
    [ServerRpc(RequireOwnership = false)]
    public void RegisterCandidateServerRpc(int orbitIndex, bool add)
    {
        RegisterCandidate(orbitIndex, add);
    }

    public void RegisterCandidate(int orbitIndex, bool add)
    {
        if (!IsServer) return;
        if (add) _candidateIndices.Add(orbitIndex);
        else _candidateIndices.Remove(orbitIndex);
    }

    // ======= SNAP ON ENTER (opsional) =======
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToSpecificSlotServerRpc(int slotIndex)
    {
        if (!manager || manager.slots == null) return;
        var slot = manager.slots.FirstOrDefault(s => s && s.Index == slotIndex);
        if (!slot || !slot.SnapPoint) return;

        ApplySnapToSlot(slot);
    }

    // ======= SNAP SAAT RILIS (pakai kandidat atau terdekat) =======
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToCandidateOrNearestServerRpc(Vector3 worldPos)
    {
        if (!manager || manager.slots == null || manager.slots.Length == 0)
        {
            SyncTransformOnly();
            return;
        }

        OrbitSlot best = null;
        float bestDist = float.MaxValue;

        // 1) jika ada kandidat
        if (_candidateIndices.Count > 0)
        {
            foreach (var idx in _candidateIndices)
            {
                var s = manager.slots.FirstOrDefault(o => o && o.Index == idx);
                if (s == null || !s.SnapPoint) continue;
                float d = (s.SnapPoint.position - worldPos).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = s; }
            }
        }
        // 2) fallback: global nearest
        if (best == null)
        {
            foreach (var s in manager.slots)
            {
                if (!s || !s.SnapPoint) continue;
                float d = (s.SnapPoint.position - worldPos).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            if (best == null || (best.SnapPoint.position - worldPos).sqrMagnitude >
                maxSnapDistance * maxSnapDistance)
            {
                SyncTransformOnly();
                return;
            }
        }

        ApplySnapToSlot(best);
        _candidateIndices.Clear();
    }

    // ======= CORE: Terapkan snap ke slot =======
    void ApplySnapToSlot(OrbitSlot slot)
    {
        if (!IsServer || slot == null || slot.SnapPoint == null) return;

        transform.position = slot.SnapPoint.position;
        if (orientToSnapPoint) transform.rotation = slot.SnapPoint.rotation;

        // WAJIB: set index orbit saat ini
        CurrentOrbitIndex.Value = slot.Index;

        // mulai orbit keliling slot
        StartOrbitAround(slot);

        // sinkron ke klien
        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;

        // (opsional) efek visual slot
        slot.BlinkFeedback();

        // ownership balik ke server agar state konsisten
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        Debug.Log($"[Snap] {PlanetName} tersnap ke slot {slot.Index}");
    }

    // ===== Orbit helpers =====
    public void StartOrbitAround(OrbitSlot slot)
    {
        _currentSlot = slot;

        Vector3 center = slot.transform.position;
        _orbitRadius = Vector3.Distance(center, transform.position);

        Vector3 local = transform.position - center;
        _orbitAngleDeg = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
    }

    public void StopOrbit() { _currentSlot = null; }

    void SyncTransformOnly()
    {
        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
    }

    // ===== Ownership helper =====
    [ServerRpc(RequireOwnership = false)]
    void RequestOwnershipServerRpc(ulong clientId)
    {
        if (IsSpawned) NetworkObject.ChangeOwnership(clientId);
    }
}
