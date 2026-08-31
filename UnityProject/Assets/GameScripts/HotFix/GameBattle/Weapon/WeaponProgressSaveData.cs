using System;
using System.Collections.Generic;

namespace GameBattle.Weapon
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

    /// <summary>Unity JsonUtility 可序列化的武器进度存档。</summary>
    [Serializable]
    public sealed class WeaponProgressSaveData
    {
        public int schemaVersion = 1;
        public int revision;
        public List<WeaponFragmentSaveRecord> fragmentRecords =
            new List<WeaponFragmentSaveRecord>();
        public List<WeaponEquipSaveRecord> equipRecords =
            new List<WeaponEquipSaveRecord>();
        public List<WeaponPendingSaveRecord> pendingRecords =
            new List<WeaponPendingSaveRecord>();
    }
}
