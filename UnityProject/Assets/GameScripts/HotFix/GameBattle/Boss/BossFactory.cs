using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.5：BossFactory —— 只注册 ZhangLiang 的封闭 Boss 工厂/池
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 2 / specs/zhang-liang-boss-runtime/spec.md）：
    //   - 封闭注册表：只注册 ZhangLiang（薄类型 ZhangLiangBoss + 独立类型池）；
    //     未知/未支持 bossKey 在 Acquire 显式失败，不创建占位 Boss。
    //   - Acquire(BossSpawnRequest)：租借 → 分配 runtimeId → BossStatsResolver 解析
    //     数值 → 注入定义/地图/终点/回调 → 初始化车道与 waveOrder → 开始移动；
    //     任一步失败都回滚本次租借到正确池。
    //   - Release(BossBase)：先清除 Skill 所有权（取消运行中激活），再归还正确池。
    //
    // 不变量：
    //   1. 每次租借分配新 RuntimeId（RuntimeIdAllocator，从 1 单调递增）。
    //   2. Acquire/Release 对称：每次租借恰好一次 Release；失败回滚到正确池。
    //   3. 数值解析唯一：经 EnemyStatsResolver 基线 + BossStatsResolver 倍率，不反查表。
    // ============================================================================

    /// <summary>
    /// 封闭 key 注册表 + Boss 独立类型池的 Boss 工厂（只支持 ZhangLiang）。
    /// </summary>
    /// <remarks>
    /// <para><b>封闭注册表（spec "Only ZhangLiang is supported in the first slice"）：</b>
    /// 只注册 <see cref="ZhangLiangBoss"/>，其余 11 个 Boss 行即使配置存在也不可出生；
    /// 未知键在 <see cref="Acquire(BossSpawnRequest)"/> 显式失败。</para>
    /// <para><b>失败回滚（design.md 决策 6）：</b>任一步失败先把本次租借归还正确池
    /// （Reset + 入池），再重新抛出，保证池租借对称。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部
    /// <see cref="ZhangLiangBossWavePort"/> 使用。</para>
    /// </remarks>
    internal sealed class BossFactory
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[BossFactory]";

        // ====================================================================
        // 注入依赖
        // ====================================================================

        /// <summary>运行时 ID 分配器。每次租借分配新 ID，保证池复用不复用旧 ID。</summary>
        private readonly RuntimeIdAllocator _idAllocator;

        /// <summary>普通敌人基线定义（按地图 EnemyTypeIndex 解析，供 BossStatsResolver）。</summary>
        private readonly EnemyDefinitionSnapshot _baselineDefinition;

        /// <summary>封闭注册表：Boss 键 → 租借/回收委托 + 定义快照。</summary>
        private readonly IReadOnlyDictionary<int, BossTypeRegistration> _registry;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 以 Boss 目录建封闭注册表并构造 Boss 工厂：只注册 ZhangLiang。
        /// </summary>
        /// <param name="idAllocator">运行时 ID 分配器（不可为 null）。</param>
        /// <param name="catalog">不可变 Boss 目录（不可为 null）。</param>
        /// <param name="baselineDefinition">普通敌人基线定义（按地图 EnemyTypeIndex 解析，不可为 null）。</param>
        /// <param name="poolScope">战斗对象池作用域（不可为 null，跨局复用池容量）。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        internal BossFactory(
            RuntimeIdAllocator idAllocator,
            BossCatalogSnapshot catalog,
            EnemyDefinitionSnapshot baselineDefinition,
            BattlePoolScope poolScope)
        {
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _baselineDefinition = baselineDefinition ?? throw new ArgumentNullException(nameof(baselineDefinition));
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (poolScope == null)
            {
                throw new ArgumentNullException(nameof(poolScope));
            }

            _registry = BuildRegistry(catalog, poolScope);
        }

        /// <summary>
        /// 由目录定义构建封闭注册表：只注册 ZhangLiang 并绑定独立类型池。
        /// </summary>
        private static IReadOnlyDictionary<int, BossTypeRegistration> BuildRegistry(
            BossCatalogSnapshot catalog,
            BattlePoolScope poolScope)
        {
            var registry = new Dictionary<int, BossTypeRegistration>();
            foreach (BossDefinitionSnapshot definition in catalog.Definitions)
            {
                // 首期只支持 ZhangLiang；其余行保持配置存在但不可出生。
                if (!string.Equals(definition.ResName, ZhangLiangBoss.ResNameConst, StringComparison.Ordinal))
                {
                    continue;
                }

                BattleObjectPool<ZhangLiangBoss> pool = poolScope.GetPool(() => new ZhangLiangBoss());
                registry.Add(
                    definition.Id,
                    new BossTypeRegistration(
                        definition,
                        acquire: () => pool.Acquire(),
                        release: boss => pool.Release((ZhangLiangBoss)boss)));
            }

            return registry;
        }

        // ====================================================================
        // Acquire —— 租借并初始化 Boss
        // ====================================================================

        /// <summary>
        /// 按出生请求租借并初始化一个 Boss：租借 → 分配 runtimeId → 解析数值 →
        /// 注入地图/终点/回调 → 初始化车道与 waveOrder → 开始移动。
        /// </summary>
        /// <param name="request">出生请求（不可为 null）。</param>
        /// <returns>已初始化并开始移动的 Boss（供端口登记到 EnemyManager）。</returns>
        /// <exception cref="ArgumentNullException">request 为 null。</exception>
        /// <exception cref="ArgumentException">请求 Boss 键未注册（unknown/unsupported key）。</exception>
        /// <exception cref="EnemyStatsResolutionException">难度索引越界或配置非法（不夹取）。</exception>
        /// <remarks>
        /// <para><b>失败回滚（design.md 决策 6）：</b>任一步失败都先把本次租借归还到
        /// 正确池（Reset + 入池），再重新抛出异常。</para>
        /// </remarks>
        internal BossBase Acquire(BossSpawnRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!_registry.TryGetValue(request.BossId, out BossTypeRegistration registration))
            {
                // 封闭注册表：未知/未支持键显式失败，不创建占位 Boss。
                throw new ArgumentException(
                    $"{LogTag} 未知或未支持 Boss id={request.BossId}（首期只支持 {ZhangLiangBoss.ResNameConst}，禁止占位）");
            }

            BossBase boss = registration.Acquire();
            if (boss == null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} 池返回 null 对象 id={request.BossId}");
            }

            try
            {
                // 分配新运行时 ID（池复用不复用旧 ID）。
                int runtimeId = _idAllocator.Allocate();
                boss.AssignRuntimeId(runtimeId);

                // 唯一数值解析器：普通基线 × 配置倍率。
                ConfiguredEnemyResolvedStats stats = BossStatsResolver.Resolve(
                    _baselineDefinition,
                    registration.Definition,
                    request.DifficultyIndex,
                    request.StrategyProfile);

                // 注入定义/数值与依赖、初始化车道与 waveOrder（generation 递增）。
                boss.BossConfiguredInit(
                    request.Map,
                    request.CellSize,
                    request.EndPointTarget,
                    request.OnEnemyKilled,
                    request.OnDeathRequested,
                    stats,
                    registration.Definition,
                    registration.Definition.ResourcePath,
                    request.IsPlayerLane,
                    request.WaveOrder);

                // 开始移动（SPAWNING → MOVING）。
                boss.BeginMoving();

                return boss;
            }
            catch
            {
                // 失败回滚：把本次租借归还正确池（Reset + 入池），再重新抛出。
                registration.Release(boss);
                throw;
            }
        }

        // ====================================================================
        // Release —— 归还 Boss 到正确池
        // ====================================================================

        /// <summary>
        /// 归还一个 Boss 到其正确类型的池（按 <see cref="BossBase.ResName"/> 分发）。
        /// </summary>
        /// <param name="boss">要归还的 Boss。null 或已归还返回 false。</param>
        /// <returns>成功归还返回 true；null、键未注册或重复 Release 返回 false。</returns>
        /// <remarks>
        /// <para><b>先清 Skill 所有权（design.md 决策 7）：</b>调用
        /// <see cref="BossBase.ClearSkillOwnership"/> 取消运行中激活（effect 前死亡取消
        /// 混乱，effect 后不清已提交 Buff），再归还池（池内部先 ResetState 再入池）。</para>
        /// </remarks>
        internal bool Release(BossBase boss)
        {
            if (boss == null)
            {
                return false;
            }

            if (boss.Definition == null || !_registry.TryGetValue(boss.Definition.Id, out BossTypeRegistration registration))
            {
                return false;
            }

            // 清技能所有权（取消运行中激活）后再归还池（Reset + 入池）。
            boss.ClearSkillOwnership();
            return registration.Release(boss);
        }

        // ====================================================================
        // 内部注册类型
        // ====================================================================

        /// <summary>
        /// 单类型注册：定义快照 + acquire/release 委托（绑定独立类型池）。
        /// </summary>
        private sealed class BossTypeRegistration
        {
            /// <summary>该键的 Boss 定义快照（数值/动画/时间轴来源）。</summary>
            public readonly BossDefinitionSnapshot Definition;

            /// <summary>租借委托（从独立类型池 Acquire）。</summary>
            public readonly Func<BossBase> Acquire;

            /// <summary>回收委托（归还到独立类型池，先 Reset 再入池；返回是否成功归还）。</summary>
            public readonly Func<BossBase, bool> Release;

            /// <summary>构造单类型注册。</summary>
            internal BossTypeRegistration(
                BossDefinitionSnapshot definition,
                Func<BossBase> acquire,
                Func<BossBase, bool> release)
            {
                Definition = definition;
                Acquire = acquire;
                Release = release;
            }
        }
    }
}
