using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// STEP-4 框架一键构建器（关卡框架与系统）。
/// 菜单：LanternKeeper > STEP-4 > 构建框架（数据驱动关卡）
/// 自动完成：
///   1) 灰盒 SO ×3（Level_A/B/C，数据与 STEP-3 三关一一对应）+ LevelDatabase
///   2) Boot     ：GameFlow（持久单例，开场跳主菜单）
///   3) MainMenu ：主菜单（UI 由 GameFlow 运行时自建，标题《一纸灯影》）
///   4) LevelSelect：选关页（LevelSelectUI 运行时按数据库生成卡片）
///   5) Gameplay ：通用关卡场景——LevelManager 按数据表搭关（皮影/灯/环/判定/UI 全数据驱动）
///   6) Build Settings：Boot → MainMenu → LevelSelect → Gameplay（老三关移出构建，资产保留）
/// 判定目标数值沿用 STEP-3 校准值；之后加一关 = 建 LevelConfig SO 拖进 LevelDatabase，不碰代码。
/// </summary>
public static class FrameworkSceneBuilder
{
    const string SceneDir = "Assets/Scenes";
    const string SoDir = "Assets/SO";

    static TMP_FontAsset font;
    static Sprite lanternSprite, ringSprite, curtainSprite;
    static Material litMat, unlitMat;
    static AudioClip gong;
    static GameObject greyboxPuppet, wukongPuppet;

    [MenuItem("LanternKeeper/STEP-4/构建框架（数据驱动关卡）")]
    public static void BuildAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/STKAITI SDF.asset");
        lanternSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Spike/Prop_Lantern.png");
        ringSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Greybox/Target_Ring.png");
        curtainSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Spike/BG_Curtain.png");
        litMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Spike/Mat_SpriteLit.mat");
        unlitMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Greybox/Mat_UnlitSprite.mat");
        gong = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX_Gong_Placeholder.asset");
        greyboxPuppet = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/GreyboxPuppet.prefab");
        wukongPuppet = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Puppet_Wukong_Grey.prefab");

        if (font == null || lanternSprite == null || ringSprite == null || curtainSprite == null ||
            litMat == null || unlitMat == null || gong == null || greyboxPuppet == null || wukongPuppet == null)
        {
            Debug.LogError("[STEP-4] 公共资产缺失——请先执行 LanternKeeper/STEP-3/构建三关 生成灰盒占位资产，再跑本构建器。");
            return;
        }

        EnsureFolder(SoDir);
        EnsureFolder(SceneDir);

        var cfgA = BuildConfigA();
        var cfgB = BuildConfigB();
        var cfgC = BuildConfigC();
        var db = BuildDatabase(cfgA, cfgB, cfgC);

