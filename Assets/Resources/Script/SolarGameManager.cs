using UnityEngine;
using Unity.Netcode;
using System.Linq;

public enum GamePhase { Lobby, Playing, Scoring, Result }

public class SolarGameManager : NetworkBehaviour
{
    [Header("Config")]
    public float roundSeconds = 180f;
    public Transform spawnArea;

    [Header("Refs (auto)")]
    public Planet[] planets;     // diisi otomatis (server+client)
    public OrbitSlot[] slots;    // diisi otomatis (server+client)

    [Header("Network State")]
    public NetworkVariable<float> timeLeft =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<GamePhase> phase =
        new(GamePhase.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> teamScore = new(0);

    [Header("UI")]
    public GameObject checkButton;   // tombol cek urutan (akan dimunculkan otomatis)

    [Header("SFX")]
    public AudioSource audioSource;
    public AudioClip sfxAllSnapped;  // tombol muncul
    public AudioClip sfxCorrect;     // semua benar
    public AudioClip sfxWrong;       // ada salah

    // runtime flags
    bool _buttonShown;
    bool _allSnappedCached;

    // warn-once flags (fallback tanpa audio)
    bool _warnedNoAudioSource, _warnedMissingAllSnapped, _warnedMissingCorrect, _warnedMissingWrong;

    // ---------- Life Cycle ----------
    void Awake()
    {
        Debug.Log($"[GM Awake] {name} instanceID={GetInstanceID()}");
    }

    public override void OnNetworkSpawn()
    {
        // server: bind penuh + reset ronde
        if (IsServer)
        {
            RebindAll();     // isi arrays & set manager di planet
            ResetRoundServer();
        }
        else
        {
            // client: minimal bind supaya UI/pewarnaan bisa jalan
            RebindAllLocal();
        }
    }

    void Start()
    {
        // jaga-jaga kalau OnNetworkSpawn client belum cukup
        if (!IsServer) RebindAllLocal();
    }

    // ---------- Rebind / Autowire ----------
    // server: isi planets/slots dan set manager ke this
    void RebindAll()
    {
        planets = FindObjectsOfType<Planet>(true);
        slots = FindObjectsOfType<OrbitSlot>(true).OrderBy(s => s.Index).ToArray();

        foreach (var p in planets)
            if (p && p.manager != this) p.manager = this;

        Debug.Log($"[GM RebindAll][SERVER] planets={planets.Length}, slots={slots.Length}");
    }

    // client: isi arrays (tanpa set manager)
    void RebindAllLocal()
    {
        if (planets == null || planets.Length == 0)
            planets = FindObjectsOfType<Planet>(true);

        if (slots == null || slots.Length == 0)
            slots = FindObjectsOfType<OrbitSlot>(true).OrderBy(s => s.Index).ToArray();

        Debug.Log($"[GM RebindAllLocal][CLIENT] planets={planets.Length}, slots={slots.Length}");
    }

    [ContextMenu("DEBUG/Print Binding")]
    void DebugPrintBinding()
    {
        Debug.Log($"[GM {name}] planets={planets?.Length ?? 0}, slots={slots?.Length ?? 0}");
        if (planets != null)
            foreach (var p in planets)
                if (p) Debug.Log($" - {p.PlanetName} mgr={(p.manager ? p.manager.name : "NULL")} idx={p.CurrentOrbitIndex.Value}");
    }

    // ---------- Ronde ----------
    [ServerRpc(RequireOwnership = false)]
    public void StartRoundServerRpc()
    {
        if (!IsServer) return;
        if (phase.Value != GamePhase.Lobby && phase.Value != GamePhase.Result) return;
        ResetRoundServer();
        phase.Value = GamePhase.Playing;
    }

    void ResetRoundServer()
    {
        if (planets == null) RebindAll();

        // acak posisi planet & reset state
        if (planets != null)
        {
            foreach (var p in planets)
            {
                if (!p) continue;
                var pos = RandomPointInArea(spawnArea);
                p.ResetServer(pos);
            }
        }

        // reset visual
        ClearAllSlotVisualsClientRpc();

        teamScore.Value = 0;
        timeLeft.Value = roundSeconds;
        phase.Value = GamePhase.Lobby;

        _buttonShown = false;
        _allSnappedCached = false;
        // ShowCheckButtonClientRpc(false);
    }

    Vector3 RandomPointInArea(Transform area)
    {
        var size = new Vector3(2, 0.5f, 2);
        var local = new Vector3(
            Random.Range(-size.x * .5f, size.x * .5f),
            Random.Range(0, size.y),
            Random.Range(-size.z * .5f, size.z * .5f)
        );
        return area ? area.TransformPoint(local) : local;
    }

    void Update()
    {
        if (!IsServer) return;

        if (phase.Value == GamePhase.Playing)
        {
            timeLeft.Value = Mathf.Max(0, timeLeft.Value - Time.deltaTime);

            // tombol muncul jika semua planet sudah tersnap (index > 0)
            if (!_allSnappedCached && planets != null && planets.Length > 0)
            {
                _allSnappedCached = planets.All(p => p && p.CurrentOrbitIndex.Value > 0);
                if (_allSnappedCached && !_buttonShown)
                {
                    _buttonShown = true;
                    ShowCheckButtonClientRpc(true);
                    PlayOneShotClientRpc(0);   // all snapped
                }
            }
        }
    }

    // ---------- UI: Tombol Cek ----------
    public void OnClickCheckOrder()
    {
        if (NetworkObject && NetworkObject.IsSpawned)
        {
            if (IsServer) DoCheckAndColorizeServer();
            else DoCheckAndColorizeServerRpc();
        }
        else
        {
            // FALLBACK lokal: warna di klien saja (tidak sync)
            Debug.LogWarning("[GM] Not spawned. Running local check (no networking).");
            RebindAllLocal();
            DoCheckAndColorizeLocal();
        }
    }

    void DoCheckAndColorizeLocal()
    {
        if (slots == null || planets == null) return;

        foreach (var slot in slots)
        {
            var p = planets.FirstOrDefault(pl => pl && pl.CurrentOrbitIndex.Value == slot.Index);
            bool isCorrect = (p != null) && (p.IdUrutanBenar == slot.Index);
            slot.ApplyResultColor(isCorrect);  // langsung lokal, tanpa RPC
        }
    }


    [ServerRpc(RequireOwnership = false)]
    void DoCheckAndColorizeServerRpc() => DoCheckAndColorizeServer();

    void DoCheckAndColorizeServer()
    {
        if (slots == null || slots.Length == 0 || planets == null || planets.Length == 0)
        {
            Debug.LogWarning("[GM] DoCheck: slots/planets kosong. RebindAll() dulu.");
            RebindAll();
            if (slots == null || planets == null) return;
        }

        Debug.Log("[Check] SERVER snapshot: " + string.Join(", ",
            planets.Where(p => p).Select(p => $"{p.PlanetName}:{p.CurrentOrbitIndex.Value}")));

        bool allCorrect = true;

        foreach (var slot in slots)
        {
            if (!slot) continue;

            // cari planet yang sedang di slot.Index
            var p = planets.FirstOrDefault(pl => pl && pl.CurrentOrbitIndex.Value == slot.Index);
            bool isCorrect = (p != null) && (p.IdUrutanBenar == slot.Index);

            if (!isCorrect) allCorrect = false;

            // kirim ke semua client untuk warnai slot (pakai INDEX)
            SetSlotResultClientRpc(slot.Index, isCorrect);

            Debug.Log($"[Check] Slot {slot.Index} => {(p ? p.PlanetName : "NONE")}, " +
                      $"current={(p ? p.CurrentOrbitIndex.Value : 0)}, " +
                      $"correct={(p ? p.IdUrutanBenar : -1)}");
        }

        PlayOneShotClientRpc(allCorrect ? 1 : 2);
    }

    // ---------- Client RPCs ----------
    [ClientRpc]
    void ShowCheckButtonClientRpc(bool show)
    {
        if (checkButton) checkButton.SetActive(show);
    }

    [ClientRpc]
    void ClearAllSlotVisualsClientRpc()
    {
        if (slots == null) return;
        foreach (var s in slots)
            if (s) s.ClearResultLock();   // reset warna & buka kunci hasil
    }

    // isCorrect=true → hijau; false → merah
    [ClientRpc]
    void SetSlotResultClientRpc(int slotIndex, bool isCorrect)
    {
        if (slots == null) return;
        var slot = slots.FirstOrDefault(s => s && s.Index == slotIndex);
        if (!slot) return;
        slot.ApplyResultColor(isCorrect);
    }

    // 0 = allSnapped, 1 = correct, 2 = wrong
    [ClientRpc]
    void PlayOneShotClientRpc(int clipIndex)
    {
        if (!audioSource)
        {
            if (!_warnedNoAudioSource)
            {
                Debug.LogWarning("[SolarGameManager] AudioSource belum di-assign. Audio dilewati.");
                _warnedNoAudioSource = true;
            }
            return;
        }

        AudioClip clip = null;
        switch (clipIndex)
        {
            case 0:
                clip = sfxAllSnapped;
                if (!clip && !_warnedMissingAllSnapped)
                { Debug.LogWarning("[SolarGameManager] sfxAllSnapped belum di-assign."); _warnedMissingAllSnapped = true; }
                break;
            case 1:
                clip = sfxCorrect;
                if (!clip && !_warnedMissingCorrect)
                { Debug.LogWarning("[SolarGameManager] sfxCorrect belum di-assign."); _warnedMissingCorrect = true; }
                break;
            case 2:
                clip = sfxWrong;
                if (!clip && !_warnedMissingWrong)
                { Debug.LogWarning("[SolarGameManager] sfxWrong belum di-assign."); _warnedMissingWrong = true; }
                break;
            default:
                Debug.LogWarning($"[SolarGameManager] clipIndex tidak dikenal: {clipIndex}");
                return;
        }
        if (!clip) return;
        audioSource.PlayOneShot(clip);
    }

    // ---------- (Opsional) Back-compat ----------
    [ServerRpc(RequireOwnership = false)]
    public void ForceVerifyServerRpc() => DoCheckAndColorizeServer();
}
