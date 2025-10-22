using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

[DisallowMultipleComponent]
public class Planet : NetworkBehaviour
{
    [Header("Info")]
    public string PlanetName = "Planet";
    [Tooltip("Urutan benar dari Matahari (1..8)")]
    public int IdUrutanBenar = 1;

    [Header("Networked State")]
    public NetworkVariable<int> CurrentOrbitIndex =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [SerializeField]
    private NetworkVariable<Vector3> NetPos =
        new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [SerializeField]
    private NetworkVariable<Quaternion> NetRot =
        new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Accessor (read-only) bila diperlukan di luar
    public Vector3 NetPosValue => NetPos.Value;
    public Quaternion NetRotValue => NetRot.Value;

    [Header("Refs")]
    public SolarGameManager manager;

    [Header("Snap Settings")]
    [Tooltip("Maksimal jarak untuk fallback snap ke orbit terdekat (kalau tidak ada kandidat).")]
    public float maxSnapDistance = 0.5f;
    [Tooltip("Rotasi planet mengikuti SnapPoint.")]
    public bool orientToSnapPoint = true;

    [Header("Orbit Motion")]
    [Tooltip("Aktifkan supaya planet mengelilingi orbit setelah tersnap.")]
    public bool orbitWhenSnapped = true;
    [Tooltip("Kecepatan mengelilingi orbit (derajat/detik).")]
    public float orbitSpeedDeg = 20f;
    [Tooltip("Rotasi diri (spin) derajat/detik.")]
    public float selfSpinDeg = 50f;
    [Tooltip("Kemiringan bidang orbit (derajat). 0 = datar.")]
    public float orbitPlaneTiltDeg = 0f;

    // Runtime
    [HideInInspector] public OrbitSlot currentSlot;
    float _orbitAngleDeg;
    float _orbitRadius;
    bool _isGrabbed;                 // status server tentang sedang dipegang

    // Kandidat index slot dari trigger child
    readonly HashSet<int> _candidateOrbitIndices = new();

    Rigidbody _rb;

    // ---------- Life Cycle ----------
    void Awake()
    {
        if (!manager) manager = FindObjectOfType<SolarGameManager>();
        _rb = GetComponent<Rigidbody>();
        if (_rb)
        {
            _rb.useGravity = false;
            _rb.isKinematic = false; // server yang menggerakkan; client hanya lerp visual
        }
    }

    public override void OnNetworkSpawn()
    {
        // Sinkron state transform saat client join
        if (!IsServer)
            transform.SetPositionAndRotation(NetPos.Value, NetRot.Value);
    }

    void Update()
    {
        // Klien: smooth ke nilai network (server yang menulis)
        if (!IsServer)
        {
            transform.position = Vector3.Lerp(transform.position, NetPos.Value, 0.35f);
            transform.rotation = Quaternion.Slerp(transform.rotation, NetRot.Value, 0.35f);
        }
    }

    void LateUpdate()
    {
        if (!IsServer) return;                             // hanya server yang menggerakkan
        if (!orbitWhenSnapped || currentSlot == null) return;
        if (_isGrabbed) return;

        _orbitAngleDeg += orbitSpeedDeg * Time.deltaTime;

        // offset pada bidang XZ
        float rad = _orbitAngleDeg * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _orbitRadius;

        // tilt opsional
        if (Mathf.Abs(orbitPlaneTiltDeg) > 0.001f)
            offset = Quaternion.Euler(orbitPlaneTiltDeg, 0f, 0f) * offset;

        // pusat orbit & yaw parent
        Vector3 center = currentSlot.transform.position;
        Quaternion orbitYaw = Quaternion.Euler(0f, currentSlot.transform.eulerAngles.y, 0f);
        Vector3 worldPos = center + (orbitYaw * offset);

        // spin diri
        Quaternion worldRot = transform.rotation * Quaternion.Euler(0f, selfSpinDeg * Time.deltaTime, 0f);

        // apply & broadcast
        transform.SetPositionAndRotation(worldPos, worldRot);
        NetPos.Value = worldPos;
        NetRot.Value = worldRot;
    }

    // ---------- Server-side Reset ----------
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

