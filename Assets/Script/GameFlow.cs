using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// STEP-4 场景流：Boot → MainMenu → LevelSelect → Gameplay + 关卡解锁逻辑。
/// GameFlow 物体由构建器放进 Boot 与 MainMenu 两场景（防直接打开任一场景时无人主持流程），
/// DontDestroyOnLoad + 单例去重；存档随实例常驻内存，改档即时落盘。
/// 解锁规则：第 0 关默认解锁；第 i 关解锁 = 存档里显式解锁过，或第 i-1 关拿过 ≥1★。
/// </summary>
public class GameFlow : MonoBehaviour
{
    public static GameFlow Instance { get; private set; }

    [Tooltip("关卡数据库（决定选关顺序与解锁链）")]
    public LevelDatabase database;
    [Tooltip("UI 字体（STKAITI SDF，构建器接线；运行时自建 GameFlow 时回退 TMP 全局默认）")]
    public TMP_FontAsset font;

    public const string SceneBoot = "Boot";
    public const string SceneMainMenu = "MainMenu";
    public const string SceneLevelSelect = "LevelSelect";
    public const string SceneGameplay = "Gameplay";

    /// <summary>当前进行中的关卡下标（-1 = 未从选关进入）。</summary>
    public int CurrentLevelIndex { get; private set; } = -1;
    public SaveSystem.SaveData Save { get; private set; }
    public List<LevelConfig> Levels => database != null ? database.levels : null;
    public LevelConfig CurrentLevel => ValidIndex(CurrentLevelIndex) ? Levels[CurrentLevelIndex] : null;

