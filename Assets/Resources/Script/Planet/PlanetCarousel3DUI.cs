using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class PlanetCarousel3DUI_Net : NetworkBehaviour
{
    [Header("Data & Prefab")]
    public PlanetData[] dataList;
    public GameObject[] planetPrefabs;

    [Header("Anchor & Layout 3D")]
    public Transform anchor;
    public Vector3 centerLocalPos = Vector3.zero;
    public Vector3 sideOffset = new Vector3(0.6f, 0f, 0f);
    public float depthOffset = 0f;

    [Header("Skala")]
    public Vector3 centerScale = Vector3.one * 0.28f;
    public Vector3 sideScale = Vector3.one * 0.18f;

    [Header("Animasi LeanTween")]
    public float moveDuration = 0.35f;
    public float scaleDuration = 0.30f;
    public LeanTweenType moveEase = LeanTweenType.easeInOutCubic;
    public LeanTweenType scaleEase = LeanTweenType.easeOutBack;

    [Header("Auto Spin (opsional)")]
    public bool addAutoSpinIfMissing = true;
    public float selfSpinDeg = 25f;

    [Header("UI Panels (semua ikut sinkron)")]
    public PlanetInfoUI[] infoUIPanels;       // drag manual atau auto cari
    public bool autoFindInfoPanels = true;    // cari otomatis saat OnNetworkSpawn

    // Event global opsional
    public static System.Action<PlanetData> OnPlanetInfoChanged;

    // Runtime
    readonly List<GameObject> _instances = new();
    bool _animating;

    // Network index planet aktif
    private NetworkVariable<int> currentIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    void Awake()
    {
        if (!anchor) anchor = transform;
        int n = Mathf.Max(dataList != null ? dataList.Length : 0,
                          planetPrefabs != null ? planetPrefabs.Length : 0);
        for (int i = 0; i < n; i++) _instances.Add(null);
    }

    public override void OnNetworkSpawn()
    {
        // Cari otomatis semua panel jika autoFind aktif
        if (autoFindInfoPanels && (infoUIPanels == null || infoUIPanels.Length == 0))
            infoUIPanels = FindObjectsOfType<PlanetInfoUI>(true);

        // Set listener: tiap index berubah → update planet & UI semua pemain
        currentIndex.OnValueChanged += (oldVal, newVal) =>
        {
            SetIndex(newVal, immediate: false);
            UpdateInfoUI(newVal);
        };

        // Set awal
        if (IsServer) currentIndex.Value = 0;
    }

    // === Tombol Next/Prev dipanggil dari UI ===
    public void OnPrev() { if (!_animating) RequestShiftServerRpc(-1); }
    public void OnNext() { if (!_animating) RequestShiftServerRpc(+1); }

    [ServerRpc(RequireOwnership = false)]
    void RequestShiftServerRpc(int dir)
    {
        if (_instances.Count == 0) return;
        int n = _instances.Count;
        int newIndex = ((currentIndex.Value + dir) % n + n) % n;
        currentIndex.Value = newIndex; // memicu semua client update
    }

    // === Update 3D Planet ===
    void SetIndex(int newIndex, bool immediate)
    {
        if (_instances.Count == 0) return;
        _animating = !immediate;

        int left = ((newIndex - 1) % _instances.Count + _instances.Count) % _instances.Count;
        int right = ((newIndex + 1) % _instances.Count + _instances.Count) % _instances.Count;

        var goC = EnsureInstance(newIndex);
        var goL = EnsureInstance(left);
        var goR = EnsureInstance(right);

        // Hanya aktifkan 3 planet di sekitar pusat
        for (int i = 0; i < _instances.Count; i++)
        {
            if (!_instances[i]) continue;
            _instances[i].SetActive(i == newIndex || i == left || i == right);
        }

        var posC = centerLocalPos + new Vector3(0, 0, depthOffset);
        var posL = centerLocalPos + new Vector3(-sideOffset.x, 0, depthOffset);
        var posR = centerLocalPos + new Vector3(+sideOffset.x, 0, depthOffset);

        Setup(goC); Setup(goL); Setup(goR);

        if (immediate)
        {
            SetLocal(goC, posC, centerScale);
            SetLocal(goL, posL, sideScale);
            SetLocal(goR, posR, sideScale);
            _animating = false;
            return;
        }

        AnimateMoveScale(goC, posC, centerScale);
        AnimateMoveScale(goL, posL, sideScale);
        AnimateMoveScale(goR, posR, sideScale, () => { _animating = false; });
    }

    // === Update Semua Panel UI ===
    void UpdateInfoUI(int idx)
    {
        if (dataList == null || idx < 0 || idx >= dataList.Length) return;
        var data = dataList[idx];

        // Array panel manual
        if (infoUIPanels != null && infoUIPanels.Length > 0)
        {
            foreach (var panel in infoUIPanels)
                if (panel) panel.Show(data);
        }

        // Broadcast event global (opsional)
        OnPlanetInfoChanged?.Invoke(data);
    }

    // === Util Animasi & Setup ===
    void AnimateMoveScale(GameObject go, Vector3 pos, Vector3 scale, System.Action onComplete = null)
    {
        if (!go) { onComplete?.Invoke(); return; }
        LeanTween.moveLocal(go, pos, moveDuration).setEase(moveEase);
        LeanTween.scale(go, scale, scaleDuration).setEase(scaleEase)
            .setOnComplete(() => onComplete?.Invoke());
    }

    void SetLocal(GameObject go, Vector3 pos, Vector3 scale)
    {
        if (!go) return;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
    }

    void Setup(GameObject go)
    {
        if (!go) return;
        go.transform.SetParent(anchor, false);

        if (addAutoSpinIfMissing && !go.GetComponentInChildren<AutoSpin>())
        {
            var s = go.AddComponent<AutoSpin>();
            s.degPerSec = selfSpinDeg;
        }
    }

    GameObject EnsureInstance(int idx)
    {
        if (idx < 0 || idx >= _instances.Count) return null;
        if (_instances[idx]) return _instances[idx];

        GameObject prefab = (planetPrefabs != null && idx < planetPrefabs.Length) ? planetPrefabs[idx] : null;
        GameObject go;

        if (prefab)
        {
            // Instans langsung dengan parent untuk menghindari SetParent setelahnya
            go = Instantiate(prefab, anchor, false);

            // --- STRIP semua NetworkObject di instans ini ---
            var netObjs = go.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true);
            for (int i = 0; i < netObjs.Length; i++)
            {
#if UNITY_EDITOR
                Object.DestroyImmediate(netObjs[i]); // editor
#else
            Destroy(netObjs[i]);                 // playmode
#endif
            }
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.transform.SetParent(anchor, false);
        }

        go.name = $"Planet3D_{idx + 1}";
        go.transform.localPosition = centerLocalPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = sideScale;
        go.SetActive(false);

        _instances[idx] = go;
        return go;
    }


    class AutoSpin : MonoBehaviour
    {
        public float degPerSec = 25f;
        void Update() => transform.Rotate(0f, degPerSec * Time.deltaTime, 0f, Space.Self);
    }
}
