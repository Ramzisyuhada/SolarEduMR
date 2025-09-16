using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System;

public class PlanetCarousel3DUI_Net : NetworkBehaviour
{
    [Header("Data & Prefab (jumlah sebaiknya sama)")]
    public PlanetData[] dataList;           // pastikan PlanetData.audioClip diisi
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

    [Header("UI Panels (sinkron semua klien)")]
    public PlanetInfoUI[] infoUIPanels;     // drag manual atau auto find
    public bool autoFindInfoPanels = true;  // cari otomatis saat OnNetworkSpawn

    [Header("Audio (sinkron)")]
    public AudioSource audioSource;         // assign di inspector
    public bool playAudioOnChange = true;   // auto play ketika index berubah
    public bool loopAudio = false;
    [Range(0f, 1f)] public float audioVolume = 1f;
    public bool restartIfSameClip = true;
    [Tooltip("Lead time sync; jadwalkan start sedikit ke depan biar semua sempat ‘baris’.")]
    public double audioSyncLeadSeconds = 0.12;   // 120 ms mulus
    [Tooltip("Headroom minimum supaya tidak start di masa lalu.")]
    public double minStartHeadroom = 0.02;       // 20 ms

    // Event global opsional
    public static Action<PlanetData> OnPlanetInfoChanged;

    // Runtime
    private readonly List<GameObject> _instances = new();
    private bool _animating;

    // Network: index planet aktif
    private NetworkVariable<int> currentIndex = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ===== Helpers jumlah item =====
    private int CountPrefabs => planetPrefabs != null ? planetPrefabs.Length : 0;
    private int CountData => dataList != null ? dataList.Length : 0;
    private int Count => Mathf.Max(CountPrefabs, CountData);

    // ===== Lifecycle =====
    private void Awake()
    {
        if (!anchor) anchor = transform;

        int n = Count;
        for (int i = 0; i < n; i++) _instances.Add(null);

        if (n == 0)
            Debug.LogWarning("[Planet] Tidak ada data/prefab. Carousel idle.");
        else if (CountPrefabs != CountData)
            Debug.LogWarning($"[Planet] Jumlah data ({CountData}) != prefabs ({CountPrefabs}). 3D pakai {Count} item; UI/Audio di-guard.");
    }
    private void OnEnable()
    {
    }

    private void OnDisable()
    {

    }
    public override void OnNetworkSpawn()
    {
        if (autoFindInfoPanels && (infoUIPanels == null || infoUIPanels.Length == 0))
            infoUIPanels = FindObjectsOfType<PlanetInfoUI>(true);

        currentIndex.OnValueChanged += OnIndexChanged_Network;

        // Terapkan state saat join
        int idx = SafeIndex(currentIndex.Value);
        SetIndex(idx, immediate: true);
        UpdateInfoUI(idx);

        // Late-joiner: lokal play agar ada suara; sinkron presisi terjadi di perubahan index berikutnya.
        TryPlayAudioLocal(idx, forceRestart: true);

        if (IsServer && currentIndex.Value != idx)
            currentIndex.Value = idx;
    }

    public override void OnNetworkDespawn()
    {
        currentIndex.OnValueChanged -= OnIndexChanged_Network;
    }

    private void OnDestroy()
    {
        currentIndex.OnValueChanged -= OnIndexChanged_Network;
    }

    private void OnIndexChanged_Network(int oldVal, int newVal)
    {
        int n = Count;
        if (n <= 0) return;

        int newIdx = SafeIndex(newVal);
        SetIndex(newIdx, immediate: false);
        UpdateInfoUI(newIdx);

        // Server kirim sinkron play biar serempak
        if (IsServer && playAudioOnChange)
            BroadcastPlayAudioSynced(newIdx, restartIfSameClip);
    }

    // ===== UI Handlers =====
    public void OnPrev() => SendShift(-1);
    public void OnNext() => SendShift(+1);

    // Opsional tombol audio sinkron:
    public void OnPlayAudio()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        { TryPlayAudioLocal(GetCurrentIndex(), forceRestart: false); return; }