    private GameObject menuUI;
    private Button resetButton;
    private bool resetArmed;
    private float resetArmUntil;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Save = SaveSystem.Load();
        AudioListener.volume = Save.masterVolume;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        // Boot 场景进来主动跳主菜单；直接打开 MainMenu 时场景已加载，这里补建 UI
        string active = SceneManager.GetActiveScene().name;
        if (active == SceneBoot)
            SceneManager.LoadScene(SceneMainMenu);
        else if (active == SceneMainMenu)
            BuildMainMenuUI();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneMainMenu) BuildMainMenuUI();
    }

    /// <summary>运行时兜底：直接打开 LevelSelect/Gameplay 调试时自建一个无数据库的实例（各 UI 会给出未配置提示）。</summary>
    public static GameFlow EnsureInstance()
    {
        if (Instance != null) return Instance;
        new GameObject("GameFlow").AddComponent<GameFlow>();
        return Instance;
    }

    // ---------- 查询 ----------

    public bool ValidIndex(int index) => Levels != null && index >= 0 && index < Levels.Count && Levels[index] != null;

    public bool IsUnlocked(int index)
    {
        if (!ValidIndex(index)) return false;
        if (index == 0) return true;
        var rec = Save.GetRecord(Levels[index].levelId);
        if (rec != null && rec.unlocked) return true;
        var prev = Save.GetRecord(Levels[index - 1].levelId);
        return prev != null && prev.stars >= 1;
    }

    public int GetStars(int index) => ValidIndex(index) ? Save.GetStars(Levels[index].levelId) : 0;

    // ---------- 存档 ----------

    public void CompleteLevel(int levelId, int stars)
    {
        Save.RecordResult(levelId, stars);
        int idx = Levels != null ? Levels.FindIndex(l => l != null && l.levelId == levelId) : -1;
        if (ValidIndex(idx + 1)) Save.Unlock(Levels[idx + 1].levelId); // 解锁下一关
        SaveSystem.Save(Save);
    }

    public void ResetSave()
    {
        SaveSystem.DeleteSave();
        Save = SaveSystem.Load();
        AudioListener.volume = Save.masterVolume;
    }

    // ---------- 场景流 ----------

    public void StartLevel(int index)
    {
        CurrentLevelIndex = index;
        SceneManager.LoadScene(SceneGameplay);
    }

    public void NextLevel()
    {
        if (ValidIndex(CurrentLevelIndex + 1)) StartLevel(CurrentLevelIndex + 1);
        else ToLevelSelect();
    }

    public void ToMainMenu() => SceneManager.LoadScene(SceneMainMenu);
    public void ToLevelSelect() => SceneManager.LoadScene(SceneLevelSelect);

    // ---------- 主菜单 UI（运行时自建，无预制依赖） ----------

    private void BuildMainMenuUI()
    {
        if (menuUI != null) Destroy(menuUI);
        EnsureEventSystem();

        menuUI = new GameObject("MainMenuUI");
        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(menuUI.transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 底色：暗夜纸色占位
        var bg = MakeImage(canvasGO.transform, "Bg", new Color(0.055f, 0.042f, 0.032f, 1f));
        Stretch(bg.rectTransform);

        MakeText(canvasGO.transform, "Title", "《一纸灯影》", 110f, new Color(0.95f, 0.88f, 0.72f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1400f, 150f));
        MakeText(canvasGO.transform, "Subtitle", "皮影戏 · 灰盒开发版 v0.4", 30f, new Color(0.72f, 0.6f, 0.45f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(1400f, 50f));

        MakeButton(canvasGO.transform, "开始游戏", new Vector2(0f, 30f), new Vector2(360f, 92f),
            new Color(0.55f, 0.16f, 0.09f, 0.97f), OnStartClicked);
        resetButton = MakeButton(canvasGO.transform, "重置存档", new Vector2(0f, -70f), new Vector2(360f, 72f),
            new Color(0.28f, 0.2f, 0.12f, 0.9f), OnResetClicked);
        MakeButton(canvasGO.transform, "退出", new Vector2(0f, -170f), new Vector2(360f, 72f),
            new Color(0.22f, 0.16f, 0.1f, 0.9f), OnQuitClicked);
    }

    private void OnStartClicked() => ToLevelSelect();

    /// <summary>两步确认，防手滑清档。</summary>
    private void OnResetClicked()
    {
        if (!resetArmed)
        {
            resetArmed = true;
            resetArmUntil = Time.unscaledTime + 3f;
            SetButtonLabel(resetButton, "确认重置？（再点一次）");
            StartCoroutine(ResetArmTimeout());
            return;
        }
        resetArmed = false;
        ResetSave();
        SetButtonLabel(resetButton, "重置存档");
        // 主菜单 UI 是常驻 GameFlow 自建的，重置后无级可刷——下次进选关页自然读到空档
    }

    private IEnumerator ResetArmTimeout()
    {
        while (resetArmed && Time.unscaledTime < resetArmUntil)
            yield return null;
        if (resetArmed)
        {
            resetArmed = false;
            SetButtonLabel(resetButton, "重置存档");
        }
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void SetButtonLabel(Button btn, string text)
    {
        if (btn == null) return;
        var tmp = btn.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = text;
    }

    // ---------- 通用 UI 构件（与 PlayResultUI 同套路；字体一律走序列化引用，STKAITI 不是 TMP 全局默认） ----------

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>(); // 本工程仅新输入系统
    }

    protected Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = BuiltinSprite();
        img.color = color;
        return img;
    }

    protected TMP_Text MakeText(Transform parent, string name, string content, float size, Color color,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = content;
        tmp.raycastTarget = false;
        return tmp;
    }

    protected Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 sizeDelta, Color bg,
        UnityEngine.Events.UnityAction onClick)
    {
        var anchor = new Vector2(0.5f, 0.5f);
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var img = go.AddComponent<Image>();
        img.sprite = BuiltinSprite();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        var lbl = MakeText(go.transform, "Label", label, 34f, new Color(0.98f, 0.94f, 0.85f, 1f),
            anchor, Vector2.zero, sizeDelta);
        lbl.transform.SetParent(go.transform, false);
        return btn;
    }

    protected static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Sprite cachedSprite;
    private static Sprite BuiltinSprite()
    {
        if (cachedSprite == null)
            cachedSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
        return cachedSprite;
    }
}
