using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// STEP-2 统一指针输入（Input System 轮询方案，与 STEP-1 DragTransform 同套路，先跑通者为准，不叠加两套）：
///   命中带 PuppetJoint 的部件 → 绕枢轴旋转该关节（限位夹取 + 松手吸附）；
///   命中皮影身体 → 整体拖动根（ShadowPuppet.DragTo）。
/// 命中取"排序层最上层"的碰撞体（OverlapPointAll + SpriteRenderer.sortingOrder），
/// 所以点手臂/金箍棒转关节、点身体移动，互不抢。
/// 触屏：Pointer.current 同时覆盖鼠标/触屏/笔；触屏模拟鼠标开关见
/// Project Settings > Input System Package（桌面鼠标原生可用，不影响验证）。
/// </summary>
public class PuppetDragger : MonoBehaviour
{
    public static PuppetDragger Instance { get; private set; }

    [Header("手感")]
    [Tooltip("旋转灵敏度：1 = 指针绕枢轴 1:1 跟随")]
    public float rotateSensitivity = 1f;

    /// <summary>调试面板读取：当前命中的部件名与角度。</summary>
    public string CurrentHitName { get; private set; } = "-";

    private enum DragKind { None, MoveRoot, RotateJoint }

    private DragKind kind = DragKind.None;
    private ShadowPuppet puppet;
    private PuppetJoint joint;
    private Vector3 grabOffset;
    private float startPointerAngle;
    private float startJointAngle;
    private Camera cam;

    private void Awake()
    {
        Instance = this;
        cam = Camera.main;
    }

    private void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        var pointer = Pointer.current;
        if (pointer == null) return;

        Vector2 screen = pointer.position.ReadValue();
        bool pressed = pointer.press.wasPressedThisFrame;
        bool held = pointer.press.isPressed;
        Vector3 world = ScreenToWorld(screen);

        if (pressed) Pick(world);

        if (kind == DragKind.None) return;
        if (!held)
        {
            Release();
            return;
        }

        if (kind == DragKind.RotateJoint && joint != null)
        {
            Vector3 p = joint.pivot != null ? joint.pivot.position : joint.transform.position;
            float now = AngleAround(p, world);
            float delta = Mathf.DeltaAngle(startPointerAngle, now) * rotateSensitivity;
            joint.RotateTo(startJointAngle + delta);
            CurrentHitName = $"{joint.name}  {joint.CurrentAngle:0.#}°";
        }
        else if (kind == DragKind.MoveRoot && puppet != null)
        {
            puppet.DragTo(world + grabOffset);
            CurrentHitName = puppet.name;
        }
    }

    private void Pick(Vector3 world)
    {
        joint = null;
        puppet = null;

        // STEP-3：指针压在 UI（开演/提示/结果面板按钮）上时不抓场景物体
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            kind = DragKind.None;
            CurrentHitName = "-";
            return;
        }

        Collider2D col = TopmostAt(world);
        if (col == null)
        {
            kind = DragKind.None;
            CurrentHitName = "-";
            return;
        }

        joint = col.GetComponentInParent<PuppetJoint>();
        if (joint != null)
        {
            kind = DragKind.RotateJoint;
            Vector3 p = joint.pivot != null ? joint.pivot.position : joint.transform.position;
            startPointerAngle = AngleAround(p, world);
            startJointAngle = joint.CurrentAngle;
            return;
        }

        puppet = col.GetComponentInParent<ShadowPuppet>();
        if (puppet != null)
        {
            kind = DragKind.MoveRoot;
            grabOffset = puppet.transform.position - world;
        }
        else
        {
            kind = DragKind.None; // 例如灯（走 DragTransform），不抢
        }
    }

    private void Release()
    {
        if (kind == DragKind.RotateJoint && joint != null) joint.SnapOnRelease();
        if (kind == DragKind.MoveRoot && puppet != null) puppet.EndDrag();
        kind = DragKind.None;
        joint = null;
        puppet = null;
    }

    /// <summary>取命中点最上层的非触发器 2D 碰撞体（按 SpriteRenderer 排序值，高者优先）。</summary>
    private static Collider2D TopmostAt(Vector3 world)
    {
        var hits = Physics2D.OverlapPointAll(world);
        Collider2D best = null;
        int bestOrder = int.MinValue;
        foreach (var h in hits)
        {
            if (h == null || h.isTrigger) continue;
            var r = h.GetComponentInParent<SpriteRenderer>();
            int order = r != null ? r.sortingOrder : 0;
            if (order > bestOrder)
            {
                bestOrder = order;
                best = h;
            }
        }
        return best;
    }

    private static float AngleAround(Vector3 pivotPos, Vector3 world)
    {
        Vector2 d = (Vector2)world - (Vector2)pivotPos;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    private Vector3 ScreenToWorld(Vector2 screen)
    {
        float depth = Mathf.Abs(cam.transform.position.z);
        return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
    }
}
