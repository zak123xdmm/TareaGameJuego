
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Generador de escenarios cartoon para Cheems Runner.
/// Permite generar un escenario fijo en el editor o uno infinito durante el juego.
/// Los obstáculos se gestionan independientemente mediante ObstacleSpawner.
/// </summary>
[DisallowMultipleComponent]
public class ScenarioGenerator : MonoBehaviour
{
    private const string RootName = "ScenarioRoot";
    private const float GroundY = -0.15f;

    [Header("Jugador y generación")]
    public Transform player;
    [Min(10f)] public float segmentLength = 30f;
    [Range(3, 20)] public int segmentsAhead = 8;
    [Range(1, 6)] public int segmentsBehind = 2;
    public int seed = 12345;
    public bool roadStartsAtZero = true;
    [Min(1f)] public float startLineZ = 3f;

    [Header("Escenario fijo en el editor")]
    [Tooltip("Desactivado: conserva el escenario generado en el editor. Activado: genera escenario infinito durante el juego.")]
    public bool generateAtRuntime = false;
    [Min(1)] public int fixedSegmentsAhead = 50;
    [Min(0)] public int fixedSegmentsBehind = 1;

    [Header("Carretera")]
    [Min(2f)] public float roadWidth = 8f;
    [Min(0.2f)] public float curbWidth = 0.6f;
    public Color roadColor = new Color(0.22f, 0.22f, 0.27f);
    public Color lineColor = new Color(1f, 0.92f, 0.45f);
    public Color curbColorA = Color.white;
    public Color curbColorB = new Color(0.92f, 0.22f, 0.27f);

    [Header("Terreno")]
    public Color grassColor = new Color(0.38f, 0.82f, 0.36f);
    public Color grassColorAlt = new Color(0.33f, 0.76f, 0.32f);
    public Color hillColor = new Color(0.30f, 0.66f, 0.42f);
    [Min(20f)] public float sideWidth = 120f;

    [Header("Decoración")]
    [Range(0, 30)] public int decorationsPerSide = 8;
    [Min(0f)] public float decorationSpread = 22f;
    public Color trunkColor = new Color(0.45f, 0.29f, 0.16f);
    public Color rockColor = new Color(0.62f, 0.62f, 0.66f);
    public Color roofColor = new Color(0.85f, 0.27f, 0.22f);
    public Color poleColor = new Color(0.25f, 0.25f, 0.3f);
    public Color lampColor = new Color(1f, 0.93f, 0.55f);
    public Color cloudColor = Color.white;

    public Color[] foliageColors =
    {
        new Color(0.18f, 0.65f, 0.30f),
        new Color(0.30f, 0.78f, 0.33f),
        new Color(0.12f, 0.55f, 0.35f),
        new Color(0.95f, 0.62f, 0.20f)
    };

    public Color[] houseColors =
    {
        new Color(1f, 0.85f, 0.45f),
        new Color(0.60f, 0.82f, 0.98f),
        new Color(0.98f, 0.65f, 0.72f),
        new Color(0.80f, 0.95f, 0.70f)
    };

    [Header("Modelos personalizados")]
    public GameObject[] customDecorations;
    [Range(0f, 1f)] public float customChance = 0.25f;

    [Header("Ambiente")]
    public bool applyAtmosphere = true;
    public Color skyColor = new Color(0.53f, 0.81f, 0.98f);
    public bool enableFog = true;

    private readonly Dictionary<int, GameObject> segments =
        new Dictionary<int, GameObject>();

    private readonly Dictionary<Color, Material> matCache =
        new Dictionary<Color, Material>();

    private Transform root;
    private Shader baseShader;
    private int lastIndex = int.MinValue;
    private bool isGenerating;

    // ------------------------------------------------------------
    // CICLO DE VIDA
    // ------------------------------------------------------------

    private void Start()
    {
        if (!Application.isPlaying || !generateAtRuntime)
            return;

        GenerateNow();
    }

