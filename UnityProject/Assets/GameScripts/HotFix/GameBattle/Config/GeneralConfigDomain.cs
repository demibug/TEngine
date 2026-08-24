using System;
using System.Collections.Generic;

namespace GameBattle
{
    public enum GeneralCombatArchetype
    {
        Pike = 0,
        Bow = 1,
    }

    public sealed class GeneralPartRecruitEntry
    {
        public string PartText { get; }
        public int Weight { get; }

        public GeneralPartRecruitEntry(string partText, int weight)
        {
            PartText = partText ?? throw new ArgumentNullException(nameof(partText));
            Weight = weight;
        }
    }

    public sealed class GeneralConfigSnapshot
    {
        private readonly string[] _partWords;

        public int Index { get; }
        public string Name { get; }
        public string Family { get; }
        public IReadOnlyList<string> PartWords => _partWords;
        public GeneralCombatArchetype CombatArchetype { get; }
        public float RangeCells { get; }
        public int AttackDamage { get; }
        public float AttackIntervalSeconds { get; }
        public string DamageMode { get; }
        public string TargetPolicy { get; }
        public string PrefabAddress { get; }
        public string AnimationKey { get; }
        public string ProjectileType { get; }
        public int ProjectileSpeed { get; }
        public int PartRecruitWeight { get; }
        public int? SkillId { get; }
        public string RecipeKey => GeneralRecipeKey.Create(_partWords[0], _partWords[1]);
        internal SoldierType LogicalSoldierType => CombatArchetype == GeneralCombatArchetype.Bow
            ? SoldierType.Bow
            : SoldierType.Spear;
        public string LogicalSoldierText => CombatArchetype == GeneralCombatArchetype.Bow ? "弓" : "枪";

        public GeneralConfigSnapshot(
            int index,
            string name,
            string family,
            IReadOnlyList<string> partWords,
            GeneralCombatArchetype combatArchetype,
            float rangeCells,
            int attackDamage,
            float attackIntervalSeconds,
            string damageMode,
            string targetPolicy,
            string prefabAddress,
            string animationKey,
            string projectileType,
            int projectileSpeed,
            int partRecruitWeight,
            int? skillId = null)
        {
            Index = index;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Family = family ?? throw new ArgumentNullException(nameof(family));
            if (partWords == null)
            {
                throw new ArgumentNullException(nameof(partWords));
            }

            _partWords = new string[partWords.Count];
            for (int i = 0; i < partWords.Count; i++)
            {
                _partWords[i] = partWords[i];
            }

            CombatArchetype = combatArchetype;
            RangeCells = rangeCells;
            AttackDamage = attackDamage;
            AttackIntervalSeconds = attackIntervalSeconds;
            DamageMode = damageMode ?? string.Empty;
            TargetPolicy = targetPolicy ?? string.Empty;
            PrefabAddress = prefabAddress ?? string.Empty;
            AnimationKey = animationKey ?? string.Empty;
            ProjectileType = projectileType ?? string.Empty;
            ProjectileSpeed = projectileSpeed;
            PartRecruitWeight = partRecruitWeight;
            SkillId = skillId;
        }

        internal UnitConfigSnapshot ToUnitConfigSnapshot()
            => new UnitConfigSnapshot(
                Index,
                LogicalSoldierText,
                AnimationKey,
                RangeCells,
                AttackDamage,
                AttackIntervalSeconds,
                DamageMode,
                TargetPolicy,
                ProjectileType,
                ProjectileSpeed,
                PrefabAddress,
                SkillId);
    }

    public sealed class GeneralCatalogSnapshot
    {
        private readonly List<GeneralConfigSnapshot> _definitions;
        private readonly Dictionary<int, GeneralConfigSnapshot> _byIndex;
        private readonly Dictionary<string, GeneralConfigSnapshot> _byRecipe;
        private readonly List<GeneralPartRecruitEntry> _partRecruitEntries;

        public IReadOnlyList<GeneralConfigSnapshot> Definitions => _definitions;
        public IReadOnlyList<GeneralPartRecruitEntry> PartRecruitEntries => _partRecruitEntries;

        public GeneralCatalogSnapshot(IReadOnlyList<GeneralConfigSnapshot> definitions)
        {
            _definitions = definitions != null
                ? new List<GeneralConfigSnapshot>(definitions)
                : new List<GeneralConfigSnapshot>();
            _byIndex = new Dictionary<int, GeneralConfigSnapshot>();
            _byRecipe = new Dictionary<string, GeneralConfigSnapshot>(StringComparer.Ordinal);
            _partRecruitEntries = new List<GeneralPartRecruitEntry>();

            for (int i = 0; i < _definitions.Count; i++)
            {
                GeneralConfigSnapshot definition = _definitions[i];
                if (definition == null)
                {
                    continue;
                }

                if (!_byIndex.ContainsKey(definition.Index))
                {
                    _byIndex.Add(definition.Index, definition);
                }

                if (definition.PartWords.Count == 2)
                {
                    string key = GeneralRecipeKey.Create(definition.PartWords[0], definition.PartWords[1]);
                    if (!_byRecipe.ContainsKey(key))
                    {
                        _byRecipe.Add(key, definition);
                    }

                    _partRecruitEntries.Add(new GeneralPartRecruitEntry(
                        definition.PartWords[0], definition.PartRecruitWeight));
                    _partRecruitEntries.Add(new GeneralPartRecruitEntry(
                        definition.PartWords[1], definition.PartRecruitWeight));
                }
            }
        }

        public GeneralConfigSnapshot GetByIndexOrDefault(int index)
            => _byIndex.TryGetValue(index, out GeneralConfigSnapshot value) ? value : null;

        /// <summary>
        /// 按有序配方查询武将：firstPart 必须是左字、secondPart 必须是右字。
        /// 反序（如"飞","张"）不命中任何配方，返回 null。
        /// </summary>
        public GeneralConfigSnapshot GetByRecipeOrDefault(string firstPart, string secondPart)
            => _byRecipe.TryGetValue(GeneralRecipeKey.Create(firstPart, secondPart), out GeneralConfigSnapshot value)
                ? value
                : null;
    }

    /// <summary>
    /// 武将配方 key：保持传入顺序（左字 + 分隔符 + 右字），不做排序。
    /// 语义对齐原始工程 findGeneralByParts：parts 按传入顺序 join，只有
    /// "张","飞" 命中张飞；反序 "飞","张" 对应 "飞张"，不命中任何武将。
    /// </summary>
    public static class GeneralRecipeKey
    {
        public static string Create(string firstPart, string secondPart)
        {
            string first = firstPart ?? string.Empty;
            string second = secondPart ?? string.Empty;
            return first + "\u001f" + second;
        }
    }
}
