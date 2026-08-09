using System;

namespace GameBattle
{
    // ============================================================================
    // 等级领域：UnitLevelService —— 校验最大等级、解析倍率、统一应用等级数值
    // ----------------------------------------------------------------------------
    // 职责（最终方案"计划新增文件"Unit/UnitLevelService.cs）：
    //   1. 校验最大等级（合并上限）。
    //   2. 从 UnitLevelConfigSnapshot 解析伤害及攻速倍率。
    //   3. 统一应用等级数值。
    //
    // 数值公式（最终方案"战斗接入与等级"SoldierBase.ApplyLevel）：
    //   damage   = baseDamage × DamageMultiplier[level-1]
    //   interval = baseInterval ÷ AttackSpeedMultiplier[level-1]
    //   C# 整数伤害统一采用四舍五入（MidpointRounding.AwayFromZero），
    //   避免各兵种自行转换。
    //
    // 与配置的关系：
    //   UnitLevelConfigSnapshot（BattleConfigSnapshot.UnitLevel）包含 MaxLevel /
    //   DamageLevelMultipliers / AttackSpeedLevelMultipliers。若配置缺失或数组为空，
    //   回退到 RecruitDefinitions.MaxLevel 与默认倍率 1.0。
    //
    // 本类型为无状态服务：不持有可变状态，可跨局复用（由 Factory 构造单实例）。
    // ============================================================================

    /// <summary>
    /// 等级数值服务：校验最大等级、解析伤害/攻速倍率、统一应用等级数值。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（最终方案）：</b>校验最大等级、从 <see cref="UnitLevelConfigSnapshot"/>
    /// 解析伤害及攻速倍率、统一应用等级数值。</para>
    /// <para><b>无状态：</b>本类型不持有可变状态，注入的等级配置只读，可跨局复用。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 UnitSlotBoard /
    /// SoldierBase / BattleInputController 使用。</para>
    /// </remarks>
    internal sealed class UnitLevelService
    {
        // ====================================================================
        // 注入配置（只读）
        // ====================================================================

        /// <summary>单位等级配置（非 null，由 Factory 注入 BattleConfigSnapshot.UnitLevel）。</summary>
        private readonly UnitLevelConfigSnapshot _config;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造等级数值服务。
        /// </summary>
        /// <param name="config">单位等级配置快照（可为 null，回退 RecruitDefinitions 默认值）。</param>
        internal UnitLevelService(UnitLevelConfigSnapshot config)
        {
            _config = config;
        }

        // ====================================================================
        // 最大等级
        // ====================================================================

        /// <summary>
        /// 当前最大等级（合并上限）。
        /// </summary>
        internal int MaxLevel
        {
            get
            {
                if (_config != null && _config.MaxLevel > 0)
                {
                    return _config.MaxLevel;
                }

                return RecruitDefinitions.MaxLevel;
            }
        }

        /// <summary>
        /// 校验指定等级是否达到最大等级。
        /// </summary>
        /// <param name="level">当前等级。</param>
        /// <returns>true 表示已达到最大等级，不可继续合并。</returns>
        internal bool IsMaxLevel(int level)
        {
            return level >= MaxLevel;
        }

        // ====================================================================
        // 倍率解析
        // ====================================================================

        /// <summary>
        /// 获取指定等级的伤害倍率（索引 = level - 1）。
        /// </summary>
        /// <param name="level">等级（1..MaxLevel）。</param>
        /// <returns>伤害倍率；配置缺失或越界时回退 1f。</returns>
        internal float GetDamageMultiplier(int level)
        {
            if (_config?.DamageLevelMultipliers != null)
            {
                int index = level - 1;
                if (index >= 0 && index < _config.DamageLevelMultipliers.Count)
                {
                    float value = _config.DamageLevelMultipliers[index];
                    return value > 0f ? value : 1f;
                }
            }

            return 1f;
        }

        /// <summary>
        /// 获取指定等级的攻速倍率（索引 = level - 1）。
        /// </summary>
        /// <param name="level">等级（1..MaxLevel）。</param>
        /// <returns>攻速倍率；配置缺失或越界时回退 1f。</returns>
        internal float GetAttackSpeedMultiplier(int level)
        {
            if (_config?.AttackSpeedLevelMultipliers != null)
            {
                int index = level - 1;
                if (index >= 0 && index < _config.AttackSpeedLevelMultipliers.Count)
                {
                    float value = _config.AttackSpeedLevelMultipliers[index];
                    return value > 0f ? value : 1f;
                }
            }

            return 1f;
        }

        // ====================================================================
        // 统一应用等级数值
        // ====================================================================

        /// <summary>
        /// 计算指定等级的最终攻击伤害（整数四舍五入）。
        /// </summary>
        /// <param name="baseDamage">1 级基础攻击力。</param>
        /// <param name="level">当前等级（1..MaxLevel）。</param>
        /// <returns>四舍五入后的伤害值。</returns>
        /// <remarks>
        /// <para>公式：<c>damage = baseDamage × DamageMultiplier[level-1]</c>。</para>
        /// <para>C# 整数伤害统一采用四舍五入（<see cref="MidpointRounding.AwayFromZero"/>），
        /// 避免各兵种自行转换（最终方案）。</para>
        /// </remarks>
        internal int ResolveDamage(int baseDamage, int level)
        {
            float multiplier = GetDamageMultiplier(level);
            float raw = baseDamage * multiplier;
            return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 计算指定等级的最终攻击间隔（秒）。
        /// </summary>
        /// <param name="baseIntervalSeconds">1 级基础攻击间隔（秒）。</param>
        /// <param name="level">当前等级（1..MaxLevel）。</param>
        /// <returns>最终攻击间隔（秒）。</returns>
        /// <remarks>
        /// 公式：<c>interval = baseInterval ÷ AttackSpeedMultiplier[level-1]</c>。
        /// </remarks>
        internal float ResolveAttackInterval(float baseIntervalSeconds, int level)
        {
            float multiplier = GetAttackSpeedMultiplier(level);
            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            return baseIntervalSeconds / multiplier;
        }
    }
}
