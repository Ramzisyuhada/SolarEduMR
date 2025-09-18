using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(NetworkObject))]
public class XRTouchResponder : NetworkBehaviour
{
    [Header("Data Planet (opsional)")]
    [SerializeField] private PlanetData Data;

    [Header("UI Target")]
    [SerializeField] private GameObject PengertianPlanet;
    [SerializeField] private PlanetInfoUI InformasiUI;

    [Header("Hover Feedback (opsional)")]
    [SerializeField] private bool useHoverScale = true;
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float hoverDuration = 0.12f;
    [SerializeField] private bool ignoreTimeScale = true;

    [Header("Start State")]
    [SerializeField] private bool startHidden = true; // panel tidak tampil dulu

    [Header("UI Button (opsional)")]
    [Tooltip("Tombol untuk toggle panel via OnClick. Boleh dikosongkan.")]
    [SerializeField] private Button toggleButton;
    [Tooltip("Tombol untuk play/pause audio via OnClick. Boleh dikosongkan.")]
    [SerializeField] private Button soundButton;
    [Tooltip("Kalau true & field kosong, cari Button di children saat Awake().")]
    [SerializeField] private bool autoFindButtonsInChildren = true;

    [Header("Audio (voice-over)")]
    [Tooltip("Clip suara yang akan diputar. (Assign manual di Inspector)")]
    [SerializeField] private AudioClip voiceClip;
    [Tooltip("Sumber audio; jika kosong akan dibuat otomatis di GameObject ini.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Putar suara otomatis saat panel muncul.")]
    [SerializeField] private bool playAudioOnShow = false;
    [Tooltip("Hentikan suara saat panel ditutup.")]
    [SerializeField] private bool stopAudioOnHide = true;
    [Tooltip("Loop suara (berguna untuk ambience).")]
    [SerializeField] private bool loopAudio = false;
    [Range(0f, 1f)][SerializeField] private float volume = 1f;
    [Tooltip("0 = 2D UI, 1 = 3D spatial di dunia.")]
    [Range(0f, 1f)][SerializeField] private float spatialBlend = 0f;

    // XRI
    private XRSimpleInteractable _interactable;

    // Net state
    private readonly NetworkVariable<bool> IsShow = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> IsAudioPlaying = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool _serverInitialized = false;

    // cache
    private Vector3 _initialScale;
    private CanvasGroup _panelCg;

