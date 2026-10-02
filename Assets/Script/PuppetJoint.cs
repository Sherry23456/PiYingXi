using UnityEngine;

/// <summary>
/// STEP-2 关节脚本：挂在可旋转部件上，绕枢轴在 [minAngle, maxAngle] 内转动；
/// 松手时按网格吸附（距网格角 <= snapThreshold 才吸）。
/// 角度一律用"相对枢轴父级的局部 Z 角"计算，避免父级缩放/旋转导致世界角漂移（见 STEP-2 已知坑）。
/// 枢轴：pivot 留空用自身 Transform——部件 Sprite 的 Pivot 应在 Sprite Editor 里设到关节处
/// （本工程灰盒占位图已用导入设置把手臂 Pivot 设在肩部）。
/// </summary>
public class PuppetJoint : MonoBehaviour
{
    [Header("限位（局部 Z 角，度）")]
    public float minAngle = -30f;
    public float maxAngle = 30f;

    [Header("吸附")]
    [Tooltip("松手时距最近网格角小于该值才吸附")]
    public float snapThreshold = 4f;
    [Tooltip("吸附网格间隔（度）")]
    public float snapGrid = 15f;

    [Tooltip("枢轴；留空则用自身 Transform")]
    public Transform pivot;

    /// <summary>当前角度：相对枢轴父级的局部 Z 角，[-180,180] 归一。</summary>
    public float CurrentAngle => currentAngle;
    /// <summary>最近一次松手是否发生了吸附（调试/演出用）。</summary>
    public bool LastReleaseSnapped { get; private set; }

    private float currentAngle;

    private void Awake()
    {
        if (pivot == null) pivot = transform;
        currentAngle = ReadLocalAngle();
    }

    /// <summary>增量旋转（带限位夹取）。</summary>
    public void RotateBy(float delta) => RotateTo(currentAngle + delta);

    /// <summary>转到目标角（带限位夹取）。</summary>
    public void RotateTo(float targetAngle)
    {
        currentAngle = Mathf.Clamp(targetAngle, minAngle, maxAngle);
        Apply();
    }

    /// <summary>松手吸附：距网格角 <= snapThreshold 时吸附到网格角。</summary>
    public void SnapOnRelease()
    {
        LastReleaseSnapped = false;
        if (snapGrid <= 0f) return;
        float nearest = Mathf.Round(currentAngle / snapGrid) * snapGrid;
        if (Mathf.Abs(nearest - currentAngle) <= snapThreshold && nearest >= minAngle && nearest <= maxAngle)
        {
            currentAngle = nearest;
            LastReleaseSnapped = true;
            Apply();
        }
    }

    private void Apply() => pivot.localRotation = Quaternion.Euler(0f, 0f, currentAngle);

    private float ReadLocalAngle()
    {
        float z = pivot.localEulerAngles.z;
        return z > 180f ? z - 360f : z;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform p = pivot != null ? pivot : transform;
        Quaternion parentRot = p.parent != null ? p.parent.rotation : Quaternion.identity;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p.position, 0.08f);          // 枢轴
        Gizmos.color = Color.cyan;                          // 下限方向
        Gizmos.DrawRay(p.position, parentRot * Quaternion.Euler(0f, 0f, minAngle) * Vector3.up * 1.2f);
        Gizmos.color = Color.red;                           // 上限方向
        Gizmos.DrawRay(p.position, parentRot * Quaternion.Euler(0f, 0f, maxAngle) * Vector3.up * 1.2f);
        Gizmos.color = Color.green;                         // 当前朝向
        Gizmos.DrawRay(p.position, p.up * 1.5f);
    }
#endif
}
