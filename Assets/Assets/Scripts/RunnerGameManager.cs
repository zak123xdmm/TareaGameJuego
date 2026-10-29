using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Controla la partida: menú con botón INICIAR, cuenta regresiva, puntaje por tiempo,
/// hamburguesas, vidas, Game Over y Victoria. TODA la interfaz se crea por código.
/// Ponlo en el objeto "GameManager" (Tools > Runner > Configurar escena lo hace solo).
/// </summary>
public class RunnerGameManager : MonoBehaviour
{
    public enum State { Menu, Countdown, Playing, GameOver, Victory }

    const string BestKey = "Runner_BestScore";

    [Header("Referencias (se buscan solas)")]
    public RunnerPlayer player;
    public ObstacleSpawner spawner;
    public ScenarioGenerator scenario;

    [Header("Partida")]
    [Tooltip("Dónde empieza Cheems, relativo al escenario (antes de la línea de salida).")]
    public Vector3 startOffset = new Vector3(0f, 0f, 1f);
    public int startingLives = 3;
    [Tooltip("Puntos que se ganan por cada segundo sobrevivido.")]
    public float pointsPerSecond = 10f;
    [Tooltip("Al llegar a este puntaje se gana la partida.")]
    public int victoryScore = 1500;
    public int countdownFrom = 3;

    [Header("Textos")]
    public string gameTitle = "CHEEMS RUN";
    public string subtitle = "Escapa de la nube Amsiedad";
    public string startButtonText = "INICIAR PARTIDA";
    public string gameOverTitle = "JUEGO TERMINADO";
    public string victoryTitle = "¡VICTORIA!";
    public string victoryMessage = "¡Meta alcanzada!";
    public string newRecordMessage = "¡NUEVO RÉCORD!";
    public string retryButtonText = "JUGAR DE NUEVO";

    [Header("Colores")]
    public Color titleColor = new Color(1f, 0.82f, 0.25f);
    public Color gameOverColor = new Color(1f, 0.3f, 0.3f);
    public Color victoryColor = new Color(1f, 0.85f, 0.2f);
    public Color heartColor = new Color(1f, 0.25f, 0.3f);

    public State CurrentState { get { return state; } }
    public int Score { get { return Mathf.FloorToInt(score); } }

    // --- Estado ---
    State state = State.Menu;
    float score;
    int burgers, best, lastShownScore = -1;
    float lockUntil, flashAlpha, scorePunch;

    // --- UI ---
    Font font;
    Canvas canvas;
    GameObject menuPanel, hudPanel, gameOverPanel, victoryPanel;
    Text scoreText, burgerText, countdownText, titleText;
    Text goTitle, goScore, goBest, vicTitle, vicMsg, vicScore, vicBest;
    Image flashImage;
    readonly List<Image> hearts = new List<Image>();
    Sprite heartSprite;

    // ------------------------------------------------------------------ inicio

    void Start()
    {
        FindReferences();

        if (player == null)
        {
            Debug.LogError("[RunnerGameManager] No encuentro a RunnerPlayer. Usa Tools > Runner > Configurar escena.");
            enabled = false;
            return;
        }

        if (player.onHit == null) player.onHit = new UnityEvent<int>();
        if (player.onDied == null) player.onDied = new UnityEvent();
        if (player.onPickup == null) player.onPickup = new UnityEvent<int>();
        player.onHit.AddListener(OnPlayerHit);
        player.onDied.AddListener(OnPlayerDied);
        player.onPickup.AddListener(OnPickup);

        best = PlayerPrefs.GetInt(BestKey, 0);

        EnsureEventSystem();
        BuildUI();
        ShowMenu();
    }

    void FindReferences()
    {
        if (player == null) player = FindFirstObjectByType<RunnerPlayer>();
        if (scenario == null) scenario = FindFirstObjectByType<ScenarioGenerator>();
        if (spawner == null) spawner = FindFirstObjectByType<ObstacleSpawner>();
        if (spawner == null) spawner = gameObject.AddComponent<ObstacleSpawner>();
        if (spawner.player == null) spawner.player = player;
        if (spawner.scenario == null) spawner.scenario = scenario;
        if (scenario != null && scenario.player == null) scenario.player = player.transform;
    }