    private void Update()
    {
        if (!Application.isPlaying || !generateAtRuntime || isGenerating)
            return;

        if (player == null)
        {
            FindPlayer();

            if (player == null)
                return;
        }

        int currentIndex = CurrentIndex();

        if (currentIndex != lastIndex)
            Refresh(currentIndex);
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
        {
            foreach (Material material in matCache.Values)
            {
                if (material != null)
                    Destroy(material);
            }
        }

        matCache.Clear();
    }

    // ------------------------------------------------------------
    // API PÚBLICA
    // ------------------------------------------------------------

    [ContextMenu("Generar escenario infinito ahora")]
    public void GenerateNow()
    {
        if (isGenerating)
            return;

        isGenerating = true;

        try
        {
            ClearAllInternal();

            if (player == null)
                FindPlayer();

            Refresh(CurrentIndex());

            if (applyAtmosphere)
                ApplyAtmosphere();
        }
        finally
        {
            isGenerating = false;
        }
    }

    [ContextMenu("Generar escenario fijo en editor")]
    public void GenerateFixedScenario()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "Detén Play antes de generar el escenario fijo.",
                this
            );
            return;
        }

        if (isGenerating)
            return;

        isGenerating = true;

        try
        {
            ClearAllInternal();
            EnsureRoot();

            int from = roadStartsAtZero
                ? -1
                : -Mathf.Max(0, fixedSegmentsBehind);

            int to = Mathf.Max(1, fixedSegmentsAhead);

            for (int i = from; i <= to; i++)
            {
                GameObject segment = BuildSegment(i);
                segments[i] = segment;
            }

            lastIndex = 0;
            generateAtRuntime = false;

            if (applyAtmosphere)
                ApplyAtmosphere();

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(gameObject);

            if (gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif

            Debug.Log(
                "Escenario fijo generado: " + segments.Count +
                " segmentos. Guarda la escena con Ctrl+S.",
                this
            );
        }
        finally
        {
            isGenerating = false;
        }
    }

    [ContextMenu("Limpiar escenario")]
    public void ClearAll()
    {
        if (Application.isPlaying)
            ClearAllInternal();
        else
        {
#if UNITY_EDITOR
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Undo.RegisterFullObjectHierarchyUndo(
                    gameObject,
                    "Limpiar escenario"
                );
            }
#endif
            ClearAllInternal();
        }
    }

    // ------------------------------------------------------------
    // JUGADOR Y SEGMENTOS
    // ------------------------------------------------------------

    private void FindPlayer()
    {
        if (player != null)
            return;

        GameObject playerObject = null;

        try
        {
            playerObject = GameObject.FindWithTag("Player");
        }
        catch (UnityException)
        {
            // El proyecto podría no tener definido el tag Player.
        }

        if (playerObject != null)
        {
            player = playerObject.transform;
            return;
        }

        RunnerPlayer runnerPlayer = FindFirstObjectByType<RunnerPlayer>();

        if (runnerPlayer != null)
            player = runnerPlayer.transform;
    }

    private int CurrentIndex()
    {
        float z = player != null
            ? player.position.z - transform.position.z
            : 0f;

        return Mathf.FloorToInt(
            z / Mathf.Max(10f, segmentLength)
        );
    }

    private void EnsureRoot()
    {
        if (root != null)
            return;

        Transform existing = transform.Find(RootName);

        if (existing != null)
        {
            root = existing;
            return;
        }

        GameObject rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(transform, false);
        root = rootObject.transform;
    }

    private void ClearAllInternal()
    {
        segments.Clear();

        Transform oldRoot = transform.Find(RootName);

        if (oldRoot != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Undo.RegisterFullObjectHierarchyUndo(
                    oldRoot.gameObject,
                    "Regenerar escenario"
                );
            }
#endif

            oldRoot.name = "_ScenarioRoot_Old";
            oldRoot.SetParent(null);
            SafeDestroy(oldRoot.gameObject);
        }

        root = null;
        lastIndex = int.MinValue;
    }

    private void Refresh(int currentIndex)
    {
        EnsureRoot();

        int from = currentIndex - segmentsBehind;

        if (roadStartsAtZero)
            from = Mathf.Max(from, -1);

        int to = currentIndex + segmentsAhead;

        List<int> remove = new List<int>();

        foreach (KeyValuePair<int, GameObject> pair in segments)
        {
            if (pair.Key < from || pair.Key > to)
                remove.Add(pair.Key);
        }

        foreach (int index in remove)
        {
            if (segments.TryGetValue(index, out GameObject segment) &&
                segment != null)
            {
                SafeDestroy(segment);
            }

            segments.Remove(index);
        }

        for (int i = from; i <= to; i++)
        {
            if (!segments.ContainsKey(i))
                segments[i] = BuildSegment(i);
        }

        lastIndex = currentIndex;
    }

    // ------------------------------------------------------------
    // CONSTRUCCIÓN DEL SEGMENTO
    // ------------------------------------------------------------

    private GameObject BuildSegment(int index)
    {
        GameObject segment = new GameObject("Segment_" + index);

        Transform t = segment.transform;
        t.SetParent(root, false);
        t.localPosition = new Vector3(
            0f,
            0f,
            index * segmentLength
        );

        System.Random rng = new System.Random(
            unchecked(seed * 397 + index * 7919)
        );

        float length = segmentLength;
        float halfRoad = roadWidth * 0.5f;

        bool roadless = roadStartsAtZero && index < 0;

        if (!roadless)
        {
            BoxCollider roadCollider = segment.AddComponent<BoxCollider>();
            roadCollider.center = new Vector3(
                0f,
                -0.5f,
                length * 0.5f
            );

            roadCollider.size = new Vector3(
                roadWidth + curbWidth * 2f,
                1f,
                length + 0.2f
            );

            Prim(
                PrimitiveType.Cube,
                t,
                new Vector3(0f, -0.25f, length * 0.5f),
                new Vector3(roadWidth, 0.5f, length),
                roadColor,
                false
            );

            int stripes = Mathf.Max(
                1,
                Mathf.RoundToInt(length / 3f)
            );

            float stripeLength = length / stripes;

            for (int i = 0; i < stripes; i++)
            {
                Color color = (((index * stripes + i) & 1) == 0)
                    ? curbColorA
                    : curbColorB;

                float z = (i + 0.5f) * stripeLength;

                for (int side = -1; side <= 1; side += 2)
                {
                    Prim(
                        PrimitiveType.Cube,
                        t,
                        new Vector3(
                            side * (halfRoad + curbWidth * 0.5f),
                            0.1f,
                            z
                        ),
                        new Vector3(curbWidth, 0.3f, stripeLength),
                        color,
                        false
                    );
                }
            }

            int dashes = Mathf.Max(
                1,
                Mathf.RoundToInt(length / 6f)
            );

            float pitch = length / dashes;

            for (int i = 0; i < dashes; i++)
            {
                Prim(
                    PrimitiveType.Cube,
                    t,
                    new Vector3(0f, 0.012f, (i + 0.5f) * pitch),
                    new Vector3(0.25f, 0.025f, pitch * 0.5f),
                    lineColor,
                    false
                );
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Prim(
                    PrimitiveType.Cube,
                    t,
                    new Vector3(
                        side * (halfRoad - 0.5f),
                        0.012f,
                        length * 0.5f
                    ),
                    new Vector3(0.15f, 0.025f, length),
                    Color.white,
                    false
                );
            }
        }

        if (roadStartsAtZero && index == 0)
            BuildStartArea(t, length, halfRoad);

        Color grass = ((index & 1) == 0)
            ? grassColor
            : grassColorAlt;

        float sideStart = halfRoad + curbWidth;

        for (int side = -1; side <= 1; side += 2)
        {
            Prim(
                PrimitiveType.Cube,
                t,
                new Vector3(
                    side * (sideStart + sideWidth * 0.5f),
                    GroundY - 0.5f,
                    length * 0.5f
                ),
                new Vector3(sideWidth, 1f, length),
                grass,
                false
            );
        }

        if (roadless)
        {
            Prim(
                PrimitiveType.Cube,
                t,
                new Vector3(0f, GroundY - 0.5f, length * 0.5f),
                new Vector3(sideStart * 2f, 1f, length),
                grass,
                false
            );
        }

        BuildHills(t, rng, length, halfRoad);
        BuildClouds(t, rng, length);
        BuildDecorations(t, rng, length, halfRoad, roadless);

        return segment;
    }

    private void BuildHills(
        Transform t,
        System.Random rng,
        float length,
        float halfRoad)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            for (int h = 0; h < 2; h++)
            {
                float x = side * (
                    halfRoad + 30f + R(rng, 0f, 60f)
                );

                float z = R(rng, 0f, length);
                float width = R(rng, 25f, 45f);
                float height = R(rng, 8f, 18f);

                Color color = Color.Lerp(
                    hillColor,
                    grassColor,
                    (float)rng.NextDouble() * 0.4f
                );

                Prim(
                    PrimitiveType.Sphere,
                    t,
                    new Vector3(
                        x,
                        GroundY - height * 0.35f,
                        z
                    ),
                    new Vector3(width, height * 2f, width),
                    color,
                    false
                );
            }
        }
    }

    private void BuildClouds(
        Transform t,
        System.Random rng,
        float length)
    {
        int cloudCount = rng.Next(1, 3);

        for (int c = 0; c < cloudCount; c++)
        {
            Vector3 position = new Vector3(
                R(rng, -70f, 70f),
                R(rng, 22f, 38f),
                R(rng, 0f, length)
            );

            for (int k = 0; k < 3; k++)
            {
                Vector3 offset = new Vector3(
                    k * 4.5f - 4.5f + R(rng, -1f, 1f),
                    R(rng, -1f, 1f),
                    R(rng, -1f, 1f)
                );

                Prim(
                    PrimitiveType.Sphere,
                    t,
                    position + offset,
                    new Vector3(
                        R(rng, 6f, 9f),
                        R(rng, 3.5f, 5f),
                        R(rng, 5f, 7f)
                    ),
                    cloudColor,
                    false
                );
            }
        }
    }

    private void BuildDecorations(
        Transform t,
        System.Random rng,
        float length,
        float halfRoad,
        bool roadless)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            for (int k = 0; k < decorationsPerSide; k++)
            {
                float z = (k + (float)rng.NextDouble()) *
                    (length / Mathf.Max(1, decorationsPerSide));

                double type = rng.NextDouble();

                float baseOffset = halfRoad + curbWidth + 2f;
                float x = side * (
                    baseOffset + R(rng, 0f, decorationSpread)
                );

                if (customDecorations != null &&
                    customDecorations.Length > 0 &&
                    rng.NextDouble() < customChance)
                {
                    GameObject prefab = customDecorations[
                        rng.Next(customDecorations.Length)
                    ];

                    if (prefab != null)
                    {
                        GameObject instance = Instantiate(prefab, t);
                        instance.name = prefab.name;
                        instance.transform.localPosition =
                            new Vector3(x, GroundY, z);

                        instance.transform.localRotation =
                            Quaternion.Euler(
                                0f,
                                R(rng, 0f, 360f),
                                0f
                            );

                        continue;
                    }
                }

                if (type < 0.55)
                {
                    MakeTree(t, rng, x, z);
                }
                else if (type < 0.75)
                {
                    MakeBush(t, rng, x, z);
                }
                else if (type < 0.88)
                {
                    MakeRock(t, rng, x, z);
                }
                else
                {
                    float houseX = side * (
                        baseOffset + 8f + R(rng, 0f, 10f)
                    );

                    MakeHouse(t, rng, side, houseX, z);
                }
            }

            if (!roadless)
            {
                MakeLamp(
                    t,
                    side * (halfRoad + curbWidth + 0.8f),
                    length * 0.25f
                );

                MakeLamp(
                    t,
                    side * (halfRoad + curbWidth + 0.8f),
                    length * 0.75f
                );
            }
        }
    }

    // ------------------------------------------------------------
    // ZONA DE SALIDA
    // ------------------------------------------------------------

    private void BuildStartArea(
        Transform t,
        float length,
        float halfRoad)
    {
        float z0 = Mathf.Clamp(startLineZ, 1f, length - 1f);

        int columns = Mathf.Max(
            2,
            Mathf.RoundToInt(roadWidth / 0.5f)
        );

        float cellWidth = roadWidth / columns;

        for (int row = 0; row < 2; row++)
        {
            for (int i = 0; i < columns; i++)
            {
                Color color = (((row + i) & 1) == 0)
                    ? Color.white
                    : new Color(0.08f, 0.08f, 0.08f);

                Prim(
                    PrimitiveType.Cube,
                    t,
                    new Vector3(
                        -halfRoad + (i + 0.5f) * cellWidth,
                        0.016f,
                        z0 + (row - 0.5f) * 0.5f
                    ),
                    new Vector3(cellWidth, 0.03f, 0.5f),
                    color,
                    false
                );
            }
        }

        float postX = halfRoad + curbWidth + 0.4f;

        for (int side = -1; side <= 1; side += 2)
        {
            Prim(
                PrimitiveType.Cylinder,
                t,
                new Vector3(side * postX, 3f, z0),
                new Vector3(0.5f, 3f, 0.5f),
                curbColorB
            );
        }

        Prim(
            PrimitiveType.Cube,
            t,
            new Vector3(0f, 6f, z0),
            new Vector3(postX * 2f + 0.8f, 1.1f, 0.6f),
            curbColorB
        );

        int boardColumns = Mathf.Max(
            4,
            Mathf.RoundToInt(postX * 2f / 0.6f)
        );

        float boardWidth = postX * 2f / boardColumns;

        for (int row = 0; row < 2; row++)
        {
            for (int i = 0; i < boardColumns; i++)
            {
                Color color = (((row + i) & 1) == 0)
                    ? Color.white
                    : new Color(0.08f, 0.08f, 0.08f);

                Prim(
                    PrimitiveType.Cube,
                    t,
                    new Vector3(
                        -postX + (i + 0.5f) * boardWidth,
                        5.75f + row * 0.5f,
                        z0 - 0.32f
                    ),
                    new Vector3(boardWidth, 0.5f, 0.04f),
                    color,
                    false
                );
            }
        }
    }

    // ------------------------------------------------------------
    // ÁRBOLES, ARBUSTOS, ROCAS, CASAS Y FAROLAS
    // ------------------------------------------------------------

    private void MakeTree(
        Transform t,
        System.Random rng,
        float x,
        float z)
    {
        float height = R(rng, 2f, 3.5f);

        Prim(
            PrimitiveType.Cylinder,
            t,
            new Vector3(x, GroundY + height * 0.5f, z),
            new Vector3(0.5f, height * 0.5f, 0.5f),
            trunkColor
        );

        Color leaf = Pick(
            foliageColors,
            rng,
            new Color(0.2f, 0.7f, 0.3f)
        );

        float size = R(rng, 2.6f, 4f);

        Prim(
            PrimitiveType.Sphere,
            t,
            new Vector3(
                x,
                GroundY + height + size * 0.3f,
                z
            ),
            Vector3.one * size,
            leaf
        );

        Prim(
            PrimitiveType.Sphere,
            t,
            new Vector3(
                x + R(rng, -0.4f, 0.4f),
                GroundY + height + size * 0.75f,
                z + R(rng, -0.4f, 0.4f)
            ),
            Vector3.one * (size * 0.65f),
            leaf
        );
    }

    private void MakeBush(
        Transform t,
        System.Random rng,
        float x,
        float z)
    {
        Color leaf = Pick(
            foliageColors,
            rng,
            new Color(0.2f, 0.7f, 0.3f)
        );

        float sx = R(rng, 1.2f, 2f);
        float sy = R(rng, 0.9f, 1.4f);

        Prim(
            PrimitiveType.Sphere,
            t,
            new Vector3(x, GroundY + sy * 0.35f, z),
            new Vector3(sx, sy, sx),
            leaf
        );
    }

    private void MakeRock(
        Transform t,
        System.Random rng,
        float x,
        float z)
    {
        float sy = R(rng, 0.8f, 1.4f);

        GameObject rock = Prim(
            PrimitiveType.Sphere,
            t,
            new Vector3(x, GroundY + sy * 0.3f, z),
            new Vector3(
                R(rng, 1f, 2.2f),
                sy,
                R(rng, 1f, 2f)
            ),
            rockColor
        );

        rock.transform.localRotation = Quaternion.Euler(
            0f,
            R(rng, 0f, 360f),
            0f
        );
    }

    private void MakeHouse(
        Transform t,
        System.Random rng,
        int side,
        float x,
        float z)
    {
        float width = R(rng, 5f, 7f);
        float height = R(rng, 3.5f, 5f);
        float depth = R(rng, 5f, 7f);

        Color wall = Pick(
            houseColors,
            rng,
            new Color(1f, 0.85f, 0.45f)
        );

        Prim(
            PrimitiveType.Cube,
            t,
            new Vector3(x, GroundY + height * 0.5f, z),
            new Vector3(width, height, depth),
            wall
        );

        GameObject roof = Prim(
            PrimitiveType.Cube,
            t,
            new Vector3(x, GroundY + height, z),
            new Vector3(
                width / 1.4142f,
                width / 1.4142f,
                depth + 0.8f
            ),
            roofColor
        );

        roof.transform.localRotation = Quaternion.Euler(
            0f,
            0f,
            45f
        );

        float faceX = x - side * (width * 0.5f + 0.02f);

        Prim(
            PrimitiveType.Cube,
            t,
            new Vector3(
                faceX,
                GroundY + 0.9f,
                z - depth * 0.2f
            ),
            new Vector3(0.05f, 1.8f, 1.1f),
            trunkColor,
            false
        );

        Prim(
            PrimitiveType.Cube,
            t,
            new Vector3(
                faceX,
                GroundY + height * 0.6f,
                z + depth * 0.2f
            ),
            new Vector3(0.05f, 1f, 1.2f),
            lampColor,
            false
        );
    }

    private void MakeLamp(Transform t, float x, float z)
    {
        Prim(
            PrimitiveType.Cylinder,
            t,
            new Vector3(x, GroundY + 2.2f, z),
            new Vector3(0.14f, 2.2f, 0.14f),
            poleColor
        );

        Prim(
            PrimitiveType.Sphere,
            t,
            new Vector3(x, GroundY + 4.5f, z),
            Vector3.one * 0.7f,
            lampColor,
            false
        );
    }

    // ------------------------------------------------------------
    // AMBIENTE
    // ------------------------------------------------------------

    private void ApplyAtmosphere()
    {
        Camera camera = Camera.main;

        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = skyColor;
        }

        RenderSettings.fog = enableFog;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = skyColor;

        float far = Mathf.Max(
            segmentLength,
            (generateAtRuntime ? segmentsAhead : fixedSegmentsAhead)
                * segmentLength
        );

        RenderSettings.fogStartDistance = far * 0.45f;
        RenderSettings.fogEndDistance = far * 0.9f;
    }

    // ------------------------------------------------------------
    // PRIMITIVAS Y MATERIALES
    // ------------------------------------------------------------

    private GameObject Prim(
        PrimitiveType type,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Color color,
        bool castShadows = true)
    {
        GameObject go = GameObject.CreatePrimitive(type);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        Collider collider = go.GetComponent<Collider>();

        if (collider != null)
            SafeDestroy(collider);

        Renderer renderer = go.GetComponent<Renderer>();

        if (renderer != null)
        {
            renderer.sharedMaterial = GetMat(color);

            if (!castShadows)
                renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        return go;
    }

    private Material GetMat(Color color)
    {
        if (matCache.TryGetValue(color, out Material material) &&
            material != null)
        {
            return material;
        }

        Shader shader = GetShader();

        if (shader == null)
        {
            Debug.LogError(
                "ScenarioGenerator: no se encontró un shader compatible.",
                this
            );
            return null;
        }

        material = new Material(shader)
        {
            name = "Scn_" + ColorUtility.ToHtmlStringRGB(color)
        };

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.12f);

        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", 0.12f);

        matCache[color] = material;
        return material;
    }

    private Shader GetShader()
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

        if (baseShader == null)
            baseShader = Shader.Find("Standard");

        SafeDestroy(temporary);
        return baseShader;
    }

    private static float R(
        System.Random rng,
        float min,
        float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    private static Color Pick(
        Color[] colors,
        System.Random rng,
        Color fallback)
    {
        if (colors == null || colors.Length == 0)
            return fallback;

        return colors[rng.Next(colors.Length)];
    }

    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
