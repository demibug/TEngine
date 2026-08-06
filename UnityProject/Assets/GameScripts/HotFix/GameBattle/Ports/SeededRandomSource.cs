using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.5：SeededRandomSource —— 确定性种子随机源实现
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Ports/SeededRandomSource.cs）：
    //   不依赖 UnityEngine.Random 的确定性实现。从固定种子出发，相同种子 + 相同调用
    //   序列产生相同返回值序列，满足 spec battle-simulation "Simulation is reproducible"。
    //
    // 来源证据：
    //   - BattleLoadoutDto.RandomSeed：种子注入确定性 SeededRandomSource
    //     （spec 双时钟与黄金轨迹要求可回放随机序列）
    //   - 1.3 导出为函数式常量随机源 0.5（黄金轨迹可回放）
    //   - EnemyManager.cs:1178 DefaultRandom fallback 注释："生产环境应通过构造函数注入
    //     确定性随机源（如 SeededRandomSource，Ports/SeededRandomSource.cs，后续 task）"
    //
    // 算法选择：
    //   使用 System.Random 的种子构造重载（new Random(seed)）。.NET Framework / Unity
    //   Mono 的 System.Random 在相同种子下确定性可复现。不使用 UnityEngine.Random
    //   （其种子与平台相关，不可跨平台复现）。
    //
    //   注意：.NET Core 3.0+ 的 System.Random 构造函数在相同种子下也确定性，
    //   但算法与 .NET Framework 不同。Unity 2021+ 使用 Mono/CoreCLR，本实现依赖
    //   Mono 的 System.Random 种子确定性。若未来切换运行时导致确定性变化，
    //   需替换为自实现 PRNG（如 xorshift）并重新生成黄金轨迹——这属于批准的行为偏差。
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase/CombatServices，改为强类型注入。
    //   - spec battle-simulation "Simulation is reproducible"：不依赖未声明的真实时间。
    //   - design.md 第 3 节：随机进度归属 BattleRuntime 独占，每局从 Loadout.RandomSeed
    //     构造新实例，不沿用旧局随机进度。
    //
    // 不变量：
    //   1. 确定性：相同 seed + 相同调用序列 → 相同返回值序列。
    //   2. 不依赖 UnityEngine.Random、Time.deltaTime 或系统时间。
    //   3. NextUnit 返回 [0, 1) 半开区间。
    //   4. 每局新建：由 BattleRuntimeFactory 从 Loadout.RandomSeed 构造，不跨局复用。
    // ============================================================================

    /// <summary>
    /// 确定性种子随机源实现，不依赖 <see cref="UnityEngine.Random"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Ports/SeededRandomSource.cs）：</b>
    /// 从固定种子出发，相同种子 + 相同调用序列产生相同返回值序列
    /// （spec battle-simulation "Simulation is reproducible"）。</para>
    ///
    /// <para><b>算法：</b>使用 <see cref="System.Random"/> 的种子构造重载。
    /// Unity Mono 运行时下 <c>new Random(seed)</c> 在相同种子下确定性可复现。
    /// 不使用 <c>UnityEngine.Random</c>（其种子与平台相关，不可跨平台复现）。</para>
    ///
    /// <para><b>每局新建（design.md 第 3 节）：</b>
    /// 由 <c>BattleRuntimeFactory</c> 从 <c>BattleLoadoutDto.RandomSeed</c> 构造新实例，
    /// 注入到 <see cref="DeckManager"/> 等服务。不跨局复用随机进度
    /// （重开时销毁旧 Runtime，新建 RandomSource 从相同或新种子出发）。</para>
    ///
    /// <para><b>线程安全：</b>不要求。所有调用在 Unity 主线程的 Runtime 串行队列中执行。
    /// <see cref="System.Random"/> 非线程安全，本类型亦不做同步——单局内只由主线程访问。</para>
    /// </remarks>
    internal sealed class SeededRandomSource : IRandomSource
    {
        // ====================================================================
        // 底层 RNG
        // ====================================================================

        /// <summary>
        /// 底层种子随机数生成器。Unity Mono 下相同种子确定性可复现。
        /// <para>不使用 UnityEngine.Random，避免跨平台种子差异。</para>
        /// </summary>
        private readonly Random _rng;

        /// <summary>
        /// 构造时使用的种子（只读副本，用于日志/诊断）。
        /// </summary>
        internal int Seed { get; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造确定性种子随机源。
        /// </summary>
        /// <param name="seed">
        /// 随机种子。相同种子 + 相同调用序列 → 相同返回值序列。
        /// 0 为合法种子值（由 <c>BattleLoadoutDto.CreateMinimalDefault</c> 使用）。
        /// </param>
        /// <remarks>
        /// <para>由 <c>BattleRuntimeFactory</c> 在组装阶段从
        /// <c>BattleLoadoutDto.RandomSeed</c> 构造。每局新建实例，不跨局复用。</para>
        /// </remarks>
        internal SeededRandomSource(int seed)
        {
            Seed = seed;
            _rng = new Random(seed);
        }

        // ====================================================================
        // IRandomSource 实现
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>使用 <see cref="Random.NextDouble"/> 并显式钳制到 [0, 1) 半开区间，
        /// 防止极端情况返回 1.0 导致 floor 后越界（对应 JS DeckManager.js:102
        /// 的 <c>Math.max(0, Math.min(0.999999999, ...))</c> 钳制语义）。</para>
        /// </remarks>
        public float NextUnit()
        {
            // NextDouble 返回 [0, 1)，但防御性钳制以跨运行时保证半开区间。
            double value = _rng.NextDouble();
            if (value < 0d)
            {
                value = 0d;
            }
            else if (value >= 1d)
            {
                // 理论上 NextDouble 不返回 1.0，但防御性钳制保证 floor 后不越界。
                value = 0.999999999d;
            }
            return (float)value;
        }

        /// <inheritdoc/>
        public int Next(int max)
        {
            if (max <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "max 必须为正数");
            }
            return _rng.Next(max);
        }

        /// <inheritdoc/>
        public int Next(int min, int max)
        {
            if (max <= min)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max,
                    "max 必须大于 min");
            }
            return _rng.Next(min, max);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>Fisher-Yates 洗牌，从末尾向前交换，每个位置用 <see cref="Next(int)"/>
        /// 选取交换目标。确定性可复现。</para>
        /// </remarks>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }
            int n = list.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }
}
