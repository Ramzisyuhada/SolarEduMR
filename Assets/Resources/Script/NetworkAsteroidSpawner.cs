// NetworkAsteroidSpawner.cs
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkAsteroidSpawner : NetworkBehaviour
{
    [Header("Target World Center (mis. meja MR / anchor)")]
    public Transform worldCenter;

    [Header("Spawn Ring")]
    public float innerRadius = 12f;
    public float outerRadius = 16f;
    public float spawnHeight = 2.0f;     // offset Y dari center
    public float killDistance = 80f;     // diteruskan ke asteroid (optional override di prefab)

    [Header("Asteroid Prefabs (NetworkObject)")]
    public NetworkObject[] asteroidPrefabs;

    [Header("Spawn Rules")]
    public int maxAlive = 25;
    public float spawnInterval = 1.2f;
    public Vector2 speedRange = new Vector2(3f, 7f);
    public Vector2 scaleRange = new Vector2(0.7f, 2.3f);
    public Vector2 angularDegPerSec = new Vector2(20f, 80f); // kecepatan rotasi acak
    public Vector2 lifeTimeRange = new Vector2(18f, 36f);

    [Header("Aim")]
    public float aimCenterBias = 0.5f; // 0=acak bebas, 1=arah tepat ke center
    public float verticalNoise = 0.25f;

    float _timer;
    readonly List<NetworkObject> _alive = new();

    void Reset()
    {
        worldCenter = null;
        innerRadius = 12f; outerRadius = 16f;
        spawnHeight = 2f;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        enabled = IsServer; // spawner cuma jalan di server
    }

    void Update()
    {
        if (!IsServer) return;
        if (asteroidPrefabs == null || asteroidPrefabs.Length == 0) return;

        // bersihkan list kalau ada yang sudah despawn
        _alive.RemoveAll(no => no == null || !no.IsSpawned);

        _timer += Time.deltaTime;
        if (_alive.Count < maxAlive && _timer >= spawnInterval)
        {
            _timer = 0f;
            SpawnOne();
        }
    }

    void SpawnOne()
    {
        var centerPos = worldCenter ? worldCenter.position : Vector3.zero;

        // posisi acak di ring
        float r = Random.Range(innerRadius, outerRadius);
        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 pos = centerPos + new Vector3(Mathf.Cos(angle) * r, spawnHeight, Mathf.Sin(angle) * r);

        // arah: menuju center + sedikit noise
        Vector3 toCenter = (centerPos - pos).normalized;
        Vector3 rand = Random.insideUnitSphere; rand.y *= verticalNoise;
        Vector3 dir = Vector3.Slerp(rand.normalized, toCenter, Mathf.Clamp01(aimCenterBias)).normalized;

        float speed = Random.Range(speedRange.x, speedRange.y);
        Vector3 velocity = dir * speed;

        // angular vel (deg/s → rad/s di asteroid)
        Vector3 angVel = Random.onUnitSphere * Random.Range(angularDegPerSec.x, angularDegPerSec.y);

        float scale = Random.Range(scaleRange.x, scaleRange.y);
        float life = Random.Range(lifeTimeRange.x, lifeTimeRange.y);

        // pick prefab
        var prefab = asteroidPrefabs[Random.Range(0, asteroidPrefabs.Length)];
        var no = Instantiate(prefab, pos, Quaternion.LookRotation(dir, Vector3.up));

        no.Spawn(true); // broadcast ke klien
        _alive.Add(no);

        // init komponen asteroid
        var ast = no.GetComponent<NetworkAsteroid>();
        if (ast)
        {
            ast.ServerInit(worldCenter, pos, velocity, angVel, scale, -1f);
            // override life & kill distance bila perlu:
            OverrideLifeAndKill(ast, life, killDistance);
        }
    }

    void OverrideLifeAndKill(NetworkAsteroid ast, float life, float killDist)
    {
        // akses via refleksi kecil (atau tambahkan setter publik jika mau)
        var lifeField = typeof(NetworkAsteroid).GetField("lifeSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (lifeField != null) lifeField.SetValue(ast, life);

        var killField = typeof(NetworkAsteroid).GetField("killDistance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (killField != null) killField.SetValue(ast, killDist);
    }
}