    void OnDestroy()
    {
        if (player == null) return;
        if (player.onHit != null) player.onHit.RemoveListener(OnPlayerHit);
        if (player.onDied != null) player.onDied.RemoveListener(OnPlayerDied);
        if (player.onPickup != null) player.onPickup.RemoveListener(OnPickup);
    }

    Vector3 StartPoint()
    {
        Vector3 g = scenario != null ? scenario.transform.position : Vector3.zero;
        return g + startOffset;
    }

    // ------------------------------------------------------------------ estados

    void ShowMenu()
    {
        FindReferences();
        state = State.Menu;
        if (spawner != null) spawner.ClearAll();
        if (player != null)
        {
            player.ResetState();
            player.SetLives(startingLives);
            player.Teleport(StartPoint());
        }
        score = 0f; burgers = 0; lastShownScore = -1;
        RefreshScore();
        if (burgerText != null) burgerText.text = "HAMBURGUESAS  0";
        RefreshHearts();

        if (menuPanel != null) menuPanel.SetActive(true);
        if (hudPanel != null) hudPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        lockUntil = Time.unscaledTime + 0.2f;
    }

    void OnStartPressed()
    {
        if (state == State.Menu) StartCoroutine(CountdownRoutine());
    }

    void Retry()
    {
        if (state != State.GameOver && state != State.Victory) return;
        FindReferences();
        if (spawner != null) spawner.ClearAll();
        if (player != null)
        {
            player.ResetState();
            player.SetLives(startingLives);
            player.Teleport(StartPoint());
        }
        score = 0f; burgers = 0; lastShownScore = -1;
        RefreshScore();
        if (burgerText != null) burgerText.text = "HAMBURGUESAS  0";
        RefreshHearts();
        StartCoroutine(CountdownRoutine());
    }

    IEnumerator CountdownRoutine()
    {
        state = State.Countdown;
        if (menuPanel != null) menuPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (hudPanel != null) hudPanel.SetActive(true);
        if (countdownText != null) countdownText.gameObject.SetActive(true);

        for (int i = countdownFrom; i >= 1; i--)
        {
            if (countdownText != null) countdownText.text = i.ToString();
            if (countdownText != null) yield return Pop(countdownText.transform, 0.8f);
        }

        if (countdownText != null) countdownText.text = "¡YA!";
        state = State.Playing;
        if (player != null) player.BeginRun();
        if (spawner != null) spawner.BeginSpawning();
        if (countdownText != null) yield return Pop(countdownText.transform, 0.6f);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }

