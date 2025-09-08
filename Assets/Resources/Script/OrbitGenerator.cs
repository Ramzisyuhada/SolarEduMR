using UnityEngine;
using System.Linq;

[ExecuteInEditMode]
public class OrbitGenerator : MonoBehaviour
{
    [Header("TableSystem / Tabletop")]
    public Transform tableRoot;
    public Collider tableCollider;
    public MeshRenderer tableRenderer;
    public Vector2 tableSizeXZOverride;
    public float topSurfaceYOffset = 0.01f;
    public float tableMargin = 0.08f;

    [Header("Orbit Config (Fit to Table jika aktif)")]
    public bool fitToTable = true;
    public int jumlahOrbit = 8;
    [Tooltip("Kalau fitToTable=false, manual radius awal")]
    public float radiusAwal = 1.0f;
    [Tooltip("Kalau fitToTable=false, manual jarak antar orbit")]
    public float jarakAntarOrbit = 0.5f;
    public int segmentsLingkaran = 64;

    [Header("Parents")]
    public Transform orbitParent;
    public Transform planetsParent;

    [Header("Prefabs Orbit")]
    public GameObject defaultOrbitSlotPrefab;
    public GameObject[] orbitSlotPrefabs; // orbit berbeda → prefab berbeda

    [Header("Prefabs Planet")]
    public GameObject defaultPlanetPrefab;
    public GameObject[] planetPrefabs;    // tiap orbit bisa beda prefab

    [Header("Planet Options")]
    public bool placePlanetsOnSnapPoint = false;
    public float scatterRadius = 0.6f;
    public float randomY = 0.0f;
    public float defaultPlanetScale = 0.25f;
    public string[] planetNames = new string[]
    { "Merkurius","Venus","Bumi","Mars","Jupiter","Saturnus","Uranus","Neptunus" };

    [Header("Auto-Link")]
    public SolarGameManager gameManager;

    // ======== PUBLIC BUTTONS ========
    public void GenerateAll() { PrepareParentsAndFit(); GenerateOrbitsInternal(); GeneratePlanetsInternal(); AutoAssignToManager(); }
    public void GenerateOrbits() { PrepareParentsAndFit(); GenerateOrbitsInternal(); }
    public void GeneratePlanets() { PrepareParentsAndFit(); GeneratePlanetsInternal(); AutoAssignToManager(); }

    public void AutoAssignToManager()
    {
        var mgr = GetOrFindManager(); if (!mgr) return;
        var planets = planetsParent.GetComponentsInChildren<Planets>(true);
        var slots = orbitParent.GetComponentsInChildren<OrbitSlot>(true);
        mgr.planets = planets.OrderBy(p => p.IdUrutanBenar).ToArray();
        mgr.slots = slots.OrderBy(s => s.Index).ToArray();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(mgr);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
#endif
        Debug.Log($"[OrbitGenerator] Assign ke GameManager: {mgr.planets.Length} planets, {mgr.slots.Length} slots");
    }

    // ======== CORE ========
    void PrepareParentsAndFit()
    {
        EnsureParents();

        if (fitToTable)
        {
            Vector3 center; Vector2 sizeXZ; float topY;
            GetTableInfo(out center, out sizeXZ, out topY);

            orbitParent.position = new Vector3(center.x, topY + topSurfaceYOffset, center.z);
            orbitParent.rotation = Quaternion.Euler(0f, tableRoot ? tableRoot.eulerAngles.y : 0f, 0f);

            float maxRadius = Mathf.Max(0.01f, Mathf.Min(sizeXZ.x, sizeXZ.y) * 0.5f - tableMargin);
            float step = maxRadius / Mathf.Max(1, jumlahOrbit);
            radiusAwal = step;
            jarakAntarOrbit = step;
        }
    }

