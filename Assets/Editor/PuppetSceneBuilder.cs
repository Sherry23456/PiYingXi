using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// STEP-2 皮影关节偶一键构建器。
/// 菜单：LanternKeeper > STEP-2 > 构建皮影场景 Spike_Puppet
/// 自动完成：
///   1) 占位图（身体/手臂/金箍棒，美术素材后续替换同名 PNG；手臂 Pivot 已设肩部）
///   2) 灰盒预制体 GreyboxPuppet（身体+左右臂）与 Puppet_Wukong_Grey（身体+左右臂+金箍棒）
///   3) 场景 Spike_Puppet：相机(正交5) + 全局光0.15 + 幕布 + 双皮影实例 + 暖灯(DragTransform) + 调试面板
/// 复用 STEP-1 的幕布/灯笼占位图与 Sprite-Lit 材质（缺则补生成）。
/// </summary>
public static class PuppetSceneBuilder
{
    const string ArtDir = "Assets/Art/Greybox";
    const string SpikeArtDir = "Assets/Art/Spike";
    const string PrefabDir = "Assets/Prefab";
    const string SceneDir = "Assets/Scenes";
    const string ScenePath = SceneDir + "/Spike_Puppet.unity";

    [MenuItem("LanternKeeper/STEP-2/构建皮影场景 Spike_Puppet")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(ArtDir);
        EnsureFolder(SpikeArtDir);
        EnsureFolder(PrefabDir);
        EnsureFolder(SceneDir);

        Material lit = EnsureLitMaterial();

        // ---- STEP-1 公共占位资产：缺则生成；已存在则直接用（不得重设导入设置，
        //      否则 Multiple→Single 会替换子精灵，令已保存的 Spike_Shadow 场景引用失效） ----
        Sprite curtain = EnsureSharedSprite(SpikeArtDir, "BG_Curtain", 512, 512, (x, y, w, h) => new Color(0.96f, 0.94f, 0.88f, 1f));
        Sprite lantern = EnsureSharedSprite(SpikeArtDir, "Prop_Lantern", 128, 128, LanternShape);

        // ---- STEP-2 占位图（PPU=100，公共约定） ----
        Sprite wukongBody = BuildSprite(ArtDir, "Puppet_Wukong_身体", 220, 420, WukongBody, null);
        Sprite wukongArm  = BuildSprite(ArtDir, "Puppet_Wukong_手臂", 140, 320, WukongArm, new Vector2(0.5f, 0.875f)); // Pivot=肩
        Sprite cudgel     = BuildSprite(ArtDir, "Puppet_Wukong_金箍棒", 60, 300, CudgelShape, null);
        Sprite greyBody   = BuildSprite(ArtDir, "Puppet_Greybox_身体", 220, 360, GreyboxBody, null);
        Sprite greyArm    = BuildSprite(ArtDir, "Puppet_Greybox_手臂", 140, 320, GreyboxArm, new Vector2(0.5f, 0.875f)); // Pivot=肩

        // ---- 场景 ----
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 相机：正交 Size=5（1920x1080 基准）
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 1f);
        camGO.AddComponent<UniversalAdditionalCameraData>();

        // 氛围：全局光 0.15
        var globalGO = new GameObject("BG_GlobalLight2D");
        var global = globalGO.AddComponent<Light2D>();
        global.lightType = Light2D.LightType.Global;
        global.intensity = 0.15f;
        global.color = Color.white;

        // 幕布：Sorting Order 最低，接影子
        SpriteGO("BG_Curtain", curtain, lit, -10, Vector3.zero, new Vector3(4f, 2.5f, 1f));

        // 灰盒皮影 1：GreyboxPuppet（身体+左右臂，默认限位 -30~30）
        var grey = BuildGreyboxPuppet(greyBody, greyArm, lit);
        grey.transform.position = new Vector3(-4.8f, -1.6f, 0f);
        PrefabUtility.SaveAsPrefabAssetAndConnect(grey, PrefabDir + "/GreyboxPuppet.prefab", InteractionMode.AutomatedAction);

        // 灰盒皮影 2：Puppet_Wukong_Grey（身体+左右臂+金箍棒，限位 -60~60）
        var wukong = BuildWukong(wukongBody, wukongArm, cudgel, lit);
        wukong.transform.position = new Vector3(2.8f, -1.6f, 0f);
        PrefabUtility.SaveAsPrefabAssetAndConnect(wukong, PrefabDir + "/Puppet_Wukong_Grey.prefab", InteractionMode.AutomatedAction);

