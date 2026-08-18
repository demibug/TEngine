using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>
    /// 以可回放的帧时钟管理接触伤害、刀兵命中、攻击释放等到期动作，并提供攻击冷却判断。
    /// </summary>
    /// <remarks>
    /// <para><b>双时钟语义（决策 0.9，12.2 证据）：</b>冷却与到期判断只读 <see cref="FrameNowMs"/>（外部帧时间戳，同一外部帧的所有子步观察同一值，对应还原工程 <c>Laya.timer.currTimer</c> 经 <c>now()</c> 派生），<b>不</b>读 <c>stepMs</c>。这保证：</para>
    /// <list type="bullet">
    /// <item>同一外部帧多次子步的冷却判断时间戳不变，避免卡顿帧（如 550ms 拆成 7 子步）因时间戳递增而多触发攻击。</item>
    /// <item>同时间戳的重复攻击被冷却拒绝（还原工程 <c>EnemyAttack.test.js:9,15</c> 与 <c>AttackScheduler.js:18,23,30,31</c> 的“same-timestamp duplicate attack is rejected”证据）。</item>
    /// </list>
    /// <para><b>到期动作（design.md 第 2 节、5.5 禁止双轨推进）：</b>还原工程用 <c>Laya.timer.once</c> 注册接触伤害（<c>EnemyBase.js:428,438-447</c> <c>CONTACT_DAMAGE_DELAY_MS=50</c>）与刀兵命中（500ms），C# 侧统一改为逻辑到期动作，由本调度器按 <c>frameNowMs</c> 判断是否到期、按 <see cref="BattleUpdatePhase.DueActionsAndInput"/> 在固定位置执行，不得保留 TEngine Timer/Laya Timer 与 Manager 双轨推进。</para>
    /// <para><b>所有权：</b>本类型是纯逻辑调度核心，不依赖 <c>BattleRuntime</c>（Runtime 在后续批次构建后会持有并使用本调度器，而非本调度器依赖 Runtime）。也不实现 TEngine <c>IUpdateModule</c>——更新由 <see cref="BattleSimulation"/> 驱动，<see cref="BattleSimulation"/> 由 <c>BattleModule</c>/<c>BattleRuntime</c> 驱动，<c>BattleManager</c> 不兼任框架更新入口（design.md 第 2 节）。</para>
    /// <para><b>确定性：</b>到期动作队列保持稳定插入顺序执行，不依赖 <c>Dictionary</c>/<c>HashSet</c> 的未定义遍历顺序决定目标与伤害（spec.md “Simulation is reproducible”）。</para>
    /// </remarks>
    internal sealed class BattleActionScheduler
    {
        /// <summary>
        /// 单一外部帧的时间戳（毫秒）。同一外部帧的所有子步共享同一值，由 <see cref="BattleSimulation"/> 在帧入口一次性传入，子步内不变。
        /// </summary>
        /// <remarks>
        /// 对应还原工程 <c>Laya.timer.currTimer</c>（<c>GameLoop.js:46</c> 在子步循环外只读一次）。冷却判断、到期动作触发时间点均读此值。
        /// </remarks>
        public long FrameNowMs { get; private set; }

        /// <summary>
        /// 已冻结标记：首次 <c>TryFreeze</c> 成功后，调度器不再推进冷却、不再触发新到期动作、不再注册新到期动作。
        /// </summary>
        /// <remarks>
        /// 语义对应决策 0.4 与 spec.md “Battle result is frozen once”：Settling 取消弹道、接触伤害、刀兵命中、攻击释放与动画回调，且不再修改生命/奖励/死亡记录。
        /// </remarks>
        public bool IsFrozen { get; private set; }

        // 到期动作队列：按注册（插入）顺序执行，保证确定性。不使用无序集合。
        private readonly List<ScheduledAction> _pending = new List<ScheduledAction>();

        /// <summary>
        /// 在外部帧入口设置本帧时间戳。同一外部帧只应调用一次；子步循环内不得重置，以保证所有子步观察同一 <c>frameNowMs</c>。
        /// </summary>
        /// <param name="frameNowMs">本外部帧时间戳（毫秒），即还原工程 <c>currTimer</c>。注意 550ms 外部帧传入 550，而非截断后的 500（12.2.3 证据：观察值在截断前读取）。</param>
        public void BeginFrame(long frameNowMs)
        {
            FrameNowMs = frameNowMs;
        }

        /// <summary>
        /// 注册一个逻辑到期动作（如 50ms 接触伤害、500ms 刀兵命中、攻击释放回调）。
        /// </summary>
        /// <param name="dueAtMs">到期时间戳（毫秒），通常 = <see cref="FrameNowMs"/> + 延迟毫秒。</param>
        /// <param name="action">到期时执行的同步回调。回调内的伤害/状态提交作为同步副作用生效，不得被无依据推迟到帧末。</param>
        /// <returns>注册句柄，可用于取消（如目标死亡、Settling 静默清理）。</returns>
        /// <remarks>
        /// 已冻结时不接受新注册，返回 <c>null</c>，对应 Settling “关闭命令、新生成和新攻击入口”。
        /// </remarks>
        public ScheduledActionHandle Schedule(long dueAtMs, Action action)
        {
            if (IsFrozen)
            {
                return null;
            }

            if (action == null)
            {
                return null;
            }

            var scheduled = new ScheduledAction(dueAtMs, action);
            _pending.Add(scheduled);
            return new ScheduledActionHandle(scheduled);
        }

        /// <summary>
        /// 判断单位攻击是否冷却完毕。冷却判断只读 <see cref="FrameNowMs"/>，同帧所有子步结果一致。
        /// </summary>
        /// <param name="lastAttackTimeMs">上次攻击的时间戳（毫秒）。</param>
        /// <param name="intervalMs">攻击冷却间隔（毫秒）。</param>
        /// <returns>是否已冷却完毕可触发攻击。</returns>
        /// <remarks>
        /// 对应 <c>AttackScheduler.js:23,30,31</c>：<c>currentTime - unit.lastAttackTime &gt;= intervalMs</c>，<c>currentTime = now()</c> 同帧不变。
        /// </remarks>
        public bool IsAttackCooldownReady(long lastAttackTimeMs, long intervalMs)
        {
            return FrameNowMs - lastAttackTimeMs >= intervalMs;
        }

        /// <summary>
        /// 推进并执行所有已到期的动作。在 <see cref="BattleUpdatePhase.DueActionsAndInput"/> 阶段、每个子步开始处调用一次。
        /// </summary>
        /// <param name="stepMs">当前子步时长（毫秒）。仅用于向前推进到期判断基准——到期判断仍以 <see cref="FrameNowMs"/> 为准。</param>
        /// <returns>本子步实际执行的到期动作数量。</returns>
        /// <remarks>
        /// <para>执行规则（确定性）：</para>
        /// <list type="bullet">
        /// <item>按注册顺序遍历，到期（<c>dueAtMs &lt;= FrameNowMs</c>）则执行并移除；未到期保留。</item>
        /// <item>回调内的伤害/死亡/奖励/胜负作为同步副作用立即生效，<b>不</b>推迟到帧末结算阶段（spec.md “Update phases are explicit and single-owned”）。</item>
        /// <item>回调内若触发首次 <c>TryFreeze</c>，<see cref="BattleSimulation"/> 在紧随的检查点中止当前 phase 剩余迭代与后续 phase/子步；本方法正常返回当前已完成执行的同步提交（spec.md “Battle result is frozen once”、决策 0.4）。</item>
        /// </list>
        /// <para><b>不重入销毁集合：</b><c>TryFreeze</c> 不在回调调用栈内直接销毁 Manager 或集合，集合清理由 Settling 检查点统一执行（spec.md “Freeze occurs inside a manager update”）。</para>
        /// </remarks>
        public int FlushDueActions(long stepMs)
        {
            if (IsFrozen)
            {
                return 0;
            }

            // 确定性执行：按注册顺序（FIFO）收集到期项，再按同一顺序执行。
            // 到期判断基准为 FrameNowMs（同帧不变），stepMs 不参与冷却/到期判断，仅作为概念传入。
            // 先收集再执行避免遍历中修改集合造成跳过，同时保证与 JS 黄金轨迹一致的执行顺序。
            int executed = 0;
            List<ScheduledAction> dueThisStep = null;
            for (int i = 0; i < _pending.Count; i++)
            {
                ScheduledAction item = _pending[i];
                if (item.Executed || item.Cancelled)
                {
                    continue;
                }

                if (item.DueAtMs <= FrameNowMs)
                {
                    (dueThisStep ??= new List<ScheduledAction>()).Add(item);
                }
            }

            if (dueThisStep == null)
            {
                // 无到期项，但需清理已取消/已执行项。
                PruneCompleted();
                return 0;
            }

            // 先从待执行队列移除全部到期项，再按注册顺序执行，避免回调内修改集合影响遍历。
            for (int i = 0; i < dueThisStep.Count; i++)
            {
                _pending.Remove(dueThisStep[i]);
            }

            for (int i = 0; i < dueThisStep.Count; i++)
            {
                ScheduledAction item = dueThisStep[i];
                if (item.Cancelled || item.Executed)
                {
                    // 回调前可能已被前一回调间接取消。
                    continue;
                }

                // 先执行回调，再标记 executed：若先 MarkExecuted，ScheduledAction.Invoke 的
                // Executed 守卫会直接返回，到期回调永远不执行（Buff duration 强依赖 exactly-once）。
                item.Invoke();
                item.MarkExecuted();
                executed++;
                // 回调可能触发 TryFreeze；IsFrozen 由 BattleSimulation 在检查点统一置位。
                // 当前同步提交正常返回后，BattleSimulation 的检查点负责中止剩余 phase/子步。
                if (IsFrozen)
                {
                    break;
                }
            }

            // 清理本轮收集之外的已取消/已执行项。
            PruneCompleted();

            return executed;
        }

        /// <summary>
        /// 清理已执行或已取消的登记项，保持待执行队列紧凑。
        /// </summary>
        private void PruneCompleted()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                ScheduledAction item = _pending[i];
                if (item.Executed || item.Cancelled)
                {
                    _pending.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 取消单个到期动作（如目标死亡、投射物命中后回收）。幂等。
        /// </summary>
        public void Cancel(ScheduledActionHandle handle)
        {
            if (handle == null || handle.Action == null)
            {
                return;
            }

            handle.Action.Cancel();
        }

        /// <summary>
        /// 冻结调度器：进入 Settling 后调用。冻结后不再推进冷却、不再触发或注册新到期动作。
        /// 幂等：重复调用无副作用。
        /// </summary>
        /// <remarks>
        /// 不在此处销毁 Manager 或集合；残余到期动作的清理由 <c>BattleRuntimeScope</c>/<c>BattleRuntime</c> 在 Settling 静默清理顺序中统一执行（spec.md “Runtime quiescence and cleanup have one ordered owner”）。
        /// </remarks>
        public void Freeze()
        {
            IsFrozen = true;
        }

        /// <summary>
        /// 清空所有到期动作与状态，供同局运行时销毁时调用。重置后可被新运行时复用实例（若池化），但不得沿用旧局时间戳。
        /// </summary>
        public void Clear()
        {
            _pending.Clear();
            IsFrozen = false;
            FrameNowMs = 0;
        }

        /// <summary>
        /// 单个已注册的到期动作。内部类型，不暴露给战斗业务层，避免外部直接 Invoke。
        /// </summary>
        internal sealed class ScheduledAction
        {
            private readonly Action _callback;

            public long DueAtMs { get; }
            public bool Executed { get; private set; }
            public bool Cancelled { get; private set; }

            public ScheduledAction(long dueAtMs, Action callback)
            {
                DueAtMs = dueAtMs;
                _callback = callback;
            }

            public void MarkExecuted()
            {
                Executed = true;
            }

            public void Cancel()
            {
                Cancelled = true;
            }

            public void Invoke()
            {
                if (Cancelled || Executed)
                {
                    return;
                }

                _callback?.Invoke();
            }
        }
    }

    /// <summary>
    /// 到期动作注册句柄。持有对内部动作的引用，仅用于取消。
    /// </summary>
    internal sealed class ScheduledActionHandle
    {
        internal BattleActionScheduler.ScheduledAction Action { get; }

        internal ScheduledActionHandle(BattleActionScheduler.ScheduledAction action)
        {
            Action = action;
        }
    }
}
