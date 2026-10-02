using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// STEP-3 开演编排：持有"开演"按钮（与可选"提示"按钮），点击后汇总各判定达标率：
///   r = 各启用判定的平均达标率（PoseJudge / LampJudge，场景里没接的自动跳过）；
///   r ≥ star1At → 占位演出 + 星级（3★=1，2★≥0.8，1★≥star1At）；否则失败反馈。
/// 成功后锁指针输入直到重玩/下一关（场景重载复位）。
/// </summary>
public class PerformanceDirector : MonoBehaviour
{
    [Header("引用（按关卡接线，可空）")]
    public PoseJudge poseJudge;
    public LampJudge lampJudge;
    public PerformancePlaceholder perf;
    public PlayResultUI resultUI;
    [Tooltip("有幽灵影时显示[提示]按钮")]
    public GhostHint ghostHint;

    [Header("阈值")]
    public float star1At = 0.6f;

    public TMP_FontAsset font;

    public struct EvalResult
    {
        public bool hasCriteria;
        public float passRate;
        public int stars;
        public List<PoseJudge.FailInfo> fails;
    }

    private GameObject playButton;
    private bool locked;

    private void Start()
    {
        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }
        BuildButtons();
    }

    public EvalResult EvaluateAll()
    {
        var e = new EvalResult { hasCriteria = false, passRate = 0f, stars = 0, fails = new List<PoseJudge.FailInfo>() };
        int n = 0;
        float sum = 0f;

        if (poseJudge != null && poseJudge.HasConfig)
        {
            var r = poseJudge.Evaluate();
            sum += r.passRate; n++;
            e.fails.AddRange(r.fails);
        }
        if (lampJudge != null && lampJudge.Enabled)
        {
            bool pass = lampJudge.EvaluatePass(out float errPos, out float errSize);
            sum += pass ? 1f : 0f; n++;
            if (!pass) e.fails.AddRange(lampJudge.GetFailParts());
        }

        if (n == 0) return e;
        e.hasCriteria = true;
        e.passRate = sum / n;
        float s3 = poseJudge != null ? poseJudge.star3At : 1f;
        float s2 = poseJudge != null ? poseJudge.star2At : 0.8f;
        e.stars = PoseJudge.StarsFor(e.passRate, s3, s2, star1At);
        return e;
    }

    public string DescribeState()
    {
        var e = EvaluateAll();
        if (!e.hasCriteria) return "[开演] 本关未接任何判定";
        return $"[开演] 达标率 {e.passRate * 100f:0}% → {e.stars}★（阈值 {star1At * 100f:0}%）";
    }

    private void OnPlayClicked()
    {
        if (locked) return;
        var e = EvaluateAll();
        if (!e.hasCriteria)
        {
            resultUI.ShowMessage("本关未配置判定");
            return;
        }
        if (e.stars >= 1)
        {
            locked = true;
            if (playButton != null) playButton.SetActive(false);
            if (PuppetDragger.Instance != null) PuppetDragger.Instance.enabled = false;
            if (perf != null) perf.Play();
            if (resultUI != null) resultUI.ShowSuccess(e.stars);
        }
        else
        {
            if (resultUI != null) resultUI.ShowFail(e.fails);
        }
    }

    // ---------- 自建按钮 ----------

    private void BuildButtons()
    {
        var canvasGO = new GameObject("StageCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        playButton = MakeButton(canvasGO.transform, "开  演", new Vector2(0f, 60f), new Vector2(250f, 90f),
            new Color(0.55f, 0.16f, 0.09f, 0.97f), OnPlayClicked);

        if (ghostHint != null)
            MakeButton(canvasGO.transform, "提示 Tab", new Vector2(230f, 66f), new Vector2(170f, 60f),
                new Color(0.28f, 0.2f, 0.12f, 0.9f), () => ghostHint.CycleMode());
    }

    private GameObject MakeButton(Transform parent, string label, Vector2 pos, Vector2 sizeDelta, Color bg, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var img = go.AddComponent<Image>();
        img.sprite = PlayResultUIBuiltinSprite();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(go.transform, false);
        var lrt = lblGO.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = sizeDelta;
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
        tmp.fontSize = 34f;
        tmp.color = new Color(0.98f, 0.94f, 0.85f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = label;
        return go;
    }

    private static Sprite cachedSprite;
    private static Sprite PlayResultUIBuiltinSprite()
    {
        if (cachedSprite == null)
            cachedSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
        return cachedSprite;
    }
}
