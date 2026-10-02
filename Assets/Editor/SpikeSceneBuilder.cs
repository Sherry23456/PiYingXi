using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// STEP-1 灰盒 Spike 一键构建器。
/// 菜单：LanternKeeper > STEP-1 > 构建灰盒场景 Spike_Shadow
/// 自动完成：占位图生成（幕布/灰盒皮影/缺口皮影/灯笼）→ Sprite-Lit 材质 →
/// 相机(正交 Size=5) + 全局光(0.15) + 幕布 + 皮影(Shadow Caster 2D) + 双灯(Point) → 保存场景。
/// Play 验证三条见《STEP-1-工程奠基与光影Spike.md》第 8 条。
/// </summary>
public static class SpikeSceneBuilder
{
    const string ArtDir = "Assets/Art/Spike";
    const string SceneDir = "Assets/Scenes";
    const string ScenePath = SceneDir + "/Spike_Shadow.unity";

    [MenuItem("LanternKeeper/STEP-1/构建灰盒场景 Spike_Shadow")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(ArtDir);
        EnsureFolder(SceneDir);

        Material lit = BuildLitMaterial();

        Sprite curtain = BuildSprite("BG_Curtain", 512, (x, y, w, h) => Color.white);
        Sprite puppet = BuildSprite("Puppet_Greybox", 256, SolidPuppet);
        Sprite notch = BuildSprite("Puppet_Greybox_Notch", 256, NotchPuppet);
        Sprite lantern = BuildSprite("Prop_Lantern", 128, LanternShape);

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

        // 氛围：全局光 0.15，保留环境底光
        var globalGO = new GameObject("BG_GlobalLight2D");
        var global = globalGO.AddComponent<Light2D>();
        global.lightType = Light2D.LightType.Global;
        global.intensity = 0.15f;
        global.color = Color.white;

        // 幕布：Sorting Order 最低，接受影子
        SpriteGO("BG_Curtain", curtain, lit, -10, Vector3.zero, new Vector3(4f, 2.5f, 1f));

        // 灰盒皮影：Shadow Caster 2D（Use Renderer Silhouette），在幕布前
        var puppetGO = SpriteGO("Puppet_Greybox", puppet, lit, 0, new Vector3(0f, 0.6f, 0f), Vector3.one);
        AddShadowCaster(puppetGO);

        // 缺口皮影：默认隐藏；验证镂空时启用它并隐藏 Puppet_Greybox
        var notchGO = SpriteGO("Puppet_Greybox_Notch", notch, lit, 0, new Vector3(4.2f, 0.6f, 0f), Vector3.one);
        AddShadowCaster(notchGO);
        notchGO.SetActive(false);

        // 灯 1：暖黄（拖灯脚本挂根上）
        MakeLantern("Lantern_Warm", new Vector3(2.6f, 1.6f, 0f), new Color(1f, 0.82f, 0.55f), lantern, lit);
        // 灯 2：冷蓝，默认关闭；验证"双影颜色分离"时启用
        var cool = MakeLantern("Lantern_Cool", new Vector3(-2.6f, 1.6f, 0f), new Color(0.5f, 0.72f, 1f), lantern, lit);
        cool.SetActive(false);

        EditorSceneManager.SaveScene(scene, ScenePath);

        Debug.Log("[STEP-1] Spike_Shadow 构建完成。Play 验证三条：①拖灯看影子反向移动/随距离缩放；②启用 Lantern_Cool 看双影与颜色分离；③启用 Puppet_Greybox_Notch（并隐藏 Puppet_Greybox）看镂空。");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
    }

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
        // URP 14 起属性改名为 useRendererSilhouette（旧版 useRendererSilhouettes），反射兜底防再改名
        var prop = typeof(ShadowCaster2D).GetProperty("useRendererSilhouette")
                ?? typeof(ShadowCaster2D).GetProperty("useRendererSilhouettes");
        if (prop != null) prop.SetValue(sc, true);
        else Debug.LogWarning("[STEP-1] 未找到 Use Renderer Silhouette 属性，请在 Inspector 手动勾选。");
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

    static Material BuildLitMaterial()
    {
        const string matPath = ArtDir + "/Mat_SpriteLit.mat";
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

    static Sprite BuildSprite(string assetName, int size, PixelFunc func)
    {
        string path = $"{ArtDir}/{assetName}.png";
        if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = func(x, y, size, size);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spritePixelsPerUnit = 100; // 公共约定 PPU=100
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Color SolidPuppet(int x, int y, int w, int h) => new Color(0.09f, 0.08f, 0.08f, 1f);

    static Color NotchPuppet(int x, int y, int w, int h)
    {
        float dx = x - w / 2f + 0.5f, dy = y - h / 2f + 0.5f;
        if (dx * dx + dy * dy < 44f * 44f) return Color.clear; // 中央圆孔 → 影子镂空验证
        return new Color(0.09f, 0.08f, 0.08f, 1f);
    }

    static Color LanternShape(int x, int y, int w, int h)
    {
        float dx = x - w / 2f + 0.5f, dy = y - h / 2f + 0.5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r > 60f) return Color.clear;
        if (r > 54f) return new Color(0.85f, 0.45f, 0.2f, 1f); // 深橙描边
        return new Color(1f, 0.78f, 0.35f, 1f); // 暖黄灯身
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
/// 远程触发钩子：工程 Temp/spike-build.request 存在时，编辑器编译完成后自动执行一次构建（供外部工具驱动，无标记则完全静默）。
/// </summary>
[InitializeOnLoad]
internal static class SpikeAutoRun
{
    static SpikeAutoRun()
    {
        string marker = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "spike-build.request");
        if (!File.Exists(marker)) return;
        File.Delete(marker);
        EditorApplication.delayCall += () =>
        {
            try { Build(); }
            catch (System.Exception e) { Debug.LogError("[STEP-1] 自动构建失败：" + e.Message); }
        };
    }
}
