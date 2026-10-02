using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// STEP-3 三关一键构建器（对影判定与核心闭环）。
/// 菜单：LanternKeeper > STEP-3 > 构建三关（对影判定闭环）
/// 自动完成：
///   1) 占位资产：Target_Ring 环形目标线（无光照材质）、SFX_Gong_Placeholder 占位锣声
///   2) 关A"纯摆"   Level_A_PurePose ：摆皮影到幽灵影位置即达标（PoseJudge + GhostHint）
///   3) 关B"移灯"   Level_B_MoveLamp ：拖灯让影子套进环形目标线（LampJudge + Target_Ring）
///   4) 关C"混合"   Level_C_Mixed    ：摆皮影 + 移灯都要对（两者判定叠加）
///   5) 公共：正交相机5 / 全局光0.15 / 幕布 / 拖拽输入 / 调试面板 / 开演编排(_Stage) / 结果UI
///   6) Build Settings 加入三关（下一关按索引推进）
/// 判定目标数值按 STEP-3 规格写入 Inspector，可在场景里直接调；"从当前姿态捕获目标"可快速改目标。
/// </summary>
public static class LevelSceneBuilder
{
    const string ArtDir = "Assets/Art/Greybox";
    const string SpikeArtDir = "Assets/Art/Spike";
    const string AudioDir = "Assets/Audio";
    const string SceneDir = "Assets/Scenes";

    static Material lit, unlit;
    static Sprite curtain, lantern, ring, greyBody, greyArm, wukongBody, wukongArm, cudgel;
    static AudioClip gong;
    static TMP_FontAsset font;

    [MenuItem("LanternKeeper/STEP-3/构建三关（对影判定闭环）")]
    public static void BuildAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(ArtDir);
        EnsureFolder(SpikeArtDir);
        EnsureFolder(AudioDir);
        EnsureFolder(SceneDir);

        lit = EnsureLitMaterial();
        unlit = EnsureUnlitMaterial();
        curtain = EnsureSharedSprite(SpikeArtDir, "BG_Curtain", 512, 512, (x, y, w, h) => new Color(0.96f, 0.94f, 0.88f, 1f));
        lantern = EnsureSharedSprite(SpikeArtDir, "Prop_Lantern", 128, 128, LanternShape);
        ring = BuildSprite(ArtDir, "Target_Ring", 256, 256, RingShape, null);
        gong = EnsureGong();
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/STKAITI SDF.asset");
        if (font == null) font = TMP_Settings.defaultFontAsset;

        // 皮影占位图（STEP-2 已生成，这里确保导入设置一致）
        greyBody = BuildSprite(ArtDir, "Puppet_Greybox_身体", 220, 360, GreyboxBody, null);
        greyArm = BuildSprite(ArtDir, "Puppet_Greybox_手臂", 140, 320, GreyboxArm, new Vector2(0.5f, 0.875f));
        wukongBody = BuildSprite(ArtDir, "Puppet_Wukong_身体", 220, 420, WukongBody, null);
        wukongArm = BuildSprite(ArtDir, "Puppet_Wukong_手臂", 140, 320, WukongArm, new Vector2(0.5f, 0.875f));
        cudgel = BuildSprite(ArtDir, "Puppet_Wukong_金箍棒", 60, 300, CudgelShape, null);

