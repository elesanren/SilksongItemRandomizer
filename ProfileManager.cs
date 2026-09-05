using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SilksongItemRandomizer
{
    public static class ProfileManager
    {
        public static int CurrentProfile { get; private set; } = 1;

        public static string ProfileDir => Path.Combine(Paths.ConfigPath, "SilksongItemRandomizer");
        public static string GlobalSavePathFor(int id) => Path.Combine(ProfileDir, $"profile_{id}_global_save.json");
        public static string ConfigPathFor(int id) => Path.Combine(ProfileDir, $"profile_{id}_config.json");

        [Serializable]
        private class ConfigEntryRecord
        {
            public string Section;
            public string Key;
            public string Value;
        }

        public static void MigrateLegacyData()
        {
            try
            {
                if (!Directory.Exists(ProfileDir))
                    Directory.CreateDirectory(ProfileDir);

                string legacySave = Path.Combine(ProfileDir, "global_save.json");
                string targetSave = GlobalSavePathFor(1);

                if (File.Exists(legacySave) && !File.Exists(targetSave))
                {
                    File.Copy(legacySave, targetSave);
                    try { File.Delete(legacySave); }
                    catch { }
                }

                string configPath = ConfigPathFor(1);
                if (!File.Exists(configPath))
                {
                    SaveCurrentConfigInternal(1);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Profile] 迁移旧数据失败: {ex.Message}");
            }
        }

        public static void SaveCurrentConfig()
        {
            try
            {
                SaveCurrentConfigInternal(CurrentProfile);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Profile] 保存当前档配置失败: {ex.Message}");
            }
        }

        private static void SaveCurrentConfigInternal(int id)
        {
            var dict = GlobalConfig.ExportValues();
            var records = new List<ConfigEntryRecord>();
            foreach (var kv in dict)
            {
                records.Add(new ConfigEntryRecord
                {
                    Section = kv.Key.Item1,
                    Key = kv.Key.Item2,
                    Value = kv.Value
                });
            }
            if (!Directory.Exists(ProfileDir))
                Directory.CreateDirectory(ProfileDir);
            string json = JsonConvert.SerializeObject(records, Formatting.Indented);
            File.WriteAllText(ConfigPathFor(id), json);
        }

        public static void SwitchTo(int id)
        {
            if (id < 1 || id > 4) return;
            if (id == CurrentProfile) return;

            Plugin.SaveGlobalDataNow();
            SaveCurrentConfig();

            CurrentProfile = id;

            Plugin.Instance.LoadGlobalData();

            string configPath = ConfigPathFor(id);
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var records = JsonConvert.DeserializeObject<List<ConfigEntryRecord>>(json);
                if (records != null)
                {
                    var dict = new Dictionary<(string, string), string>();
                    foreach (var r in records)
                        dict[(r.Section, r.Key)] = r.Value;
                    GlobalConfig.ApplyValues(dict);
                }
            }

            Plugin.RebuildRuntimeState();

            RecentItemsUI.Reset();
            RecentItemsUI.RestoreFromSave();
        }

        public static void OnProfileChanged()
        {
            int id = GameManager.instance != null ? GameManager.instance.profileID : 0;
            if (id >= 1 && id <= 4 && id != CurrentProfile)
            {
                Plugin.Log.LogInfo($"[Profile] 事件驱动切档: {CurrentProfile} -> {id}");
                SwitchTo(id);
            }
        }
    }
}
