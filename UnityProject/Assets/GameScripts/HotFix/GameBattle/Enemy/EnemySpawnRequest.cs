using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.4/3.6：EnemySpawnRequest + EnemyLeaseIdentity
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 6 / specs/configured-enemy-spawning/spec.md）：
    //   - EnemySpawnRequest：普通敌人出生请求，携带已解析 enemyKey、isPlayerLane、
    //     waveOrder、difficultyIndex、对应策略 profile 以及初始化所需地图/终点/回调；
    //     工厂据此完成租借与初始化，不得反查可变全局配置表。
    //   - EnemyLeaseIdentity：generation-aware 租借身份值对象（runtimeId + generation +
    //     waveOrder），供下一波 EnemyManager 追踪波次所有权，幂等匹配并抵抗迟到回调。
    //
    // 不变量：
    //   1. EnemySpawnRequest 不可变，构造时校验必需依赖非空。
    //   2. EnemyLeaseIdentity 不可变值类型，实现值相等与 GetHashCode。
    // ============================================================================

    /// <summary>
    /// 普通敌人出生请求：携带已解析的敌人键、车道、waveOrder、难度、策略 profile
    /// 与初始化所需的地图/终点/回调。
    /// </summary>
    /// <remarks>
    /// <para>spec "Map default and row override resolve one enemy key"：请求中的
    /// <see cref="EnemyKey"/> 是行解析后的"生效"普通敌人键（空键已按地图 EnemyTypeIndex
    /// 解析），工厂不反查可变全局配置表。</para>
    /// <para><see cref="StrategyProfile"/> 为该行显式引用的策略乘数 profile（来自
    /// <see cref="OrderedWavePlanSnapshot"/>，由调用方解析后传入），工厂不随机选择。</para>
    /// </remarks>
    internal sealed class EnemySpawnRequest
    {
        /// <summary>生效的普通敌人键（Mob0/Mob1/Mob2/Mob3）。</summary>
        public string EnemyKey { get; }

        /// <summary>是否玩家方车道。</summary>
        public bool IsPlayerLane { get; }

        /// <summary>波次所有权（不可变 waveOrder，随租借携带）。</summary>
        public int WaveOrder { get; }

        /// <summary>0-based 难度索引（同时索引血量曲线、策略乘数、早期乘数）。</summary>
        public int DifficultyIndex { get; }

        /// <summary>该行显式引用的策略乘数 profile。</summary>
        public IReadOnlyList<float> StrategyProfile { get; }

        /// <summary>地图数据（提供路径与坐标 API）。</summary>
        public MapData Map { get; }

        /// <summary>格子尺寸（px）。</summary>
        public float CellSize { get; }

        /// <summary>终点攻击目标（按车道绑定阿斗）。</summary>
        public IEnemyEndPointAttackTarget EndPointTarget { get; }

        /// <summary>击杀奖励回调。</summary>
        public EnemyKilledHandler OnEnemyKilled { get; }

        /// <summary>死亡请求移除回调。</summary>
        public EnemyDeathRequestHandler OnDeathRequested { get; }

        /// <summary>逻辑宽度。</summary>
        public float Width { get; }

        /// <summary>逻辑高度。</summary>
        public float Height { get; }

        /// <summary>构造普通敌人出生请求。</summary>
        /// <exception cref="ArgumentException">enemyKey 为空。</exception>
        /// <exception cref="ArgumentNullException">map / endPointTarget / onEnemyKilled / onDeathRequested 为 null。</exception>
        internal EnemySpawnRequest(
            string enemyKey,
            bool isPlayerLane,
            int waveOrder,
            int difficultyIndex,
            IReadOnlyList<float> strategyProfile,
            MapData map,
            float cellSize,
            IEnemyEndPointAttackTarget endPointTarget,
            EnemyKilledHandler onEnemyKilled,
            EnemyDeathRequestHandler onDeathRequested,
            float width = 40f,
            float height = 40f)
        {
            if (string.IsNullOrEmpty(enemyKey))
            {
                throw new ArgumentException("enemyKey 不能为空", nameof(enemyKey));
            }

            EnemyKey = enemyKey;
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
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// generation-aware 敌人租借身份值对象：runtimeId + generation + waveOrder。
    /// </summary>
    /// <remarks>
    /// <para><b>用途（design.md 决策 5 / task 3.6）：</b>供下一波 EnemyManager 追踪波次
    /// 所有权。出生请求成功后返回本身份；Manager 以完整身份幂等登记/移除，ID 相同但
    /// generation 不同的迟到事实被忽略（防止池复用迟到回调减少新波活动计数）。</para>
    /// <para><b>不变性：</b>值类型，实现值相等与 GetHashCode，可作 HashSet/字典键。</para>
    /// <para><b>无效哨兵：</b><see cref="IsValid"/> 为 false 表示未处于有效租借
    /// （如 Reset 后 runtimeId 归零）。</para>
    /// </remarks>
    internal readonly struct EnemyLeaseIdentity : IEquatable<EnemyLeaseIdentity>
    {
        /// <summary>运行时 ID（由 RuntimeIdAllocator 每次租借分配）。</summary>
        public readonly int RuntimeId;

        /// <summary>租借世代（每次租借单调递增，跨租借不重置）。</summary>
        public readonly long Generation;

        /// <summary>波次所有权（不可变 waveOrder）。</summary>
        public readonly int WaveOrder;

        /// <summary>是否处于有效租借（RuntimeId &gt; 0）。</summary>
        public bool IsValid => RuntimeId > 0;

        /// <summary>构造租借身份。</summary>
        internal EnemyLeaseIdentity(int runtimeId, long generation, int waveOrder)
        {
            RuntimeId = runtimeId;
            Generation = generation;
            WaveOrder = waveOrder;
        }

        /// <inheritdoc/>
        public bool Equals(EnemyLeaseIdentity other)
        {
            return RuntimeId == other.RuntimeId
                && Generation == other.Generation
                && WaveOrder == other.WaveOrder;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is EnemyLeaseIdentity other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RuntimeId;
                hash = hash * 397 ^ Generation.GetHashCode();
                hash = hash * 397 ^ WaveOrder;
                return hash;
            }
        }

        /// <summary>值相等。</summary>
        public static bool operator ==(EnemyLeaseIdentity a, EnemyLeaseIdentity b)
        {
            return a.Equals(b);
        }

        /// <summary>值不等。</summary>
        public static bool operator !=(EnemyLeaseIdentity a, EnemyLeaseIdentity b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"EnemyLeaseIdentity(runtimeId={RuntimeId}, generation={Generation}, waveOrder={WaveOrder})";
        }
    }
}
