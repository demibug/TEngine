using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.1：NormalWaveSpawnRequest + BossWaveSpawnRequest + NormalWaveSpawnHandler
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 5 / specs/ordered-wave-plan/spec.md / task 4.1-4.4）：
    //   波次状态机的出生请求载荷。WaveManager 只消费 OrderedWavePlanSnapshot 与
    //   注入的出生端口，不持有 Enemy/Boss 对象或配置行。
    //
    // 契约（任务已决定）：
    //   1. Normal/Boss 请求均携带 lane、waveOrder、difficultyIndex。
    //   2. Normal 额外携带已解析 enemyKey 与 strategyProfile（索引 + 只读 profile，
    //      无歧义；handler 不反查可变全局配置表）。
    //   3. Boss 携带 bossKey，按 IBossWavePort.Spawn 出生，不冒充 Normal。
    //   4. 请求不可变，构造时校验必需字段。
    //
    // 不变量：
    //   1. Normal 请求 enemyKey 非空；Boss 请求 bossKey 非空。
    //   2. 纯逻辑，无反射/dynamic/Emit，热更安全。
    // ============================================================================

    /// <summary>
    /// 普通敌人出生请求（WaveManager 状态机 → Normal spawn handler）。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <see cref="EnemySpawnRequest"/> 的边界：</b>本请求只携带波次状态机
    /// 需要的字段（enemyKey / lane / waveOrder / difficultyIndex / strategyProfile），
    /// 不携带地图/终点/回调等运行时依赖。下一波 Runtime 集成（4.6）负责把本请求
    /// 翻译为 <see cref="EnemySpawnRequest"/>（补齐 Map/EndPointTarget/回调）后交给
    /// EnemyFactory。</para>
    /// <para><see cref="StrategyProfileIndex"/> 是配置行显式引用的源表 profile 索引；
    /// <see cref="StrategyProfile"/> 是该索引解析后的只读乘数数组，两者都携带，
    /// 供 handler 无歧义消费。</para>
    /// </remarks>
    internal sealed class NormalWaveSpawnRequest
    {
        /// <summary>生效的普通敌人键（Mob0/Mob1/Mob2/Mob3；空键已由 Provider 按地图解析）。</summary>
        public int EnemyId { get; }

        /// <summary>是否玩家方车道。</summary>
        public bool IsPlayerLane { get; }

        /// <summary>波次所有权（不可变 waveOrder，对应配置行 Order）。</summary>
        public int WaveOrder { get; }

        /// <summary>0-based 难度索引。</summary>
        public int DifficultyIndex { get; }

        /// <summary>策略乘数 profile 的源表原始索引。</summary>
        public int StrategyProfileIndex { get; }

        /// <summary>解析后的只读策略乘数 profile（索引对应的数组）。</summary>
        public IReadOnlyList<float> StrategyProfile { get; }

        /// <summary>构造普通敌人出生请求。</summary>
        /// <exception cref="ArgumentException">enemyKey 为空。</exception>
        /// <exception cref="ArgumentNullException">strategyProfile 为 null。</exception>
        internal NormalWaveSpawnRequest(
            int enemyId,
            bool isPlayerLane,
            int waveOrder,
            int difficultyIndex,
            int strategyProfileIndex,
            IReadOnlyList<float> strategyProfile)
        {
            if (enemyId < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(enemyId));
            }

            EnemyId = enemyId;
            IsPlayerLane = isPlayerLane;
            WaveOrder = waveOrder;
            DifficultyIndex = difficultyIndex;
            StrategyProfileIndex = strategyProfileIndex;
            StrategyProfile = strategyProfile
                ?? throw new ArgumentNullException(nameof(strategyProfile));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"NormalWaveSpawnRequest(enemyId={EnemyId}, playerLane={IsPlayerLane}, " +
                   $"waveOrder={WaveOrder}, difficulty={DifficultyIndex}, profile={StrategyProfileIndex})";
        }
    }

    /// <summary>
    /// Boss 出生请求（WaveManager 状态机 → <see cref="IBossWavePort.Spawn"/>）。
    /// </summary>
    /// <remarks>
    /// <para>携带 bossKey、lane、waveOrder、difficultyIndex 与策略乘数 profile
    /// （Boss 最大生命按普通基线 × 配置倍率，策略乘数参与基线解析）。</para>
    /// </remarks>
    internal sealed class BossWaveSpawnRequest
    {
        /// <summary>Boss 敌人键（Boss 行必填）。</summary>
        public int BossId { get; }

        /// <summary>是否玩家方车道。</summary>
        public bool IsPlayerLane { get; }

        /// <summary>波次所有权（不可变 waveOrder，对应配置行 Order）。</summary>
        public int WaveOrder { get; }

        /// <summary>0-based 难度索引。</summary>
        public int DifficultyIndex { get; }

        /// <summary>策略乘数 profile 的源表原始索引。</summary>
        public int StrategyProfileIndex { get; }

        /// <summary>解析后的只读策略乘数 profile（Boss 基线解析用）。</summary>
        public IReadOnlyList<float> StrategyProfile { get; }

        /// <summary>构造 Boss 出生请求。</summary>
        /// <exception cref="ArgumentException">bossKey 为空。</exception>
        /// <exception cref="ArgumentNullException">strategyProfile 为 null。</exception>
        internal BossWaveSpawnRequest(
            int bossId,
            bool isPlayerLane,
            int waveOrder,
            int difficultyIndex,
            int strategyProfileIndex,
            IReadOnlyList<float> strategyProfile)
        {
            if (bossId < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bossId));
            }

            BossId = bossId;
            IsPlayerLane = isPlayerLane;
            WaveOrder = waveOrder;
            DifficultyIndex = difficultyIndex;
            StrategyProfileIndex = strategyProfileIndex;
            StrategyProfile = strategyProfile
                ?? throw new ArgumentNullException(nameof(strategyProfile));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"BossWaveSpawnRequest(bossId={BossId}, playerLane={IsPlayerLane}, " +
                   $"waveOrder={WaveOrder}, difficulty={DifficultyIndex}, profile={StrategyProfileIndex})";
        }
    }

    /// <summary>
    /// 普通敌人出生 handler 委托：由 WaveManager 注入（下一波 Runtime 集成时桥接到
    /// EnemyFactory.Acquire(EnemySpawnRequest)），成功返回 <see cref="WaveEntityHandle"/>。
    /// </summary>
    /// <param name="request">波次状态机解析出的普通敌人出生请求。</param>
    /// <returns>成功出生后的波次所有权 handle；失败必须抛异常（禁止返回无效 handle）。</returns>
    internal delegate WaveEntityHandle NormalWaveSpawnHandler(NormalWaveSpawnRequest request);
}
