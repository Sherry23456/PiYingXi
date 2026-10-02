using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// STEP-1 临时拖灯脚本：按住鼠标左键/触屏，拖动物体的 Transform。
/// 用法：挂在灯的根物体上；命中检测走子物体上的 2D 碰撞体（GetComponentInParent 回溯到根）。
/// 本工程 activeInputHandler=1（仅新输入系统），不可使用旧 Input 类。
/// </summary>
public class DragTransform : MonoBehaviour
{
    [Tooltip("小于 0 时自动取相机到 z=0 平面的距离（正交相机适用）")]
    public float depthOverride = -1f;

    private Camera cam;
    private bool dragging;
    private Vector3 grabOffset;

    private void Awake()
    {
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

        if (pressed)
        {
            var col = Physics2D.OverlapPoint(world);
            if (col != null && col.GetComponentInParent<DragTransform>() == this)
            {
                dragging = true;
                grabOffset = transform.position - world;
            }
        }

        if (dragging)
        {
            if (held) transform.position = world + grabOffset;
            else dragging = false;
        }
    }

    private Vector3 ScreenToWorld(Vector2 screen)
    {
        float depth = depthOverride >= 0f ? depthOverride : Mathf.Abs(cam.transform.position.z);
        return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
    }
}