    IEnumerator Pop(Transform tr, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            tr.localScale = Vector3.one * Mathf.Lerp(1.6f, 0.9f, t / duration);
            yield return null;
        }
    }

    void OnPlayerHit(int livesLeft)
    {
        RefreshHearts();
        flashAlpha = 0.5f;
    }

    void OnPlayerDied()
    {
        if (state == State.Playing) StartCoroutine(GameOverRoutine());
    }

    IEnumerator GameOverRoutine()
    {
        state = State.GameOver;
        if (spawner != null) spawner.StopSpawning();
        RefreshHearts();
        yield return new WaitForSeconds(1.2f);

        int s = Score;
        if (s > best) { best = s; PlayerPrefs.SetInt(BestKey, best); PlayerPrefs.Save(); }

        if (goTitle != null) { goTitle.text = gameOverTitle; goTitle.color = gameOverColor; }
        if (goScore != null) goScore.text = "Puntuación final: " + s;
        if (goBest != null) goBest.text = "Mejor puntuación: " + best;
        if (hudPanel != null) hudPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        lockUntil = Time.unscaledTime + 0.8f;
    }

    IEnumerator VictoryRoutine()
    {
        state = State.Victory;
        if (spawner != null) spawner.StopSpawning();
        if (player != null) player.Celebrate();
        yield return new WaitForSeconds(1.0f);

        int s = Score;
        bool newRecord = s > best;
        if (newRecord) { best = s; PlayerPrefs.SetInt(BestKey, best); PlayerPrefs.Save(); }

        if (vicTitle != null) { vicTitle.text = victoryTitle; vicTitle.color = victoryColor; }
        if (vicMsg != null) vicMsg.text = newRecord ? newRecordMessage : victoryMessage;
        if (vicScore != null) vicScore.text = "Puntuación: " + s;
        if (vicBest != null) vicBest.text = "Mejor puntuación: " + best;
        if (hudPanel != null) hudPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(true);
        lockUntil = Time.unscaledTime + 0.8f;
    }

    void OnPickup(int points)
    {
        if (state != State.Playing) return;
        score += points;
        burgers++;
        if (burgerText != null) burgerText.text = "HAMBURGUESAS  " + burgers;
        scorePunch = 1f;
    }

    // ------------------------------------------------------------------ bucle

    void Update()
    {
        float dt = Time.deltaTime;

        if (state == State.Playing)
        {
            score += pointsPerSecond * dt;
            RefreshScore();
            if (score >= victoryScore) StartCoroutine(VictoryRoutine());
        }
        else if (ConfirmPressed() && Time.unscaledTime >= lockUntil)
        {
            if (state == State.Menu) OnStartPressed();
            else if (state == State.GameOver && gameOverPanel != null && gameOverPanel.activeSelf) Retry();
            else if (state == State.Victory && victoryPanel != null && victoryPanel.activeSelf) Retry();
        }

        // animaciones de interfaz
        if (menuPanel != null && menuPanel.activeSelf && titleText != null)
            titleText.transform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.unscaledTime * 3f));

        scorePunch = Mathf.MoveTowards(scorePunch, 0f, dt * 4f);
        if (scoreText != null)
            scoreText.transform.localScale = Vector3.one * (1f + 0.25f * scorePunch);

        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, dt * 1.2f);
        if (flashImage != null)
            flashImage.color = new Color(1f, 0.1f, 0.1f, flashAlpha);
    }

    void RefreshScore()
    {
        int s = Score;
        if (s == lastShownScore) return;
        lastShownScore = s;
        if (scoreText != null) scoreText.text = "PUNTOS  " + s;
    }

    void RefreshHearts()
    {
        if (player == null) return;
        for (int i = 0; i < hearts.Count; i++)
        {
            if (hearts[i] != null)
                hearts[i].color = i < player.Lives ? heartColor : new Color(0.15f, 0.15f, 0.15f, 0.55f);
        }
    }

    static bool ConfirmPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        return k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