        // 暖灯（拖灯走 STEP-1 的 DragTransform，与 PuppetDragger 互不干扰）
        MakeLantern("Lantern_Warm", new Vector3(0.6f, 3.4f, 0f), new Color(1f, 0.82f, 0.55f), lantern, lit);

        // 统一指针输入
        var sys = new GameObject("_Systems");
        sys.AddComponent<PuppetDragger>();

        // 输入调试面板（后续删）
        MakeDebugPanel();

        EditorSceneManager.SaveScene(scene, ScenePath);

        Debug.Log("[STEP-2] Spike_Puppet 构建完成。Play 验证：①点身体拖动整只皮影；②点手臂绕肩旋转、到 ±限位被夹住；③接近 15° 网格松手吸附；④旋转/拖动时幕布影子实时同步；⑤角落调试面板显示命中部件与角度。");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
    }

    // ---------- 结构 ----------

    static GameObject BuildGreyboxPuppet(Sprite body, Sprite arm, Material lit)
    {
        var root = new GameObject("GreyboxPuppet");
        var sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = body;
        sr.material = lit;
        sr.sortingOrder = 0;
        root.AddComponent<PolygonCollider2D>();
        AddShadowCaster(root);
        root.AddComponent<ShadowPuppet>();

        MakeArm(root, "ArmL", new Vector3(-0.6f, 1.5f, 0f), arm, lit, -30f, 30f);
        MakeArm(root, "ArmR", new Vector3(0.6f, 1.5f, 0f), arm, lit, -30f, 30f);
        return root;
    }

    static GameObject BuildWukong(Sprite body, Sprite arm, Sprite cudgel, Material lit)
    {
        var root = new GameObject("Puppet_Wukong_Grey");
        var sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = body;
        sr.material = lit;
        sr.sortingOrder = 0;
        root.AddComponent<PolygonCollider2D>();
        AddShadowCaster(root);
        root.AddComponent<ShadowPuppet>();

        var armL = MakeArm(root, "ArmL", new Vector3(-0.55f, 0.75f, 0f), arm, lit, -60f, 60f);
        var armR = MakeArm(root, "ArmR", new Vector3(0.55f, 0.75f, 0f), arm, lit, -60f, 60f);

        // 金箍棒：右臂子物体，不独立关节；带碰撞体→点它=转右臂
        var stick = new GameObject("Cudgel");
        stick.transform.SetParent(armR.transform, false);
        stick.transform.localPosition = new Vector3(0f, -2.25f, 0f); // 手部位置
        stick.transform.localRotation = Quaternion.Euler(0f, 0f, 75f);
        var ssr = stick.AddComponent<SpriteRenderer>();
        ssr.sprite = cudgel;
        ssr.material = lit;
        ssr.sortingOrder = 2;
        stick.AddComponent<PolygonCollider2D>();
        AddShadowCaster(stick);
        return root;
    }

    static GameObject MakeArm(GameObject root, string name, Vector3 shoulderLocalPos, Sprite arm, Material lit, float minA, float maxA)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = shoulderLocalPos; // 关节枢轴放"肩"
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = arm;
        sr.material = lit;
        sr.sortingOrder = 1;
        go.AddComponent<PolygonCollider2D>();
        AddShadowCaster(go);
        var joint = go.AddComponent<PuppetJoint>();
        joint.minAngle = minA;
        joint.maxAngle = maxA;
        // joint.pivot 留空 → Awake 自动用自身 Transform（Sprite Pivot 已设肩部）
        return go;
    }

    static GameObject MakeLantern(string name, Vector3 pos, Color lightColor, Sprite sprite, Material lit)
    {
        var root = new GameObject(name);
        root.transform.position = pos;

        var spriteGO = new GameObject("LanternSprite");
        spriteGO.transform.SetParent(root.transform, false);
        var sr = spriteGO.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.material = lit;
        sr.sortingOrder = 10;
        spriteGO.AddComponent<CircleCollider2D>().radius = 0.55f;

        var lightGO = new GameObject("Light");
        lightGO.transform.SetParent(root.transform, false);
        var l2d = lightGO.AddComponent<Light2D>();
        l2d.lightType = Light2D.LightType.Point;
        l2d.intensity = 1f;
        l2d.color = lightColor;
        l2d.pointLightOuterRadius = 10f;

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
        tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSize = 20f;
        tmp.color = new Color(1f, 0.95f, 0.8f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        var rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(14f, -14f);
        rt.sizeDelta = new Vector2(620f, 320f);

        canvasGO.AddComponent<PuppetDebugPanel>().label = tmp;
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

    static void AddShadowCaster(GameObject go)
    {
        var sc = go.AddComponent<ShadowCaster2D>();
        sc.castsShadows = true;
        // URP 各版本属性名不稳定（useRendererSilhouette/useRendererSilhouettes），反射兜底
        var prop = typeof(ShadowCaster2D).GetProperty("useRendererSilhouette")
                ?? typeof(ShadowCaster2D).GetProperty("useRendererSilhouettes");
        if (prop != null) prop.SetValue(sc, true);
        else Debug.LogWarning("[STEP-2] 未找到 Use Renderer Silhouette 属性，请在 Inspector 手动勾选。");
    }

    static Material EnsureLitMaterial()
    {
        const string matPath = SpikeArtDir + "/Mat_SpriteLit.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        return mat;
    }

    delegate Color PixelFunc(int x, int y, int w, int h);

    /// <summary>共享资产专用：PNG 不存在才生成并做基础导入；存在则绝不重设导入设置。</summary>
    static Sprite EnsureSharedSprite(string dir, string assetName, int w, int h, PixelFunc func)
    {
        string path = $"{dir}/{assetName}.png";
        if (!File.Exists(path))
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

            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.textureType = TextureImporterType.Sprite;
            st.spriteMode = 1;
            st.spriteMeshType = SpriteMeshType.FullRect;
            st.spritePixelsPerUnit = 100;
            st.spriteAlignment = (int)SpriteAlignment.Custom;
            st.spritePivot = new Vector2(0.5f, 0.5f);
            st.alphaIsTransparency = true;
            st.mipmapEnabled = false;
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Sprite BuildSprite(string dir, string assetName, int w, int h, PixelFunc func, Vector2? pivot)
    {
        string path = $"{dir}/{assetName}.png";
        if (!File.Exists(path))
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

            AssetDatabase.ImportAsset(path); // 先导入资源库，否则 GetAtPath 返回 null
        }

        // 每次构建都强制应用导入设置：FullRect（防 Tight 裁剪偏移）+ 自定义 Pivot（关节枢轴）
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null)
        {
            AssetDatabase.ImportAsset(path);
            ti = (TextureImporter)AssetImporter.GetAtPath(path);
        }
        var st = new TextureImporterSettings();
        ti.ReadTextureSettings(st);
        st.textureType = TextureImporterType.Sprite;
        st.spriteMode = 1; // Single：Unity 6 默认 Multiple 会自动切片裁剪并忽略 Pivot
        st.spriteMeshType = SpriteMeshType.FullRect;
        st.spritePixelsPerUnit = 100; // 公共约定 PPU=100
        st.spriteAlignment = (int)SpriteAlignment.Custom; // Pivot 只有在 Custom 对齐下才生效
        st.spritePivot = pivot ?? new Vector2(0.5f, 0.5f);
        st.alphaIsTransparency = true;
        st.mipmapEnabled = false;
        ti.SetTextureSettings(st);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ---------- 占位图形（美术素材到位后整体替换同名 PNG） ----------

    // 孙悟空：头圆 + 躯干 + 下摆外扩，剪影深灰
    static Color WukongBody(int x, int y, int w, int h)
    {
        if (InCircle(x, y, 110f, 352f, 62f)) return Silhouette(0.10f);
        if (InRoundRect(x, y, 38f, 40f, 182f, 300f, 26f)) return Silhouette(0.10f);
        // 下摆：y 40→150 由半宽 82 收窄到 40
        if (y >= 40f && y <= 150f)
        {
            float t = (y - 40f) / 110f;
            float half = Mathf.Lerp(82f, 40f, t);
            if (Mathf.Abs(x - 110f) <= half) return Silhouette(0.10f);
        }
        return Color.clear;
    }

    // 手臂胶囊：肩圆 + 肘身 + 手圆（左/右共用一张，对称）
    static Color WukongArm(int x, int y, int w, int h)
    {
        if (InCircle(x, y, 70f, 280f, 42f)) return Silhouette(0.17f); // 肩
        if (InCircle(x, y, 70f, 55f, 40f)) return Silhouette(0.17f);  // 手
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

    // 金箍棒：金身 + 两端深色箍
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

    // 灯笼：圆身 + 深橙描边（与 STEP-1 同款）
    static Color LanternShape(int x, int y, int w, int h)
    {
        float dx = x - w / 2f + 0.5f, dy = y - h / 2f + 0.5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r > 60f) return Color.clear;
        if (r > 54f) return new Color(0.85f, 0.45f, 0.2f, 1f);
        return new Color(1f, 0.78f, 0.35f, 1f);
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
