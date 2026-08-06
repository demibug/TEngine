namespace GameBattle
{
    // ============================================================================
    // 任务 4.1：IPoolableBattleObject —— 可池化战斗对象的可验证契约
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Pooling/IPoolableBattleObject.cs）：
    //   定义获取、回收和 Reset 的可验证契约。所有由 BattleObjectPool 池化的
    //   纯逻辑对象（Mob0Enemy / SimpleDynamicArrow / 攻击效果等）MUST 实现本接口，
    //   以保证 Acquire/Release 对称、活动租借计数和完整 Reset。
    //
    // 来源证据（还原工程池复位契约）：
    //   - ObjectPool.js：按 class / 按 key 的逻辑对象池，recover 时先 reset 再入池，
    //     __InPool 标记防止重复回收。
    //   - AnimationEntityPool.js：表现对象池，recover 前必须调用 resetForPool / Td，
    //     资源路径和池键保持原代码。
    //   - enemy-pool-reset-contract.md：每次出生必须复位 ID、阵营、生命、路径、
    //     攻击冷却、接触回调标记、位置、击退、贡献者；回收顺序固定。
    //   - friendly-unit-pool-reset-contract.md：必须复位运行时 ID、阵营、等级、
    //     攻击力、目标数组、攻击冷却、定时器、事件监听。
    //   - projectile-pool-reset-contract.md：必须清除 projectileId、目标、起点、
    //     进度、命中集合、状态标记；重复回收返回 false。
    //   - 11_POOLING_AND_OWNERSHIP.md：复用前必须清理 ID、owner/target、side、
    //     position/progress、health、state、cooldown、timers、listeners、weapon/projectile
    //     references、lifecycle token。
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase / CombatServices / GameObjectEventProxy，
    //     池化改为按明确类型的强类型对象池，替代全局反射式对象池。
    //   - design.md 决策 3：BattleRuntime 独占活动实体；BattlePoolScope 区分可跨局
    //     复用的池容量与必须逐局清空的活动对象。
    //   - spec battle-runtime-lifecycle "Runtime quiescence and cleanup have one ordered owner"：
    //     对象 MUST 先完成 Reset 并归还池，之后才能在重开时复用池容量。
    //   - task 4.1 约束：Acquire/Release 对称，活动租借计数，完整 Reset 契约；
    //     不预热、不硬编码容量；同次战斗会话内复用已清空的高水位容量。
    //
    // 不变量：
    //   1. Acquire/Release 对称：每次 Acquire 必须对应恰好一次 Release。
    //   2. 完整 Reset：Release 前必须执行 ResetState，回收后无残留状态。
    //   3. 重复 Release 安全：已回收对象再次 Release 返回 false，不重复入池。
    //   4. 池复用无污染：Acquire 取得的复用对象状态等价于新构造对象。
    // ============================================================================
    //
    // 注：本接口不依赖 UnityEngine，纯逻辑对象可在 EditMode 无需 Scene 测试。
    // ============================================================================

    /// <summary>
    /// 可池化战斗对象的可验证契约。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>定义获取、回收和 Reset 的可验证契约。
    /// 所有由 <see cref="BattleObjectPool{T}"/> 池化的纯逻辑对象 MUST 实现本接口。</para>
    ///
    /// <para><b>Acquire/Release 对称（task 4.1）：</b>
    /// 每次 <see cref="BattleObjectPool{T}.Acquire"/> MUST 对应恰好一次
    /// <see cref="BattleObjectPool{T}.Release"/>。活动租借计数（ActiveCount）追踪未归还的
    /// 租借对象数量，用于 Settling 静默清理断言与退出清理验证。</para>
    ///
    /// <para><b>完整 Reset 契约（task 4.1 / 还原工程池复位契约）：</b>
    /// <see cref="ResetState"/> 在 Release 前由池调用，MUST 清除全部可变状态，包括：
    /// </para>
    /// <list type="bullet">
    /// <item>运行时 ID、阵营、特殊标记（enemy-pool-reset-contract.md）</item>
    /// <item>目标引用、攻击者引用、所有者引用（11_POOLING_AND_OWNERSHIP.md）</item>
    /// <item>位置、路径索引、剩余路径距离、网格坐标</item>
    /// <item>生命、最大生命、速度、动画状态</item>
    /// <item>攻击冷却、接触回调标记、攻击状态</item>
    /// <item>击退、移动锁、停止移动标记</item>
    /// <item>伤害贡献者数组、表现计数</item>
    /// <item>活动定时器、事件监听、生命周期代号（friendly-unit-pool-reset-contract.md）</item>
    /// <item>命中敌人 ID 集合、归一化进度、命中启用状态（projectile-pool-reset-contract.md）</item>
    /// </list>
    /// <para>Reset 后对象状态等价于新构造对象，保证池复用无污染。</para>
    ///
    /// <para><b>重复 Release 安全（ObjectPool.js __InPool 语义）：</b>
    /// 已回收的对象再次 Release 返回 false，不重复入池。对应还原工程
    /// <c>if (!value || value.__InPool) return false;</c> 防御。</para>
    ///
    /// <para><b>本接口为 internal：</b>只供 GameBattle 内部池化对象实现，
    /// 不对其他程序集暴露。</para>
    /// </remarks>
    internal interface IPoolableBattleObject
    {
        /// <summary>
        /// 重置对象到等价于新构造的状态。
        /// </summary>
        /// <remarks>
        /// <para><b>调用时机：</b>由 <see cref="BattleObjectPool{T}.Release"/> 在归还对象前
        /// 调用。Acquire 复用对象时不再调用（Reset 只在 Release 时执行一次）。</para>
        ///
        /// <para><b>完整性要求（还原工程池复位契约）：</b>
        /// MUST 清除全部可变状态，使对象等价于新构造。遗漏任一字段会导致池复用污染
        /// （旧目标/路径/冷却/监听进入新生命周期）。具体需要清除的字段清单见
        /// enemy-pool-reset-contract.md / friendly-unit-pool-reset-contract.md /
        /// projectile-pool-reset-contract.md / 11_POOLING_AND_OWNERSHIP.md。</para>
        ///
        /// <para><b>幂等性：</b>多次调用安全，结果状态相同。</para>
        ///
        /// <para><b>不抛出：</b>实现 MUST NOT 抛出异常。若内部清理失败，记录日志但仍
        /// 返回，保证池回收不因单个对象清理失败而中断。</para>
        /// </remarks>
        void ResetState();
    }
}
