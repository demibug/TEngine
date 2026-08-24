using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.5：BattleConfigValidator —— 配置快照校验器
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节 / decision 6 / specs/battle-config-snapshot/spec.md
    //   "Invalid configuration blocks battle entry"）：
    //   在创建运行时前验证配置快照的坐标、路径、权重、引用和数值范围，
    //   返回结构化校验结果（BattleConfigValidationResult），阻止非法配置进入运行状态。
    //
    //   spec "Invalid configuration blocks battle entry"：
    //   缺失表、非法权重、错误地图尺寸、未知单位或不完整路径 MUST 返回可诊断错误，
    //   并阻止运行时进入运行状态。
    //
    //   决策 0.7：预期失败返回结构化结果而非异常。本类型为预期配置校验失败的唯一产生点。
    //
    // 校验覆盖（task 3.5 / 本 change 起的新权威）：
    //   1. 配置版本（版本字段在 BattleLoadoutDto.ConfigVersion，权威校验在
    //      BattleRuntimeFactory.TryValidateLoadout loadout 层；本校验点在快照层做
    //      SourceTag 防御性复查，空 SourceTag → InvalidVersion）
    //   2. 缺表（Map/Units/UnitLevel/Economy/Deck/Projectile/EnemyCatalog/OrderedWavePlan/
    //      BuffCatalog 任一为 null → MissingSection；legacy Enemy/Wave 快照字段不再参与校验）
    //   3. 缺字段（必需集合为空或 null → MissingField；注意 BattleConfigSnapshot 构造已拒 null，
    //      此处主要校验空集合与业务必需字段）
    //   4. 未知兵种（单位索引不在 0..3 或文本不在四兵集合 → UnknownUnit）
    //   5. 非法时间/距离（单位攻击间隔为负、攻击距离非正 → InvalidTime/InvalidDistance；
    //      legacy 波间延迟/敌人速度/接触伤害/Boss 概率不再校验）
    //   6. 地图尺寸（Width/Height 非正 → InvalidMapSize；MapData 构造已保证，此处复查）
    //   7. 越界玩家/对手路径（路径点不在地图范围内 → PathOutOfBounds）
    //   8. 缺失引用（牌组 BaseSoldierTexts 引用了未定义的兵种文本 → MissingReference）
    //   9. 敌人目录（EnemyCatalog 结构/数值 → EnemyCatalogInvalid/MissingField）
    //  10. 有序波次计划（order 连续/类型/时序/车道/数量/enemyKey/难度/profile/bossKey →
    //      WavePlan* / DifficultyIndexInvalid / StrategyProfileInvalid 等）
    //  11. 运行能力（所选计划含 Boss 行且能力不支持 → BossCapabilityUnsupported）
    //
    // 本 change 起，legacy 的 snapshot.Enemy / snapshot.Wave（含 waveUnitCounts、Boss 数组、
    // spawnStrategyWeights、skipBoss、MaxRounds、DelayTimeMs 与单敌人速度/接触伤害/血量/类型）
    // 不再作为生产权威被本校验器读取：它们为数据/回滚兼容保留，但不得参与启动门禁
    // （design.md：旧字段 MAY 为数据/回滚兼容保留）。
    //
    // route marker 表现边界（task 3.5 特别要求）：
    //   还原工程中 route marker（路径标记点）可能同时用于表现层动画与游戏逻辑路径。
    //   本校验器只校验"游戏路径"——即 MapData.GetPlayerPath()/GetOpponentPath() 返回的路径点。
    //   若路径点坐标出现在阻挡格(Kind=Blocked)或不可行走格上，视为 route marker 误用：
    //   表现层 marker 不应作为游戏路径点传入。本类型以 RouteMarkerMismatch 类别报告此类问题，
    //   不按 PathOutOfBounds 误判，也不按 InvalidMapSize 误判。
    //
    // 不变量：
    //   1. 校验只读不修改快照。
    //   2. 返回结构化结果，不抛异常（决策 0.7）。
    //   3. 多个错误一次性收集，便于上游诊断。
    //   4. 错误码映射由 BattleConfigValidationResult 承担，本类型只产生错误项。
    // ============================================================================

    /// <summary>
    /// 配置快照校验器，在创建运行时前验证配置合法性与完整性（task 3.5）。
    /// </summary>
    /// <remarks>
    /// <para><b>spec "Invalid configuration blocks battle entry"：</b>
    /// 缺失表、非法结构、错误地图尺寸、未知单位或不完整路径 MUST 返回可诊断错误，
    /// 并阻止运行时进入运行状态。本类型返回 <see cref="BattleConfigValidationResult"/>，
    /// 由 <see cref="BattleRuntimeFactory"/> 据错误码决定是否中止组装。</para>
    ///
    /// <para><b>决策 0.7 结构化结果：</b>
    /// 预期配置错误不抛异常，收集为错误项列表，调用方基于稳定错误码做程序化判断。</para>
    ///
    /// <para><b>校验只读：</b>本类型不修改传入的快照，每次 Validate 产生独立结果。</para>
    ///
    /// <para><b>route marker 边界：</b>本校验器只校验游戏路径（MapData.GetPlayerPath/
    /// GetOpponentPath）。若路径点落在阻挡格或不可行走格上，以 RouteMarkerMismatch 类别报告，
    /// 不与 PathOutOfBounds 或 InvalidMapSize 混淆。表现层 marker 不应作为游戏路径点传入。</para>
    /// </remarks>
    internal static class BattleConfigValidator
    {
        // ====================================================================
        // 本期合法兵种集合（design.md decision：本期只覆盖刀/弓/枪/骑）
        // ====================================================================

        /// <summary>
        /// 本期合法兵种文本集合（刀/弓/枪/骑）。
        /// 来源：DeckDefinitions.js BASE_POOL / golden-battle-bundle.json deck.baseSoldierTexts。
        /// </summary>
        private static readonly HashSet<string> ValidSoldierTexts = new HashSet<string>
        {
            "刀", "弓", "枪", "骑",
        };

        /// <summary>
        /// 本期合法单位索引集合（0=刀, 1=弓, 2=枪, 3=骑）。
        /// </summary>
        private static readonly HashSet<int> ValidUnitIndices = new HashSet<int>
        {
            0, 1, 2, 3,
        };

        // ====================================================================
        // 公共入口
        // ====================================================================

        /// <summary>
        /// 校验配置快照（结构 + 交叉引用 + 默认运行时能力），返回结构化结果。
        /// </summary>
        /// <param name="snapshot">待校验的不可变配置快照。</param>
        /// <returns>结构化校验结果。IsValid 为 true 表示通过；否则 Errors 含具体错误项。</returns>
        /// <remarks>
        /// <para>等价于 <see cref="Validate(BattleConfigSnapshot, BattleRuntimeCapabilities)"/>
        /// 使用显式无 Boss 能力 <see cref="BattleRuntimeCapabilities.None"/>；生产启动入口会传入
        /// <see cref="BattleRuntimeCapabilities.Production"/>。</para>
        /// <para>本方法只读不修改快照，不抛异常（决策 0.7）。所有错误一次性收集。</para>
        /// <para>调用方（<see cref="BattleRuntimeFactory"/>）据 <see cref="BattleConfigValidationResult.ErrorCode"/>
        /// 决定是否中止组装：非 None 即返回对应错误码，不进入运行状态。</para>
        /// </remarks>
        internal static BattleConfigValidationResult Validate(BattleConfigSnapshot snapshot)
        {
            return Validate(snapshot, BattleRuntimeCapabilities.None);
        }

        /// <summary>
        /// 校验配置快照，返回结构化结果（design.md 决策 3 三层校验：
        /// 结构校验、交叉引用校验、运行能力校验）。
        /// </summary>
        /// <param name="snapshot">待校验的不可变配置快照。</param>
        /// <param name="capabilities">启动事务声明的运行时能力（Boss 波 gate 消费）。</param>
        /// <returns>结构化校验结果。IsValid 为 true 表示通过；否则 Errors 含具体错误项。</returns>
        /// <remarks>
        /// <para>spec "Invalid configuration blocks battle entry"：缺失表、非法结构、错误地图尺寸、
        /// 未知单位或不完整路径 MUST 返回可诊断错误，并阻止运行时进入运行状态。</para>
        /// <para><b>能力校验（spec "Reject Boss content when the capability is absent"）：</b>
        /// 所选计划含 Boss 行而 <paramref name="capabilities"/> 未声明支持时返回
        /// <see cref="BattleConfigErrorCategory.BossCapabilityUnsupported"/>，不静默跳过。</para>
        /// <para>本方法只读不修改快照，不抛异常（决策 0.7）。所有错误一次性收集。</para>
        /// <para>调用方据 <see cref="BattleConfigValidationResult.ErrorCode"/> 决定是否中止：
        /// 非 None 即返回对应错误码，不进入运行状态。</para>
        /// </remarks>
        internal static BattleConfigValidationResult Validate(
            BattleConfigSnapshot snapshot,
            BattleRuntimeCapabilities capabilities)
        {
            if (snapshot == null)
            {
                // 快照本身为 null：直接返回缺表错误（不应发生，Factory 已检查，但防御性处理）。
                var nullErrors = new List<BattleConfigValidationError>
                {
                    new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingSection,
                        "配置快照为 null",
                        "BattleConfigSnapshot"),
                };
                return new BattleConfigValidationResult(nullErrors);
            }

            var errors = new List<BattleConfigValidationError>();

            // 1. 配置版本校验（版本字段在 BattleLoadoutDto.ConfigVersion，权威校验在
            //    BattleRuntimeFactory.TryValidateLoadout loadout 层；本校验点做快照层防御性复查。
            //    详见 ValidateVersion 注释。）
            ValidateVersion(snapshot, errors);

            // 2. 缺表校验
            ValidateSections(snapshot, errors);

            // 3. 缺字段校验
            ValidateFields(snapshot, errors);

            // 4. 未知兵种校验
            ValidateUnits(snapshot, errors);

            // 5. 非法时间/距离校验
            ValidateTimesAndDistances(snapshot, errors);

            // 6. 地图尺寸校验
            ValidateMapSize(snapshot, errors);

            // 7. 越界路径校验
            ValidatePaths(snapshot, errors);

            // 8. 缺失引用校验（牌组引用未定义兵种）
            ValidateReferences(snapshot, errors);

            // 9. 敌人目录 + 有序波次计划（结构 + 交叉引用，tasks 2.7/2.9）
            ValidateEnemyCatalog(snapshot, errors);
            ValidateOrderedWavePlan(snapshot, errors);

            // 9b. Buff 目录（结构 + 数值，task 3.5）
            ValidateBuffCatalog(snapshot, errors);

            // 9c. Skill 目录（结构 + 数值，task 2.3）
            ValidateSkillCatalog(snapshot, errors);

            // 9d. Boss 目录（结构 + 数值，task 4.1）与所选 Boss 行依赖闭包（task 4.3）。
            ValidateBossCatalog(snapshot, errors);
            ValidateSelectedBossDependencies(snapshot, errors);

            // 9e. 武器目录（结构 + 启用集合，tasks 3.2/3.3）。
            ValidateWeaponCatalog(snapshot, errors);

            // 9f. 启用武将目录、有序配方与招募权重。
            ValidateGeneralCatalog(snapshot, errors);

            // 10. 运行能力校验（Boss 波 gate，spec "Reject Boss content when the capability is absent"）
            ValidateBossCapabilities(snapshot, capabilities, errors);

            // 若前面已致命错误，后续校验可能无意义，但仍收集所有错误便于诊断。
            return new BattleConfigValidationResult(errors);
        }

        private static void ValidateGeneralCatalog(
            BattleConfigSnapshot snapshot,
            List<BattleConfigValidationError> errors)
        {
            GeneralCatalogSnapshot catalog = snapshot.GeneralCatalog;
            if (catalog == null)
            {
                return;
            }

            var indices = new HashSet<int>();
            var recipes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < catalog.Definitions.Count; i++)
            {
                GeneralConfigSnapshot general = catalog.Definitions[i];
                string path = $"GeneralCatalog.Definitions[{i}]";
                if (general == null)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralConfigInvalid,
                        "武将定义为空",
                        path));
                    continue;
                }

                if (!indices.Add(general.Index))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralConfigInvalid,
                        $"武将 index={general.Index} 重复",
                        path + ".Index"));
                }

                if (string.IsNullOrWhiteSpace(general.Name)
                    || string.IsNullOrWhiteSpace(general.Family)
                    || general.PartWords.Count != 2
                    || string.IsNullOrWhiteSpace(general.PartWords.Count > 0 ? general.PartWords[0] : null)
                    || string.IsNullOrWhiteSpace(general.PartWords.Count > 1 ? general.PartWords[1] : null)
                    || (general.PartWords.Count == 2 && general.PartWords[0] == general.PartWords[1]))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralConfigInvalid,
                        "武将名称/姓氏缺失，或配方不是两个非空且不同的字",
                        path + ".PartWords"));
                }
                else if (!recipes.Add(general.RecipeKey))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralConfigInvalid,
                        $"有序武将配方 '{general.PartWords[0]}+{general.PartWords[1]}' 重复",
                        path + ".PartWords"));
                }

                if (general.RangeCells <= 0f
                    || general.AttackDamage <= 0
                    || general.AttackIntervalSeconds <= 0f
                    || general.PartRecruitWeight <= 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralConfigInvalid,
                        "武将距离、伤害、攻击间隔和武将字招募权重必须为正",
                        path));
                }

                if (string.IsNullOrWhiteSpace(general.PrefabAddress)
                    || string.IsNullOrWhiteSpace(general.AnimationKey)
                    || string.IsNullOrWhiteSpace(general.DamageMode)
                    || string.IsNullOrWhiteSpace(general.TargetPolicy))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralConfigInvalid,
                        "武将表现地址、动画键、伤害模式和目标策略不能为空",
                        path));
                }

                if (general.CombatArchetype == GeneralCombatArchetype.Bow)
                {
                    if (string.IsNullOrWhiteSpace(general.ProjectileType)
                        || general.ProjectileSpeed <= 0
                        || snapshot.Projectile == null
                        || !Contains(snapshot.Projectile.Types, general.ProjectileType))
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.GeneralConfigInvalid,
                            "弓兵原型武将必须引用已注册投射物并配置正速度",
                            path + ".ProjectileType"));
                    }
                }
                else if (general.CombatArchetype == GeneralCombatArchetype.Pike)
                {
                    if (!string.IsNullOrEmpty(general.ProjectileType) || general.ProjectileSpeed != 0)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.GeneralConfigInvalid,
                            "枪兵原型武将不得配置投射物",
                            path + ".ProjectileType"));
                    }
                }
                else
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralConfigInvalid,
                        $"未知武将战斗原型 {(int)general.CombatArchetype}",
                        path + ".CombatArchetype"));
                }

                // 武将主动技能绑定（本 change 起）：配置了 skillKey 的武将严格校验
                // 技能存在、类别 Active、triggerAttackCount 正，以及 handler 专用 effect 字段。
                ValidateGeneralSkill(snapshot, general, path, errors);
            }
        }

        /// <summary>
        /// 校验配置了主动技能键（skillKey）的武将：技能存在、Category=Active、
        /// triggerAttackCount 正，以及该技能 handler 专用 effect 配置字段完整。
        /// 未配置技能（skillKey 为空）的武将与普通士兵完全不受影响。
        /// </summary>
        /// <remarks>
        /// <para>spec "Skill definitions are validated before use"：运行时不 fallback、
        /// 不按 key 推导缺失字段。缺技能 / 错误类别 / 非法 trigger / handler 专用
        /// effect 字段缺失 MUST 在启动前被检出并阻断。</para>
        /// <para>只校验本框架已实现的 handler（BattleShout / FireArrowBarrage）；
        /// 配置了未实现 handler 的武将技能同样要求存在与 Active，但不做 handler
        /// 专用字段假设（禁止为其他未实现技能发明 fallback 配置）。</para>
        /// </remarks>
        private static void ValidateGeneralSkill(
            BattleConfigSnapshot snapshot,
            GeneralConfigSnapshot general,
            string path,
            List<BattleConfigValidationError> errors)
        {
            if (!general.SkillId.HasValue)
            {
                return;
            }

            SkillCatalogSnapshot skillCatalog = snapshot.SkillCatalog;
            if (skillCatalog == null || !skillCatalog.TryGetById(general.SkillId.Value, out SkillDefinitionSnapshot skillDef))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.GeneralSkillDefinitionMissing,
                    $"武将 index={general.Index} 引用的技能 skillKey='{general.SkillId}' 在 Skill 目录中不存在",
                    path + ".SkillId"));
                return;
            }

            string skillPath = $"Skill.{general.SkillId}";

            if (skillDef.Category != SkillCategory.Active)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.GeneralSkillCategoryInvalid,
                    $"武将 index={general.Index} 引用的技能 '{general.SkillId}' 类别={skillDef.Category}" +
                    "，主动技能必须为 Active",
                    path + ".SkillId"));
            }

            if (!skillDef.TriggerAttackCount.HasValue || skillDef.TriggerAttackCount.Value <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.GeneralSkillTriggerInvalid,
                    $"武将 index={general.Index} 引用的技能 '{general.SkillId}' triggerAttackCount 非法" +
                    $"（值={skillDef.TriggerAttackCount}，要求正数）",
                    path + ".SkillId"));
            }

            // handler 专用 effect 配置：只对本框架已实现 handler 校验专用字段。
            // 禁止为其他未实现技能发明 fallback 配置。
            if (string.Equals(skillDef.HandlerKey, "BattleShout", StringComparison.Ordinal))
            {
                if (!skillDef.RangeTiles.HasValue || skillDef.RangeTiles.Value <= 0f
                    || !skillDef.EffectBuffType.HasValue
                    || !skillDef.EffectDurationMs.HasValue
                    || skillDef.EffectDurationMs.Value <= 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralSkillEffectConfigMissing,
                        $"武将 index={general.Index} 引用的 BattleShout 技能 '{general.SkillId}' " +
                        "缺少专用 effect 配置（要求 range>0 / buffType / duration>0）",
                        $"{skillPath}.EffectConfig"));
                }
            }
            else if (string.Equals(skillDef.HandlerKey, "FireArrowBarrage", StringComparison.Ordinal))
            {
                bool invalidMultiplier = !skillDef.EffectDamageMultiplier.HasValue
                    || float.IsNaN(skillDef.EffectDamageMultiplier.Value)
                    || float.IsInfinity(skillDef.EffectDamageMultiplier.Value)
                    || skillDef.EffectDamageMultiplier.Value <= 0f;
                if (!skillDef.RangeTiles.HasValue || skillDef.RangeTiles.Value <= 0f
                    || invalidMultiplier)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.GeneralSkillEffectConfigMissing,
                        $"武将 index={general.Index} 引用的 FireArrowBarrage 技能 '{general.SkillId}' " +
                        "缺少专用 effect 配置（要求 range>0 / 有限正 damageMultiplier）",
                        $"{skillPath}.EffectConfig"));
                }
            }
        }

        private static bool Contains(IReadOnlyList<string> values, string expected)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // 1. 配置版本校验
        // ====================================================================

        /// <summary>
        /// 校验配置版本。本方法为快照层防御性复查，配置版本字段的权威校验在
        /// <see cref="BattleRuntimeFactory.TryValidateLoadout"/>（loadout 层）。
        /// </summary>
        /// <remarks>
        /// <para><b>版本字段位置（task 3.5 "覆盖配置版本"）：</b>
        /// 配置版本占位字段 <c>ConfigVersion</c> 定义在 <see cref="GameCommon.Battle.BattleLoadoutDto"/>
        /// 而非 <see cref="BattleConfigSnapshot"/> 中（1.8 审计：版本/hash 机制缺失，由 task 3.2/3.5/8.2 新建）。
        /// 版本字段的权威校验（拒绝负值等非法占位）由
        /// <see cref="BattleRuntimeFactory.TryValidateLoadout"/> 在 loadout 校验步骤执行，
        /// 负值返回 <see cref="BattleErrorCode.ConfigVersionMismatch"/>。</para>
        ///
        /// <para><b>本快照层复查内容：</b>快照本身不含版本字段，但含 <see cref="BattleConfigSnapshot.SourceTag"/>
        /// （配置来源标识 "Json"/"Luban"）。若 SourceTag 为空，表明配置来源未知，无法确定版本基线，
        /// 报告 <see cref="BattleConfigErrorCategory.InvalidVersion"/> 作为防御性保护。
        /// 正常路径下 Provider 总会设置 SourceTag，此项只在非标准构造路径触发。</para>
        ///
        /// <para><b>后续扩展点：</b>待 task 3.2/8.2 产物接入版本/hash 字段到快照或 loadout 后，
        /// 在 <see cref="BattleRuntimeFactory.TryValidateLoadout"/> 中扩展版本号匹配与 hash 校验。</para>
        /// </remarks>
        private static void ValidateVersion(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            // 防御性复查：SourceTag 为空表明配置来源未知，无法确定版本基线。
            // 正常路径下 JsonBattleConfigProvider 设 "Json"、LubanBattleConfigProvider 设 "Luban"；
            // BattleConfigSnapshot 构造已把 null 规范化为空串，此处只复查空串。
            // 版本字段的权威校验（ConfigVersion 负值等）在 BattleRuntimeFactory.TryValidateLoadout。
            if (string.IsNullOrEmpty(snapshot.SourceTag))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidVersion,
                    "配置来源标识 SourceTag 为空，无法确定版本基线；" +
                    "版本字段的权威校验在 BattleRuntimeFactory.TryValidateLoadout（loadout 层）",
                    "SourceTag"));
            }
        }

        // ====================================================================
        // 2. 缺表校验
        // ====================================================================

        /// <summary>
        /// 校验必需配置子节是否缺失（null）。
        /// </summary>
        private static void ValidateSections(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            // BattleConfigSnapshot 构造函数已对 null 子节抛 ArgumentNullException，
            // 但本校验器防御性复查，确保即使通过非标准路径构造的快照也能检出缺表。
            if (snapshot.Map == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "地图配置缺失",
                    "Map"));
            }

            if (snapshot.EnemyCatalog == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "敌人目录缺失（本 change 起为新生产链权威）",
                    "EnemyCatalog"));
            }

            if (snapshot.OrderedWavePlan == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "有序波次计划缺失（本 change 起为新生产链权威）",
                    "OrderedWavePlan"));
            }

            if (snapshot.BuffCatalog == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "Buff 目录缺失（本 change 起为新生产链权威）",
                    "BuffCatalog"));
            }

            if (snapshot.SkillCatalog == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "Skill 目录缺失（本 change 起为新生产链权威）",
                    "SkillCatalog"));
            }

            if (snapshot.Units == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "单位配置列表缺失",
                    "Units"));
            }

            if (snapshot.UnitLevel == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "单位等级配置缺失",
                    "UnitLevel"));
            }

            if (snapshot.Economy == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "经济配置缺失",
                    "Economy"));
            }

            if (snapshot.Deck == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "牌组配置缺失",
                    "Deck"));
            }

            if (snapshot.Projectile == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "投射物配置缺失",
                    "Projectile"));
            }
        }

        // ====================================================================
        // 3. 缺字段校验
        // ====================================================================

        /// <summary>
        /// 校验必需字段与集合是否为空。
        /// </summary>
        private static void ValidateFields(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            // 地图尺寸字段
            if (snapshot.Map != null && (snapshot.Map.Width <= 0 || snapshot.Map.Height <= 0))
            {
                // 尺寸非正归到 InvalidMapSize，此处不重复
            }

            // 单位列表
            if (snapshot.Units != null && snapshot.Units.Count == 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingField,
                    "单位配置列表为空",
                    "Units"));
            }

            // 牌组必需字段
            if (snapshot.Deck != null)
            {
                if (snapshot.Deck.BaseSoldierTexts.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "牌组基础兵字表为空",
                        "Deck.BaseSoldierTexts"));
                }

                if (snapshot.Deck.HandSize <= 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "手牌大小非正",
                        "Deck.HandSize"));
                }
            }

            // 投射物必需字段
            if (snapshot.Projectile != null)
            {
                if (snapshot.Projectile.Types.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "投射物类型列表为空",
                        "Projectile.Types"));
                }

                if (string.IsNullOrEmpty(snapshot.Projectile.PrimaryType))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "主要投射物类型为空",
                        "Projectile.PrimaryType"));
                }
            }
        }

        // ====================================================================
        // 4. 未知兵种校验
        // ====================================================================

        /// <summary>
        /// 校验单位索引与文本是否在本期合法四兵集合内。
        /// </summary>
        private static void ValidateUnits(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            if (snapshot.Units == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.Units.Count; i++)
            {
                UnitConfigSnapshot unit = snapshot.Units[i];

                if (!ValidUnitIndices.Contains(unit.Index))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.UnknownUnit,
                        $"单位索引={unit.Index} 不在本期合法集合 0..3",
                        $"Units[{i}].Index"));
                }

                if (string.IsNullOrEmpty(unit.Text))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.UnknownUnit,
                        $"单位文本为空",
                        $"Units[{i}].Text"));
                }
                else if (!ValidSoldierTexts.Contains(unit.Text))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.UnknownUnit,
                        $"单位文本='{unit.Text}' 不在本期合法四兵集合 (刀/弓/枪/骑)",
                        $"Units[{i}].Text"));
                }
            }
        }

        // ====================================================================
        // 6. 非法时间/距离校验
        // ====================================================================

        /// <summary>
        /// 校验时间与距离字段非负/非零。
        /// </summary>
        private static void ValidateTimesAndDistances(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            // 单位攻击间隔与距离
            if (snapshot.Units != null)
            {
                for (int i = 0; i < snapshot.Units.Count; i++)
                {
                    UnitConfigSnapshot unit = snapshot.Units[i];

                    if (unit.AttackIntervalSeconds < 0)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.InvalidTime,
                            $"单位攻击间隔={unit.AttackIntervalSeconds} 为负",
                            $"Units[{i}].AttackIntervalSeconds"));
                    }

                    if (unit.RangeCells <= 0)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.InvalidDistance,
                            $"单位攻击距离={unit.RangeCells} 非正",
                            $"Units[{i}].RangeCells"));
                    }
                }
            }
        }

        // ====================================================================
        // 6. 地图尺寸校验
        // ====================================================================

        /// <summary>
        /// 校验地图尺寸合法（Width/Height 为正）。
        /// </summary>
        /// <remarks>
        /// MapData 构造已保证尺寸为正，本校验器防御性复查。
        /// </remarks>
        private static void ValidateMapSize(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            if (snapshot.Map == null)
            {
                return;
            }

            if (snapshot.Map.Width <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapSize,
                    $"地图列数={snapshot.Map.Width} 非正",
                    "Map.Width"));
            }

            if (snapshot.Map.Height <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapSize,
                    $"地图行数={snapshot.Map.Height} 非正",
                    "Map.Height"));
            }
        }

        // ====================================================================
        // 8. 越界路径校验
        // ====================================================================

        /// <summary>
        /// 校验玩家与对手路径点均在地图范围内，且路径点不落在阻挡格或不可行走格上。
        /// </summary>
        /// <remarks>
        /// <para>task 3.5 特别要求：route marker 若仅属表现不得按游戏路径规则误判。</para>
        /// <para>本校验器区分两种问题：</para>
        /// <list type="bullet">
        /// <item><see cref="BattleConfigErrorCategory.PathOutOfBounds"/>：路径点坐标超出地图边界。</item>
        /// <item><see cref="BattleConfigErrorCategory.RouteMarkerMismatch"/>：路径点坐标在地图范围内，
        /// 但落在阻挡格(Kind=Blocked)或非通道格上。这种情况表明表现层 route marker
        /// 被错误地作为游戏路径点传入——游戏路径只应经过通道格(Kind=Passage)。
        /// 不按 PathOutOfBounds 或 InvalidMapSize 误判。</item>
        /// </list>
        /// <para>判定依据：游戏路径（Enemy 沿路径移动）只允许通过通道格(Passage, kind=0)。
        /// 还原工程 MapData.js findPath 只通过 kind=0 格。若路径点落在 kind=1(可建造) 或
        /// kind=2(阻挡) 格上，视为 route marker 与游戏路径混淆。</para>
        /// </remarks>
        private static void ValidatePaths(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            if (snapshot.Map == null)
            {
                return;
            }

            MapData map = snapshot.Map;

            // 玩家路径
            ValidateSinglePath(map, map.GetPlayerPath(), "Player", errors);

            // 对手路径
            ValidateSinglePath(map, map.GetOpponentPath(), "Opponent", errors);

            // 起终点边界（MapData 构造已校验，此处复查）
            ValidatePathEndpoint(map, map.PlayerStart, "PlayerStart", errors);
            ValidatePathEndpoint(map, map.PlayerEnd, "PlayerEnd", errors);
            ValidatePathEndpoint(map, map.OpponentStart, "OpponentStart", errors);
            ValidatePathEndpoint(map, map.OpponentEnd, "OpponentEnd", errors);
        }

        /// <summary>
        /// 校验单条路径的所有点在边界内且不落在非通道格上。
        /// </summary>
        private static void ValidateSinglePath(
            MapData map,
            IReadOnlyList<GridPosition> path,
            string sideLabel,
            List<BattleConfigValidationError> errors)
        {
            if (path == null || path.Count == 0)
            {
                // 空路径不在此校验（可能由 A* 未实现导致，由 MissingField 覆盖）
                return;
            }

            for (int i = 0; i < path.Count; i++)
            {
                GridPosition p = path[i];
                string pathStr = $"{sideLabel}.Path[{i}] ({p.X},{p.Y})";

                if (!map.IsInside(p))
                {
                    // 越界
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.PathOutOfBounds,
                        $"{sideLabel} 路径点[{i}] ({p.X},{p.Y}) 越界（width={map.Width} height={map.Height}）",
                        $"{sideLabel}.Path[{i}]"));
                    continue;
                }

                // 路径点在地图内：检查是否为通道格。
                // 游戏路径只允许通过通道格(Passage, kind=0)；若落在可建造或阻挡格上，
                // 视为 route marker 误用——表现层 marker 不应作为游戏路径点传入。
                GridCell cell = map.GetCell(p);
                if (cell.Kind != GridCellKind.Passage)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.RouteMarkerMismatch,
                        $"{sideLabel} 路径点[{i}] ({p.X},{p.Y}) 落在 {cell.Kind} 格上，" +
                        "应为 Passage(通道)；route marker 若仅属表现不得作为游戏路径点",
                        $"{sideLabel}.Path[{i}]"));
                }
            }
        }

        /// <summary>
        /// 校验单个起终点在地图范围内。
        /// </summary>
        private static void ValidatePathEndpoint(
            MapData map,
            GridPosition endpoint,
            string label,
            List<BattleConfigValidationError> errors)
        {
            if (!map.IsInside(endpoint))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.PathOutOfBounds,
                    $"{label} ({endpoint.X},{endpoint.Y}) 越界（width={map.Width} height={map.Height}）",
                    label));
            }
        }

        // ====================================================================
        // 9. 缺失引用校验
        // ====================================================================

        /// <summary>
        /// 校验牌组 BaseSoldierTexts 引用的兵种文本均在单位配置中定义。
        /// </summary>
        private static void ValidateReferences(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            if (snapshot.Deck == null || snapshot.Units == null)
            {
                return;
            }

            // 收集单位配置中已定义的兵种文本
            var definedTexts = new HashSet<string>();
            for (int i = 0; i < snapshot.Units.Count; i++)
            {
                if (!string.IsNullOrEmpty(snapshot.Units[i].Text))
                {
                    definedTexts.Add(snapshot.Units[i].Text);
                }
            }

            // 校验牌组引用
            IReadOnlyList<string> baseTexts = snapshot.Deck.BaseSoldierTexts;
            for (int i = 0; i < baseTexts.Count; i++)
            {
                string text = baseTexts[i];
                if (string.IsNullOrEmpty(text))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingReference,
                        $"牌组基础兵字表[{i}] 为空",
                        $"Deck.BaseSoldierTexts[{i}]"));
                    continue;
                }

                if (!definedTexts.Contains(text))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingReference,
                        $"牌组引用兵种 '{text}' 未在单位配置中定义",
                        $"Deck.BaseSoldierTexts[{i}]"));
                }
            }

            // 校验牌组兵字是否在本期合法四兵集合内（双重保险，与 ValidateUnits 互补）
            for (int i = 0; i < baseTexts.Count; i++)
            {
                string text = baseTexts[i];
                if (!string.IsNullOrEmpty(text) && !ValidSoldierTexts.Contains(text))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.UnknownUnit,
                        $"牌组兵种 '{text}' 不在本期合法四兵集合 (刀/弓/枪/骑)",
                        $"Deck.BaseSoldierTexts[{i}]"));
                }
            }

            // 校验投射物主要类型在类型列表中
            if (snapshot.Projectile != null)
            {
                string primary = snapshot.Projectile.PrimaryType;
                if (!string.IsNullOrEmpty(primary))
                {
                    bool found = false;
                    for (int i = 0; i < snapshot.Projectile.Types.Count; i++)
                    {
                        if (snapshot.Projectile.Types[i] == primary)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.MissingReference,
                            $"投射物主要类型 '{primary}' 不在 Types 列表中",
                            "Projectile.PrimaryType"));
                    }
                }
            }
        }

        // ====================================================================
        // 10. 敌人目录校验（spec "Enemy configuration is a keyed immutable catalog"）
        // ====================================================================

        /// <summary>
        /// 校验敌人目录的结构与数值合法性（tasks 2.7/2.9）。
        /// </summary>
        /// <remarks>
        /// <para>双索引唯一性（重复 enemyKey/typeIndex）由
        /// <see cref="EnemyCatalogSnapshot"/> 构造保证，此处只校验定义数值与资源字段：
        /// 非空键/资源地址、正移动速度、非空且全为正数的逐难度血量、有效早期乘数、
        /// 非负接触伤害与击杀奖励。</para>
        /// </remarks>
        private static void ValidateEnemyCatalog(
            BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            EnemyCatalogSnapshot catalog = snapshot.EnemyCatalog;
            if (catalog == null)
            {
                // MissingSection 已在 ValidateSections 报告。
                return;
            }

            if (catalog.Definitions.Count == 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingField,
                    "敌人目录为空（没有任何普通敌人定义）",
                    "EnemyCatalog.Definitions"));
                return;
            }

            for (int i = 0; i < catalog.Definitions.Count; i++)
            {
                EnemyDefinitionSnapshot def = catalog.Definitions[i];
                string defPath = $"EnemyCatalog.Definitions[{i}]";

                if (def.TypeIndex < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.EnemyCatalogInvalid,
                        $"敌人定义 typeIndex={def.TypeIndex} 为负",
                        $"{defPath}.TypeIndex"));
                }

                if (def.Id < 1)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.EnemyCatalogInvalid,
                        $"敌人定义（typeIndex={def.TypeIndex}）Key 为空",
                        $"{defPath}.Key"));
                }

                if (string.IsNullOrEmpty(def.ResourceAddress))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.EnemyCatalogInvalid,
                        $"敌人定义 '{def.Id}' 资源地址为空",
                        $"{defPath}.ResourceAddress"));
                }

                if (def.MoveSpeed <= 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.EnemyCatalogInvalid,
                        $"敌人定义 '{def.Id}' 移动速度={def.MoveSpeed} 非正",
                        $"{defPath}.MoveSpeed"));
                }

                if (def.HealthByWave.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        $"敌人定义 '{def.Id}' 各波次血量数组为空",
                        $"{defPath}.HealthByWave"));
                }
                else
                {
                    for (int h = 0; h < def.HealthByWave.Count; h++)
                    {
                        if (def.HealthByWave[h] <= 0)
                        {
                            errors.Add(new BattleConfigValidationError(
                                BattleConfigErrorCategory.EnemyCatalogInvalid,
                                $"敌人定义 '{def.Id}' 各波次血量[{h}]={def.HealthByWave[h]} 非正",
                                $"{defPath}.HealthByWave[{h}]"));
                        }
                    }
                }

                if (def.EarlyRoundHealthMultipliers.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        $"敌人定义 '{def.Id}' 早期波次血量乘数数组为空",
                        $"{defPath}.EarlyRoundHealthMultipliers"));
                }
                else
                {
                    for (int m = 0; m < def.EarlyRoundHealthMultipliers.Count; m++)
                    {
                        if (def.EarlyRoundHealthMultipliers[m] <= 0f)
                        {
                            errors.Add(new BattleConfigValidationError(
                                BattleConfigErrorCategory.EnemyCatalogInvalid,
                                $"敌人定义 '{def.Id}' 早期乘数[{m}]={def.EarlyRoundHealthMultipliers[m]} 非正",
                                $"{defPath}.EarlyRoundHealthMultipliers[{m}]"));
                        }
                    }
                }

                if (def.ContactDamage < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.EnemyCatalogInvalid,
                        $"敌人定义 '{def.Id}' 接触伤害={def.ContactDamage} 为负",
                        $"{defPath}.ContactDamage"));
                }

                if (def.RewardGold < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.EnemyCatalogInvalid,
                        $"敌人定义 '{def.Id}' 击杀奖励={def.RewardGold} 为负",
                        $"{defPath}.RewardGold"));
                }
            }
        }

        // ====================================================================
        // 9b. Buff 目录校验（spec "Buff definitions form a validated immutable catalog"）
        // ====================================================================

        /// <summary>
        /// 校验 Buff 目录的结构与数值合法性（task 3.5）。
        /// </summary>
        /// <remarks>
        /// <para>拒绝：重复 type（由 <see cref="BuffCatalogSnapshot"/> 构造保证，
        /// Provider 以 BuffTypeDuplicate 结构化错误报告）、未知 Kind/StackPolicy、
        /// 未知通道值（合法 0..6）、Numeric/State 定义通道为空、非正 maxStacks。</para>
        /// <para>Custom 定义允许空通道（其 handler 拥有窄效果契约）；若 Custom 显式
        /// 携带通道仍按已知通道集合校验，不额外拒绝（type 8 custom 配置为空通道）。</para>
        /// <para>所有诊断包含 Buff type 与字段名（路径形如 <c>Buff.{type}.Channels</c>），
        /// 不依赖运行时 fallback 掩盖缺失配置。</para>
        /// </remarks>
        private static void ValidateBuffCatalog(
            BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            BuffCatalogSnapshot catalog = snapshot.BuffCatalog;
            if (catalog == null)
            {
                // MissingSection 已在 ValidateSections 报告。
                return;
            }

            if (catalog.Definitions.Count == 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingField,
                    "Buff 目录为空（没有任何 Buff 定义）",
                    "BuffCatalog.Definitions"));
                return;
            }

            IReadOnlyList<BuffDefinitionSnapshot> definitions = catalog.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                BuffDefinitionSnapshot def = definitions[i];
                string buffPath = $"Buff.{def.Type}";

                // 未知 Kind（handler 类别）。
                if (!IsKnownBuffKind(def.Kind))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BuffKindUnknown,
                        $"Buff type={def.Type} 的 Kind={def.Kind} 未知（合法 0 Numeric/1 State/2 Custom）",
                        $"{buffPath}.Kind"));
                }

                // 未知叠层策略。
                if (!IsKnownBuffStackPolicy(def.StackPolicy))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BuffStackPolicyUnknown,
                        $"Buff type={def.Type} 的 StackPolicy={def.StackPolicy} 未知（合法 Add/Refresh/Replace）",
                        $"{buffPath}.StackPolicy"));
                }

                // Numeric/State 必填通道；Custom 允许空通道（handler 拥有窄效果契约）。
                IReadOnlyList<int> channels = def.Channels;
                if (def.Kind != BuffKind.Custom && channels.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BuffChannelMissing,
                        $"Buff type={def.Type} 的 {def.Kind} 定义缺少通道（仅 Custom 允许空通道）",
                        $"{buffPath}.Channels"));
                }

                // 未知通道值（合法 0..6：Numeric 0..6 / State 0..6）。
                for (int c = 0; c < channels.Count; c++)
                {
                    if (channels[c] < 0 || channels[c] > 6)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.BuffChannelInvalid,
                            $"Buff type={def.Type} 的通道={channels[c]} 未知（合法 0..6）",
                            $"{buffPath}.Channels[{c}]"));
                    }
                }

                // 非正 maxStacks。
                if (def.MaxStacks <= 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BuffMaxStacksInvalid,
                        $"Buff type={def.Type} 的 maxStacks={def.MaxStacks} 非正",
                        $"{buffPath}.MaxStacks"));
                }
            }
        }

        /// <summary>
        /// 判断 Kind 是否为已知业务类别（Numeric / State / Custom）。
        /// </summary>
        private static bool IsKnownBuffKind(BuffKind kind)
        {
            return kind == BuffKind.Numeric || kind == BuffKind.State || kind == BuffKind.Custom;
        }

        /// <summary>
        /// 判断叠层策略是否为已知业务策略（Add / Refresh / Replace）。
        /// </summary>
        private static bool IsKnownBuffStackPolicy(BuffStackPolicy policy)
        {
            return policy == BuffStackPolicy.Add
                   || policy == BuffStackPolicy.Refresh
                   || policy == BuffStackPolicy.Replace;
        }

        // ====================================================================
        // 9c. Skill 目录校验（spec "Skill definitions are validated before use"）
        // ====================================================================

        /// <summary>
        /// 校验 Skill 目录的结构与数值合法性（task 2.3）。
        /// </summary>
        /// <remarks>
        /// <para>拒绝：空 key、重复 key（目录构造已保证，此处防御性复查）、未知类别、
        /// 负冷却毫秒、毫秒溢出（超出 int 秒源可表达范围）、空 handlerKey。</para>
        /// <para>重复 key 由 <see cref="SkillCatalogSnapshot"/> 构造保证，Provider 以
        /// SkillKeyDuplicate 结构化错误报告；本校验器的重复扫描为防御性复查（与
        /// InvalidMapSize 相同模式），覆盖非标准构造路径。</para>
        /// <para>所有诊断包含 Skill key 与字段名（路径形如 <c>Skill.{key}.CooldownMs</c>），
        /// 不依赖运行时 fallback 掩盖缺失配置。</para>
        /// </remarks>
        private static void ValidateSkillCatalog(
            BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            SkillCatalogSnapshot catalog = snapshot.SkillCatalog;
            if (catalog == null)
            {
                // MissingSection 已在 ValidateSections 报告。
                return;
            }

            if (catalog.Definitions.Count == 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingField,
                    "Skill 目录为空（没有任何 Skill 定义）",
                    "SkillCatalog.Definitions"));
                return;
            }

            IReadOnlyList<SkillDefinitionSnapshot> definitions = catalog.Definitions;
            var seenKeys = new HashSet<int>();
            for (int i = 0; i < definitions.Count; i++)
            {
                SkillDefinitionSnapshot def = definitions[i];
                string skillPath = $"Skill.{def.Id}";

                // 空 key（缺失主键）。
                if (def.Id < 1)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.SkillKeyInvalid,
                        $"Skill 定义（索引 {i}）的 Key 为空",
                        $"SkillCatalog.Definitions[{i}].Key"));
                }
                else if (!seenKeys.Add(def.Id))
                {
                    // 重复 key（目录构造已保证，此处防御性复查）。
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.SkillKeyDuplicate,
                        $"Skill key='{def.Id}' 重复",
                        $"{skillPath}.Key"));
                }

                // 未知类别（严格映射 active/boss/passive，不做 fallback）。
                if (!IsKnownSkillCategory(def.Category))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.SkillCategoryUnknown,
                        $"Skill key='{def.Id}' 的 Category={def.Category} 未知（合法 active/boss/passive）",
                        $"{skillPath}.Category"));
                }

                // 负冷却毫秒。
                if (def.CooldownMs < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.SkillCooldownInvalid,
                        $"Skill key='{def.Id}' 的 CooldownMs={def.CooldownMs} 为负",
                        $"{skillPath}.CooldownMs"));
                }
                // 空 handlerKey（缺配置时在 xlsx 补齐，运行时不 fallback）。
                if (string.IsNullOrEmpty(def.HandlerKey))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.SkillHandlerKeyMissing,
                        $"Skill key='{def.Id}' 的 handlerKey 为空",
                        $"{skillPath}.HandlerKey"));
                }
            }
        }

        /// <summary>
        /// 判断 Skill 类别是否为已知业务类别（Active / Boss / Passive）。
        /// </summary>
        private static bool IsKnownSkillCategory(SkillCategory category)
        {
            return category == SkillCategory.Active
                   || category == SkillCategory.Boss
                   || category == SkillCategory.Passive;
        }

        // ====================================================================
        // 11. 有序波次计划校验（结构 + 交叉引用，spec "Wave order is explicit and valid"）
        // ====================================================================

        /// <summary>
        /// 校验有序波次计划的结构（order 连续/唯一、类型、时序、车道、数量、bossKey）
        /// 与交叉引用（行级 enemyKey、地图默认映射、难度/profile、strategyProfile）。
        /// </summary>
        private static void ValidateOrderedWavePlan(
            BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            OrderedWavePlanSnapshot plan = snapshot.OrderedWavePlan;
            if (plan == null)
            {
                // MissingSection 已在 ValidateSections 报告。
                return;
            }

            if (plan.ActivePlanId < 1)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.WavePlanMissing,
                    "波次计划 activePlanId 为空",
                    "OrderedWavePlan.ActivePlanId"));
            }

            IReadOnlyList<WavePlanEntry> rows = plan.Rows;
            if (rows.Count == 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.WavePlanMissing,
                    "波次计划没有任何行",
                    "OrderedWavePlan.Rows"));
                return;
            }

            var seenOrders = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                WavePlanEntry row = rows[i];
                string rowPath = $"WavePlan.{row.Order}";

                // order 从 1 开始、严格连续且不重复。
                int expectedOrder = i + 1;
                if (row.Order != expectedOrder)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WaveOrderGap,
                        $"order={row.Order} 不连续：应为 {expectedOrder}（从 1 开始严格连续）",
                        rowPath));
                }

                if (!seenOrders.Add(row.Order))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WaveOrderDuplicate,
                        $"order={row.Order} 重复",
                        rowPath));
                }

                // 波次类型。
                if (!IsKnownWaveKind(row.Kind))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WaveKindUnknown,
                        $"未知波次类型 {row.Kind}",
                        $"{rowPath}.Kind"));
                }

                // 时序非负（spec "Wave order is explicit and valid"）。
                if (row.PreDelayMs < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WaveTimingInvalid,
                        $"前置延迟={row.PreDelayMs}ms 为负",
                        $"{rowPath}.PreDelayMs"));
                }

                if (row.SpawnIntervalMs < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WaveTimingInvalid,
                        $"出生间隔={row.SpawnIntervalMs}ms 为负",
                        $"{rowPath}.SpawnIntervalMs"));
                }

                if (row.PostDelayMs < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WaveTimingInvalid,
                        $"结束等待={row.PostDelayMs}ms 为负",
                        $"{rowPath}.PostDelayMs"));
                }

                // 至少一个出生车道（spec "Wave order is explicit and valid"）。
                if (!row.PlayerLane && !row.OpponentLane)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WaveLaneInvalid,
                        "两个出生车道都关闭",
                        $"{rowPath}.Lane"));
                }

                if (row.Kind == WavePlanKind.Normal)
                {
                    ValidateNormalRow(snapshot, plan, row, rowPath, errors);
                }
                else if (row.Kind == WavePlanKind.Boss)
                {
                    // Boss 行必须携带 bossKey（spec "Wave order is explicit and valid"）。
                    if (!row.BossId.HasValue)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.WaveBossKeyMissing,
                            "Boss 行缺少 bossKey",
                            $"{rowPath}.BossId"));
                    }
                }
            }
        }

        /// <summary>
        /// 校验单条 Normal 行：数量、生效敌人键解析、难度与策略 profile 交叉引用。
        /// </summary>
        private static void ValidateNormalRow(
            BattleConfigSnapshot snapshot,
            OrderedWavePlanSnapshot plan,
            WavePlanEntry row,
            string rowPath,
            List<BattleConfigValidationError> errors)
        {
            // Normal 行数量非正（spec "Wave order is explicit and valid"）。
            if (row.NormalCount <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.WaveCountInvalid,
                    $"Normal 行普通敌人数量={row.NormalCount} 非正",
                    $"{rowPath}.NormalCount"));
            }

            // 解析生效敌人键：非空 enemyKey 直接使用；空时按地图 EnemyTypeIndex 解析
            // （spec "Map default and row override resolve one enemy key"）。
            if (!TryResolveNormalEnemyKey(snapshot, row, rowPath, errors, out EnemyDefinitionSnapshot definition))
            {
                return;
            }

            // 难度索引：同时索引血量曲线与策略乘数位置（spec "Difficulty selection is explicit"）。
            if (row.DifficultyIndex < 0 || row.DifficultyIndex >= definition.HealthByWave.Count)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.DifficultyIndexInvalid,
                    $"难度索引={row.DifficultyIndex} 越界：敌人 '{definition.Id}' 血量曲线长度={definition.HealthByWave.Count}",
                    $"{rowPath}.DifficultyIndex"));
            }

            // strategyProfile 交叉引用 + 难度在 profile 乘数长度内。
            if (!plan.TryGetProfile(row.StrategyProfile, out IReadOnlyList<float> profile))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.StrategyProfileInvalid,
                    $"策略 profile 索引={row.StrategyProfile} 无效（未被计划保留或不存在）",
                    $"{rowPath}.StrategyProfile"));
            }
            else if (row.DifficultyIndex < 0 || row.DifficultyIndex >= profile.Count)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.DifficultyIndexInvalid,
                    $"难度索引={row.DifficultyIndex} 越界：策略 profile[{row.StrategyProfile}] 乘数长度={profile.Count}",
                    $"{rowPath}.DifficultyIndex"));
            }
        }

        /// <summary>
        /// 解析 Normal 行生效的普通敌人键并校验未知 key/typeIndex。
        /// </summary>
        private static bool TryResolveNormalEnemyKey(
            BattleConfigSnapshot snapshot,
            WavePlanEntry row,
            string rowPath,
            List<BattleConfigValidationError> errors,
            out EnemyDefinitionSnapshot definition)
        {
            EnemyCatalogSnapshot catalog = snapshot.EnemyCatalog;
            if (catalog == null)
            {
                definition = null;
                return false;
            }

            if (!!row.EnemyId.HasValue)
            {
                if (catalog.TryGetById(row.EnemyId.Value, out definition))
                {
                    return true;
                }

                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.EnemyKeyUnknown,
                    $"Normal 行引用了目录中不存在的 enemyKey='{row.EnemyId}'",
                    $"{rowPath}.EnemyId"));
                return false;
            }

            int typeIndex = snapshot.Map != null ? snapshot.Map.EnemyTypeIndex : -1;
            if (catalog.TryGetByTypeIndex(typeIndex, out definition))
            {
                return true;
            }

            errors.Add(new BattleConfigValidationError(
                BattleConfigErrorCategory.EnemyTypeIndexUnknown,
                $"Normal 行未填写 enemyKey，且地图默认敌人索引 typeIndex={typeIndex} 无法在目录中解析",
                "Map.EnemyTypeIndex"));
            return false;
        }

        /// <summary>
        /// 判断波次类型是否为已知业务类型（Normal / Boss）。
        /// </summary>
        private static bool IsKnownWaveKind(WavePlanKind kind)
        {
            return kind == WavePlanKind.Normal || kind == WavePlanKind.Boss;
        }

        // ====================================================================
        // 9d. Boss 目录与所选 Boss 行依赖校验（task 4.1/4.3）
        // ====================================================================

        /// <summary>
        /// 校验 Boss 目录的结构与数值合法性（task 4.1）。
        /// </summary>
        /// <remarks>
        /// <para>重复 key 由 <see cref="BossCatalogSnapshot"/> 构造保证（Provider 以
        /// BossKeyDuplicate 结构化错误报告）。此处只校验定义数值：非空键、非空技能键、
        /// 时间轴非 null 且 effect&lt;complete、正生命倍率/移动速度/逻辑尺寸、
        /// 非负接触伤害与击杀奖励。</para>
        /// <para><b>production resource closure 边界（task 4.4）：</b>本校验器不把
        /// 空的 <see cref="BossDefinitionSnapshot.ResourcePath"/> 判为非法——那是
        /// 7.x resource plan 的生产表现门禁，pure logic 使用 Null view 可验收；
        /// gameplay 合法与 production resource closure 在此清晰分离。</para>
        /// </remarks>
        private static void ValidateBossCatalog(
            BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            BossCatalogSnapshot catalog = snapshot.BossCatalog;
            if (catalog == null)
            {
                // 无 Boss 行的计划允许 BossCatalog 为 null（旧兼容路径）；
                // 所选计划含 Boss 行时由 ValidateSelectedBossDependencies 报告 BossCatalogMissing。
                return;
            }

            IReadOnlyList<BossDefinitionSnapshot> definitions = catalog.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                BossDefinitionSnapshot def = definitions[i];
                string bossPath = $"Boss.{def.Id}";

                if (def.Id < 1)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossConfigInvalid,
                        $"Boss 定义（索引 {i}）的 Key 为空",
                        $"BossCatalog.Definitions[{i}].Key"));
                }

                if (def.SkillId < 1)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossSkillKeyMissing,
                        $"Boss key='{def.Id}' 缺少技能键（skillKey 为空，必填）",
                        $"{bossPath}.SkillId"));
                }

                if (def.Timeline == null)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossTimelineInvalid,
                        $"Boss key='{def.Id}' 技能时间轴为空（必填）",
                        $"{bossPath}.Timeline"));
                }
                else if (def.Timeline.EffectAtMs < 0
                         || def.Timeline.CompleteAtMs <= def.Timeline.EffectAtMs)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossTimelineInvalid,
                        $"Boss key='{def.Id}' 技能时间轴非法：effect={def.Timeline.EffectAtMs}ms " +
                        $"complete={def.Timeline.CompleteAtMs}ms（要求 0 &lt;= effect &lt; complete）",
                        $"{bossPath}.Timeline"));
                }

                if (float.IsNaN(def.HealthMultiplier)
                    || float.IsInfinity(def.HealthMultiplier)
                    || def.HealthMultiplier <= 0f)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossConfigInvalid,
                        $"Boss key='{def.Id}' 生命倍率={def.HealthMultiplier} 非正",
                        $"{bossPath}.HealthMultiplier"));
                }

                if (float.IsNaN(def.MoveSpeed)
                    || float.IsInfinity(def.MoveSpeed)
                    || def.MoveSpeed <= 0f
                    || def.MoveSpeed > int.MaxValue)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossConfigInvalid,
                        $"Boss key='{def.Id}' 移动速度={def.MoveSpeed} 非正",
                        $"{bossPath}.MoveSpeed"));
                }

                if (def.ContactDamage < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossConfigInvalid,
                        $"Boss key='{def.Id}' 接触伤害={def.ContactDamage} 为负",
                        $"{bossPath}.ContactDamage"));
                }

                if (def.RewardGold < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossConfigInvalid,
                        $"Boss key='{def.Id}' 击杀奖励={def.RewardGold} 为负",
                        $"{bossPath}.RewardGold"));
                }

                if (float.IsNaN(def.LogicalWidth)
                    || float.IsInfinity(def.LogicalWidth)
                    || float.IsNaN(def.LogicalHeight)
                    || float.IsInfinity(def.LogicalHeight)
                    || def.LogicalWidth <= 0f
                    || def.LogicalHeight <= 0f)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossConfigInvalid,
                        $"Boss key='{def.Id}' 逻辑尺寸非法：宽={def.LogicalWidth} 高={def.LogicalHeight}（应为正）",
                        $"{bossPath}.LogicalDimensions"));
                }
            }
        }

        /// <summary>
        /// 校验所选计划中每个 Boss 行的直接依赖闭包（task 4.3）。
        /// </summary>
        /// <remarks>
        /// <para>spec "Only ZhangLiang is supported in the first slice"：对每个被计划
        /// 引用的 Boss 行校验：Boss 目录存在 → 定义存在 → enabled → skillKey 非空 →
        /// Skill 定义存在 → handlerKey 非空 → 效果 Buff（type 14 / chaos）在 Buff 目录中存在 →
        /// effect 配置（range/duration）完整 → 时间轴合法。</para>
        /// <para><b>不扩展通用 RequiredSkill/RequiredBuff capabilities（design.md 决策 3）：</b>
        /// 只直接校验 selected 行引用的具体 Boss→Skill→Buff 依赖，不新增通用 DSL。
        /// handler 的实际注册由运行时 SkillRunner.Attach 校验（目录可含未实现 handler 行）。</para>
        /// </remarks>
        private static void ValidateSelectedBossDependencies(
            BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            OrderedWavePlanSnapshot plan = snapshot.OrderedWavePlan;
            if (plan == null)
            {
                return;
            }

            BossCatalogSnapshot catalog = snapshot.BossCatalog;
            IReadOnlyList<WavePlanEntry> rows = plan.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                WavePlanEntry row = rows[i];
                if (row.Kind != WavePlanKind.Boss)
                {
                    continue;
                }

                string rowPath = $"WavePlan.{row.Order}";
                if (!row.BossId.HasValue)
                {
                    // WaveBossKeyMissing 已在 ValidateOrderedWavePlan 报告。
                    continue;
                }

                if (catalog == null)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossCatalogMissing,
                        $"计划 '{plan.ActivePlanId}' 含 Boss 行（order={row.Order}，bossKey='{row.BossId}'），" +
                        "但配置快照没有 Boss 目录，无法验证依赖",
                        $"{rowPath}.BossId"));
                    continue;
                }

                if (!catalog.TryGetById(row.BossId.Value, out BossDefinitionSnapshot bossDef))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossKeyUnknown,
                        $"计划 '{plan.ActivePlanId}' Boss 行（order={row.Order}）引用了目录中不存在的 " +
                        $"bossKey='{row.BossId}'",
                        $"{rowPath}.BossId"));
                    continue;
                }

                if (!bossDef.Enabled)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossDisabled,
                        $"计划 '{plan.ActivePlanId}' Boss 行（order={row.Order}）引用的 " +
                        $"bossKey='{row.BossId}' 未启用（disabled，不得出生）",
                        $"{rowPath}.BossId"));
                    continue;
                }

                // Boss → Skill 依赖闭包（缺 Skill/handler/Buff）。
                ValidateBossSkillClosure(snapshot, row, rowPath, bossDef, errors);
            }
        }

        /// <summary>
        /// 校验单个 Boss 定义的 Skill→handler→Buff 依赖闭包。
        /// </summary>
        private static void ValidateBossSkillClosure(
            BattleConfigSnapshot snapshot,
            WavePlanEntry row,
            string rowPath,
            BossDefinitionSnapshot bossDef,
            List<BattleConfigValidationError> errors)
        {
            string bossPath = $"Boss.{bossDef.Id}";
            if (bossDef.SkillId < 1)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.BossSkillKeyMissing,
                    $"Boss key='{bossDef.Id}'（order={row.Order}）缺少技能键（skillKey 为空）",
                    $"{bossPath}.SkillId"));
                return;
            }

            if (string.Equals(bossDef.ResName, ZhangLiangBoss.ResNameConst, StringComparison.Ordinal)
                && bossDef.SkillId < 1)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.BossSkillDefinitionMissing,
                    $"首期 ZhangLiang 必须引用 SoulCapture，实际 skillKey='{bossDef.SkillId}'",
                    $"{bossPath}.SkillId"));
                return;
            }

            SkillCatalogSnapshot skillCatalog = snapshot.SkillCatalog;
            if (skillCatalog == null || !skillCatalog.TryGetById(bossDef.SkillId, out SkillDefinitionSnapshot skillDef))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.BossSkillDefinitionMissing,
                    $"Boss key='{bossDef.Id}'（order={row.Order}）引用的技能 " +
                    $"skillKey='{bossDef.SkillId}' 在 Skill 目录中不存在",
                    $"{bossPath}.SkillId"));
                return;
            }

            if (string.IsNullOrEmpty(skillDef.HandlerKey)
                || (string.Equals(bossDef.ResName, ZhangLiangBoss.ResNameConst, StringComparison.Ordinal)
                    && !string.Equals(skillDef.HandlerKey, "SoulCapture", StringComparison.Ordinal)))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.BossSkillHandlerMissing,
                    $"Boss key='{bossDef.Id}' 引用的技能 '{bossDef.SkillId}' handlerKey 无生产实现" +
                    $"（实际='{skillDef.HandlerKey}'，首期只注册 SoulCapture）",
                    $"Skill.{bossDef.SkillId}.HandlerKey"));
            }

            // 效果 Buff：effectBuffType 必填，且必须引用一基主键中的 Buff14（chaos）。
            if (!skillDef.EffectBuffType.HasValue)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.BossEffectBuffMissing,
                    $"Boss key='{bossDef.Id}' 引用的技能 '{bossDef.SkillId}' 缺少效果 Buff 类型 " +
                    "（effectBuffType 为空，无法施加 Buff14）",
                    $"Skill.{bossDef.SkillId}.EffectBuffType"));
            }
            else
            {
                int buffType = skillDef.EffectBuffType.Value;
                BuffCatalogSnapshot buffCatalog = snapshot.BuffCatalog;
                if (buffType != 14 || buffCatalog == null || !buffCatalog.ContainsType(buffType))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossEffectBuffMissing,
                        $"Boss key='{bossDef.Id}' 引用的技能 '{bossDef.SkillId}' 效果 Buff " +
                        $"type={buffType} 无效（SoulCapture 必须使用已存在的 Buff14）",
                        $"Skill.{bossDef.SkillId}.EffectBuffType"));
                }
            }

            // effect 配置（范围/持续）：SoulCapture 专用配置必填，不做通用 DSL。
            if (!skillDef.EffectDurationMs.HasValue || skillDef.EffectDurationMs.Value <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.BossSkillEffectConfigMissing,
                    $"Boss key='{bossDef.Id}' 引用的技能 '{bossDef.SkillId}' 效果持续时长非法" +
                    $"（effectDurationMs={skillDef.EffectDurationMs?.ToString() ?? "null"}，应为正）",
                    $"Skill.{bossDef.SkillId}.EffectDurationMs"));
            }

            if (!skillDef.RangeTiles.HasValue || skillDef.RangeTiles.Value <= 0f)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.BossSkillEffectConfigMissing,
                    $"Boss key='{bossDef.Id}' 引用的技能 '{bossDef.SkillId}' 效果范围非法" +
                    $"（rangeTiles={skillDef.RangeTiles?.ToString() ?? "null"}，应为正）",
                    $"Skill.{bossDef.SkillId}.RangeTiles"));
            }

            if (string.Equals(bossDef.ResName, ZhangLiangBoss.ResNameConst, StringComparison.Ordinal))
            {
                if (skillDef.CooldownMs != 8000)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossSkillEffectConfigMissing,
                        $"SoulCapture cooldownMs 必须为 8000，实际={skillDef.CooldownMs}",
                        $"Skill.{bossDef.SkillId}.CooldownMs"));
                }

                if (skillDef.RangeTiles != 2f || skillDef.EffectDurationMs != 2000)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossSkillEffectConfigMissing,
                        "SoulCapture 专用配置必须为 rangeTiles=2、effectDurationMs=2000",
                        $"Skill.{bossDef.SkillId}"));
                }

                if (bossDef.Timeline == null
                    || bossDef.Timeline.EffectAtMs != 500
                    || bossDef.Timeline.CompleteAtMs != 1400)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossTimelineInvalid,
                        "ZhangLiang 时间线必须为 effectAtMs=500、completeAtMs=1400",
                        $"{bossPath}.Timeline"));
                }
            }
        }

        // ====================================================================
        // 9e. 武器目录校验（spec "Exactly four basic weapon definitions are enabled"）
        // ====================================================================

        /// <summary>
        /// 校验武器目录的结构与启用集合（tasks 3.2/3.3）。
        /// </summary>
        /// <remarks>
        /// <para><b>非必选目录（与 Boss 目录同一门禁策略）：</b>本 change 不要求所有
        /// 启动路径都装配 WeaponCatalog——旧兼容快照/旧测试直接构造时可为 null（不产生
        /// 武器校验错误，也不装配玩家默认武器）。生产 Luban Provider 恒构造本目录，
        /// 因此生产路径的目录一定存在并被校验。</para>
        /// <para><b>结构校验：</b>拒绝空目录（MissingField）、未知类别（合法 0 Bow/
        /// 1 Spear/2 Knife/3 Sword，<see cref="BattleConfigErrorCategory.WeaponTypeUnknown"/>）、
        /// 负附加攻击力（<see cref="BattleConfigErrorCategory.WeaponConfigInvalid"/>）。
        /// 重复 id 由 <see cref="WeaponCatalogSnapshot"/> 构造保证（Provider 以
        /// WeaponIdDuplicate 报告），此处不重复扫描。</para>
        /// <para><b>启用集合校验（spec "Exactly four basic weapon definitions are
        /// enabled"）：</b>启用行必须恰好为 id ∈ {1, 11, 21, 32}，每行类别与 id 匹配
        /// （1→Bow、11→Spear、21→Knife、32→Sword）、handlerKey=Basic、AddAttackPower=1；
        /// 任一偏离即报告 <see cref="BattleConfigErrorCategory.WeaponEnabledSetInvalid"/>。
        /// 其余 40 行 disabled，MUST 不产生运行时状态。</para>
        /// <para>所有诊断包含武器 id 与字段名（路径形如 <c>Weapon.{id}.HandlerKey</c>），
        /// 不依赖运行时 fallback 掩盖缺失配置。</para>
        /// </remarks>
        private static void ValidateWeaponCatalog(
            BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            WeaponCatalogSnapshot catalog = snapshot.WeaponCatalog;
            if (catalog == null)
            {
                // 非必选（旧兼容路径）：null 不进入启动门禁，也不装配玩家默认武器。
                return;
            }

            if (catalog.Definitions.Count == 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingField,
                    "武器目录为空（没有任何武器定义）",
                    "WeaponCatalog.Definitions"));
                return;
            }

            var enabledIds = new HashSet<int>();
            IReadOnlyList<WeaponDefinitionSnapshot> definitions = catalog.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                WeaponDefinitionSnapshot def = definitions[i];
                string weaponPath = $"Weapon.{def.Id}";

                // 未知类别（合法 0 Bow/1 Spear/2 Knife/3 Sword，严格映射不 fallback）。
                if (!IsKnownWeaponType(def.Type))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WeaponTypeUnknown,
                        $"Weapon id={def.Id} 的 Type={def.Type} 未知（合法 0 Bow/1 Spear/2 Knife/3 Sword）",
                        $"{weaponPath}.Type"));
                }

                // 有限数值：附加攻击力非负。
                if (def.AddAttackPower < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WeaponConfigInvalid,
                        $"Weapon id={def.Id} 的附加攻击力={def.AddAttackPower} 为负",
                        $"{weaponPath}.AddAttackPower"));
                }

                if (def.Enabled)
                {
                    enabledIds.Add(def.Id);
                }
            }

            // 启用集合必须恰好为四条基础武器（id 1/11/21/32），不允许缺失或多余。
            for (int i = 0; i < RequiredWeaponEnabledIds.Length; i++)
            {
                int requiredId = RequiredWeaponEnabledIds[i];
                if (!enabledIds.Contains(requiredId))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WeaponEnabledSetInvalid,
                        $"基础武器启用集合缺少 id={requiredId}（首期必须恰好启用 1/11/21/32）",
                        $"Weapon.{requiredId}.Enabled"));
                }
            }

            foreach (int enabledId in enabledIds)
            {
                if (!IsRequiredWeaponEnabledId(enabledId))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WeaponEnabledSetInvalid,
                        $"额外启用非基础武器 id={enabledId}（首期仅 1/11/21/32 可启用，其余 40 行 MUST disabled）",
                        $"Weapon.{enabledId}.Enabled"));
                }
            }

            // 启用行必须为 Basic +1 且类别与 id 匹配。
            for (int i = 0; i < definitions.Count; i++)
            {
                WeaponDefinitionSnapshot def = definitions[i];
                if (!def.Enabled)
                {
                    continue;
                }

                string weaponPath = $"Weapon.{def.Id}";
                if (!string.Equals(def.HandlerKey, BasicWeaponResolver.BasicHandlerKey, StringComparison.Ordinal))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WeaponEnabledSetInvalid,
                        $"基础武器 id={def.Id} 的 handlerKey='{def.HandlerKey}' 不是 'Basic'",
                        $"{weaponPath}.HandlerKey"));
                }

                if (def.AddAttackPower != BasicWeaponResolver.BasicAttackPower)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WeaponEnabledSetInvalid,
                        $"基础武器 id={def.Id} 的附加攻击力={def.AddAttackPower} 不等于 1",
                        $"{weaponPath}.AddAttackPower"));
                }

                if (!IsExpectedWeaponTypeForId(def.Id, def.Type))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.WeaponEnabledSetInvalid,
                        $"基础武器 id={def.Id} 的类别={def.Type} 与期望不符（1→Bow、11→Spear、21→Knife、32→Sword）",
                        $"{weaponPath}.Type"));
                }
            }
        }

        /// <summary>首期启用基础武器 id 集合（1/11/21/32）。</summary>
        private static readonly int[] RequiredWeaponEnabledIds =
        {
            BasicWeaponResolver.BowWeaponId,
            BasicWeaponResolver.SpearWeaponId,
            BasicWeaponResolver.KnifeWeaponId,
            BasicWeaponResolver.CavalryWeaponId,
        };

        /// <summary>判断武器类别是否为已知业务类别（Bow/Spear/Knife/Sword）。</summary>
        private static bool IsKnownWeaponType(WeaponType type)
        {
            return type == WeaponType.Bow
                   || type == WeaponType.Spear
                   || type == WeaponType.Knife
                   || type == WeaponType.Sword;
        }

        /// <summary>判断 id 是否为首期启用基础武器 id。</summary>
        private static bool IsRequiredWeaponEnabledId(int id)
        {
            for (int i = 0; i < RequiredWeaponEnabledIds.Length; i++)
            {
                if (RequiredWeaponEnabledIds[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>判断基础武器 id 的类别是否匹配（显式映射，不比较枚举原始数值）。</summary>
        private static bool IsExpectedWeaponTypeForId(int id, WeaponType type)
        {
            switch (id)
            {
                case BasicWeaponResolver.BowWeaponId:
                    return type == WeaponType.Bow;
                case BasicWeaponResolver.SpearWeaponId:
                    return type == WeaponType.Spear;
                case BasicWeaponResolver.KnifeWeaponId:
                    return type == WeaponType.Knife;
                case BasicWeaponResolver.CavalryWeaponId:
                    return type == WeaponType.Sword;
                default:
                    return false;
            }
        }

        // ====================================================================
        // 12. 运行能力校验（spec "Reject Boss content when the capability is absent"）
        // ====================================================================

        /// <summary>
        /// Boss 波能力 gate：所选计划含 Boss 行时，启动事务必须声明支持 Boss 波且
        /// 支持对应 bossKey；否则在世界加载前拒绝（不得静默跳过或降级）。
        /// </summary>
        private static void ValidateBossCapabilities(
            BattleConfigSnapshot snapshot,
            BattleRuntimeCapabilities capabilities,
            List<BattleConfigValidationError> errors)
        {
            OrderedWavePlanSnapshot plan = snapshot.OrderedWavePlan;
            if (plan == null)
            {
                return;
            }

            BattleRuntimeCapabilities effective = capabilities ?? BattleRuntimeCapabilities.None;
            IReadOnlyList<WavePlanEntry> rows = plan.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                WavePlanEntry row = rows[i];
                if (row.Kind != WavePlanKind.Boss)
                {
                    continue;
                }

                string rowPath = $"WavePlan.{row.Order}";
                if (!effective.SupportsBossWaves)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossCapabilityUnsupported,
                        $"计划 '{plan.ActivePlanId}' 含 Boss 行（order={row.Order}），" +
                        "但当前运行时不支持 Boss 波",
                        $"{rowPath}.Kind"));
                    continue;
                }

                if (!!row.BossId.HasValue && !effective.SupportsBossId(row.BossId.Value))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.BossCapabilityUnsupported,
                        $"计划 '{plan.ActivePlanId}' Boss 行（order={row.Order}）引用了未注册的 " +
                        $"bossKey='{row.BossId}'",
                        $"{rowPath}.BossId"));
                }
            }
        }

        // ====================================================================
        // 13. 选中地图行的严格地图校验（task 2.4 / design.md 决策 5）
        // ====================================================================

        /// <summary>
        /// 校验生产选中的地图运行快照：身份、资源、格子尺寸、路径连续性、双路入口、
        /// 路径标记坐标域与敌人类型索引。
        /// </summary>
        /// <param name="map">由生产 Provider 完整消费选中行得到的 <see cref="MapData"/>。</param>
        /// <param name="expectedMapId">装载信息中的预期 MapId，地图身份必须与其完全相等
        /// （spec "MapId is the sole battle map selector"）。</param>
        /// <returns>结构化校验结果。通过时 ErrorCode 为 None。</returns>
        /// <remarks>
        /// <para><b>调用点：</b>仅由启动配置准备（<see cref="BattleStartupContext.Prepare"/>）
        /// 在创建 Runtime 前调用（spec "Map configuration is validated before runtime
        /// creation"）。兼容 map0 的旧快照（JSON 黄金夹具/旧构造入口）不经过本方法，
        /// 因此不改变旧入口行为。</para>
        /// <para><b>坐标域（design.md 决策 5）：</b></para>
        /// <list type="bullet">
        /// <item>格子和路径坐标：0 &lt;= x &lt; Width、0 &lt;= y &lt; Height。</item>
        /// <item>routeMarker 表现坐标：0 &lt;= x &lt;= Width、0 &lt;= y &lt;= Height，
        /// 允许落在最外侧网格线上（spec "Validate route markers in marker space"）。</item>
        /// </list>
        /// <para><b>路径规则：</b>非空、首尾等于配置起终点、相邻点曼哈顿距离为 1、
        /// 落在对应阵营可通行格；Entry 在格子域内并与 Start 曼哈顿相邻。</para>
        /// <para>grid 形状与格子编码（"kind_lane"）由
        /// <see cref="LubanBattleConfigProvider"/> 在规范化前校验（原始编码不在快照中）。</para>
        /// </remarks>
        internal static BattleConfigValidationResult ValidateMapConfig(MapData map, int expectedMapId)
        {
            if (map == null)
            {
                return new BattleConfigValidationResult(new[]
                {
                    new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingSection,
                        "地图配置缺失",
                        "Map"),
                });
            }

            var errors = new List<BattleConfigValidationError>();

            // 地图身份（spec "MapId is the sole battle map selector"）：MapIndex 必须与预期 MapId 完全相等。
            if (map.MapIndex != expectedMapId)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapIdentity,
                    $"地图身份 MapIndex={map.MapIndex} 与预期 MapId={expectedMapId} 不一致",
                    "Map.MapIndex"));
            }

            // 资源与呈现数据（spec "Reject unusable presentation data"）。
            // 空名称/资源地址属于字段非法（ConfigInvalid），不是缺字段（ConfigMissing）。
            if (string.IsNullOrEmpty(map.Name))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapPresentation,
                    "地图名称为空",
                    "Map.Name"));
            }

            if (string.IsNullOrEmpty(map.ResourceAddress))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapPresentation,
                    "地图世界资源地址为空",
                    "Map.ResourceAddress"));
            }

            if (map.CellWidth <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapSize,
                    $"地图格子像素宽={map.CellWidth} 非正",
                    "Map.CellWidth"));
            }

            if (map.CellHeight <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapSize,
                    $"地图格子像素高={map.CellHeight} 非正",
                    "Map.CellHeight"));
            }

            if (map.EnemyTypeIndex < 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.EnemyTypeIndexInvalid,
                    $"地图敌人类型索引={map.EnemyTypeIndex} 为负",
                    "Map.EnemyTypeIndex"));
            }

            // 双路路径（spec "Reject malformed grid or path data"）。
            ValidateSelectedMapPath(map, map.GetPlayerPath(), map.PlayerStart, map.PlayerEnd, "Player", errors);
            ValidateSelectedMapPath(map, map.GetOpponentPath(), map.OpponentStart, map.OpponentEnd, "Opponent", errors);

            // 双路入口（design.md 决策 5：Entry 在格子域内并与 Start 曼哈顿相邻）。
            ValidateSelectedMapEntry(map, map.PlayerEntry, map.PlayerStart, "Player", errors);
            ValidateSelectedMapEntry(map, map.OpponentEntry, map.OpponentStart, "Opponent", errors);

            // 表现层路径标记（spec "Validate route markers in marker space"）。
            ValidateSelectedMapMarkers(map, errors);

            return new BattleConfigValidationResult(errors);
        }

        /// <summary>
        /// 校验单条地图路径：非空、首尾等于起终点、连续、越界或不可通行。
        /// </summary>
        private static void ValidateSelectedMapPath(
            MapData map,
            IReadOnlyList<GridPosition> path,
            GridPosition start,
            GridPosition end,
            string sideLabel,
            List<BattleConfigValidationError> errors)
        {
            if (path == null || path.Count == 0)
            {
                // 选中的地图行路径为空属于字段非法（ConfigInvalid），不是缺表（ConfigMissing）。
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidMapPath,
                    $"{sideLabel} 路径为空",
                    $"Map.{sideLabel}Path"));
                return;
            }

            if (path[0] != start)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.PathEndpointMismatch,
                    $"{sideLabel} 路径首点 {path[0]} 不等于起点 {start}",
                    $"Map.{sideLabel}Path"));
            }

            if (path[path.Count - 1] != end)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.PathEndpointMismatch,
                    $"{sideLabel} 路径末点 {path[path.Count - 1]} 不等于终点 {end}",
                    $"Map.{sideLabel}Path"));
            }

            for (int i = 0; i < path.Count; i++)
            {
                GridPosition p = path[i];

                if (i > 0)
                {
                    GridPosition prev = path[i - 1];
                    int manhattan = Math.Abs(prev.X - p.X) + Math.Abs(prev.Y - p.Y);
                    if (manhattan != 1)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.PathDiscontinuous,
                            $"{sideLabel} 路径点[{i - 1}] {prev} 与 [{i}] {p} 不相邻（曼哈顿距离={manhattan}）",
                            $"Map.{sideLabel}Path[{i}]"));
                    }
                }

                if (!map.IsInside(p))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.PathOutOfBounds,
                        $"{sideLabel} 路径点[{i}] ({p.X},{p.Y}) 越界（width={map.Width} height={map.Height}）",
                        $"Map.{sideLabel}Path[{i}]"));
                    continue;
                }

                // 游戏路径只允许经过通道格（kind=0）；落在可建造/阻挡格视为表现标记误用。
                GridCell cell = map.GetCell(p);
                if (cell.Kind != GridCellKind.Passage)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.RouteMarkerMismatch,
                        $"{sideLabel} 路径点[{i}] ({p.X},{p.Y}) 落在 {cell.Kind} 格上，应为 Passage(通道)",
                        $"Map.{sideLabel}Path[{i}]"));
                }
            }
        }

        /// <summary>
        /// 校验单侧入口：在格子域内、与对应起点曼哈顿相邻且落在通道格上。
        /// </summary>
        private static void ValidateSelectedMapEntry(
            MapData map,
            GridPosition entry,
            GridPosition start,
            string sideLabel,
            List<BattleConfigValidationError> errors)
        {
            if (!map.IsInside(entry))
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.EntryInvalid,
                    $"{sideLabel} 入口 {entry} 越界（width={map.Width} height={map.Height}）",
                    $"Map.{sideLabel}Entry"));
                return;
            }

            int manhattan = Math.Abs(entry.X - start.X) + Math.Abs(entry.Y - start.Y);
            if (manhattan != 1)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.EntryInvalid,
                    $"{sideLabel} 入口 {entry} 与起点 {start} 曼哈顿距离={manhattan}，应相邻",
                    $"Map.{sideLabel}Entry"));
            }

            GridCell cell = map.GetCell(entry);
            if (cell.Kind != GridCellKind.Passage)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.EntryInvalid,
                    $"{sideLabel} 入口 {entry} 落在 {cell.Kind} 格上，应为 Passage(通道)",
                    $"Map.{sideLabel}Entry"));
            }
        }

        /// <summary>
        /// 校验表现层路径标记全部落在 marker 坐标域（0..Width × 0..Height）。
        /// </summary>
        private static void ValidateSelectedMapMarkers(
            MapData map,
            List<BattleConfigValidationError> errors)
        {
            IReadOnlyList<GridPosition> markers = map.RouteMarkers;
            if (markers == null || markers.Count == 0)
            {
                return;
            }

            for (int i = 0; i < markers.Count; i++)
            {
                GridPosition marker = markers[i];
                if (marker.X < 0 || marker.X > map.Width
                    || marker.Y < 0 || marker.Y > map.Height)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.RouteMarkerOutOfBounds,
                        $"路径标记[{i}] ({marker.X},{marker.Y}) 超出 marker 坐标域 " +
                        $"(0..{map.Width} × 0..{map.Height})",
                        $"Map.RouteMarkers[{i}]"));
                }
            }
        }
    }
}
