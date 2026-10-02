using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// STEP-3 演出结果 UI（自建 Canvas，无需预制）：
///   成功：星级逐个弹出 + "下一关/重玩"；失败：面板抖动 + 提示未达标部件 + 幽灵影红标高亮 2 秒。
/// 失败高亮走 GhostHint.ShowFailHighlightFor（灰盒期红染代替红框描边，美术期再换）。
/// </summary>
public class PlayResultUI : MonoBehaviour
{
    public TMP_FontAsset font;
    [Tooltip("失败反馈时幽灵影高亮时长（秒）")]
    public float failHighlightSeconds = 2f;
    public GhostHint ghostHint;

    private Canvas canvas;
    private GameObject panel;
    private RectTransform panelRect;
    private TMP_Text titleText;
    private TMP_Text starsText;
    private TMP_Text hintText;
    private Button nextButton;
    private Button retryButton;

    private void Awake()
    {
        EnsureEventSystem();
        Build();
        Hide();
    }

    public void ShowSuccess(int stars)
    {
        gameObject.SetActive(true);
        titleText.text = "开演成功！";
        hintText.text = "";
        nextButton.gameObject.SetActive(SceneManager.GetActiveScene().buildIndex + 1 < SceneManager.sceneCountInBuildSettings);
        Show();
        StartCoroutine(PopStars(stars));
    }

    public void ShowFail(List<PoseJudge.FailInfo> fails)
    {
        gameObject.SetActive(true);
        titleText.text = "影子还没摆对";
        starsText.text = "";
        var sb = new System.Text.StringBuilder();
        if (fails != null && fails.Count > 0)
        {
            sb.Append("未达标：");
            for (int i = 0; i < fails.Count && i < 3; i++)
            {
                if (i > 0) sb.Append("，");
                sb.Append($"{fails[i].partName} 差{fails[i].error:0.#}{(fails[i].partName == "影子大小" ? "%" : fails[i].partName == "影子位置" || fails[i].partName == "根位置" ? "" : "°")}");
            }
            sb.Append("（红标高亮 2 秒）");
        }
        hintText.text = sb.ToString();
        nextButton.gameObject.SetActive(false);
        Show();
        if (ghostHint != null) ghostHint.ShowFailHighlightFor(failHighlightSeconds);
        StartCoroutine(Shake());
    }

    public void ShowMessage(string msg)
    {
        gameObject.SetActive(true);
        titleText.text = msg;
        starsText.text = "";
        hintText.text = "";
        nextButton.gameObject.SetActive(false);
        Show();
    }

    private void Show() => panel.SetActive(true);
    private void Hide() => panel.SetActive(false);

    private void OnRetry() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    private void OnNext() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);

    // ---------- 自建 UI ----------

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>(); // 本工程仅新输入系统
    }

    private void Build()
    {
        var canvasGO = new GameObject("ResultCanvas");
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGO.transform, false);
        panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(760f, 430f);
        var bg = panel.AddComponent<Image>();
        bg.sprite = BuiltinSprite();
        bg.color = new Color(0.09f, 0.06f, 0.05f, 0.94f);

        titleText = MakeText(panel.transform, "Title", 52f, new Color(0.96f, 0.9f, 0.78f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(700f, 80f));

        starsText = MakeText(panel.transform, "Stars", 96f, new Color(1f, 0.82f, 0.3f, 1f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(700f, 130f));

        hintText = MakeText(panel.transform, "Hint", 28f, new Color(0.9f, 0.75f, 0.6f, 1f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(700f, 60f));

        retryButton = MakeButton(panel.transform, "重玩", new Vector2(0.5f, 0f), new Vector2(-115f, 36f), new Vector2(190f, 64f),
            new Color(0.55f, 0.18f, 0.1f, 1f), OnRetry);
        nextButton = MakeButton(panel.transform, "下一关", new Vector2(0.5f, 0f), new Vector2(115f, 36f), new Vector2(190f, 64f),
            new Color(0.72f, 0.42f, 0.12f, 1f), OnNext);
    }

    private TMP_Text MakeText(Transform parent, string name, float size, Color color, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
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
        tmp.raycastTarget = false;
        return tmp;
    }

    private Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Color bg, UnityEngine.Events.UnityAction onClick)
    {
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

        var lbl = MakeText(go.transform, "Label", 32f, new Color(0.98f, 0.94f, 0.85f, 1f),
            new Vector2(0.5f, 0.5f), Vector2.zero, sizeDelta);
        lbl.text = label;
        return btn;
    }

    private static Sprite builtinSprite;
    private static Sprite BuiltinSprite()
    {
        if (builtinSprite == null) builtinSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
        return builtinSprite;
    }

    // ---------- 动效 ----------

    private IEnumerator PopStars(int stars)
    {
        starsText.transform.localScale = Vector3.one * 0.6f;
        for (int i = 1; i <= 3; i++)
        {
            if (i <= stars)
                starsText.text = $"<color=#FFD24D>{new string('★', i)}</color><color=#5A4A3A>{new string('★', 3 - i)}</color>";
            else
                starsText.text = $"<color=#FFD24D>{new string('★', stars)}</color><color=#5A4A3A>{new string('★', 3 - stars)}</color>";
            starsText.transform.localScale = Vector3.one * (i <= stars ? 1.12f : 1f);
            yield return new WaitForSeconds(0.28f);
        }
        starsText.transform.localScale = Vector3.one;
    }

    private IEnumerator Shake()
    {
        Vector2 origin = panelRect.anchoredPosition;
        float t = 0f;
        while (t < 0.5f)
        {
            t += Time.deltaTime;
            float k = 1f - t / 0.5f;
            panelRect.anchoredPosition = origin + new Vector2(Mathf.Sin(t * 55f) * 14f * k, 0f);
            yield return null;
        }
        panelRect.anchoredPosition = origin;
    }
}
