using System;

namespace GameBattle
{
    /// <summary>
    /// 战斗逻辑时钟唯一入口：以 <c>elapseSeconds</c> 为唯一逻辑时间源，执行 500ms 截断、最大 80ms 子步拆分、显式阶段调度、结果冻结与冻结后中止检查点。
    /// </summary>
    /// <remarks>
    /// <para><b>所有权链（design.md 第 2 节，决策 0.1/0.2）：</b></para>
    /// <code>
    /// TEngine Update
    ///   -> BattleModule.OnUpdate(elapseSeconds, realElapseSeconds)
    ///     -> BattleRuntime.Advance(deltaMilliseconds)
    ///       -> BattleSimulation.Advance(clamp 500ms, split &lt;= 80ms)
    ///         -> explicit ordered phases (BattleUpdatePhase)
    ///           -> managers/systems once per substep
    /// </code>
    /// <para><b>不实现 TEngine <c>IUpdateModule</c> 的约束声明：</b>本类型 <b>不</b>实现 <c>TEngine.IUpdateModule</c>。
    /// 框架更新入口与子步拆分由本类型唯一承担，但本类型本身由 <c>BattleModule</c>（TEngine 模块，在后续批次 2.4/2.6 构建）在 <c>OnUpdate</c> 中转交帧时间驱动，
    /// 而非由框架直接轮询。<c>BattleManager</c>（Phase 2/3.10 构建）只负责战斗规则、波次关联和胜负判断，<b>不</b>实现 <c>IUpdateModule</c>，
    /// 否则它会同时承担框架生命周期、拆步和业务规则，使重开、测试和重复注册难以验证（design.md 第 2 节）。
    /// 故 Simulation 与 BattleManager 均不挂载到 <c>ModuleSystem._updateExecuteList</c>。</para>
    /// <para><b>双时钟（决策 0.9，12.2 证据，spec.md “Frame time and substep time preserve source behavior”）：</b></para>
    /// <list type="bullet">
    /// <item><see cref="FrameNowMs"/>：外部帧时间戳（毫秒），同一外部帧的所有子步观察同一值。对应还原工程 <c>Laya.timer.currTimer</c>（<c>GameLoop.js:46</c> 子步循环外只读一次）。攻击冷却、接触伤害冷却、到期动作触发时间点均读此值。</item>
    /// <item><see cref="StepMs"/>：当前子步时长（毫秒），驱动敌人移动、弹道、攻击效果累计。对应还原工程 GameLoop 传给 callback 的 <c>step = Math.min(LOGIC_STEP_MS=80, remaining)</c>（<c>GameLoop.js:52-53</c>）。</item>
    /// </list>
    /// <para><b>不得合并为单一逐子步时钟</b>——合并会改变卡顿帧攻击次数（design.md 第 2 节末段警示、12.2.6）。任何将两种时钟合并的改动 MUST 作为批准的行为偏差记录并重新生成黄金轨迹。</para>
    /// <para><b>暂停语义（决策 0.9，12.2.4）：</b>暂停期间不以真实时间（<c>realElapseSeconds</c>）补偿推进。恢复后只从下一次有效 <c>elapseSeconds</c> 继续推进。本类型自身 <see cref="Pause"/> 标志为 true 时 <see cref="Advance"/> 直接返回，不推进任何子步；生产暂停必须同时冻结帧时钟（等价 Laya <c>scale=0</c>），仅设标志而不冻结时钟会导致恢复时补步（12.2.4 强化用例）。</para>
    /// <para><b>确定性（spec.md “Simulation is reproducible”）：</b>从外部接收逻辑时间、随机源、配置快照和输入命令，不依赖未声明的真实时间或无序集合遍历决定目标、伤害和胜负。</para>
    /// </remarks>
    internal sealed class BattleSimulation
    {
        /// <summary>
        /// 单帧逻辑时间最大截断值（毫秒）。对应还原工程 <c>GameLoop.MAX_FRAME_DELTA_MS = 500</c>（<c>GameLoop.js:110</c>）。
        /// </summary>
        public const int MaxFrameDeltaMs = 500;

        /// <summary>
        /// 最大子步长（毫秒），不是固定步长累加器。对应还原工程 <c>GameLoop.LOGIC_STEP_MS = 80</c>（<c>GameLoop.js:109</c>）。
        /// </summary>
        /// <remarks>
        /// 16ms 必须本帧立即推进 16ms，不等待累计到 80ms（spec.md “Advance by sixteen milliseconds”、12.2.6、8.1）。
        /// </remarks>
        public const int LogicStepMs = 80;

