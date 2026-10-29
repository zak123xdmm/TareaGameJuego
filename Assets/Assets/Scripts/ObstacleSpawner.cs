
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class ObstacleSpawner : MonoBehaviour
{
    [Header("Referencias")]
    public RunnerPlayer player;
    public ScenarioGenerator scenario;

    [Header("Aleatoriedad por partida")]
    public bool randomizeEveryRun = true;
    public int seed = 12345;
    public bool avoidRepeatedTypes = true;
    [Range(0f, 1f)] public float comboChance = 0.22f;
    [Range(0f, 1f)] public float doubleBurgerChance = 0.35f;

    [Header("Aparición")]
    [Min(20f)] public float spawnDistance = 110f;
    [Min(10f)] public float firstObstacleDistance = 38f;
    public Vector2 startInterval = new Vector2(2.0f, 3.1f);
    public Vector2 hardInterval = new Vector2(1.35f, 2.1f);
    [Min(0.8f)] public float minSafeInterval = 1.15f;
    [Min(10f)] public float rampDuration = 100f;
    [Range(0f, 1f)] public float highChance = 0.42f;
    [Range(0f, 1f)] public float variationChance = 0.7f;

    [Header("Combinaciones")]
    [Tooltip("Distancia adicional entre obstáculos de una combinación.")]
    [Min(5f)] public float comboDistance = 10f;
    [Tooltip("Distancia mínima entre obstáculos para que el jugador pueda reaccionar.")]
    [Min(5f)] public float minimumObstacleDistance = 13f;

    [Header("Hamburguesas")]
    [Range(0f, 1f)] public float pickupChance = 0.72f;
    public int pickupPoints = 50;
    [Min(0.5f)] public float burgerSpacing = 1.6f;
    [Min(0f)] public float burgerHeight = 0.45f;

    [Header("Limpieza")]
    [Min(5f)] public float despawnDistance = 22f;

    [Header("Tamaños")]
    [Min(1f)] public float obstacleWidth = 3.4f;
    [Min(0.5f)] public float lowHeight = 1.0f;
    [Min(0.1f)] public float crouchMargin = 0.2f;

    [Header("Colores")]
    public Color woodColor = new Color(0.72f, 0.50f, 0.26f);
    public Color woodDark = new Color(0.42f, 0.27f, 0.13f);
    public Color stripeRed = new Color(0.92f, 0.22f, 0.27f);
    public Color stripeWhite = Color.white;
    public Color slabColor = new Color(0.55f, 0.25f, 0.75f);
    public Color warnYellow = new Color(1f, 0.82f, 0.15f);
    public Color poleColor = new Color(0.25f, 0.25f, 0.3f);
    public Color bunColor = new Color(0.93f, 0.66f, 0.28f);
    public Color pattyColor = new Color(0.40f, 0.20f, 0.10f);
    public Color cheeseColor = new Color(1f, 0.85f, 0.2f);
    public Color lettuceColor = new Color(0.35f, 0.8f, 0.3f);
    public Color tireColor = new Color(0.12f, 0.12f, 0.14f);
    public Color coneColor = new Color(1f, 0.35f, 0.08f);

    readonly List<GameObject> spawned = new List<GameObject>();
    readonly Dictionary<Color, Material> matCache = new Dictionary<Color, Material>();

    Transform container;
    Shader baseShader;
    bool active;
    float startTime;
    float nextZ;
    float lastSpawnZ = -99999f;
    int previousObstacleType = -1;
    int previousLowType = -1;

    void Start()
    {
        FindReferences();
        EnsureContainer();
    }

    void FindReferences()
    {
        if (player == null)
            player = FindFirstObjectByType<RunnerPlayer>();

        if (scenario == null)
            scenario = FindFirstObjectByType<ScenarioGenerator>();
    }

    public void BeginSpawning()
    {
        FindReferences();

        if (player == null)
        {
            Debug.LogWarning("ObstacleSpawner: no se encontró RunnerPlayer.");
            return;
        }

        ClearAll();

        if (randomizeEveryRun)
        {
            int runSeed = unchecked(
                System.Environment.TickCount
                ^ (int)System.DateTime.Now.Ticks
                ^ UnityEngine.Random.Range(int.MinValue, int.MaxValue)
            );

            UnityEngine.Random.InitState(runSeed);
        }
        else
        {
            UnityEngine.Random.InitState(seed);
        }

        active = true;
        startTime = Time.time;
        nextZ = player.transform.position.z + firstObstacleDistance;
        lastSpawnZ = player.transform.position.z;
        previousObstacleType = -1;
        previousLowType = -1;
    }

    public void StopSpawning()
    {
        active = false;
    }

    public void ClearAll()
    {
        active = false;

        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null)
                Destroy(spawned[i]);
        }

        spawned.Clear();
        lastSpawnZ = -99999f;
        previousObstacleType = -1;
        previousLowType = -1;
    }

    void Update()
    {
        if (player == null)
        {
            FindReferences();
            if (player == null) return;
        }

        float pz = player.transform.position.z;

        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            GameObject obj = spawned[i];

            if (obj == null)
            {
                spawned.RemoveAt(i);
                continue;
            }

            if (obj.transform.position.z < pz - despawnDistance)
            {
                Destroy(obj);
                spawned.RemoveAt(i);
            }
        }

        if (!active) return;

        int safety = 0;

        while (nextZ < pz + spawnDistance && safety < 20)
        {
            SpawnPattern(nextZ);

            float intervalDistance = NextInterval() * PredictedSpeed();
            intervalDistance = Mathf.Max(minimumObstacleDistance, intervalDistance);

            nextZ += intervalDistance;
            safety++;
        }
    }

    float Difficulty()
    {
        return Mathf.Clamp01(
            (Time.time - startTime) / Mathf.Max(1f, rampDuration)
        );
    }

    float NextInterval()
    {
        float difficulty = Difficulty();

        float min = Mathf.Lerp(
            startInterval.x,
            hardInterval.x,
            difficulty
        );

        float max = Mathf.Lerp(
            startInterval.y,
            hardInterval.y,
            difficulty
        );

        min = Mathf.Max(minSafeInterval, min);
        max = Mathf.Max(min, max);

        float interval = Random.Range(min, max);

        if (Random.value < variationChance)
            interval *= Random.Range(0.85f, 1.25f);

        return Mathf.Max(minSafeInterval, interval);
    }

    float PredictedSpeed()
    {
        if (player == null) return 10f;

        float speed = player.CurrentSpeed;
        float timeToReach = spawnDistance / Mathf.Max(1f, speed);

        return Mathf.Min(
            player.maxSpeed,
            speed + player.speedIncreasePerSecond * timeToReach
        );
    }

    Vector3 GroundOrigin()
    {
        return scenario != null
            ? scenario.transform.position
            : Vector3.zero;
    }

    void EnsureContainer()
    {
        if (container != null) return;

        Transform existing = transform.Find("Obstaculos");

        if (existing != null)
        {
            container = existing;
            return;
        }

        GameObject go = new GameObject("Obstaculos");
        container = go.transform;
        container.SetParent(transform, false);
    }

    void SpawnPattern(float z)
    {
        EnsureContainer();

        float difficulty = Difficulty();
        float x = GroundOrigin().x;
        float y = GroundOrigin().y;

        bool high = Random.value < Mathf.Lerp(highChance, 0.48f, difficulty);

        if (avoidRepeatedTypes && previousObstacleType == (high ? 1 : 0))
        {
            if (Random.value < 0.45f)
                high = !high;
        }

        GameObject obstacle = high
            ? BuildHigh(x, y, z)
            : BuildLow(x, y, z);

        if (obstacle != null)
            spawned.Add(obstacle);

        previousObstacleType = high ? 1 : 0;
        lastSpawnZ = z;

        if (Random.value < pickupChance)
            SpawnBurgers(high, x, y, z);

        // En algunas ocasiones se genera una segunda secuencia,
        // siempre separada para que el jugador tenga tiempo de reaccionar.
        if (Random.value < comboChance && Difficulty() > 0.15f)
        {
            float secondZ = z + Mathf.Max(
                comboDistance,
                minimumObstacleDistance * Random.Range(1.0f, 1.45f)
            );

            bool secondHigh = !high;

            GameObject second = secondHigh
                ? BuildHigh(x, y, secondZ)
                : BuildLow(x, y, secondZ);

            if (second != null)
                spawned.Add(second);

            if (Random.value < pickupChance)
                SpawnBurgers(secondHigh, x, y, secondZ);

            previousObstacleType = secondHigh ? 1 : 0;
            lastSpawnZ = secondZ;
        }
    }

    GameObject BuildLow(float x, float y, float z)
    {
        if (player == null) return null;

        float h = Mathf.Clamp(
            lowHeight,
            0.6f,
            Mathf.Max(0.65f, player.jumpHeight * 0.62f)
        );

        int type = Random.Range(0, 4);

        if (avoidRepeatedTypes && type == previousLowType)
            type = (type + Random.Range(1, 4)) % 4;

        previousLowType = type;

        GameObject root = new GameObject("ObstaculoBajo_" + type);
        root.transform.SetParent(container, false);
        root.transform.position = new Vector3(x, y, z);
        root.transform.rotation = Quaternion.Euler(
            0f,
            Random.Range(-7f, 7f),
            0f
        );

        RunnerObstacle obstacle = root.AddComponent<RunnerObstacle>();
        obstacle.kind = ObstacleKind.Low;

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.isTrigger = true;

        float width = obstacleWidth;

        switch (type)
        {
            case 0:
                // Caja de madera
                width = Random.Range(1.5f, 2.1f);

                Prim(
                    PrimitiveType.Cube,
                    root.transform,
                    new Vector3(0f, h * 0.5f, 0f),
                    new Vector3(width, h, 1.3f),
                    woodColor
                );

                Prim(
                    PrimitiveType.Cube,
                    root.transform,
                    new Vector3(0f, h + 0.04f, 0f),
                    new Vector3(width + 0.12f, 0.08f, 1.42f),
                    woodDark
                );

                for (int i = -1; i <= 1; i++)
                {
                    Prim(
                        PrimitiveType.Cube,
                        root.transform,
                        new Vector3(i * width * 0.25f, h * 0.5f, -0.67f),
                        new Vector3(0.1f, h * 0.88f, 0.06f),
                        woodDark
                    );
                }

                collider.center = new Vector3(0f, h * 0.5f, 0f);
                collider.size = new Vector3(width * 0.9f, h * 0.9f, 1.3f);
                break;

            case 1:
                // Valla de seguridad con colores aleatorios
                width = obstacleWidth * Random.Range(0.8f, 1.15f);

                int slats = Random.Range(5, 9);
                float slatWidth = width / slats;

                for (int i = 0; i < slats; i++)
                {
                    Color color = (i % 2 == 0)
                        ? stripeRed
                        : stripeWhite;

                    Prim(
                        PrimitiveType.Cube,
                        root.transform,
                        new Vector3(
                            -width * 0.5f + (i + 0.5f) * slatWidth,
                            h * 0.5f,
                            0f
                        ),
                        new Vector3(slatWidth, h * 0.85f, 0.3f),
                        color
                    );
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    Prim(
                        PrimitiveType.Cube,
                        root.transform,
                        new Vector3(side * (width * 0.5f - 0.12f), 0.05f, 0f),
                        new Vector3(0.24f, 0.1f, 0.5f),
                        poleColor
                    );
                }

                collider.center = new Vector3(0f, h * 0.5f, 0f);
                collider.size = new Vector3(width * 0.95f, h * 0.9f, 0.5f);
                break;

            case 2:
                // Pilas de neumáticos
                int tireCount = Random.Range(2, 4);
                float tireSize = Random.Range(0.65f, 0.9f);

                for (int i = 0; i < tireCount; i++)
                {
                    Prim(
                        PrimitiveType.Cylinder,
                        root.transform,
                        new Vector3(
                            (i - (tireCount - 1) * 0.5f) * tireSize * 0.9f,
                            tireSize * 0.35f,
                            0f
                        ),
                        new Vector3(tireSize, tireSize * 0.35f, tireSize),
                        tireColor
                    );
                }

                collider.center = new Vector3(0f, tireSize * 0.35f, 0f);
                collider.size = new Vector3(
                    tireCount * tireSize * 0.8f,
                    tireSize * 0.7f,
                    tireSize
                );
                break;

            default:
                // Conos naranjas de obra
                int coneCount = Random.Range(3, 6);
                float coneWidth = Random.Range(0.55f, 0.8f);

                for (int i = 0; i < coneCount; i++)
                {
                    float cx = -((coneCount - 1) * coneWidth) * 0.5f
                               + i * coneWidth;

                    Prim(
                        PrimitiveType.Cylinder,
                        root.transform,
                        new Vector3(cx, 0.05f, 0f),
                        new Vector3(0.48f, 0.07f, 0.48f),
                        poleColor
                    );

                    Prim(
                        PrimitiveType.Cylinder,
                        root.transform,
                        new Vector3(cx, 0.32f, 0f),
                        new Vector3(0.13f, 0.28f, 0.13f),
                        coneColor
                    );

                    Prim(
                        PrimitiveType.Cylinder,
                        root.transform,
                        new Vector3(cx, 0.48f, 0f),
                        new Vector3(0.035f, 0.08f, 0.035f),
                        warnYellow
                    );
                }

                collider.center = new Vector3(0f, 0.3f, 0f);
                collider.size = new Vector3(
                    coneCount * coneWidth,
                    0.65f,
                    0.65f
                );
                break;
        }

        return root;
    }

    GameObject BuildHigh(float x, float y, float z)
    {
        if (player == null) return null;

        float clear = Mathf.Min(
            player.CrouchHeight + crouchMargin + 0.05f,
            player.StandHeight * 0.85f
        );

        clear = Mathf.Max(1.1f, clear);

        float slabHeight = Random.Range(2.3f, 3.4f);
        float width = obstacleWidth + Random.Range(0.3f, 1.2f);

        GameObject root = new GameObject("ObstaculoAlto");
        root.transform.SetParent(container, false);
        root.transform.position = new Vector3(x, y, z);

        RunnerObstacle obstacle = root.AddComponent<RunnerObstacle>();
        obstacle.kind = ObstacleKind.High;

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.center = new Vector3(
            0f,
            clear + slabHeight * 0.5f,
            0f
        );
        collider.size = new Vector3(
            width * 0.95f,
            slabHeight,
            0.6f
        );

        Color signColor = Random.value < 0.5f
            ? slabColor
            : new Color(
                Random.Range(0.2f, 0.9f),
                Random.Range(0.2f, 0.9f),
                Random.Range(0.2f, 0.9f)
            );

        Prim(
            PrimitiveType.Cube,
            root.transform,
            new Vector3(0f, clear + slabHeight * 0.5f, 0f),
            new Vector3(width, slabHeight, 0.35f),
            signColor
        );

        int bands = Random.Range(7, 12);
        float bandWidth = width / bands;

        for (int i = 0; i < bands; i++)
        {
            Prim(
                PrimitiveType.Cube,
                root.transform,
                new Vector3(
                    -width * 0.5f + (i + 0.5f) * bandWidth,
                    clear + 0.2f,
                    -0.2f
                ),
                new Vector3(bandWidth, 0.4f, 0.05f),
                (i % 2 == 0)
                    ? warnYellow
                    : new Color(0.1f, 0.1f, 0.1f)
            );
        }

        float postHeight = clear + slabHeight;

        for (int side = -1; side <= 1; side += 2)
        {
            Prim(
                PrimitiveType.Cylinder,
                root.transform,
                new Vector3(
                    side * (width * 0.5f + 0.15f),
                    postHeight * 0.5f,
                    0f
                ),
                new Vector3(0.22f, postHeight * 0.5f, 0.22f),
                poleColor
            );
        }

        return root;
    }

    void SpawnBurgers(bool afterHigh, float x, float y, float z)
    {
        if (Random.value > pickupChance) return;

        if (afterHigh)
        {
            // Hamburguesas bajas para recoger al agacharse.
            int count = Random.value < doubleBurgerChance ? 3 : 1;

            for (int i = 0; i < count; i++)
            {
                float offset = count == 1
                    ? 0f
                    : (i - (count - 1) * 0.5f) * burgerSpacing;

                spawned.Add(
                    BuildBurger(
                        new Vector3(
                            x,
                            y + burgerHeight,
                            z + offset
                        )
                    )
                );
            }
        }
        else
        {
            // Hamburguesas en arco para saltar y recoger.
            float top = Mathf.Min(
                lowHeight,
                player.jumpHeight * 0.6f
            );

            float heightVariation = Random.Range(0.2f, 0.7f);
            float[] heights =
            {
                top + heightVariation,
                top + Random.Range(0.8f, 1.25f),
                top + heightVariation
            };

            if (Random.value < 0.5f)
            {
                spawned.Add(
                    BuildBurger(
                        new Vector3(x, y + heights[0], z - burgerSpacing)
                    )
                );

                spawned.Add(
                    BuildBurger(
                        new Vector3(x, y + heights[1], z)
                    )
                );

                spawned.Add(
                    BuildBurger(
                        new Vector3(x, y + heights[2], z + burgerSpacing)
                    )
                );
            }
            else
            {
                spawned.Add(
                    BuildBurger(
                        new Vector3(
                            x,
                            y + top + Random.Range(0.4f, 0.9f),
                            z
                        )
                    )
                );
            }
        }
    }

    GameObject BuildBurger(Vector3 position)
    {
        GameObject root = new GameObject("Hamburguesa");
        root.transform.SetParent(container, false);
        root.transform.position = position;

        root.transform.rotation = Quaternion.Euler(
            Random.Range(-8f, 8f),
            Random.Range(0f, 360f),
            Random.Range(-8f, 8f)
        );

        SphereCollider collider = root.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 0.55f;

        RunnerPickup pickup = root.AddComponent<RunnerPickup>();
        pickup.points = pickupPoints;

        Transform t = root.transform;

        Prim(
            PrimitiveType.Sphere,
            t,
            new Vector3(0f, -0.14f, 0f),
            new Vector3(0.62f, 0.24f, 0.62f),
            bunColor
        );

        Prim(
            PrimitiveType.Cylinder,
            t,
            new Vector3(0f, -0.04f, 0f),
            new Vector3(0.6f, 0.04f, 0.6f),
            pattyColor
        );

        GameObject cheese = Prim(
            PrimitiveType.Cube,
            t,
            new Vector3(0f, 0.03f, 0f),
            new Vector3(0.5f, 0.03f, 0.5f),
            cheeseColor
        );

        cheese.transform.localRotation = Quaternion.Euler(
            0f,
            Random.Range(25f, 65f),
            0f
        );

        Prim(
            PrimitiveType.Cylinder,
            t,
            new Vector3(0f, 0.07f, 0f),
            new Vector3(0.68f, 0.02f, 0.68f),
            lettuceColor
        );

        Prim(
            PrimitiveType.Sphere,
            t,
            new Vector3(0f, 0.2f, 0f),
            new Vector3(0.64f, 0.36f, 0.64f),
            bunColor
        );

        int sesameCount = Random.Range(3, 7);

        for (int i = 0; i < sesameCount; i++)
        {
            float angle = i * (360f / sesameCount) * Mathf.Deg2Rad;

            Prim(
                PrimitiveType.Sphere,
                t,
                new Vector3(
                    Mathf.Cos(angle) * Random.Range(0.1f, 0.19f),
                    Random.Range(0.31f, 0.36f),
                    Mathf.Sin(angle) * Random.Range(0.1f, 0.19f)
                ),
                new Vector3(0.07f, 0.04f, 0.07f),
                Color.white
            );
        }

        return root;
    }

    GameObject Prim(
        PrimitiveType type,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Color color
    )
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        Collider collider = go.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        Renderer renderer = go.GetComponent<Renderer>();

        if (renderer != null)
        {
            renderer.sharedMaterial = GetMat(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        return go;
    }

    Material GetMat(Color color)
    {
        if (matCache.TryGetValue(color, out Material material) &&
            material != null)
        {
            return material;
        }

        material = new Material(GetShader());
        material.name = "Obs_" + ColorUtility.ToHtmlStringRGB(color);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.15f);

        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", 0.15f);

        matCache[color] = material;
        return material;
    }

    Shader GetShader()
    {
        if (baseShader != null)
            return baseShader;

        GameObject temporary = GameObject.CreatePrimitive(
            PrimitiveType.Cube
        );

        Renderer renderer = temporary.GetComponent<Renderer>();

        baseShader = renderer != null &&
                     renderer.sharedMaterial != null
            ? renderer.sharedMaterial.shader
            : Shader.Find("Universal Render Pipeline/Lit");

        Destroy(temporary);

        return baseShader;
    }

    void OnDestroy()
    {
        foreach (Material material in matCache.Values)
        {
            if (material != null)
                Destroy(material);
        }

        matCache.Clear();
    }
}
