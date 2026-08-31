using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameCommon.Battle;
using GameConfig;
using GameConfig.battle;
using LubanWeaponRewardRow = GameConfig.battle.WeaponReward;
using LubanWeaponRow = GameConfig.battle.Weapon;

namespace GameBattle.Weapon
{
    /// <summary>局外武器装备槽位。</summary>
    public enum WeaponEquipSlot
    {
        Bow = 0,
        Spear = 1,
        Knife = 2,
        Sword = 3,
    }

    /// <summary>战斗运行时使用的武器类别。</summary>
    public enum WeaponType
    {
        Bow = 0,
        Spear = 1,
        Knife = 2,
        Sword = 3,
    }

    /// <summary>局外武器配置定义。</summary>
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

        internal WeaponDefinition(LubanWeaponRow row)
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
            int id,
            WeaponEquipSlot slot,
            int fragmentNum,
            bool enabled = true,
            bool obtainable = true,
            bool recyclable = true,
            int recycleGoldPerFragment = 1)
        {
            Id = id;
            Slot = slot;
            AddAttackPower = 1;
            Enabled = enabled;
            HandlerKey = "Basic";
            Rarity = 0;
            FragmentNum = fragmentNum;
            Obtainable = obtainable;
            DisplayName = string.Empty;
            Description = string.Empty;
            IconLocation = string.Empty;
            Recyclable = recyclable;
            RecycleGoldPerFragment = recycleGoldPerFragment;
        }
    }

    /// <summary>战斗奖励表中的武器候选定义。</summary>
    public sealed class WeaponRewardDefinition
    {
        public int Id { get; }
        public int MapId { get; }
        public int WeaponId { get; }
        public bool WinOnly { get; }
        public int Weight { get; }
        public int MinFragments { get; }
        public int MaxFragments { get; }

        internal WeaponRewardDefinition(LubanWeaponRewardRow row)
        {
            Id = row.Id;
            MapId = row.MapId;
            WeaponId = row.WeaponId;
            WinOnly = row.WinOnly;
            Weight = row.Weight;
            MinFragments = row.MinFragments;
            MaxFragments = row.MaxFragments;
        }

        internal WeaponRewardDefinition(
            int id,
            int mapId,
            int weaponId,
            bool winOnly,
            int weight,
            int minFragments,
            int maxFragments)
        {
            Id = id;
            MapId = mapId;
            WeaponId = weaponId;
            WinOnly = winOnly;
            Weight = weight;
            MinFragments = minFragments;
            MaxFragments = maxFragments;
        }
    }

    /// <summary>
    /// 局外武器目录。它只负责复制并校验 Luban 配置，不持有玩家进度。
    /// </summary>
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

        internal static WeaponCatalog FromTables(Tables tables)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            var definitions = new List<WeaponDefinition>();
            var byId = new Dictionary<int, WeaponDefinition>();
            foreach (LubanWeaponRow row in tables.TbWeapon.DataList)
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

            definitions.Sort((left, right) => left.Id.CompareTo(right.Id));

            var rewards = new List<WeaponRewardDefinition>();
            foreach (LubanWeaponRewardRow row in tables.TbWeaponReward.DataList)
            {
                ValidateRewardRow(row, byId);
                rewards.Add(new WeaponRewardDefinition(row));
            }

            rewards.Sort((left, right) => left.Id.CompareTo(right.Id));
            return new WeaponCatalog(
                Array.AsReadOnly(definitions.ToArray()),
                new ReadOnlyDictionary<int, WeaponDefinition>(byId),
                Array.AsReadOnly(rewards.ToArray()));
        }

        internal static WeaponCatalog CreateForTests(IReadOnlyList<WeaponDefinition> definitions)
        {
            return CreateForTests(definitions, Array.Empty<WeaponRewardDefinition>());
        }

        internal static WeaponCatalog CreateForTests(
            IReadOnlyList<WeaponDefinition> definitions,
            IReadOnlyList<WeaponRewardDefinition> rewards)
        {
            var ordered = new List<WeaponDefinition>(definitions ?? Array.Empty<WeaponDefinition>());
            ordered.Sort((left, right) => left.Id.CompareTo(right.Id));
            var byId = new Dictionary<int, WeaponDefinition>();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (byId.ContainsKey(ordered[i].Id))
                {
                    throw new ArgumentException($"武器配置存在重复 id={ordered[i].Id}");
                }

                byId.Add(ordered[i].Id, ordered[i]);
            }

            var orderedRewards = new List<WeaponRewardDefinition>(
                rewards ?? Array.Empty<WeaponRewardDefinition>());
            orderedRewards.Sort((left, right) => left.Id.CompareTo(right.Id));

            return new WeaponCatalog(
                Array.AsReadOnly(ordered.ToArray()),
                new ReadOnlyDictionary<int, WeaponDefinition>(byId),
                Array.AsReadOnly(orderedRewards.ToArray()));
        }

        private static void ValidateWeaponRow(LubanWeaponRow row)
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
            LubanWeaponRewardRow row,
            IReadOnlyDictionary<int, WeaponDefinition> byId)
        {
            if (row == null || row.Id <= 0 || row.MapId < 0 || row.Weight <= 0
                || row.MinFragments <= 0 || row.MaxFragments < row.MinFragments)
            {
                throw new InvalidOperationException($"武器奖励配置 id={row?.Id ?? 0} 非法");
            }

            if (!byId.TryGetValue(row.WeaponId, out WeaponDefinition weapon)
                || !weapon.Obtainable)
            {
                throw new InvalidOperationException(
                    $"武器奖励 id={row.Id} 引用了不存在或不可掉落的 weaponId={row.WeaponId}");
            }
        }
    }

    /// <summary>战斗配置快照中的轻量武器定义。</summary>
    public sealed class WeaponDefinitionSnapshot
    {
        public int Id { get; }
        public WeaponType Type { get; }
        public int AddAttackPower { get; }
        public bool Enabled { get; }
        public string HandlerKey { get; }

        public WeaponDefinitionSnapshot(
            int id,
            WeaponType type,
            int addAttackPower,
            bool enabled,
            string handlerKey)
        {
            Id = id;
            Type = type;
            AddAttackPower = addAttackPower;
            Enabled = enabled;
            HandlerKey = handlerKey ?? string.Empty;
        }

        public override string ToString()
        {
            return $"WeaponDefinition(id={Id}, type={Type}, addAttackPower={AddAttackPower}, " +
                   $"enabled={Enabled}, handlerKey='{HandlerKey}')";
        }
    }

    /// <summary>战斗配置使用的不可变武器快照目录。</summary>
    public sealed class WeaponCatalogSnapshot
    {
        public IReadOnlyList<WeaponDefinitionSnapshot> Definitions { get; }

        private readonly IReadOnlyDictionary<int, WeaponDefinitionSnapshot> _byId;

        public WeaponCatalogSnapshot(IReadOnlyList<WeaponDefinitionSnapshot> definitions)
        {
            IReadOnlyList<WeaponDefinitionSnapshot> source =
                definitions ?? Array.Empty<WeaponDefinitionSnapshot>();
            var copy = new WeaponDefinitionSnapshot[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy, (left, right) => left.Id.CompareTo(right.Id));

            var byId = new Dictionary<int, WeaponDefinitionSnapshot>(copy.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                WeaponDefinitionSnapshot definition = copy[i];
                if (byId.ContainsKey(definition.Id))
                {
                    throw new ArgumentException($"武器目录存在重复 id={definition.Id}");
                }

                byId.Add(definition.Id, definition);
            }

            Definitions = Array.AsReadOnly(copy);
            _byId = new ReadOnlyDictionary<int, WeaponDefinitionSnapshot>(byId);
        }

        public bool TryGetById(int id, out WeaponDefinitionSnapshot definition)
        {
            return _byId.TryGetValue(id, out definition);
        }

        public bool ContainsId(int id)
        {
            return _byId.ContainsKey(id);
        }
    }

    /// <summary>
    /// 将本局四槽武器装载显式映射到玩家兵种的运行时 resolver。
    /// </summary>
    internal class BasicWeaponResolver
    {
        internal const string BasicHandlerKey = "Basic";
        internal const int BasicAttackPower = 1;

        internal const int BowWeaponId = 1;
        internal const int SpearWeaponId = 11;
        internal const int KnifeWeaponId = 21;
        internal const int CavalryWeaponId = 32;

        private readonly IReadOnlyDictionary<SoldierType, WeaponDefinitionSnapshot> _bySoldierType;

        internal BasicWeaponResolver(WeaponCatalogSnapshot catalog)
            : this(catalog, BattleWeaponLoadoutDto.CreateBasicDefault())
        {
        }

        internal BasicWeaponResolver(
            WeaponCatalogSnapshot catalog,
            BattleWeaponLoadoutDto loadout)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var enabledById = new Dictionary<int, WeaponDefinitionSnapshot>();
            IReadOnlyList<WeaponDefinitionSnapshot> definitions = catalog.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                WeaponDefinitionSnapshot definition = definitions[i];
                if (definition.Enabled)
                {
                    enabledById[definition.Id] = definition;
                }
            }

            if (enabledById.Count != 4)
            {
                throw new InvalidOperationException(
                    $"基础武器启用行数={enabledById.Count} 不等于 4");
            }

            var selected = new[]
            {
                (loadout.BowWeaponId, SoldierType.Bow, WeaponType.Bow),
                (loadout.SpearWeaponId, SoldierType.Spear, WeaponType.Spear),
                (loadout.KnifeWeaponId, SoldierType.Knife, WeaponType.Knife),
                (loadout.SwordWeaponId, SoldierType.Cavalry, WeaponType.Sword),
            };
            var mapping = new Dictionary<SoldierType, WeaponDefinitionSnapshot>(selected.Length);
            for (int i = 0; i < selected.Length; i++)
            {
                (int id, SoldierType soldierType, WeaponType weaponType) = selected[i];
                if (!enabledById.TryGetValue(id, out WeaponDefinitionSnapshot definition))
                {
                    throw new InvalidOperationException(
                        $"基础武器目录缺少启用行 id={id}（SoldierType={soldierType}）");
                }

                if (definition.Type != weaponType)
                {
                    throw new InvalidOperationException(
                        $"id={id} 的武器类别={definition.Type} 与期望 {weaponType} 不一致");
                }

                if (!string.Equals(definition.HandlerKey, BasicHandlerKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"id={id} 的 handlerKey='{definition.HandlerKey}' 不是 '{BasicHandlerKey}'");
                }

                if (definition.AddAttackPower != BasicAttackPower)
                {
                    throw new InvalidOperationException(
                        $"id={id} 的附加攻击力={definition.AddAttackPower} 不等于 {BasicAttackPower}");
                }

                mapping.Add(soldierType, definition);
            }

            _bySoldierType = new ReadOnlyDictionary<SoldierType, WeaponDefinitionSnapshot>(mapping);
        }

        internal virtual bool TryResolve(
            SoldierType type,
            out WeaponDefinitionSnapshot definition)
        {
            return _bySoldierType.TryGetValue(type, out definition);
        }
    }
}