        /// <summary>
        /// 当前外部帧时间戳（毫秒）。同一外部帧的所有子步共享同一值，由 <see cref="Advance"/> 在帧入口一次性设置。
        /// </summary>
        /// <remarks>
        /// 550ms 外部帧传入 550，本字段观察 550（<b>未被截断</b>），而 <see cref="StepMs"/> 累计最多推进 500ms（12.2.3：观察值在 <c>Math.min</c> 截断前读取）。
        /// </remarks>
        public long FrameNowMs { get; private set; }

        /// <summary>
        /// 当前子步时长（毫秒）。每子步 = <c>Math.Min(LogicStepMs, remaining)</c>，不足 80ms 的余数在当前帧立即推进。
        /// </summary>
        public long StepMs { get; private set; }

        /// <summary>
        /// 规则位移累计时间（毫秒），按 <see cref="StepMs"/> 累加，上限受 500ms 截断约束。对应还原工程 <c>GameLoop.elapsedGameTime</c>。
        /// </summary>
        public long ElapsedGameTimeMs { get; private set; }

        /// <summary>
        /// 上一外部帧已吸收的时间戳（毫秒）。对应还原工程 <c>GameLoop.lastTimer</c>，用于计算下一帧 <c>remaining = frameNow - lastTimer</c>。
        /// </summary>
        public long LastTimerMs { get; private set; }

        /// <summary>
        /// 暂停标记。为 true 时 <see cref="Advance"/> 不推进任何子步（对应 <c>GameLoop.js:45</c>）。
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 是否已冻结（首次 <c>TryFreeze</c> 成功后置位）。冻结后中止剩余 phase/子步，进入零伤害 Settling。
        /// </summary>
        public bool IsFrozen { get; private set; }

        /// <summary>
        /// 到期动作与攻击冷却调度器。本类型持有并以 <see cref="FrameNowMs"/> 驱动其冷却/到期判断。
        /// </summary>
        public BattleActionScheduler ActionScheduler { get; }

        // 阶段执行回调表：按 BattleUpdatePhase 顺序注册，每子步依次调用一次。
        // 使用数组按枚举顺序索引，保证确定性，不依赖字典遍历顺序。
        private readonly Action<long, long, BattleUpdatePhase>[] _phaseHandlers;

        // 结果冻结回调：首次完成事实经此入口冻结。幂等。
        private readonly Func<bool> _tryFreezeHandler;

        /// <summary>
        /// 构造模拟器。
        /// </summary>
        /// <param name="phaseHandlers">
        /// 按 <see cref="BattleUpdatePhase"/> 顺序的阶段执行回调数组（索引即阶段整数值）。
        /// 每个回调签名 <c>(frameNowMs, stepMs, phase) =&gt; void</c>，在对应阶段每子步调用一次。
        /// 回调内可调用 <c>TryFreeze</c>；模拟器在紧随的检查点中止剩余迭代。
        /// </param>
        /// <param name="tryFreezeHandler">
        /// 幂等结果冻结判断回调：返回 true 表示首次冻结成功。模拟器据此在检查点中止。
        /// 该回调 MUST NOT 在伤害调用栈内重入销毁 Manager 或集合（spec.md “Freeze occurs inside a manager update”）。
        /// </param>
        /// <param name="actionScheduler">到期动作/冷却调度器。若为 null 则内部新建一个空调度器。</param>
        public BattleSimulation(
            Action<long, long, BattleUpdatePhase>[] phaseHandlers,
            Func<bool> tryFreezeHandler,
            BattleActionScheduler actionScheduler = null)
        {
            _phaseHandlers = phaseHandlers ?? throw new ArgumentNullException(nameof(phaseHandlers));
            _tryFreezeHandler = tryFreezeHandler ?? throw new ArgumentNullException(nameof(tryFreezeHandler));
            ActionScheduler = actionScheduler ?? new BattleActionScheduler();

            int phaseCount = Enum.GetValues(typeof(BattleUpdatePhase)).Length;
            if (_phaseHandlers.Length < phaseCount)
            {
                throw new ArgumentException(
                    $"phaseHandlers 长度 {_phaseHandlers.Length} 小于 BattleUpdatePhase 阶段数 {phaseCount}，阶段回调不完整。",
                    nameof(phaseHandlers));
            }
        }

