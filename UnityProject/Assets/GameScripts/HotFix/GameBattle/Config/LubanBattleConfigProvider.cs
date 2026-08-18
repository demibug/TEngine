using System;
using System.Collections.Generic;
using GameConfig;
using GameConfig.battle;
using UnityEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.3/2.4/2.5：LubanBattleConfigProvider —— Luban 生产配置 Provider
    // ----------------------------------------------------------------------------
    // 职责（design.md decision 0.6 / 决策 2 / specs/battle-config-snapshot/spec.md
    //   "Configuration source preserves equivalent values"）：
    //   从 Luban Tables 读取配置并复制为不可变业务快照。生产入口只使用本 Provider。
    //
    // 本 change（tasks 2.4/2.5）新增两个新生产链权威来源：
    //   - 敌人目录：按 enemyKey 成对关联 TbEnemy（目录）与 TbEnemyStats（数值），
    //     禁止 TbEnemy.DataList[0]、固定 speed/contact/reward 与 FallbackHealthByWave。
    //   - 有序波次计划：按 map.ActivePlanId 精确选择 TbWavePlan 中同 planId 行并按
    //     Order 排序，仅保留被所选计划显式引用的 spawnStrategies。
    //   Normal 行 enemyKey 为空时按地图 EnemyTypeIndex 从目录解析；未知 key/typeIndex、
    //   重复/不连续 order、非法时序/计数/车道/profile 等以 BattleConfigDataException
    //   抛给启动层转换为结构化校验错误，不静默 fallback。
    //
    // 【legacy】旧 EnemyConfigSnapshot/WaveConfigSnapshot 仍被 WaveManager/BattleManager
    // 消费，为兼容继续产出；它们不再是新生产链权威（tasks 3.x/4.x 移除消费后淘汰）。
    //
    // 不变量：
    //   1. 生产入口只使用本 Provider。
    //   2. 缺失字段在 MissingFieldNotes 中明确标注，不静默补默认值。
    //   3. 业务快照不泄漏 Luban 集合：目录/计划在 BattleConfigDomain 层深拷贝为只读。
    //   4. 地图坐标只在适配层读取一次。
    // ============================================================================

    /// <summary>
    /// Luban 生产配置 Provider，从 Luban Tables 读取配置并适配现有表结构。
    /// </summary>
    /// <remarks>
    /// <para><b>决策 0.6：</b>Luban 为最终生产配置源，生产入口只使用本 Provider。
    /// JSON Provider 仅编入测试或开发验证边界。</para>
    ///
    /// <para><b>新生产链（tasks 2.4/2.5）：</b><see cref="GetSnapshot(int)"/> 同时产出
    /// <see cref="BattleConfigSnapshot.EnemyCatalog"/>（TbEnemy × TbEnemyStats 成对目录）
    /// 与 <see cref="BattleConfigSnapshot.OrderedWavePlan"/>（activePlanId 精确选择的逐波计划），
    /// 作为后续波次/敌人运行的权威配置形态。</para>
    ///
    /// <para><b>结构化错误：</b>配置无法构建合法快照时抛 <see cref="BattleConfigDataException"/>，
    /// 由 <see cref="BattleStartupContext.Prepare"/> 转换为可由
    /// <see cref="BattleConfigValidator"/> 表达的结构化错误并在世界加载前阻断。</para>
    ///
    /// <para><b>应用级 ConfigSystem 持有配置数据（task 3.4 / decision 0.11）：</b>
    /// 本 Provider 从已加载的 <see cref="Tables"/> 复制快照，不在模拟子步加载 TextAsset，
    /// 也不由 BattleRuntime 卸载应用级配置资源。</para>
    /// </remarks>
    public sealed class LubanBattleConfigProvider : IBattleConfigProvider
    {
        // ====================================================================
        // 持有的 Luban Tables 引用
        // ====================================================================

        private readonly Tables _tables;

        /// <summary>
        /// 构造 Luban 配置 Provider。
        /// </summary>
        /// <param name="tables">已加载的 Luban Tables（由应用级 ConfigSystem 持有，不从 BattleRuntime 卸载）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="tables"/> 为 null。</exception>
        public LubanBattleConfigProvider(Tables tables)
        {
            _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        }

        // ====================================================================
        // IBattleConfigProvider 实现
        // ====================================================================

        /// <summary>
        /// 获取 map0 适配快照（兼容旧调用方/测试夹具的显式入口）。
        /// </summary>
        /// <returns>规范化后的不可变配置快照，缺失字段在 MissingFieldNotes 中标注。</returns>
        /// <remarks>
        /// <para>本无参入口只保留为现有夹具/兼容调用的 map0 adapter（design.md 决策 2），
        /// 等价于 <see cref="GetSnapshot(int)"/> 传入 MapId 0。生产启动路径必须使用
        /// <see cref="GetSnapshot(int)"/> 按 Loadout.MapId 精确选择，禁止隐式 map0。</para>
        /// </remarks>
        public BattleConfigSnapshot GetSnapshot()
        {
            return GetSnapshot(mapId: 0);
        }

        /// <summary>
        /// 按地图标识精确获取本局配置快照（生产入口）。
        /// </summary>
        /// <param name="mapId">装载信息中的地图标识。</param>
        /// <returns>规范化后的不可变配置快照，缺失字段在 MissingFieldNotes 中标注。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="mapId"/> 为负。</exception>
        /// <exception cref="BattleMapConfigMissingException">MapId 对应的地图行不存在。</exception>
        /// <exception cref="InvalidOperationException">地图行字段非法（grid 形状/编码不一致等）。</exception>
        /// <exception cref="BattleConfigDataException">敌人目录/波次计划无法构建合法业务快照。</exception>
        /// <remarks>
        /// <para>只使用 <paramref name="mapId"/> 选择本局地图行（spec "MapId is the sole
        /// battle map selector"），不得回退 map0。选中行经完整规范化入口消费
        /// Name/ResourceAddress/CellWidth/CellHeight/Grid/双路入口与路径/RouteMarkers/
        /// EnemyTypeIndex，一次启动只解析并校验一份快照。</para>
        /// </remarks>
        public BattleConfigSnapshot GetSnapshot(int mapId)
        {
            if (mapId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mapId), $"地图 MapId 为负：{mapId}");
            }

            var missingNotes = new List<string>();

            // ----------------------------------------------------------------
            // 地图：按 MapId 精确选择，缺行抛 BattleMapConfigMissingException。
            // ----------------------------------------------------------------
            TbMap tbMap = _tables.TbMap;
            Map mapRow = tbMap.GetOrDefault(mapId);
            if (mapRow == null)
            {
                throw new BattleMapConfigMissingException(mapId);
            }

            MapData map = NormalizeMapFromLuban(mapRow, missingNotes);

            // ----------------------------------------------------------------
            // 敌人目录（新生产链权威）：TbEnemy × TbEnemyStats 按 enemyKey 成对关联。
            // ----------------------------------------------------------------
            EnemyCatalogSnapshot catalog = BuildCatalogFromLuban(_tables.TbEnemy, _tables.TbEnemyStats);

            // ----------------------------------------------------------------
            // 有序波次计划（新生产链权威）：activePlanId 精确选择 + Order 升序。
            // ----------------------------------------------------------------
            OrderedWavePlanSnapshot plan = BuildPlanFromLuban(
                _tables.TbWave, _tables.TbWavePlan, catalog, mapRow.EnemyTypeIndex);

            // ----------------------------------------------------------------
            // Buff 目录（新生产链权威）：TbBuff 行显式消费为领域目录。
            // ----------------------------------------------------------------
            BuffCatalogSnapshot buffCatalog = BuildBuffCatalogFromLuban(_tables.TbBuff);

            // ----------------------------------------------------------------
            // Skill 目录（新生产链权威）：TbSkill 行显式消费为领域目录。
            // ----------------------------------------------------------------
            SkillCatalogSnapshot skillCatalog = BuildSkillCatalogFromLuban(_tables.TbSkill);

            // ----------------------------------------------------------------
            // Boss 目录（本 change 起权威）：TbBoss 行显式消费为领域目录。
            // ----------------------------------------------------------------
            BossCatalogSnapshot bossCatalog = BuildBossCatalogFromLuban(_tables.TbBoss);

            // ----------------------------------------------------------------
            // 武器目录（本 change 起权威）：TbWeapon 行显式消费为领域目录。
            // ----------------------------------------------------------------
            WeaponCatalogSnapshot weaponCatalog = BuildWeaponCatalogFromLuban(_tables.TbWeapon);

            // ----------------------------------------------------------------
            // 【legacy】单敌人快照：由目录按地图 EnemyTypeIndex 派生（兼容 WaveManager）。
            // ----------------------------------------------------------------
            EnemyConfigSnapshot enemy = BuildLegacyEnemyFromCatalog(catalog, mapRow.EnemyTypeIndex);

            // ----------------------------------------------------------------
            // 【legacy】并行数组式波次快照（兼容 WaveManager/BattleManager）。
            // ----------------------------------------------------------------
            WaveConfigSnapshot wave = NormalizeWaveFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 单位
            // ----------------------------------------------------------------
            IReadOnlyList<UnitConfigSnapshot> units = NormalizeUnitsFromLuban();

            // ----------------------------------------------------------------
            // 单位等级
            // ----------------------------------------------------------------
            UnitLevelConfigSnapshot unitLevel = NormalizeUnitLevelFromLuban();

            // ----------------------------------------------------------------
            // 经济
            // ----------------------------------------------------------------
            EconomyConfigSnapshot economy = NormalizeEconomyFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 牌组（Luban 无 deck 表）
            // ----------------------------------------------------------------
            DeckConfigSnapshot deck = NormalizeDeckFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 投射物
            // ----------------------------------------------------------------
            ProjectileConfigSnapshot projectile = NormalizeProjectileFromLuban(missingNotes);

            return new BattleConfigSnapshot(
                map: map,
                enemy: enemy,
                wave: wave,
                units: units,
                unitLevel: unitLevel,
                economy: economy,
                deck: deck,
                projectile: projectile,
                missingFieldNotes: missingNotes,
                sourceTag: "Luban",
                enemyCatalog: catalog,
                orderedWavePlan: plan,
                buffCatalog: buffCatalog,
                skillCatalog: skillCatalog,
                bossCatalog: bossCatalog,
                weaponCatalog: weaponCatalog);
        }

        // ====================================================================
        // 地图适配（task 2.5 保持不变）
        // ====================================================================

        /// <summary>
        /// 从 Luban 选中的地图行适配完整地图运行快照。
        /// </summary>
        /// <param name="mapRow">按 MapId 精确选中的地图行。</param>
        /// <param name="missingNotes">缺失字段标注列表。</param>
        /// <returns>完整消费选中行的不可变 <see cref="MapData"/>。</returns>
        /// <remarks>
        /// <para>生产路径不再使用 map0 grid/端点/enemyTypeIndex fallback（task 2.5）：
        /// 完整读取选中行的 Grid、双路坐标与路径、RouteMarkers、EnemyTypeIndex、
        /// CellWidth/CellHeight、Name/ResourceAddress。</para>
        /// </remarks>
        private MapData NormalizeMapFromLuban(Map mapRow, List<string> missingNotes)
        {
            if (mapRow == null)
            {
                throw new ArgumentNullException(nameof(mapRow));
            }

            if (mapRow.Grid == null || mapRow.Grid.Count == 0)
            {
                throw new InvalidOperationException(
                    $"MapId={mapRow.MapIndex} 的 Grid 为空，无法构建地图格子");
            }

            ValidateGridShapeAndCodes(mapRow);

            return BattleConfigNormalizer.NormalizeMap(
                ConvertGrid(mapRow.Grid),
                mapIndex: mapRow.MapIndex,
                playerStart: ToGridPosition(mapRow.PlayerStart),
                playerEnd: ToGridPosition(mapRow.PlayerEnd),
                opponentStart: ToGridPosition(mapRow.OpponentStart),
                opponentEnd: ToGridPosition(mapRow.OpponentEnd),
                playerPath: ConvertPath(mapRow.PlayerPath),
                opponentPath: ConvertPath(mapRow.OpponentPath),
                name: mapRow.Name,
                resourceAddress: mapRow.ResourceAddress,
                cellWidth: mapRow.CellWidth,
                cellHeight: mapRow.CellHeight,
                playerEntry: ToGridPosition(mapRow.PlayerEntry),
                opponentEntry: ToGridPosition(mapRow.OpponentEntry),
                routeMarkers: ConvertMarkers(mapRow.RouteMarkers),
                enemyTypeIndex: mapRow.EnemyTypeIndex);
        }

        /// <summary>
        /// 校验选中地图行的 grid 形状与格子编码。
        /// </summary>
        private static void ValidateGridShapeAndCodes(Map mapRow)
        {
            if (mapRow.Grid.Count != mapRow.Width)
            {
                throw new InvalidOperationException(
                    $"MapId={mapRow.MapIndex} Grid 列数={mapRow.Grid.Count} 不等于 Width={mapRow.Width}");
            }

            for (int x = 0; x < mapRow.Grid.Count; x++)
            {
                System.Collections.Generic.List<string> column = mapRow.Grid[x];
                if (column == null || column.Count != mapRow.Height)
                {
                    throw new InvalidOperationException(
                        $"MapId={mapRow.MapIndex} Grid 列[{x}] 行数={(column?.Count ?? 0)} 不等于 Height={mapRow.Height}");
                }

                for (int y = 0; y < column.Count; y++)
                {
                    string code = column[y];
                    if (string.IsNullOrEmpty(code))
                    {
                        throw new InvalidOperationException(
                            $"MapId={mapRow.MapIndex} Grid[{x}][{y}] 格子编码为空");
                    }

                    string[] parts = code.Split('_');
                    if (parts.Length != 2
                        || !int.TryParse(parts[0], out int kind)
                        || kind < 0 || kind > 2
                        || !int.TryParse(parts[1], out int lane)
                        || lane < 0 || lane > 1)
                    {
                        throw new InvalidOperationException(
                            $"MapId={mapRow.MapIndex} Grid[{x}][{y}] 编码 '{code}' 非法，" +
                            "应为严格两分段 'kind_lane'（kind ∈ {0,1,2}，lane ∈ {0,1}）");
                    }
                }
            }
        }

        /// <summary>
        /// 把 Luban List&lt;List&lt;string&gt;&gt; Grid 转换为只读列优先网格。
        /// </summary>
        private static IReadOnlyList<IReadOnlyList<string>> ConvertGrid(
            System.Collections.Generic.List<System.Collections.Generic.List<string>> grid)
        {
            var result = new IReadOnlyList<string>[grid.Count];
            for (int x = 0; x < grid.Count; x++)
            {
                result[x] = grid[x];
            }

            return result;
        }

        // ====================================================================
        // 敌人目录适配（task 2.4）
        // ====================================================================

        /// <summary>
        /// 从 TbEnemy（目录）+ TbEnemyStats（数值）按 enemyKey 成对构建敌人目录。
        /// </summary>
        /// <param name="tbEnemy">敌人目录表（含 typeIndex/resourceAddress）。</param>
        /// <param name="tbEnemyStats">敌人数值表（速度/血量曲线/接触伤害/奖励）。</param>
        /// <returns>按 enemyKey/typeIndex 双索引的不可变敌人目录。</returns>
        /// <exception cref="BattleConfigDataException">普通敌人缺少 EnemyStats 行或
        /// typeIndex 冲突（无法构建合法双索引目录）。</exception>
        /// <remarks>
        /// <para>spec "Enemy configuration is a keyed immutable catalog"：目录与数值必须成对；
        /// 技能召唤类（TypeIndex 为 null）不获得普通敌人 typeIndex，不进入普通波目录。</para>
        /// <para>禁止 TbEnemy.DataList[0]、固定 speed/contact/reward 与 FallbackHealthByWave。</para>
        /// </remarks>
        internal static EnemyCatalogSnapshot BuildCatalogFromLuban(TbEnemy tbEnemy, TbEnemyStats tbEnemyStats)
        {
            if (tbEnemy == null)
            {
                throw new ArgumentNullException(nameof(tbEnemy));
            }

            if (tbEnemyStats == null)
            {
                throw new ArgumentNullException(nameof(tbEnemyStats));
            }

            var definitions = new List<EnemyDefinitionSnapshot>();
            IReadOnlyList<Enemy> enemies = tbEnemy.DataList;
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (!enemy.TypeIndex.HasValue)
                {
                    // 技能召唤类等不获得普通敌人 typeIndex，不进入普通波目录（design.md 决策 2）。
                    continue;
                }

                EnemyStats stats = tbEnemyStats.GetOrDefault(enemy.Key);
                if (stats == null)
                {
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.EnemyStatsMissing,
                        $"普通敌人 enemyKey='{enemy.Key}'（typeIndex={enemy.TypeIndex.Value}）" +
                        "缺少对应的 EnemyStats 行，敌人目录与数值必须成对关联",
                        $"Enemy.{enemy.Key}.EnemyStats");
                }

                definitions.Add(new EnemyDefinitionSnapshot(
                    typeIndex: enemy.TypeIndex.Value,
                    key: enemy.Key,
                    resourceAddress: enemy.ResourceAddress ?? string.Empty,
                    moveSpeed: stats.MoveSpeed,
                    healthByWave: stats.HealthByWave,
                    earlyRoundHealthMultipliers: stats.EarlyRoundHealthMultipliers,
                    contactDamage: stats.ContactDamage,
                    rewardGold: stats.RewardGold));
            }

            try
            {
                return new EnemyCatalogSnapshot(definitions);
            }
            catch (ArgumentException ex)
            {
                // 重复 key/typeIndex 无法构建合法双索引目录，转为结构化配置错误。
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.EnemyTypeIndexConflict,
                    $"敌人目录构建失败：{ex.Message}",
                    "EnemyCatalog");
            }
        }

        /// <summary>
        /// 【legacy】由敌人目录按地图 EnemyTypeIndex 派生单敌人快照。
        /// </summary>
        private static EnemyConfigSnapshot BuildLegacyEnemyFromCatalog(
            EnemyCatalogSnapshot catalog, int mapEnemyTypeIndex)
        {
            if (catalog.TryGetByTypeIndex(mapEnemyTypeIndex, out EnemyDefinitionSnapshot def))
            {
                return new EnemyConfigSnapshot(
                    type: def.Key,
                    mapEnemyTypeIndex: mapEnemyTypeIndex,
                    speed: def.MoveSpeed,
                    healthByWave: def.HealthByWave,
                    earlyRoundHealthMultipliers: def.EarlyRoundHealthMultipliers,
                    contactDamage: def.ContactDamage);
            }

            throw new BattleConfigDataException(
                BattleConfigErrorCategory.EnemyTypeIndexUnknown,
                $"地图默认敌人索引 typeIndex={mapEnemyTypeIndex} 无法在敌人目录中解析",
                "Map.EnemyTypeIndex");
        }

        // ====================================================================
        // 波次计划适配（task 2.5）
        // ====================================================================

        /// <summary>
        /// 从 TbWave（activePlanId 选择器）+ TbWavePlan（逐行内容）构建有序波次计划。
        /// </summary>
        /// <param name="tbWave">波次总配置（ActivePlanId + spawnStrategies profile 数据）。</param>
        /// <param name="tbWavePlan">逐波计划表。</param>
        /// <param name="catalog">敌人目录（用于解析空 enemyKey）。</param>
        /// <param name="mapEnemyTypeIndex">本图敌人类型索引（Normal 行空 enemyKey 的默认解析来源）。</param>
        /// <returns>按 Order 升序、仅保留显式引用 profile 的不可变有序计划。</returns>
        /// <exception cref="BattleConfigDataException">activePlanId 为空/无对应行、空键无法按
        /// 地图索引解析、越界 strategyProfile、未知波次类型。</exception>
        /// <remarks>
        /// <para>spec "Battle selects one configured wave plan"：按 activePlanId 精确选择
        /// 同 planId 行并按 Order 升序；spec "Legacy arrays cannot decide finite battle behavior"：
        /// 旧数量/Boss/权重数组不参与本计划构建。</para>
        /// </remarks>
        internal static OrderedWavePlanSnapshot BuildPlanFromLuban(
            TbWave tbWave,
            TbWavePlan tbWavePlan,
            EnemyCatalogSnapshot catalog,
            int mapEnemyTypeIndex)
        {
            if (tbWave == null)
            {
                throw new ArgumentNullException(nameof(tbWave));
            }

            if (tbWavePlan == null)
            {
                throw new ArgumentNullException(nameof(tbWavePlan));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            string activePlanId = tbWave.ActivePlanId ?? string.Empty;
            if (string.IsNullOrEmpty(activePlanId))
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.WavePlanMissing,
                    "波次总配置 activePlanId 为空，无法选择逐波计划",
                    "Wave.ActivePlanId");
            }

            var rows = new List<WavePlanEntry>();
            var referencedProfiles = new HashSet<int>();
            IReadOnlyList<WavePlan> planRows = tbWavePlan.DataList;
            for (int i = 0; i < planRows.Count; i++)
            {
                WavePlan row = planRows[i];
                if (!string.Equals(row.PlanId, activePlanId, StringComparison.Ordinal))
                {
                    continue;
                }

                WavePlanKind kind = MapWavePlanKind(row.Kind);

                // Normal 行解析生效敌人键（空键按地图 EnemyTypeIndex 解析）；
                // Boss 行不占用 enemyKey（BossKey 空串按“未配置”处理，语义遵循 spec/design）。
                string enemyKey = kind == WavePlanKind.Normal
                    ? ResolveNormalEnemyKey(row, catalog, mapEnemyTypeIndex)
                    : string.Empty;

                rows.Add(new WavePlanEntry(
                    planId: row.PlanId,
                    order: row.Order,
                    kind: kind,
                    enemyKey: enemyKey,
                    normalCount: row.NormalCount,
                    difficultyIndex: row.DifficultyIndex,
                    bossKey: row.BossKey ?? string.Empty,
                    preDelayMs: row.PreDelayMs,
                    spawnIntervalMs: row.SpawnIntervalMs,
                    postDelayMs: row.PostDelayMs,
                    playerLane: row.PlayerLane,
                    opponentLane: row.OpponentLane,
                    strategyProfile: row.StrategyProfile));

                referencedProfiles.Add(row.StrategyProfile);
            }

            if (rows.Count == 0)
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.WavePlanMissing,
                    $"波次总配置引用的计划 activePlanId='{activePlanId}' 在 TbWavePlan 中没有任何行",
                    "WavePlan.PlanId");
            }

            // 仅保留被所选计划显式引用的策略 profile；越界引用为结构化错误，不静默 fallback。
            var profiles = new Dictionary<int, IReadOnlyList<float>>();
            IReadOnlyList<IReadOnlyList<float>> sourceStrategies = tbWave.SpawnStrategies;
            foreach (int profileIndex in referencedProfiles)
            {
                if (profileIndex < 0 || profileIndex >= sourceStrategies.Count)
                {
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.StrategyProfileInvalid,
                        $"计划 '{activePlanId}' 引用了不存在的策略 profile 索引={profileIndex}" +
                        $"（可用范围 0..{sourceStrategies.Count - 1}）",
                        $"WavePlan.StrategyProfile[{profileIndex}]");
                }

                profiles[profileIndex] = sourceStrategies[profileIndex];
            }

            return new OrderedWavePlanSnapshot(activePlanId, rows, profiles);
        }

        /// <summary>
        /// 解析 Normal 行生效的普通敌人键：非空 enemyKey 直接使用；为空时按地图
        /// EnemyTypeIndex 从目录解析，无法解析即为结构化错误（spec "Map default and row
        /// override resolve one enemy key"）。
        /// </summary>
        private static string ResolveNormalEnemyKey(
            WavePlan row, EnemyCatalogSnapshot catalog, int mapEnemyTypeIndex)
        {
            if (!string.IsNullOrEmpty(row.EnemyKey))
            {
                return row.EnemyKey;
            }

            if (catalog.TryGetByTypeIndex(mapEnemyTypeIndex, out EnemyDefinitionSnapshot def))
            {
                return def.Key;
            }

            throw new BattleConfigDataException(
                BattleConfigErrorCategory.EnemyTypeIndexUnknown,
                $"Normal 行（order={row.Order}）未填写 enemyKey，且地图默认敌人索引 " +
                $"typeIndex={mapEnemyTypeIndex} 无法在敌人目录中解析",
                $"WavePlan.{row.Order}.EnemyKey");
        }

        /// <summary>
        /// 把 Luban EWaveKind 枚举映射到业务 WavePlanKind；未知类型为结构化错误。
        /// </summary>
        private static WavePlanKind MapWavePlanKind(EWaveKind kind)
        {
            switch (kind)
            {
                case EWaveKind.Normal:
                    return WavePlanKind.Normal;
                case EWaveKind.Boss:
                    return WavePlanKind.Boss;
                default:
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.WaveKindUnknown,
                        $"未知波次类型 EWaveKind={kind}",
                        "WavePlan.Kind");
            }
        }

        // ====================================================================
        // Buff 目录适配（task 3.3）
        // ====================================================================

        /// <summary>
        /// 从 TbBuff 逐行显式消费为不可变 Buff 目录。
        /// </summary>
        /// <param name="tbBuff">Luban Buff 表（Type/Name/Label/Kind/Channels/
        /// StackPolicy/MaxStacks/ConflictKey）。</param>
        /// <returns>按 type 唯一索引的不可变 Buff 目录。</returns>
        /// <exception cref="BattleConfigDataException">未知 Kind/StackPolicy，或
        /// 重复 type 无法构建按 type 索引目录。</exception>
        /// <remarks>
        /// <para>spec "Buff definitions form a validated immutable catalog"：每行
        /// Type/Name/Label/Kind/Channels/StackPolicy/MaxStacks/ConflictKey 被显式
        /// 消费到领域快照，禁止按 type 推导缺失 channel/stack 默认值，也不做运行时 fallback。</para>
        /// <para>未知 Kind/StackPolicy（无法映射为领域枚举）与重复 type（无法构建
        /// 合法目录）在本层抛 <see cref="BattleConfigDataException"/>，由
        /// <see cref="BattleStartupContext.Prepare"/> 转换为结构化校验错误并阻断启动。</para>
        /// </remarks>
        internal static BuffCatalogSnapshot BuildBuffCatalogFromLuban(TbBuff tbBuff)
        {
            if (tbBuff == null)
            {
                throw new ArgumentNullException(nameof(tbBuff));
            }

            var definitions = new List<BuffDefinitionSnapshot>(tbBuff.DataList.Count);
            IReadOnlyList<Buff> rows = tbBuff.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                Buff row = rows[i];
                definitions.Add(new BuffDefinitionSnapshot(
                    type: row.Type,
                    name: row.Name ?? string.Empty,
                    label: row.Label ?? string.Empty,
                    kind: MapBuffKind(row.Kind, row.Type),
                    channels: row.Channels,
                    stackPolicy: MapBuffStackPolicy(row.StackPolicy, row.Type),
                    maxStacks: row.MaxStacks,
                    conflictKey: row.ConflictKey ?? string.Empty));
            }

            try
            {
                return new BuffCatalogSnapshot(definitions);
            }
            catch (ArgumentException ex)
            {
                // 重复 type 无法构建按 type 索引目录，转为结构化配置错误。
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.BuffTypeDuplicate,
                    $"Buff 目录构建失败：{ex.Message}",
                    "BuffCatalog");
            }
        }

        /// <summary>
        /// 把 Luban Buff.Kind int 映射到业务 BuffKind；未知值为结构化错误（不 fallback）。
        /// </summary>
        private static BuffKind MapBuffKind(int kind, int type)
        {
            switch (kind)
            {
                case (int)BuffKind.Numeric:
                    return BuffKind.Numeric;
                case (int)BuffKind.State:
                    return BuffKind.State;
                case (int)BuffKind.Custom:
                    return BuffKind.Custom;
                default:
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.BuffKindUnknown,
                        $"Buff type={type} 的 Kind={kind} 未知（合法 0 Numeric/1 State/2 Custom）",
                        $"Buff.{type}.Kind");
            }
        }

        /// <summary>
        /// 把 Luban Buff.StackPolicy 字符串映射到业务 BuffStackPolicy；
        /// 未知/空值为结构化错误（不按 type 推导默认策略）。
        /// </summary>
        private static BuffStackPolicy MapBuffStackPolicy(string stackPolicy, int type)
        {
            if (string.IsNullOrEmpty(stackPolicy))
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.BuffStackPolicyUnknown,
                    $"Buff type={type} 的 StackPolicy 为空（必填 Add/Refresh/Replace，不得在代码中推导默认值）",
                    $"Buff.{type}.StackPolicy");
            }

            switch (stackPolicy)
            {
                case "Add":
                    return BuffStackPolicy.Add;
                case "Refresh":
                    return BuffStackPolicy.Refresh;
                case "Replace":
                    return BuffStackPolicy.Replace;
                default:
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.BuffStackPolicyUnknown,
                        $"Buff type={type} 的 StackPolicy='{stackPolicy}' 未知（合法 Add/Refresh/Replace）",
                        $"Buff.{type}.StackPolicy");
            }
        }

        // ====================================================================
        // Skill 目录适配（task 2.2）
        // ====================================================================

        /// <summary>
        /// 从 TbSkill 逐行显式消费为不可变 Skill 目录。
        /// </summary>
        /// <param name="tbSkill">Luban Skill 表（Key/Category/CooldownSeconds/HandlerKey/
        /// EffectBuffType/EffectDurationMs）。</param>
        /// <returns>按 key 唯一索引的不可变 Skill 目录。</returns>
        /// <exception cref="BattleConfigDataException">未知 Category、CooldownSeconds
        /// 为空/为负，或重复 key 无法构建按 key 索引目录。</exception>
        /// <remarks>
        /// <para>spec "Skill definitions are validated before use"：每行 Key/Category/
        /// CooldownSeconds/HandlerKey/EffectBuffType/EffectDurationMs
        /// 被显式消费到领域快照。Category 严格映射 active/boss/passive（大小写以实际
        /// xlsx 为准，不做 fallback）；CooldownSeconds 为 null/负值、handlerKey 为空
        /// 即结构化失败，不以 checked 转换或默认 handler 掩盖；
        /// EffectBuffType/EffectDurationMs 只读透传，框架不解释。</para>
        /// <para>目录可含未实现 handler 的行（handlerKey 非空但未在 registry 注册）；
        /// 这类行可留在目录，但不得 attach（spec "Catalog rows MAY exist without a
        /// registered handler, but such rows MUST NOT be attached"）。</para>
        /// </remarks>
        internal static SkillCatalogSnapshot BuildSkillCatalogFromLuban(TbSkill tbSkill)
        {
            if (tbSkill == null)
            {
                throw new ArgumentNullException(nameof(tbSkill));
            }

            var definitions = new List<SkillDefinitionSnapshot>(tbSkill.DataList.Count);
            IReadOnlyList<Skill> rows = tbSkill.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                Skill row = rows[i];

                // 空 handlerKey 为结构化错误（必填；缺配置时在 xlsx 补齐，不做 fallback）。
                if (string.IsNullOrEmpty(row.HandlerKey))
                {
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.SkillHandlerKeyMissing,
                        $"Skill key='{row.Key}' 的 handlerKey 为空（必填，不得在代码中推导默认值）",
                        $"Skill.{row.Key}.HandlerKey");
                }

                definitions.Add(new SkillDefinitionSnapshot(
                    key: row.Key ?? string.Empty,
                    category: MapSkillCategory(row.Category, row.Key),
                    cooldownMs: ConvertCooldownToMs(row.CooldownSeconds, row.Key),
                    handlerKey: row.HandlerKey,
                    effectBuffType: row.EffectBuffType,
                    effectDurationMs: row.EffectDurationMs,
                    rangeTiles: row.RangeTiles));
            }

            try
            {
                return new SkillCatalogSnapshot(definitions);
            }
            catch (ArgumentException ex)
            {
                // 重复 key 无法构建按 key 索引目录，转为结构化配置错误。
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.SkillKeyDuplicate,
                    $"Skill 目录构建失败：{ex.Message}",
                    "SkillCatalog");
            }
        }

        /// <summary>
        /// 把 Luban Skill.Category 字符串严格映射到业务 SkillCategory；
        /// 未知/空值为结构化错误（不按 key 推导或 fallback）。
        /// </summary>
        private static SkillCategory MapSkillCategory(string category, string key)
        {
            if (string.IsNullOrEmpty(category))
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.SkillCategoryUnknown,
                    $"Skill key='{key}' 的 Category 为空（必填 active/boss/passive，不得在代码中推导默认值）",
                    $"Skill.{key}.Category");
            }

            switch (category)
            {
                case "active":
                    return SkillCategory.Active;
                case "boss":
                    return SkillCategory.Boss;
                case "passive":
                    return SkillCategory.Passive;
                default:
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.SkillCategoryUnknown,
                        $"Skill key='{key}' 的 Category='{category}' 未知（严格映射 active/boss/passive，不做 fallback）",
                        $"Skill.{key}.Category");
            }
        }

        /// <summary>
        /// 把 Luban Skill.CooldownSeconds（int?）转成 long 毫秒；
        /// null/负值为结构化错误，不静默替换默认值。
        /// </summary>
        private static long ConvertCooldownToMs(int? cooldownSeconds, string key)
        {
            if (!cooldownSeconds.HasValue)
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.SkillCooldownInvalid,
                    $"Skill key='{key}' 的 CooldownSeconds 为空（必填，不得在代码中推导默认值）",
                    $"Skill.{key}.CooldownSeconds");
            }

            int seconds = cooldownSeconds.Value;
            if (seconds < 0)
            {
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.SkillCooldownInvalid,
                    $"Skill key='{key}' 的 CooldownSeconds={seconds} 为负",
                    $"Skill.{key}.CooldownSeconds");
            }

            // int 秒乘 1000 后由 long 承接，不可能溢出。
            return (long)seconds * 1000L;
        }

        // ====================================================================
        // Boss 目录适配（task 4.1）
        // ====================================================================

        /// <summary>
        /// 从 TbBoss 逐行显式消费为不可变 Boss 目录。
        /// </summary>
        /// <param name="tbBoss">Luban Boss 表（Key/Name/SkillKey/AnimationKey/ResourcePath/
        /// AttackAnimation/IdleAnimation/Timeline/Enabled/HealthMultiplier/MoveSpeed/
        /// ContactDamage/RewardGold/LogicalWidth/LogicalHeight）。</param>
        /// <returns>按 key 唯一索引的不可变 Boss 目录。</returns>
        /// <exception cref="BattleConfigDataException">重复 key 无法构建按 key 索引目录。</exception>
        /// <remarks>
        /// <para>spec "Luban Boss rows are copied into immutable definitions"：每行
        /// Key/Name/SkillKey/AnimationKey/ResourcePath/AttackAnimation/IdleAnimation/
        /// Timeline/Enabled/HealthMultiplier/MoveSpeed/ContactDamage/RewardGold/
        /// LogicalWidth/LogicalHeight 被显式消费到领域快照，不保留 Luban row，
        /// 也不做运行时 fallback。缺字段/非法数值由
        /// <see cref="BattleConfigValidator"/> 在启动前拒绝。</para>
        /// <para>重复 key（无法构建合法目录）在本层抛
        /// <see cref="BattleConfigDataException"/>，由
        /// <see cref="BattleStartupContext.Prepare"/> 转换为结构化校验错误并阻断启动。</para>
        /// </remarks>
        internal static BossCatalogSnapshot BuildBossCatalogFromLuban(TbBoss tbBoss)
        {
            if (tbBoss == null)
            {
                throw new ArgumentNullException(nameof(tbBoss));
            }

            var definitions = new List<BossDefinitionSnapshot>(tbBoss.DataList.Count);
            IReadOnlyList<Boss> rows = tbBoss.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                Boss row = rows[i];
                BossTimelineSnapshot timeline = null;
                if (row.Timeline != null)
                {
                    timeline = new BossTimelineSnapshot(row.Timeline.EffectAtMs, row.Timeline.CompleteAtMs);
                }

                definitions.Add(new BossDefinitionSnapshot(
                    key: row.Key ?? string.Empty,
                    name: row.Name ?? string.Empty,
                    skillKey: row.SkillKey ?? string.Empty,
                    animationKey: row.AnimationKey ?? string.Empty,
                    resourcePath: row.ResourcePath ?? string.Empty,
                    attackAnimation: row.AttackAnimation ?? string.Empty,
                    idleAnimation: row.IdleAnimation ?? string.Empty,
                    timeline: timeline,
                    enabled: row.Enabled,
                    healthMultiplier: row.HealthMultiplier,
                    moveSpeed: row.MoveSpeed,
                    contactDamage: row.ContactDamage,
                    rewardGold: row.RewardGold,
                    logicalWidth: row.LogicalWidth,
                    logicalHeight: row.LogicalHeight));
            }

            try
            {
                return new BossCatalogSnapshot(definitions);
            }
            catch (ArgumentException ex)
            {
                // 重复 key 无法构建按 key 索引目录，转为结构化配置错误。
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.BossKeyDuplicate,
                    $"Boss 目录构建失败：{ex.Message}",
                    "BossCatalog");
            }
        }

        // ====================================================================
        // 武器目录适配（task 3.1/3.2）
        // ====================================================================

        /// <summary>
        /// 从 TbWeapon 逐行显式消费为不可变武器目录。
        /// </summary>
        /// <param name="tbWeapon">Luban 武器表（Id/Type/AddAttPower/Enabled/HandlerKey）。</param>
        /// <returns>按 id 唯一索引的不可变武器目录。</returns>
        /// <exception cref="BattleConfigDataException">未知 Type，或重复 id 无法构建
        /// 按 id 索引目录。</exception>
        /// <remarks>
        /// <para>spec "Exactly four basic weapon definitions are enabled"：每行
        /// Id/Type/AddAttPower/Enabled/HandlerKey 被显式消费到领域快照。Type 严格
        /// 映射 0 Bow/1 Spear/2 Knife/3 Sword（大小写以实际 xlsx 为准，不做 fallback）；
        /// 禁用行 handlerKey 为 null 时规范化为空串，不推导 "Basic"。</para>
        /// <para>未知 Type（无法映射为领域枚举）与重复 id（无法构建合法目录）在
        /// 本层抛 <see cref="BattleConfigDataException"/>，由
        /// <see cref="BattleStartupContext.Prepare"/> 转换为结构化校验错误并阻断启动。</para>
        /// </remarks>
        internal static WeaponCatalogSnapshot BuildWeaponCatalogFromLuban(TbWeapon tbWeapon)
        {
            if (tbWeapon == null)
            {
                throw new ArgumentNullException(nameof(tbWeapon));
            }

            var definitions = new List<WeaponDefinitionSnapshot>(tbWeapon.DataList.Count);
            IReadOnlyList<Weapon> rows = tbWeapon.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                Weapon row = rows[i];
                definitions.Add(new WeaponDefinitionSnapshot(
                    id: row.Id,
                    type: MapWeaponType(row.Type, row.Id),
                    addAttackPower: row.AddAttPower,
                    enabled: row.Enabled,
                    handlerKey: row.HandlerKey ?? string.Empty));
            }

            try
            {
                return new WeaponCatalogSnapshot(definitions);
            }
            catch (ArgumentException ex)
            {
                // 重复 id 无法构建按 id 索引目录，转为结构化配置错误。
                throw new BattleConfigDataException(
                    BattleConfigErrorCategory.WeaponIdDuplicate,
                    $"武器目录构建失败：{ex.Message}",
                    "WeaponCatalog");
            }
        }

        /// <summary>
        /// 把 Luban Weapon.Type int 严格映射到业务 WeaponType；
        /// 未知值为结构化错误（不按 id 推导或 fallback）。
        /// </summary>
        private static WeaponType MapWeaponType(int type, int id)
        {
            switch (type)
            {
                case (int)WeaponType.Bow:
                    return WeaponType.Bow;
                case (int)WeaponType.Spear:
                    return WeaponType.Spear;
                case (int)WeaponType.Knife:
                    return WeaponType.Knife;
                case (int)WeaponType.Sword:
                    return WeaponType.Sword;
                default:
                    throw new BattleConfigDataException(
                        BattleConfigErrorCategory.WeaponTypeUnknown,
                        $"Weapon id={id} 的 Type={type} 未知（合法 0 Bow/1 Spear/2 Knife/3 Sword，" +
                        "不得在代码中推导默认值）",
                        $"Weapon.{id}.Type");
            }
        }

        // ====================================================================
        // 【legacy】并行数组式波次适配（兼容 WaveManager/BattleManager）
        // ====================================================================

        /// <summary>
        /// 从 Luban TbWave 适配 legacy 波次配置快照。
        /// </summary>
        /// <remarks>
        /// <para>旧数量/Boss/权重数组已标 deprecated，不再驱动新生产链（spec "Legacy arrays
        /// cannot decide finite battle behavior"）；本方法仅为兼容仍被 WaveManager/BattleManager
        /// 消费的 <see cref="WaveConfigSnapshot"/> 保留复制逻辑。</para>
        /// </remarks>
        private WaveConfigSnapshot NormalizeWaveFromLuban(List<string> missingNotes)
        {
            TbWave tbWave = _tables.TbWave;

            // 生成策略表转换
            var strategies = new List<IReadOnlyList<float>>(tbWave.SpawnStrategies.Count);
            for (int i = 0; i < tbWave.SpawnStrategies.Count; i++)
            {
                strategies.Add(new List<float>(tbWave.SpawnStrategies[i]));
            }

            // TODO(task 3.1/3.2 BLOCKED)：Luban Wave 表缺失 skipBoss/delayTimeMs/maxRounds
            missingNotes.Add(
                "TbWave -> 缺失 skipBoss/delayTimeMs/maxRounds；使用黄金基线值(skipBoss=true/delayTimeMs=10000/maxRounds=20)");

            return new WaveConfigSnapshot(
                waveUnitCounts: tbWave.WaveUnitCounts,
                bossWaveNumbers: tbWave.BossWaveNumbers,
                bossSpawnChances: tbWave.BossSpawnChances,
                spawnStrategyWeights: tbWave.SpawnStrategyWeights,
                spawnStrategies: strategies,
                skipBoss: true,
                delayTimeMs: 10000,
                maxRounds: 20);
        }

        // ====================================================================
        // 单位适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbUnit 适配单位配置列表。
        /// </summary>
        private IReadOnlyList<UnitConfigSnapshot> NormalizeUnitsFromLuban()
        {
            TbUnit tbUnit = _tables.TbUnit;
            var units = new List<UnitConfigSnapshot>(tbUnit.DataList.Count);
            foreach (Unit u in tbUnit.DataList)
            {
                units.Add(new UnitConfigSnapshot(
                    index: u.Index,
                    text: u.Text,
                    animationKey: u.AnimationKey,
                    rangeCells: u.RangeCells,
                    attackDamage: u.AttackDamage,
                    attackIntervalSeconds: u.AttackIntervalSeconds,
                    damageMode: u.DamageMode,
                    targetPolicy: u.TargetPolicy));
            }

            return units;
        }

        // ====================================================================
        // 单位等级适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbUnitLevel 适配单位等级配置。
        /// </summary>
        private UnitLevelConfigSnapshot NormalizeUnitLevelFromLuban()
        {
            TbUnitLevel tbUnitLevel = _tables.TbUnitLevel;
            return new UnitLevelConfigSnapshot(
                maxLevel: tbUnitLevel.MaxLevel,
                damageLevelMultipliers: tbUnitLevel.DamageLevelMultipliers,
                attackSpeedLevelMultipliers: tbUnitLevel.AttackSpeedLevelMultipliers);
        }

        // ====================================================================
        // 经济适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbEconomy 适配经济配置。
        /// </summary>
        private EconomyConfigSnapshot NormalizeEconomyFromLuban(List<string> missingNotes)
        {
            TbEconomy tbEconomy = _tables.TbEconomy;

            missingNotes.Add(
                "TbEconomy -> 缺失 PlayerMaxHealth/OpponentMaxHealth；使用 BattleState.js 硬编码黄金基线值(3/3)");

            return new EconomyConfigSnapshot(
                initialGold: tbEconomy.InitialGold,
                refreshCostStart: tbEconomy.RefreshCostStart,
                refreshCostIncrement: tbEconomy.RefreshCostIncrement,
                unitBaseCost: tbEconomy.UnitBaseCost,
                handSize: tbEconomy.HandSize,
                playerMaxHealth: 3,
                opponentMaxHealth: 3);
        }

        // ====================================================================
        // 牌组适配
        // ====================================================================

        /// <summary>
        /// 适配牌组配置。Luban 无 deck 表，使用 DeckDefinitions.js 硬编码黄金基线值。
        /// </summary>
        private DeckConfigSnapshot NormalizeDeckFromLuban(List<string> missingNotes)
        {
            missingNotes.Add(
                "无 deck 表 -> Luban 缺失牌组配置；使用 DeckDefinitions.js 硬编码黄金基线值");

            return new DeckConfigSnapshot(
                minimalMode: true,
                baseSoldierTexts: new string[] { "刀", "弓", "枪", "骑" },
                handSize: 5,
                defaultLevel: 1,
                baseUnitCost: 1);
        }

        // ====================================================================
        // 投射物适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbProjectile 适配投射物配置。
        /// </summary>
        private ProjectileConfigSnapshot NormalizeProjectileFromLuban(List<string> missingNotes)
        {
            TbProjectile tbProjectile = _tables.TbProjectile;

            missingNotes.Add(
                "TbProjectile -> 缺失 PrimaryType/MovementStrategy/HitStrategy；" +
                "使用 ProjectileFactory 黄金基线值(SimpleDynamicArrow/TargetEnemyBezierMovement/HitEnemyStrategy)");

            return new ProjectileConfigSnapshot(
                types: tbProjectile.Types,
                primaryType: "SimpleDynamicArrow",
                movementStrategy: "TargetEnemyBezierMovement",
                hitStrategy: "HitEnemyStrategy");
        }

        // ====================================================================
        // 工具方法
        // ====================================================================

        /// <summary>
        /// 把 Luban List&lt;Vector2Int&gt; 路径转换为 IReadOnlyList&lt;GridPosition&gt;。
        /// </summary>
        private static IReadOnlyList<GridPosition> ConvertPath(List<Vector2Int> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<GridPosition>();
            }

            var result = new GridPosition[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = new GridPosition(source[i].x, source[i].y);
            }

            return result;
        }

        /// <summary>
        /// 把 Luban Vector2Int 转换为 <see cref="GridPosition"/>。
        /// </summary>
        private static GridPosition ToGridPosition(Vector2Int source)
            => new GridPosition(source.x, source.y);

        /// <summary>
        /// 把 Luban List&lt;Vector2Int&gt; 表现层路径标记转换为 IReadOnlyList&lt;GridPosition&gt;。
        /// </summary>
        private static IReadOnlyList<GridPosition> ConvertMarkers(List<Vector2Int> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<GridPosition>();
            }

            var result = new GridPosition[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = new GridPosition(source[i].x, source[i].y);
            }

            return result;
        }
    }
}