    void GenerateOrbitsInternal()
    {
        for (int i = orbitParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(orbitParent.GetChild(i).gameObject);

        for (int i = 0; i < jumlahOrbit; i++)
        {
            float radius = radiusAwal + i * jarakAntarOrbit;
            GameObject orbitGO = new GameObject($"OrbitSlot_{i + 1}");
            orbitGO.transform.SetParent(orbitParent);
            orbitGO.transform.localPosition = Vector3.zero;
            orbitGO.transform.localRotation = Quaternion.identity;

            // LineRenderer orbit
            var line = orbitGO.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = 0.01f;
            line.positionCount = Mathf.Max(segmentsLingkaran, 32);
            for (int j = 0; j < line.positionCount; j++)
            {
                float angle = j * 2 * Mathf.PI / line.positionCount;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                line.SetPosition(j, pos);
            }

            // SnapPoint
            GameObject snapPoint = new GameObject("SnapPoint");
            snapPoint.transform.SetParent(orbitGO.transform);
            snapPoint.transform.localPosition = new Vector3(radius, 0, 0);
            snapPoint.transform.localRotation = Quaternion.LookRotation(snapPoint.transform.localPosition.normalized, Vector3.up);

            // OrbitSlot component
            var slot = orbitGO.AddComponent<OrbitSlot>();
            slot.Index = i + 1;
            slot.SnapPoint = snapPoint.transform;

            // Prefab visual per orbit
            GameObject orbitPrefab = null;
            if (orbitSlotPrefabs != null && i < orbitSlotPrefabs.Length && orbitSlotPrefabs[i])
                orbitPrefab = orbitSlotPrefabs[i];
            else if (defaultOrbitSlotPrefab)
                orbitPrefab = defaultOrbitSlotPrefab;

            if (orbitPrefab)
            {
                var visual = Instantiate(orbitPrefab, orbitGO.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
            }
        }
    }

    void GeneratePlanetsInternal()
    {
        if ((planetPrefabs == null || planetPrefabs.Length == 0) && !defaultPlanetPrefab)
        {
            Debug.LogError("[OrbitGenerator] Planet Prefab belum diisi.");
            return;
        }

        for (int i = planetsParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(planetsParent.GetChild(i).gameObject);

        int count = Mathf.Min(jumlahOrbit, planetNames != null ? planetNames.Length : jumlahOrbit);
        var slots = orbitParent.GetComponentsInChildren<OrbitSlot>(true).OrderBy(s => s.Index).ToArray();

        for (int idx = 0; idx < count; idx++)
        {
            string pName = planetNames != null && idx < planetNames.Length ? planetNames[idx] : $"Planet_{idx + 1}";

            // prefab per orbit → fallback ke default
            GameObject prefabToUse = null;
            if (planetPrefabs != null && idx < planetPrefabs.Length && planetPrefabs[idx])
                prefabToUse = planetPrefabs[idx];
            else
                prefabToUse = defaultPlanetPrefab;

            if (!prefabToUse)
            {
                Debug.LogWarning($"[OrbitGenerator] Orbit {idx + 1} tidak ada prefab planet.");
                continue;
            }

            var go = Instantiate(prefabToUse, planetsParent);
            go.name = $"Planet_{idx + 1}_{pName}";
            go.transform.localScale = Vector3.one * defaultPlanetScale;

            // posisi awal
            Vector3 pos; Quaternion rot;
            if (placePlanetsOnSnapPoint && slots.Length > idx && slots[idx].SnapPoint)
            {
                pos = slots[idx].SnapPoint.position;
                rot = slots[idx].SnapPoint.rotation;
            }
            else
            {
                Vector2 r = Random.insideUnitCircle * scatterRadius;
                pos = new Vector3(orbitParent.position.x + r.x, orbitParent.position.y, orbitParent.position.z + r.y);
                rot = Quaternion.identity;
            }

            go.transform.position = pos;
            go.transform.rotation = rot;

            // Planet.cs config
            var planet = go.GetComponent<Planets>();
            if (planet)
            {
                planet.PlanetName = pName;
                planet.IdUrutanBenar = idx + 1;
                if (!planet.manager) planet.manager = GetOrFindManager();
#if UNITY_EDITOR
                planet.NetPos.Value = go.transform.position;
                planet.NetRot.Value = go.transform.rotation;
#endif
            }
        }
    }

    void GetTableInfo(out Vector3 center, out Vector2 sizeXZ, out float topY)
    {
        if (tableCollider)
        {
            var b = tableCollider.bounds;
            center = b.center;
            sizeXZ = new Vector2(b.size.x, b.size.z);
            topY = b.max.y; return;
        }
        if (tableRenderer)
        {
            var b = tableRenderer.bounds;
            center = b.center;
            sizeXZ = new Vector2(b.size.x, b.size.z);
            topY = b.max.y; return;
        }
        if (tableSizeXZOverride != Vector2.zero && tableRoot)
        {
            center = tableRoot.position;
            sizeXZ = tableSizeXZOverride;
            topY = tableRoot.position.y; return;
        }

        var t = tableRoot ? tableRoot : transform;
        center = t.position;
        sizeXZ = new Vector2(2f, 2f);
        topY = t.position.y;
    }

    void EnsureParents()
    {
        if (!orbitParent)
        {
            var orbits = transform.Find("Orbits");
            if (!orbits) orbits = new GameObject("Orbits").transform;
            orbits.SetParent(transform);
            orbitParent = orbits;
        }
        if (!planetsParent)
        {
            var pls = transform.Find("Planets");
            if (!pls) pls = new GameObject("Planets").transform;
            pls.SetParent(transform);
            planetsParent = pls;
        }
        orbitParent.localPosition = Vector3.zero; orbitParent.localRotation = Quaternion.identity;
        planetsParent.localPosition = Vector3.zero; planetsParent.localRotation = Quaternion.identity;
    }

    SolarGameManager GetOrFindManager()
    {
        if (!gameManager) gameManager = FindObjectOfType<SolarGameManager>();
        return gameManager;
    }
}
