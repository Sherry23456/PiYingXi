using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// STEP-4 全流程 PlayMode 验证（可批处理跑：-runTests -testPlatform PlayMode）：
/// Boot→MainMenu→选关→关A摆影开演3★→下一关B→移灯开演3★→重玩→回选关亮星→重置存档归零。
/// 判定摆位用真实数据：姿态直接摆到目标；移灯用 LampJudge 针孔模型反解灯位（与策划公式同源）。
/// 截图走 ScreenCapture.CaptureScreenshotIntoRenderTexture（含 UI），存 Assets/Screenshots/test-*.png 供人工复核字体渲染。
/// </summary>
public class PlayFlowTests
{
    const float Timeout = 8f;
    const float PerfTimeout = 15f;

    [UnityTest]
    public IEnumerator FullLoop_Menu_Select_Play_Stars_Save_Reset()
    {
        // 0) 清场：删旧档，从主菜单起步（MainMenu 场景对象自带接线好的 GameFlow）
        SaveSystem.DeleteSave();
        SceneManager.LoadScene(GameFlow.SceneMainMenu);
        yield return WaitScene(GameFlow.SceneMainMenu);
        var flow = GameFlow.EnsureInstance();
        Assert.IsNotNull(flow, "GameFlow 未创建");
        Assert.IsNotNull(GameObject.Find("Btn_开始游戏"), "主菜单缺[开始游戏]按钮");
        yield return Shot("test-mainmenu.png");

        // 1) 选关页：三卡，仅关A解锁
        flow.ToLevelSelect();
        yield return WaitScene(GameFlow.SceneLevelSelect);
        Assert.IsNotNull(GameObject.Find("Card_关A · 纯摆"), "选关页缺关A卡片");
        Assert.IsNotNull(GameObject.Find("Card_关B · 移灯"), "选关页缺关B卡片");
        Assert.IsTrue(flow.IsUnlocked(0), "第0关应默认解锁");
        Assert.IsFalse(flow.IsUnlocked(1), "第1关未通关应锁定");
        yield return Shot("test-levelselect.png");

        // 2) 进关A：Intro 剧情字条
        flow.StartLevel(0);
        yield return WaitScene(GameFlow.SceneGameplay);
        var lm = Object.FindFirstObjectByType<LevelManager>();
        Assert.IsNotNull(lm, "Gameplay 缺 LevelManager");
        yield return WaitState(lm, LevelManager.State.Intro);
        Assert.AreEqual("关A · 纯摆", lm.LoadedConfig.displayName, "加载的关卡数据不对");
        Assert.IsTrue((GameObject)Priv(lm, "introUI") != null, "缺剧情字条 UI");
        yield return Shot("test-intro.png");

        // 3) 关A：关字条 → 摆到目标姿态 → 开演 → 3★
        InvokePriv(lm, "EnterSolving");
        yield return WaitState(lm, LevelManager.State.Solving);
        var pj = Object.FindFirstObjectByType<PoseJudge>();
        Assert.IsNotNull(pj, "关A缺姿态判定");
        pj.ApplyTargetToPuppet(); // 把皮影摆到目标姿态（官方接口）
        yield return null;
        yield return Shot("test-solving.png");
        InvokePriv(lm, "OnPlayClicked");
        yield return WaitState(lm, LevelManager.State.Performing);
        yield return WaitState(lm, LevelManager.State.Result);
        Assert.AreEqual(3, flow.GetStars(0), "关A满判定应得3★");
        Assert.IsTrue(flow.IsUnlocked(1), "通关关A应解锁关B");
        yield return Shot("test-result.png");

        // 4) 下一关 → 关B：Intro → 反解灯位 → 开演 3★
        var resultUIA = Object.FindFirstObjectByType<PlayResultUI>();
        InvokePriv(resultUIA, "OnNext"); // 与"下一关"按钮同一入口（onNext 回调）
        yield return WaitScene(GameFlow.SceneGameplay);
        lm = Object.FindFirstObjectByType<LevelManager>();
        yield return WaitState(lm, LevelManager.State.Intro);
        Assert.AreEqual("关B · 移灯", lm.LoadedConfig.displayName, "应进入关B");
        InvokePriv(lm, "EnterSolving");
        yield return WaitState(lm, LevelManager.State.Solving);

        var lj = Object.FindFirstObjectByType<LampJudge>();
        Assert.IsNotNull(lj, "关B缺移灯判定");
        Assert.IsFalse(lj.EvaluatePass(out _, out _), "灯未移动时不应达标");
        Vector2 lampPos = SolveLampFor(lj);
        lj.lamp.position = new Vector3(lampPos.x, lampPos.y, 0f);
        yield return null;
        Assert.IsTrue(lj.EvaluatePass(out float ePos, out float eSize),
            $"反解灯位后移灯判定应达标（位置差{ePos:0.00} 尺寸差{eSize:0.00}）");

        InvokePriv(lm, "OnPlayClicked");
        yield return WaitState(lm, LevelManager.State.Performing);
        yield return WaitState(lm, LevelManager.State.Result);
        Assert.AreEqual(3, flow.GetStars(1), "关B达标应得3★");

        // 5) 失败路径：进关C不摆任何东西直接开演 → 仍停 Solving + 失败反馈
        var resultUIB = Object.FindFirstObjectByType<PlayResultUI>();
        InvokePriv(resultUIB, "OnNext");
        yield return WaitScene(GameFlow.SceneGameplay);
        lm = Object.FindFirstObjectByType<LevelManager>();
        yield return WaitState(lm, LevelManager.State.Intro);
        Assert.AreEqual("关C · 混合", lm.LoadedConfig.displayName, "应进入关C");
        InvokePriv(lm, "EnterSolving");
        yield return WaitState(lm, LevelManager.State.Solving);
        InvokePriv(lm, "OnPlayClicked");
        yield return WaitState(lm, LevelManager.State.Solving, 0.5f); // 演出不该触发
        var resultUI = Object.FindFirstObjectByType<PlayResultUI>();
        Assert.IsNotNull(resultUI, "关C缺结果UI组件");
        Assert.IsTrue(resultUI.gameObject.activeSelf && resultUI.transform.Find("ResultCanvas/Panel").gameObject.activeSelf,
            "失败后应弹出失败面板");
        yield return Shot("test-fail.png");

        // 6) 重玩：重载同关
        InvokePriv(resultUI, "OnRetry");
        yield return WaitScene(GameFlow.SceneGameplay);
        lm = Object.FindFirstObjectByType<LevelManager>();
        yield return WaitState(lm, LevelManager.State.Intro);
        Assert.AreEqual("关C · 混合", lm.LoadedConfig.displayName, "重玩应回到关C");

        // 7) 回选关：亮星与解锁链
        flow.ToLevelSelect();
        yield return WaitScene(GameFlow.SceneLevelSelect);
        Assert.AreEqual(3, flow.GetStars(0), "选关页读档：关A 3★");
        Assert.AreEqual(3, flow.GetStars(1), "选关页读档：关B 3★");
        Assert.IsTrue(flow.IsUnlocked(2), "关C已通关应解锁");
        yield return Shot("test-levelselect-stared.png");

        // 8) 存档落盘与重载
        var disk = SaveSystem.Load();
        Assert.AreEqual(3, disk.GetStars(1), "磁盘存档应含关B 3★");

        // 9) 重置存档：全部归零（主菜单两步确认按钮同逻辑）
        flow.ResetSave();
        Assert.AreEqual(0, flow.GetStars(0), "重置后关A星应为0");
        Assert.IsFalse(flow.IsUnlocked(1), "重置后关B应重新锁定");
        Assert.IsFalse(System.IO.File.Exists(SaveSystem.SavePath), "重置后 save.json 应不存在");
    }