#endif
    }

    // ------------------------------------------------------------------ construcción de la interfaz

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        InputSystemUIInputModule module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    void BuildUI()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject cgo = new GameObject("RunnerUI");
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler cs = cgo.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();

        heartSprite = MakeHeartSprite();
        Vector2 center = new Vector2(0.5f, 0.5f);
        Vector2 topLeft = new Vector2(0f, 1f);
        Vector2 topRight = new Vector2(1f, 1f);

        // ---------- HUD ----------
        hudPanel = Panel("HUD", canvas.transform, Color.clear);
        scoreText = Label(hudPanel.transform, "PUNTOS  0", 64, Color.white, TextAnchor.UpperLeft, topLeft, new Vector2(40f, -30f), new Vector2(800f, 90f));
        burgerText = Label(hudPanel.transform, "HAMBURGUESAS  0", 40, new Color(1f, 0.85f, 0.4f), TextAnchor.UpperLeft, topLeft, new Vector2(40f, -110f), new Vector2(800f, 60f));
        for (int i = 0; i < startingLives; i++)
        {
            RectTransform rt = MakeRect("Vida" + i, hudPanel.transform, topRight, new Vector2(-40f - i * 75f, -35f), new Vector2(64f, 64f));
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = heartSprite;
            img.raycastTarget = false;
            hearts.Add(img);
        }

        // ---------- destello rojo al recibir un golpe ----------
        GameObject flash = Panel("Flash", canvas.transform, Color.clear);
        flashImage = flash.GetComponent<Image>();
        flashImage.raycastTarget = false;

        // ---------- cuenta regresiva ----------
        countdownText = Label(canvas.transform, "3", 260, Color.white, TextAnchor.MiddleCenter, center, new Vector2(0f, 120f), new Vector2(900f, 320f));

        // ---------- menú ----------
        menuPanel = Panel("Menu", canvas.transform, new Color(0.03f, 0.06f, 0.14f, 0.55f));
        titleText = Label(menuPanel.transform, gameTitle, 150, titleColor, TextAnchor.MiddleCenter, center, new Vector2(0f, 260f), new Vector2(1700f, 220f));
        Label(menuPanel.transform, subtitle, 56, Color.white, TextAnchor.MiddleCenter, center, new Vector2(0f, 120f), new Vector2(1500f, 90f));
        MakeButton(menuPanel.transform, startButtonText, new Vector2(0f, -60f), new Vector2(640f, 140f), new Color(0.2f, 0.75f, 0.3f), OnStartPressed);
        Label(menuPanel.transform, "Flecha ARRIBA: saltar    |    Flecha ABAJO: agacharse", 40, new Color(0.9f, 0.95f, 1f), TextAnchor.MiddleCenter, center, new Vector2(0f, -220f), new Vector2(1600f, 60f));
        Label(menuPanel.transform, "(también puedes iniciar con Enter o Espacio)", 30, new Color(0.75f, 0.8f, 0.9f), TextAnchor.MiddleCenter, center, new Vector2(0f, -285f), new Vector2(1400f, 50f));

        // ---------- Game Over ----------
        gameOverPanel = Panel("GameOver", canvas.transform, new Color(0.15f, 0.02f, 0.02f, 0.7f));
        goTitle = Label(gameOverPanel.transform, gameOverTitle, 130, gameOverColor, TextAnchor.MiddleCenter, center, new Vector2(0f, 230f), new Vector2(1700f, 190f));
        goScore = Label(gameOverPanel.transform, "Puntuación final: 0", 76, Color.white, TextAnchor.MiddleCenter, center, new Vector2(0f, 70f), new Vector2(1500f, 110f));
        goBest = Label(gameOverPanel.transform, "Mejor puntuación: 0", 52, new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleCenter, center, new Vector2(0f, -20f), new Vector2(1500f, 80f));
        MakeButton(gameOverPanel.transform, retryButtonText, new Vector2(0f, -170f), new Vector2(700f, 140f), new Color(0.95f, 0.5f, 0.15f), Retry);

        // ---------- Victoria ----------
        victoryPanel = Panel("Victoria", canvas.transform, new Color(0.02f, 0.14f, 0.05f, 0.7f));
        vicTitle = Label(victoryPanel.transform, victoryTitle, 140, victoryColor, TextAnchor.MiddleCenter, center, new Vector2(0f, 260f), new Vector2(1700f, 200f));
        vicMsg = Label(victoryPanel.transform, victoryMessage, 64, Color.white, TextAnchor.MiddleCenter, center, new Vector2(0f, 130f), new Vector2(1500f, 100f));
        vicScore = Label(victoryPanel.transform, "Puntuación: 0", 70, Color.white, TextAnchor.MiddleCenter, center, new Vector2(0f, 30f), new Vector2(1500f, 100f));
        vicBest = Label(victoryPanel.transform, "Mejor puntuación: 0", 48, new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleCenter, center, new Vector2(0f, -50f), new Vector2(1500f, 80f));
        MakeButton(victoryPanel.transform, retryButtonText, new Vector2(0f, -190f), new Vector2(700f, 140f), new Color(0.2f, 0.7f, 0.35f), Retry);
    }

    RectTransform MakeRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    GameObject Panel(string name, Transform parent, Color color)
    {
        RectTransform rt = MakeRect(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = color.a > 0.01f;
        return rt.gameObject;
    }

    Text Label(Transform parent, string content, int size, Color color, TextAnchor align,
               Vector2 anchor, Vector2 pos, Vector2 boxSize)
    {
        RectTransform rt = MakeRect("Texto", parent, anchor, pos, boxSize);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        Outline o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.85f);
        o.effectDistance = new Vector2(3f, -3f);
        return t;
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color, UnityAction onClick)
    {
        RectTransform rt = MakeRect("Boton", parent, new Vector2(0.5f, 0.5f), pos, size);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;

        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        ColorBlock cb = b.colors;
        cb.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        b.colors = cb;
        b.onClick.AddListener(onClick);

        Label(rt, label, 60, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        return b;
    }

    Sprite MakeHeartSprite()
    {
        int n = 64;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float fx = (x / (n - 1f)) * 2.6f - 1.3f;
                float fy = (y / (n - 1f)) * 2.6f - 1.2f;
                float a = fx * fx + fy * fy - 1f;
                bool inside = a * a * a - fx * fx * fy * fy * fy <= 0f;
                px[y * n + x] = inside ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}