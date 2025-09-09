using UnityEngine;
using Unity.Netcode;

[DisallowMultipleComponent]
public class OrbitSlot : NetworkBehaviour
{
    [Range(1, 32)] public int Index = 1;
    public Transform SnapPoint;

    [Header("Snap via Release (gunakan trigger child untuk kandidat)")]
    public bool snapOnEnter = false;     // PASTIKAN false untuk “snap saat dilepas”
    public bool orientToSnapPoint = true;

    [Header("Feedback (opsional)")]
    public Color blinkColor = new(1f, .6f, .1f, 1f);
    public float blinkTime = .25f;

    Material[] _mats; Color[] _origCols;
    [Header("Highlight Orbit")]
    public Renderer ringRenderer;          // drag ring visual (LineRenderer/Mesh) ke sini
    public Color highlightColor = Color.yellow;
    private Color[] defaultColors;         // warna asli material orbit

    void Awake()
    {
        if (!SnapPoint)
        {
            var sp = transform.Find("SnapPoint");
            if (sp) SnapPoint = sp;
        }
        if (ringRenderer)
        {
            var mats = ringRenderer.materials;
            defaultColors = new Color[mats.Length];
            for (int i = 0; i < mats.Length; i++)
                if (mats[i].HasProperty("_Color"))
                    defaultColors[i] = mats[i].color;
        }
    }
    void SetRingColor(Color c)
    {
        if (!ringRenderer) return;
        var mats = ringRenderer.materials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i].HasProperty("_Color"))
                mats[i].color = c;
    }

    void RestoreDefaultColor()
    {
        if (!ringRenderer || defaultColors == null) return;
        var mats = ringRenderer.materials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i].HasProperty("_Color"))
                mats[i].color = defaultColors[i];
    }


    // ===== dipanggil dari child via forwarder =====
    public void OnChildTriggerEnter(Collider other)
    {
        Debug.Log("Hello world");
        var planet = other.GetComponentInParent<Planet>();
        if (!planet) return;

        // Ganti warna saat planet masuk trigger
        SetRingColor(highlightColor);

        if (IsServer) planet.RegisterCandidate(Index, true);
        else planet.RegisterCandidateServerRpc(Index, true);
    }

    public void OnChildTriggerExit(Collider other)
    {
        var planet = other.GetComponentInParent<Planet>();
        if (!planet) return;

        // Balik ke warna semula saat planet keluar trigger
        RestoreDefaultColor();

        if (IsServer) planet.RegisterCandidate(Index, false);
        else planet.RegisterCandidateServerRpc(Index, false);
    }


    // ===== opsional visual =====
    public void ClearHighlight()
    {
        if (_mats == null) return;
        for (int i = 0; i < _mats.Length; i++)
            if (_mats[i].HasProperty("_Color")) _mats[i].color = _origCols[i];
    }

    public void BlinkFeedback()
    {
        if (!isActiveAndEnabled || _mats == null) return;
        StopAllCoroutines();
        StartCoroutine(BlinkCo());
    }

    System.Collections.IEnumerator BlinkCo()
    {
        float t = 0f;
        while (t < blinkTime)
        {
            t += Time.deltaTime;
            float f = Mathf.PingPong(t * 8f, 1f);
            for (int i = 0; i < _mats.Length; i++)
                if (_mats[i].HasProperty("_Color"))
                    _mats[i].color = Color.Lerp(_origCols[i], blinkColor, f);
            yield return null;
        }
        ClearHighlight();
    }
}