        // pastikan server own (authority penuh di server)
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
    }

    public void SetOrbitIndex(int idx)
    {
        if (!IsServer) return;
        CurrentOrbitIndex.Value = idx;
    }

    // ---------- Hooks dari sistem input (XR/HVR) ----------
    public void OnGrabbedByClient()
    {
        // Client memberi tahu server bahwa sedang dipegang
        SetGrabStateServerRpc(true);
        // TIDAK memindahkan ownership ke client (server-authoritative penuh)
    }

    public void OnReleasedByClient()
    {
        // Client memberi tahu server bahwa sudah dilepas
        SetGrabStateServerRpc(false);
        // Snap saat rilis diputuskan server
        TrySnapToCandidateOrNearestServerRpc(transform.position);
    }

    // ---------- Server RPCs ----------
    [ServerRpc(RequireOwnership = false)]
    void SetGrabStateServerRpc(bool grabbed)
    {
        _isGrabbed = grabbed;
        if (_rb) _rb.isKinematic = grabbed; // saat dipegang, matikan physics server
        if (grabbed) StopOrbit();
    }

    // Dipanggil trigger: registrasi kandidat index orbit (add/remove)
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

    // Snap langsung ke slot tertentu (dipanggil OrbitSlot saat enter trigger, jika diinginkan)
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToSpecificSlotServerRpc(int slotIndex)
    {
        if (!manager || manager.slots == null) { SyncTransformOnly(); return; }
        var slot = manager.slots.FirstOrDefault(s => s && s.Index == slotIndex);
        if (!slot || !slot.SnapPoint) { SyncTransformOnly(); return; }

        transform.position = slot.SnapPoint.position;
        if (orientToSnapPoint) transform.rotation = slot.SnapPoint.rotation;

        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
        CurrentOrbitIndex.Value = slot.Index;

        StartOrbitAround(slot);
        try { slot.BlinkFeedback(); } catch { /* opsional */ }

        // pastikan server own
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        _candidateOrbitIndices.Clear();
    }

    // Snap saat rilis: pilih dari kandidat / nearest global dengan batas jarak
    [ServerRpc(RequireOwnership = false)]
    public void TrySnapToCandidateOrNearestServerRpc(Vector3 worldPos)
    {
        if (!manager || manager.slots == null || manager.slots.Length == 0)
        { SyncTransformOnly(); return; }

        OrbitSlot target = null; float best = float.MaxValue;

        // 1) kandidat dari trigger
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

        // 2) fallback: nearest global (cek jarak maksimum)
        if (target == null)
        {
            foreach (var s in manager.slots)
            {
                if (!s || !s.SnapPoint) continue;
                float d = (s.SnapPoint.position - worldPos).sqrMagnitude;
                if (d < best) { best = d; target = s; }
            }

            if (target == null ||
                (target.SnapPoint.position - worldPos).sqrMagnitude > maxSnapDistance * maxSnapDistance)
            {
                SyncTransformOnly();
                return;
            }
        }

        // Apply snap
        transform.position = target.SnapPoint.position;
        if (orientToSnapPoint) transform.rotation = target.SnapPoint.rotation;

        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
        CurrentOrbitIndex.Value = target.Index;

        StartOrbitAround(target);
        try { target.BlinkFeedback(); } catch { /* opsional */ }

        // kembalikan ownership ke server (jaga konsistensi)
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        _candidateOrbitIndices.Clear();
    }

    void SyncTransformOnly()
    {
        if (!IsServer) return; // penting: hanya server yang boleh menulis
        NetPos.Value = transform.position;
        NetRot.Value = transform.rotation;
    }

    // ---------- Orbit helpers ----------
    /// <summary>Panggil setelah planet disnap ke slot.</summary>
    public void StartOrbitAround(OrbitSlot slot)
    {
        currentSlot = slot;

        Vector3 center = slot.transform.position;
        _orbitRadius = Vector3.Distance(center, transform.position);

        // sudut awal dari posisi sekarang relatif ke pusat (plane XZ)
        Vector3 local = (transform.position - center);
        _orbitAngleDeg = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;

        if (IsServer) CurrentOrbitIndex.Value = slot.Index;
    }

    public void StopOrbit()
    {
        currentSlot = null;
    }
    [ServerRpc(RequireOwnership = false)]
    public void SetTransformServerRpc(Vector3 pos, Quaternion rot)
    {
        if (!IsServer) return;
        transform.SetPositionAndRotation(pos, rot);
        NetPos.Value = pos;
        NetRot.Value = rot;
    }
    public void ServerSetTransform(Vector3 pos, Quaternion rot)
    {
        if (!IsServer) return;
        transform.SetPositionAndRotation(pos, rot);
        NetPos.Value = pos;
        NetRot.Value = rot;
    }
}
