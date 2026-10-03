using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

/// <summary>
/// STEP-4 关卡数据表（ScriptableObject）：之后加一关 = 建一张本表，全程不碰代码。
/// 已知坑规避：所有资源引用一律直接拖字段（预制体/Sprite/Timeline），禁用 Resources 字符串路径加载——
/// SO 里嵌套引用在打包后仍随场景/资产序列化保留，字符串路径才是丢引用的主因。
/// 容差一律世界单位/角度，禁止像素（STEP-3 结论：换分辨率全崩）。
/// </summary>
[CreateAssetMenu(menuName = "LanternKeeper/关卡配置 LevelConfig", fileName = "Level_New")]
public class LevelConfig : ScriptableObject
{
    [Header("标识")]
    [Tooltip("存档/解锁链用的稳定 ID，建关后不要再改")]
    public int levelId = 1;
    public string displayName = "新关";

    [Header("文案")]
    [Tooltip("开演前的剧情字条")]
    [TextArea(2, 6)] public string storyIntro = "";
    [Tooltip("通关后展示的知识卡（内容红线：未审校必须勾 pendingReview）")]
    [TextArea(2, 6)] public string knowledgeCard = "";
    [Tooltip("知识卡是否未审校（true 时游戏内挂'待审校'角标）")]
    public bool knowledgePendingReview = true;
    [Tooltip("解谜界面右上角的操作提示条")]
    [TextArea(1, 2)] public string hintText = "";

    [Header("皮影（预制体 + 初始姿态 + 目标姿态）")]
    public List<PuppetEntry> puppets = new List<PuppetEntry>();

    [Header("灯")]
    public List<LampEntry> lamps = new List<LampEntry>();

    [Header("道具")]
    public List<PropEntry> props = new List<PropEntry>();

    [Header("容差覆写（不勾选则用全局默认）")]
    public ToleranceOverride tolerance;

    [Header("预留（后续步骤接入，STEP-4 留空即可）")]
    [Tooltip("STEP-5 起可换美术幽灵影图")]
    public Sprite ghostHintSprite;
    [Tooltip("STEP-8 演出 Timeline；留空用占位演出（灯闪+锣+推镜）")]
    public TimelineAsset performanceTimeline;
    [Tooltip("STEP-11 关卡 BGM")]
    public AudioClip bgm;
    [Tooltip("UI 上的目标剪影图（美术期）")]
    public Sprite targetSilhouetteSprite;
    [Tooltip("3★ 附加条件用时（秒），0 = 不启用（默认不用）")]
    public float parTime = 0f;

    // ---------- 子表结构 ----------

    [System.Serializable]
    public class PuppetEntry
    {
        [Tooltip("皮影预制体（根上挂 ShadowPuppet）")]
        public GameObject prefab;
        public Vector2 initialPos;
        [Tooltip("本关皮影是否可拖动（移灯关固定皮影时取消勾选）")]
        public bool canDrag = true;
        [Tooltip("是否参与姿态判定（纯移灯关取消勾选）")]
        public bool judgePose = true;
        [Tooltip("开演准备：各部件初始局部 Z 角（留空用预制体默认）")]
        public List<NamedAngle> initialAngles = new List<NamedAngle>();
        [Tooltip("目标根位置（世界单位）")]
        public Vector2 targetPos;
        [Tooltip("目标姿态：各部件目标局部 Z 角")]
        public List<NamedAngle> targetAngles = new List<NamedAngle>();
    }

    [System.Serializable]
    public class NamedAngle
    {
        [Tooltip("对应 PuppetJoint 所在物体名")]
        public string partName;
        public float angle;
    }

    [System.Serializable]
    public class LampEntry
    {
        public string lampName = "Lantern";
        public Vector2 pos;
        public Color color = new Color(1f, 0.82f, 0.55f, 1f);
        public float intensity = 1f;
        [Tooltip("光照外半径（世界单位）")]
        public float outerRadius = 6f;
        [Tooltip("本关是否可拖动")]
        public bool canDrag = true;
        [Tooltip("影子目标（参与'影子套目标'判定时填写，移灯关用）")]
        public ShadowTarget shadowTarget;
    }

    [System.Serializable]
    public class ShadowTarget
    {
        [Tooltip("是否参与移灯判定")]
        public bool judge;
        public Vector2 targetPos;
        [Tooltip("目标影子半径（世界单位）")]
        public float targetRadius = 3.5f;
    }

    [System.Serializable]
    public class PropEntry
    {
        public GameObject prefab;
        public Vector2 pos;
        public float scale = 1f;
        [Tooltip("排序层（幕布-10，皮影 0~9，灯 10）")]
        public int sortingOrder;
    }

    [System.Serializable]
    public class ToleranceOverride
    {
        public bool useOverride;
        [Tooltip("部件角度容差（度）")] public float angleTolerance = 8f;
        [Tooltip("皮影根位置容差（世界单位）")] public float rootPosTolerance = 0.35f;
        [Tooltip("影子落点容差（世界单位）")] public float shadowPosTolerance = 0.5f;
        [Tooltip("影子半径相对容差（比例）")] public float shadowSizeTolerance = 0.25f;
    }
}
