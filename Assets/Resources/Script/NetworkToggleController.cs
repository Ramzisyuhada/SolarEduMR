using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

[DisallowMultipleComponent]
public class NetworkToggleController : NetworkBehaviour
{
    [Header("Target yang akan diaktif/nonaktifkan")]
    [Tooltip("Jangan tempel script ini di objek yang ikut di-toggle. Pakai child/objek terpisah.")]
    public GameObject targetObject;            // mis. Orbit / apapun

    [Header("Pengertian Planet")]
    public GameObject PengertianPlanet;        // panel/GO untuk mode Pengertian
    public GameObject UIPengertian;            // container UI pengertian
    public GameObject Planet;                  // container 3D planet untuk mode pengertian

    [Header("UI")]
    public Button toggleButton;                // tombol untuk targetObject (mode 'Target')
    public Button toggleButton2;               // tombol untuk PengertianPlanet (mode 'Pengertian')

    [Header("Otoritas")]
    [Tooltip("Jika true, hanya Host/Server yang boleh mengubah state.")]
    public bool onlyServerCanToggle = false;

    [Header("Default State (untuk offline / sebelum spawn pertama)")]
    public bool startActive = false;           // default targetObject
    public bool startPengertianActive = false; // default PengertianPlanet

    // ====== State jaringan (server-authoritative) ======
    private readonly NetworkVariable<bool> isActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> isPengertianActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Penanda inisialisasi server sekali
    private bool _serverInitialized = false;

    // ---------- Unity lifecycle ----------
    private void Awake()
    {
        if (!targetObject)
            Debug.LogWarning("[NetworkToggleController] targetObject belum di-assign!");
        if (!PengertianPlanet)
            Debug.LogWarning("[NetworkToggleController] PengertianPlanet belum di-assign!");

        // OFFLINE / sebelum spawn → tampilkan default lokal
        if (!IsSpawned)
        {
            ApplyTargetState(startActive);
            ApplyPengertianState(startPengertianActive);
        }

        if (toggleButton != null)
            toggleButton.onClick.AddListener(OnClickToggleTarget);
        else
            Debug.LogWarning("[NetworkToggleController] toggleButton belum di-assign!");

        if (toggleButton2 != null)
            toggleButton2.onClick.AddListener(OnClickTogglePengertian);
        else
            Debug.LogWarning("[NetworkToggleController] toggleButton2 belum di-assign!");
    }

    public override void OnNetworkSpawn()
    {
        // Inisialisasi nilai awal di SERVER sekali (menghormati nilai inspector)
        if (IsServer && !_serverInitialized)
        {
            // Pastikan eksklusif
            if (startActive && startPengertianActive)
                startPengertianActive = false;

            isActive.Value = startActive;
            isPengertianActive.Value = startPengertianActive;
            _serverInitialized = true;
        }

        // Apply state saat join (client & server)
        ApplyTargetState(isActive.Value);
        ApplyPengertianState(isPengertianActive.Value);

        // Subscribe perubahan
        isActive.OnValueChanged += OnActiveChanged;
        isPengertianActive.OnValueChanged += OnPengertianChanged;

        // Atur interaksi tombol ketika sudah join jaringan
        if (toggleButton != null)
            toggleButton.interactable = !onlyServerCanToggle || IsServer;
        if (toggleButton2 != null)
            toggleButton2.interactable = !onlyServerCanToggle || IsServer;

        Debug.Log($"[NetworkToggleController] Spawned. IsServer={IsServer}, target={isActive.Value}, pengertian={isPengertianActive.Value}");
    }

    public override void OnNetworkDespawn()
    {
        isActive.OnValueChanged -= OnActiveChanged;
        isPengertianActive.OnValueChanged -= OnPengertianChanged;
    }

    private void OnDisable()
    {
        // Proteksi dobel-subscribe jika ada enable/disable
        isActive.OnValueChanged -= OnActiveChanged;
        isPengertianActive.OnValueChanged -= OnPengertianChanged;

        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(OnClickToggleTarget);
        if (toggleButton2 != null)
            toggleButton2.onClick.RemoveListener(OnClickTogglePengertian);
    }

    // ---------- Apply helpers ----------
    private void OnActiveChanged(bool oldVal, bool newVal)
    {
        ApplyTargetState(newVal);
        // Eksklusif: jika target ON, pengertian OFF (visual sudah ditangani Apply*)
        if (newVal && isPengertianActive.Value)
        {
            // Hanya server yang boleh mengubah NetworkVariable
            if (IsServer) isPengertianActive.Value = false;
        }
    }

    private void OnPengertianChanged(bool oldVal, bool newVal)
    {
        ApplyPengertianState(newVal);
        // Eksklusif: jika pengertian ON, target OFF
        if (newVal && isActive.Value)
        {
            if (IsServer) isActive.Value = false;
        }
    }

