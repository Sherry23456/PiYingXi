using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// STEP-4 关卡运行时：读 LevelConfig 数据表把整关搭出来，并驱动状态机
///   Intro（剧情字条）→ Solving（摆影/移灯）→ Performing（Timeline 或占位演出）→ Result（星级/知识卡）。
/// 判定注册：每张皮影一张 PoseJudge（目标姿态来自数据），每盏参与判定的灯一张 LampJudge；
/// 开演时汇总各判定达标率取平均（与 STEP-3 PerformanceDirector 同口径）。
/// 加一关不需要改本脚本——建 SO 数据表并拖进 LevelDatabase 即可。
/// </summary>
public class LevelManager : MonoBehaviour
{
    public enum State { Intro, Solving, Performing, Result }

    [Header("场景接线（构建器填）")]
    public TMP_FontAsset font;
    [Tooltip("灯占位图")] public Sprite lanternSprite;
    [Tooltip("目标环占位图")] public Sprite ringSprite;
    [Tooltip("被灯照亮的幕布/皮影材质（Sprite-Lit）")]
    public Material litMaterial;
    [Tooltip("无光照材质（目标环）")]
    public Material unlitMaterial;
    [Tooltip("占位锣声")]
    public AudioClip gong;
    [Tooltip("直接打开 Gameplay 场景调试时的兜底关卡；正常流程读 GameFlow.CurrentLevel")]
    public LevelConfig fallbackLevel;

    public State CurrentState => state;
    public LevelConfig LoadedConfig => cfg;

    private State state = State.Intro;
    private LevelConfig cfg;
    private GameFlow flow;

    private readonly List<PoseJudge> poseJudges = new List<PoseJudge>();
    private readonly List<LampJudge> lampJudges = new List<LampJudge>();
    private readonly List<DragTransform> lampDrags = new List<DragTransform>();
    private readonly List<ShadowPuppet> puppets = new List<ShadowPuppet>();
    private Light2D firstLampLight;

    private GhostHint ghostHint;
    private PerformancePlaceholder perf;
    private PlayableDirector director;
    private PlayResultUI resultUI;

    private GameObject introUI;
    private GameObject stageButtons;
    private GameObject hintUI;
    private int inputEnableAtFrame;

    // ---------- 生命周期 ----------

    private void Start()
    {
        flow = GameFlow.EnsureInstance();
        cfg = flow.CurrentLevel != null ? flow.CurrentLevel : fallbackLevel;
        if (cfg == null)
        {
            Debug.LogError("[LevelManager] 未配置关卡：请从主菜单/选关进入，或在 LevelManager 上接 fallbackLevel。");
            return;
        }
        Load(cfg);
    }

    /// <summary>按数据表把整关搭出来（皮影/灯/道具/判定/提示/UI），落到 Intro 状态。</summary>
    public void Load(LevelConfig config)
    {
        cfg = config;
        BuildWorld();
        BuildStage();
        BuildUI();
        EnterIntro();
    }

    // ---------- 建关 ----------

