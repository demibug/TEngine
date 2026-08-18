using System;
using System.Collections.Generic;
using GameBattle.Tests.EditMode.Golden;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode
{
    /// <summary>
    /// BattleSimulation 时间拆步、双时钟与冻结中止测试（OpenSpec change
    /// port-minimal-battle-to-gamebattle task 2.12）。
    /// <para>
    /// 覆盖 spec.md battle-simulation 的下列场景：
    /// 16ms / 80ms / 81ms / 500ms / 550ms / 0ms / 负值；550ms 时 frameNow=550 而
    /// step 合计 500；同帧攻击冷却时间戳不变；冻结后同 phase 剩余对象、后续 phase
    /// 与剩余子步均不执行。
    /// </para>
    /// <para>
    /// 依赖未实现的 EnemyManager/ProjectileManager/AttackEffectManager 的场景
    /// （生成敌人下一步才移动、新近战效果本步累计、新箭下一步才移动）记录在
    /// known_risks 中推迟到对应 Phase 的任务（4.7/5.9）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class BattleSimulationTimeTests
    {
        // ────────────────────────── 测试辅助 ──────────────────────────

        /// <summary>
        /// 创建一个最小的 <see cref="BattleSimulation"/>，阶段回调默认为空操作，
        /// 仅用于验证时间拆步与冻结中止行为。
        /// </summary>
        /// <param name="tryFreezeHandler">结果冻结回调；默认返回 false（不冻结）。</param>
        /// <param name="phaseHandlers">可选阶段回调覆盖；索引即 <see cref="BattleUpdatePhase"/> 整数值。</param>
        private static BattleSimulation CreateSimulation(
            Func<bool> tryFreezeHandler = null,
            Action<long, long, BattleUpdatePhase>[] phaseHandlers = null)
        {
            int phaseCount = Enum.GetValues(typeof(BattleUpdatePhase)).Length;
            var handlers = phaseHandlers ?? new Action<long, long, BattleUpdatePhase>[phaseCount];
            if (handlers.Length < phaseCount)
            {
                throw new ArgumentException(
                    $"phaseHandlers 长度 {handlers.Length} 小于阶段数 {phaseCount}。",
                    nameof(phaseHandlers));
            }

            return new BattleSimulation(
                handlers,
                tryFreezeHandler ?? (() => false));
        }

        /// <summary>
        /// 记录每个子步每个阶段的调用次数与 stepMs/frameNowMs，用于断言阶段执行顺序与中止行为。
        /// </summary>
        private sealed class PhaseCallRecorder
        {
            public readonly List<(long FrameNowMs, long StepMs, BattleUpdatePhase Phase)> Invocations =
                new List<(long, long, BattleUpdatePhase)>();

            public Action<long, long, BattleUpdatePhase> RecorderFor(BattleUpdatePhase phase)
            {
                return (frameNow, step, p) => Invocations.Add((frameNow, step, p));
            }

            public int CountPhase(BattleUpdatePhase phase)
            {
                int n = 0;
                for (int i = 0; i < Invocations.Count; i++)
                {
                    if (Invocations[i].Phase == phase)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        // ────────────────────────── 16ms ──────────────────────────

        [Test]
        [Description("16ms 外部帧：单子步 16ms，不等待累计到 80ms。spec.md Advance by sixteen milliseconds。")]
        public void AdvanceBy16Ms_SingleSubstep16()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(16);

            Assert.AreEqual(16, sim.FrameNowMs, "FrameNowMs 应观察 16ms 原始帧时间戳。");
            Assert.AreEqual(16, sim.ElapsedGameTimeMs, "规则位移应累计 16ms。");
            Assert.AreEqual(16, sim.StepMs, "最后子步时长应为 16ms。");
        }

        // ────────────────────────── 80ms ──────────────────────────

        [Test]
        [Description("80ms 外部帧：单子步 80ms，不拆分。")]
        public void AdvanceBy80Ms_SingleSubstep80()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(80);

            Assert.AreEqual(80, sim.FrameNowMs, "FrameNowMs 应观察 80ms。");
            Assert.AreEqual(80, sim.ElapsedGameTimeMs, "规则位移应累计 80ms。");
            Assert.AreEqual(80, sim.StepMs, "子步时长应为 80ms。");
        }

        // ────────────────────────── 81ms ──────────────────────────

        [Test]
        [Description("81ms 外部帧：拆分为 80ms + 1ms 两子步，不足 80ms 余数立即推进。")]
        public void AdvanceBy81Ms_Splits80And1()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(81);

            Assert.AreEqual(81, sim.FrameNowMs, "FrameNowMs 应观察 81ms。");
            Assert.AreEqual(81, sim.ElapsedGameTimeMs, "规则位移应累计 81ms。");
            Assert.AreEqual(1, sim.StepMs, "最后子步时长应为 1ms（余数）。");
        }

        // ────────────────────────── 500ms ──────────────────────────

        [Test]
        [Description("500ms 外部帧：达到截断上限，7 子步 [80,80,80,80,80,80,20]，不拆分更多。")]
        public void AdvanceBy500Ms_SevenSubstepsNotClamped()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(500);

            Assert.AreEqual(500, sim.FrameNowMs, "FrameNowMs 应观察 500ms。");
            Assert.AreEqual(500, sim.ElapsedGameTimeMs, "规则位移应累计 500ms。");
            Assert.AreEqual(20, sim.StepMs, "最后子步时长应为 20ms。");
        }

        // ────────────────────────── 550ms ──────────────────────────

        [Test]
        [Description("550ms 外部帧：FrameNowMs 观察 550（未截断），规则位移合计 500（500ms 截断），7 子步 [80,80,80,80,80,80,20]。"
            + " spec.md Advance by five hundred and fifty milliseconds / One frame advances five hundred milliseconds。")]
        public void AdvanceBy550Ms_FrameNow550_StepSum500()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(550);

            Assert.AreEqual(550, sim.FrameNowMs,
                "frameNowMs 必须观察 550（未截断），否则双时钟语义被破坏。");
            Assert.AreEqual(500, sim.ElapsedGameTimeMs,
                "规则位移合计必须为 500（500ms 截断）。");
            Assert.AreEqual(20, sim.StepMs,
                "最后子步时长应为 20ms。");
        }

        [Test]
        [Description("550ms 外部帧：7 子步全部观察同一 FrameNowMs=550，证明同帧时间戳不变。")]
        public void AdvanceBy550Ms_AllSubstepsShareSameFrameNow()
        {
            var recorder = new PhaseCallRecorder();
            var handlers = new Action<long, long, BattleUpdatePhase>[Enum.GetValues(typeof(BattleUpdatePhase)).Length];
            // 在 Enemy 阶段记录，每子步都会进入。
            handlers[(int)BattleUpdatePhase.Enemy] = recorder.RecorderFor(BattleUpdatePhase.Enemy);

            BattleSimulation sim = CreateSimulation(phaseHandlers: handlers);
            sim.Advance(550);

            Assert.AreEqual(7, recorder.Invocations.Count,
                "550ms 帧应产生 7 个子步。");
            for (int i = 0; i < recorder.Invocations.Count; i++)
            {
                Assert.AreEqual(550, recorder.Invocations[i].FrameNowMs,
                    $"第 {i} 个子步的 FrameNowMs 必须仍是 550，不得被子步推进改变。");
            }

            // 校验子步时长序列：前 6 步 80ms，末步 20ms。
            long[] expectedSteps = { 80, 80, 80, 80, 80, 80, 20 };
            for (int i = 0; i < expectedSteps.Length; i++)
            {
                Assert.AreEqual(expectedSteps[i], recorder.Invocations[i].StepMs,
                    $"第 {i} 个子步时长应为 {expectedSteps[i]}ms。");
            }
        }

        // ────────────────────────── 0ms ──────────────────────────

        [Test]
        [Description("0ms 外部帧：remaining=0，不推进任何子步。spec.md 暂停语义。")]
        public void AdvanceBy0Ms_NoProgress()
        {
            BattleSimulation sim = CreateSimulation();
            // 先推进一次，建立非零 LastTimerMs。
            sim.Advance(100);
            Assert.AreEqual(100, sim.LastTimerMs, "前置推进后 LastTimerMs 应吸收 100ms。");

            // 再以同时间戳推进：remaining = 100 - 100 = 0。
            sim.Advance(100);
            Assert.AreEqual(100, sim.FrameNowMs, "0 remaining 时 FrameNowMs 不应改变。");
            Assert.AreEqual(100, sim.ElapsedGameTimeMs, "规则位移不应推进。");
        }

        // ────────────────────────── 负值 ──────────────────────────

        [Test]
        [Description("负值外部帧（frameNow < LastTimer）：remaining<0，不推进任何子步，不抛异常。"
            + " 对应 GameLoop.js:48 remaining<=0 直接返回。")]
        public void AdvanceByNegativeMs_NoProgressNoThrow()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(100);
            long elapsedBefore = sim.ElapsedGameTimeMs;

            // 时间倒流：frameNow=50 < LastTimer=100。
            Assert.DoesNotThrow(() => sim.Advance(50),
                "负 remaining 不得抛异常，应直接返回。");

            Assert.AreEqual(elapsedBefore, sim.ElapsedGameTimeMs,
                "负 remaining 不应推进规则位移。");
        }

        [Test]
        [Description("首帧 frameNow=0 且 LastTimer=0：remaining=0，不推进。")]
        public void AdvanceFromZeroToZero_NoProgress()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(0);

            Assert.AreEqual(0, sim.FrameNowMs, "首帧 0ms 不应设置 FrameNowMs。");
            Assert.AreEqual(0, sim.ElapsedGameTimeMs, "首帧 0ms 不应推进规则位移。");
        }

        [Test]
        [Description("首帧负值 frameNow=-10：remaining=-10，不推进，不抛异常。")]
        public void AdvanceFromZeroToNegative_NoProgressNoThrow()
        {
            BattleSimulation sim = CreateSimulation();
            Assert.DoesNotThrow(() => sim.Advance(-10),
                "首帧负值不得抛异常。");
            Assert.AreEqual(0, sim.ElapsedGameTimeMs, "首帧负值不应推进规则位移。");
        }

        // ────────────────────────── 同帧攻击冷却时间戳不变 ──────────────────────────

        [Test]
        [Description("550ms 帧拆成 7 子步，攻击冷却判断在所有子步中观察同一 FrameNowMs=550，"
            + "同一冷却间隔下的重复攻击被拒绝。对应 spec.md Frame time and substep time preserve source behavior。")]
        public void AttackCooldown_TimestampStableWithinFrame()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(550);

            // 调度器在 BeginFrame 时接收并固定 FrameNowMs=550。
            Assert.AreEqual(550, sim.ActionScheduler.FrameNowMs,
                "调度器 FrameNowMs 应与模拟器一致为 550。");

            // 模拟一个单位在子步 0 已攻击（lastAttackTime=550），冷却间隔 80ms。
            long lastAttackMs = sim.FrameNowMs;
            const long intervalMs = 80;

            // 同一帧内后续子步的冷却判断：FrameNowMs 仍是 550，550-550=0 < 80，未冷却。
            bool ready = sim.ActionScheduler.IsAttackCooldownReady(lastAttackMs, intervalMs);
            Assert.IsFalse(ready,
                "同帧攻击冷却时间戳不变：550-550=0 < 80ms，应判定未冷却。");
        }

        [Test]
        [Description("相邻帧的冷却判断使用新 FrameNowMs：上一帧 550 攻击，下一帧 650 时 650-550=100 >= 80，已冷却。")]
        public void AttackCooldown_AdvancesAcrossFrames()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(550);
            long lastAttackMs = sim.FrameNowMs;
            const long intervalMs = 80;

            sim.Advance(650);
            Assert.AreEqual(650, sim.ActionScheduler.FrameNowMs,
                "下一帧调度器 FrameNowMs 应为 650。");

            bool ready = sim.ActionScheduler.IsAttackCooldownReady(lastAttackMs, intervalMs);
            Assert.IsTrue(ready,
                "跨帧冷却：650-550=100 >= 80ms，应判定已冷却。");
        }

        // ────────────────────────── 黄金 CanonicalFrames 对照 ──────────────────────────

        [Test]
        [Description("使用 GoldenBattleFixtures.CanonicalFrames 逐帧验证拆步与规则位移累计。"
            + " 覆盖 16/80/81/500/550/0ms 标准帧序列。")]
        public void CanonicalFrames_MatchSimulationBehavior()
        {
            foreach (var frame in GoldenBattleFixtures.CanonicalFrames)
            {
                BattleSimulation sim = CreateSimulation();

                if (frame.Paused)
                {
                    sim.Pause();
                }

                // CanonicalFrame.DeltaMs 表示相对前一帧的时间戳增量；首帧从 0 开始。
                // 这里以单帧方式验证：直接 Advance(frame.DeltaMs)。
                if (frame.DeltaMs > 0 || !frame.Paused)
                {
                    sim.Advance(frame.DeltaMs);
                }

                if (frame.Paused || frame.DeltaMs == 0)
                {
                    Assert.AreEqual(0, sim.ElapsedGameTimeMs,
                        $"帧 {frame.DeltaMs}ms（paused={frame.Paused}）不应推进规则位移。");
                }
                else
                {
                    Assert.AreEqual(frame.ExpectedLogicAdvanceMs, sim.ElapsedGameTimeMs,
                        $"帧 {frame.DeltaMs}ms 规则位移累计应为 {frame.ExpectedLogicAdvanceMs}ms。");

                    // 550ms 帧：FrameNowMs 必须是 550（未截断），不是 500。
                    Assert.AreEqual(frame.DeltaMs, sim.FrameNowMs,
                        $"帧 {frame.DeltaMs}ms 的 FrameNowMs 必须观察原始值（未截断）。");
                }
            }
        }

        // ────────────────────────── 冻结后同 phase 剩余对象不执行 ──────────────────────────

        [Test]
        [Description("首次 TryFreeze 成功后：当前 phase 回调内触发冻结，当前 phase 剩余迭代不再执行。"
            + " 由于阶段回调为单次调用，本测试通过在 Enemy 阶段触发冻结，验证后续阶段均不执行。"
            + " spec.md Two completion candidates would occur in one substep。")]
        public void Freeze_StopsRemainingPhasesInSubstep()
        {
            var recorder = new PhaseCallRecorder();
            var handlers = new Action<long, long, BattleUpdatePhase>[Enum.GetValues(typeof(BattleUpdatePhase)).Length];
            // sim 先声明再在 lambda 中捕获，保证 lambda 执行时引用非 null。
            BattleSimulation sim = null;
            for (int i = 0; i < handlers.Length; i++)
            {
                handlers[i] = (frameNow, step, phase) =>
                {
                    recorder.Invocations.Add((frameNow, step, phase));
                    // 在第一个阶段（DueActionsAndInput）触发冻结。
                    if (phase == BattleUpdatePhase.DueActionsAndInput)
                    {
                        sim.TryFreeze();
                    }
                };
            }

            sim = new BattleSimulation(
                handlers,
                tryFreezeHandler: () => true);

            sim.Advance(80);

            // 第一个阶段执行后触发冻结，后续 6 个阶段均不应执行。
            Assert.AreEqual(1, recorder.Invocations.Count,
                "冻结后当前子步后续 phase 不应执行。");
            Assert.AreEqual(BattleUpdatePhase.DueActionsAndInput, recorder.Invocations[0].Phase,
                "唯一执行的阶段应为 DueActionsAndInput。");
            Assert.IsTrue(sim.IsFrozen, "模拟器应处于冻结状态。");
        }

        // ────────────────────────── 冻结后后续 phase 不执行（逐阶段验证） ──────────────────────────

        [Test]
        [Description("在 Enemy 阶段触发冻结：Enemy 执行后，Projectile/AttackRelease/WaveSpawn/UnitAttack/AttackEffect 均不执行。")]
        public void Freeze_InEnemyPhase_StopsAllLaterPhases()
        {
            var recorder = new PhaseCallRecorder();
            var handlers = new Action<long, long, BattleUpdatePhase>[Enum.GetValues(typeof(BattleUpdatePhase)).Length];
            BattleSimulation sim = null;
            bool shouldFreeze = false;
            for (int i = 0; i < handlers.Length; i++)
            {
                handlers[i] = (frameNow, step, phase) =>
                {
                    recorder.Invocations.Add((frameNow, step, phase));
                    if (phase == BattleUpdatePhase.Enemy)
                    {
                        shouldFreeze = true;
                        sim.TryFreeze();
                    }
                };
            }

            sim = new BattleSimulation(handlers, tryFreezeHandler: () => shouldFreeze);
            sim.Advance(80);

            Assert.AreEqual(2, recorder.Invocations.Count,
                "DueActionsAndInput + Enemy 两个阶段执行后冻结，后续阶段不应执行。");
            Assert.AreEqual(BattleUpdatePhase.DueActionsAndInput, recorder.Invocations[0].Phase);
            Assert.AreEqual(BattleUpdatePhase.Enemy, recorder.Invocations[1].Phase);
        }

        // ────────────────────────── 冻结后剩余子步不执行 ──────────────────────────

        [Test]
        [Description("首子步触发冻结后，当前帧剩余子步不执行。"
            + " 使用 550ms 帧（7 子步），在第 1 子步冻结，验证仅 1 子步执行。"
            + " spec.md Battle result is frozen once / Freeze occurs inside a manager update。")]
        public void Freeze_InFirstSubstep_StopsRemainingSubsteps()
        {
            var recorder = new PhaseCallRecorder();
            var handlers = new Action<long, long, BattleUpdatePhase>[Enum.GetValues(typeof(BattleUpdatePhase)).Length];
            BattleSimulation sim = null;
            bool shouldFreeze = false;
            // 在 Enemy 阶段首次进入时触发冻结。
            handlers[(int)BattleUpdatePhase.Enemy] = (frameNow, step, phase) =>
            {
                recorder.Invocations.Add((frameNow, step, phase));
                shouldFreeze = true;
                sim.TryFreeze();
            };

            sim = new BattleSimulation(handlers, tryFreezeHandler: () => shouldFreeze);

            sim.Advance(550);

            // 仅第 1 子步的 Enemy 阶段执行；剩余 6 子步不应执行。
            Assert.AreEqual(1, recorder.Invocations.Count,
                "首子步冻结后，剩余 6 子步的 Enemy 阶段不应执行。");
            Assert.AreEqual(550, recorder.Invocations[0].FrameNowMs,
                "唯一执行的子步应观察 FrameNowMs=550。");
        }

        // ────────────────────────── 冻结后 ElapsedGameTime 不再推进 ──────────────────────────

        [Test]
        [Description("首子步冻结后，ElapsedGameTimeMs 只累计首子步，剩余子步不累计。"
            + " 550ms 帧首子步 80ms 冻结，ElapsedGameTimeMs 应为 80 而非 500。")]
        public void Freeze_StopsElapsedTimeAccumulation()
        {
            var handlers = new Action<long, long, BattleUpdatePhase>[Enum.GetValues(typeof(BattleUpdatePhase)).Length];
            BattleSimulation sim = null;
            handlers[(int)BattleUpdatePhase.Enemy] = (frameNow, step, phase) =>
            {
                sim.TryFreeze();
            };

            sim = new BattleSimulation(handlers, tryFreezeHandler: () => true);
            sim.Advance(550);

            Assert.AreEqual(80, sim.ElapsedGameTimeMs,
                "首子步冻结后规则位移只累计 80ms，剩余子步不应推进。");
            Assert.IsTrue(sim.IsFrozen, "模拟器应已冻结。");
        }

        // ────────────────────────── 冻结后再 Advance 直接返回 ──────────────────────────

        [Test]
        [Description("冻结后再次调用 Advance 直接返回，不推进任何子步。"
            + " spec.md Module receives updates while settling：TEngine 全局更新继续，但 Simulation 不推进。")]
        public void Advance_AfterFreeze_NoProgress()
        {
            var handlers = new Action<long, long, BattleUpdatePhase>[Enum.GetValues(typeof(BattleUpdatePhase)).Length];
            BattleSimulation sim = null;
            handlers[(int)BattleUpdatePhase.DueActionsAndInput] = (f, s, p) => sim.TryFreeze();
            sim = new BattleSimulation(handlers, tryFreezeHandler: () => true);

            sim.Advance(80);
            Assert.IsTrue(sim.IsFrozen, "前置：模拟器应已冻结。");
            long elapsed = sim.ElapsedGameTimeMs;

            sim.Advance(200);
            Assert.AreEqual(elapsed, sim.ElapsedGameTimeMs,
                "冻结后 Advance 不应推进规则位移。");
        }

        // ────────────────────────── 暂停后不推进 ──────────────────────────

        [Test]
        [Description("暂停后 Advance 不推进任何子步；恢复后从下一次有效 elapseSeconds 继续。"
            + " spec.md Simulation is reproducible / 暂停语义。")]
        public void Pause_StopsAdvance_ResumeContinues()
        {
            BattleSimulation sim = CreateSimulation();
            sim.Advance(80);
            Assert.AreEqual(80, sim.ElapsedGameTimeMs, "前置推进 80ms。");

            sim.Pause();
            sim.Advance(160);
            Assert.AreEqual(80, sim.ElapsedGameTimeMs,
                "暂停期间 Advance 不应推进规则位移。");

            sim.Resume();
            sim.Advance(160);
            // 恢复后从下一次有效 elapseSeconds 继续：LastTimerMs=80，frameNow=160，remaining=80。
            Assert.AreEqual(160, sim.ElapsedGameTimeMs,
                "恢复后应从下一次有效帧时间继续推进。");
        }

        // ────────────────────────── Reset 清空状态 ──────────────────────────

        [Test]
        [Description("Reset 清空所有时钟与冻结/暂停状态，供新局复用实例。")]
        public void Reset_ClearsAllState()
        {
            var handlers = new Action<long, long, BattleUpdatePhase>[Enum.GetValues(typeof(BattleUpdatePhase)).Length];
            BattleSimulation sim = null;
            handlers[(int)BattleUpdatePhase.DueActionsAndInput] = (f, s, p) => sim.TryFreeze();
            sim = new BattleSimulation(handlers, tryFreezeHandler: () => true);

            sim.Advance(550);
            Assert.IsTrue(sim.IsFrozen, "前置冻结。");

            sim.Reset();
            Assert.AreEqual(0, sim.FrameNowMs, "Reset 后 FrameNowMs 应清零。");
            Assert.AreEqual(0, sim.ElapsedGameTimeMs, "Reset 后 ElapsedGameTimeMs 应清零。");
            Assert.IsFalse(sim.IsFrozen, "Reset 后 IsFrozen 应为 false。");
            Assert.IsFalse(sim.IsPaused, "Reset 后 IsPaused 应为 false。");
        }
    }
}
