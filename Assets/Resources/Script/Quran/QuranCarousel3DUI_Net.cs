using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;   // hanya untuk opsi label Text; boleh kosongkan kalau tak terpakai
//#define TMP_PRESENT     // uncomment kalau kamu pakai TextMeshPro dan isi field TMP di Inspector
#if TMP_PRESENT
using TMPro;
#endif

public class QuranCarousel3DUI_Net : NetworkBehaviour
{
    [Header("Data Qur'an (urut sesuai index)")]
    public QuranData[] dataList;

    [Header("Panel Qur'an 3D (UI saja)")]
    public Quran3DDisplay quranDisplay;   // Panel UI untuk judul, gambar ayat & arti

    [Header("Suara Ayat & Arti (sesuai index)")]
    [Tooltip("Panjang array sebaiknya >= jumlah dataList. Index 0 -> ayat 0, dst.")]
    public AudioClip[] AyatClip;
    public AudioClip[] ArtiClip;

    [Header("UI (opsional)")]
    [Tooltip("Isi jika mau label tombol Play/Stop berubah otomatis")]
    public Text buttonLabelText;
#if TMP_PRESENT
    public TMP_Text buttonLabelTMP;
#endif

    [Header("Behavior")]
    [Tooltip("Jika true, setelah Prev/Next audio akan lanjut otomatis.")]
    public bool autoContinueAfterShift = false;

    public enum ContinueMode { Ayat = 1, Arti = 2 }
    [Tooltip("Mode audio saat auto-continue setelah Prev/Next.")]
    public ContinueMode autoContinueMode = ContinueMode.Ayat;

    // ====== State jaringan ======
    private NetworkVariable<int> currentIndex = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 0=stop, 1=ayat, 2=arti
    private NetworkVariable<byte> playMode = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Waktu server saat mulai play (untuk sync offset)
    private NetworkVariable<double> playStartTime = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Index ayat yang sedang diputar (agar pindah halaman tidak menggeser audio berjalan)
    private NetworkVariable<int> playIndex = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    bool _localInitialized;

    // ====== LIFECYCLE ======
    void Start()
    {
        if (!IsSpawned) EnsureLocalInitialized(); // mode offline tetap jalan
    }

    public override void OnNetworkSpawn()
    {
        if (!quranDisplay) quranDisplay = FindObjectOfType<Quran3DDisplay>(true);

        // Reaksi perubahan index aktif
        currentIndex.OnValueChanged += (_, newVal) =>
        {
            UpdateQuranDisplay(newVal);
            if (IsClient) ApplyPlaybackFromState();  // jaga konsistensi audio
            UpdateToggleLabel();
        };

        // Reaksi perubahan state playback
        playMode.OnValueChanged += (_, __) =>
        {
            if (IsClient) ApplyPlaybackFromState();
            UpdateToggleLabel();
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

            // jaga konsistensi playback utk late joiner
            if (playMode.Value != 0 && (playIndex.Value < 0 || playIndex.Value >= (dataList?.Length ?? 0)))
                playMode.Value = 0;
        }
        else
        {
            UpdateQuranDisplay(currentIndex.Value);
            ApplyPlaybackFromState(); // klien yang baru join langsung ikut
        }

        UpdateToggleLabel();
    }

    void EnsureLocalInitialized()
    {
        if (_localInitialized) return;
        if (!quranDisplay) quranDisplay = FindObjectOfType<Quran3DDisplay>(true);
        UpdateQuranDisplay(currentIndex.Value);
        _localInitialized = true;
        UpdateToggleLabel();
    }

    // ====== Prev / Next ======
    public void OnPrev()
    {
        if (IsSpawned) RequestShiftServerRpc(-1, autoContinueAfterShift, (int)autoContinueMode);
        else { EnsureLocalInitialized(); ShiftLocal(-1); }
    }

