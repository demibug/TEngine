using System;
using System.Collections.Generic;
using GameCommon.Battle;

namespace GameBattle.Weapon
{
    /// <summary>武器碎片、装备槽和待确认提示的局外状态。</summary>
    internal sealed class WeaponCollectionState
    {
        private static readonly (WeaponEquipSlot Slot, int WeaponId)[] BasicDefaults =
        {
            (WeaponEquipSlot.Bow, 1),
            (WeaponEquipSlot.Spear, 11),
            (WeaponEquipSlot.Knife, 21),
            (WeaponEquipSlot.Sword, 32),
        };

        private readonly WeaponCatalog _catalog;
        private readonly Dictionary<int, int> _fragments;
        private readonly Dictionary<WeaponEquipSlot, int> _equipped;
        private readonly Dictionary<int, int> _pending;
        private int _revision;

        private WeaponCollectionState(
            WeaponCatalog catalog,
            Dictionary<int, int> fragments,
            Dictionary<WeaponEquipSlot, int> equipped,
            Dictionary<int, int> pending,
            int revision)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _fragments = fragments;
            _equipped = equipped;
            _pending = pending;
            _revision = revision;
        }

        internal static WeaponCollectionState Create(
            WeaponCatalog catalog,
            WeaponProgressSaveData save)
        {
            var fragments = new Dictionary<int, int>();
            var equipped = new Dictionary<WeaponEquipSlot, int>();
            var pending = new Dictionary<int, int>();
            int revision = Math.Max(0, save?.revision ?? 0);

            if (save != null && save.schemaVersion == 1)
            {
                CopySaveRecords(save, fragments, equipped, pending);
            }

            var state = new WeaponCollectionState(
                catalog, fragments, equipped, pending, revision);
            state.EnsureBasicDefaults();
            state.NormalizePendingCounts();
            return state;
        }

        internal static WeaponCollectionState CreateDefault(WeaponCatalog catalog)
        {
            return Create(catalog, null);
        }

        internal WeaponCollectionState Clone()
        {
            return new WeaponCollectionState(
                _catalog,
                new Dictionary<int, int>(_fragments),
                new Dictionary<WeaponEquipSlot, int>(_equipped),
                new Dictionary<int, int>(_pending),
                _revision);
        }

        internal WeaponMutationResult ChangeFragments(int weaponId, int delta)
        {
            if (!_catalog.TryGet(weaponId, out WeaponDefinition definition))
            {
                return WeaponMutationResult.Fail($"未知武器 id={weaponId}");
            }

            int before = GetFragments(weaponId);
            long afterLong = (long)before + delta;
            if (afterLong < 0 || afterLong > int.MaxValue)
            {
                return WeaponMutationResult.Fail(
                    $"武器 id={weaponId} 碎片变更越界：before={before}, delta={delta}");
            }

            int after = (int)afterLong;
            int equippedCopies = IsWeaponEquipped(weaponId) ? 1 : 0;
            int minimum = equippedCopies * definition.FragmentNum;
            if (after < minimum)
            {
                return WeaponMutationResult.Fail(
                    $"武器 id={weaponId} 已装备，至少需要保留 {minimum} 个碎片");
            }

            int beforeCompleted = before / definition.FragmentNum;
            int afterCompleted = after / definition.FragmentNum;
            int created = Math.Max(0, afterCompleted - beforeCompleted);
            _fragments[weaponId] = after;

            if (created > 0)
            {
                _pending[weaponId] = GetPending(weaponId) + created;
            }
            else if (delta < 0 && GetPending(weaponId) > afterCompleted)
            {
                _pending[weaponId] = afterCompleted;
            }

            return WeaponMutationResult.Ok(weaponId, after, created);
        }

        internal WeaponMutationResult RecycleAllUnequipped(int weaponId)
        {
            if (!_catalog.TryGet(weaponId, out WeaponDefinition definition)
                || !definition.Recyclable)
            {
                return WeaponMutationResult.Fail($"武器 id={weaponId} 不允许回收");
            }

            int total = GetFragments(weaponId);
            int reserved = IsWeaponEquipped(weaponId) ? definition.FragmentNum : 0;
            int recyclable = Math.Max(0, total - reserved);
            if (recyclable == 0)
            {
                return WeaponMutationResult.Fail($"武器 id={weaponId} 没有可回收碎片");
            }

            WeaponMutationResult changed = ChangeFragments(weaponId, -recyclable);
            if (!changed.IsSuccess)
            {
                return changed;
            }

            int gold = checked(recyclable * definition.RecycleGoldPerFragment);
            return WeaponMutationResult.Ok(weaponId, changed.TotalFragments, 0, gold);
        }

        internal bool TryEquip(WeaponEquipSlot slot, int weaponId, out string error)
        {
            if (!_catalog.TryGet(weaponId, out WeaponDefinition definition))
            {
                error = $"未知武器 id={weaponId}";
                return false;
            }

            if (!definition.Enabled || definition.Slot != slot)
            {
                error = $"武器 id={weaponId} 未启用或与槽位 {slot} 不匹配";
                return false;
            }

            if (GetFragments(weaponId) / definition.FragmentNum < 1)
            {
                error = $"武器 id={weaponId} 尚未完整";
                return false;
            }

            _equipped[slot] = weaponId;
            error = string.Empty;
            return true;
        }

        internal void AcknowledgeNewWeapons()
        {
            _pending.Clear();
        }

        internal BattleWeaponLoadoutDto CreateBattleLoadout()
        {
            return new BattleWeaponLoadoutDto(
                bowWeaponId: GetEquipped(WeaponEquipSlot.Bow),
                spearWeaponId: GetEquipped(WeaponEquipSlot.Spear),
                knifeWeaponId: GetEquipped(WeaponEquipSlot.Knife),
                swordWeaponId: GetEquipped(WeaponEquipSlot.Sword));
        }

