using TMPro;
using UnityEngine;

/// <summary>
/// STEP-2 输入调试面板：屏幕角落显示当前命中部件与各关节角度（任务 3）。
/// 验证手感后整段删除：DebugCanvas 物体 + 本脚本。
/// </summary>
public class PuppetDebugPanel : MonoBehaviour
{
    public TMP_Text label;

    private void Update()
    {
        if (label == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[STEP-2 Input Debug]");

        var dragger = PuppetDragger.Instance;
        sb.AppendLine($"Hit: {(dragger != null ? dragger.CurrentHitName : "-")}");
        sb.AppendLine("----");

        foreach (var sp in FindObjectsByType<ShadowPuppet>(FindObjectsSortMode.None))
        {
            var pose = sp.CapturePose();
            sb.AppendLine($"{sp.name} root({pose.rootPos.x:0.00},{pose.rootPos.y:0.00})");
            foreach (var j in pose.joints)
                sb.AppendLine($"  {j.partName}: {j.localAngle:0.#} deg");
        }

        label.text = sb.ToString();
    }
}
