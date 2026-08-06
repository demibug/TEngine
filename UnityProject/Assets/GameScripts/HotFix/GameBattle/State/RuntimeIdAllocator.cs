namespace GameBattle
{
    // ============================================================================
    // 任务 3.7：RuntimeIdAllocator —— 确定性运行时 ID 分配器
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / State/RuntimeIdAllocator.cs）：
    //   为每次实体获取分配确定性的新运行时 ID，池复用不得沿用旧 ID。
    //
    // 来源证据（CriticalGameState.js:37-58）：
    //   还原工程 GameDataCore 持有 runtimeId 字段，allocateRuntimeId()/xy()
    //   每次调用执行 `this.runtimeId += 1; return this.runtimeId;`。
    //   该 ID 用于敌人、单位、投射物、攻击效果等实体在本局内的唯一标识。
    //
    // 决策依据（design.md 决策 5 / spec battle-simulation "Simulation is reproducible"）：
    //   - 删除 SingletonBase 与字符串服务容器（design 决策 5），ID 分配改为强类型
    //     注入的独立值对象，不再挂在全局 GameDataCore 单例上。
    //   - 确定性：相同输入序列产生相同 ID 序列（从 1 递增），供黄金轨迹对照。
    //   - 每局重置：重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 Allocator，
    //     runtimeId 从 0 开始，不复用旧局 ID 空间（spec "Restart creates clean per-battle state"）。
    //
    // 不变量：
    //   1. 每局新建/销毁：不跨局复用，重开由 BattleRuntimeFactory 新建。
    //   2. 单调递增：每次分配返回的 ID 严格大于上一次，从 1 开始。
    //   3. 线程安全：本类型由 BattleRuntime 在逻辑线程（Unity 主线程）单线程访问，
    //      不加锁；若后续异步访问需另行封装。
    // ============================================================================

    /// <summary>
    /// 确定性运行时 ID 分配器：每局新建，从 1 单调递增，池复用不复用旧 ID。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md State/RuntimeIdAllocator.cs）：</b>为每次实体获取分配
    /// 确定性的新运行时 ID，替代还原工程 <c>GameDataCore.runtimeId</c> + <c>allocateRuntimeId()</c>
    /// （<c>CriticalGameState.js:37-58</c>）。</para>
    ///
    /// <para><b>确定性（spec battle-simulation "Simulation is reproducible"）：</b>
    /// 相同输入序列产生相同 ID 序列（1, 2, 3, ...），供 JS/C# 黄金轨迹对照。</para>
    ///
    /// <para><b>每局重置（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 <see cref="BattleRuntimeFactory"/> 产生新
    /// Allocator，<see cref="_nextId"/> 从 0 开始，不复用旧局 ID 空间。</para>
    ///
    /// <para><b>池复用不复用旧 ID（design.md 目录表）：</b>对象池回收实体后，再次
    /// Acquire 必须通过本分配器获取新 ID，旧 ID 不得继续有效（task 4.5/4.7 验证）。</para>
    ///
    /// <para><b>线程安全：</b>本类型由 BattleRuntime 在 Unity 主线程单线程访问，不加锁。
    /// 若后续异步访问需另行封装。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 Manager/Factory 使用，
    /// 不对其他程序集暴露。外部通过 <see cref="BattleReadModel"/> 只读快照观察 ID。</para>
    /// </remarks>
    internal sealed class RuntimeIdAllocator
    {
        /// <summary>
        /// 下一个待分配的 ID。从 0 初始化，首次分配返回 1（对应还原工程
        /// <c>runtimeId = 0</c> 初始化 + <c>runtimeId += 1; return runtimeId</c> 语义）。
        /// </summary>
        private int _nextId;

        /// <summary>
        /// 构造新的 ID 分配器，从 1 开始分配。
        /// </summary>
        /// <remarks>
        /// 由 <see cref="BattleRuntimeFactory"/> 在每次 <c>Create</c> 时构造新实例，
        /// 保证每局 ID 空间独立。
        /// </remarks>
        internal RuntimeIdAllocator()
        {
            _nextId = 0;
        }

        /// <summary>
        /// 已分配的最大 ID。未分配过时为 0。
        /// <para>供 <see cref="BattleReadModel"/> 诊断快照与黄金轨迹对照使用。</para>
        /// </summary>
        internal int LastAllocatedId => _nextId;

        /// <summary>
        /// 分配一个新的运行时 ID。从 1 开始单调递增。
        /// </summary>
        /// <returns>新分配的运行时 ID，严格大于上一次分配的值。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>CriticalGameState.js:57</c>：
        /// <code>this.runtimeId += 1; return this.runtimeId;</code></para>
        /// <para>每次调用必定返回新值，池复用实体必须重新调用本方法获取新 ID，
        /// 旧 ID 不得继续有效（design.md 目录表 / task 4.5）。</para>
        /// </remarks>
        internal int Allocate()
        {
            _nextId += 1;
            return _nextId;
        }

        /// <summary>
        /// 重置分配器到初始状态（仅供测试或重置场景使用）。
        /// </summary>
        /// <remarks>
        /// <para>生产代码中重开通过 <see cref="BattleRuntimeFactory"/> 新建 Allocator 实现，
        /// 不调用本方法。本方法供测试在同一个 Allocator 上验证确定性时使用。</para>
        /// </remarks>
        internal void Reset()
        {
            _nextId = 0;
        }
    }
}
