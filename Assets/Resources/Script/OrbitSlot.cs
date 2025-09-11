using UnityEngine;
using Unity.Netcode;

public class OrbitSlot : NetworkBehaviour
{
    [Range(1, 32)] public int Index = 1;
    public Transform SnapPoint;

    [Header("Snap/Visual")]
    public Renderer ringRenderer;
    public Color highlightColor = new(1f, 0.85f, 0.1f, 1f);
    public Color correctColor = new(0.2f, 0.95f, 0.2f, 1f);
    public Color wrongColor = new(0.95f, 0.25f, 0.25f, 1f);

    // --- state visual
    Material[] _mats;
    Color[] _defaults;
    bool _resultLocked; // <- jika true, warna tidak diubah oleh trigger

    void Awake()
    {
        if (!SnapPoint)
        {
            var sp = transform.Find("SnapPoint");
            if (sp) SnapPoint = sp;
        }
        CacheDefaults();
    }

    void CacheDefaults()
    {
        if (!ringRenderer) return;
        _mats = ringRenderer.materials; // instance materials (bukan shared)
        _defaults = new Color[_mats.Length];
        for (int i = 0; i < _mats.Length; i++)
            _defaults[i] = GetMatColor(_mats[i], Color.white);
    }

    // === dipanggil forwarder dari child collider ===
    public void OnChildTriggerEnter(Collider other)
    {
        if (_resultLocked) return;     // <- JANGAN ubah kalau sudah hasil
        var p = other.GetComponentInParent<Planet>();
        if (!p) return;

        SetRingColor(highlightColor);

        if (IsServer) p.RegisterCandidate(Index, true);
        else p.RegisterCandidateServerRpc(Index, true);
    }

    public void OnChildTriggerExit(Collider other)
    {
        if (_resultLocked) return;     // <- JANGAN ubah kalau sudah hasil
        var p = other.GetComponentInParent<Planet>();
        if (!p) return;

        RestoreDefaultColor();

        if (IsServer) p.RegisterCandidate(Index, false);
        else p.RegisterCandidateServerRpc(Index, false);
    }

    // === dipanggil GameManager saat tombol "Cek" ===
    public void ApplyResultColor(bool isCorrect)
    {
        _resultLocked = true; // <- kunci supaya tidak ditimpa trigger
        SetRingColor(isCorrect ? correctColor : wrongColor);
    }

    // === dipanggil saat reset ronde ===
    public void ClearResultLock()
    {
        _resultLocked = false;
        RestoreDefaultColor();
    }

    public void RestoreDefaultColor()
    {
        if (!ringRenderer || _mats == null || _defaults == null) return;
        for (int i = 0; i < _mats.Length; i++)
            SetMatColor(_mats[i], _defaults[i]);
    }

    void SetRingColor(Color c)
    {
        if (!ringRenderer) return;
        if (_mats == null || _mats.Length == 0) CacheDefaults();
        for (int i = 0; i < _mats.Length; i++)
            SetMatColor(_mats[i], c);
    }

    // --- dukung URP Lit (_BaseColor) & Standard (_Color)
    static Color GetMatColor(Material m, Color fallback)
    {
        if (!m) return fallback;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color")) return m.color;
        return fallback;
    }
    static void SetMatColor(Material m, Color c)
    {
        if (!m) return;
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); return; }
        if (m.HasProperty("_Color")) { m.color = c; return; }
    }

    // (opsional) efek blink saat snap
    public void BlinkFeedback() { /* ... */ }
}
