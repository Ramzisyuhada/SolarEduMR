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

    [Header("Orbit Visual")]
    public Material orbitLineMaterial;                 // drag material URP Unlit / Built-in Unlit
    public Color orbitColor = new Color(1f, 0f, 0.9f); // warna garis orbit
    public float lineWidth = 0.01f;                    // ketebalan garis
    public bool billboardToCamera = true;              // LineRenderer alignment

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

    [Header("Planet Naming")]
    public bool gunakanNamaPrefab = true;  // true = ambil nama dari prefab; false = pakai planetNames[]
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
        var planets = planetsParent.GetComponentsInChildren<Planet>(true);
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
        // Bersihkan orbit lama
        for (int i = orbitParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(orbitParent.GetChild(i).gameObject);

        for (int i = 0; i < jumlahOrbit; i++)
        {
            float radius = radiusAwal + i * jarakAntarOrbit;

            // Root orbit
            GameObject orbitGO = new GameObject($"OrbitSlot_{i + 1}");
            orbitGO.transform.SetParent(orbitParent);
            orbitGO.transform.localPosition = Vector3.zero;
            orbitGO.transform.localRotation = Quaternion.identity;

            // Garis orbit (visual)
            var line = orbitGO.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = lineWidth;
            line.positionCount = Mathf.Max(segmentsLingkaran, 32);

            // material/warna/alignment (hindari pink di mobile/URP)
            line.material = EnsureOrbitMaterial();
            // set color untuk shader Unlit/Color; untuk URP/Unlit warna di _BaseColor (di-set saat buat material)
            line.startColor = orbitColor;
            line.endColor = orbitColor;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = billboardToCamera ? LineAlignment.View : LineAlignment.TransformZ;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (int j = 0; j < line.positionCount; j++)
            {
                float ang = j * 2 * Mathf.PI / line.positionCount;
                Vector3 p = new Vector3(Mathf.Cos(ang) * radius, 0, Mathf.Sin(ang) * radius);
                line.SetPosition(j, p);
            }

            // SnapPoint menghadap keluar lingkaran
            GameObject snapPoint = new GameObject("SnapPoint");
            snapPoint.transform.SetParent(orbitGO.transform);
            snapPoint.transform.localPosition = new Vector3(radius, 0, 0);
            snapPoint.transform.localRotation = Quaternion.LookRotation(snapPoint.transform.localPosition.normalized, Vector3.up);

            // Komponen OrbitSlot
            var slot = orbitGO.AddComponent<OrbitSlot>();
            slot.Index = i + 1;
            slot.SnapPoint = snapPoint.transform;

            // === Collider melingkar (SphereCollider rapat) ===
            const int colliderCount = 48;     // makin besar = makin rapat
            const float sphereRadius = 0.02f; // sesuaikan dg ukuran planet & akurasi snap
            float angleStep = 360f / colliderCount;

            for (int c = 0; c < colliderCount; c++)
            {
                float angDeg = c * angleStep;
                Vector3 localPos = Quaternion.Euler(0, angDeg, 0) * new Vector3(radius, 0, 0);

                GameObject colGO = new GameObject($"Collider_{c}");
                colGO.transform.SetParent(orbitGO.transform);
                colGO.transform.localPosition = localPos;
                colGO.transform.localRotation = Quaternion.identity;

                var sphere = colGO.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = sphereRadius;

                // Forward event trigger dari child ke OrbitSlot (parent)
                var fwd = colGO.AddComponent<OrbitTriggerForwarder>();
                fwd.parentSlot = slot;
            }

            // Prefab visual opsional per-orbit
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

                // Auto-assign renderer utk highlight bila kosong
                if (!slot.ringRenderer)
                {
                    var rend = visual.GetComponentInChildren<Renderer>();
                    if (rend) slot.ringRenderer = rend;
                }
            }
            else
            {
                if (!slot.ringRenderer)
                {
                    var rend = orbitGO.GetComponent<Renderer>();
                    if (rend) slot.ringRenderer = rend;
                }
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

        // bersihkan planet lama
        for (int i = planetsParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(planetsParent.GetChild(i).gameObject);

        int count = Mathf.Min(
            jumlahOrbit,
            Mathf.Max(
                planetNames != null ? planetNames.Length : 0,
                planetPrefabs != null ? planetPrefabs.Length : 0,
                jumlahOrbit
            )
        );

        var slots = orbitParent.GetComponentsInChildren<OrbitSlot>(true).OrderBy(s => s.Index).ToArray();

        // anti-duplikat nama
        var nameCounter = new System.Collections.Generic.Dictionary<string, int>();

        for (int idx = 0; idx < count; idx++)
        {
            // pilih prefab per orbit (urutan prefab = urutan benar)
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

            // tentukan nama planet
            string baseName = gunakanNamaPrefab
                ? CleanPrefabName(prefabToUse.name)
                : (planetNames != null && idx < planetNames.Length && !string.IsNullOrWhiteSpace(planetNames[idx]))
                    ? planetNames[idx]
                    : CleanPrefabName(prefabToUse.name);

            if (!nameCounter.ContainsKey(baseName)) nameCounter[baseName] = 0;
            nameCounter[baseName]++;
            string finalName = (nameCounter[baseName] > 1) ? $"{baseName}_{nameCounter[baseName]}" : baseName;

            // instantiate
            var go = Instantiate(prefabToUse, planetsParent);
            go.name = $"Planet_{idx + 1}_{finalName}";
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
                pos = new Vector3(orbitParent.position.x + r.x,
                                  orbitParent.position.y + randomY,
                                  orbitParent.position.z + r.y);
                rot = Quaternion.identity;
            }

            go.transform.SetPositionAndRotation(pos, rot);

            // Planet.cs config
            var planet = go.GetComponent<Planet>();
            if (planet)
            {
                planet.PlanetName = finalName;
                planet.IdUrutanBenar = idx + 1; // urutan benar mengikuti urutan prefab / generate
                if (!planet.manager) planet.manager = GetOrFindManager();
#if UNITY_EDITOR
                planet.NetPos.Value = go.transform.position;
                planet.NetRot.Value = go.transform.rotation;
#endif
            }
            else
            {
                Debug.LogWarning($"[OrbitGenerator] Prefab tidak punya Planet.cs: {go.name}");
            }
        }
    }

    // ====== TABLE INFO ======
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

    // ====== UTIL ======
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

    string CleanPrefabName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Planet";
        return raw.Replace("(Clone)", "").Trim();
    }

    // buat material fallback kalau belum diassign (hindari pink)
    Material EnsureOrbitMaterial()
    {
        if (orbitLineMaterial) return orbitLineMaterial;

        // coba URP Unlit dulu, kalau tidak ada jatuh ke Built-in Unlit/Color
        Shader sh =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color");

        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", orbitColor);
        orbitLineMaterial = mat;
        return orbitLineMaterial;
    }
}


