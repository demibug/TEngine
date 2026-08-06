using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.8：ProjectileManager —— 投射物唯一推进拥有者、稳定有序集合、移除队列与 Settling 静默清理
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Projectile/ProjectileManager.cs）：
    //   每子步推进一次活动弹道，处理移除队列和战斗结束清空。
    //
    // 来源证据（还原工程 ProjectileManager.js:1-181，原始符号 vA）：
    //   还原工程 ProjectileManager 继承 SingletonBase，由 GameLoop 字符串 callback Map
    //   以 'bulletMgr' 键驱动。核心数据：
    //     - activeProjectiles: array           // 活动投射物有序集合（数组，插入顺序）
    //     - updateLog / removalLog: array      // 诊断日志
    //   核心方法：
    //     - create(config, startPoint)：factory.produce → resetData → push 到数组
    //     - update(deltaMs)：逆序遍历（尾→头），允许当前索引同步 splice
    //         * movement.update(deltaMs, speedScale) → projectile.update(deltaMs)
    //         * 触发 hitStrategy（requestRemove/hitEnable/both + delay 倒计时）
    //         * shouldRemove（出界 / requestedRemoval / removeAfterHit）→ _removeAt
    //     - remove/removeById → _removeAt
    //     - gameOver()：逆序遍历 _removeAt 再清空数组
    //     - _removeAt(index, reason)：movement.beforeRecover → factory.recover → splice
    //
    // 决策依据：
    //   - design.md 第 1 节目录表："每子步推进一次活动弹道，处理移除队列和战斗结束清空。"
    //   - design.md 第 2 节更新顺序：ProjectileManager.update 在 EnemyManager 之后、
    //     BattleManager 之前执行；C# 由 BattleSimulation 在 BattleUpdatePhase.Projectile
    //     阶段回调，每子步一次（决策 0.3）。
    //   - design.md 第 282 行：新箭因 Projectile 阶段已过去，下一子步才首次移动。
    //   - spec battle-simulation "Projectile is launched after projectile phase"：
    //     攻击释放时序在既有弹道推进后创建新投射物，新投射物直到下一子步才首次移动。
    //   - spec battle-simulation "Update phases are explicit and single-owned"：
    //     每个系统每子步最多更新一次。
    //   - task 5.8 核心约束：投射物每子步只由 Manager 推进一次（唯一推进拥有者）。
    //   - task 4.6 约束（稳定有序集合）：禁止依赖 Dictionary/HashSet 未定义遍历顺序。
    //   - 决策 0.4 / spec "Battle result is frozen once"：首次 TryFreeze 成功后只完成
    //     当前同步提交并中止剩余 phase/子步；TryFreeze 不在嵌套伤害调用栈内重入销毁集合。
    //   - spec battle-runtime-lifecycle "Settling has no gameplay damage authority" /
    //     "Runtime quiescence and cleanup have one ordered owner"：Settling 静默清理顺序中
    //     "清理 ProjectileManager" 在 "清理 AttackEffectManager" 之后、"清理 EnemyManager"
    //     之前，MUST 取消空中弹道且不造成伤害。
    //
    // 唯一推进拥有者设计（task 5.8 核心约束）：
    //   投射物每子步只由本类型.Update 调用 ProjectileBase.Advance 一次。
    //   ProjectileAttackEffect（task 5.8）虽实现 IAttackEffect 并由 AttackEffectManager 跟踪，
    //   但其 Update 方法委托到 ProjectileBase.Advance —— 为保证"每子步只推进一次"，
    //   ProjectileAttackEffect.Update 为空操作（投射物推进的唯一入口是本类型.Update）。
    //   见 ProjectileAttackEffect.cs 的设计说明。
    //
    // 稳定有序集合设计（task 4.6 约束，镜像 EnemyManager 模式）：
    //   JS 原版 activeProjectiles 为数组，遍历顺序 = 插入顺序。C# 采用 List<ProjectileBase>
    //   作为有序活动集合，保证遍历顺序 = 创建顺序。移除请求入 List 队列，遍历结束后
    //   统一处理（与 EnemyManager/AttackEffectManager 模式一致）。
    //
    // 新箭下一子步才移动（task 2.11 / 0.9 / spec "Projectile is launched after projectile phase"）：
    //   Primary 保证：BattleSimulation 的阶段顺序固定为 Projectile → ... → AttackRelease，
    //   攻击释放阶段（弓兵创建投射物）在 Projectile 阶段之后，故新箭不在创建子步的
    //   Projectile 阶段被遍历到。本类型.Update 只遍历调用时已在 _projectiles 中的投射物，
    //   创建于本子步后续阶段的新箭自然不被推进。
    //   防御性双保险：ProjectileBase.Advance 首次调用时若 frameNowMs 等于创建帧则跳过推进
    //   （task 5.7 已实现 MarkCreationFrame 守卫）。
    //
    // 不变量：
    //   1. 唯一推进：projectile.Advance 只由本类型.Update 在 Projectile 阶段调用，每子步每投射物一次。
    //   2. 稳定有序：活动集合基于 List（创建顺序），不依赖 Dictionary/HashSet 遍历顺序。
    //   3. 移除队列：同子步内多个完成/取消请求不重入修改集合，先入队再统一处理。
    //   4. 冻结中止：IsFrozen 后 Update 直接返回；遍历中检测冻结则停止剩余迭代。
    //   5. Settling 静默清理：Clear 取消全部空中弹道且不造成伤害（只回收，不调 Advance/Hit）。
    //   6. 池回收对称：移除时经 ProjectileFactory.Release 归还池。
    //   7. 幂等清理：Clear/GameOver 可重复调用，后续调用为空操作。
    // ============================================================================

    /// <summary>
    /// 投射物唯一推进拥有者：每子步推进一次活动弹道，处理移除队列与 Settling 静默清理。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>每子步推进一次活动弹道，处理移除队列和
    /// 战斗结束清空。替代还原工程 <c>ProjectileManager.js</c>（<c>ProjectileManager.js:1-181</c>，
    /// 原始符号 vA）。</para>
    ///
    /// <para><b>唯一推进（task 5.8 核心约束 / spec "Update phases are explicit and single-owned"）：</b>
    /// <see cref="Update"/> 是活动投射物的唯一推进入口，由 <see cref="BattleSimulation"/> 在
    /// <see cref="BattleUpdatePhase.Projectile"/> 阶段回调，每子步一次。每个投射物每子步只
    /// 调用一次 <see cref="ProjectileBase.Advance"/>。<see cref="ProjectileAttackEffect"/>
    /// 虽实现 <see cref="IAttackEffect"/> 供 <see cref="AttackEffectManager"/> 跟踪/取消，
    /// 但其 <c>Update</c> 方法为空操作——投射物推进的唯一入口是本类型。<c>Update</c>。</para>
    ///
    /// <para><b>稳定有序集合（task 4.6 约束）：</b>
    /// 活动投射物集合使用 <see cref="List{T}"/>（创建顺序），不依赖 Dictionary/HashSet 的
    /// 未定义遍历顺序。遍历前复制快照到 <c>_updateBuffer</c>，避免遍历中修改集合导致迭代异常
    /// （对应 JS 逆序遍历 + splice 的等价语义）。移除请求入 <c>_removeQueue</c>，
    /// 遍历结束后由 <see cref="ProcessRemoveQueue"/> 统一处理。</para>
    ///
    /// <para><b>新箭下一子步才移动（task 2.11 / 0.9 / spec "Projectile is launched after projectile phase"）：</b>
    /// Primary 保证是阶段顺序：BattleSimulation 固定为 Projectile → ... → AttackRelease，
    /// 攻击释放阶段创建的新箭不在创建子步的 Projectile 阶段被遍历到。本类型.Update 只遍历
    /// 调用时已在 <c>_projectiles</c> 中的投射物。防御性双保险：
    /// <see cref="ProjectileBase.Advance"/> 首次调用时若 frameNowMs 等于创建帧则跳过推进
    /// （task 5.7 已实现 <see cref="ProjectileBase.MarkCreationFrame"/> 守卫）。</para>
    ///
    /// <para><b>移除队列（与 EnemyManager/AttackEffectManager 模式一致）：</b>
    /// 同子步内多个完成/取消请求不重入修改集合，先入 <c>_removeQueue</c> 再统一处理。
    /// 对应决策 0.4 "TryFreeze 不在嵌套伤害调用栈内重入销毁集合"。</para>
    ///
    /// <para><b>池回收（task 5.7 + task 4.1）：</b>
    /// 移除时经 <see cref="ProjectileFactory.Release"/> 归还池，保证 Acquire/Release 对称。</para>
    ///
    /// <para><b>冻结中止（决策 0.4 / spec "Battle result is frozen once"）：</b>
    /// 若 <see cref="IsFrozen"/> 为 true，<see cref="Update"/> 直接返回，不推进剩余投射物。
    /// 遍历中若检测到冻结，停止剩余迭代。冻结标志由外部（BattleRuntime.EnterSettling）设置。</para>
    ///
    /// <para><b>Settling 静默清理（spec "Settling has no gameplay damage authority" /
    /// "Runtime quiescence and cleanup have one ordered owner"）：</b>
    /// <see cref="Clear"/> 取消并回收全部空中弹道，不造成伤害（只调
    /// <see cref="ProjectileFactory.Release"/>，不调 <see cref="ProjectileBase.Advance"/> 或
    /// 任何伤害提交）。由 BattleRuntime.EnterSettling 在"清理 ProjectileManager"步骤调用。
    /// 幂等。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 ProjectileManager。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleRuntime 在阶段回调中调用，
    /// 不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class ProjectileManager
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀，便于在日志中筛选投射物管理器相关条目。</summary>
        private const string LogTag = "[ProjectileManager]";

        // ====================================================================
        // 活动投射物集合：有序 List（创建顺序），保证遍历顺序确定
        // ====================================================================

        /// <summary>
        /// 活动投射物有序集合（对应 JS <c>activeProjectiles: array</c>）。
        /// <para><b>稳定有序（task 4.6 约束）：</b>使用 <see cref="List{T}"/> 而非
        /// <see cref="HashSet{T}"/>，保证遍历顺序 = 创建顺序（对应 JS 数组的插入顺序遍历）。
        /// 不依赖 Dictionary/HashSet 的未定义遍历顺序决定投射物推进或移除。</para>
        /// <para>移除时从列表删除（O(n)，单局活动投射物数量可控，可接受）。</para>
        /// </summary>
        private readonly List<ProjectileBase> _projectiles = new List<ProjectileBase>();

        // ====================================================================
        // 更新快照缓冲（对应 JS 逆序遍历的等价语义：快照遍历避免迭代异常）
        // ====================================================================

        /// <summary>
        /// 更新快照缓冲：遍历前复制活动投射物引用，避免遍历中修改集合导致迭代异常。
        /// <para>JS 原版采用逆序遍历 + splice 实现遍历中移除。C# 移植改为快照遍历 +
        /// 移除队列统一处理（与 EnemyManager/AttackEffectManager 模式一致），语义等价且
        /// 避免逆序遍历在嵌套命中调用栈中的可读性问题。</para>
        /// </summary>
        private readonly List<ProjectileBase> _updateBuffer = new List<ProjectileBase>();

        // ====================================================================
        // 移除队列：同子步完成/取消请求先入队，遍历结束后统一处理
        // ====================================================================

        /// <summary>
        /// 延迟移除队列：完成/取消的投射物入队，遍历结束后由
        /// <see cref="ProcessRemoveQueue"/> 统一处理。
        /// <para>对应决策 0.4 "TryFreeze 不在嵌套伤害调用栈内重入销毁集合"——
        /// 同子步内多个完成请求不重入修改集合，先入队再统一处理。</para>
        /// <para>使用 <see cref="List{T}"/> 而非 <see cref="Queue{T}"/> 以便诊断时观察与去重。</para>
        /// </summary>
        private readonly List<ProjectileBase> _removeQueue = new List<ProjectileBase>();

        /// <summary>
        /// 投射物→移除原因临时映射（仅用于诊断日志）。
        /// <para>使用 <see cref="Dictionary{TKey, TValue}"/> 仅用于 O(1) 查找原因，
        /// 不用于决定遍历顺序。处理完后移除条目。</para>
        /// </summary>
        private readonly Dictionary<ProjectileBase, string> _removeReasons =
            new Dictionary<ProjectileBase, string>();

        // ====================================================================
        // 注入依赖
        // ====================================================================

        /// <summary>投射物工厂，供 Acquire/Release 池回收。不可为 null。</summary>
        private readonly ProjectileFactory _factory;

        // ====================================================================
        // 冻结标志（决策 0.4 / Settling）
        // ====================================================================

        /// <summary>
        /// 是否已冻结。冻结后 <see cref="Update"/> 直接返回，不再推进剩余投射物。
        /// <para>由外部（<c>BattleRuntime.EnterSettling</c>）设置。对应决策 0.4
        /// "TryFreeze 后中止当前 phase 剩余迭代"与 spec "Settling has no gameplay damage authority"。</para>
        /// </summary>
        internal bool IsFrozen { get; set; }

        /// <summary>
        /// 是否已清理（<see cref="Clear"/> 已调用）。幂等清理标志。
        /// </summary>
        internal bool IsCleared { get; private set; }

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>
        /// 当前活动投射物数量（对应 JS <c>activeCount</c>）。
        /// </summary>
        internal int ActiveCount => _projectiles.Count;

        /// <summary>
        /// 累计 Update 调用次数（诊断用）。
        /// </summary>
        internal int UpdateCount { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造投射物管理器，注入投射物工厂。
        /// </summary>
        /// <param name="factory">投射物工厂。每局新建。不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 null。</exception>
        /// <remarks>
        /// <para>由 <c>BattleRuntimeFactory</c> 在每次 Create 时构造新实例，保证每局独立。</para>
        /// <para>JS 原版的 configure({ gameLoop, enemyManager, gameData, projectileFactory, laya })
        /// 在 C# 移植中简化为只注入 ProjectileFactory——gameLoop 由 BattleSimulation 阶段回调替代，
        /// enemyManager/gameData 已由 ProjectileFactory 内部持有（供策略 Configure），
        /// laya 表现依赖被删除（design.md 第 9 行纯逻辑约束）。</para>
        /// </remarks>
        internal ProjectileManager(ProjectileFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            IsFrozen = false;
            IsCleared = false;
        }

        // ====================================================================
        // 登记 —— 由 BowSoldier/AttackScheduler 在创建投射物后调用
        // ====================================================================

        /// <summary>
        /// 登记一个已发射的活动投射物到管理器（对应 JS <c>create</c> 后 push 到数组）。
        /// </summary>
        /// <param name="projectile">已发射的活动投射物（非 null，<see cref="ProjectileBase.IsActive"/> 为 true）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="projectile"/> 为 null。</exception>
        /// <exception cref="ArgumentException">投射物已登记。</exception>
        /// <remarks>
        /// <para><b>稳定有序：</b>投射物追加到 <see cref="_projectiles"/> 末尾，保证遍历顺序 = 创建顺序。
        /// 对应 JS <c>activeProjectiles.push(projectile)</c>。</para>
        ///
        /// <para><b>新箭下一子步才移动：</b>本方法只登记到集合，不触发推进。推进只由
        /// <see cref="Update"/> 在 Projectile 阶段执行。若本方法在 AttackRelease 阶段被调用
        /// （弓兵创建投射物），该投射物在当前子步的 Projectile 阶段已过去，故下一子步才首次移动。</para>
        ///
        /// <para><b>调用时机：</b>由 BowSoldier（task 6.2）经 ProjectileFactory.Acquire 创建
        /// 并 Fire 后调用。ProjectileAttackEffect（task 5.8）作为 IAttackEffect 桥接时，
        /// 其 Launch 方法也会调用本方法登记投射物。</para>
        /// </remarks>
        internal void Add(ProjectileBase projectile)
        {
            if (projectile == null)
            {
                throw new ArgumentNullException(nameof(projectile));
            }

            if (_projectiles.Contains(projectile))
            {
                throw new ArgumentException("投射物已登记到 ProjectileManager，不可重复登记", nameof(projectile));
            }

            _projectiles.Add(projectile);
        }

        // ====================================================================
        // Update —— 唯一推进入口，由 BattleSimulation 在 Projectile 阶段回调
        // ====================================================================

        /// <summary>
        /// 推进一帧：快照遍历活动投射物，调用 <see cref="ProjectileBase.Advance"/>，
        /// 触发命中策略，完成/取消的投射物入移除队列。
        /// </summary>
        /// <param name="frameNowMs">当前外部帧时间戳（毫秒）。同帧所有子步观察同一值。
        /// 用于新箭下一子步才移动守卫（<see cref="ProjectileBase.Advance"/> 内部检查创建帧）。</param>
        /// <param name="stepMs">子步时长（毫秒），驱动弹道移动与命中延迟累计。</param>
        /// <remarks>
        /// <para><b>阶段（决策 0.3）：</b>由 <see cref="BattleSimulation"/> 在
        /// <see cref="BattleUpdatePhase.Projectile"/> 阶段回调，每子步一次。</para>
        ///
        /// <para><b>唯一推进（task 5.8 核心约束 / spec "Update phases are explicit and single-owned"）：</b>
        /// 每个投射物每子步只调用一次 <see cref="ProjectileBase.Advance"/>。本方法是投射物
        /// 推进的唯一入口——<see cref="ProjectileAttackEffect"/> 的 Update 为空操作，
        /// 不重复推进。</para>
        ///
        /// <para><b>新箭下一子步才移动（spec "Projectile is launched after projectile phase"）：</b>
        /// 快照遍历只包含调用时已在 <see cref="_projectiles"/> 中的投射物。创建于本子步后续阶段
        /// （AttackRelease/UnitAttack）的新箭不在快照中，故不被推进。防御性双保险：
        /// <see cref="ProjectileBase.Advance"/> 首次调用时若 frameNowMs 等于创建帧则跳过推进
        /// （task 5.7 已实现 <see cref="ProjectileBase.MarkCreationFrame"/> 守卫）。</para>
        ///
        /// <para><b>快照遍历（对应 JS 逆序遍历 + splice 的等价语义）：</b>
        /// 遍历前复制 <see cref="_projectiles"/> 到 <see cref="_updateBuffer"/>，避免遍历中
        /// 修改集合导致迭代异常。移除请求入 <see cref="_removeQueue"/>，遍历结束后由
        /// <see cref="ProcessRemoveQueue"/> 统一处理。</para>
        ///
        /// <para><b>命中触发（对应 JS ProjectileManager.js:85-106）：</b>
        /// 推进后检查命中策略触发条件（shouldRemove / hitEnabled + triggerMode）。
        /// delayMs > 0 时推进延迟倒计时；到期后执行命中。removeAfterHit=true 时请求移除。
        /// 命中在发生点同步生效（spec "伤害在发生点同步生效"）。</para>
        ///
        /// <para><b>移除判定（对应 JS ProjectileManager.js:117-125）：</b>
        /// shouldRemove（出界 / requestedRemoval / removeAfterHit）的投射物入移除队列。
        /// removeDelayMs > 0 且非 immediate 时推进移除延迟倒计时；到期后入移除队列。</para>
        ///
        /// <para><b>冻结中止（决策 0.4 / spec "Freeze occurs inside a manager update"）：</b>
        /// 若 <see cref="IsFrozen"/> 为 true，直接返回不推进。遍历中若检测到冻结，停止剩余迭代。
        /// 不在伤害调用栈内重入销毁集合——移除请求入 <see cref="_removeQueue"/>，遍历结束后
        /// 由 <see cref="ProcessRemoveQueue"/> 统一处理。</para>
        /// </remarks>
        internal void Update(long frameNowMs, long stepMs)
        {
            if (IsFrozen || IsCleared)
            {
                return;
            }

            UpdateCount++;

            // 快照遍历：复制 _projectiles 到 _updateBuffer，避免遍历中修改集合。
            _updateBuffer.Clear();
            foreach (ProjectileBase projectile in _projectiles)
            {
                _updateBuffer.Add(projectile);
            }

            int count = _updateBuffer.Count;
            for (int i = 0; i < count; i++)
            {
                // 冻结中止：遍历中若冻结则停止剩余迭代（决策 0.4）。
                if (IsFrozen)
                {
                    break;
                }

                ProjectileBase projectile = _updateBuffer[i];

                // 投射物可能在遍历中被移除（如前一个投射物命中导致 TryFreeze），
                // 但移除在遍历后处理；此处投射物仍在 _projectiles 中。

                // JS 原版守卫：if (!projectile.attacker) continue;
                // C# 移植：attackerId 为 -1 的哨兵投射物不推进（对应无攻击者箭矢）。
                // 本期 SimpleDynamicArrow 总有攻击者，此守卫为防御性。
                if (projectile.AttackerId < 0)
                {
                    continue;
                }

                // 唯一推进点：每子步每投射物只调用一次 Advance
                // （spec "Update phases are explicit and single-owned"、task 5.8 核心约束）。
                // Advance 内部：
                //   1. 新箭守卫：首次调用若 frameNowMs == creationFrameMs 则跳过（task 5.7）。
                //   2. 委托 OnUpdate → movement.Update(stepMs, speedScale) 推进贝塞尔位移。
                // 方法内可能触发 RequestRemove（到达目标）或命中结算（同步副作用）。
                if (projectile.IsActive)
                {
                    projectile.Advance(frameNowMs, stepMs);
                }

                // 触发命中策略（对应 JS ProjectileManager.js:85-106）。
                // 命中策略的触发条件、延迟倒计时与命中执行在此处处理。
                // 命中在发生点同步生效，可能调用 TryFreeze；IsFrozen 由 BattleSimulation
                // 在检查点统一置位，当前同步提交正常返回后检查点负责中止剩余迭代。
                bool shouldRemove = projectile.IsRemovalRequested;
                if (projectile is SimpleDynamicArrow arrow)
                {
                    HitEnemyStrategy strategy = arrow.HitStrategy;
                    if (strategy != null && !strategy.IsCompleted)
                    {
                        if (strategy.ShouldTrigger(shouldRemove, projectile.HitEnabled))
                        {
                            if (strategy.DelayMs > 0)
                            {
                                if (strategy.TickDelay(stepMs))
                                {
                                    bool removeAfterHit = strategy.Apply(projectile);
                                    if (removeAfterHit)
                                    {
                                        shouldRemove = true;
                                    }
                                }
                            }
                            else
                            {
                                bool removeAfterHit = strategy.Apply(projectile);
                                if (removeAfterHit)
                                {
                                    shouldRemove = true;
                                }
                            }
                        }
                    }
                }

                // 移除判定（对应 JS ProjectileManager.js:117-125）。
                // shouldRemove（requestedRemoval / removeAfterHit / 出界）的投射物入移除队列。
                // removeDelayMs > 0 且非 immediate 时推进移除延迟倒计时；到期后入移除队列。
                if (shouldRemove)
                {
                    if (projectile.RemoveDelayMs == 0 || projectile.IsImmediateRemoval)
                    {
                        EnqueueRemove(projectile, projectile.IsImmediateRemoval ? "immediate" : "completed");
                    }
                    else if (projectile.TickRemoveDelay(stepMs))
                    {
                        EnqueueRemove(projectile, "delayed");
                    }
                }
            }

            // 遍历结束后处理移除队列。
            ProcessRemoveQueue();
        }

        // ====================================================================
        // 取消 —— 单个投射物
        // ====================================================================

        /// <summary>
        /// 请求移除单个投射物（对应 JS <c>remove(projectile)</c>）。
        /// </summary>
        /// <param name="projectile">要移除的投射物。</param>
        /// <param name="reason">移除原因（如 "removed"），供诊断。</param>
        /// <returns>true=投射物存在并已入移除队列；false=投射物不在活动集合中。</returns>
        /// <remarks>
        /// <para><b>不重入销毁集合：</b>只入 <see cref="_removeQueue"/>，由
        /// <see cref="ProcessRemoveQueue"/> 统一处理。若在 <see cref="Update"/> 遍历中调用本方法，
        /// 移除在遍历结束后执行。</para>
        /// <para><b>不造成伤害：</b>移除时经 <see cref="ProjectileFactory.Release"/> 回收，
        /// 不调 <see cref="ProjectileBase.Advance"/> 或任何伤害提交。</para>
        /// </remarks>
        internal bool Remove(ProjectileBase projectile, string reason = "removed")
        {
            if (projectile == null || !_projectiles.Contains(projectile))
            {
                return false;
            }

            return EnqueueRemove(projectile, reason);
        }

        // ====================================================================
        // 移除队列处理
        // ====================================================================

        /// <summary>
        /// 将投射物入移除队列（若未已入队）。
        /// </summary>
        /// <param name="projectile">要移除的投射物。</param>
        /// <param name="reason">移除原因。</param>
        /// <returns>true=本次入队成功；false=投射物不在活动集合或已入队。</returns>
        private bool EnqueueRemove(ProjectileBase projectile, string reason)
        {
            // 投射物必须仍在活动集合中。
            if (!_projectiles.Contains(projectile))
            {
                return false;
            }

            // 去重：避免同投射物多次入队。
            if (_removeQueue.Contains(projectile))
            {
                return false;
            }

            _removeQueue.Add(projectile);
            _removeReasons[projectile] = reason;
            return true;
        }

        /// <summary>
        /// 处理移除队列：统一回收所有待移除投射物。
        /// </summary>
        /// <remarks>
        /// <para>由 <see cref="Update"/> 在遍历结束后调用。也可由外部在安全点显式调用
        /// （如 Settling 清理前的同步点）。处理后清空队列。幂等。</para>
        /// <para><b>不重入销毁集合：</b>本方法在 Update 遍历结束后执行，不在伤害调用栈内。</para>
        /// <para><b>池回收对称（task 5.7 + task 4.1）：</b>经
        /// <see cref="ProjectileFactory.Release"/> 归还池，保证 Acquire/Release 对称。</para>
        /// </remarks>
        internal void ProcessRemoveQueue()
        {
            if (_removeQueue.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _removeQueue.Count; i++)
            {
                ProjectileBase projectile = _removeQueue[i];
                string reason = _removeReasons.TryGetValue(projectile, out string r) ? r : "removed";
                ReleaseProjectile(projectile, reason);
            }

            _removeQueue.Clear();
            _removeReasons.Clear();
        }

        /// <summary>
        /// 释放单个投射物：从集合移除，经工厂回收（对应 JS <c>_removeAt</c>）。
        /// </summary>
        /// <param name="projectile">要释放的投射物。</param>
        /// <param name="reason">移除原因。</param>
        private void ReleaseProjectile(ProjectileBase projectile, string reason)
        {
            // 从活动集合移除（对应 JS activeProjectiles.splice）。
            _projectiles.Remove(projectile);

            // 经工厂回收（对应 JS projectileFactory.recover(projectile)）。
            // ProjectileFactory.Release 先调 Recover（投射物自身状态回收），
            // 再委托 BattleObjectPool.Release（池入池 + ResetState）。
            // 防御性捕获：回收失败不中断批量移除。
            try
            {
                if (projectile is SimpleDynamicArrow arrow)
                {
                    _factory.Release(arrow);
                }
                else
                {
                    // 本期只有 SimpleDynamicArrow，出现新类型时扩展。
                    Log.Warning($"{LogTag} 未知投射物类型，无法回收 type={projectile.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 回收投射物异常 reason={reason}: {ex}");
            }
        }

        // ====================================================================
        // Settling 静默清理 —— 取消全部空中弹道，不造成伤害
        // --------------------------------------------------------------------
        // 对应 JS gameOver()：逆序遍历 _removeAt 再清空数组。但 C# 语义更强：
        // Settling 静默清理 MUST 不造成伤害（spec "Settling has no gameplay damage authority"）。
        // 回收只经 ProjectileFactory.Release，不调 Advance/Hit，满足此约束。
        // ====================================================================

        /// <summary>
        /// Settling 静默清理：取消并回收全部空中弹道，不造成伤害（对应 JS <c>gameOver()</c>）。
        /// </summary>
        /// <remarks>
        /// <para><b>Settling 静默清理（spec "Settling has no gameplay damage authority" /
        /// "Runtime quiescence and cleanup have one ordered owner"）：</b>
        /// 由 <c>BattleRuntime.EnterSettling</c> 在"清理 ProjectileManager"步骤调用
        /// （在"清理 AttackEffectManager"之后、"清理 EnemyManager"之前）。
        /// 取消并回收全部残余弹道，且不造成伤害（只经 <see cref="ProjectileFactory.Release"/> 回收，
        /// 不调 <see cref="ProjectileBase.Advance"/> 或任何伤害提交）。</para>
        ///
        /// <para><b>幂等：</b>重复调用为空操作。由 <c>BattleRuntime.EnterSettling</c> 调用，
        /// 也可由测试重置调用。</para>
        ///
        /// <para><b>不重入销毁集合：</b>快照遍历，逐个 ReleaseProjectile。ReleaseProjectile 从
        /// <see cref="_projectiles"/> 移除并经工厂回收。遍历使用快照缓冲避免迭代异常。</para>
        ///
        /// <para><b>顺序（对应 JS <c>for (index = length - 1; index >= 0; index--) _removeAt</c>）：</b>
        /// 按创建顺序逐个回收，顺序确定。</para>
        /// </remarks>
        internal void Clear()
        {
            if (IsCleared)
            {
                return;
            }

            // 快照遍历，避免遍历中 ReleaseProjectile 修改 _projectiles 导致迭代异常。
            _updateBuffer.Clear();
            foreach (ProjectileBase projectile in _projectiles)
            {
                _updateBuffer.Add(projectile);
            }

            for (int i = 0; i < _updateBuffer.Count; i++)
            {
                ProjectileBase projectile = _updateBuffer[i];
                ReleaseProjectile(projectile, "game-over");
            }

            _projectiles.Clear();
            _removeQueue.Clear();
            _removeReasons.Clear();
            _updateBuffer.Clear();
            IsCleared = true;
            // 不在此设置 IsFrozen：IsFrozen 语义是"冻结以进入 Settling"，
            // IsCleared 语义是"已清空"。Update 同时检查两者，清理后不推进。
        }

        /// <summary>
        /// 战斗结束清理：等价于 <see cref="Clear"/>（对应 JS <c>gameOver</c> 语义）。
        /// </summary>
        /// <remarks>
        /// <para>JS <c>gameOver()</c> 先逆序遍历 <c>_removeAt</c> 再 <c>activeProjectiles = []</c>。
        /// C# <see cref="Clear"/> 已完成等价语义（快照遍历 ReleaseProjectile + 清空集合）。
        /// 本方法为语义别名，保持与 EnemyManager.GameOver/Clear 的 API 对称，
        /// 供 BattleRuntime.EnterSettling 按场景语义调用。</para>
        /// <para>幂等。</para>
        /// </remarks>
        internal void GameOver()
        {
            Clear();
        }

        // ====================================================================
        // 诊断查询
        // ====================================================================

        /// <summary>
        /// 获取活动投射物列表的只读快照（按创建顺序，诊断用）。
        /// </summary>
        internal IReadOnlyList<ProjectileBase> GetProjectilesSnapshot()
        {
            return _projectiles.ToArray();
        }

        /// <summary>
        /// 投射物是否在活动集合中登记（诊断用）。
        /// </summary>
        internal bool Contains(ProjectileBase projectile)
        {
            return projectile != null && _projectiles.Contains(projectile);
        }
    }
}
