using Unity.Netcode;
using UnityEngine;
using System.Linq;

public enum GamePhase { Lobby, Playing, Scoring, Result }

public class SolarGameManager : NetworkBehaviour
{
    [Header("Config")]
    public float roundSeconds = 180f;
    public Transform spawnArea;          // area acak untuk spawn planet (opsional)
    public Planet[] planets;             // diisi OrbitGenerator.AutoAssignToManager()
    public OrbitSlot[] slots;            // diisi OrbitGenerator.AutoAssignToManager()

    [Header("State (Network)")]
    public NetworkVariable<float> timeLeft =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<GamePhase> phase =
        new(GamePhase.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> teamScore = new(0);

    private bool _allPlacedCached;

    public override void OnNetworkSpawn()
    {
        if (IsServer) ResetRoundServer();
    }

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
        if (planets == null || planets.Length == 0) return;

        // reset posisi & orbit index
        foreach (var p in planets)
        {
            var pos = spawnArea ? RandomPointInArea(spawnArea) : p.transform.position;
            p.ResetServer(pos);
            p.SetOrbitIndex(0); // penting: kosongkan orbit saat start
        }

        if (slots != null)
        {
            foreach (var s in slots) s.ClearHighlight();
        }

        teamScore.Value = 0;
        timeLeft.Value = roundSeconds;
        _allPlacedCached = false;
        phase.Value = GamePhase.Lobby;
    }

    public bool CheckOrderServer(out Planet[] wrongOnes)
    {
        bool allCorrect = true;
        var wrong = new System.Collections.Generic.List<Planet>();

        foreach (var p in planets)
        {
            if (p.CurrentOrbitIndex.Value != p.IdUrutanBenar)
            {
                allCorrect = false;
                wrong.Add(p);
                Debug.Log($"{p.PlanetName} salah: di orbit {p.CurrentOrbitIndex.Value}, harusnya {p.IdUrutanBenar}");
            }
            else
            {
                Debug.Log($"{p.PlanetName} benar di Orbit {p.CurrentOrbitIndex.Value}");
            }
        }

        wrongOnes = wrong.ToArray();
        return allCorrect;
    }

    void Update()
    {
        if (!IsServer) return;
        if (phase.Value != GamePhase.Playing) return;

        timeLeft.Value = Mathf.Max(0, timeLeft.Value - Time.deltaTime);

        // semua planet dianggap "terpasang" jika CurrentOrbitIndex > 0
        if (!_allPlacedCached)
        {
            _allPlacedCached = planets != null && planets.Length > 0 &&
                               planets.All(p => p.CurrentOrbitIndex.Value > 0);
        }

        // kondisi selesai
        if (timeLeft.Value <= 0f || _allPlacedCached)
        {
            DoScoringServer();
        }
    }

    void DoScoringServer()
    {
        if (!IsServer) return;
        phase.Value = GamePhase.Scoring;

        Planet[] wrong;
        bool allCorrect = CheckOrderServer(out wrong);

        int correct = planets.Length - wrong.Length;
        int score = correct * 10;
        if (allCorrect && timeLeft.Value > 0f)
            score += Mathf.RoundToInt(timeLeft.Value / 5f); // bonus sisa waktu

        teamScore.Value = score;

        // Kirim feedback ke semua client
        var wrongIds = wrong.Select(p => p.NetworkObjectId).ToArray();
        ShowResultClientRpc(correct, teamScore.Value, timeLeft.Value, wrongIds);

        phase.Value = GamePhase.Result;
    }

    [ClientRpc]
    void ShowResultClientRpc(int correctCount, int totalScore, float timeLeftAtEnd, ulong[] wrongPlanetIds)
    {
        Debug.Log($"[RESULT] Benar: {correctCount}/{planets.Length}, Skor: {totalScore}, Sisa: {timeLeftAtEnd:F1}s");

        // Highlight planet salah (client-side visual only)
        foreach (var id in wrongPlanetIds)
        {
            var obj = NetworkManager.Singleton?.SpawnManager?.SpawnedObjects.TryGetValue(id, out var netObj) == true
                      ? netObj.gameObject : null;
            if (!obj) continue;

            // contoh: ubah warna renderer sebentar (silakan ganti ke sistem highlight-mu)
            var rend = obj.GetComponentInChildren<Renderer>();
            if (rend) StartCoroutine(BlinkRed(rend, 0.6f));
        }
    }

    System.Collections.IEnumerator BlinkRed(Renderer r, float dur)
    {
        var mats = r.materials;
        var oldColors = mats.Select(m => m.HasProperty("_Color") ? m.color : Color.white).ToArray();

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float f = Mathf.PingPong(t * 6f, 1f);
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i].HasProperty("_Color"))
                    mats[i].color = Color.Lerp(oldColors[i], Color.red, f);
            }
            yield return null;
        }

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i].HasProperty("_Color"))
                mats[i].color = oldColors[i];
        }
    }

    Vector3 RandomPointInArea(Transform area)
    {
        // fallback box 2x0.5x2 kalau tidak ada collider khusus
        var size = new Vector3(2, 0.5f, 2);
        var local = new Vector3(
            Random.Range(-size.x * .5f, size.x * .5f),
            Random.Range(0, size.y),
            Random.Range(-size.z * .5f, size.z * .5f)
        );
        return area.TransformPoint(local);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ForceVerifyServerRpc()
    {
        if (phase.Value == GamePhase.Playing) DoScoringServer();
    }

  

}