        if (IsServer) BroadcastPlayAudioSynced(GetCurrentIndex(), false);
        else RequestPlayServerRpc(false);
    }
    public void OnPauseAudio()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        { if (audioSource) audioSource.Pause(); return; }

        if (IsServer) PauseAudioClientRpc();
        else RequestPauseServerRpc();
    }
    public void OnStopAudio()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        { if (audioSource) audioSource.Stop(); return; }

        if (IsServer) StopAudioClientRpc();
        else RequestStopServerRpc();
    }

    private void SendShift(int dir)
    {
        if (_animating) return;

        if (!gameObject.activeInHierarchy || !enabled)
        {
            Debug.LogWarning("[Planet] Diabaikan: GameObject non-aktif.");
            return;
        }

        // Offline/editor
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        { ShiftLocal(dir); return; }

        if (!IsSpawned)
        {
            Debug.LogWarning("[Planet] Diabaikan: NetworkObject belum IsSpawned.");
            return;
        }

        if (IsServer) { ShiftServer(dir); return; }

        if (IsClient) RequestShiftServerRpc(dir);
    }

    // ===== RPC untuk shift & kontrol audio =====
    [ServerRpc(RequireOwnership = false)]
    private void RequestShiftServerRpc(int dir) => ShiftServer(dir);

    [ServerRpc(RequireOwnership = false)]
    private void RequestPlayServerRpc(bool forceRestart) => BroadcastPlayAudioSynced(GetCurrentIndex(), forceRestart);

    [ServerRpc(RequireOwnership = false)]
    private void RequestPauseServerRpc() => PauseAudioClientRpc();

    [ServerRpc(RequireOwnership = false)]
    private void RequestStopServerRpc() => StopAudioClientRpc();

    // ===== Server logic =====
    private void ShiftServer(int dir)
    {
        int n = Count;
        if (n <= 0) return;

        int target = WrapIndex(currentIndex.Value + dir, n);
        currentIndex.Value = target; // OnValueChanged → server panggil BroadcastPlayAudioSynced
        if (playAudioOnChange)
            BroadcastPlayAudioSynced(target, restartIfSameClip);
    }

    private void BroadcastPlayAudioSynced(int idx, bool forceRestart)
    {
        double tServerNow = NetworkManager.ServerTime.Time;
        PlayClipClientRpc(idx, tServerNow, forceRestart, audioVolume, loopAudio);
    }

    // ===== ClientRpc sinkron audio =====
    [ClientRpc]
    private void PlayClipClientRpc(int idx, double sentServerTime, bool forceRestart, float volume, bool loop)
    {
        if (!audioSource) { Debug.LogWarning("[Audio][RPC] No AudioSource"); return; }
        if (CountData <= 0 || idx < 0 || idx >= CountData) { audioSource.Stop(); return; }

        var data = dataList[idx];
        var clip = data != null ? data.narration : null;
        if (!clip) { Debug.LogWarning("[Audio][RPC] Clip null"); audioSource.Stop(); return; }

        EnsureAudibleSettingsForTest();
        audioSource.loop = loop;
        audioSource.volume = volume;

        double serverNowAtClient = NetworkManager.Singleton.ServerTime.Time;
        double oneWay = serverNowAtClient - sentServerTime;
        double startDSP = AudioSettings.dspTime + Math.Max(minStartHeadroom, audioSyncLeadSeconds - oneWay);

        // kalau clip sama & sudah play & tidak forceRestart → biarin
        if (!forceRestart && audioSource.clip == clip && audioSource.isPlaying) return;

        audioSource.Stop();
        audioSource.clip = clip;

        if (startDSP <= AudioSettings.dspTime + 0.005)
        {
            audioSource.Play();
            Debug.LogWarning("[Audio][RPC] startDSP ~now → fallback Play()");
            if (!audioSource.isPlaying)
            {
                audioSource.PlayOneShot(clip, audioSource.volume);
                Debug.LogWarning("[Audio][RPC] Fallback PlayOneShot");
            }
            return;
        }

        audioSource.PlayScheduled(startDSP);
        Debug.Log($"[Audio][RPC] PlayScheduled in {(startDSP - AudioSettings.dspTime) * 1000f:0}ms, clip={clip.name}, vol={audioSource.volume}");
    }

    [ClientRpc] private void PauseAudioClientRpc() { if (audioSource) audioSource.Pause(); }
    [ClientRpc] private void StopAudioClientRpc() { if (audioSource) audioSource.Stop(); }

    // ===== Offline (tanpa Netcode) =====
    private void ShiftLocal(int dir)
    {
        int n = Count;
        if (n <= 0) return;

        int cur = SafeIndex(currentIndex.Value);
        int next = WrapIndex(cur + dir, n);

        SetIndex(next, immediate: false);
        UpdateInfoUI(next);
        if (playAudioOnChange) TryPlayAudioLocal(next, forceRestart: restartIfSameClip);
    }

    private void TryPlayAudioLocal(int idx, bool forceRestart)
    {
        if (!audioSource) { Debug.LogWarning("[Audio][LOCAL] No AudioSource"); return; }
        if (CountData <= 0 || idx < 0 || idx >= CountData) { audioSource.Stop(); return; }

        var data = dataList[idx];
        var clip = data != null ? data.narration : null;
        if (!clip) { Debug.LogWarning("[Audio][LOCAL] Clip null"); audioSource.Stop(); return; }

        EnsureAudibleSettingsForTest();
        audioSource.loop = loopAudio;

        if (!forceRestart && audioSource.clip == clip && audioSource.isPlaying) return;

        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.Play();
        Debug.Log($"[Audio][LOCAL] Play() clip={clip.name}, vol={audioSource.volume}");

        if (!audioSource.isPlaying)
        {
            audioSource.PlayOneShot(clip, audioSource.volume);
            Debug.LogWarning("[Audio][LOCAL] Fallback PlayOneShot");
        }
    }

    private void EnsureAudibleSettingsForTest()
    {
        if (!audioSource) return;
        // Paksa setelan aman supaya bisa kedengeran
        audioSource.spatialBlend = 0f;          // 2D dulu
        audioSource.volume = Mathf.Clamp01(audioVolume <= 0 ? 1f : audioVolume);
        audioSource.mute = false;
        audioSource.pitch = 1f;
        audioSource.dopplerLevel = 0f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 100f;
        audioSource.maxDistance = 1000f;

        if (!audioSource.gameObject.activeInHierarchy)
            Debug.LogWarning("[Audio] AudioSource GameObject sedang non-aktif.");
    }

    // ===== Update 3D =====
    private void SetIndex(int newIndex, bool immediate)
    {
        int n = Count; if (n <= 0) return;

        _animating = !immediate;

        int left = WrapIndex(newIndex - 1, n);
        int right = WrapIndex(newIndex + 1, n);

        var goC = EnsureInstance(newIndex);
        var goL = EnsureInstance(left);
        var goR = EnsureInstance(right);

        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i]) _instances[i].SetActive(i == newIndex || i == left || i == right);

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

    private void UpdateInfoUI(int idx)
    {
        if (CountData <= 0 || idx < 0 || idx >= CountData) return;

        var data = dataList[idx];
        if (infoUIPanels != null)
            foreach (var p in infoUIPanels) if (p) p.Show(data);

        OnPlanetInfoChanged?.Invoke(data);
    }

    private void AnimateMoveScale(GameObject go, Vector3 pos, Vector3 scale, Action onComplete = null)
    {
        if (!go) { onComplete?.Invoke(); return; }
        LeanTween.moveLocal(go, pos, moveDuration).setEase(moveEase);
        LeanTween.scale(go, scale, scaleDuration).setEase(scaleEase)
                 .setOnComplete(() => onComplete?.Invoke());
    }

    private void SetLocal(GameObject go, Vector3 pos, Vector3 scale)
    {
        if (!go) return;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
    }

    private void Setup(GameObject go)
    {
        if (!go) return;
        if (go.transform.parent != anchor) go.transform.SetParent(anchor, false);
        if (addAutoSpinIfMissing && !go.GetComponentInChildren<AutoSpin>())
        {
            var s = go.AddComponent<AutoSpin>();
            s.degPerSec = selfSpinDeg;
        }
    }

    private GameObject EnsureInstance(int idx)
    {
        if (idx < 0 || idx >= _instances.Count) return null;
        if (_instances[idx]) return _instances[idx];

        GameObject prefab = (planetPrefabs != null && idx < planetPrefabs.Length) ? planetPrefabs[idx] : null;
        GameObject go;

        if (prefab)
        {
            go = Instantiate(prefab, anchor, false);
            // strip semua NetworkObject di instans visual
            var netObjs = go.GetComponentsInChildren<NetworkObject>();
            for (int i = 0; i < netObjs.Length; i++)
            {
#if UNITY_EDITOR
               UnityEngine.Object.DestroyImmediate(netObjs[i]);
#else
               Destroy(netObjs[i]);
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
        go.AddComponent<NetworkObject>();
        _instances[idx] = go;
        return go;
    }

    // ===== Math helpers & utils =====
    private static int WrapIndex(int i, int n) { if (n <= 0) return 0; i %= n; if (i < 0) i += n; return i; }
    private int SafeIndex(int i) => WrapIndex(i, Count);
    public int GetCurrentIndex() => SafeIndex(currentIndex.Value);

    public void ForceRefresh()
    {
        int idx = GetCurrentIndex();
        SetIndex(idx, immediate: true);
        UpdateInfoUI(idx);
        TryPlayAudioLocal(idx, forceRestart: false);
    }

    // ===== Komponen kecil putar diri =====
    private class AutoSpin : MonoBehaviour
    {
        public float degPerSec = 25f;
        void Update() => transform.Rotate(0f, degPerSec * Time.deltaTime, 0f, Space.Self);
    }
}
