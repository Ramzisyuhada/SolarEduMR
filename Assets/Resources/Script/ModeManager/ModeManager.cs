using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public enum AppMode { None, Planet, Quran, Arrange }

public class ModeManager : NetworkBehaviour
{
    [Header("Root GameObjects untuk Tiap Mode")]
    public GameObject planetCarouselRoot;   // PlanetCarousel3DUI_Net
    public GameObject quranCarouselRoot;    // QuranCarousel3DUI_Net
    public GameObject arrangeGameRoot;      // SolarGameManager + OrbitGenerator

    [Header("Reference Controller (opsional tapi disarankan)")]
    public PlanetCarousel3DUI_Net planetController;
    public QuranCarousel3DUI_Net quranController;
    // tambahkan controller arrange bila perlu

    [Header("UI Tombol Global (opsional)")]
    public GameObject modeButtonsRoot;      // Panel tombol (Planet/Quran/Arrange)
    [Space(4)]
    public Button planetPrevBtn;
    public Button planetNextBtn;
    public Button quranPrevBtn;
    public Button quranNextBtn;
    // tambahkan tombol arrange bila ada

    [Header("Config")]
    public bool syncModeForAllPlayers = true;
    public AppMode defaultMode = AppMode.None;
    public bool showButtonsOnStart = false;

    private NetworkVariable<AppMode> currentMode = new(
        AppMode.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ---------------- Lifecycle ----------------
    public override void OnNetworkSpawn()
    {
        currentMode.OnValueChanged += OnModeChanged;

        if (IsServer)
        {
            // Set hanya sekali saat network siap; kalau sudah berubah (mis. dari host UI), biarkan.
            if (currentMode.Value == AppMode.None && defaultMode != AppMode.None)
                currentMode.Value = defaultMode;
        }

        // Terapkan state yang berlaku saat spawn / late-join
        ApplyMode(currentMode.Value);
    }

    private void OnDestroy()
    {
        currentMode.OnValueChanged -= OnModeChanged;
    }

    private void OnModeChanged(AppMode oldMode, AppMode newMode)
    {
        ApplyMode(newMode);
    }

    // ---------------- API Umum ----------------
    public void SetModePlanet() => SetMode(AppMode.Planet);
    public void SetModeQuran() => SetMode(AppMode.Quran);
    public void SetModeArrange() => SetMode(AppMode.Arrange);
    public void SetModeNone() => SetMode(AppMode.None);

    private void SetMode(AppMode mode)
    {
        if (syncModeForAllPlayers)
        {
            if (IsServer) currentMode.Value = mode;
            else RequestChangeModeServerRpc(mode);
        }
        else
        {
            // Lokal saja (tidak broadcast)
            ApplyMode(mode);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestChangeModeServerRpc(AppMode newMode)
    {
        currentMode.Value = newMode;
    }

    // ---------------- Inti: Terapkan Mode + wiring tombol ----------------
    private void ApplyMode(AppMode mode)
    {
        // 1) Matikan semua root dulu
        if (planetCarouselRoot) planetCarouselRoot.SetActive(false);
        if (quranCarouselRoot) quranCarouselRoot.SetActive(false);
        if (arrangeGameRoot) arrangeGameRoot.SetActive(false);

        // 2) Nyalakan yang dipilih
        switch (mode)
        {
            case AppMode.Planet:
                if (planetCarouselRoot) planetCarouselRoot.SetActive(true);
                break;
            case AppMode.Quran:
                if (quranCarouselRoot) quranCarouselRoot.SetActive(true);
                break;
            case AppMode.Arrange:
                if (arrangeGameRoot) arrangeGameRoot.SetActive(true);
                break;
            case AppMode.None:
            default:
                break;
        }

        // 3) Tampilkan/ sembunyikan panel tombol mode
        if (modeButtonsRoot)
            modeButtonsRoot.SetActive(showButtonsOnStart || mode != AppMode.None);

        // 4) Wiring tombol global — aktifkan hanya tombol milik mode yang hidup
        RewireGlobalButtons(mode);
    }

    private void RewireGlobalButtons(AppMode mode)
    {
        // Matikan semua interaksi & lepas listener
        if (planetPrevBtn) { planetPrevBtn.interactable = false; planetPrevBtn.onClick.RemoveAllListeners(); }
        if (planetNextBtn) { planetNextBtn.interactable = false; planetNextBtn.onClick.RemoveAllListeners(); }
        if (quranPrevBtn) { quranPrevBtn.interactable = false; quranPrevBtn.onClick.RemoveAllListeners(); }
        if (quranNextBtn) { quranNextBtn.interactable = false; quranNextBtn.onClick.RemoveAllListeners(); }

        // Aktifkan sesuai mode aktif + root aktif + controller ada
        if (mode == AppMode.Planet && planetCarouselRoot && planetCarouselRoot.activeInHierarchy && planetController)
        {
            if (planetPrevBtn)
            {
                planetPrevBtn.interactable = true;
                planetPrevBtn.onClick.AddListener(planetController.OnPrev);
            }
            if (planetNextBtn)
            {
                planetNextBtn.interactable = true;
                planetNextBtn.onClick.AddListener(planetController.OnNext);
            }
        }
        else if (mode == AppMode.Quran && quranCarouselRoot && quranCarouselRoot.activeInHierarchy && quranController)
        {
            if (quranPrevBtn)
            {
                quranPrevBtn.interactable = true;
                quranPrevBtn.onClick.AddListener(quranController.OnPrev);
            }
            if (quranNextBtn)
            {
                quranNextBtn.interactable = true;
                quranNextBtn.onClick.AddListener(quranController.OnNext);
            }
        }
        // Tambahkan block Arrange kalau punya tombol globalnya
    }

    // Opsional: panggil dari tombol “Mulai”
    public void ShowModeButtons(bool show)
    {
        if (modeButtonsRoot) modeButtonsRoot.SetActive(show);
    }
}
