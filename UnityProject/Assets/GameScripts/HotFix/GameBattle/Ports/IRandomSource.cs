namespace GameBattle
{
    // ============================================================================
    // 任务 6.5：IRandomSource —— 逻辑随机端口
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Ports/IRandomSource.cs）：
    //   逻辑随机端口，支持固定序列和种子回放。替代 UnityEngine.Random 与全局
    //   Math.random，使战斗模拟在相同种子下可精确复现
    //   （spec battle-simulation "Simulation is reproducible"）。
    //
    // 来源证据：
    //   - DeckManager.js:7 randomSource = Math.random（构造注入随机源）
    //   - DeckManager.js:98-108 drawText 用 randomSource() * pool.length 抽取
    //   - EnemyManager.js:254-260 randomTarget 用随机源选目标
    //   - BattleLoadoutDto.RandomSeed：种子注入确定性 SeededRandomSource
    //   - 1.3 导出为函数式常量随机源 0.5（黄金轨迹可回放）
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase/CombatServices，改为强类型注入。
    //   - spec battle-simulation "Simulation is reproducible"：不依赖未声明的真实时间
    //     或无序集合遍历决定目标、伤害和胜负。
    //   - design.md 第 2 节：随机进度归属 BattleRuntime 独占，每局从 Loadout.RandomSeed
    //     构造新实例，不沿用旧局随机进度。
    //
    // 使用方式：
    //   BattleRuntimeFactory 在组装阶段从 Loadout.RandomSeed 构造 SeededRandomSource，
    //   注入到 DeckManager / EnemyManager 等需要随机的服务。测试可注入固定序列实现
    //   或常量实现以验证确定性。
    //
    // 不变量：
    //   1. 确定性：相同种子 + 相同调用序列 → 相同返回值序列。
    //   2. 不依赖 UnityEngine.Random、Time.deltaTime 或系统时间。
    //   3. 线程安全不要求：所有调用在 Unity 主线程的 Runtime 串行队列中执行。
    // ============================================================================

    /// <summary>
    /// 逻辑随机端口，支持固定序列和种子回放。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Ports/IRandomSource.cs）：</b>
    /// 替代 <c>UnityEngine.Random</c> 与全局 <c>Math.random</c>，使战斗模拟在相同种子下
    /// 可精确复现（spec battle-simulation "Simulation is reproducible"）。</para>
    ///
    /// <para><b>确定性约束：</b>
    /// 相同种子 + 相同调用序列 MUST 产生相同返回值序列。实现 MUST NOT 依赖
    /// <c>UnityEngine.Random</c>、<c>Time.deltaTime</c> 或系统时间。</para>
    ///
    /// <para><b>注入方式（design.md 决策 5）：</b>
    /// 由 <c>BattleRuntimeFactory</c> 在组装阶段从 <c>BattleLoadoutDto.RandomSeed</c>
    /// 构造 <see cref="SeededRandomSource"/>，注入到 <see cref="DeckManager"/>、
    /// <c>EnemyManager</c> 等需要随机的服务。测试可注入固定序列实现或常量实现。</para>
    ///
    /// <para><b>线程安全：</b>不要求。所有调用在 Unity 主线程的 Runtime 串行队列中执行
    /// （design.md:206 / task 6.6：所有输入在 Unity 主线程通过 Runtime 串行队列执行）。</para>
    /// </remarks>
    internal interface IRandomSource
    {
        /// <summary>
        /// 返回 [0, 1) 范围内的随机浮点数。
        /// </summary>
        /// <returns>[0, 1) 随机浮点数。MUST 满足 0 &lt;= 返回值 &lt; 1。</returns>
        /// <remarks>
        /// <para>对应 JS <c>Math.random()</c> 语义，供 <see cref="DeckManager.DrawText"/>
        /// 等方法按 <c>floor(value * pool.Length)</c> 抽取。</para>
        /// <para>实现 MUST 保证返回值在 [0, 1) 半开区间，避免 floor 后越界。</para>
        /// </remarks>
        float NextUnit();

        /// <summary>
        /// 返回 [0, max) 范围内的随机整数。
        /// </summary>
        /// <param name="max">上界（不包含）。MUST 为正数。</param>
        /// <returns>[0, max) 随机整数。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> &lt;= 0。</exception>
        int Next(int max);

        /// <summary>
        /// 返回 [min, max) 范围内的随机整数。
        /// </summary>
        /// <param name="min">下界（包含）。</param>
        /// <param name="max">上界（不包含）。MUST 大于 <paramref name="min"/>。</param>
        /// <returns>[min, max) 随机整数。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> &lt;= <paramref name="min"/>。</exception>
        int Next(int min, int max);

        /// <summary>
        /// 原地洗牌指定列表（Fisher-Yates），使元素顺序随机化。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="list">待洗牌的可变列表（原地修改，非 null）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="list"/> 为 null。</exception>
        /// <remarks>
        /// <para>用于 <see cref="DeckManager"/> 洗牌牌池等场景。洗牌 MUST 基于
        /// <see cref="NextUnit"/> 或 <see cref="Next(int)"/>，保证确定性可复现。</para>
        /// </remarks>
        void Shuffle<T>(System.Collections.Generic.IList<T> list);
    }
}