        BuildLevelA();
        BuildLevelB();
        BuildLevelC();

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(SceneDir + "/Level_A_PurePose.unity", true),
            new EditorBuildSettingsScene(SceneDir + "/Level_B_MoveLamp.unity", true),
            new EditorBuildSettingsScene(SceneDir + "/Level_C_Mixed.unity", true),
        };

        Debug.Log("[STEP-3] 三关构建完成。Play 验证：关A摆皮影对齐幽灵影→开演3★；关B拖灯把影子套进环；关C两者都对→演出+星级；失败时面板提示未达标部件且幽灵影红标2秒；Tab循环提示档。");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneDir + "/Level_A_PurePose.unity");
    }

    // ---------- 三关 ----------

    static void BuildLevelA()
    {
        var scene = NewLevel("Level_A_PurePose", "关A · 纯摆：把皮影摆得和幽灵影一致，再点[开演]");

        var puppet = InstantiatePrefab("Assets/Prefab/GreyboxPuppet.prefab", scene);
        puppet.transform.position = new Vector3(-4.5f, -1.6f, 0f);

        var lamp = MakeLantern("Lantern_Warm", new Vector3(0.6f, 3.4f, 0f), new Color(1f, 0.82f, 0.55f), 10f);

        var judgeGO = new GameObject("_Judge");
        var pj = judgeGO.AddComponent<PoseJudge>();
        pj.puppet = puppet.GetComponent<ShadowPuppet>();
        pj.targetRootPos = new Vector2(1.8f, -1.6f);
        pj.rootPosTolerance = 0.35f;
        pj.parts.Add(new PoseJudge.PartTarget { partName = "ArmL", targetAngle = 18f });
        pj.parts.Add(new PoseJudge.PartTarget { partName = "ArmR", targetAngle = -18f });

        var ghostGO = new GameObject("_GhostHint");
        ghostGO.AddComponent<GhostHint>().judge = pj;

        BuildStage(lamp: lamp, poseJudge: pj, lampJudge: null, ghostHint: ghostGO.GetComponent<GhostHint>());

        EditorSceneManager.SaveScene(scene, SceneDir + "/Level_A_PurePose.unity");
    }

    static void BuildLevelB()
    {
        var scene = NewLevel("Level_B_MoveLamp", "关B · 移灯：拖动灯，让影子套进环形目标线");

        var puppet = InstantiatePrefab("Assets/Prefab/GreyboxPuppet.prefab", scene);
        puppet.transform.position = new Vector3(0f, -1.6f, 0f); // 固定不动，只移灯

        var lamp = MakeLantern("Lantern_Warm", new Vector3(6.2f, 4.0f, 0f), new Color(1f, 0.82f, 0.55f), 6f);

        // 环形目标线（无光照，幕布之上皮影之下）
        var ringGO = SpriteGO("Target_Ring", ring, unlit, -9, Vector3.zero, Vector3.one);
        ringGO.GetComponent<SpriteRenderer>().color = new Color(1f, 0.78f, 0.4f, 0.55f);

        // 目标数值：灯在 (-0.4, 3.4) 时按模型反推影子落点/大小（R=6, centerStretch=0.5, sizeGain=1）
        var lampJudge = new GameObject("_Judge").AddComponent<LampJudge>();
        lampJudge.lamp = lamp.transform;
        lampJudge.lampLight = lamp.GetComponentInChildren<Light2D>();
        lampJudge.shadowCaster = puppet.transform;
        lampJudge.targetShadowPos = new Vector2(0.04f, -2.09f);
        lampJudge.targetShadowRadius = 2.27f;
        lampJudge.posTolerance = 0.5f;
        lampJudge.sizeTolerance = 0.25f;
        lampJudge.ringRenderer = ringGO.GetComponent<SpriteRenderer>();

        BuildStage(lamp: lamp, poseJudge: null, lampJudge: lampJudge, ghostHint: null);

        EditorSceneManager.SaveScene(scene, SceneDir + "/Level_B_MoveLamp.unity");
    }

    static void BuildLevelC()
    {
        var scene = NewLevel("Level_C_Mixed", "关C · 混合：摆对皮影 + 影子进环，再点[开演]");

        var puppet = InstantiatePrefab("Assets/Prefab/Puppet_Wukong_Grey.prefab", scene);
        puppet.transform.position = new Vector3(-4.5f, -1.6f, 0f);

        var lamp = MakeLantern("Lantern_Warm", new Vector3(5.8f, 4.2f, 0f), new Color(1f, 0.82f, 0.55f), 6f);

        var ringGO = SpriteGO("Target_Ring", ring, unlit, -9, Vector3.zero, Vector3.one);
        ringGO.GetComponent<SpriteRenderer>().color = new Color(1f, 0.78f, 0.4f, 0.55f);

        var judgeGO = new GameObject("_Judge");
        var pj = judgeGO.AddComponent<PoseJudge>();
        pj.puppet = puppet.GetComponent<ShadowPuppet>();
        pj.targetRootPos = new Vector2(1.6f, -1.6f);
        pj.rootPosTolerance = 0.35f;
        pj.parts.Add(new PoseJudge.PartTarget { partName = "ArmL", targetAngle = 15f });
        pj.parts.Add(new PoseJudge.PartTarget { partName = "ArmR", targetAngle = -15f });

        var lampJudge = judgeGO.AddComponent<LampJudge>();
        lampJudge.lamp = lamp.transform;
        lampJudge.lampLight = lamp.GetComponentInChildren<Light2D>();
        lampJudge.shadowCaster = puppet.transform;
        // 目标：皮影在 (1.6,-1.6)、灯在 (0.2,3.2) 时按模型反推（R=6）
        lampJudge.targetShadowPos = new Vector2(1.74f, -2.08f);
        lampJudge.targetShadowRadius = 2.52f;
        lampJudge.posTolerance = 0.5f;
        lampJudge.sizeTolerance = 0.25f;
        lampJudge.ringRenderer = ringGO.GetComponent<SpriteRenderer>();

        var ghostGO = new GameObject("_GhostHint");
        ghostGO.AddComponent<GhostHint>().judge = pj;

        BuildStage(lamp: lamp, poseJudge: pj, lampJudge: lampJudge, ghostHint: ghostGO.GetComponent<GhostHint>());

        EditorSceneManager.SaveScene(scene, SceneDir + "/Level_C_Mixed.unity");
    }

    // ---------- 公共构件 ----------

    /// <summary>新场景 + 相机/全局光/幕布/输入/调试面板/关名提示，返回场景句柄。</summary>
    static UnityEngine.SceneManagement.Scene NewLevel(string sceneName, string hint)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 1f);
        camGO.AddComponent<UniversalAdditionalCameraData>();
        camGO.AddComponent<AudioListener>(); // 开演锣声需要

        var globalGO = new GameObject("BG_GlobalLight2D");
        var global = globalGO.AddComponent<Light2D>();
        global.lightType = Light2D.LightType.Global;
        global.intensity = 0.15f;
        global.color = Color.white;

        SpriteGO("BG_Curtain", curtain, lit, -10, Vector3.zero, new Vector3(4f, 2.5f, 1f));

        var sys = new GameObject("_Systems");
        sys.AddComponent<PuppetDragger>();

        MakeDebugPanel();
        MakeHintLabel(hint);
        return scene;
    }

    /// <summary>开演编排：_Stage（判定接线 + 占位演出 + 结果UI + 开演/提示按钮）。</summary>
    static void BuildStage(GameObject lamp, PoseJudge poseJudge, LampJudge lampJudge, GhostHint ghostHint)
    {
        var stage = new GameObject("_Stage");
        var perf = stage.AddComponent<PerformancePlaceholder>();
        perf.gong = gong;
        if (lamp != null) perf.flickerLight = lamp.GetComponentInChildren<Light2D>();

        var resultUI = stage.AddComponent<PlayResultUI>();
        resultUI.font = font;
        resultUI.ghostHint = ghostHint;

        var director = stage.AddComponent<PerformanceDirector>();
        director.font = font;
        director.poseJudge = poseJudge;
        director.lampJudge = lampJudge;
        director.perf = perf;
        director.resultUI = resultUI;
        director.ghostHint = ghostHint;
    }

    static GameObject InstantiatePrefab(string path, UnityEngine.SceneManagement.Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        if (go == null) Debug.LogError($"[STEP-3] 预制体加载失败：{path}");
        return go;
    }

    static GameObject MakeLantern(string name, Vector3 pos, Color lightColor, float outerRadius)
    {
        var root = new GameObject(name);
        root.transform.position = pos;

        var spriteGO = new GameObject("LanternSprite");
        spriteGO.transform.SetParent(root.transform, false);
        var sr = spriteGO.AddComponent<SpriteRenderer>();
        sr.sprite = lantern;
        sr.material = lit;
        sr.sortingOrder = 10;
        spriteGO.AddComponent<CircleCollider2D>().radius = 0.55f;

        var lightGO = new GameObject("Light");
        lightGO.transform.SetParent(root.transform, false);
        var l2d = lightGO.AddComponent<Light2D>();
        l2d.lightType = Light2D.LightType.Point;
        l2d.intensity = 1f;
        l2d.color = lightColor;
        l2d.pointLightOuterRadius = outerRadius;

        root.AddComponent<DragTransform>();
        return root;
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

    static void MakeHintLabel(string hint)
    {
        var canvasGO = new GameObject("HintCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 深色衬底小条：提示文字压在灯光亮池上也读得清
        var chipGO = new GameObject("HintChip");
        chipGO.transform.SetParent(canvasGO.transform, false);
        var chipRt = chipGO.AddComponent<RectTransform>();
        chipRt.anchorMin = chipRt.anchorMax = new Vector2(1f, 1f);
        chipRt.pivot = new Vector2(1f, 1f);
        chipRt.anchoredPosition = new Vector2(-30f, -24f);
        chipRt.sizeDelta = new Vector2(1240f, 76f);
        var chip = chipGO.AddComponent<Image>();
        chip.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
        chip.color = new Color(0.08f, 0.06f, 0.05f, 0.55f);

        var textGO = new GameObject("LevelHint");
        textGO.transform.SetParent(chipGO.transform, false);
        var rt = textGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(1200f, 60f);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
        tmp.fontSize = 34f;
        tmp.color = new Color(0.94f, 0.87f, 0.72f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = hint;
        tmp.raycastTarget = false;
    }

    // ---------- 通用 ----------

    static GameObject SpriteGO(string name, Sprite sprite, Material mat, int order, Vector3 pos, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.material = mat;
        sr.sortingOrder = order;
        return go;
    }

    static Material EnsureLitMaterial()
    {
        const string matPath = SpikeArtDir + "/Mat_SpriteLit.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        return mat;
    }

    static Material EnsureUnlitMaterial()
    {
        const string matPath = ArtDir + "/Mat_UnlitSprite.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        return mat;
    }

    /// <summary>占位锣声：低频叠加正弦 + 指数衰减 + 起振噪声，约 1.6 秒。</summary>
    static AudioClip EnsureGong()
    {
        const string clipPath = AudioDir + "/SFX_Gong_Placeholder.asset";
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        if (clip != null) return clip;

        const int sampleRate = 22050;
        const float duration = 1.6f;
        int n = (int)(sampleRate * duration);
        var data = new float[n];
        var rnd = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sampleRate;
            float s = 0.55f * Mathf.Sin(2f * Mathf.PI * 146f * t) * Mathf.Exp(-2.6f * t)
                    + 0.30f * Mathf.Sin(2f * Mathf.PI * 219f * t) * Mathf.Exp(-3.1f * t)
                    + 0.22f * Mathf.Sin(2f * Mathf.PI * 367f * t) * Mathf.Exp(-4.0f * t)
                    + 0.12f * Mathf.Sin(2f * Mathf.PI * 541f * t) * Mathf.Exp(-5.0f * t);
            if (t < 0.03f) s += 0.5f * ((float)rnd.NextDouble() * 2f - 1f) * (1f - t / 0.03f);
            data[i] = Mathf.Clamp(s, -1f, 1f) * 0.85f;
        }
        clip = AudioClip.Create("SFX_Gong_Placeholder", n, 1, sampleRate, false);
        clip.SetData(data, 0);
        AssetDatabase.CreateAsset(clip, clipPath);
        return clip;
    }

    delegate Color PixelFunc(int x, int y, int w, int h);

    /// <summary>共享资产专用：PNG 不存在才生成；存在则绝不重设导入设置。</summary>
    static Sprite EnsureSharedSprite(string dir, string assetName, int w, int h, PixelFunc func)
    {
        string path = $"{dir}/{assetName}.png";
        if (!File.Exists(path))
        {
            WritePng(path, w, h, func);
            ImportSprite(path, new Vector2(0.5f, 0.5f));
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>每次构建都强制导入设置（FullRect + 自定义 Pivot），防 Tight 裁剪与 Pivot 丢失。</summary>
    static Sprite BuildSprite(string dir, string assetName, int w, int h, PixelFunc func, Vector2? pivot)
    {
        string path = $"{dir}/{assetName}.png";
        if (!File.Exists(path)) WritePng(path, w, h, func);
        ImportSprite(path, pivot ?? new Vector2(0.5f, 0.5f));
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void WritePng(string path, int w, int h, PixelFunc func)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = func(x, y, w, h);
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static void ImportSprite(string path, Vector2 pivot)
    {
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null)
        {
            AssetDatabase.ImportAsset(path);
            ti = (TextureImporter)AssetImporter.GetAtPath(path);
        }
        var st = new TextureImporterSettings();
        ti.ReadTextureSettings(st);
        st.textureType = TextureImporterType.Sprite;
        st.spriteMode = 1; // Single：Unity 6 默认 Multiple 会切片并忽略 Pivot
        st.spriteMeshType = SpriteMeshType.FullRect;
        st.spritePixelsPerUnit = 100;
        st.spriteAlignment = (int)SpriteAlignment.Custom;
        st.spritePivot = pivot;
        st.alphaIsTransparency = true;
        st.mipmapEnabled = false;
        ti.SetTextureSettings(st);
        ti.SaveAndReimport();
    }

    // ---------- 占位图形 ----------

    // 环形目标线：空心圆环
    static Color RingShape(int x, int y, int w, int h)
    {
        float dx = x - w / 2f + 0.5f, dy = y - h / 2f + 0.5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r >= 106f && r <= 118f) return new Color(1f, 0.9f, 0.6f, 1f);
        return Color.clear;
    }

    // 灯笼：圆身 + 深橙描边（与 STEP-1/2 同款）
    static Color LanternShape(int x, int y, int w, int h)
    {
        float dx = x - w / 2f + 0.5f, dy = y - h / 2f + 0.5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r > 60f) return Color.clear;
        if (r > 54f) return new Color(0.85f, 0.45f, 0.2f, 1f);
        return new Color(1f, 0.78f, 0.35f, 1f);
    }

    // ---- 皮影占位图（与 STEP-2 PuppetSceneBuilder 同款，保证占位图一致） ----

    static Color WukongBody(int x, int y, int w, int h)
    {
        if (InCircle(x, y, 110f, 352f, 62f)) return Silhouette(0.10f);
        if (InRoundRect(x, y, 38f, 40f, 182f, 300f, 26f)) return Silhouette(0.10f);
        if (y >= 40f && y <= 150f)
        {
            float t = (y - 40f) / 110f;
            float half = Mathf.Lerp(82f, 40f, t);
            if (Mathf.Abs(x - 110f) <= half) return Silhouette(0.10f);
        }
        return Color.clear;
    }

    static Color WukongArm(int x, int y, int w, int h)
    {
        if (InCircle(x, y, 70f, 280f, 42f)) return Silhouette(0.17f);
        if (InCircle(x, y, 70f, 55f, 40f)) return Silhouette(0.17f);
        if (x >= 28f && x <= 112f && y >= 55f && y <= 280f) return Silhouette(0.17f);
        return Color.clear;
    }

    static Color GreyboxArm(int x, int y, int w, int h)
    {
        if (InCircle(x, y, 70f, 280f, 42f)) return Silhouette(0.20f);
        if (InCircle(x, y, 70f, 55f, 40f)) return Silhouette(0.20f);
        if (x >= 28f && x <= 112f && y >= 55f && y <= 280f) return Silhouette(0.20f);
        return Color.clear;
    }

    static Color CudgelShape(int x, int y, int w, int h)
    {
        if (x < 16f || x > 44f || y < 15f || y > 285f) return Color.clear;
        bool band = (y >= 240f && y <= 280f) || (y >= 20f && y <= 60f);
        return band ? new Color(0.42f, 0.28f, 0.10f, 1f) : new Color(0.78f, 0.60f, 0.22f, 1f);
    }

    static Color GreyboxBody(int x, int y, int w, int h)
    {
        return InRoundRect(x, y, 30f, 20f, 190f, 340f, 28f) ? Silhouette(0.14f) : Color.clear;
    }

    static Color Silhouette(float tone) => new Color(tone, tone * 0.92f, tone * 0.85f, 1f);

    static bool InCircle(float x, float y, float cx, float cy, float r)
    {
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= r * r;
    }

    static bool InRoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return false;
        float cx = Mathf.Clamp(x, x0 + r, x1 - r);
        float cy = Mathf.Clamp(y, y0 + r, y1 - r);
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= r * r;
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
/// 远程触发钩子：工程 Temp/step3-build.request 存在时，编辑器编译完成后自动执行一次构建（供外部工具驱动，无标记则完全静默）。
/// </summary>
[InitializeOnLoad]
internal static class Step3AutoRun
{
    static Step3AutoRun()
    {
        string marker = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "step3-build.request");
        if (!File.Exists(marker)) return;
        File.Delete(marker);
        EditorApplication.delayCall += () =>
        {
            try { LevelSceneBuilder.BuildAll(); }
            catch (System.Exception e) { Debug.LogError("[STEP-3] 自动构建失败：" + e.Message); }
        };
    }
}