    public void OnNext()
    {
        if (IsSpawned) RequestShiftServerRpc(+1, autoContinueAfterShift, (int)autoContinueMode);
        else { EnsureLocalInitialized(); ShiftLocal(+1); }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestShiftServerRpc(int dir, bool autoContinue, int autoMode /*1=ayat,2=arti*/)
    {
        int n = Mathf.Max(dataList?.Length ?? 0, 1);
        int newIndex = ((currentIndex.Value + dir) % n + n) % n;
        currentIndex.Value = newIndex;

        if (!autoContinue)
        {
            // default: stop saat ganti ayat
            playMode.Value = 0;
        }
        else
        {
            // Auto-continue: mulai audio ayat/arti sesuai pilihan
            playIndex.Value = newIndex;
            playStartTime.Value = NetworkManager.ServerTime.Time;
            playMode.Value = (byte)Mathf.Clamp(autoMode, 1, 2);
        }
    }

    // fallback lokal (offline)
    void ShiftLocal(int dir)
    {
        int n = Mathf.Max(dataList?.Length ?? 0, 1);
        int newIndex = ((currentIndex.Value + dir) % n + n) % n;
        currentIndex.Value = newIndex;
        UpdateQuranDisplay(newIndex);

        if (!autoContinueAfterShift)
        {
            quranDisplay?.StopAll();
        }
        else
        {
            // Lanjutkan lokal sesuai mode
            if (autoContinueMode == ContinueMode.Ayat) PlayLocalAudio(1);
            else PlayLocalAudio(2);
        }

        UpdateToggleLabel();
    }

    // ====== Update UI Panel ======
    void UpdateQuranDisplay(int idx)
    {
        if (!quranDisplay) return;

        if (dataList == null || dataList.Length == 0 || idx < 0 || idx >= dataList.Length)
        {
            quranDisplay.Hide();
            return;
        }

        quranDisplay.AutoAssignCamera();
        quranDisplay.Show(dataList[idx]);   // tampilkan gambar judul/ayat/arti
    }
    //public void OnStopAudio()
    //{
    //    // Stop lokal langsung biar cepat
    //    quranDisplay?.StopAll();

    //    // Broadcast ke semua klien lewat server
    //    if (IsSpawned) RequestStopAllServerRpc();
    //    else
    //    {
    //        playMode.Value = 0;
    //        playStartTime.Value = 0;
    //        playIndex.Value = currentIndex.Value;
    //    }

    //    UpdateToggleLabel();
    //}

    // ====== TOMBOL TOGGLE PLAY/STOP (SATU TOMBOL) ======
    public void OnPlayStopToggle()
    {
        // Jika sedang bermain → Stop
        if (playMode.Value != 0)
        {
            if (IsSpawned) RequestStopAllServerRpc();
            else quranDisplay?.StopAll();
        }
        // Jika berhenti → Play AYAT pada index sekarang
        else
        {
            if (IsSpawned) RequestPlayServerRpc(1);  // default: mode ayat
            else PlayLocalAudio(1);
        }

        UpdateToggleLabel();
    }
    [ClientRpc]
    void ForceStopAllClientRpc()
    {
        if (!quranDisplay)
            quranDisplay = FindObjectOfType<Quran3DDisplay>(true);

        // Stop SEMUA AudioSource di panel & anak-anaknya (tegas)
        quranDisplay?.StopAll();
    }

    // (Opsional) method khusus mainkan Arti
    public void OnPlayArti()
    {
        if (IsSpawned) RequestPlayServerRpc(2);
        else PlayLocalAudio(2);
        UpdateToggleLabel();
    }

    // (Opsional) method khusus Stop
    public void OnStopAudio()
    {
        if (IsSpawned) RequestStopAllServerRpc();
        else quranDisplay?.StopAll();
        UpdateToggleLabel();
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPlayServerRpc(int mode /*1=ayat,2=arti*/)
    {
        if (dataList == null || dataList.Length == 0) return;

        playIndex.Value = currentIndex.Value;
        playStartTime.Value = NetworkManager.ServerTime.Time; // timestamp server
        playMode.Value = (byte)Mathf.Clamp(mode, 0, 2);
        // Tidak perlu ClientRpc manual → OnValueChanged memicu ApplyPlaybackFromState
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestStopAllServerRpc()
    {
        // 1) Matikan state global (supaya ApplyPlaybackFromState() kondisi stop)
        playMode.Value = 0;
        playStartTime.Value = 0;
        playIndex.Value = currentIndex.Value;

        // 2) Paksa SEMUA klien stop audio sekarang juga (hindari race)
        ForceStopAllClientRpc();
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

        // Pastikan panel menunjukkan ayat yang sedang diputar
        if (currentIndex.Value != idx)
            UpdateQuranDisplay(idx);

        // Hitung offset dari waktu server
        double now = NetworkManager.ServerTime.Time;
        float offset = Mathf.Max(0f, (float)(now - playStartTime.Value));

        if (playMode.Value == 1) PlayClipAt(AyatClip, idx, offset);
        else if (playMode.Value == 2) PlayClipAt(ArtiClip, idx, offset);
    }

    // ====== Playback lokal (offline) ======
    void PlayLocalAudio(int mode)
    {
        int idx = currentIndex.Value;
        if (mode == 1) PlayClipAt(AyatClip, idx, 0f);
        else if (mode == 2) PlayClipAt(ArtiClip, idx, 0f);
    }

    // ====== Helper audio: ambil/auto-add AudioSource di quranDisplay ======
    void PlayClipAt(AudioClip[] clips, int idx, float offset)
    {
        if (clips == null || idx < 0 || idx >= clips.Length) return;
        if (!quranDisplay) return;

        // Cari/siapkan AudioSource di gameobject quranDisplay
        AudioSource src = quranDisplay.GetComponent<AudioSource>();
        if (!src) src = quranDisplay.gameObject.AddComponent<AudioSource>();

        src.playOnAwake = false;
        src.loop = false;

        src.clip = clips[idx];
        if (!src.clip) return;

        // Clamp offset agar tidak out-of-range
        src.time = Mathf.Clamp(offset, 0f, Mathf.Max(0.0f, src.clip.length - 0.001f));
        src.Play();
    }

    // ====== Ubah label tombol (opsional) ======
    void UpdateToggleLabel()
    {
        string label = (playMode.Value == 0) ? "Play" : "Stop";

        if (buttonLabelText) buttonLabelText.text = label;
#if TMP_PRESENT
        if (buttonLabelTMP) buttonLabelTMP.text = label;
#endif
    }

    // ==== GETTER untuk UI / debug ====
    public int GetCurrentIndex() => currentIndex.Value;
    public byte GetPlayMode() => playMode.Value; // 0=stop,1=ayat,2=arti
    public bool IsPlaying() => playMode.Value != 0;

    // (Opsional) baca status AudioSource untuk UI label cepat
    public bool IsAudioSourcePlaying()
    {
        if (!quranDisplay) return false;
        var src = quranDisplay.GetComponent<AudioSource>();
        return src && src.isPlaying;
    }
}
