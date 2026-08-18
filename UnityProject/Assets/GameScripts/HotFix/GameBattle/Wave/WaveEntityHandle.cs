using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.1：WaveEntityKind + WaveEntityHandle —— 波次所有权 handle 值对象
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 5 / specs/ordered-wave-plan/spec.md）：
    //   每次成功出生返回一个 generation-aware 的 WaveEntityHandle，供 WaveManager
    //   以完整值幂等登记/移除并追踪波次所有权，抵抗池复用迟到回调。
    //
    // 契约（任务 4.1 / task 4.5）：
    //   1. 不可变值类型：runtimeId + generation + waveOrder + kind，完整值相等 + 稳定 hash。
    //   2. 相同 runtimeId 但 generation 不同不匹配（迟到事实被忽略）。
    //   3. 从 EnemyLeaseIdentity 转换 Normal handle 的清晰入口（FromEnemyLease）。
    //   4. Boss 与普通敌人共用同一 handle 语义；Boss 不冒充 Normal。
    //
    // 不变量：
    //   1. IsValid 以 RuntimeId > 0 判定（对应 EnemyLeaseIdentity.IsValid）。
    //   2. 值类型可作 HashSet<WaveEntityHandle> 键。
    //   3. 纯逻辑，无反射/dynamic/Emit，热更安全。
    // ============================================================================

    /// <summary>
    /// 波次实体种类：普通敌人 / Boss。
    /// </summary>
    /// <remarks>
    /// <para>用于 <see cref="WaveEntityHandle.Kind"/>，使 Boss 出生事实与普通敌人
    /// 在波次所有权上可区分，且 Boss 波绝不冒充 Normal。</para>
    /// </remarks>
    internal enum WaveEntityKind
    {
        /// <summary>普通敌人（由 Normal spawn handler 出生）。</summary>
        Normal = 1,

        /// <summary>Boss（由 IBossWavePort 出生）。</summary>
        Boss = 2,
    }

    /// <summary>
    /// generation-aware 波次所有权 handle：runtimeId + generation + waveOrder + kind。
    /// </summary>
    /// <remarks>
    /// <para><b>用途（design.md 决策 5 / task 4.1/4.5）：</b>每次成功 spawn 由 spawn
    /// handler / Boss port 返回；WaveManager 以完整 handle 幂等登记
    /// （HashSet），按完整值删除。ID 相同但 generation 不同的迟到事实不匹配
    /// （防止池复用迟到回调减少新波活动计数）。</para>
    /// <para><b>Boss 与普通共用：</b><see cref="Kind"/> 区分 Normal/Boss；
    /// Boss 出生返回 Kind=Boss 的 handle，不冒充 Normal。</para>
    /// <para><b>转换入口：</b><see cref="FromEnemyLease"/> 把
    /// <see cref="EnemyLeaseIdentity"/>（普通敌人租借身份）转换为 Normal handle，
    /// 供 EnemyManager 移除事实接线（下一波 Runtime 集成）。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 WaveManager 与测试使用。</para>
    /// </remarks>
    internal readonly struct WaveEntityHandle : IEquatable<WaveEntityHandle>
    {
        /// <summary>运行时 ID（由 RuntimeIdAllocator 每次租借分配，> 0 有效）。</summary>
        public readonly int RuntimeId;

        /// <summary>租借世代（每次租借单调递增，跨租借不重置）。</summary>
        public readonly long Generation;

        /// <summary>波次所有权（不可变 waveOrder，对应配置行 Order）。</summary>
        public readonly int WaveOrder;

        /// <summary>波次实体种类（Normal / Boss）。</summary>
        public readonly WaveEntityKind Kind;

        /// <summary>是否有效 handle（RuntimeId &gt; 0）。</summary>
        public bool IsValid => RuntimeId > 0;

        /// <summary>构造波次所有权 handle。</summary>
        /// <param name="runtimeId">运行时 ID（> 0 有效）。</param>
        /// <param name="generation">租借世代。</param>
        /// <param name="waveOrder">波次所有权（配置行 Order）。</param>
        /// <param name="kind">波次实体种类。</param>
        internal WaveEntityHandle(int runtimeId, long generation, int waveOrder, WaveEntityKind kind)
        {
            RuntimeId = runtimeId;
            Generation = generation;
            WaveOrder = waveOrder;
            Kind = kind;
        }

        /// <summary>
        /// 把普通敌人租借身份转换为 Normal 波次 handle（清晰转换入口）。
        /// </summary>
        /// <param name="lease">普通敌人租借身份（EnemyLeaseIdentity）。</param>
        /// <returns>Kind=Normal 的波次 handle。</returns>
        /// <remarks>
        /// <para>供下一波 Runtime 集成：EnemyManager 的 WaveEntityRemoved 事实携带
        /// <see cref="EnemyLeaseIdentity"/>，经本入口转换为 WaveEntityHandle 后
        /// 交给 WaveManager 幂等移除。</para>
        /// </remarks>
        internal static WaveEntityHandle FromEnemyLease(EnemyLeaseIdentity lease)
        {
            return new WaveEntityHandle(lease.RuntimeId, lease.Generation, lease.WaveOrder, WaveEntityKind.Normal);
        }

        /// <inheritdoc/>
        public bool Equals(WaveEntityHandle other)
        {
            return RuntimeId == other.RuntimeId
                && Generation == other.Generation
                && WaveOrder == other.WaveOrder
                && Kind == other.Kind;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is WaveEntityHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RuntimeId;
                hash = hash * 397 ^ Generation.GetHashCode();
                hash = hash * 397 ^ WaveOrder;
                hash = hash * 397 ^ (int)Kind;
                return hash;
            }
        }

        /// <summary>值相等。</summary>
        public static bool operator ==(WaveEntityHandle a, WaveEntityHandle b)
        {
            return a.Equals(b);
        }

        /// <summary>值不等。</summary>
        public static bool operator !=(WaveEntityHandle a, WaveEntityHandle b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WaveEntityHandle(runtimeId={RuntimeId}, generation={Generation}, waveOrder={WaveOrder}, kind={Kind})";
        }
    }
}
