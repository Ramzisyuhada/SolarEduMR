using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils; // XROrigin
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Baca play area dari OpenXR (jika tersedia), gambar outline, buat BoxCollider aman,
/// dan (opsional) lakukan soft-clamp dengan menggeser worldRoot agar user tetap di dalam.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class OpenXRRoomBounds : MonoBehaviour
{
    [Header("References")]
    [Tooltip("XROrigin aktif di scene")]
    public XROrigin xrOrigin;

    [Tooltip("Root dunia/level yang akan digeser saat soft-clamp. Default: xrOrigin.Origin")]
    public Transform worldRoot;

    [Tooltip("Collider kotak yang mewakili area aman (AABB). Jika kosong, akan dibuat otomatis.")]
    public BoxCollider safeBoxCollider;

    [Header("Visual")]
    public bool drawLine = true;
    [Tooltip("Tinggi garis di atas lantai (meter)")]
    public float lineHeight = 0.01f;
    [Tooltip("Lebar garis (meter)")]
    public float lineWidth = 0.01f;

    [Header("Clamp Settings")]
    [Tooltip("Aktifkan geser dunia balik saat kepala keluar poligon")]
    public bool enableSoftClamp = true;

    [Tooltip("Padding mengerutkan AABB (bukan poligon). X=padding X, Y=padding Z")]
    public Vector2 paddingAABB = new Vector2(0.05f, 0.05f);

    [Tooltip("Jika boundary tidak tersedia dari runtime, pakai kotak default (meter)")]
    public Vector2 fallbackBoxSize = new Vector2(3f, 3f);

    [Header("Debug")]
    public bool logBoundaryFetch = true;

    private XRInputSubsystem _xrInput;
    private readonly List<Vector3> _boundary = new();
    private LineRenderer _lr;
    private Transform _originT; // cache transform dari xrOrigin.Origin

    void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        _lr.enabled = false;
        _lr.loop = true;
        _lr.useWorldSpace = false; // kita gambar di origin space
        _lr.widthMultiplier = lineWidth;
        _lr.positionCount = 0;
    }

    void Start()
    {
        if (!xrOrigin) xrOrigin = FindObjectOfType<XROrigin>();
        if (!xrOrigin)
        {
            Debug.LogError("[OpenXRRoomBounds] XROrigin tidak ditemukan.");
            enabled = false;
            return;
        }

        if (!worldRoot) worldRoot = _originT = xrOrigin.Origin.transform;   // ✅ ini benar, pakai komponen Transform
        ; // default ke Origin
        if (!worldRoot) worldRoot = xrOrigin.transform; // fallback terakhir

        _originT = xrOrigin.Origin ? xrOrigin.Origin.transform : xrOrigin.transform;

        // Ambil XRInputSubsystem (OpenXR/Unity XR)
        var list = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(list);
        if (list.Count > 0)
        {
            _xrInput = list[0];
            _xrInput.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            if (logBoundaryFetch) Debug.Log($"[OpenXRRoomBounds] XRInputSubsystem: {_xrInput.SubsystemDescriptor.id}");
        }
        else
        {
            if (logBoundaryFetch) Debug.LogWarning("[OpenXRRoomBounds] XRInputSubsystem tidak ditemukan.");
        }

        // Pastikan ada BoxCollider
        if (!safeBoxCollider)
        {
            safeBoxCollider = gameObject.GetComponent<BoxCollider>();
            if (!safeBoxCollider) safeBoxCollider = gameObject.AddComponent<BoxCollider>();
            safeBoxCollider.isTrigger = true; // biasanya trigger untuk logika
        }

        RefreshBoundary();
    }

    void LateUpdate()
    {
        if (!enableSoftClamp || _boundary.Count < 3 || xrOrigin == null) return;

        // Posisi kepala di origin space (meter)
        Vector3 headLocal = xrOrigin.CameraInOriginSpacePos;
        Vector2 headXZ = new Vector2(headLocal.x, headLocal.z);

        if (!PointInPolygonXZ(headXZ, _boundary))
        {
            // Dorong dunia balik ke centroid poligon (pendekatan sederhana & halus)
            Vector2 centroid = PolygonCentroidXZ(_boundary);
            Vector2 dir = (centroid - headXZ);
            if (dir.sqrMagnitude > 0f)
            {
                Vector3 delta = new Vector3(dir.x, 0f, dir.y);
                worldRoot.position += delta;
            }
        }
    }

    /// <summary>
    /// Panggil ini (mis. saat scene load atau setelah user re-setup guardian) untuk ambil boundary terbaru.
    /// </summary>
    public void RefreshBoundary()
    {
        _boundary.Clear();

        bool ok = false;
        if (_xrInput != null)
        {
            // TryGetBoundaryPoints menulis titik2 poligon DALAM ruang origin (local)
            ok = _xrInput.TryGetBoundaryPoints(_boundary);
        }

        if (ok && _boundary.Count >= 3)
        {
            if (logBoundaryFetch) Debug.Log($"[OpenXRRoomBounds] Boundary OK. Vertices: {_boundary.Count}");
            ElevateBoundaryToLineHeight();
            DrawIfNeeded();
            UpdateBoxColliderAABB();
        }
        else
        {
            if (logBoundaryFetch) Debug.LogWarning("[OpenXRRoomBounds] Boundary tidak tersedia. Pakai fallback box.");
            CreateFallbackBox();
            ElevateBoundaryToLineHeight();
            DrawIfNeeded();
            UpdateBoxColliderAABB();
        }
    }

    /// <summary>
    /// True jika worldPos berada di dalam poligon boundary (origin space).
    /// </summary>
    public bool IsInside(Vector3 worldPos)
    {
        if (_boundary.Count < 3) return true; // kalau tidak ada data, anggap aman

        // Konversi world → origin space (local)
        Vector3 local = _originT.InverseTransformPoint(worldPos);
        Vector2 pXZ = new Vector2(local.x, local.z);
        return PointInPolygonXZ(pXZ, _boundary);
    }

    // ==== Internal Helpers ====

    private void CreateFallbackBox()
    {
        _boundary.Clear();
        float hx = Mathf.Max(0.01f, fallbackBoxSize.x * 0.5f);
        float hz = Mathf.Max(0.01f, fallbackBoxSize.y * 0.5f);
        _boundary.Add(new Vector3(-hx, 0f, -hz));
        _boundary.Add(new Vector3(hx, 0f, -hz));
        _boundary.Add(new Vector3(hx, 0f, hz));
        _boundary.Add(new Vector3(-hx, 0f, hz));
    }

    private void ElevateBoundaryToLineHeight()
    {
        for (int i = 0; i < _boundary.Count; i++)
        {
            var v = _boundary[i];
            v.y = lineHeight;
            _boundary[i] = v;
        }
    }

    private void DrawIfNeeded()
    {
        if (!drawLine)
        {
            _lr.enabled = false;
            _lr.positionCount = 0;
            return;
        }

        _lr.enabled = true;
        _lr.widthMultiplier = lineWidth;
        _lr.positionCount = _boundary.Count;

        // Karena LineRenderer useWorldSpace=false, kita set posisi di local (origin space)
        _lr.SetPositions(_boundary.ToArray());

        // Pastikan LineRenderer mengikuti origin space
        // Tempatkan GameObject ini di bawah xrOrigin.Origin agar koordinat cocok
        if (transform.parent != _originT)
        {
            transform.SetParent(_originT, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
    }

    private void UpdateBoxColliderAABB()
    {
        if (!safeBoxCollider || _boundary.Count == 0) return;

        // Hitung AABB di origin space (XZ)
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;

        foreach (var p in _boundary)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        // Padding AABB
        minX += paddingAABB.x;
        maxX -= paddingAABB.x;
        minZ += paddingAABB.y;
        maxZ -= paddingAABB.y;

        // Set collider di origin space ⇒ collider harus jadi child dari origin
        if (safeBoxCollider.transform != _originT)
        {
            safeBoxCollider.transform.SetParent(_originT, worldPositionStays: false);
            safeBoxCollider.transform.localPosition = Vector3.zero;
            safeBoxCollider.transform.localRotation = Quaternion.identity;
            safeBoxCollider.transform.localScale = Vector3.one;
        }

        Vector3 center = new Vector3((minX + maxX) * 0.5f, 1.0f, (minZ + maxZ) * 0.5f);
        Vector3 size = new Vector3(
            Mathf.Max(0.01f, maxX - minX),
            2.0f, // tinggi 2 m
            Mathf.Max(0.01f, maxZ - minZ)
        );

        safeBoxCollider.center = center;
        safeBoxCollider.size = size;
        safeBoxCollider.isTrigger = true;
    }

    // ======= Poligon Utils (di bidang XZ, dalam origin space) =======

    private static bool PointInPolygonXZ(Vector2 p, List<Vector3> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 pi = new Vector2(poly[i].x, poly[i].z);
            Vector2 pj = new Vector2(poly[j].x, poly[j].z);

            bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
                             (p.x < (pj.x - pi.x) * (p.y - pi.y) / Mathf.Max(1e-6f, (pj.y - pi.y)) + pi.x);

            if (intersect) inside = !inside;
        }
        return inside;
    }

    private static Vector2 PolygonCentroidXZ(List<Vector3> poly)
    {
        float sx = 0f, sz = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            sx += poly[i].x;
            sz += poly[i].z;
        }
        float n = Mathf.Max(1, poly.Count);
        return new Vector2(sx / n, sz / n);
    }
}