    // ---------- LIFECYCLE ----------
    public override void OnNetworkSpawn()
    {
        IsShow.OnValueChanged += OnIsShowChanged;
        IsAudioPlaying.OnValueChanged += OnIsAudioPlayingChanged;

        InitPanelIfNeeded();
        InitAudioIfNeeded();

        if (IsServer && !_serverInitialized)
        {
            _serverInitialized = true;
            // State awal panel
            IsShow.Value = startHidden ? false : (PengertianPlanet && PengertianPlanet.activeSelf);
            // State awal audio selalu off, kecuali kamu ingin default on
            IsAudioPlaying.Value = false;
        }
        else
        {
            // Apply current states tanpa animasi untuk late joiner
            ApplyIsShow(IsShow.Value, animated: false);
            ApplyAudio(IsAudioPlaying.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        IsShow.OnValueChanged -= OnIsShowChanged;
        IsAudioPlaying.OnValueChanged -= OnIsAudioPlayingChanged;
    }

    void Awake()
    {
        if (!InformasiUI) InformasiUI = GetComponent<PlanetInfoUI>();
        voiceClip = Data.narration;
        InformasiUI.planetDescImage.sprite = Data.infoImage;
        //_interactable = GetComponent<XRSimpleInteractable>();
        //_interactable.hoverEntered.AddListener(OnHoverEnter);
        //_interactable.hoverExited.AddListener(OnHoverExit);
        //_interactable.selectEntered.AddListener(OnSelectEnter); // Poke/Press

        if (autoFindButtonsInChildren)
        {
            if (!toggleButton) toggleButton = GetComponentInChildren<Button>(true);
            if (!soundButton)
            {
                // Cari tombol kedua bila ada beberapa Button di children
                var btns = GetComponentsInChildren<Button>(true);
                if (btns.Length > 1)
                {
                    // heuristik: pakai yang bukan toggleButton sebagai soundButton
                    foreach (var b in btns) if (b != toggleButton) { soundButton = b; break; }
                }
            }
        }

        _initialScale = transform.localScale;

        InitPanelIfNeeded();
        InitAudioIfNeeded();

        // Sembunyikan awal bila diminta
        if (PengertianPlanet && startHidden)
        {
            PengertianPlanet.SetActive(false);
            if (_panelCg) _panelCg.alpha = 0f;
            PengertianPlanet.transform.localScale = Vector3.one; // siap pop-in
        }


    }

    void OnEnable()
    {
        if (toggleButton) toggleButton.onClick.AddListener(DoTogglePanel);
        if (soundButton) soundButton.onClick.AddListener(DoToggleAudio);
    }

    void OnDisable()
    {
        if (toggleButton) toggleButton.onClick.RemoveListener(DoTogglePanel);
        if (soundButton) soundButton.onClick.RemoveListener(DoToggleAudio);
    }

    void OnDestroy()
    {
        //if (_interactable)
        //{
        //    _interactable.hoverEntered.RemoveListener(OnHoverEnter);
        //    _interactable.hoverExited.RemoveListener(OnHoverExit);
        //    _interactable.selectEntered.RemoveListener(OnSelectEnter);
        //}
        if (toggleButton) toggleButton.onClick.RemoveListener(DoTogglePanel);
        if (soundButton) soundButton.onClick.RemoveListener(DoToggleAudio);

        IsShow.OnValueChanged -= OnIsShowChanged;
        IsAudioPlaying.OnValueChanged -= OnIsAudioPlayingChanged;
    }

    // ---------- HELPERS ----------
    void InitPanelIfNeeded()
    {
        if (!PengertianPlanet) return;
        if (!_panelCg)
        {
            _panelCg = PengertianPlanet.GetComponent<CanvasGroup>();
            if (!_panelCg) _panelCg = PengertianPlanet.AddComponent<CanvasGroup>();
        }
    }

    void InitAudioIfNeeded()
    {
        if (!audioSource)
        {
            audioSource = GetComponent<AudioSource>();
            if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = loopAudio;
        audioSource.volume = volume;
        audioSource.spatialBlend = spatialBlend; // 0=2D, 1=3D
        // voiceClip bisa dikosongkan; assign di Inspector sesuai kebutuhan.
        if (!audioSource.clip && voiceClip) audioSource.clip = voiceClip;
    }

    // ---------- XRI EVENTS (Poke/Press) ----------
    //void OnHoverEnter(HoverEnterEventArgs _)
    //{
    //    if (!useHoverScale) return;
    //    LeanTween.cancel(gameObject);
    //    LeanTween.scale(gameObject, _initialScale * hoverScale, hoverDuration)
    //             .setEaseOutQuad()
    //             .setIgnoreTimeScale(ignoreTimeScale);
    //}

    //void OnHoverExit(HoverExitEventArgs _)
    //{
    //    if (!useHoverScale) return;
    //    LeanTween.cancel(gameObject);
    //    LeanTween.scale(gameObject, _initialScale, hoverDuration)
    //             .setEaseOutQuad()
    //             .setIgnoreTimeScale(ignoreTimeScale);
    //}

    void OnSelectEnter(SelectEnterEventArgs _)
    {
        // Poke/press -> toggle panel
        DoTogglePanel();
    }

    // ---------- PUBLIC (dipanggil Button OnClick) ----------
    public void DoTogglePanel()
    {
        // Offline/editor
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            bool next = !(PengertianPlanet && PengertianPlanet.activeSelf);
            ApplyIsShow(next, animated: true);
            // Auto-play saat panel muncul (offline)
            if (playAudioOnShow && next) ApplyAudio(true);
            if (stopAudioOnHide && !next) ApplyAudio(false);
            return;
        }

        // Online
        if (IsServer) IsShow.Value = !IsShow.Value;
        else RequestTogglePanelServerRpc();
    }

    public void DoToggleAudio()
    {
        // Offline/editor
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            ApplyAudio(!(audioSource && audioSource.isPlaying));
            return;
        }

        // Online
        if (IsServer) IsAudioPlaying.Value = !(IsAudioPlaying.Value);
        else RequestToggleAudioServerRpc();
    }

    // ---------- NETCODE RPC ----------
    [ServerRpc(RequireOwnership = false)]
    private void RequestTogglePanelServerRpc(ServerRpcParams _ = default)
    {
        IsShow.Value = !IsShow.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleAudioServerRpc(ServerRpcParams _ = default)
    {
        IsAudioPlaying.Value = !(IsAudioPlaying.Value);
    }

    // ---------- STATE APPLY (callbacks) ----------
    private void OnIsShowChanged(bool prev, bool show)
    {
        ApplyIsShow(show, animated: true);

        // Kebijakan audio saat panel show/hide
        if (playAudioOnShow && show)
        {
            if (IsServer) IsAudioPlaying.Value = true;
        }
        else if (stopAudioOnHide && !show)
        {
            if (IsServer) IsAudioPlaying.Value = false;
        }
    }

    private void OnIsAudioPlayingChanged(bool prev, bool play)
    {
        voiceClip = Data.narration;
        ApplyAudio(play);
    }

    private void ApplyIsShow(bool show, bool animated)
    {
        if (!PengertianPlanet)
        {
            Debug.LogWarning("[XRTouchResponder] PengertianPlanet belum di-assign.");
            return;
        }

        InitPanelIfNeeded();
        LeanTween.cancel(PengertianPlanet);

        if (!animated)
        {
            PengertianPlanet.SetActive(show);
            PengertianPlanet.transform.localScale = Vector3.one;
            if (_panelCg) _panelCg.alpha = show ? 1f : 0f;
            return;
        }

        if (show)
        {
            PengertianPlanet.SetActive(true);
            PengertianPlanet.transform.localScale = Vector3.one * 0.6f;
            if (_panelCg) _panelCg.alpha = 0f;

            LeanTween.scale(PengertianPlanet, Vector3.one, 0.28f)
                     .setEaseOutBack()
                     .setIgnoreTimeScale(true);

            if (_panelCg)
                LeanTween.alphaCanvas(_panelCg, 1f, 0.24f)
                         .setEaseOutQuad()
                         .setIgnoreTimeScale(true);
        }
        else
        {
            LeanTween.scale(PengertianPlanet, Vector3.one * 0.6f, 0.20f)
                     .setEaseInBack()
                     .setIgnoreTimeScale(true)
                     .setOnComplete(() =>
                     {
                         if (PengertianPlanet) PengertianPlanet.SetActive(false);
                     });

            if (_panelCg)
                LeanTween.alphaCanvas(_panelCg, 0f, 0.18f)
                         .setEaseInQuad()
                         .setIgnoreTimeScale(true);
        }

        // (Opsional) integrasi UI info berbasis Data
        // if (InformasiUI) { if (show) InformasiUI.Show(Data); else InformasiUI.Hide(); }
    }

    private void ApplyAudio(bool play)
    {
        InitAudioIfNeeded();

        if (!audioSource)
        {
            Debug.LogWarning("[XRTouchResponder] AudioSource tidak ditemukan/terbuat.");
            return;
        }

        // Pastikan clip ada
        if (!audioSource.clip && voiceClip) audioSource.clip = voiceClip;

        if (play)
        {
            if (!audioSource.clip)
            {
                Debug.LogWarning("[XRTouchResponder] voiceClip belum di-assign.");
                return;
            }

            audioSource.loop = loopAudio;
            audioSource.volume = volume;
            audioSource.spatialBlend = spatialBlend;

            // Mulai dari awal kalau sebelumnya sudah pernah play
            if (audioSource.isPlaying) audioSource.Stop();
            audioSource.Play();
        }
        else
        {
            if (audioSource.isPlaying) audioSource.Stop();
        }
    }
}
