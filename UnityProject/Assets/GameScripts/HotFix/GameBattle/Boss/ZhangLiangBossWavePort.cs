using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.5/5.6：ZhangLiangBossWavePort —— 生产 Boss 波交接端口（只支持张梁）
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 1/2 / specs/zhang-liang-boss-runtime/spec.md）：
    //   按 BossWaveSpawnRequest 出生 ZhangLiang Boss：BossFactory 租借初始化 →
    //   EnemyManager.Register（EnemyManager 是唯一 active hostile owner）→
    //   AttachSkill（SkillRunner，firstReady=spawn+cooldown）→ 返回 Boss-kind handle。
    //   不维护第二个 active dictionary；Cleanup 经 EnemyManager.ForceRemoveBosses
    //   强制移除 Boss，实际 release 仍由唯一移除点归还 Boss 工厂池。
    //
    // 事务与回滚（spec "Spawn transaction fails"）：
    //   Acquire 后任一步失败按逆序回滚：清除 Skill 所有权 → EnemyManager.Unregister →
    //   BossFactory.Release（归还池）。成功才返回有效 handle。
    //
    // 停止/清理（spec "Boss cleanup is ordered and idempotent"）：
    //   Stop：幂等停止，之后不再出生新 Boss（Spawn 显式失败）。
    //   Cleanup：幂等停止并强制移除活动 Boss；重复调用为空操作。
    // ============================================================================

    /// <summary>
    /// 生产 Boss 波交接端口：只支持 ZhangLiang，复用 EnemyManager 作为唯一活动 owner。
    /// </summary>
    /// <remarks>
    /// <para><b>只支持 ZhangLiang（spec "Only ZhangLiang is supported in the first slice"）：</b>
    /// <see cref="SupportedBossKeys"/> 只含 ZhangLiang；Spawn 对未知/未启用/未支持键显式失败。</para>
    /// <para><b>端口不维护 active 索引（design.md 决策 1）：</b>出生后实体进入
    /// EnemyManager；端口只持 factory、依赖与 stopped 状态。Cleanup 请求
    /// <see cref="EnemyManager.ForceRemoveBosses"/> 按 Boss kind 强制移除。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 WaveManager 与测试使用。</para>
    /// </remarks>
    internal sealed class ZhangLiangBossWavePort : IBossWavePort
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[ZhangLiangBossWavePort]";

        // ====================================================================
        // 注入依赖（不可变，构造时注入）
        // ====================================================================

        /// <summary>Boss 目录（校验定义存在/enabled/时间轴）。</summary>
        private readonly BossCatalogSnapshot _bossCatalog;

        /// <summary>Skill 目录（解析 Boss 技能冷却用于 firstReady）。</summary>
        private readonly SkillCatalogSnapshot _skillCatalog;

        /// <summary>Boss 工厂/池（只注册 ZhangLiang）。</summary>
        private readonly BossFactory _bossFactory;

        /// <summary>敌人管理器（唯一 active hostile owner；登记/移除）。</summary>
        private readonly EnemyManager _enemyManager;

        /// <summary>最小技能运行器（唯一时间源；Boss attach 用）。</summary>
        private readonly SkillRunner _skillRunner;

        /// <summary>地图数据（提供路径与坐标 API）。</summary>
        private readonly MapData _map;

        /// <summary>格子尺寸（px）。</summary>
        private readonly float _cellSize;

        /// <summary>按车道解析终点攻击目标（玩家路/电脑路）。</summary>
        private readonly Func<bool, IEnemyEndPointAttackTarget> _endPointTargetResolver;

        /// <summary>Boss 击杀回调（首次死亡恰好一次提交 reward/KillCount/BossKillCount）。</summary>
        private readonly BossKilledHandler _onBossKilled;

        /// <summary>是否已停止（Stop/Cleanup 置位；之后 Spawn 显式失败）。</summary>
        private bool _stopped;

        /// <summary>构造生产 Boss 波交接端口。</summary>
        /// <param name="bossCatalog">Boss 目录（不可为 null）。</param>
        /// <param name="skillCatalog">Skill 目录（不可为 null）。</param>
        /// <param name="bossFactory">Boss 工厂/池（不可为 null）。</param>
        /// <param name="enemyManager">敌人管理器（不可为 null）。</param>
        /// <param name="skillRunner">最小技能运行器（不可为 null）。</param>
        /// <param name="map">地图数据（不可为 null）。</param>
        /// <param name="cellSize">格子尺寸（px）。</param>
        /// <param name="endPointTargetResolver">按车道解析终点攻击目标（不可为 null）。</param>
        /// <param name="onBossKilled">Boss 击杀回调（可为 null，表示仅记录不提交）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        internal ZhangLiangBossWavePort(
            BossCatalogSnapshot bossCatalog,
            SkillCatalogSnapshot skillCatalog,
            BossFactory bossFactory,
            EnemyManager enemyManager,
            SkillRunner skillRunner,
            MapData map,
            float cellSize,
            Func<bool, IEnemyEndPointAttackTarget> endPointTargetResolver,
            BossKilledHandler onBossKilled)
        {
            _bossCatalog = bossCatalog ?? throw new ArgumentNullException(nameof(bossCatalog));
            _skillCatalog = skillCatalog ?? throw new ArgumentNullException(nameof(skillCatalog));
            _bossFactory = bossFactory ?? throw new ArgumentNullException(nameof(bossFactory));
            _enemyManager = enemyManager ?? throw new ArgumentNullException(nameof(enemyManager));
            _skillRunner = skillRunner ?? throw new ArgumentNullException(nameof(skillRunner));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _cellSize = cellSize;
            _endPointTargetResolver = endPointTargetResolver
                ?? throw new ArgumentNullException(nameof(endPointTargetResolver));
            _onBossKilled = onBossKilled;
            _stopped = false;
        }

        // ====================================================================
        // IBossWavePort 实现
        // ====================================================================

        /// <summary>能力可用（未停止）。</summary>
        public bool IsAvailable => !_stopped;

        /// <summary>只支持 ZhangLiang。</summary>
        public IReadOnlyList<string> SupportedBossKeys => new[] { ZhangLiangBoss.ResNameConst };

        /// <summary>
        /// 请求一次 Boss 出生（按请求的 bossKey/lane/waveOrder/difficulty）。
        /// </summary>
        /// <param name="request">Boss 出生请求（不可为 null）。</param>
        /// <returns>成功出生后的 Boss-kind 波次所有权 handle。</returns>
        /// <exception cref="InvalidOperationException">端口已停止、键未知/未启用/未支持或事务失败。</exception>
        /// <remarks>
        /// <para><b>事务（spec "Spawn transaction fails"）：</b>
        /// Acquire → Register → AttachSkill；任一步失败按逆序回滚
        /// （清 Skill 所有权 → EnemyManager.Unregister → BossFactory.Release），
        /// 不返回无效 handle、不泄漏池租借。</para>
        /// </remarks>
        public WaveEntityHandle Spawn(BossWaveSpawnRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_stopped)
            {
                throw new InvalidOperationException($"{LogTag} 端口已停止，不再出生新 Boss");
            }

            if (!_bossCatalog.TryGetById(request.BossId, out BossDefinitionSnapshot definition))
            {
                throw new InvalidOperationException(
                    $"{LogTag} 未知 bossId={request.BossId}（目录中不存在，禁止占位）");
            }

            if (!definition.Enabled)
            {
                throw new InvalidOperationException(
                    $"{LogTag} bossId={request.BossId} 未启用（disabled，不得出生）");
            }

            if (!IsSupportedResource(definition.ResName))
            {
                throw new InvalidOperationException(
                    $"{LogTag} bossId={request.BossId} 的资源 '{definition.ResName}' 不受支持");
            }

            IEnemyEndPointAttackTarget endPointTarget = _endPointTargetResolver(request.IsPlayerLane);
            var spawnRequest = new BossSpawnRequest(
                bossId: request.BossId,
                isPlayerLane: request.IsPlayerLane,
                waveOrder: request.WaveOrder,
                difficultyIndex: request.DifficultyIndex,
                strategyProfile: request.StrategyProfile,
                map: _map,
                cellSize: _cellSize,
                endPointTarget: endPointTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                {
                    // Boss 击杀奖励/计数经注入回调提交（EnemyBase 死亡点恰好一次触发）。
                    _onBossKilled?.Invoke(reward, lane);
                },
                onDeathRequested: (killedId, reason) => _enemyManager.RequestRemoveEnemy(killedId, reason));

            BossBase boss = _bossFactory.Acquire(spawnRequest);
            try
            {
                _enemyManager.Register(boss);
            }
            catch
            {
                // Register 失败：回滚（清 Skill + 归还池）。
                _bossFactory.Release(boss);
                throw;
            }

            try
            {
                // firstReady = spawn battle clock + Boss 技能冷却（首次 8000ms；
                // 后续冷却由 SkillRunner 维护）。
                long firstReadyMs = _skillRunner.Scheduler.FrameNowMs;
                if (_skillCatalog.TryGetById(definition.SkillId, out SkillDefinitionSnapshot skillDef))
                {
                    firstReadyMs = checked(firstReadyMs + skillDef.CooldownMs);
                }

                if (!boss.AttachSkill(_skillRunner, definition.SkillId, firstReadyMs))
                {
                    throw new InvalidOperationException(
                        $"{LogTag} Boss id={request.BossId} 技能 attach 失败（skillId={definition.SkillId}）");
                }
            }
            catch
            {
                // AttachSkill 失败：逆序回滚（EnemyManager.Unregister → 归还池）。
                _enemyManager.Unregister(boss.Id);
                _bossFactory.Release(boss);
                throw;
            }

            return new WaveEntityHandle(boss.Id, boss.Generation, request.WaveOrder, WaveEntityKind.Boss);
        }

        /// <summary>幂等停止：不再出生新 Boss。</summary>
        public void Stop()
        {
            _stopped = true;
        }

        /// <summary>幂等清理：停止并强制移除活动 Boss（重复调用为空操作）。</summary>
        /// <remarks>
        /// <para>不维护 active dictionary；经 <see cref="EnemyManager.ForceRemoveBosses"/>
        /// 按 Boss kind 强制移除，实际 release 仍由唯一移除点归还 Boss 工厂池
        /// （release 时清除 Skill 所有权）。</para>
        /// </remarks>
        public void Cleanup()
        {
            _stopped = true;
            _enemyManager.ForceRemoveBosses();
        }

        // ====================================================================
        // 辅助
        // ====================================================================

        /// <summary>判断键是否受支持（首期只支持 ZhangLiang）。</summary>
        private static bool IsSupportedResource(string resName)
        {
            return string.Equals(resName, ZhangLiangBoss.ResNameConst, StringComparison.Ordinal);
        }
    }
}