        /// <summary>
        /// 推进一个外部帧。执行 500ms 截断、最大 80ms 子步拆分、显式阶段调度，并在首次 <c>TryFreeze</c> 后中止检查点。
        /// </summary>
        /// <param name="frameNowMs">本外部帧时间戳（毫秒）。注意：传入的是<b>未截断</b>的原始帧时间戳（如 550），截断只作用于规则位移累计，不影响 <see cref="FrameNowMs"/> 观察值。</param>
        /// <remarks>
        /// <para><b>拆步算法（对应 <c>GameLoop.js:44-58</c>）：</b></para>
        /// <list type="number">
        /// <item>若 <see cref="IsPaused"/> 为 true，直接返回，不推进（<c>GameLoop.js:45</c>）。</item>
        /// <item><c>remaining = frameNowMs - LastTimerMs</c>；若 <c>remaining &lt;= 0</c> 直接返回，不补步（<c>GameLoop.js:48</c>）。</item>
        /// <item><c>remaining = Math.Min(remaining, MaxFrameDeltaMs=500)</c> 截断（<c>GameLoop.js:49</c>）。</item>
        /// <item>设置 <see cref="FrameNowMs"/> = 传入的原始 <paramref name="frameNowMs"/>（未截断，同帧所有子步不变）。</item>
        /// <item>通知 <see cref="ActionScheduler"/>.<c>BeginFrame</c> 同一 <c>frameNowMs</c>。</item>
        /// <item><c>while (remaining &gt; 0)</c>：每子步 <c>step = Math.Min(LogicStepMs=80, remaining)</c>，按 <see cref="BattleUpdatePhase"/> 顺序执行阶段回调，<c>ElapsedGameTimeMs += step</c>，<c>remaining -= step</c>。</item>
        /// <item>每阶段回调后检查 <see cref="IsFrozen"/>：若冻结则跳过当前子步后续 phase 与当前帧剩余子步。</item>
        /// <item><c>LastTimerMs = frameNowMs</c> 吸收完整帧时间戳（<c>GameLoop.js:57</c>），下一帧 <c>remaining</c> 不再含本帧时间。</item>
        /// </list>
        /// <para><b>黄金基线对照（golden-battle-bundle.json frameTimeSequence）：</b></para>
        /// <list type="bullet">
        /// <item>16ms → 1 子步 [16]，FrameNowMs=16，ElapsedGameTime=16。</item>
        /// <item>80ms → 1 子步 [80]，FrameNowMs=80，ElapsedGameTime=80。</item>
        /// <item>550ms → 7 子步 [80,80,80,80,80,80,20]，FrameNowMs=550，ElapsedGameTime=500。</item>
        /// <item>0ms（暂停）→ 0 子步，不补步。</item>
        /// </list>
        /// </remarks>
        public void Advance(long frameNowMs)
        {
            if (IsFrozen)
            {
                // 已冻结（Settling）：不再推进任何子步。TEngine 全局更新驱动仍可继续，但本 Simulation 不推进（spec.md “Module receives updates while settling”）。
                return;
            }

            if (IsPaused)
            {
                // 决策 0.9 / 12.2.4：暂停不补步。
                return;
            }

            long remaining = frameNowMs - LastTimerMs;
            if (remaining <= 0)
            {
                // currTimer 未推进，直接返回，不补步（GameLoop.js:48）。
                return;
            }

            // 500ms 截断（GameLoop.js:49,110）。截断只作用于规则位移累计，FrameNowMs 仍观察原始 frameNowMs。
            remaining = Math.Min(remaining, MaxFrameDeltaMs);

            // 设置帧级时间戳：同帧所有子步观察同一值（GameLoop.js:46 子步循环外只读一次）。
            FrameNowMs = frameNowMs;
            ActionScheduler.BeginFrame(frameNowMs);

            while (remaining > 0)
            {
                // 最大 80ms 拆步；不足 80ms 的余数在当前帧立即推进（GameLoop.js:52）。
                long step = Math.Min(LogicStepMs, remaining);
                StepMs = step;
                remaining -= step;

                bool phaseAborted = false;
                foreach (BattleUpdatePhase phase in PhaseOrder)
                {
                    ExecutePhase(phase);

                    // TryFreeze 中止检查点：首次冻结成功后，当前 phase 剩余迭代已完成同步提交并返回，
                    // 此处跳过当前子步后续 phase（spec.md "Battle result is frozen once"、决策 0.4）。
                    // task 6.10 闭环：phase handler 内部可能触发 BattleManager.TryFreezeResult →
                    // resultBuilder.TryFreeze → resultBuilder.IsFrozen=true。此处调用 Simulation.TryFreeze()
                    // 检查 _tryFreezeHandler（指向 resultBuilder.IsFrozen）并据此置位 Simulation.IsFrozen
                    // + Freeze ActionScheduler，使后续检查点能正确中止。
                    TryFreeze();

                    if (IsFrozen)
                    {
                        phaseAborted = true;
                        break;
                    }
                }

                ElapsedGameTimeMs += step;

                if (phaseAborted || IsFrozen)
                {
                    // 决策 0.4：首次 TryFreeze 后只完成当前同步提交并中止剩余 phase/子步。
                    // 跳过当前帧剩余子步。
                    break;
                }
            }

            // 吸收完整帧时间戳（GameLoop.js:57），下一帧 remaining 不再含本帧时间。
            LastTimerMs = frameNowMs;
        }

