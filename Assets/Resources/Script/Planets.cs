using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

[DisallowMultipleComponent]
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
    public float maxSnapDistance = 0.6f;
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
        Debug.Log($"[Planet Awake] {name} mgr={(manager ? manager.name : "NULL")}");
    }

    void Start()
    {
        // Saat client join, pastikan posisi awal ikut NetPos / NetRot
        if (!IsServer)
        {
            transform.SetPositionAndRotation(NetPos.Value, NetRot.Value);
        }
    }

    void Update()
    {
        // klien non-server: lerp ke nilai network
        if (!IsServer && !IsOwner)
        {
            transform.position = Vector3.Lerp(transform.position, NetPos.Value, 0.35f);
            transform.rotation = Quaternion.Slerp(transform.rotation, NetRot.Value, 0.35f);
        }
    }

    void LateUpdate()
    {
        if (!IsServer) return;
        if (!orbitWhenSnapped || _currentSlot == null || _svGrabbed) return;

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
    bool _svGrabbed; // hanya dipakai server

    [ServerRpc(RequireOwnership = false)]
    void SetGrabStateServerRpc(bool grabbed)
    {
        _svGrabbed = grabbed;
        if (_rb) _rb.isKinematic = grabbed;
        if (grabbed) StopOrbit();
    }
    // ===== Public hooks untuk sistem input =====
    public void OnGrabbedByClient()
    {
        _isGrabbed = true;                    // lokal (klien ini)
        SetGrabStateServerRpc(true);          // beri tahu server
        if (!IsOwner) RequestOwnershipServerRpc(NetworkManager.LocalClientId);
    }

    public void OnReleasedByClient()
    {
        _isGrabbed = false;                   // lokal
        SetGrabStateServerRpc(false);         // beri tahu server
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

        Debug.Log($"[RESET] {PlanetName} @ {worldPos}");
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
        if (add)
        {
            if (_candidateIndices.Add(orbitIndex))
                Debug.Log($"[CANDIDATE +] {PlanetName} add slot {orbitIndex} (now: {string.Join(",", _candidateIndices)})");
        }
        else
        {
            if (_candidateIndices.Remove(orbitIndex))
                Debug.Log($"[CANDIDATE -] {PlanetName} remove slot {orbitIndex} (now: {string.Join(",", _candidateIndices)})");
        }
    }

    // ======= SNAP ON ENTER (opsional) =======
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToSpecificSlotServerRpc(int slotIndex)
    {
        if (!manager || manager.slots == null) { Debug.LogWarning($"[SNAP SPECIFIC] manager/slots NULL"); return; }
        var slot = manager.slots.FirstOrDefault(s => s && s.Index == slotIndex);
        if (!slot || !slot.SnapPoint) { Debug.LogWarning($"[SNAP SPECIFIC] Slot {slotIndex} tidak valid / SnapPoint null"); return; }

        ApplySnapToSlot(slot);
    }

    // ======= SNAP SAAT RILIS (pakai kandidat atau terdekat) =======
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToCandidateOrNearestServerRpc(Vector3 worldPos)
    {
        if (!manager)
        {
            manager = FindObjectOfType<SolarGameManager>();
            Debug.LogWarning($"[SNAP] manager null, re-find => {(manager ? "OK" : "FAIL")}");
        }

        if (!manager || manager.slots == null || manager.slots.Length == 0)
        {
            Debug.LogWarning($"[SNAP] GAGAL: manager/slots kosong di SERVER. Tidak bisa snap.");
            DumpSlotsOnServer();
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
            Debug.Log($"[SNAP] {PlanetName} pilih dari kandidat => {(best ? best.Index.ToString() : "NONE")} distSqr={bestDist:F4}");
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

            if (best == null)
            {
                Debug.LogWarning("[SNAP] Tidak menemukan slot terdekat sama sekali.");
                SyncTransformOnly();
                return;
            }

            float maxDistSqr = maxSnapDistance * maxSnapDistance;
            if (bestDist > maxDistSqr)
            {
                Debug.LogWarning($"[SNAP] Terdekat adalah slot {best.Index} tapi JAUH (distSqr={bestDist:F3} > maxSqr={maxDistSqr:F3}). Tidak snap.");
                SyncTransformOnly();
                return;
            }

            Debug.Log($"[SNAP] Fallback nearest => slot {best.Index} distSqr={bestDist:F4} (OK <= {maxDistSqr:F4})");
        }

        ApplySnapToSlot(best);
        _candidateIndices.Clear();
    }

    // ======= CORE: Terapkan snap ke slot =======
    void ApplySnapToSlot(OrbitSlot slot)
    {
        if (!IsServer || slot == null || slot.SnapPoint == null)
        {
            Debug.LogWarning($"[APPLY SNAP] Gagal: server={IsServer}, slot={(slot?._GetName() ?? "NULL")}, snap={(slot?.SnapPoint ? "OK" : "NULL")}");
            return;
        }

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
        try { slot.BlinkFeedback(); } catch { /* ignore if not implemented */ }

        // ownership balik ke server agar state konsisten
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        Debug.Log($"[Snap] {PlanetName} tersnap ke slot {slot.Index} @ {transform.position}");
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

    // ===== Debug Utils =====
    void DumpSlotsOnServer()
    {
        if (!IsServer) return;
        var arr = FindObjectsOfType<OrbitSlot>(true);
        Debug.Log($"[SLOTS DUMP][SV] count={arr.Length}  manager.slots={(manager && manager.slots != null ? manager.slots.Length : -1)}");
        foreach (var s in arr.OrderBy(x => x.Index))
        {
            Debug.Log($" - slot {s.Index} snap={(s.SnapPoint ? "OK" : "NULL")} pos={(s.SnapPoint ? s.SnapPoint.position : s.transform.position)}");
        }
    }

    void OnDrawGizmosSelected()
    {
        // bantu visual jarak snap
        Gizmos.color = new Color(0, 1, 0, 0.25f);
        Gizmos.DrawWireSphere(transform.position, maxSnapDistance);
    }
}

// helper kecil untuk debug null-safe
static class _OrbitSlotDbgExt
{
    public static string _GetName(this OrbitSlot s) => s ? s.name : "NULL";
}