    private void BuildWorld()
    {
        // 皮影 + 姿态判定
        float rootTol = cfg.tolerance != null && cfg.tolerance.useOverride ? cfg.tolerance.rootPosTolerance : 0.35f;
        float angleTol = cfg.tolerance != null && cfg.tolerance.useOverride ? cfg.tolerance.angleTolerance : 8f;
        foreach (var entry in cfg.puppets)
        {
            if (entry == null || entry.prefab == null) continue;
            var go = Instantiate(entry.prefab, new Vector3(entry.initialPos.x, entry.initialPos.y, 0f), Quaternion.identity);
            go.name = entry.prefab.name;
            var sp = go.GetComponent<ShadowPuppet>();
            if (sp == null)
            {
                Debug.LogError($"[LevelManager] 预制体 {entry.prefab.name} 根上没有 ShadowPuppet，跳过");
                Destroy(go);
                continue;
            }
            sp.draggable = entry.canDrag;
            puppets.Add(sp);

            if (entry.judgePose)
            {
                var judgeGO = new GameObject($"_Judge_{go.name}");
                var pj = judgeGO.AddComponent<PoseJudge>();
                pj.puppet = sp;
                pj.targetRootPos = entry.targetPos;
                pj.rootPosTolerance = rootTol;
                foreach (var a in entry.targetAngles)
                    pj.parts.Add(new PoseJudge.PartTarget
                    {
                        partName = a.partName,
                        targetAngle = a.angle,
                        overrideAngleTol = cfg.tolerance != null && cfg.tolerance.useOverride,
                        angleTolerance = angleTol
                    });
                poseJudges.Add(pj);
            }

            foreach (var a in entry.initialAngles)
                foreach (var j in sp.joints)
                    if (j != null && j.name == a.partName) j.RotateTo(a.angle);
        }

        // 灯 + 移灯判定 + 目标环
        float shadowPosTol = cfg.tolerance != null && cfg.tolerance.useOverride ? cfg.tolerance.shadowPosTolerance : 0.5f;
        float shadowSizeTol = cfg.tolerance != null && cfg.tolerance.useOverride ? cfg.tolerance.shadowSizeTolerance : 0.25f;
        foreach (var entry in cfg.lamps)
        {
            if (entry == null) continue;
            var lamp = MakeLantern(entry);
            if (entry.canDrag) lampDrags.Add(lamp.GetComponent<DragTransform>());
            if (firstLampLight == null) firstLampLight = lamp.GetComponentInChildren<Light2D>();

            var target = entry.shadowTarget;
            if (target != null && target.judge && puppets.Count > 0)
            {
                var ringGO = new GameObject("Target_Ring");
                var sr = ringGO.AddComponent<SpriteRenderer>();
                sr.sprite = ringSprite;
                sr.material = unlitMaterial != null ? unlitMaterial : sr.material;
                sr.sortingOrder = -9; // 幕布之上皮影之下
                sr.color = new Color(1f, 0.78f, 0.4f, 0.55f);

                var judgeGO = new GameObject($"_Judge_{entry.lampName}");
                var lj = judgeGO.AddComponent<LampJudge>();
                lj.lamp = lamp.transform;
                lj.lampLight = lamp.GetComponentInChildren<Light2D>();
                lj.shadowCaster = puppets[0].transform;
                lj.targetShadowPos = target.targetPos;
                lj.targetShadowRadius = target.targetRadius;
                lj.posTolerance = shadowPosTol;
                lj.sizeTolerance = shadowSizeTol;
                lj.ringRenderer = sr;
                lampJudges.Add(lj);
            }
        }

        // 道具
        foreach (var entry in cfg.props)
        {
            if (entry == null || entry.prefab == null) continue;
            var go = Instantiate(entry.prefab, new Vector3(entry.pos.x, entry.pos.y, 0f), Quaternion.identity);
            go.transform.localScale = Vector3.one * Mathf.Max(entry.scale, 0.01f);
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>())
                sr.sortingOrder = entry.sortingOrder;
        }
    }

    private GameObject MakeLantern(LevelConfig.LampEntry entry)
    {
        var root = new GameObject(entry.lampName);
        root.transform.position = new Vector3(entry.pos.x, entry.pos.y, 0f);

        var spriteGO = new GameObject("LanternSprite");
        spriteGO.transform.SetParent(root.transform, false);
        var sr = spriteGO.AddComponent<SpriteRenderer>();
        sr.sprite = lanternSprite;
        sr.material = litMaterial != null ? litMaterial : sr.material;
        sr.sortingOrder = 10;
        spriteGO.AddComponent<CircleCollider2D>().radius = 0.55f;

        var lightGO = new GameObject("Light");
        lightGO.transform.SetParent(root.transform, false);
        var l2d = lightGO.AddComponent<Light2D>();
        l2d.lightType = Light2D.LightType.Point;
        l2d.intensity = entry.intensity;
        l2d.color = entry.color;
        l2d.pointLightOuterRadius = entry.outerRadius;

        var drag = root.AddComponent<DragTransform>();
        drag.enabled = false; // 开场锁输入，Solving 才开

        return root;
    }

    private void BuildStage()
    {
        var stage = new GameObject("_Stage");

        // 占位演出（STEP-8 换 Timeline：cfg.performanceTimeline 有值则走 PlayableDirector）
        perf = stage.AddComponent<PerformancePlaceholder>();
        perf.gong = gong;
        if (firstLampLight != null) perf.flickerLight = firstLampLight;

        if (cfg.performanceTimeline != null)
        {
            director = stage.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = cfg.performanceTimeline;
        }

        // 幽灵影（绑第一张皮影的判定；灰盒期一关一影，多皮影提示后续再扩）
        if (poseJudges.Count > 0)
        {
            var ghostGO = new GameObject("_GhostHint");
            ghostHint = ghostGO.AddComponent<GhostHint>();
            ghostHint.judge = poseJudges[0];
            if (flow?.Save != null)
                ghostHint.defaultMode = (GhostHint.Mode)Mathf.Clamp(flow.Save.ghostHintDefault, 0, 2);
        }

        resultUI = stage.AddComponent<PlayResultUI>();
        resultUI.font = font;
        resultUI.ghostHint = ghostHint;
    }

    // ---------- UI ----------

    private void BuildUI()
    {
        // 开演 / 提示按钮（Solving 才显示）
        stageButtons = new GameObject("StageUI");
        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(stageButtons.transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        MakeButton(canvasGO.transform, "开  演", new Vector2(0f, 60f), new Vector2(250f, 90f),
            new Color(0.55f, 0.16f, 0.09f, 0.97f), OnPlayClicked);
        if (ghostHint != null)
            MakeButton(canvasGO.transform, "提示 Tab", new Vector2(230f, 66f), new Vector2(170f, 60f),
                new Color(0.28f, 0.2f, 0.12f, 0.9f), () => ghostHint.CycleMode());
        stageButtons.SetActive(false);

        // 提示条（右上角深色衬底：压在灯光亮池上也读得清）
        if (!string.IsNullOrEmpty(cfg.hintText))
        {
            hintUI = new GameObject("HintUI");
            var hintCanvasGO = new GameObject("Canvas");
            hintCanvasGO.transform.SetParent(hintUI.transform, false);
            var hintCanvas = hintCanvasGO.AddComponent<Canvas>();
            hintCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hintCanvas.sortingOrder = 5;
            var hintScaler = hintCanvasGO.AddComponent<CanvasScaler>();
            hintScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            hintScaler.referenceResolution = new Vector2(1920f, 1080f);
            hintScaler.matchWidthOrHeight = 0.5f;

            var chipGO = new GameObject("HintChip");
            chipGO.transform.SetParent(hintCanvasGO.transform, false);
            var chipRt = chipGO.AddComponent<RectTransform>();
            chipRt.anchorMin = chipRt.anchorMax = new Vector2(1f, 1f);
            chipRt.pivot = new Vector2(1f, 1f);
            chipRt.anchoredPosition = new Vector2(-30f, -24f);
            chipRt.sizeDelta = new Vector2(1240f, 76f);
            var chip = chipGO.AddComponent<Image>();
            chip.sprite = BuiltinSprite();
            chip.color = new Color(0.08f, 0.06f, 0.05f, 0.55f);

            var tmp = MakeText(chipGO.transform, "LevelHint", cfg.hintText, 34f,
                new Color(0.94f, 0.87f, 0.72f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 60f));
            hintUI.SetActive(false);
        }

        // 开场剧情字条
        introUI = new GameObject("IntroUI");
        var introCanvasGO = new GameObject("Canvas");
        introCanvasGO.transform.SetParent(introUI.transform, false);
        var introCanvas = introCanvasGO.AddComponent<Canvas>();
        introCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        introCanvas.sortingOrder = 30;
        var introScaler = introCanvasGO.AddComponent<CanvasScaler>();
        introScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        introScaler.referenceResolution = new Vector2(1920f, 1080f);
        introScaler.matchWidthOrHeight = 0.5f;
        introCanvasGO.AddComponent<GraphicRaycaster>();

        var dim = MakeImage(introCanvasGO.transform, "Dim", new Color(0f, 0f, 0f, 0.82f));
        Stretch(dim.rectTransform);

        var panel = MakeImage(introCanvasGO.transform, "Panel", new Color(0.09f, 0.07f, 0.05f, 0.97f));
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(980f, 640f);

        MakeText(panel.transform, "LevelName", cfg.displayName, 52f, new Color(0.96f, 0.9f, 0.78f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(880f, 70f));
        MakeText(panel.transform, "Story", string.IsNullOrEmpty(cfg.storyIntro) ? "（本关暂无剧情字条）" : cfg.storyIntro,
            36f, new Color(0.92f, 0.85f, 0.7f, 1f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(860f, 360f));
        MakeText(panel.transform, "Tip", "轻点任意处 · 开演准备", 26f, new Color(0.65f, 0.55f, 0.4f, 1f),
            new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(860f, 40f));
    }

    // ---------- 状态机 ----------

    private void EnterIntro()
    {
        state = State.Intro;
        SetWorldInput(false);
        if (introUI != null) introUI.SetActive(true);
        if (stageButtons != null) stageButtons.SetActive(false);
        if (hintUI != null) hintUI.SetActive(false);
    }

    private void EnterSolving()
    {
        state = State.Solving;
        if (introUI != null) introUI.SetActive(false);
        if (stageButtons != null) stageButtons.SetActive(true);
        if (hintUI != null) hintUI.SetActive(true);
        inputEnableAtFrame = Time.frameCount + 2; // 消掉关闭字条的这次点击，别顺手抓走关节
    }

    private void OnPlayClicked()
    {
        if (state != State.Solving) return;
        var e = EvaluateAll();
        if (!e.hasCriteria)
        {
            resultUI.ShowMessage("本关未配置判定");
            return;
        }
        if (e.stars >= 1) EnterPerforming(e.stars);
        else resultUI.ShowFail(e.fails);
    }

    private void EnterPerforming(int stars)
    {
        state = State.Performing;
        if (stageButtons != null) stageButtons.SetActive(false);
        if (hintUI != null) hintUI.SetActive(false);
        SetWorldInput(false);
        if (director != null)
        {
            director.Play();
            StartCoroutine(WaitTimelineThenResult(stars));
        }
        else if (perf != null)
        {
            perf.Play();
            StartCoroutine(WaitPlaceholderThenResult(stars));
        }
        else
        {
            EnterResult(stars);
        }
    }

    private IEnumerator WaitPlaceholderThenResult(int stars)
    {
        yield return null;
        while (perf != null && perf.IsPlaying) yield return null;
        EnterResult(stars);
    }

    private IEnumerator WaitTimelineThenResult(int stars)
    {
        yield return null;
        float timeout = director != null ? (float)director.duration + 5f : 2f;
        while (director != null && director.state == PlayState.Playing && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        EnterResult(stars);
    }

    private void EnterResult(int stars)
    {
        state = State.Result;
        flow?.CompleteLevel(cfg.levelId, stars);

        bool hasNext = flow != null && flow.ValidIndex(flow.CurrentLevelIndex + 1);
        if (resultUI != null)
        {
            resultUI.nextLabel = hasNext ? "下一关" : "回选关";
            resultUI.onNext = hasNext
                ? (System.Action)(() => flow.NextLevel())
                : () => flow.ToLevelSelect();
            resultUI.onRetry = () => SceneManager.LoadScene(GameFlow.SceneGameplay);
            resultUI.ShowSuccess(stars, cfg.knowledgeCard, cfg.knowledgePendingReview);
        }
    }

    // ---------- 判定汇总（口径同 STEP-3 PerformanceDirector） ----------

    public struct EvalResult
    {
        public bool hasCriteria;
        public float passRate;
        public int stars;
        public List<PoseJudge.FailInfo> fails;
    }

    public EvalResult EvaluateAll()
    {
        var e = new EvalResult { hasCriteria = false, passRate = 0f, stars = 0, fails = new List<PoseJudge.FailInfo>() };
        int n = 0;
        float sum = 0f;

        foreach (var pj in poseJudges)
            if (pj != null && pj.HasConfig)
            {
                var r = pj.Evaluate();
                sum += r.passRate; n++;
                e.fails.AddRange(r.fails);
            }
        foreach (var lj in lampJudges)
            if (lj != null && lj.Enabled)
            {
                bool pass = lj.EvaluatePass(out _, out _);
                sum += pass ? 1f : 0f; n++;
                if (!pass) e.fails.AddRange(lj.GetFailParts());
            }

        if (n == 0) return e;
        e.hasCriteria = true;
        e.passRate = sum / n;
        e.stars = PoseJudge.StarsFor(e.passRate, 1f, 0.8f, 0.6f);
        return e;
    }

    // ---------- 输入锁 ----------

    private void SetWorldInput(bool enabled)
    {
        if (PuppetDragger.Instance != null) PuppetDragger.Instance.enabled = enabled;
        foreach (var dt in lampDrags)
            if (dt != null) dt.enabled = enabled;
    }

    // ---------- 每帧 ----------

    private void Update()
    {
        switch (state)
        {
            case State.Intro:
                if (DismissPressed()) EnterSolving();
                break;
            case State.Solving:
                if (Time.frameCount >= inputEnableAtFrame) SetWorldInput(true);
                break;
        }

        // 幽灵影默认档随 Tab 循环即时落档（存档字段：ghostHintDefault）
        if (ghostHint != null && flow?.Save != null)
        {
            int tier = Mathf.Clamp((int)ghostHint.CurrentMode, 0, 2);
            if (tier != flow.Save.ghostHintDefault)
            {
                flow.Save.ghostHintDefault = tier;
                SaveSystem.Save(flow.Save);
            }
        }
    }

    private static bool DismissPressed()
    {
        var pointer = Pointer.current;
        if (pointer != null && pointer.press.wasPressedThisFrame) return true;
        var kb = Keyboard.current;
        return kb != null && kb.anyKey.wasPressedThisFrame;
    }

    // ---------- 通用 UI 构件 ----------

    private Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = BuiltinSprite();
        img.color = color;
        return img;
    }

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
        var anchor = new Vector2(0.5f, 0f);
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
        return btn;
    }

    private static void Stretch(RectTransform rt)
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