    // ---------- 工具 ----------

    /// <summary>
    /// 空跑演练（STEP-4 验收核心）：内存克隆关A数据表 → 纯改数据（数字/文案）→ 挂进 LevelDatabase →
    /// 选关页出现并可通关拿星，全程不写任何运行时代码。演练为纯内存操作（Play 中对 SO 写盘引用不可靠，
    /// 即策划文档"已知坑"；人工流程=复制 SO 改数字拖进数据库，管线等价）。测完自愈还原。
    /// </summary>
    [UnityTest]
    public IEnumerator Drill_DataOnlyLevel_Playable()
    {
        SaveSystem.DeleteSave();
        SceneManager.LoadScene(GameFlow.SceneMainMenu); // 保证有带数据库接线的 GameFlow
        yield return WaitScene(GameFlow.SceneMainMenu);

        var flow = GameFlow.EnsureInstance();
        Assert.IsNotNull(flow.database, "GameFlow 未接数据库");
        flow.database.levels.RemoveAll(l => l == null); // 自愈：清历史残留
        int before = flow.database.levels.Count;
        LevelConfig d = null;

        try
        {
            // ① 复制数据表并纯改数字与文案（Inspector 同款操作，仅内存）
            d = Object.Instantiate(flow.database.levels[0]);
            d.levelId = 4;
            d.displayName = "关D · 演练验收";
            d.hintText = "关D · 演练验收：本关由纯数据复制而来";
            d.puppets[0].targetPos = new Vector2(0.5f, -1.2f);
            d.puppets[0].targetAngles[0].angle = 25f;
            d.puppets[0].targetAngles[1].angle = -25f;

            // ② 拖进数据库（运行时数据接入）
            flow.database.levels.Add(d);
            yield return null;

            // ③ 选关页出现第 4 张卡
            flow.ToLevelSelect();
            yield return WaitScene(GameFlow.SceneLevelSelect);
            Assert.AreEqual(before + 1, flow.Levels.Count, "数据库应新增 1 关");
            Assert.IsNotNull(GameObject.Find("Card_关D · 演练验收"), "选关页缺演练关卡片");
            Assert.IsFalse(flow.IsUnlocked(3), "清档后演练关应锁定（解锁链由上一关通关状态决定）");
            yield return Shot("test-drill-levelselect.png");

            // ④ 进入演练关可玩：摆到目标 → 开演 → 3★
            flow.StartLevel(3);
            yield return WaitScene(GameFlow.SceneGameplay);
            var lm = Object.FindFirstObjectByType<LevelManager>();
            yield return WaitState(lm, LevelManager.State.Intro);
            Assert.AreEqual("关D · 演练验收", lm.LoadedConfig.displayName, "演练关数据未生效");
            InvokePriv(lm, "EnterSolving");
            yield return WaitState(lm, LevelManager.State.Solving);
            var pj = Object.FindFirstObjectByType<PoseJudge>();
            pj.ApplyTargetToPuppet();
            yield return null;
            InvokePriv(lm, "OnPlayClicked");
            yield return WaitState(lm, LevelManager.State.Performing);
            yield return WaitState(lm, LevelManager.State.Result);
            Assert.AreEqual(3, flow.GetStars(3), "演练关应可通关拿3★");
            yield return Shot("test-drill-result.png");
        }
        finally
        {
            // ⑤ 还原：移出演练关、清档
            if (d != null) flow.database.levels.Remove(d);
            if (d != null) Object.Destroy(d);
            flow.ResetSave();
        }
    }

