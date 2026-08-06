using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.3：AttackEffectManager —— 唯一推进、取消、稳定移除队列、池回收与 Settling 静默清理
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Combat/AttackEffectManager.cs）：
    //   统一推进、取消和回收活动攻击效果；唯一 effect 更新拥有者。
    //
    // 来源证据（还原工程 AttackEffectManager.js:1-81）：
    //   还原工程 AttackEffectManager 是一个由 GameLoop 驱动的纯逻辑管理器，核心数据：
    //     - effects: Set<effect>                  // 活动效果集合（JS Set 保留插入顺序）
    //     - records: Map<effect, { poolClass }>   // 效果→池类型记录，用于回收
    //     - objectPool: ObjectPool|null           // 池引用（可选）
    //     - updateCount: number                   // 更新次数（诊断）
    //   核心方法：
    //     - create(ClassType, create): 经 objectPool.takeByClass 获取/新建效果
    //     - add(effect, { poolClass }): 登记 effects + records
    //     - update(deltaMs): 快照遍历，跳过 inactive，调 effect.update，inactive 则 _release
    //     - remove(effect, reason): _release
    //     - cancelOwner(owner, reason): 遍历匹配 owner 调 remove
    //     - gameOver(): 遍历 remove 再 clear（战斗结束清理）
    //     - _release(effect, reason): 清 records/effects，调 effect.cleanup，objectPool.recoverByClass
    //
    // 决策依据：
    //   - design.md 第 1 节目录表："统一推进、取消和回收活动攻击效果；唯一 effect 更新拥有者。"
    //   - design.md 第 2 节更新顺序：BattleManager 内部 4.3 AttackEffectManager.update
    //     在 _updateUnitAttacks 之后执行；C# 由 BattleSimulation 在
    //     BattleUpdatePhase.AttackEffect 阶段回调，每子步一次（决策 0.3）。
    //   - task 5.3 约束：唯一推进（禁止 TEngine Timer/Laya Timer 双轨，详见 task 5.5）、
    //     取消、稳定移除队列、池回收、Settling 静默清理。
    //   - task 4.6 约束（稳定有序集合）：禁止依赖 Dictionary/HashSet 的未定义遍历顺序。
    //     本类型采用 List<IAttackEffect> 作为有序活动集合（对应 JS Set 的插入顺序遍历），
    //     移除请求入 List 队列，遍历结束后统一处理（与 EnemyManager 模式一致）。
    //   - design.md 决策 0.4 / spec "Battle result is frozen once"：首次 TryFreeze 成功后
    //     只完成当前同步提交并中止剩余 phase/子步。本类型在 Update 遍历中若检测到冻结标志，
    //     停止剩余迭代；不在伤害调用栈内重入销毁集合。
    //   - spec battle-runtime-lifecycle "Settling has no gameplay damage authority" /
    //     "Runtime quiescence and cleanup have one ordered owner"：Settling 静默清理顺序中
    //     "清理 AttackEffectManager" 是步骤 3，MUST 取消并回收全部残余效果且不造成伤害。
    //   - spec battle-simulation "Effect manager participates in a substep"：攻击效果每子步
    //     只累计一次 stepMs，不因嵌套 Manager 调用而双倍推进。
    //   - spec battle-simulation "Melee effect is created by attack scheduling"：单位攻击调度
    //     在当前子步创建的近战效果，在当前子步的攻击效果阶段立即累计一次本子步 stepMs。
    //   - task 5.5（后续）：KnifeAttackTimeline 合入 KnifeAttackEffect，禁止 TEngine Timer/
    //     Laya Timer 与 Manager 双轨推进。本类型作为唯一推进拥有者，effect.Update 由本类型
    //     唯一调用；具体效果（task 5.4）以逻辑时间实现时序，不自行注册 Timer。
    //
    // 并行任务契约推断（task 5.4 同批次并行）：
    //   本类型引用的具体效果类型（MeleeAttackEffect / KnifeAttackEffect / PikeAttackEffect /
    //   CavalrySweepEffect / ProjectileAttackEffect）由 task 5.4 并行创建，当前尚未存在。
    //   为使本类型可独立编译，定义最小契约接口 IAttackEffect（见下方），task 5.4 的具体类型
    //   将实现该接口。接口成员依据还原工程 MeleeAttackEffect.js / KnifeAttackEffect.js 等
    //   推断：
    //     - Active: bool        ← effect.active（是否活动）
    //     - Owner: object       ← effect.owner（所有者，用于 cancelOwner 匹配）
    //     - Update(deltaMs)     ← effect.update(deltaMs)（推进一帧）
    //     - Cancel(reason)      ← effect.cleanup(reason)（取消并清理，不造成伤害）
    //   另外实现 IPoolableBattleObject.ResetState 以支持 BattleObjectPool 池化（task 4.1 契约）。
    //
    // 池回收设计（task 5.3 + task 4.1）：
    //   还原工程用 objectPool.recoverByClass(effect) 按类回收。C# BattleObjectPool<T> 是
    //   泛型强类型池，每个具体类型一个池实例。由于本类型在同一集合中管理多种效果类型，
    //   无法在 Manager 内用单一泛型池调用回收。采用"每效果回收委托"方案：
    //     - Add(effect, releaseToPool) 时记录回收委托（对应 JS records.set(effect, { poolClass })）。
    //     - releaseToPool 由调用方（AttackScheduler/Unit 等，知道具体类型 T 并持有
    //       BattleObjectPool<T>）提供，签名 Action<IAttackEffect>，内部执行 pool.Release((T)effect)。
    //     - 无池（releaseToPool=null）时不回收，只 Cancel（对应 JS 无 objectPool 时的行为）。
    //   这保持 Manager 对具体类型的解耦，同时满足 task 4.1 Acquire/Release 对称契约。
    //
    // 不变量：
    //   1. 唯一推进：effect.Update 只由本类型.Update 在 AttackEffect 阶段调用，每子步每效果一次。
    //   2. 稳定有序：活动集合基于 List（插入顺序），不依赖 Dictionary/HashSet 遍历顺序。
    //   3. 移除队列：同子步内多个完成/取消请求不重入修改集合，先入队再统一处理。
    //   4. 冻结中止：IsFrozen 后 Update 直接返回；遍历中检测冻结则停止剩余迭代。
    //   5. Settling 静默清理：Clear 取消全部活动效果且不造成伤害（只调 Cancel，不调 Update/Hit）。
    //   6. 池回收对称：有 releaseToPool 的效果在移除时经委托归还池；无委托的效果只 Cancel。
    //   7. 幂等清理：Clear/GameOver 可重复调用，后续调用为空操作。
    // ============================================================================

    /// <summary>
    /// 攻击效果最小契约：AttackEffectManager 依赖的效果属性与行为（task 5.3 为 task 5.4 定义）。
    /// </summary>
    /// <remarks>
    /// <para><b>契约来源（还原工程 MeleeAttackEffect.js / KnifeAttackEffect.js /
    /// PikeAttackEffect.js / CavalrySweepEffect.js / ProjectileAttackEffect.js 推断）：</b>
    /// task 5.4 并行创建的具体效果类型将实现本接口。接口成员对应 JS 效果的字段：</para>
    /// <list type="bullet">
    /// <item><see cref="Active"/> ← <c>this.active</c>（是否活动；false 表示已完成或已取消）</item>
    /// <item><see cref="Owner"/> ← <c>this.owner</c>（所有者引用，用于 <c>cancelOwner</c> 匹配）</item>
    /// <item><see cref="Update"/> ← <c>effect.update(deltaMs)</c>（推进一帧，可能触发命中结算）</item>
    /// <item><see cref="Cancel"/> ← <c>effect.cleanup(reason)</c>（取消并清理，不造成伤害）</item>
    /// </list>
    ///
    /// <para><b>与 <see cref="IPoolableBattleObject"/> 的关系：</b>
    /// task 5.4 的具体效果类型同时实现 <see cref="IPoolableBattleObject"/>（支持
    /// <see cref="BattleObjectPool{T}"/> 池化）。本接口继承 <see cref="IPoolableBattleObject"/>
    /// 以表达"攻击效果既是池化对象又是管理器托管对象"的双重契约，避免 task 5.4 需要实现两个
    /// 不相关接口。<see cref="IPoolableBattleObject.ResetState"/> 在池 Release 时由池调用，
    /// 与 <see cref="Cancel"/>（管理器移除时调用）分工：ResetState 清空全部可变状态供复用，
    /// Cancel 停止效果活动状态并释放外部引用（owner/enemyManager/timeline 等）。</para>
    ///
    /// <para><b>Owner 匹配语义（对应 JS <c>cancelOwner</c>）：</b>
    /// JS <c>cancelOwner(owner)</c> 匹配 <c>effect.owner === owner || effect.owner?.id === owner?.id</c>。
    /// C# 移植以 <see cref="Owner"/> 引用比较为主；若 Owner 为带 Id 的实体，由调用方确保
    /// 同一实体传入同一引用。本接口不强制 Owner 类型，保持最小化。</para>
    ///
    /// <para><b>本接口为 internal：</b>只供 GameBattle 内部 AttackEffectManager 与具体效果
    /// （task 5.4）使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal interface IAttackEffect : IPoolableBattleObject
    {
        /// <summary>
        /// 是否活动（对应 <c>this.active</c>）。
        /// <para>false 表示效果已完成（duration 到期/命中已结算）或已被取消。
        /// <see cref="AttackEffectManager.Update"/> 遍历时对 Active=false 的效果执行移除回收。</para>
        /// </summary>
        bool Active { get; }

        /// <summary>
        /// 所有者引用（对应 <c>this.owner</c>），用于 <see cref="AttackEffectManager.CancelOwner"/> 匹配。
        /// <para>通常为发起攻击的单位。取消所有者时，Manager 遍历活动效果并移除 Owner 匹配的效果。</para>
        /// </summary>
        object Owner { get; }

        /// <summary>
        /// 推进一帧（对应 <c>effect.update(deltaMs)</c>）。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒），驱动效果累计与命中时机判断。</param>
        /// <remarks>
        /// <para><b>唯一推进点（task 5.3 / 5.5）：</b>本方法只由 <see cref="AttackEffectManager.Update"/>
        /// 在 <see cref="BattleUpdatePhase.AttackEffect"/> 阶段调用，每子步每效果一次。
        /// 具体效果（task 5.4）以逻辑时间实现时序，不自行注册 TEngine Timer / Laya Timer。</para>
        /// <para>方法内可能触发命中结算（如 MeleeAttackEffect 的 hit()），作为同步副作用立即生效，
        /// 不推迟到帧末（spec "Update phases are explicit and single-owned"）。</para>
        /// </remarks>
        void Update(long deltaMs);

        /// <summary>
        /// 取消并清理效果（对应 <c>effect.cleanup(reason)</c>）。
        /// </summary>
        /// <param name="reason">取消原因（如 "effect-inactive"、"effect-complete"、"owner-removed"、
        /// "game-over"），供诊断与日志使用。</param>
        /// <remarks>
        /// <para><b>不造成伤害：</b>Cancel 只停止效果活动状态、释放外部引用（owner/enemyManager/
        /// timeline/hitSet 等），不调用任何伤害提交方法。这保证 Settling 静默清理
        /// （<see cref="AttackEffectManager.Clear"/>）不违反 "Settling has no gameplay damage authority"。</para>
        /// <para><b>幂等：</b>重复调用安全。已 Cancel 的效果再次 Cancel 为空操作。</para>
        /// <para><b>与 <see cref="IPoolableBattleObject.ResetState"/> 的区别：</b>
        /// Cancel 停止活动并释放外部引用；ResetState 清空全部可变状态使对象等价于新构造（供池复用）。
        /// 池 Release 时先调 ResetState；管理器移除时先调 Cancel。</para>
        /// </remarks>
        void Cancel(string reason);
    }

    /// <summary>
    /// 统一推进、取消和回收活动攻击效果的内部管理器；唯一 effect 更新拥有者。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>统一推进、取消和回收活动攻击效果；唯一 effect
    /// 更新拥有者。替代还原工程 <c>AttackEffectManager.js</c>（<c>AttackEffectManager.js:4-79</c>）。</para>
    ///
    /// <para><b>唯一推进（task 5.3 / 5.5 / spec "Effect manager participates in a substep"）：</b>
    /// <see cref="Update"/> 是活动攻击效果的唯一推进入口，由 <see cref="BattleSimulation"/> 在
    /// <see cref="BattleUpdatePhase.AttackEffect"/> 阶段回调，每子步一次。每个效果每子步只累计
    /// 一次 <c>stepMs</c>，不因嵌套 Manager 调用而双倍推进。具体效果（task 5.4）不自行注册
    /// TEngine Timer / Laya Timer，避免双轨推进（task 5.5）。</para>
    ///
    /// <para><b>稳定有序集合（task 4.6 约束）：</b>
    /// 活动效果集合使用 <see cref="List{T}"/>（插入顺序），不依赖 Dictionary/HashSet 的未定义
    /// 遍历顺序。遍历前复制快照到 <c>_updateBuffer</c>，避免遍历中修改集合导致迭代异常
    /// （对应 JS <c>for (const effect of [...this.effects])</c>）。移除请求入
    /// <c>_removeQueue</c>，遍历结束后由 <see cref="ProcessRemoveQueue"/> 统一处理。</para>
    ///
    /// <para><b>移除队列（与 EnemyManager 模式一致）：</b>
    /// 同子步内多个完成/取消请求不重入修改集合，先入 <c>_removeQueue</c> 再统一处理。
    /// 对应决策 0.4 "TryFreeze 不在嵌套伤害调用栈内重入销毁集合"。</para>
    ///
    /// <para><b>池回收（task 5.3 + task 4.1）：</b>
    /// 有 <c>releaseToPool</c> 委托的效果在移除时经委托归还到对应 <see cref="BattleObjectPool{T}"/>
    /// （委托由 Add 调用方提供，内部执行 <c>pool.Release((T)effect)</c>）。无委托的效果只
    /// <see cref="IAttackEffect.Cancel"/>，不回收。对应 JS <c>objectPool.recoverByClass</c>。</para>
    ///
    /// <para><b>冻结中止（决策 0.4 / spec "Battle result is frozen once"）：</b>
    /// 若 <see cref="IsFrozen"/> 为 true，<see cref="Update"/> 直接返回，不推进剩余效果。
    /// 遍历中若检测到冻结，停止剩余迭代。冻结标志由外部（BattleRuntime.EnterSettling）设置。</para>
    ///
    /// <para><b>Settling 静默清理（spec "Settling has no gameplay damage authority" /
    /// "Runtime quiescence and cleanup have one ordered owner"）：</b>
    /// <see cref="Clear"/> 取消全部活动效果并回收，不造成伤害（只调 <see cref="IAttackEffect.Cancel"/>，
    /// 不调 <see cref="IAttackEffect.Update"/> 或任何伤害提交）。由 BattleRuntime.EnterSettling
    /// 在"清理 AttackEffectManager"步骤调用。幂等。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 AttackEffectManager。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleRuntime 在阶段回调中调用，
    /// 不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class AttackEffectManager
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选攻击效果管理器相关条目。
        /// </summary>
        private const string LogTag = "[AttackEffectManager]";

        // ====================================================================
        // 活动效果集合：有序 List（插入顺序），保证遍历顺序确定
        // ====================================================================

        /// <summary>
        /// 活动效果有序集合（对应 JS <c>effects: Set</c>）。
        /// <para><b>稳定有序（task 4.6 约束）：</b>使用 <see cref="List{T}"/> 而非
        /// <see cref="HashSet{T}"/>，保证遍历顺序 = 登记顺序（对应 JS Set 的插入顺序遍历）。
        /// 不依赖 Dictionary/HashSet 的未定义遍历顺序决定效果推进或移除。</para>
        /// <para>移除时从列表删除（O(n)，单局活动效果数量可控，可接受）。</para>
        /// </summary>
        private readonly List<IAttackEffect> _effects = new List<IAttackEffect>();

        // ====================================================================
        // 效果→回收委托映射（对应 JS records: Map<effect, { poolClass }>）
        // ====================================================================

        /// <summary>
        /// 效果→回收委托映射（对应 JS <c>records: Map&lt;effect, { poolClass }&gt;</c>）。
        /// <para>有委托的效果在移除时归还到对应 <see cref="BattleObjectPool{T}"/>；
        /// 无委托的效果只 Cancel 不回收。委托由 Add 调用方（知道具体类型 T 并持有池）提供，
        /// 内部执行 <c>pool.Release((T)effect)</c>。</para>
        /// <para>使用 <see cref="Dictionary{TKey, TValue}"/> 仅用于 O(1) 按引用查找回收委托，
        /// 查找结果与遍历顺序无关，不用于决定效果推进或移除顺序。</para>
        /// </summary>
        private readonly Dictionary<IAttackEffect, Action<IAttackEffect>> _releaseDelegates =
            new Dictionary<IAttackEffect, Action<IAttackEffect>>();

        // ====================================================================
        // 更新快照缓冲（对应 JS [...this.effects] 快照遍历）
        // ====================================================================

        /// <summary>
        /// 更新快照缓冲：遍历前复制活动效果引用，避免遍历中修改集合导致迭代异常。
        /// <para>对应 JS <c>for (const effect of [...this.effects])</c> 的数组展开快照。</para>
        /// </summary>
        private readonly List<IAttackEffect> _updateBuffer = new List<IAttackEffect>();

        // ====================================================================
        // 移除队列：同子步完成/取消请求先入队，遍历结束后统一处理
        // ====================================================================

        /// <summary>
        /// 延迟移除队列：完成/取消的效果入队，遍历结束后由 <see cref="ProcessRemoveQueue"/> 统一处理。
        /// <para>对应决策 0.4 "TryFreeze 不在嵌套伤害调用栈内重入销毁集合"——
        /// 同子步内多个完成请求不重入修改集合，先入队再统一处理。</para>
        /// <para>使用 <see cref="List{T}"/> 而非 <see cref="Queue{T}"/> 以便诊断时观察。</para>
        /// </summary>
        private readonly List<IAttackEffect> _removeQueue = new List<IAttackEffect>();

        // ====================================================================
        // 冻结标志（决策 0.4 / Settling）
        // ====================================================================

        /// <summary>
        /// 是否已冻结。冻结后 <see cref="Update"/> 直接返回，不再推进剩余效果。
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
        /// 当前活动效果数量（对应 JS <c>activeCount = effects.size</c>）。
        /// </summary>
        internal int ActiveCount => _effects.Count;

        /// <summary>
        /// 累计 Update 调用次数（对应 JS <c>updateCount</c>），诊断用。
        /// </summary>
        internal int UpdateCount { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造攻击效果管理器。
        /// </summary>
        /// <remarks>
        /// <para>与 JS 原版一致，本类型无构造参数（JS 的 objectPool 在 C# 改为按效果回收委托，
        /// 由 Add 调用方提供）。由 <c>BattleRuntimeFactory</c> 在每次 Create 时构造新实例，
        /// 保证每局独立。</para>
        /// <para><b>并行任务契约推断：</b>task 5.4 的具体效果类型由 AttackScheduler（task 5.2）/
        /// 单位（task 6.2）经 Add 登记到本管理器，回收委托由调用方从 <see cref="BattlePoolScope"/>
        /// 获取对应 <see cref="BattleObjectPool{T}"/> 后包装提供。</para>
        /// </remarks>
        internal AttackEffectManager()
        {
            IsFrozen = false;
            IsCleared = false;
        }

        // ====================================================================
        // 登记 —— 由 AttackScheduler/单位在创建效果后调用
        // --------------------------------------------------------------------
        // 对应 JS add(effect, { poolClass })。C# 改为接收回收委托。
        // ====================================================================

        /// <summary>
        /// 登记一个活动攻击效果到管理器（对应 JS <c>add(effect, { poolClass })</c>）。
        /// </summary>
        /// <param name="effect">已启动的活动效果（非 null，<see cref="IAttackEffect.Active"/> 为 true）。</param>
        /// <param name="releaseToPool">
        /// 回收委托（可选）。效果移除时调用，内部执行 <c>pool.Release((T)effect)</c> 归还到对应
        /// <see cref="BattleObjectPool{T}"/>。为 null 时效果移除只 Cancel 不回收
        /// （对应 JS 无 objectPool 或无 poolClass 时的行为）。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> 为 null。</exception>
        /// <exception cref="ArgumentException">效果已登记。</exception>
        /// <remarks>
        /// <para><b>稳定有序：</b>效果追加到 <see cref="_effects"/> 末尾，保证遍历顺序 = 登记顺序。
        /// 对应 JS <c>effects.add(effect)</c>（Set 保留插入顺序）。</para>
        ///
        /// <para><b>回收委托（task 4.1 对称契约）：</b>
        /// 调用方（AttackScheduler/单位，知道具体类型 T 并持有 <see cref="BattleObjectPool{T}"/>）
        /// 提供 <paramref name="releaseToPool"/>，内部执行 <c>pool.Release((T)effect)</c>。
        /// 这保持 Manager 对具体类型的解耦，同时满足 Acquire/Release 对称。对应 JS
        /// <c>records.set(effect, { poolClass })</c>。</para>
        ///
        /// <para><b>新效果立即累计（spec "Melee effect is created by attack scheduling"）：</b>
        /// 单位攻击调度在当前子步创建的效果经 Add 登记后，在当前子步的 AttackEffect 阶段
        /// 立即累计一次本子步 <c>stepMs</c>（因为 Update 快照遍历已包含本次 Add 的效果）。</para>
        /// </remarks>
        internal void Add(IAttackEffect effect, Action<IAttackEffect> releaseToPool = null)
        {
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }

            if (_releaseDelegates.ContainsKey(effect))
            {
                // 效果已登记（_releaseDelegates 与 _effects 同步维护）。
                throw new ArgumentException("效果已登记到 AttackEffectManager，不可重复登记", nameof(effect));
            }

            _effects.Add(effect);
            _releaseDelegates[effect] = releaseToPool;
        }

        // ====================================================================
        // Update —— 唯一推进入口，由 BattleSimulation 在 AttackEffect 阶段回调
        // ====================================================================

        /// <summary>
        /// 推进一帧：快照遍历活动效果，跳过 inactive，调 <see cref="IAttackEffect.Update"/>，
        /// inactive 则移除回收。
        /// </summary>
        /// <param name="stepMs">子步时长（毫秒），驱动效果累计与命中时机判断。</param>
        /// <remarks>
        /// <para><b>阶段（决策 0.3）：</b>由 <see cref="BattleSimulation"/> 在
        /// <see cref="BattleUpdatePhase.AttackEffect"/> 阶段回调，每子步一次。</para>
        ///
        /// <para><b>唯一推进（task 5.3 / 5.5 / spec "Effect manager participates in a substep"）：</b>
        /// 每个效果每子步只累计一次 <c>stepMs</c>。具体效果不自行注册 Timer，避免双轨推进。</para>
        ///
        /// <para><b>快照遍历（对应 JS <c>for (const effect of [...this.effects])</c>）：</b>
        /// 遍历前复制 <see cref="_effects"/> 到 <see cref="_updateBuffer"/>，避免遍历中修改集合
        /// 导致迭代异常。</para>
        ///
        /// <para><b>inactive 移除（对应 JS <c>if (!effect.active) _release</c>）：</b>
        /// 遍历前 inactive 的效果直接移除回收（reason="effect-inactive"）；
        /// Update 后变 inactive 的效果移除回收（reason="effect-complete"）。</para>
        ///
        /// <para><b>冻结中止（决策 0.4 / spec "Freeze occurs inside a manager update"）：</b>
        /// 若 <see cref="IsFrozen"/> 为 true，直接返回不推进。遍历中若检测到冻结，停止剩余迭代。
        /// 不在伤害调用栈内重入销毁集合——移除请求入 <see cref="_removeQueue"/>，遍历结束后
        /// 由 <see cref="ProcessRemoveQueue"/> 统一处理。</para>
        /// </remarks>
        internal void Update(long stepMs)
        {
            if (IsFrozen || IsCleared)
            {
                return;
            }

            UpdateCount++;

            // 快照遍历：复制 _effects 到 _updateBuffer，避免遍历中修改集合。
            _updateBuffer.Clear();
            foreach (IAttackEffect effect in _effects)
            {
                _updateBuffer.Add(effect);
            }

            int count = _updateBuffer.Count;
            for (int i = 0; i < count; i++)
            {
                // 冻结中止：遍历中若冻结则停止剩余迭代（决策 0.4）。
                if (IsFrozen)
                {
                    break;
                }

                IAttackEffect effect = _updateBuffer[i];

                // 效果可能在遍历中被移除（如前一个效果的命中导致本效果所有者死亡，
                // cancelOwner 已入队但 ProcessRemoveQueue 在遍历后执行；此处效果仍在 _effects）。
                // 若效果已不在 _effects，跳过（不应发生，因为移除在遍历后处理）。

                // 遍历前 inactive 的效果直接移除回收（对应 JS if (!effect.active) _release('effect-inactive')）。
                if (!effect.Active)
                {
                    EnqueueRemove(effect, "effect-inactive");
                    continue;
                }

                // 唯一推进点：每子步每效果只调用一次 Update（spec "Effect manager participates in a substep"）。
                // 方法内可能触发命中结算（同步副作用），可能调用 TryFreeze；IsFrozen 由
                // BattleSimulation 在检查点统一置位，当前同步提交正常返回后检查点负责中止剩余迭代。
                effect.Update(stepMs);

                // Update 后变 inactive 的效果移除回收（对应 JS if (!effect.active) _release('effect-complete')）。
                if (!effect.Active)
                {
                    EnqueueRemove(effect, "effect-complete");
                }
            }

            // 遍历结束后处理移除队列。
            ProcessRemoveQueue();
        }

        // ====================================================================
        // 取消 —— 单个效果 / 按所有者取消
        // ====================================================================

        /// <summary>
        /// 取消并移除单个效果（对应 JS <c>remove(effect, reason)</c>）。
        /// </summary>
        /// <param name="effect">要取消的效果。</param>
        /// <param name="reason">取消原因（如 "removed"），供诊断。</param>
        /// <returns>true=效果存在并已入移除队列；false=效果不在活动集合中。</returns>
        /// <remarks>
        /// <para><b>不重入销毁集合：</b>只入 <see cref="_removeQueue"/>，由
        /// <see cref="ProcessRemoveQueue"/> 统一处理。若在 <see cref="Update"/> 遍历中调用本方法，
        /// 移除在遍历结束后执行。</para>
        /// <para><b>不造成伤害：</b>移除时调 <see cref="IAttackEffect.Cancel"/>，不调
        /// <see cref="IAttackEffect.Update"/> 或任何伤害提交。</para>
        /// </remarks>
        internal bool Cancel(IAttackEffect effect, string reason = "removed")
        {
            if (effect == null || !_releaseDelegates.ContainsKey(effect))
            {
                return false;
            }

            EnqueueRemove(effect, reason);
            return true;
        }

        /// <summary>
        /// 取消指定所有者的全部活动效果（对应 JS <c>cancelOwner(owner, reason)</c>）。
        /// </summary>
        /// <param name="owner">所有者引用（通常为单位实体）。</param>
        /// <param name="reason">取消原因（默认 "owner-removed"）。</param>
        /// <returns>入移除队列的效果数量。</returns>
        /// <remarks>
        /// <para>对应 JS <c>cancelOwner</c>：遍历活动效果，匹配 <c>effect.Owner == owner</c> 的效果入移除队列。</para>
        /// <para><b>稳定有序：</b>遍历基于 <see cref="_effects"/>（登记顺序），入队顺序确定。</para>
        /// <para><b>不重入销毁集合：</b>只入 <see cref="_removeQueue"/>，由
        /// <see cref="ProcessRemoveQueue"/> 统一处理。</para>
        /// <para>典型场景：单位死亡/回收时取消其全部活动攻击效果。</para>
        /// </remarks>
        internal int CancelOwner(object owner, string reason = "owner-removed")
        {
            if (owner == null)
            {
                return 0;
            }

            int count = 0;
            // 遍历 _effects（登记顺序），匹配 Owner 的效果入移除队列。
            // 不在遍历中修改 _effects，移除由 ProcessRemoveQueue 统一处理。
            for (int i = 0; i < _effects.Count; i++)
            {
                IAttackEffect effect = _effects[i];
                if (ReferenceEquals(effect.Owner, owner))
                {
                    if (EnqueueRemove(effect, reason))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // ====================================================================
        // 移除队列处理
        // ====================================================================

        /// <summary>
        /// 将效果入移除队列（若未已入队）。
        /// </summary>
        /// <param name="effect">要移除的效果。</param>
        /// <param name="reason">取消原因。</param>
        /// <returns>true=本次入队成功；false=效果不在活动集合或已入队。</returns>
        private bool EnqueueRemove(IAttackEffect effect, string reason)
        {
            // 效果必须仍在活动集合中。
            if (!_releaseDelegates.ContainsKey(effect))
            {
                return false;
            }

            // 去重：避免同效果多次入队（如 Update 中 inactive 入队后又在 CancelOwner 中再次入队）。
            if (_removeQueue.Contains(effect))
            {
                return false;
            }

            // 入队：效果引用存 _removeQueue（保持入队顺序），取消原因旁路存 _removeReasons
            // （O(1) 查找，仅用于诊断日志，不影响行为）。Cancel 在 ReleaseEffect 中调用
            // （与 EnemyManager.ForceRemove 先入队再统一处理的模式一致）。
            _removeQueue.Add(effect);
            _removeReasons[effect] = reason;
            return true;
        }

        /// <summary>
        /// 效果→取消原因临时映射（仅用于 _removeQueue 处理时传递诊断原因）。
        /// <para>使用 <see cref="Dictionary{TKey, TValue}"/> 仅用于 O(1) 查找原因，
        /// 不用于决定遍历顺序。处理完后移除条目。</para>
        /// </summary>
        private readonly Dictionary<IAttackEffect, string> _removeReasons =
            new Dictionary<IAttackEffect, string>();

        /// <summary>
        /// 处理移除队列：统一取消并回收所有待移除效果。
        /// </summary>
        /// <remarks>
        /// <para>由 <see cref="Update"/> 在遍历结束后调用。也可由外部在安全点显式调用
        /// （如 Settling 清理前的同步点）。处理后清空队列。幂等。</para>
        /// <para><b>不重入销毁集合：</b>本方法在 Update 遍历结束后执行，不在伤害调用栈内。</para>
        /// </remarks>
        internal void ProcessRemoveQueue()
        {
            if (_removeQueue.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _removeQueue.Count; i++)
            {
                IAttackEffect effect = _removeQueue[i];
                string reason = _removeReasons.TryGetValue(effect, out string r) ? r : "removed";
                ReleaseEffect(effect, reason);
            }

            _removeQueue.Clear();
            _removeReasons.Clear();
        }

        /// <summary>
        /// 释放单个效果：从集合移除，调 Cancel，经委托归还池（对应 JS <c>_release</c>）。
        /// </summary>
        /// <param name="effect">要释放的效果。</param>
        /// <param name="reason">取消原因。</param>
        private void ReleaseEffect(IAttackEffect effect, string reason)
        {
            // 从活动集合与委托映射移除（对应 JS effects.delete / records.delete）。
            _effects.Remove(effect);
            _releaseDelegates.TryGetValue(effect, out Action<IAttackEffect> releaseToPool);
            _releaseDelegates.Remove(effect);

            // 调 Cancel：停止效果活动状态、释放外部引用（不造成伤害）。
            // 对应 JS effect.cleanup(reason)。Cancel 幂等。
            try
            {
                effect.Cancel(reason);
            }
            catch (Exception ex)
            {
                // 防御性捕获：Cancel 不应抛出，但保证单个效果清理失败不中断批量移除。
                Log.Error($"{LogTag} Cancel 效果异常 reason={reason}: {ex}");
            }

            // 经委托归还池（对应 JS objectPool.recoverByClass(effect)）。
            // releaseToPool 内部执行 pool.Release((T)effect)，pool.Release 会先调 ResetState 再入池。
            // 无委托时跳过（对应 JS 无 objectPool 或无 poolClass）。
            if (releaseToPool != null)
            {
                try
                {
                    releaseToPool(effect);
                }
                catch (Exception ex)
                {
                    // 池回收失败不阻断，但可能影响 Acquire/Release 对称计数。
                    Log.Error($"{LogTag} 池回收效果异常 reason={reason}: {ex}");
                }
            }
        }

        // ====================================================================
        // Settling 静默清理 —— 取消全部活动效果，不造成伤害
        // --------------------------------------------------------------------
        // 对应 JS gameOver()：遍历 remove 再 clear。但 C# 语义更强：
        // Settling 静默清理 MUST 不造成伤害（spec "Settling has no gameplay damage authority"）。
        // Cancel 只停止效果活动状态、释放外部引用，不调 Update/Hit，满足此约束。
        // ====================================================================

        /// <summary>
        /// Settling 静默清理：取消并回收全部活动效果，不造成伤害（对应 JS <c>gameOver()</c>）。
        /// </summary>
        /// <remarks>
        /// <para><b>Settling 静默清理（spec "Settling has no gameplay damage authority" /
        /// "Runtime quiescence and cleanup have one ordered owner"）：</b>
        /// 由 <c>BattleRuntime.EnterSettling</c> 在"清理 AttackEffectManager"步骤调用。
        /// 取消并回收全部残余效果，且不造成伤害（只调 <see cref="IAttackEffect.Cancel"/>，
        /// 不调 <see cref="IAttackEffect.Update"/> 或任何伤害提交）。</para>
        ///
        /// <para><b>幂等：</b>重复调用为空操作。由 <c>BattleRuntime.EnterSettling</c> 调用，
        /// 也可由测试重置调用。</para>
        ///
        /// <para><b>不重入销毁集合：</b>快照遍历，逐个 ReleaseEffect。ReleaseEffect 从
        /// <see cref="_effects"/> 移除并调 Cancel + 池回收。遍历使用快照缓冲避免迭代异常。</para>
        ///
        /// <para><b>顺序（对应 JS <c>for (const effect of [...this.effects]) remove</c>）：</b>
        /// 按登记顺序逐个取消回收，顺序确定。</para>
        /// </remarks>
        internal void Clear()
        {
            if (IsCleared)
            {
                return;
            }

            // 快照遍历，避免遍历中 ReleaseEffect 修改 _effects 导致迭代异常。
            _updateBuffer.Clear();
            foreach (IAttackEffect effect in _effects)
            {
                _updateBuffer.Add(effect);
            }

            for (int i = 0; i < _updateBuffer.Count; i++)
            {
                IAttackEffect effect = _updateBuffer[i];
                ReleaseEffect(effect, "game-over");
            }

            _effects.Clear();
            _releaseDelegates.Clear();
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
        /// <para>JS <c>gameOver()</c> 先遍历 <c>remove</c> 再 <c>effects.clear()</c>。
        /// C# <see cref="Clear"/> 已完成等价语义（快照遍历 ReleaseEffect + 清空集合）。
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
        /// 获取活动效果列表的只读快照（按登记顺序，诊断用）。
        /// </summary>
        internal IReadOnlyList<IAttackEffect> GetEffectsSnapshot()
        {
            return _effects.ToArray();
        }

        /// <summary>
        /// 效果是否在活动集合中登记（诊断用）。
        /// </summary>
        internal bool Contains(IAttackEffect effect)
        {
            return effect != null && _releaseDelegates.ContainsKey(effect);
        }

        /// <summary>
        /// 效果是否有回收委托（诊断用，验证池回收对称性）。
        /// </summary>
        internal bool HasReleaseDelegate(IAttackEffect effect)
        {
            if (effect == null || !_releaseDelegates.TryGetValue(effect, out Action<IAttackEffect> del))
            {
                return false;
            }
            return del != null;
        }
    }
}
