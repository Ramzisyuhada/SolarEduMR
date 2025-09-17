// NetworkUFONPC.cs
using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
public class NetworkUFONPC : NetworkBehaviour
{
    public enum NPCState : byte { Idle = 0, Patrol = 1, Orbit = 2, Chase = 3 }

    [Header("References")]
    [Tooltip("Child transform untuk visual (akan di-tilt/bobbing). Bisa kosong: pakai transform ini.")]
    public Transform visualRoot;
    [Tooltip("VFX untuk beam (akan di-SetActive sesuai state).")]
    public GameObject beamVfx;
    [Tooltip("SFX untuk beam (akan Play/Stop sesuai state).")]
    public AudioSource beamAudio;

    [Header("Movement")]
    public float moveSpeed = 6f;
    public float turnSpeed = 140f;      // derajat/detik (yaw)
    public float ascendSpeed = 3f;      // naik/turun untuk bob/orbit/kejar
    [Tooltip("Acceleration lerp [0..1] agar halus.")]
    [Range(0.01f, 1f)] public float accelLerp = 0.2f;

    [Header("Patrol Waypoints (opsional)")]
    public Transform[] waypoints;
    public bool loopPatrol = true;
    public float waypointReachRadius = 0.5f;

    [Header("Orbit (fallback jika waypoint kosong)")]
    public Transform orbitCenter;       // kalau null, pakai posisi spawn
    public float orbitRadius = 4f;
    public float orbitAngularSpeed = 30f; // deg/s
    public float orbitHeight = 2f;

    [Header("Chase (opsional)")]
    public bool enableChase = false;
    public string chaseTag = "Player";
    public float detectRadius = 8f;
    public float stopChaseDistance = 10f;

    [Header("Beam Logic")]
    public bool pulseBeamAtWaypoint = true;
    public float beamPulseDuration = 1.5f;
    public float beamRange = 2.0f;      // nyala beam saat dekat target

    [Header("Visual Flair")]
    public bool enableTilt = true;
    public float maxTiltDeg = 10f;      // roll/pitch maksimum
    public bool enableBob = true;
    public float bobAmplitude = 0.15f;
    public float bobSpeed = 2.2f;