    // ---------- 工具 ----------

    /// <summary>用 LampJudge 针孔模型反解"影子落点=目标"的灯位：|S-P|=(R-d)·centerStretch → L = P - d·u。</summary>
    static Vector2 SolveLampFor(LampJudge lj)
    {
        Vector2 P = lj.shadowCaster.position;
        Vector2 S = lj.targetShadowPos;
        Vector2 u = (S - P).normalized;
        float R = lj.lampLight.pointLightOuterRadius;
        float d = R - (S - P).magnitude / Mathf.Max(lj.centerStretch, 0.01f);
        return P - u * d;
    }

    static IEnumerator WaitScene(string name)
    {
        float t = 0f;
        while (SceneManager.GetActiveScene().name != name)
        {
            t += Time.unscaledDeltaTime;
            if (t > Timeout) Assert.Fail($"等待场景 {name} 超时，当前 {SceneManager.GetActiveScene().name}");
            yield return null;
        }
        yield return null; // 让 Start 跑完一帧
    }

    static IEnumerator WaitState(LevelManager lm, LevelManager.State state, float timeout = Timeout)
    {
        float t = 0f;
        while (lm == null || lm.CurrentState != state)
        {
            t += Time.unscaledDeltaTime;
            if (t > timeout) Assert.Fail($"等待状态 {state} 超时（当前 {lm.CurrentState}）");
            yield return null;
        }
    }

    static object Priv(object o, string field)
    {
        var f = o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        return f != null ? f.GetValue(o) : null;
    }

    static void InvokePriv(object o, string method)
    {
        var m = o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(m, $"反射调用失败：{o.GetType().Name}.{method}");
        m.Invoke(o, null);
    }

    static IEnumerator Shot(string fileName)
    {
        // CaptureScreenshotIntoRenderTexture 在帧末异步完成：等两帧再读，且 RT 垂直翻转需翻回
        var rt = new RenderTexture(Screen.width, Screen.height, 24);
        ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
        yield return null;
        yield return null;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        var px = tex.GetPixels32();
        int h = tex.height, w = tex.width;
        for (int y = 0; y < h / 2; y++)
            for (int x = 0; x < w; x++)
            {
                var t = px[y * w + x]; px[y * w + x] = px[(h - 1 - y) * w + x]; px[(h - 1 - y) * w + x] = t;
            }
        tex.SetPixels32(px); tex.Apply();
        System.IO.File.WriteAllBytes("Assets/Screenshots/" + fileName, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
    }
}
