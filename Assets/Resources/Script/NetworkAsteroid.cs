// NetworkAsteroid.cs
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
public class NetworkAsteroid : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] float maxHealth = 50f;
    [SerializeField] float lifeSeconds = 30f;
    [SerializeField] float killDistance = 80f;

    [Header("Visual & VFX")]
    [SerializeField] GameObject hitVfxPrefab;
    [SerializeField] GameObject breakVfxPrefab;
    [SerializeField] GameObject explosionVfxPrefab; // boleh kosong (pakai breakVfx)

    [Header("Touch (MR)")]
    [Tooltip("Paling gampang: sentuh apa saja langsung meledak (trigger/collision).")]
    [SerializeField] bool explodeOnAnyTouch = true;
    [Tooltip("Atau filter berdasarkan Tag: mis. Hand, XRController.")]
    [SerializeField] string[] explodeOnTags = new string[] { "Hand", "XRController" };
    [Tooltip("Atau filter berdasarkan Layer (set layer tangan).")]
    [SerializeField] LayerMask explodeOnLayers = ~0;
    [Tooltip("Untuk sentuhan collision: minimal impulse agar dianggap sentuh valid (0 = abaikan).")]
    [SerializeField] float minTouchImpulse = 0f;
    [Tooltip("Untuk sentuhan trigger: minimal kecepatan relatif (0 = abaikan).")]
    [SerializeField] float minRelativeSpeed = 0f;

    [Header("Server Validation")]
    [Tooltip("Jarak toleransi saat server menerima permintaan ledak dari client.")]
    [SerializeField] float serverAcceptDistance = 1.2f;

    [Header("Explosion Physics")]
    [SerializeField] bool explosionAffectsRigidbodies = true;
    [SerializeField] float explosionRadius = 3f;
    [SerializeField] float explosionForce = 8f;
    [SerializeField] bool chainReactAsteroids = true;

    [Header("(Legacy Projectile - opsional)")]
    [SerializeField] string projectileTag = "Projectile";
    [SerializeField] float projectileHitDamage = 25f;

    Rigidbody _rb;
    float _health;
    float _age;
    Transform _center;
    bool _initialized;

    // ---------------- Init ----------------
    public void ServerInit(Transform center, Vector3 startPos, Vector3 velocity, Vector3 angVel, float uniformScale, float hpOverride = -1f)
    {
        if (!IsServer) return;

        _center = center;
        transform.position = startPos;
        transform.localScale = Vector3.one * uniformScale;

        _rb.linearVelocity = velocity;
        _rb.angularVelocity = angVel * Mathf.Deg2Rad;

        _health = hpOverride > 0f ? hpOverride : maxHealth;
        _age = 0f;
        _initialized = true;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Physics authority di server; client tetap dapat event trigger lokal (kinematic) untuk kirim RPC
        _rb.isKinematic = !IsServer;
    }

    void FixedUpdate()
    {
        if (!IsServer || !_initialized) return;

        _age += Time.fixedDeltaTime;
        if (_age >= lifeSeconds) { ServerDespawn(); return; }

        if (_center)
        {
            float dist = Vector3.Distance(transform.position, _center.position);
            if (dist > killDistance) { ServerDespawn(); return; }
        }
    }

    // ---------------- TOUCH HANDLING ----------------
    // Penting: JANGAN return kalau !IsServer; di client kita pakai event ini untuk kirim RPC
    void OnTriggerEnter(Collider other)
    {
        // CLIENT: deteksi sentuh lokal oleh tangan → minta server meledakkan
        if (!IsServer && ShouldExplodeOnTouch(other, 0f))
        {
            RequestExplodeServerRpc(other.ClosestPoint(transform.position));
            return;
        }

        // SERVER: kalau mau, bisa juga langsung validasi via physics server (jika tangan di-network)
        if (IsServer && ShouldExplodeOnTouch(other, 0f))
        {
            Explode(other.ClosestPoint(transform.position));
            return;
        }

        // (Opsional) dukung projectile legacy
        if (!IsServer) return;
        float dmg = 0f;
        var dmgComp = other.GetComponent<IDealsDamage>();
        if (dmgComp != null) dmg = dmgComp.GetDamage();
        else if (!string.IsNullOrEmpty(projectileTag) && other.CompareTag(projectileTag)) dmg = projectileHitDamage;
        if (dmg > 0f) TakeDamage(dmg, other.ClosestPoint(transform.position));
    }

    void OnCollisionEnter(Collision c)
    {
        // CLIENT: tabrakan lokal oleh tangan/objek → kirim RPC kalau lolos filter
        if (!IsServer && ShouldExplodeOnTouch(c.collider, c.impulse.magnitude))
        {
            RequestExplodeServerRpc(c.contacts[0].point);
            return;
        }

        // SERVER: ledakkan bila tabrakan valid (kalau tangan di-network)
        if (IsServer && ShouldExplodeOnTouch(c.collider, c.impulse.magnitude))
        {
            Explode(c.contacts[0].point);
            return;
        }

        if (!IsServer) return;

        // Fallback damage dari impulse
        float impulse = c.impulse.magnitude;
        if (impulse > 1f) TakeDamage(Mathf.Clamp(impulse * 0.1f, 5f, 40f), c.contacts[0].point);
    }

    bool ShouldExplodeOnTouch(Collider other, float collisionImpulse)
    {
        if (explodeOnAnyTouch) return true;

        // Layer filter
        bool layerOk = ((1 << other.gameObject.layer) & explodeOnLayers) != 0;
        // Tag filter
        bool tagOk = false;
        if (explodeOnTags != null) foreach (var t in explodeOnTags)
                if (!string.IsNullOrEmpty(t) && other.CompareTag(t)) { tagOk = true; break; }

        if (!(layerOk || tagOk)) return false;

        // Kriteria kekuatan sentuhan
        if (collisionImpulse > 0f && collisionImpulse < minTouchImpulse) return false;
        if (collisionImpulse == 0f && minRelativeSpeed > 0f && other.attachedRigidbody)
        {
            var myVel = _rb ? _rb.linearVelocity : Vector3.zero;
            float rel = (myVel - other.attachedRigidbody.linearVelocity).magnitude;
            if (rel < minRelativeSpeed) return false;
        }
        return true;
    }

    // ---------------- DAMAGE / EXPLOSION (SERVER) ----------------
    void TakeDamage(float dmg, Vector3 hitPoint)
    {
        _health -= dmg;
        if (hitVfxPrefab) SpawnFxClientRpc(hitVfxPrefab.name, hitPoint);
        if (_health <= 0f) BreakAndDespawn();
    }

    void BreakAndDespawn()
    {
        if (breakVfxPrefab) SpawnFxClientRpc(breakVfxPrefab.name, transform.position);
        ServerDespawn();
    }

    void Explode(Vector3 at)
    {
        // VFX
        if (explosionVfxPrefab) SpawnFxClientRpc(explosionVfxPrefab.name, at);
        else if (breakVfxPrefab) SpawnFxClientRpc(breakVfxPrefab.name, at);

        // Physics area
        if (explosionAffectsRigidbodies && explosionRadius > 0f)
        {
            var cols = Physics.OverlapSphere(at, explosionRadius);
            foreach (var col in cols)
            {
                if (col.attachedRigidbody && col.attachedRigidbody != _rb)
                {
                    Vector3 dir = (col.transform.position - at);
                    float dist = Mathf.Max(dir.magnitude, 0.001f);
                    float falloff = Mathf.Clamp01(1f - dist / explosionRadius);
                    col.attachedRigidbody.AddForce(dir.normalized * (explosionForce * falloff), ForceMode.VelocityChange);
                }

                if (chainReactAsteroids && col.TryGetComponent(out NetworkAsteroid otherAst) && otherAst != this)
                {
                    otherAst.BreakAndDespawn();
                }
            }
        }

        ServerDespawn();
    }

    void ServerDespawn()
    {
        if (!IsServer) return;
        if (TryGetComponent(out NetworkObject no)) no.Despawn(); else Destroy(gameObject);
    }

    // ---------------- RPC: Client → Server minta ledak ----------------
    [ServerRpc(RequireOwnership = false)]
    void RequestExplodeServerRpc(Vector3 touchPoint, ServerRpcParams rpc = default)
    {
        // Validasi sederhana (anti spam/cheat): jarak titik sentuh ke posisi server harus wajar
        if ((touchPoint - transform.position).sqrMagnitude <= serverAcceptDistance * serverAcceptDistance)
        {
            Explode(touchPoint);
        }
        // else: abaikan
    }

    // ---------------- FX ke klien ----------------
    [ClientRpc]
    void SpawnFxClientRpc(string fxName, Vector3 at)
    {
        var prefab = AsteroidFxRegistry.Get(fxName);
        if (prefab)
        {
            var go = Object.Instantiate(prefab, at, Quaternion.identity);
            Object.Destroy(go, 2f);
        }
    }
}

// Opsional untuk peluru (abaikan jika tidak dipakai)
public interface IDealsDamage { float GetDamage(); }

// Registry sederhana untuk VFX (drag prefabs ke sini di satu GameObject di scene)
public class AsteroidFxRegistry : MonoBehaviour
{
    public GameObject[] prefabs; // nama prefab harus unik
    static AsteroidFxRegistry _inst;
    void Awake() { _inst = this; }
    public static GameObject Get(string name)
    {
        if (_inst == null || _inst.prefabs == null) return null;
        foreach (var p in _inst.prefabs) if (p && p.name == name) return p;
        return null;
    }
}
