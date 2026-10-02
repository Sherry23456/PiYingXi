using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// STEP-2 皮影根脚本：挂在皮影根物体上。
/// 职责：① 收集/引用所有关节部件（留空自动收集子物体上的 PuppetJoint）；
///      ② 提供整体拖动接口（带跟随平滑，避免生硬穿透感）；
///      ③ 向判定系统（STEP-3 PoseJudge）暴露"读当前姿态"接口：根位置 + 各关节局部角。
/// </summary>
public class ShadowPuppet : MonoBehaviour
{
    [Tooltip("关节部件列表；留空则 Awake 时自动收集子物体")]
    public List<PuppetJoint> joints = new List<PuppetJoint>();

    [Header("拖拽跟随")]
    [Tooltip("0 = 即时跟随；>0 = 平滑跟随，值越大越跟手（默认 20）")]
    public float dragLerp = 20f;

    public bool IsDragging { get; private set; }

    private Vector3? dragTarget;

    private void Awake()
    {
        if (joints.Count == 0) joints.AddRange(GetComponentsInChildren<PuppetJoint>());
    }

    private void Update()
    {
        if (IsDragging && dragTarget.HasValue)
        {
            if (dragLerp <= 0f)
            {
                transform.position = dragTarget.Value;
            }
            else
            {
                float t = 1f - Mathf.Exp(-dragLerp * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, dragTarget.Value, t);
            }
        }
    }

    // ---- 整体拖动接口（PuppetDragger 调用） ----
    public void BeginDrag()
    {
        IsDragging = true;
        dragTarget = transform.position;
    }

    public void DragTo(Vector2 worldPos)
    {
        if (!IsDragging) BeginDrag();
        dragTarget = new Vector3(worldPos.x, worldPos.y, transform.position.z);
    }

    public void EndDrag()
    {
        IsDragging = false;
        dragTarget = null;
    }

    // ---- 判定系统读取接口（STEP-3 用，先定形状） ----
    public struct JointPose
    {
        public string partName;
        public float localAngle;
    }

    public struct PuppetPose
    {
        public Vector2 rootPos;
        public JointPose[] joints;
    }

    /// <summary>读取当前姿态：根世界位置 + 各关节相对枢轴父级的局部 Z 角。</summary>
    public PuppetPose CapturePose()
    {
        var pose = new PuppetPose
        {
            rootPos = transform.position,
            joints = new JointPose[joints.Count]
        };
        for (int i = 0; i < joints.Count; i++)
        {
            pose.joints[i].partName = joints[i] != null ? joints[i].name : "null";
            pose.joints[i].localAngle = joints[i] != null ? joints[i].CurrentAngle : 0f;
        }
        return pose;
    }
}
