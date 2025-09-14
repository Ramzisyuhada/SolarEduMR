using UnityEngine;
using Unity.Netcode;

/// Carousel Qur'an TANPA 3D.
/// Hanya sinkronkan index, update Quran3DDisplay, dan kontrol audio via RPC.
public class QuranCarousel3DUI_Net : NetworkBehaviour
{
    [Header("Data Qur'an (urut sesuai index)")]
    public QuranData[] dataList;

    [Header("Panel Qur'an 3D (UI saja)")]
    public Quran3DDisplay quranDisplay;   // berisi ayatJudul, ayatImage, artiImage, audio sources

    // sinkron index aktif (0..N-1)
    private NetworkVariable<int> currentIndex = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    bool _localInitialized;

    void Start()
    {
        // Jika belum join network / object belum spawned, tetap bisa lokal
        if (!IsSpawned) EnsureLocalInitialized();
    }

    public override void OnNetworkSpawn()
    {
        // setiap index berubah ? update panel di semua klien
        currentIndex.OnValueChanged += (oldVal, newVal) =>
        {
            UpdateQuranDisplay(newVal);
        };

        // set awal
        if (IsServer)
            currentIndex.Value = Mathf.Clamp(currentIndex.Value, 0, Mathf.Max(0, (dataList?.Length ?? 1) - 1));
        else
            UpdateQuranDisplay(currentIndex.Value);
    }

    void EnsureLocalInitialized()
    {
        if (_localInitialized) return;
        if (!quranDisplay) quranDisplay = FindObjectOfType<Quran3DDisplay>(true);
        UpdateQuranDisplay(currentIndex.Value);
        _localInitialized = true;
    }

    // ====== Tombol Prev / Next ======
    public void OnPrev()
    {
        if (IsSpawned) RequestShiftServerRpc(-1);
        else { EnsureLocalInitialized(); ShiftLocal(-1); }
    }

    public void OnNext()
    {
        if (IsSpawned) RequestShiftServerRpc(+1);
        else { EnsureLocalInitialized(); ShiftLocal(+1); }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestShiftServerRpc(int dir)
    {
        int n = Mathf.Max(dataList != null ? dataList.Length : 0, 1);
        int newIndex = ((currentIndex.Value + dir) % n + n) % n;
        currentIndex.Value = newIndex; // memicu OnValueChanged di semua klien
    }

    // fallback lokal (offline)
    void ShiftLocal(int dir)
    {
        int n = Mathf.Max(dataList != null ? dataList.Length : 0, 1);
        int newIndex = ((currentIndex.Value + dir) % n + n) % n;
        currentIndex.Value = newIndex;
        UpdateQuranDisplay(newIndex);
    }

    // ====== Update Panel UI ======
    void UpdateQuranDisplay(int idx)
    {
        if (!quranDisplay) return;

        if (dataList == null || dataList.Length == 0 || idx < 0 || idx >= dataList.Length)
        {
            quranDisplay.Hide();
            return;
        }

        quranDisplay.AutoAssignCamera();
        quranDisplay.Show(dataList[idx]);   // tampilkan sprites & judul, tanpa 3D
    }

    // ====== Audio kontrol (tombol) ======
    public void OnPlayAyat()
    {
        if (IsSpawned) RequestPlayServerRpc(0);
        else { EnsureLocalInitialized(); quranDisplay?.PlayAyat(); }
    }

    public void OnPlayArti()
    {
        if (IsSpawned) RequestPlayServerRpc(1);
        else { EnsureLocalInitialized(); quranDisplay?.PlayArti(); }
    }

    public void OnStopAudio()
    {
        if (IsSpawned) RequestStopAllServerRpc();
        else { EnsureLocalInitialized(); quranDisplay?.StopAll(); }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPlayServerRpc(int which /*0=ayat,1=arti*/)
    {
        PlayClipClientRpc(which, currentIndex.Value);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestStopAllServerRpc()
    {
        StopAllClientRpc();
    }

    [ClientRpc]
    void PlayClipClientRpc(int which, int idx)
    {
        if (!quranDisplay) return;
        if (dataList == null || idx < 0 || idx >= dataList.Length) return;

        if (which == 0) quranDisplay.PlayAyat();
        else if (which == 1) quranDisplay.PlayArti();
    }

    [ClientRpc]
    void StopAllClientRpc()
    {
        quranDisplay?.StopAll();
    }
}
