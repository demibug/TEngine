using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameConfig;
using GameConfig.battle;

namespace GameWeapon
{
    public enum WeaponEquipSlot
    {
        Bow = 0,
        Spear = 1,
        Knife = 2,
        Sword = 3,
    }

    public sealed class WeaponDefinition
    {
        public int Id { get; }
        public WeaponEquipSlot Slot { get; }
        public int AddAttackPower { get; }
        public bool Enabled { get; }
        public string HandlerKey { get; }
        public int Rarity { get; }
        public int FragmentNum { get; }
        public bool Obtainable { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string IconLocation { get; }
        public bool Recyclable { get; }
        public int RecycleGoldPerFragment { get; }

        internal WeaponDefinition(Weapon row)
        {
            Id = row.Id;
            Slot = (WeaponEquipSlot)row.Type;
            AddAttackPower = row.AddAttPower;
            Enabled = row.Enabled;
            HandlerKey = row.HandlerKey ?? string.Empty;
            Rarity = row.Rarity;
            FragmentNum = row.FragmentNum;
            Obtainable = row.Obtainable;
            DisplayName = row.DisplayName ?? string.Empty;
            Description = row.Description ?? string.Empty;
            IconLocation = row.IconLocation ?? string.Empty;
            Recyclable = row.Recyclable;
            RecycleGoldPerFragment = row.RecycleGoldPerFragment;
        }

        internal WeaponDefinition(
            int id, WeaponEquipSlot slot, int fragmentNum, bool enabled = true,
            bool obtainable = true, bool recyclable = true, int recycleGoldPerFragment = 1)
        {
            Id = id;
            Slot = slot;
            FragmentNum = fragmentNum;
            Enabled = enabled;
            Obtainable = obtainable;
            Recyclable = recyclable;
            RecycleGoldPerFragment = recycleGoldPerFragment;
            HandlerKey = "Basic";
            DisplayName = string.Empty;
            Description = string.Empty;
            IconLocation = string.Empty;
        }
    }

    public sealed class WeaponRewardDefinition
    {
        public int Id { get; }
        public int MapId { get; }
        public int WeaponId { get; }
        public bool WinOnly { get; }
        public int Weight { get; }
        public int MinFragments { get; }
        public int MaxFragments { get; }

        internal WeaponRewardDefinition(WeaponReward row)
        {
            Id = row.Id;
            MapId = row.MapId;
            WeaponId = row.WeaponId;
            WinOnly = row.WinOnly;
            Weight = row.Weight;
            MinFragments = row.MinFragments;
            MaxFragments = row.MaxFragments;
        }
    }

    public sealed class WeaponCatalog
    {
        private readonly IReadOnlyDictionary<int, WeaponDefinition> _byId;
        private readonly IReadOnlyList<WeaponRewardDefinition> _rewards;

        public IReadOnlyList<WeaponDefinition> Definitions { get; }
        public IReadOnlyList<WeaponRewardDefinition> Rewards => _rewards;

        private WeaponCatalog(
            IReadOnlyList<WeaponDefinition> definitions,
            IReadOnlyDictionary<int, WeaponDefinition> byId,
            IReadOnlyList<WeaponRewardDefinition> rewards)
        {
            Definitions = definitions;
            _byId = byId;
            _rewards = rewards;
        }

        public bool TryGet(int weaponId, out WeaponDefinition definition)
        {
            return _byId.TryGetValue(weaponId, out definition);
        }

        public static WeaponCatalog FromTables(Tables tables)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            var definitions = new List<WeaponDefinition>();
            var byId = new Dictionary<int, WeaponDefinition>();
            foreach (Weapon row in tables.TbWeapon.DataList)
            {
                ValidateWeaponRow(row);
                var definition = new WeaponDefinition(row);
                if (byId.ContainsKey(definition.Id))
                {
                    throw new InvalidOperationException($"武器配置存在重复 id={definition.Id}");
                }

                byId.Add(definition.Id, definition);

                definitions.Add(definition);
            }

            definitions.Sort((a, b) => a.Id.CompareTo(b.Id));

            var rewards = new List<WeaponRewardDefinition>();
            foreach (WeaponReward row in tables.TbWeaponReward.DataList)
            {
                ValidateRewardRow(row, byId);
                rewards.Add(new WeaponRewardDefinition(row));
            }

            rewards.Sort((a, b) => a.Id.CompareTo(b.Id));
            return new WeaponCatalog(
                Array.AsReadOnly(definitions.ToArray()),
                new ReadOnlyDictionary<int, WeaponDefinition>(byId),
                Array.AsReadOnly(rewards.ToArray()));
        }

        internal static WeaponCatalog CreateForTests(IReadOnlyList<WeaponDefinition> definitions)
        {
            var ordered = new List<WeaponDefinition>(definitions);
            ordered.Sort((a, b) => a.Id.CompareTo(b.Id));
            var byId = new Dictionary<int, WeaponDefinition>();
            for (int i = 0; i < ordered.Count; i++)
            {
                byId.Add(ordered[i].Id, ordered[i]);
            }

            return new WeaponCatalog(
                Array.AsReadOnly(ordered.ToArray()),
                new ReadOnlyDictionary<int, WeaponDefinition>(byId),
                Array.AsReadOnly(Array.Empty<WeaponRewardDefinition>()));
        }

        private static void ValidateWeaponRow(Weapon row)
        {
            if (row == null || row.Id <= 0)
            {
                throw new InvalidOperationException("武器配置 id 必须大于 0");
            }

            if (row.Type < 0 || row.Type > 3)
            {
                throw new InvalidOperationException($"武器 id={row.Id} type={row.Type} 越界");
            }

            if (row.Rarity < 0 || row.Rarity > 4 || row.FragmentNum <= 0)
            {
                throw new InvalidOperationException(
                    $"武器 id={row.Id} rarity={row.Rarity} 或 fragmentNum={row.FragmentNum} 非法");
            }

            if (row.RecycleGoldPerFragment < 0)
            {
                throw new InvalidOperationException($"武器 id={row.Id} 回收金币不能为负");
            }
        }

        private static void ValidateRewardRow(
            WeaponReward row,
            IReadOnlyDictionary<int, WeaponDefinition> byId)
        {
            if (row == null || row.Id <= 0 || row.MapId < 0 || row.Weight <= 0
                || row.MinFragments <= 0 || row.MaxFragments < row.MinFragments)
            {
                throw new InvalidOperationException($"武器奖励配置 id={row?.Id ?? 0} 非法");
            }

            if (!byId.TryGetValue(row.WeaponId, out WeaponDefinition weapon) || !weapon.Obtainable)
            {
                throw new InvalidOperationException(
                    $"武器奖励 id={row.Id} 引用了不存在或不可掉落的 weaponId={row.WeaponId}");
            }
        }
    }
}