        /// <summary>
        /// 显式阶段顺序（决策 0.3 冻结）：DueActionsAndInput → Enemy → Projectile → AttackRelease → WaveSpawn → UnitAttack → AttackEffect。
        /// </summary>
        private static readonly BattleUpdatePhase[] PhaseOrder =
        {
            BattleUpdatePhase.DueActionsAndInput,
            BattleUpdatePhase.Enemy,
            BattleUpdatePhase.Projectile,
            BattleUpdatePhase.AttackRelease,
            BattleUpdatePhase.WaveSpawn,
            BattleUpdatePhase.UnitAttack,
            BattleUpdatePhase.AttackEffect,
        };

        /// <summary>
        /// 执行单个阶段回调。回调内若调用 <c>TryFreeze</c> 并首次成功，<see cref="IsFrozen"/> 置位，<see cref="Advance"/> 在紧随的检查点中止。
        /// </summary>
        private void ExecutePhase(BattleUpdatePhase phase)
        {
            Action<long, long, BattleUpdatePhase> handler = _phaseHandlers[(int)phase];
            handler?.Invoke(FrameNowMs, StepMs, phase);
        }

        /// <summary>
        /// 幂等结果冻结入口。供阶段回调内首次完成事实调用。
        /// </summary>
        /// <returns>是否首次冻结成功（后续调用返回 false）。</returns>
        /// <remarks>
        /// <para>语义（spec.md “Battle result is frozen once”、决策 0.4）：</para>
        /// <list type="bullet">
        /// <item>首次成功冻结后，当前正在执行的同步伤害或状态提交正常返回（调用方不抛异常、不重入销毁集合）。</item>
        /// <item>模拟器在紧随的检查点跳过当前 phase 剩余迭代、当前子步后续 phase、当前帧剩余子步，再切换到 Settling。</item>
        /// <item>后续同帧或相邻帧的完成事实被忽略（幂等）。</item>
        /// <item>MUST NOT 在伤害调用栈内重入销毁 Manager 或集合；集合清理由 Settling 检查点统一执行。</item>
        /// </list>
        /// </remarks>
        public bool TryFreeze()
        {
            if (IsFrozen)
            {
                return false;
            }

            bool frozenNow = _tryFreezeHandler();
            if (frozenNow)
            {
                IsFrozen = true;
                ActionScheduler.Freeze();
            }

            return frozenNow;
        }

        /// <summary>
        /// 暂停模拟。幂等。暂停期间 <see cref="Advance"/> 不推进任何子步。
        /// </summary>
        /// <remarks>
        /// 生产暂停必须同时冻结帧时钟（等价 Laya <c>scale=0</c>），否则恢复时 <c>remaining = frameNow - lastTimer</c> 会一次性吞掉暂停期间真实时间差（12.2.4 强化用例）。
        /// 调用方（<c>BattleModule</c>）负责确保暂停时不再以真实时间推进 <c>frameNowMs</c>。
        /// </remarks>
        public void Pause()
        {
            IsPaused = true;
        }

        /// <summary>
        /// 恢复模拟。恢复后只从下一次有效 <c>elapseSeconds</c> 继续推进，不补暂停期间真实时间（决策 0.9、12.2.4）。
        /// </summary>
        public void Resume()
        {
            IsPaused = false;
        }

        /// <summary>
        /// 重置模拟器到初始时钟状态，供新局运行时复用实例。不得沿用旧局 <c>FrameNowMs</c>/<c>ElapsedGameTimeMs</c>（决策 0.2：重开销毁旧 Runtime 状态）。
        /// </summary>
        public void Reset()
        {
            FrameNowMs = 0;
            StepMs = 0;
            ElapsedGameTimeMs = 0;
            LastTimerMs = 0;
            IsPaused = false;
            IsFrozen = false;
            ActionScheduler.Clear();
        }
    }
}
