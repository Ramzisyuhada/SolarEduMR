using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class NetworkToggleController : NetworkBehaviour
{
    [Header("Target yang akan diaktif/nonaktifkan")]
    [Tooltip("Jangan tempel script ini di objek yang ikut di-toggle. Pakai child/objek terpisah.")]
    public GameObject targetObject;

    [Header("UI")]
    public Button toggleButton;

    [Header("Otoritas")]
    public bool onlyServerCanToggle = false;

    [Header("Default State (untuk offline / saat belum spawn)")]
    public bool startActive = false;

    // state jaringan
    private NetworkVariable<bool> isActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    void Awake()
    {
        if (!targetObject)
            Debug.LogWarning("[NetworkToggleController] targetObject belum di-assign!");

        // Set state awal untuk OFFLINE / sebelum spawn
        if (!IsSpawned)
            ApplyState(startActive);

        if (toggleButton != null)
            toggleButton.onClick.AddListener(OnClickToggle);
        else
            Debug.LogWarning("[NetworkToggleController] toggleButton belum di-assign!");
    }

    public override void OnNetworkSpawn()
    {
        // Sinkronkan state ketika sudah spawn
        ApplyState(isActive.Value);
        isActive.OnValueChanged += OnActiveChanged;

        if (toggleButton != null)
            toggleButton.interactable = !onlyServerCanToggle || IsServer;

        Debug.Log($"[NetworkToggleController] Spawned. IsServer={IsServer}, isActive={isActive.Value}");
    }

    public override void OnNetworkDespawn()
    {
        isActive.OnValueChanged -= OnActiveChanged;
    }

    void OnActiveChanged(bool oldVal, bool newVal) => ApplyState(newVal);

    void ApplyState(bool value)
    {
        if (targetObject != null)
            targetObject.SetActive(value);
    }

    void OnClickToggle()
    {
        // Jika jaringan BELUM jalan, toggle lokal (agar saat tes offline tetap terasa)
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            bool newVal = !targetObject.activeSelf;
            ApplyState(newVal);
            Debug.Log($"[NetworkToggleController] Offline toggle → {newVal}");
            return;
        }

        // Mode hanya server yang boleh
        if (onlyServerCanToggle)
        {
            if (IsServer)
            {
                isActive.Value = !isActive.Value;
                Debug.Log($"[NetworkToggleController] Server toggle → {isActive.Value}");
            }
            else
            {
                Debug.Log("[NetworkToggleController] Hanya server yang boleh toggle.");
            }
            return;
        }

        // Semua client boleh request ke server
        RequestToggleServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestToggleServerRpc(ServerRpcParams _ = default)
    {
        isActive.Value = !isActive.Value;
        Debug.Log($"[NetworkToggleController] ServerRpc toggle → {isActive.Value}");
    }
}
