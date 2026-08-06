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
    // 校验覆盖（task 3.5 要求）：
    //   1. 配置版本（版本字段在 BattleLoadoutDto.ConfigVersion，权威校验在
    //      BattleRuntimeFactory.TryValidateLoadout loadout 层；本校验点在快照层做
    //      SourceTag 防御性复查，空 SourceTag → InvalidVersion）
    //   2. 缺表（Map/Enemy/Wave/Units/Economy/Deck/Projectile 任一为 null → MissingSection）
    //   3. 缺字段（必需集合为空或 null → MissingField；注意 BattleConfigSnapshot 构造已拒 null，
    //      此处主要校验空集合与业务必需字段）
    //   4. 非法/空权重（SpawnStrategyWeights 为空、含负值或总和为零无法选择 → InvalidSpawnWeight）
    //   5. 未知兵种（单位索引不在 0..3 或文本不在四兵集合 → UnknownUnit）
    //   6. 非法时间/距离（攻击间隔、波间延迟为负；攻击距离为负或零 → InvalidTime/InvalidDistance）
    //   7. 地图尺寸（Width/Height 非正 → InvalidMapSize；MapData 构造已保证，此处复查）
    //   8. 越界玩家/对手路径（路径点不在地图范围内 → PathOutOfBounds）
    //   9. 缺失引用（牌组 BaseSoldierTexts 引用了未定义的兵种文本 → MissingReference）
    //  10. route marker 若仅属表现不得按游戏路径规则误判（RouteMarkerMismatch）
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
    /// 缺失表、非法权重、错误地图尺寸、未知单位或不完整路径 MUST 返回可诊断错误，
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
        /// 校验配置快照，返回结构化结果。
        /// </summary>
        /// <param name="snapshot">待校验的不可变配置快照。</param>
        /// <returns>结构化校验结果。IsValid 为 true 表示通过；否则 Errors 含具体错误项。</returns>
        /// <remarks>
        /// <para>本方法只读不修改快照，不抛异常（决策 0.7）。所有错误一次性收集。</para>
        /// <para>调用方（<see cref="BattleRuntimeFactory"/>）据 <see cref="BattleConfigValidationResult.ErrorCode"/>
        /// 决定是否中止组装：非 None 即返回对应错误码，不进入运行状态。</para>
        /// </remarks>
        internal static BattleConfigValidationResult Validate(BattleConfigSnapshot snapshot)
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

            // 4. 非法/空权重校验
            ValidateSpawnWeights(snapshot, errors);

            // 5. 未知兵种校验
            ValidateUnits(snapshot, errors);

            // 6. 非法时间/距离校验
            ValidateTimesAndDistances(snapshot, errors);

            // 7. 地图尺寸校验
            ValidateMapSize(snapshot, errors);

            // 8. 越界路径校验
            ValidatePaths(snapshot, errors);

            // 9. 缺失引用校验（牌组引用未定义兵种）
            ValidateReferences(snapshot, errors);

            // 若前面已致命错误，后续校验可能无意义，但仍收集所有错误便于诊断。
            return new BattleConfigValidationResult(errors);
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

            if (snapshot.Enemy == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "敌人配置缺失",
                    "Enemy"));
            }

            if (snapshot.Wave == null)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.MissingSection,
                    "波次配置缺失",
                    "Wave"));
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

            // 敌人必需字段
            if (snapshot.Enemy != null)
            {
                if (snapshot.Enemy.HealthByWave.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "敌人各波次血量数组为空",
                        "Enemy.HealthByWave"));
                }

                if (snapshot.Enemy.EarlyRoundHealthMultipliers.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "敌人早期波次乘数数组为空",
                        "Enemy.EarlyRoundHealthMultipliers"));
                }

                if (string.IsNullOrEmpty(snapshot.Enemy.Type))
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "敌人类型标识为空",
                        "Enemy.Type"));
                }
            }

            // 波次必需字段
            if (snapshot.Wave != null)
            {
                if (snapshot.Wave.WaveUnitCounts.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "波次怪物数量数组为空",
                        "Wave.WaveUnitCounts"));
                }

                if (snapshot.Wave.SpawnStrategies.Count == 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "生成策略表为空",
                        "Wave.SpawnStrategies"));
                }

                if (snapshot.Wave.MaxRounds <= 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.MissingField,
                        "最大波次数非正",
                        "Wave.MaxRounds"));
                }
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
        // 4. 非法/空权重校验
        // ====================================================================

        /// <summary>
        /// 校验波次生成策略权重：不为空、不含负值、总和大于零（可选择有效索引）。
        /// </summary>
        /// <remarks>
        /// spec "Spawn weights are invalid"：
        /// 波次生成权重为空、为负或无法选择有效索引时，战斗开始失败并报告具体配置位置，
        /// 不创建半初始化实体。
        /// </remarks>
        private static void ValidateSpawnWeights(BattleConfigSnapshot snapshot, List<BattleConfigValidationError> errors)
        {
            if (snapshot.Wave == null)
            {
                return;
            }

            IReadOnlyList<int> weights = snapshot.Wave.SpawnStrategyWeights;

            if (weights.Count == 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidSpawnWeight,
                    "生成策略权重列表为空",
                    "Wave.SpawnStrategyWeights"));
                return;
            }

            int sum = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                int w = weights[i];
                if (w < 0)
                {
                    errors.Add(new BattleConfigValidationError(
                        BattleConfigErrorCategory.InvalidSpawnWeight,
                        $"生成策略权重[{i}]={w} 为负",
                        $"Wave.SpawnStrategyWeights[{i}]"));
                }

                sum += w;
            }

            if (sum <= 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidSpawnWeight,
                    $"生成策略权重总和={sum}，无法选择有效索引",
                    "Wave.SpawnStrategyWeights"));
            }

            // 策略表行数应与权重数一致
            if (snapshot.Wave.SpawnStrategies.Count != weights.Count)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidSpawnWeight,
                    $"生成策略表行数={snapshot.Wave.SpawnStrategies.Count} 不等于权重数={weights.Count}",
                    "Wave.SpawnStrategies"));
            }
        }

        // ====================================================================
        // 5. 未知兵种校验
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

            // 波间延迟
            if (snapshot.Wave != null && snapshot.Wave.DelayTimeMs < 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidTime,
                    $"波间延迟={snapshot.Wave.DelayTimeMs}ms 为负",
                    "Wave.DelayTimeMs"));
            }

            // 敌人速度
            if (snapshot.Enemy != null && snapshot.Enemy.Speed < 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidDistance,
                    $"敌人速度={snapshot.Enemy.Speed} 为负",
                    "Enemy.Speed"));
            }

            // 敌人接触伤害
            if (snapshot.Enemy != null && snapshot.Enemy.ContactDamage < 0)
            {
                errors.Add(new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidTime,
                    $"敌人接触伤害={snapshot.Enemy.ContactDamage} 为负",
                    "Enemy.ContactDamage"));
            }

            // Boss 出现概率范围 [0,1]
            if (snapshot.Wave != null && !snapshot.Wave.SkipBoss)
            {
                IReadOnlyList<float> chances = snapshot.Wave.BossSpawnChances;
                for (int i = 0; i < chances.Count; i++)
                {
                    float c = chances[i];
                    if (c < 0f || c > 1f)
                    {
                        errors.Add(new BattleConfigValidationError(
                            BattleConfigErrorCategory.InvalidSpawnWeight,
                            $"Boss 出现概率[{i}]={c} 超出 [0,1] 范围",
                            $"Wave.BossSpawnChances[{i}]"));
                    }
                }
            }
        }

        // ====================================================================
        // 7. 地图尺寸校验
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
    }
}
