using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// STEP-4 JSON 存档：Application.persistentDataPath/save.json。
/// 内容：每关星级 + 解锁状态、幽灵影默认档、音量。
/// 已知坑规避：版本号字段 + try-catch——字段变更导致旧档反序列化失败时重置新档，不报错不卡流程。
/// </summary>
public static class SaveSystem
{
    const string FileName = "save.json";
    public const int CurrentVersion = 1;

    [Serializable]
    public class LevelRecord
    {
        public int levelId;
        public int stars;
        public bool unlocked;
    }

    [Serializable]
    public class SaveData
    {
        public int version = CurrentVersion;
        public List<LevelRecord> levels = new List<LevelRecord>();
        [Tooltip("幽灵影默认档：0 隐藏 / 1 显示 / 2 显示+高亮")]
        public int ghostHintDefault = 1;
        [Tooltip("主音量 0~1")]
        public float masterVolume = 1f;

        public LevelRecord GetRecord(int levelId)
        {
            foreach (var r in levels)
                if (r != null && r.levelId == levelId) return r;
            return null;
        }

        public int GetStars(int levelId) => GetRecord(levelId)?.stars ?? 0;

        /// <summary>记录通关结果：星级只升不降。</summary>
        public void RecordResult(int levelId, int stars)
        {
            var rec = GetRecord(levelId);
            if (rec == null)
            {
                rec = new LevelRecord { levelId = levelId };
                levels.Add(rec);
            }
            rec.stars = Mathf.Max(rec.stars, stars);
            if (stars >= 1) rec.unlocked = true;
        }

        public void Unlock(int levelId)
        {
            var rec = GetRecord(levelId);
            if (rec == null)
            {
                rec = new LevelRecord { levelId = levelId };
                levels.Add(rec);
            }
            rec.unlocked = true;
        }
    }

    public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    public static SaveData Load()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                if (data != null)
                {
                    if (data.levels == null) data.levels = new List<LevelRecord>();
                    return data;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[存档] 读取失败，已重置为新档：{e.Message}");
        }
        return new SaveData();
    }

    public static void Save(SaveData data)
    {
        if (data == null) return;
        try
        {
            data.version = CurrentVersion;
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogError($"[存档] 写入失败：{e.Message}");
        }
    }

    /// <summary>删存档重置（主菜单"重置存档"调用）。</summary>
    public static void DeleteSave()
    {
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[存档] 删除失败：{e.Message}");
        }
    }
}