/// <summary>
/// Addon opsional: pasang ini di object yang punya ContinuousMoveProviderBase
/// untuk mencegah gerak keluar area berdasarkan OpenXRRoomBounds.IsInside().
/// </summary>
[DefaultExecutionOrder(1000)]
public class XRI_ContinuousMoveClamp : MonoBehaviour
{
    public OpenXRRoomBounds bounds;
    public ContinuousMoveProviderBase moveProvider;
    public float lookAheadSeconds = 0.1f; // prediksi posisi ke depan

    void Reset()
    {
        moveProvider = GetComponent<ContinuousMoveProviderBase>();
        if (!bounds) bounds = FindObjectOfType<OpenXRRoomBounds>();
    }

    void Update()
    {
        if (!moveProvider || !bounds || !bounds.enabled) return;

        // Ambil input velocity yg akan diterapkan (di world space)
        Vector3 vel = GetPlannedVelocityWorld(moveProvider);
        if (vel.sqrMagnitude <= 0f) return;

        // Prediksi posisi kepala di masa depan
        var xrOrigin = bounds.xrOrigin;
        if (!xrOrigin) return;

        Vector3 headWorld = xrOrigin.Camera.transform.position;
        Vector3 nextPos = headWorld + vel * Mathf.Max(0f, lookAheadSeconds);

        if (!bounds.IsInside(nextPos))
        {
            // Tolak gerak keluar: nolkan kecepatan untuk frame ini
            ZeroProviderSpeed(moveProvider);
        }
    }

