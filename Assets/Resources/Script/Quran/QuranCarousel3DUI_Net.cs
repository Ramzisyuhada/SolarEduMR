using UnityEngine;
using Unity.Netcode;

//
// Carousel Qur'an TANPA 3D.
// Sinkronkan index, dan playback audio sinkron pakai waktu server.
// - currentIndex → ayat aktif
// - playMode: 0=stop, 1=ayat, 2=arti
// - playStartTime: timestamp waktu server saat mulai play
// - playIndex: index ayat yang sedang dimainkan
//
public class QuranCarousel3DUI_Net : NetworkBehaviour
{
    [Header("Data Qur'an (urut sesuai index)")]
    public QuranData[] dataList;

    [Header("Panel Qur'an 3D (UI saja)")]
    public Quran3DDisplay quranDisplay;   // berisi ayatJudul, ayatImage, artiImage, audio sources

    // ====== State jaringan ======
    private NetworkVariable<int> currentIndex = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<byte> playMode = new( // 0=none,1=ayat,2=arti
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<double> playStartTime = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<int> playIndex = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    bool _localInitialized;

    void Start()
    {
        if (!IsSpawned) EnsureLocalInitialized(); // mode offline masih jalan
    }

    public override void OnNetworkSpawn()
    {
        // Refs
        if (!quranDisplay) quranDisplay = FindObjectOfType<Quran3DDisplay>(true);

        // Reaksi saat index ganti → update panel
        currentIndex.OnValueChanged += (_, newVal) =>
        {
            UpdateQuranDisplay(newVal);
            // opsional: stop audio saat ganti ayat
            // kalau mau auto-continue, hapus baris di bawah
            if (IsClient) ApplyPlaybackFromState(); // biar kalau playMode!=0 tetap ke ayat yg benar
        };

        // Reaksi saat state playback berubah
        playMode.OnValueChanged += (_, __) =>
        {
            if (IsClient) ApplyPlaybackFromState();
        };
        playIndex.OnValueChanged += (_, __) =>
        {
            if (IsClient) ApplyPlaybackFromState();
        };
        playStartTime.OnValueChanged += (_, __) =>
        {
            if (IsClient) ApplyPlaybackFromState();
        };

        // Set awal
        if (IsServer)
        {
            int max = Mathf.Max(0, (dataList?.Length ?? 1) - 1);
            currentIndex.Value = Mathf.Clamp(currentIndex.Value, 0, max);
            // jaga konsistensi playback untuk late joiner
            if (playMode.Value != 0 && (playIndex.Value < 0 || playIndex.Value >= (dataList?.Length ?? 0)))
            {
                playMode.Value = 0;
            }
        }
        else
        {
            UpdateQuranDisplay(currentIndex.Value);
            ApplyPlaybackFromState(); // kalau server lagi play, klien yang baru join langsung ikut
        }
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
        currentIndex.Value = newIndex;

        // opsional: stop audio saat ganti ayat
        playMode.Value = 0;
    }

    // fallback lokal (offline)
    void ShiftLocal(int dir)
    {
        int n = Mathf.Max(dataList != null ? dataList.Length : 0, 1);
        int newIndex = ((currentIndex.Value + dir) % n + n) % n;
        currentIndex.Value = newIndex;
        UpdateQuranDisplay(newIndex);
        // stop lokal
        if (quranDisplay) quranDisplay.StopAll();
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
        quranDisplay.Show(dataList[idx]);   // tampilkan sprites & judul
    }

    // ====== Tombol Audio ======
    public void OnPlayAyat()
    {
        if (IsSpawned) RequestPlayServerRpc(1);
        else { EnsureLocalInitialized(); quranDisplay?.PlayAyatAt(0); }
    }

    public void OnPlayArti()
    {
        if (IsSpawned) RequestPlayServerRpc(2);
        else { EnsureLocalInitialized(); quranDisplay?.PlayArtiAt(0); }
    }

    public void OnStopAudio()
    {
        if (IsSpawned) RequestStopAllServerRpc();
        else { EnsureLocalInitialized(); quranDisplay?.StopAll(); }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPlayServerRpc(int mode /*1=ayat,2=arti*/)
    {
        if (dataList == null || dataList.Length == 0) return;

        // set state agar LATE JOINER ikut
        playIndex.Value = currentIndex.Value;
        playStartTime.Value = NetworkManager.ServerTime.Time; // timestamp server
        playMode.Value = (byte)Mathf.Clamp(mode, 0, 2);
        // Tidak perlu ClientRpc manual karena kita pakai OnValueChanged di atas
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestStopAllServerRpc()
    {
        playMode.Value = 0; // semua klien stop lewat ApplyPlaybackFromState
    }

    // ====== Terapkan state playback di klien (termasuk late joiner) ======
    void ApplyPlaybackFromState()
    {
        if (!quranDisplay || dataList == null || dataList.Length == 0) return;

        // Jika stop
        if (playMode.Value == 0)
        {
            quranDisplay.StopAll();
            return;
        }

        int idx = Mathf.Clamp(playIndex.Value, 0, dataList.Length - 1);
        // Pastikan panel menampilkan ayat yang sedang diputar
        if (currentIndex.Value != idx)
        {
            UpdateQuranDisplay(idx);
        }

        // Hitung offset dari waktu server
        double now = NetworkManager.ServerTime.Time;
        double elapsed = now - playStartTime.Value;
        float offset = Mathf.Max(0f, (float)elapsed);

        if (playMode.Value == 1) quranDisplay.PlayAyatAt(offset);
        else if (playMode.Value == 2) quranDisplay.PlayArtiAt(offset);
    }
}
