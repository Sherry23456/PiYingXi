using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// STEP-3 幽灵影：把目标剪影以半透明独立 Sprite 叠在幕布上，三档循环切换：
///   隐藏 / 显示 / 显示+红标未达标部件（灰盒期用红色染色代替红框描边，美术期再换）。
/// Tab 键或 UI 按钮循环，默认档 = 显示。
/// 已知坑规避：幽灵影克隆体不挂 ShadowCaster2D / 碰撞体（不参与判定点击），材质用无光照
/// Sprites/Default（不吃灯、不投阴影），排序压在皮影之下幕布之上，避免"三影"。
/// </summary>
public class GhostHint : MonoBehaviour
{
    public enum Mode { Hidden, Show, ShowAndHighlight }

    [Tooltip("默认档位（建议 显示）")]
    public Mode defaultMode = Mode.Show;

    [Header("引用")]
    public PoseJudge judge;

    [Header("外观")]
    [Range(0f, 1f)] public float ghostAlpha = 0.25f;
    public Color ghostTint = new Color(0.85f, 0.85f, 0.9f, 1f);
    public Color failTint = new Color(1f, 0.3f, 0.22f, 1f);
    [Tooltip("失败高亮染色不透明度（比幽灵影更醒目）")]
    [Range(0f, 1f)] public float failAlpha = 0.55f;

    public Mode CurrentMode => mode;

    private Mode mode;
    private Transform ghostRoot;
    private readonly Dictionary<string, SpriteRenderer> ghostParts = new Dictionary<string, SpriteRenderer>();
    private Material ghostMat;
    private float tempHighlightUntil = -1f;

    private void Start()
    {
        mode = defaultMode;
        BuildGhost();
        Refresh();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            CycleMode();

        // 失败临时高亮到期回落默认档
        if (tempHighlightUntil > 0f && Time.time > tempHighlightUntil)
        {
            tempHighlightUntil = -1f;
            mode = defaultMode;
        }
        Refresh();
    }

    public void CycleMode()
    {
        mode = (Mode)(((int)mode + 1) % 3);
        Refresh();
    }

    /// <summary>失败反馈：强制"显示+高亮"档一段时间（PlayResultUI 调用）。</summary>
    public void ShowFailHighlightFor(float seconds)
    {
        mode = Mode.ShowAndHighlight;
        tempHighlightUntil = Time.time + seconds;
        Refresh();
    }

    // ---------- 内部 ----------

    private void BuildGhost()
    {
        if (judge == null || judge.puppet == null) return;
        if (ghostRoot != null) Destroy(ghostRoot.gameObject);

        ghostMat = new Material(Shader.Find("Sprites/Default"));
        ghostRoot = CloneHierarchy(judge.puppet.transform, transform);
        ghostRoot.name = "GhostRoot";
        ghostRoot.position = Vector3.zero;
    }

    private Transform CloneHierarchy(Transform src, Transform parent)
    {
        var go = new GameObject(src.name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = src.localPosition;
        go.transform.localRotation = src.localRotation;
        go.transform.localScale = src.localScale;

        var sr = src.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            var g = go.AddComponent<SpriteRenderer>();
            g.sprite = sr.sprite;
            g.material = ghostMat;
            g.sortingOrder = sr.sortingOrder - 5; // 幕布(-10)之上、皮影(0/1/2)之下
            ghostParts[src.name] = g;
        }
        foreach (Transform child in src)
            CloneHierarchy(child, go.transform);
        return go.transform;
    }

    private void Refresh()
    {
        if (ghostRoot == null) return;
        bool visible = mode != Mode.Hidden;
        ghostRoot.gameObject.SetActive(visible);
        if (!visible) return;

        // 目标姿态：根位置 + 各部件角度（金箍棒等子件随父部件走）
        if (judge != null)
        {
            ghostRoot.position = new Vector3(judge.targetRootPos.x, judge.targetRootPos.y, 0f);
            foreach (var pt in judge.parts)
                if (ghostParts.TryGetValue(pt.partName, out var g) && g != null)
                    g.transform.localRotation = Quaternion.Euler(0f, 0f, pt.targetAngle);
        }

        // 染色：显示档全淡影；高亮档未达标部件标红
        bool highlight = mode == Mode.ShowAndHighlight && judge != null;
        var fails = new HashSet<string>();
        if (highlight)
        {
            bool rootFail = false;
            foreach (var f in judge.GetFailParts())
            {
                if (f.partName == "根位置") rootFail = true;
                else fails.Add(f.partName);
            }
            if (rootFail) // 根没到位 → 整只标红
                foreach (var name in ghostParts.Keys) fails.Add(name);
        }

        foreach (var kv in ghostParts)
        {
            if (kv.Value == null) continue;
            bool isFail = highlight && fails.Contains(kv.Key);
            var c = isFail ? failTint : ghostTint;
            c.a = isFail ? failAlpha : ghostAlpha;
            kv.Value.color = c;
        }
    }
}