    private void ApplyTargetState(bool value)
    {
        // Mode Target
        if (targetObject) targetObject.SetActive(value);

        // Kalau mode Target aktif, mode Pengertian dimatikan
        bool pengertianOff = value;
        if (UIPengertian) UIPengertian.SetActive(!pengertianOff && isPengertienActiveLocal());
        if (Planet) Planet.SetActive(!pengertianOff && isPengertienActiveLocal());
        if (PengertianPlanet) PengertianPlanet.SetActive(!pengertianOff && isPengertienActiveLocal());
    }

    private void ApplyPengertianState(bool value)
    {
        // Mode Pengertian
        if (PengertianPlanet) PengertianPlanet.SetActive(value);
        if (UIPengertian) UIPengertian.SetActive(value);
        if (Planet) Planet.SetActive(value);

        // Matikan mode Target saat Pengertian aktif
        if (targetObject) targetObject.SetActive(!value && isActiveLocal());
    }

    // Helper membaca state lokal yang sedang berlaku (tanpa memaksa jaringan)
    private bool isActiveLocal()
    {
        // Saat offline, gunakan activeSelf jika ada, kalau tidak fallback ke startActive
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return targetObject ? targetObject.activeSelf : startActive;
        return isActive.Value;
    }

    private bool isPengertienActiveLocal()
    {
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return PengertianPlanet ? PengertianPlanet.activeSelf : startPengertianActive;
        return isPengertianActive.Value;
    }

    // ---------- Button handlers ----------
    private void OnClickToggleTarget()
    {
        // OFFLINE / belum listening → toggle lokal saja
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            bool newVal = targetObject ? !targetObject.activeSelf : !startActive;
            // Eksklusif secara lokal:
            ApplyTargetState(newVal);
            ApplyPengertianState(false);
            Debug.Log($"[NetworkToggleController] Offline toggle TARGET → {newVal}");
            return;
        }

        if (onlyServerCanToggle)
        {
            if (IsServer)
            {
                // Server memastikan eksklusif
                isActive.Value = !isActive.Value;
                if (isActive.Value) isPengertianActive.Value = false;
                Debug.Log($"[NetworkToggleController] Server toggle TARGET → {isActive.Value}");
            }
            else
            {
                Debug.Log("[NetworkToggleController] (TARGET) Hanya server yang boleh toggle.");
            }
            return;
        }

        // Semua klien boleh request ke server
        ToggleTargetServerRpc();
    }

    private void OnClickTogglePengertian()
    {
        // OFFLINE / belum listening → toggle lokal saja
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            bool newVal = PengertianPlanet ? !PengertianPlanet.activeSelf : !startPengertianActive;
            // Eksklusif secara lokal:
            ApplyPengertianState(newVal);
            ApplyTargetState(false);
            Debug.Log($"[NetworkToggleController] Offline toggle PENGERTIAN → {newVal}");
            return;
        }

        if (onlyServerCanToggle)
        {
            if (IsServer)
            {
                isPengertianActive.Value = !isPengertianActive.Value;
                if (isPengertianActive.Value) isActive.Value = false;
                Debug.Log($"[NetworkToggleController] Server toggle PENGERTIAN → {isPengertianActive.Value}");
            }
            else
            {
                Debug.Log("[NetworkToggleController] (PENGERTIAN) Hanya server yang boleh toggle.");
            }
            return;
        }

        // Semua klien boleh request ke server
        TogglePengertianServerRpc();
    }

    // ---------- RPCs (server memutuskan eksklusif) ----------
    [ServerRpc(RequireOwnership = false)]
    private void ToggleTargetServerRpc(ServerRpcParams _ = default)
    {
        isActive.Value = !isActive.Value;
        if (isActive.Value) isPengertianActive.Value = false; // eksklusif
       // PengertianPlanet.GetComponent<NetworkObject>().Spawn(false);

        Debug.Log($"[NetworkToggleController] ServerRpc TARGET → {isActive.Value}");
    }

    [ServerRpc(RequireOwnership = false)]
    private void TogglePengertianServerRpc(ServerRpcParams _ = default)
    {
        isPengertianActive.Value = !isPengertianActive.Value;
        if (isPengertianActive.Value) isActive.Value = false;
        if (!PengertianPlanet.GetComponent<NetworkObject>().IsSpawned)
        {
            PengertianPlanet.GetComponent<NetworkObject>().Spawn(true);
            Debug.Log("Hello world");
        }
        Debug.Log($"[NetworkToggleController] ServerRpc PENGERTIAN → {isPengertianActive.Value}");
    }
}