    [Header("Debug/State")]
    [SerializeField, Tooltip("State runtime (disinkron agar klien tahu).")]
    private NetworkVariable<byte> stateNet = new((byte)NPCState.Patrol,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField]
    private NetworkVariable<bool> beamOn = new(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Cache & runtime
    Rigidbody rb;
    Vector3 spawnPos;
    Quaternion visualBaseRot;
    Vector3 visualBaseLocalPos;
    int wpIndex;
    float beamTimer; // sisa durasi pulse
    Transform chaseTarget; // server-side only

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (!visualRoot) visualRoot = transform;
        visualBaseRot = visualRoot.localRotation;
        visualBaseLocalPos = visualRoot.localPosition;

        spawnPos = transform.position;
        if (!orbitCenter)
        {
            var center = new GameObject(name + "_OrbitCenter").transform;
            center.position = spawnPos;
            orbitCenter = center;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Terapkan beam awal di semua peer
        ApplyBeam(beamOn.Value);
        beamOn.OnValueChanged += (_, curr) => ApplyBeam(curr);
    }

    public override void OnNetworkDespawn()
    {
        beamOn.OnValueChanged -= (_, __) => ApplyBeam(__);
        base.OnNetworkDespawn();
    }

    void FixedUpdate()
    {
        // Jalan offline (Editor Play tanpa Netcode) ATAU server authoritative
        bool actLikeServer = !IsSpawned || (NetworkManager.Singleton && !NetworkManager.Singleton.IsListening);
        if (!(IsServer || actLikeServer)) return;

        var state = (NPCState)stateNet.Value;

        // Optional: cari target untuk Chase
        if (enableChase)
        {
            // kehilangan target?
            if (chaseTarget && Vector3.SqrMagnitude(chaseTarget.position - transform.position) > stopChaseDistance * stopChaseDistance)
                chaseTarget = null;

            if (!chaseTarget)
            {
                // deteksi sekitar
                var cols = Physics.OverlapSphere(transform.position, detectRadius);
                float best = float.MaxValue;
                Transform bestT = null;
                foreach (var c in cols)
                {
                    if (!string.IsNullOrEmpty(chaseTag) && !c.CompareTag(chaseTag)) continue;
                    float d = (c.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; bestT = c.transform; }
                }
                if (bestT) { chaseTarget = bestT; SetState(NPCState.Chase); }
            }
        }

        switch (state)
        {
            case NPCState.Idle:
                ServerHoverIdle();
                break;
            case NPCState.Patrol:
                if (waypoints != null && waypoints.Length > 0) ServerPatrol();
                else ServerOrbit();
                break;
            case NPCState.Orbit:
                ServerOrbit();
                break;
            case NPCState.Chase:
                if (chaseTarget) ServerChase();
                else SetState(waypoints != null && waypoints.Length > 0 ? NPCState.Patrol : NPCState.Orbit);
                break;
        }

        // beam pulse countdown
        if (beamTimer > 0f)
        {
            beamTimer -= Time.fixedDeltaTime;
            if (beamTimer <= 0f) beamOn.Value = false;
        }
    }

    void Update()
    {
        // visual only (klien dan server sama-sama jalankan)
        UpdateVisualTiltAndBob();
    }

    // ---------------- Server Behaviours ----------------

    void ServerHoverIdle()
    {
        // hanya bob kecil: velocity ke nol
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, accelLerp);
        // rotasi pelan stabil
        var targetRot = Quaternion.Euler(0f, transform.eulerAngles.y + (turnSpeed * 0.15f * Time.fixedDeltaTime), 0f);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeed * Time.fixedDeltaTime));
    }

    void ServerPatrol()
    {
        var target = waypoints[wpIndex] ? waypoints[wpIndex].position : transform.position;
        MoveTowards(target);

        float dist = Vector3.Distance(transform.position, target);
        if (dist <= waypointReachRadius)
        {
            // beam pulse di waypoint
            if (pulseBeamAtWaypoint) PulseBeam();

            // next waypoint
            wpIndex++;
            if (wpIndex >= waypoints.Length)
            {
                wpIndex = loopPatrol ? 0 : waypoints.Length - 1;
                if (!loopPatrol) SetState(NPCState.Orbit);
            }
        }
    }

    void ServerOrbit()
    {
        Vector3 center = orbitCenter ? orbitCenter.position : spawnPos;
        Vector3 to = transform.position - center;
        if (to.sqrMagnitude < 0.01f) to = Vector3.right * orbitRadius;
        to = to.normalized * orbitRadius;

        // target offset berputar
        Quaternion rot = Quaternion.AngleAxis(orbitAngularSpeed * Time.fixedDeltaTime, Vector3.up);
        Vector3 nextOffset = rot * to;
        Vector3 target = center + nextOffset + Vector3.up * orbitHeight;

        MoveTowards(target);
    }

    void ServerChase()
    {
        if (!chaseTarget) return;
        Vector3 target = chaseTarget.position + Vector3.up * 1.2f; // sedikit di atas target
        MoveTowards(target);

        // Nyala beam kalau dekat
        if ((target - transform.position).sqrMagnitude < beamRange * beamRange)
            beamOn.Value = true;
        else if (beamTimer <= 0f)
            beamOn.Value = false;
    }

    void MoveTowards(Vector3 worldTarget)
    {
        Vector3 to = worldTarget - transform.position;
        Vector3 dir = to.normalized;

        // bikin kecepatan lokal
        Vector3 localVel = new Vector3(
            0f,                                  // strafe X bisa 0 biar mudah dikontrol
            Mathf.Clamp(to.y, -1f, 1f) * ascendSpeed,
            moveSpeed
        );
        // arahkan ke target pada bidang horizontal
        Quaternion look = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z).sqrMagnitude > 1e-4f ? new Vector3(dir.x, 0f, dir.z) : transform.forward, Vector3.up);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, look, turnSpeed * Time.fixedDeltaTime));

        // world velocity: maju ke depan + naik/turun
        Vector3 desired = transform.TransformDirection(localVel);
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, desired, accelLerp);
    }

    void PulseBeam()
    {
        beamOn.Value = true;
        beamTimer = beamPulseDuration;
    }

    // ---------------- Visual (client & server) ----------------

    void UpdateVisualTiltAndBob()
    {
        if (!visualRoot) return;

        // Tilt berdasarkan velocity (klien punyai rb interpolated dari NetworkTransform)
        Vector3 v = rb ? rb.linearVelocity : Vector3.zero;
        if (enableTilt)
        {
            float forwardSpeed = Vector3.Dot(v, transform.forward);
            float strafeSpeed = Vector3.Dot(v, transform.right);
            float pitch = Mathf.Clamp(-forwardSpeed * 0.5f, -maxTiltDeg, maxTiltDeg); // maju → hidung turun sedikit
            float roll = Mathf.Clamp(-strafeSpeed * 0.5f, -maxTiltDeg, maxTiltDeg);  // geser kanan → roll kanan
            Quaternion tilt = Quaternion.Euler(pitch, 0f, roll);
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, visualBaseRot * tilt, 0.15f);
        }

        if (enableBob)
        {
            float t = Time.time * bobSpeed;
            float yOff = Mathf.Sin(t) * bobAmplitude;
            Vector3 p = visualBaseLocalPos; p.y += yOff;
            visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, p, 0.2f);
        }
    }

    void ApplyBeam(bool on)
    {
        if (beamVfx) beamVfx.SetActive(on);
        if (beamAudio)
        {
            if (on) { if (!beamAudio.isPlaying) beamAudio.Play(); }
            else { if (beamAudio.isPlaying) beamAudio.Stop(); }
        }
    }

    // ---------------- Public (server-only setters) ----------------
    public void SetState(NPCState s)
    {
        if (IsServer || !IsSpawned) stateNet.Value = (byte)s;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        turnSpeed = Mathf.Max(10f, turnSpeed);
        ascendSpeed = Mathf.Max(0f, ascendSpeed);
        orbitRadius = Mathf.Max(0.1f, orbitRadius);
        waypointReachRadius = Mathf.Max(0.05f, waypointReachRadius);
        beamPulseDuration = Mathf.Max(0f, beamPulseDuration);
    }
#endif
}
