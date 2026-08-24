using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.3-5.6：Boss 出生请求与击杀回调
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 1/2 / specs/zhang-liang-boss-runtime/spec.md）：
    //   Boss 出生请求：携带 bossKey/lane/waveOrder/difficulty/策略 profile 与初始化
    //   所需的地图/终点/回调；Boss 工厂据此完成租借与初始化，不反查可变全局配置表。
    // ============================================================================

    /// <summary>
    /// Boss 击杀回调：Boss 首次死亡（Enemy 死亡边界）恰好一次提交奖励与计数。
    /// </summary>
    /// <param name="rewardGold">击杀奖励金币（ZhangLiang=10）。</param>
    /// <param name="isPlayerLane">该 Boss 所在车道（true=玩家路）。</param>
    /// <remarks>
    /// <para>由 <see cref="ZhangLiangBossWavePort"/> 在出生时注入，经
    /// <see cref="EnemyBase"/> 的首次死亡回调调用。调用方负责提交奖励
    /// （<see cref="BattleEconomy.Award"/>）、<see cref="BattleState.ApplyEnemyKill"/>
    /// 与 <see cref="BattleState.ApplyBossKill"/>；endpoint/forced/Settling 移除不触发。</para>
    /// </remarks>
    internal delegate void BossKilledHandler(int rewardGold, bool isPlayerLane);

    /// <summary>
    /// Boss 出生请求：携带已解析的 Boss 键、车道、waveOrder、难度、策略 profile
    /// 与初始化所需的地图/终点/回调。
    /// </summary>
    /// <remarks>
    /// <para>spec "Boss placement remains entirely configuration ordered"：请求由
    /// <see cref="WaveManager"/> 解析的行生成，端口/工厂不反查可变全局配置表。</para>
    /// </remarks>
    internal sealed class BossSpawnRequest
    {
        /// <summary>生效的 Boss 键（如 ZhangLiang）。</summary>
        public int BossId { get; }

        /// <summary>是否玩家方车道。</summary>
        public bool IsPlayerLane { get; }

        /// <summary>波次所有权（不可变 waveOrder，随租借携带）。</summary>
        public int WaveOrder { get; }

        /// <summary>0-based 难度索引（同时索引基线血量曲线、策略乘数、早期乘数）。</summary>
        public int DifficultyIndex { get; }

        /// <summary>该行显式引用的策略乘数 profile。</summary>
        public IReadOnlyList<float> StrategyProfile { get; }

        /// <summary>地图数据（提供路径与坐标 API）。</summary>
        public MapData Map { get; }

        /// <summary>格子尺寸（px）。</summary>
        public float CellSize { get; }

        /// <summary>终点攻击目标（按车道绑定阿斗）。</summary>
        public IEnemyEndPointAttackTarget EndPointTarget { get; }

        /// <summary>击杀奖励回调（Boss 首次死亡恰好一次提交 reward/count）。</summary>
        public EnemyKilledHandler OnEnemyKilled { get; }

        /// <summary>死亡请求移除回调。</summary>
        public EnemyDeathRequestHandler OnDeathRequested { get; }

        /// <summary>构造 Boss 出生请求。</summary>
        /// <exception cref="ArgumentException">bossKey 为空。</exception>
        /// <exception cref="ArgumentNullException">map / endPointTarget / onEnemyKilled / onDeathRequested 为 null。</exception>
        internal BossSpawnRequest(
            int bossId,
            bool isPlayerLane,
            int waveOrder,
            int difficultyIndex,
            IReadOnlyList<float> strategyProfile,
            MapData map,
            float cellSize,
            IEnemyEndPointAttackTarget endPointTarget,
            EnemyKilledHandler onEnemyKilled,
            EnemyDeathRequestHandler onDeathRequested)
        {
            if (bossId < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bossId));
            }

            BossId = bossId;
            IsPlayerLane = isPlayerLane;
            WaveOrder = waveOrder;
            DifficultyIndex = difficultyIndex;
            StrategyProfile = strategyProfile
                ?? throw new ArgumentNullException(nameof(strategyProfile));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            CellSize = cellSize;
            EndPointTarget = endPointTarget
                ?? throw new ArgumentNullException(nameof(endPointTarget));
            OnEnemyKilled = onEnemyKilled
                ?? throw new ArgumentNullException(nameof(onEnemyKilled));
            OnDeathRequested = onDeathRequested
                ?? throw new ArgumentNullException(nameof(onDeathRequested));
        }
    }
}