        internal WeaponProgressSnapshot CreateSnapshot()
        {
            var entries = new List<WeaponProgressEntry>(_catalog.Definitions.Count);
            foreach (WeaponDefinition definition in _catalog.Definitions)
            {
                int total = GetFragments(definition.Id);
                entries.Add(new WeaponProgressEntry(
                    definition.Id,
                    total,
                    total / definition.FragmentNum,
                    total % definition.FragmentNum,
                    GetPending(definition.Id),
                    IsWeaponEquipped(definition.Id)));
            }

            return new WeaponProgressSnapshot(
                entries.ToArray(),
                GetEquipped(WeaponEquipSlot.Bow),
                GetEquipped(WeaponEquipSlot.Spear),
                GetEquipped(WeaponEquipSlot.Knife),
                GetEquipped(WeaponEquipSlot.Sword));
        }

        internal WeaponProgressSaveData CreateSaveData()
        {
            var data = new WeaponProgressSaveData
            {
                schemaVersion = 1,
                revision = ++_revision,
            };

            foreach (KeyValuePair<int, int> pair in _fragments)
            {
                data.fragmentRecords.Add(new WeaponFragmentSaveRecord
                {
                    weaponId = pair.Key,
                    totalFragments = pair.Value,
                });
            }

            foreach (KeyValuePair<WeaponEquipSlot, int> pair in _equipped)
            {
                data.equipRecords.Add(new WeaponEquipSaveRecord
                {
                    slot = (int)pair.Key,
                    weaponId = pair.Value,
                });
            }

            foreach (KeyValuePair<int, int> pair in _pending)
            {
                if (pair.Value > 0)
                {
                    data.pendingRecords.Add(new WeaponPendingSaveRecord
                    {
                        weaponId = pair.Key,
                        pendingCount = pair.Value,
                    });
                }
            }

            data.fragmentRecords.Sort((left, right) => left.weaponId.CompareTo(right.weaponId));
            data.equipRecords.Sort((left, right) => left.slot.CompareTo(right.slot));
            data.pendingRecords.Sort((left, right) => left.weaponId.CompareTo(right.weaponId));
            return data;
        }

        private void EnsureBasicDefaults()
        {
            for (int i = 0; i < BasicDefaults.Length; i++)
            {
                (WeaponEquipSlot slot, int weaponId) = BasicDefaults[i];
                if (!_catalog.TryGet(weaponId, out WeaponDefinition definition)
                    || !definition.Enabled || definition.Slot != slot)
                {
                    throw new InvalidOperationException(
                        $"缺少基础武器 id={weaponId} 或配置与槽位 {slot} 不匹配");
                }

                if (GetFragments(weaponId) < definition.FragmentNum)
                {
                    _fragments[weaponId] = definition.FragmentNum;
                }

                if (!_equipped.TryGetValue(slot, out int equippedId)
                    || !IsValidEquippedWeapon(slot, equippedId))
                {
                    _equipped[slot] = weaponId;
                }
            }
        }

        private bool IsValidEquippedWeapon(WeaponEquipSlot slot, int weaponId)
        {
            return _catalog.TryGet(weaponId, out WeaponDefinition definition)
                   && definition.Enabled
                   && definition.Slot == slot
                   && GetFragments(weaponId) >= definition.FragmentNum;
        }

        private void NormalizePendingCounts()
        {
            var ids = new List<int>(_pending.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                int weaponId = ids[i];
                if (!_catalog.TryGet(weaponId, out WeaponDefinition definition))
                {
                    continue;
                }

                int completed = GetFragments(weaponId) / definition.FragmentNum;
                _pending[weaponId] = Math.Min(Math.Max(0, _pending[weaponId]), completed);
            }
        }

        private static void CopySaveRecords(
            WeaponProgressSaveData save,
            Dictionary<int, int> fragments,
            Dictionary<WeaponEquipSlot, int> equipped,
            Dictionary<int, int> pending)
        {
            if (save.fragmentRecords != null)
            {
                foreach (WeaponFragmentSaveRecord record in save.fragmentRecords)
                {
                    if (record != null && record.weaponId > 0 && record.totalFragments >= 0)
                    {
                        fragments[record.weaponId] = record.totalFragments;
                    }
                }
            }

            if (save.equipRecords != null)
            {
                foreach (WeaponEquipSaveRecord record in save.equipRecords)
                {
                    if (record != null && record.slot >= 0 && record.slot <= 3 && record.weaponId > 0)
                    {
                        equipped[(WeaponEquipSlot)record.slot] = record.weaponId;
                    }
                }
            }

            if (save.pendingRecords != null)
            {
                foreach (WeaponPendingSaveRecord record in save.pendingRecords)
                {
                    if (record != null && record.weaponId > 0 && record.pendingCount > 0)
                    {
                        pending[record.weaponId] = record.pendingCount;
                    }
                }
            }
        }

        private int GetFragments(int weaponId)
        {
            return _fragments.TryGetValue(weaponId, out int value) ? value : 0;
        }

        private int GetPending(int weaponId)
        {
            return _pending.TryGetValue(weaponId, out int value) ? value : 0;
        }

        private int GetEquipped(WeaponEquipSlot slot)
        {
            return _equipped.TryGetValue(slot, out int weaponId) ? weaponId : 0;
        }

        private bool IsWeaponEquipped(int weaponId)
        {
            foreach (int equippedId in _equipped.Values)
            {
                if (equippedId == weaponId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