        BuildBoot(db);
        BuildMainMenu(db);
        BuildLevelSelect();
        BuildGameplay(cfgA);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(SceneDir + "/Boot.unity", true),
            new EditorBuildSettingsScene(SceneDir + "/MainMenu.unity", true),
            new EditorBuildSettingsScene(SceneDir + "/LevelSelect.unity", true),
            new EditorBuildSettingsScene(SceneDir + "/Gameplay.unity", true),
        };

        AssetDatabase.SaveAssets();
        PrefillFontGlyphs(db);
        EditorSceneManager.OpenScene(SceneDir + "/MainMenu.unity");
        Debug.Log("[STEP-4] 框架构建完成：Boot→MainMenu→LevelSelect→Gameplay。Play 验证：选关→Intro字条→摆影/移灯→开演→星级+知识卡→存档亮星；主菜单可重置存档。空跑演练：复制 Assets/SO 下任一关 SO 改数值，拖进 LevelDatabase 即出现在选关页。");
    }

    /// <summary>
    /// 字体资产重建 + 字形预灌（批处理可用，静态入口：RebuildFontAssetBatch）。
    /// 背景：实测本机 Unity 6 (6000.5.3f1) 的 TMP Dynamic 运行时加字形位图相对 glyphRect 偏移约一个 padding，
    /// 小字号渲染成碎片（选关卡片乱码）；且老资产经反复清空/改尺寸后 TryAddCharacters 全部失败。
    /// 方案：用 CreateFontAsset 从 STKAITI.TTF 重建干净资产（Dynamic+MultiAtlas），把全部关卡 SO 文案
    /// 与 UI 固定文案字符经 TryAddCharacters 批量预灌后落盘——运行时命中已有字形，不再动态加字。
    /// 覆盖保存到原路径，.meta 保留 → GUID 不变，所有引用不断。
    /// 加新关若出现缺字（渲染成方块/乱码），重跑本方法即可。
    /// </summary>
    [MenuItem("LanternKeeper/STEP-4/重建字体资产与字形预灌（改文案后乱码/缺字时用）")]
    public static void RebuildFontAssetMenu()
    {
        var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(SoDir + "/LevelDatabase.asset");
        RebuildFontAsset(db);
    }

    /// <summary>批处理入口：Unity.exe -batchmode -projectPath ... -executeMethod FrameworkSceneBuilder.RebuildFontAssetBatch -quit</summary>
    public static void RebuildFontAssetBatch()
    {
        var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(SoDir + "/LevelDatabase.asset");
        RebuildFontAsset(db);
    }

    static void RebuildFontAsset(LevelDatabase db)
    {
        const string fontPath = "Assets/Fonts/STKAITI SDF.asset";
        var ttf = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/STKAITI.TTF");
        if (ttf == null) { Debug.LogError("[STEP-4] 找不到 STKAITI.TTF"); return; }

        var fa = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA_HINTED,
            1024, 1024, TMPro.AtlasPopulationMode.Dynamic, true);
        fa.name = "STKAITI SDF";

        // 收集全部文案字符：SO 文案 + UI 固定文案
        var sb = new System.Text.StringBuilder();
        if (db != null)
            foreach (var cfg in db.levels)
            {
                if (cfg == null) continue;
                sb.Append(cfg.displayName).Append(cfg.storyIntro).Append(cfg.knowledgeCard).Append(cfg.hintText);
            }
        sb.Append("《一纸灯影》皮影戏灰盒开发版v0.4开始重置存退出游戏选关返回主菜单");
        sb.Append("进入未解锁通关上一解锁第一二三四五六七八九十折纯摆移混合");
        sb.Append("轻点任意处·开演准备本关暂无剧情字条影子还没摆对！未达标：根位置差（红标高2秒）下一回选知识卡待审校");
        sb.Append("本关配置判定提示Tab★☆☆√×0123456789.-—…：；、，。！？（）ABCabc");
        var seen = new HashSet<char>();
        var text = new System.Text.StringBuilder();
        foreach (char c in sb.ToString())
            if (seen.Add(c)) text.Append(c);

        string missing;
        bool ok = fa.TryAddCharacters(text.ToString(), out missing, false);
        if (!ok) { Debug.LogError("[STEP-4] 字形批量预灌失败，缺字：" + missing); return; }

        AssetDatabase.CreateAsset(fa, fontPath); // 覆盖保存，.meta 保留 → GUID 不变
        if (fa.material != null && AssetDatabase.GetAssetPath(fa.material) != fontPath)
            AssetDatabase.AddObjectToAsset(fa.material, fa);
        if (fa.atlasTextures != null)
            foreach (var tex in fa.atlasTextures)
                if (tex != null && AssetDatabase.GetAssetPath(tex) != fontPath)
                    AssetDatabase.AddObjectToAsset(tex, fa);
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        Debug.Log($"[STEP-4] 字体资产重建完成：预灌 {text.Length} 个独立字符，字形表 {fa.glyphTable.Count}，图集 {fa.atlasTextures.Length} 页，缺字=[{missing}]。");
    }

    static void PrefillFontGlyphs(LevelDatabase db)
    {
        RebuildFontAsset(db);
    }

    // ---------- 数据表 ----------

    static LevelConfig BuildConfigA()
    {
        var cfg = CreateOrLoad<LevelConfig>(SoDir + "/Level_A_PurePose.asset");
        cfg.levelId = 1;
        cfg.displayName = "关A · 纯摆";
        cfg.storyIntro = "夜幕落，戏台亮。\n你接过师傅递来的影人——\n先照着先人的影子，摆正它。";
        cfg.knowledgeCard = "皮影戏又称“影子戏”，用灯光把兽皮镂刻的影人投在幕布上表演，2011 年入选人类非物质文化遗产代表作名录。";
        cfg.knowledgePendingReview = true;
        cfg.hintText = "关A · 纯摆：把皮影摆得和幽灵影一致，再点[开演]";

        cfg.puppets.Clear();
        var pe = new LevelConfig.PuppetEntry
        {
            prefab = greyboxPuppet,
            initialPos = new Vector2(-4.5f, -1.6f),
            canDrag = true,
            judgePose = true,
            targetPos = new Vector2(1.8f, -1.6f),
        };
        pe.initialAngles.Add(new LevelConfig.NamedAngle { partName = "ArmL", angle = 0f });
        pe.initialAngles.Add(new LevelConfig.NamedAngle { partName = "ArmR", angle = 0f });
        pe.targetAngles.Add(new LevelConfig.NamedAngle { partName = "ArmL", angle = 18f });
        pe.targetAngles.Add(new LevelConfig.NamedAngle { partName = "ArmR", angle = -18f });
        cfg.puppets.Add(pe);

        cfg.lamps.Clear();
        cfg.lamps.Add(new LevelConfig.LampEntry
        {
            lampName = "Lantern_Warm",
            pos = new Vector2(0.6f, 3.4f),
            color = new Color(1f, 0.82f, 0.55f, 1f),
            intensity = 1f,
            outerRadius = 10f,
            canDrag = true,
        });

        cfg.tolerance = new LevelConfig.ToleranceOverride { useOverride = false };
        EditorUtility.SetDirty(cfg);
        return cfg;
    }

    static LevelConfig BuildConfigB()
    {
        var cfg = CreateOrLoad<LevelConfig>(SoDir + "/Level_B_MoveLamp.asset");
        cfg.levelId = 2;
        cfg.displayName = "关B · 移灯";
        cfg.storyIntro = "灯是影子的魂。\n灯挪一寸，影变一分。\n把影子送进那圈月光里。";
        cfg.knowledgeCard = "“一口叙说千古事，双手对舞百万兵”——灯离影人越近，幕布上的影子越大；灯偏移，影子也随之偏斜。";
        cfg.knowledgePendingReview = true;
        cfg.hintText = "关B · 移灯：拖动灯，让影子套进环形目标线";

        cfg.puppets.Clear();
        var pe = new LevelConfig.PuppetEntry
        {
            prefab = greyboxPuppet,
            initialPos = new Vector2(0f, -1.6f),
            canDrag = false,
            judgePose = false,
            targetPos = new Vector2(0f, -1.6f),
        };
        pe.initialAngles.Add(new LevelConfig.NamedAngle { partName = "ArmL", angle = 0f });
        pe.initialAngles.Add(new LevelConfig.NamedAngle { partName = "ArmR", angle = 0f });
        cfg.puppets.Add(pe);

        cfg.lamps.Clear();
        var le = new LevelConfig.LampEntry
        {
            lampName = "Lantern_Warm",
            pos = new Vector2(6.2f, 4.0f),
            color = new Color(1f, 0.82f, 0.55f, 1f),
            intensity = 1f,
            outerRadius = 6f,
            canDrag = true,
        };
        le.shadowTarget = new LevelConfig.ShadowTarget
        {
            judge = true,
            targetPos = new Vector2(0.04f, -2.09f), // 灯到 (-0.4,3.4) 时按模型反推（R=6, centerStretch=0.5, sizeGain=1）
            targetRadius = 2.27f,
        };
        cfg.lamps.Add(le);

        cfg.tolerance = new LevelConfig.ToleranceOverride { useOverride = false };
        EditorUtility.SetDirty(cfg);
        return cfg;
    }

    static LevelConfig BuildConfigC()
    {
        var cfg = CreateOrLoad<LevelConfig>(SoDir + "/Level_C_Mixed.asset");
        cfg.levelId = 3;
        cfg.displayName = "关C · 混合";
        cfg.storyIntro = "手要稳，灯要准。\n影人摆正，月光套影——\n好戏，这就开演。";
        cfg.knowledgeCard = "传统影人多用牛皮、驴皮镂刻上色，故有“驴皮影”之称；影人通体透亮，靠的是皮的通透与灯的巧用。";
        cfg.knowledgePendingReview = true;
        cfg.hintText = "关C · 混合：摆对皮影 + 影子进环，再点[开演]";

        cfg.puppets.Clear();
        var pe = new LevelConfig.PuppetEntry
        {
            prefab = wukongPuppet,
            initialPos = new Vector2(-4.5f, -1.6f),
            canDrag = true,
            judgePose = true,
            targetPos = new Vector2(1.6f, -1.6f),
        };
        pe.initialAngles.Add(new LevelConfig.NamedAngle { partName = "ArmL", angle = 0f });
        pe.initialAngles.Add(new LevelConfig.NamedAngle { partName = "ArmR", angle = 0f });
        pe.targetAngles.Add(new LevelConfig.NamedAngle { partName = "ArmL", angle = 15f });
        pe.targetAngles.Add(new LevelConfig.NamedAngle { partName = "ArmR", angle = -15f });
        cfg.puppets.Add(pe);

        cfg.lamps.Clear();
        var le = new LevelConfig.LampEntry
        {
            lampName = "Lantern_Warm",
            pos = new Vector2(5.8f, 4.2f),
            color = new Color(1f, 0.82f, 0.55f, 1f),
            intensity = 1f,
            outerRadius = 6f,
            canDrag = true,
        };
        le.shadowTarget = new LevelConfig.ShadowTarget
        {
            judge = true,
            targetPos = new Vector2(1.74f, -2.08f), // 皮影在 (1.6,-1.6)、灯在 (0.2,3.2) 时按模型反推（R=6）
            targetRadius = 2.52f,
        };
        cfg.lamps.Add(le);

        cfg.tolerance = new LevelConfig.ToleranceOverride { useOverride = false };
        EditorUtility.SetDirty(cfg);
        return cfg;
    }

    static LevelDatabase BuildDatabase(params LevelConfig[] configs)
    {
        var db = CreateOrLoad<LevelDatabase>(SoDir + "/LevelDatabase.asset");
        db.levels.Clear();
        foreach (var c in configs) db.levels.Add(c);
        EditorUtility.SetDirty(db);
        return db;
    }

    // ---------- 场景 ----------

    static void BuildBoot(LevelDatabase db)
    {
        var scene = NewSceneShell("Boot", new Color(0.02f, 0.02f, 0.03f, 1f), withListener: false);

        var flowGO = new GameObject("GameFlow");
        var flow = flowGO.AddComponent<GameFlow>();
        flow.database = db;
        flow.font = font;

        EditorSceneManager.SaveScene(scene, SceneDir + "/Boot.unity");
    }

    static void BuildMainMenu(LevelDatabase db)
    {
        var scene = NewSceneShell("MainMenu", new Color(0.055f, 0.042f, 0.032f, 1f), withListener: true);

        var flowGO = new GameObject("GameFlow"); // 直接打开本场景时自举；Boot 流程下会与本体重复并被单例去重
        var flow = flowGO.AddComponent<GameFlow>();
        flow.database = db;
        flow.font = font;

        EditorSceneManager.SaveScene(scene, SceneDir + "/MainMenu.unity");
    }

    static void BuildLevelSelect()
    {
        var scene = NewSceneShell("LevelSelect", new Color(0.055f, 0.042f, 0.032f, 1f), withListener: true);

        var uiGO = new GameObject("LevelSelectUI");
        var ui = uiGO.AddComponent<LevelSelectUI>();
        ui.font = font;

        EditorSceneManager.SaveScene(scene, SceneDir + "/LevelSelect.unity");
    }

    static void BuildGameplay(LevelConfig fallback)
    {
        var scene = NewSceneShell("Gameplay", new Color(0.03f, 0.03f, 0.05f, 1f), withListener: true);

        var globalGO = new GameObject("BG_GlobalLight2D");
        var global = globalGO.AddComponent<Light2D>();
        global.lightType = Light2D.LightType.Global;
        global.intensity = 0.15f;
        global.color = Color.white;

        var curtainGO = new GameObject("BG_Curtain");
        curtainGO.transform.position = Vector3.zero;
        curtainGO.transform.localScale = new Vector3(4f, 2.5f, 1f);
        var csr = curtainGO.AddComponent<SpriteRenderer>();
        csr.sprite = curtainSprite;
        csr.material = litMat;
        csr.sortingOrder = -10;

        var sys = new GameObject("_Systems");
        sys.AddComponent<PuppetDragger>();

        MakeDebugPanel();

        var lmGO = new GameObject("_LevelManager");
        var lm = lmGO.AddComponent<LevelManager>();
        lm.font = font;
        lm.lanternSprite = lanternSprite;
        lm.ringSprite = ringSprite;
        lm.litMaterial = litMat;
        lm.unlitMaterial = unlitMat;
        lm.gong = gong;
        lm.fallbackLevel = fallback;

        EditorSceneManager.SaveScene(scene, SceneDir + "/Gameplay.unity");
    }

    /// <summary>新场景 + 相机（正交 5），返回场景句柄。</summary>
    static UnityEngine.SceneManagement.Scene NewSceneShell(string name, Color bgColor, bool withListener)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bgColor;
        camGO.AddComponent<UniversalAdditionalCameraData>();
        if (withListener) camGO.AddComponent<AudioListener>();
        return scene;
    }

    static void MakeDebugPanel()
    {
        var canvasGO = new GameObject("DebugCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var textGO = new GameObject("DebugText");
        textGO.transform.SetParent(canvasGO.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
        tmp.fontSize = 20f;
        tmp.color = new Color(1f, 0.95f, 0.8f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        var rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(14f, -14f);
        rt.sizeDelta = new Vector2(660f, 460f);

        canvasGO.AddComponent<PuppetDebugPanel>().label = tmp;
    }

    // ---------- 通用 ----------

    static T CreateOrLoad<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}

/// <summary>
/// 远程触发钩子：工程 Temp/step4-build.request 存在时，编辑器编译完成后自动执行一次构建（供外部工具驱动，无标记则完全静默）。
/// </summary>
[InitializeOnLoad]
internal static class Step4AutoRun
{
    static Step4AutoRun()
    {
        string marker = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "step4-build.request");
        if (!File.Exists(marker)) return;
        File.Delete(marker);
        EditorApplication.delayCall += () =>
        {
            try { FrameworkSceneBuilder.BuildAll(); }
            catch (System.Exception e) { Debug.LogError("[STEP-4] 自动构建失败：" + e.Message); }
        };
    }
}
