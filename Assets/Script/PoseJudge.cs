using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// STEP-3 姿态判定：对比 ShadowPuppet 当前姿态与目标姿态（根目标位置 + 每部件目标局部 Z 角，Inspector 可配）。
/// 误差在容差内记该部件达标；达标率 r = 达标部件数 / 总部件数（根位置算一个部件）。
/// 星级：r=1 → 3★，r≥0.8 → 2★，r≥0.6 → 1★，其余 0★（不达标）。
/// 容差一律"世界单位/角度"，禁止像素（见 STEP-3 已知坑：换分辨率全崩）。
/// "看着对了却不过"时开 Debug 面板看 DescribeErrors 逐部件误差，通常是根位置容差太小或角度符号反了。
/// </summary>
public class PoseJudge : MonoBehaviour
{
    [System.Serializable]
    public class PartTarget
    {
        [Tooltip("对应 PuppetJoint 所在物体名")]
        public string partName;
        [Tooltip("目标局部 Z 角（度）")]
        public float targetAngle;
        [Tooltip("覆写全局角度容差")]
        public bool overrideAngleTol;
        public float angleTolerance = 8f;
    }

    public ShadowPuppet puppet;

    [Header("目标姿态（世界单位）")]
    public Vector2 targetRootPos = new Vector2(1.8f, -1.6f);
    [Tooltip("根位置容差（世界单位）")]
    public float rootPosTolerance = 0.35f;
    public List<PartTarget> parts = new List<PartTarget>();

    [Header("星级阈值（达标率 r）")]
    public float star3At = 1f;
    public float star2At = 0.8f;
    public float star1At = 0.6f;

    public struct FailInfo
    {
        public string partName;   // 部件名；根位置失败时为 "根位置"
        public float error;       // 角度差（度）或位置差（世界单位）
        public float tolerance;
    }

    public struct Result
    {
        public float passRate;
        public int stars;
        public List<FailInfo> fails;
    }

    public bool HasConfig => puppet != null;

    public Result Evaluate()
    {
        var result = new Result { fails = new List<FailInfo>() };
        if (!HasConfig) return result;

        int total = 1 + parts.Count; // 根位置算一个部件
        int passed = 0;

        var pose = puppet.CapturePose();
        float rootErr = Vector2.Distance(pose.rootPos, targetRootPos);
        if (rootErr <= rootPosTolerance) passed++;
        else result.fails.Add(new FailInfo { partName = "根位置", error = rootErr, tolerance = rootPosTolerance });

        var joints = GetJoints();
        foreach (var pt in parts)
        {
            PuppetJoint joint = null;
            foreach (var j in joints)
                if (j != null && j.name == pt.partName) { joint = j; break; }
            if (joint == null) { passed++; continue; } // 配了不存在的部件按达标处理，避免卡关
            float tol = pt.overrideAngleTol ? pt.angleTolerance : 8f;
            float err = Mathf.Abs(Mathf.DeltaAngle(joint.CurrentAngle, pt.targetAngle));
            if (err <= tol) passed++;
            else result.fails.Add(new FailInfo { partName = pt.partName, error = err, tolerance = tol });
        }

        result.passRate = total > 0 ? (float)passed / total : 1f;
        result.stars = StarsFor(result.passRate, star3At, star2At, star1At);
        return result;
    }

    /// <summary>未达标部件列表（给 GhostHint 失败高亮 / PlayResultUI 失败提示用）。</summary>
    public List<FailInfo> GetFailParts() => Evaluate().fails;

    public static int StarsFor(float r, float s3, float s2, float s1)
    {
        if (r >= s3) return 3;
        if (r >= s2) return 2;
        if (r >= s1) return 1;
        return 0;
    }

    /// <summary>逐部件误差文本（调试面板打印，排查"看着对了却不过"）。</summary>
    public string DescribeErrors()
    {
        if (!HasConfig) return "[姿态判定] 未配置皮影";
        var sb = new System.Text.StringBuilder("[姿态判定]");
        var pose = puppet.CapturePose();
        sb.Append($"\n  根位置 目标({targetRootPos.x:0.00},{targetRootPos.y:0.00}) ");
        float rootErr = Vector2.Distance(pose.rootPos, targetRootPos);
        sb.AppendLine(rootErr <= rootPosTolerance ? $"√ 差{rootErr:0.00}" : $"× 差{rootErr:0.00}/{rootPosTolerance:0.00}");
        var joints = GetJoints();
        foreach (var pt in parts)
        {
            PuppetJoint joint = null;
            foreach (var j in joints)
                if (j != null && j.name == pt.partName) { joint = j; break; }
            if (joint == null) { sb.AppendLine($"  {pt.partName} 未找到(按达标)"); continue; }
            float tol = pt.overrideAngleTol ? pt.angleTolerance : 8f;
            float err = Mathf.Abs(Mathf.DeltaAngle(joint.CurrentAngle, pt.targetAngle));
            sb.AppendLine($"  {pt.partName} 目标{pt.targetAngle:0.#}° 当前{joint.CurrentAngle:0.#}° {(err <= tol ? "√" : $"×")} 差{err:0.#}°/{tol:0.#}°");
        }
        return sb.ToString();
    }

    List<PuppetJoint> GetJoints()
    {
        if (puppet.joints != null && puppet.joints.Count > 0) return puppet.joints;
        return new List<PuppetJoint>(puppet.GetComponentsInChildren<PuppetJoint>());
    }

    /// <summary>把当前姿态记为目标（运行时摆好后右键组件菜单执行，再拷数值回 Inspector）。</summary>
    [ContextMenu("从当前姿态捕获目标")]
    public void CaptureTargetFromCurrent()
    {
        if (!HasConfig) return;
        targetRootPos = puppet.transform.position;
        parts.Clear();
        foreach (var j in GetJoints())
            parts.Add(new PartTarget { partName = j.name, targetAngle = j.CurrentAngle });
        Debug.Log($"[PoseJudge] 已捕获目标：根{targetRootPos} + {parts.Count} 部件。记得把数值拷回 Inspector 固化。");
    }

    /// <summary>把皮影摆到目标姿态（校准幽灵影/环形目标线、验证目标可达性用）。</summary>
    [ContextMenu("把皮影摆到目标姿态")]
    public void ApplyTargetToPuppet()
    {
        if (!HasConfig) return;
        puppet.transform.position = new Vector3(targetRootPos.x, targetRootPos.y, puppet.transform.position.z);
        foreach (var pt in parts)
            foreach (var j in GetJoints())
                if (j != null && j.name == pt.partName) j.RotateTo(pt.targetAngle);
    }
}
