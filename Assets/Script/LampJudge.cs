using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// STEP-3 移灯判定（关 B/C）：影子大小/位置由灯位置决定（点光源径向投影，灯近影大、灯偏影斜）。
/// 用针孔近似模型估算影子落点/大小，与目标比对：
///   影心 S = P + u·(R−d)·centerStretch，影半径 ≈ r·R/d·sizeGain
///   （P 皮影中心，L 灯位，d=|PL|，u=远离灯方向，R=光照外半径，r=皮影包络半径）。
/// 判定达标 = 影心误差 ≤ posTolerance 且 影半径相对误差 ≤ sizeTolerance（达标率 1 或 0）。
/// 场景里的 Target_Ring 环形目标线由本脚本按 targetShadowPos/Radius 实时摆放——改 Inspector 数值环跟着动。
/// centerStretch/sizeGain 是模型校准常数：把灯拖到目标位置后看实机影子与环是否贴合，不合就调（调试面板看误差）。
/// </summary>
public class LampJudge : MonoBehaviour
{
    [Header("引用")]
    public Transform lamp;
    public Light2D lampLight;
    [Tooltip("影子投射主体（皮影根）")]
    public Transform shadowCaster;

    [Header("影子目标（环形目标线同值）")]
    public Vector2 targetShadowPos = new Vector2(0.2f, -4.1f);
    [Tooltip("目标影子半径（世界单位）")]
    public float targetShadowRadius = 3.5f;

    [Header("容差")]
    [Tooltip("影心容差（世界单位）")]
    public float posTolerance = 0.5f;
    [Tooltip("影半径相对容差（比例）")]
    [Range(0.05f, 1f)] public float sizeTolerance = 0.25f;

    [Header("投影模型校准")]
    public float centerStretch = 0.5f;
    public float sizeGain = 1f;

    [Tooltip("环形目标线 SpriteRenderer（可空；由构建器接线）")]
    public SpriteRenderer ringRenderer;

    public struct ShadowFootprint
    {
        public Vector2 center;
        public float radius;
    }

    public bool Enabled => lamp != null && lampLight != null && shadowCaster != null;

    private void Update()
    {
        if (ringRenderer != null)
        {
            ringRenderer.transform.position = new Vector3(targetShadowPos.x, targetShadowPos.y, ringRenderer.transform.position.z);
            if (ringRenderer.sprite != null && ringRenderer.sprite.bounds.size.x > 0.001f)
            {
                float s = targetShadowRadius * 2f / ringRenderer.sprite.bounds.size.x;
                ringRenderer.transform.localScale = new Vector3(s, s, 1f);
            }
        }
    }

    public ShadowFootprint CurrentFootprint()
    {
        var fp = new ShadowFootprint { center = Vector2.zero, radius = 0f };
        if (!Enabled) return fp;

        Vector2 P = shadowCaster.position;
        Vector2 L = lamp.position;
        Vector2 dVec = P - L;
        float d = Mathf.Max(dVec.magnitude, 0.001f);
        Vector2 u = dVec / d;
        float R = lampLight.pointLightOuterRadius;
        float r = CasterRadius();

        fp.center = P + u * Mathf.Max(R - d, 0f) * centerStretch;
        fp.radius = r * R / d * sizeGain;
        return fp;
    }

    /// <summary>达标则达标率 1，否则 0（方向性目标，不做中间档）。</summary>
    public bool EvaluatePass(out float errPos, out float errSize)
    {
        errPos = float.MaxValue;
        errSize = float.MaxValue;
        if (!Enabled) return false;
        var fp = CurrentFootprint();
        errPos = Vector2.Distance(fp.center, targetShadowPos);
        errSize = targetShadowRadius > 0.001f ? Mathf.Abs(fp.radius - targetShadowRadius) / targetShadowRadius : 0f;
        return errPos <= posTolerance && errSize <= sizeTolerance;
    }

    public List<PoseJudge.FailInfo> GetFailParts()
    {
        var fails = new List<PoseJudge.FailInfo>();
        EvaluatePass(out float errPos, out float errSize);
        if (errPos > posTolerance)
            fails.Add(new PoseJudge.FailInfo { partName = "影子位置", error = errPos, tolerance = posTolerance });
        if (errSize > sizeTolerance)
            fails.Add(new PoseJudge.FailInfo { partName = "影子大小", error = errSize, tolerance = sizeTolerance });
        return fails;
    }

    public string DescribeErrors()
    {
        if (!Enabled) return "[移灯判定] 未配置灯/皮影";
        var fp = CurrentFootprint();
        EvaluatePass(out float errPos, out float errSize);
        return $"[移灯判定] 影心({fp.center.x:0.00},{fp.center.y:0.00}) 目标({targetShadowPos.x:0.00},{targetShadowPos.y:0.00}) " +
               $"{(errPos <= posTolerance ? "√" : "×")}差{errPos:0.00}/{posTolerance:0.00} | " +
               $"影径{fp.radius:0.00} 目标{targetShadowRadius:0.00} {(errSize <= sizeTolerance ? "√" : "×")}差{errSize * 100f:0}%/{sizeTolerance * 100f:0}%";
    }

    float CasterRadius()
    {
        // 包络半径：取渲染包围盒半尺寸较大轴；Inspector 填 casterRadius 则手填优先
        // （字段留 legacy：目前一律自动算，够灰盒用）
        float radius = 0f;
        foreach (var rend in shadowCaster.GetComponentsInChildren<Renderer>())
        {
            var b = rend.bounds;
            radius = Mathf.Max(radius, Mathf.Max(b.extents.x, b.extents.y));
        }
        return Mathf.Max(radius, 0.1f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 目标环 + 当前模型影心
        Gizmos.color = new Color(1f, 0.75f, 0.35f, 0.9f);
        Gizmos.DrawWireSphere(new Vector3(targetShadowPos.x, targetShadowPos.y, 0f), targetShadowRadius);
        if (Enabled)
        {
            var fp = CurrentFootprint();
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(new Vector3(fp.center.x, fp.center.y, 0f), fp.radius);
        }
    }
#endif
}
