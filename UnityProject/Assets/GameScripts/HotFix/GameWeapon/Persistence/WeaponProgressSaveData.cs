using System;
using System.Collections.Generic;
using TEngine;
using UnityEngine;

namespace GameWeapon
{
    [Serializable]
    public sealed class WeaponFragmentSaveRecord
    {
        public int weaponId;
        public int totalFragments;
    }

    [Serializable]
    public sealed class WeaponEquipSaveRecord
    {
        public int slot;
        public int weaponId;
    }

    [Serializable]
    public sealed class WeaponPendingSaveRecord
    {
        public int weaponId;
        public int pendingCount;
    }

    [Serializable]
    public sealed class WeaponProgressSaveData
    {
        public int schemaVersion = 1;
        public int revision;
        public List<WeaponFragmentSaveRecord> fragmentRecords = new List<WeaponFragmentSaveRecord>();
        public List<WeaponEquipSaveRecord> equipRecords = new List<WeaponEquipSaveRecord>();
        public List<WeaponPendingSaveRecord> pendingRecords = new List<WeaponPendingSaveRecord>();
    }

    public interface IWeaponProgressStore
    {
        bool TryLoad(out WeaponProgressSaveData data);
        bool TrySave(WeaponProgressSaveData data, out string error);
    }

    public sealed class PlayerPrefsWeaponProgressStore : IWeaponProgressStore
    {
        internal const string ActiveKey = "PLAYER_WEAPON_PROGRESS_V1";
        internal const string BackupKey = "PLAYER_WEAPON_PROGRESS_V1_BACKUP";

        public bool TryLoad(out WeaponProgressSaveData data)
        {
            if (TryLoadKey(ActiveKey, out data))
            {
                return true;
            }

            return TryLoadKey(BackupKey, out data);
        }

        public bool TrySave(WeaponProgressSaveData data, out string error)
        {
            if (data == null)
            {
                error = "武器存档不能为空";
                return false;
            }

            try
            {
                string json = JsonUtility.ToJson(data);
                if (Utility.PlayerPrefs.HasKey(ActiveKey))
                {
                    string previous = Utility.PlayerPrefs.GetString(ActiveKey, string.Empty);
                    Utility.PlayerPrefs.SetString(BackupKey, previous, save: false);
                }

                Utility.PlayerPrefs.SetString(ActiveKey, json, save: true);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = $"保存武器进度失败：{ex.GetType().Name}";
                return false;
            }
        }

        private static bool TryLoadKey(string key, out WeaponProgressSaveData data)
        {
            data = null;
            if (!Utility.PlayerPrefs.HasKey(key))
            {
                return false;
            }

            try
            {
                string json = Utility.PlayerPrefs.GetString(key, string.Empty);
                data = JsonUtility.FromJson<WeaponProgressSaveData>(json);
                return data != null && data.schemaVersion == 1;
            }
            catch (Exception ex)
            {
                Log.Error($"[PlayerPrefsWeaponProgressStore] 读取 {key} 失败：{ex}");
                data = null;
                return false;
            }
        }
    }
}