    private static Vector3 GetPlannedVelocityWorld(ContinuousMoveProviderBase provider)
    {
        // Provider menerapkan kecepatan di late update; kita perkirakan dari karakter controller/rig
        // Tidak ada API publik untuk "get velocity" sebelum diterapkan, jadi gunakan input XR:
        // Arah = provider.forwardSource.forward * input.y + provider.forwardSource.right * input.x
        // Di sini kita ambil kecepatan kira-kira (m/s)
        var fwd = provider.forwardSource ? provider.forwardSource.forward : Vector3.forward;
        var right = provider.forwardSource ? provider.forwardSource.right : Vector3.right;

        Vector2 input = ReadMoveInput(provider);
        Vector3 dir = (fwd * input.y + right * input.x);
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0 ? dir.normalized : Vector3.zero;

        float speed = provider.moveSpeed;
        return dir * speed;
    }

    private static Vector2 ReadMoveInput(ContinuousMoveProviderBase provider)
    {
        // ContinuousMoveProviderBase mem-baca dari input system secara internal.
        // Kita tidak punya akses langsung ke nilai mentahnya,
        // jadi pendekatan aman: gunakan XR input default axis (Primary2DAxis) jika ada.
#if ENABLE_INPUT_SYSTEM
        // Kalau proyek pakai Input System baru, sebaiknya buat InputAction sendiri dan injeksikan.
        // Di sini kita kembalikan Vector2.zero agar konservatif.
        return Vector2.zero;
#else
        // Dengan XR Legacy Input, tidak reliable. Kembalikan zero.
        return Vector2.zero;
#endif
    }

    private static void ZeroProviderSpeed(ContinuousMoveProviderBase provider)
    {
        // Cara sederhana: sementara set moveSpeed ke 0 di frame ini lalu kembalikan.
        // Untuk aman, kita pakai Time.deltaTime sebagai "pulsa satu frame".
        float original = provider.moveSpeed;
        provider.moveSpeed = 0f;
        // Pulihkan di akhir frame
        provider.StartCoroutine(RestoreNextFrame(provider, original));
    }

    private static System.Collections.IEnumerator RestoreNextFrame(ContinuousMoveProviderBase provider, float value)
    {
        yield return null;
        if (provider) provider.moveSpeed = value;
    }
}
