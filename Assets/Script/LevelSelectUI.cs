using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// STEP-4 选关页（卷轴戏折样式灰盒卡片）：读 GameFlow 关卡数据库 + 存档，按序生成卡片。
/// 卡片：关名 + 星级（★）或"未解锁"；点击已解锁卡片进入关卡。
/// 数据来源只有 LevelDatabase（SO）——加关不需要改这里任何代码。
/// </summary>
public class LevelSelectUI : MonoBehaviour
{
    [Tooltip("UI 字体（构建器接线）")]
    public TMP_FontAsset font;

    private GameFlow flow;

    private void Start()
    {
        GameFlow.EnsureEventSystem();
        flow = GameFlow.EnsureInstance();
        Build();
    }

    private void Build()
    {
        var canvasGO = new GameObject("LevelSelectCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var levels = flow.Levels;

        MakeText(canvasGO.transform, "Title", "选  关", 84f, new Color(0.95f, 0.88f, 0.72f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(800f, 110f));

        if (levels == null || levels.Count == 0)
        {
            MakeText(canvasGO.transform, "EmptyTip", "关卡数据库未配置\n（请从 Boot/MainMenu 场景进入，或在 GameFlow 上接线 LevelDatabase）",
                34f, new Color(0.85f, 0.6f, 0.4f, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 200f));
            MakeButton(canvasGO.transform, "返回主菜单", new Vector2(0f, -430f), new Vector2(300f, 70f),
                new Color(0.28f, 0.2f, 0.12f, 0.9f), () => flow.ToMainMenu());
            return;
        }

        // 卡片网格：每行 4 张，260×140，间距 40
        const int perRow = 4;
        const float cardW = 260f, cardH = 150f, gapX = 40f, gapY = 46f;
        float totalW = (Mathf.Min(levels.Count, perRow) - 1) * (cardW + gapX);
        float startX = -totalW / 2f;

        for (int i = 0; i < levels.Count; i++)
        {
            var cfg = levels[i];
            if (cfg == null) continue;
            bool unlocked = flow.IsUnlocked(i);
            int row = i / perRow, col = i % perRow;
            var pos = new Vector2(startX + col * (cardW + gapX), 40f - row * (cardH + gapY));
            BuildCard(canvasGO.transform, i, cfg, unlocked, pos, new Vector2(cardW, cardH));
        }

        MakeButton(canvasGO.transform, "返回主菜单", new Vector2(0f, -430f), new Vector2(300f, 70f),
            new Color(0.28f, 0.2f, 0.12f, 0.9f), () => flow.ToMainMenu());
    }

    private void BuildCard(Transform parent, int index, LevelConfig cfg, bool unlocked, Vector2 pos, Vector2 size)
    {
        var go = new GameObject($"Card_{cfg.displayName}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.sprite = BuiltinSprite();
        img.color = unlocked ? new Color(0.14f, 0.1f, 0.07f, 0.96f) : new Color(0.09f, 0.075f, 0.06f, 0.9f);
        var btn = go.AddComponent<Button>();
        btn.interactable = unlocked;
        if (unlocked)
        {
            int idx = index;
            btn.onClick.AddListener(() => flow.StartLevel(idx));
        }

        var stars = flow.GetStars(index);
        MakeText(go.transform, "Name", cfg.displayName, 34f,
            unlocked ? new Color(0.95f, 0.88f, 0.72f, 1f) : new Color(0.5f, 0.44f, 0.36f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(size.x - 20f, 50f));

        if (unlocked)
        {
            string starText = $"<color=#FFD24D>{new string('★', stars)}</color><color=#4A3F30>{new string('★', 3 - stars)}</color>";
            MakeText(go.transform, "Stars", stars > 0 ? starText : "☆☆☆", 40f, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(size.x - 20f, 60f));
            MakeText(go.transform, "Tip", "进入", 24f, new Color(0.8f, 0.65f, 0.45f, 0.9f),
                new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(size.x - 20f, 34f));
        }
        else
        {
            MakeText(go.transform, "Lock", "未解锁", 32f, new Color(0.5f, 0.44f, 0.36f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(size.x - 20f, 50f));
            MakeText(go.transform, "Tip", "通关上一关解锁", 20f, new Color(0.42f, 0.36f, 0.3f, 0.9f),
                new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(size.x - 20f, 30f));
        }
    }

    // ---------- UI 构件（与 GameFlow 同套路） ----------

    private TMP_Text MakeText(Transform parent, string name, string content, float size, Color color,
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

    private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 sizeDelta, Color bg,
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

        var lbl = MakeText(go.transform, "Label", label, 30f, new Color(0.98f, 0.94f, 0.85f, 1f),
            anchor, Vector2.zero, sizeDelta);
        lbl.transform.SetParent(go.transform, false);
        return btn;
    }

    private static Sprite cachedSprite;
    private static Sprite BuiltinSprite()
    {
        if (cachedSprite == null)
            cachedSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
        return cachedSprite;
    }
}
