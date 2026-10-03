using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// STEP-4 关卡数据库：选关页顺序与解锁链的唯一来源。
/// 加一关的完整流程 = 建 LevelConfig 资产 → 拖进本表 → 出现在选关页，全程不碰代码。
/// </summary>
[CreateAssetMenu(menuName = "LanternKeeper/关卡数据库 LevelDatabase", fileName = "LevelDatabase")]
public class LevelDatabase : ScriptableObject
{
    [Tooltip("按顺序排列的关卡（第 0 个默认解锁，通关上一关解锁下一关）")]
    public List<LevelConfig> levels = new List<LevelConfig>();
}
