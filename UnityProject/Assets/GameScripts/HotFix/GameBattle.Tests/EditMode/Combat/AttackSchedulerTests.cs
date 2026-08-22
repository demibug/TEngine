using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // 任务 5.2 返工：AttackScheduler 单元测试 —— 完整状态机覆盖
    // ----------------------------------------------------------------------------
    // 验证要求（返工校验）：
    //   现有 BattleSimulationTimeTests.cs 只测试 BattleActionScheduler.IsAttackCooldownReady
    //   原语，未测试 AttackScheduler.Update/ScheduleUnitAttack 完整状态机。本测试补充覆盖：
    //     1. 守卫跳过（null/非活动/禁用/回池单位不触发攻击）
    //     2. 非 Attack 态 + 有目标 + 冷却完毕 → 切换 Attack 态，本子步不攻击
    //     3. 非 Attack 态 + 无目标 → 不切换
    //     4. Attack 态 + 冷却未完毕 → 不攻击
    //     5. Attack 态 + 冷却完毕 + 有目标 → 触发一次 Attack()，写回 LastAttackTimeMs=FrameNowMs
    //     6. Attack 态 + 冷却完毕 + 无目标 → 切换 Idle，不攻击
    //     7. Attack 态 + disabled/inPool → 切换 Idle
    //     8. 冻结（IsFrozen=true）→ Update 返回 0，不遍历
    //     9. 遍历中冻结 → 停止剩余单位
    //    10. 同帧多次子步 → FrameNowMs 不变，冷却判断一致，同单位同帧不重复攻击
    //    11. 每子步每单位只调用一次 Attack（计数器验证）
    //
    // 来源证据：
    //   - AttackScheduler.cs:343-381 Update：冻结守卫 + 遍历中冻结中止。
    //   - AttackScheduler.cs:443-538 ScheduleUnitAttack：守卫 → 非 Attack 态切态 →
    //     Attack 态禁用/回池切 Idle → 冷却检查 → 写回 LastAttackTimeMs → 查目标 → Attack()。
    //   - BattleActionScheduler.cs:84-87 IsAttackCooldownReady：FrameNowMs - last >= interval。
    //   - JS 黄金源 AttackScheduler.js:13-41 update(unit, opts) 行为对照。
    //
    // 测试策略：
    //   使用 FakeUnit（实现 IAttackUnit）、真实 BattleActionScheduler、真实 AttackResolver、
    //   真实 EnemyManager（注册 FakeEnemy），构造 AttackScheduler 并调用 Update 验证状态机。
    //   不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// AttackScheduler 完整状态机单元测试（task 5.2 返工）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖 AttackScheduler.Update / ScheduleUnitAttack 的全部状态机分支：
    /// 守卫跳过、非 Attack 态切换、冷却判断、Attack 触发与写回、无目标切 Idle、
    /// 禁用/回池切 Idle、冻结守卫、遍历中冻结中止、同帧时间戳不变、每子步每单位只触发一次。</para>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class AttackSchedulerTests
    {
        // ====================================================================
        // FakeUnit —— 最小 IAttackUnit 测试替身
        // ====================================================================

        /// <summary>
        /// 最小 IAttackUnit 测试替身，带可观察的可变状态。
        /// </summary>
        /// <remarks>
        /// <para>实现 <see cref="IAttackUnit"/> 全部成员，提供可设置的属性与可观察的
        /// Attack 调用计数、SetState 调用记录，用于验证 AttackScheduler 状态机行为。</para>
        /// </remarks>
        private sealed class FakeUnit : IAttackUnit
        {
            public bool IsActive { get; set; } = true;
            public bool Disabled { get; set; }
            public bool InPool { get; set; }
            public bool Side { get; set; } = true;
            public float CenterX { get; set; } = 40f;
            public float CenterY { get; set; } = 40f;
            public float AttackRange { get; set; } = 50f;
            public float AttackIntervalSeconds { get; set; } = 1f;
            public long LastAttackTimeMs { get; set; }
            public AttackUnitState CurrentState { get; private set; } = AttackUnitState.Idle;

            /// <summary>Attack() 调用次数（验证每子步每单位只触发一次攻击）。</summary>
            public int AttackCount;

            /// <summary>SetState 调用记录（验证状态切换）。</summary>
            public readonly List<AttackUnitState> StateChanges = new List<AttackUnitState>();

            public void SetState(AttackUnitState state)
            {
                CurrentState = state;
                StateChanges.Add(state);
            }

            public void Attack(EnemyTargetDto initialTarget)
            {
                AttackCount++;
                // 记录调度器传入的初始目标，供验证初始目标单次传递。
                LastAttackTargetId = initialTarget.Id;
            }

            /// <summary>最近一次 Attack 收到的初始目标 ID（验证初始目标单次传递）。</summary>
            public int LastAttackTargetId;

            /// <summary>重置可观察状态（复用同一实例做多场景测试）。</summary>
            public void Reset()
            {
                IsActive = true;
                Disabled = false;
                InPool = false;
                Side = true;
                CenterX = 40f;
                CenterY = 40f;
                AttackRange = 50f;
                AttackIntervalSeconds = 1f;
                LastAttackTimeMs = 0;
                AttackCount = 0;
                LastAttackTargetId = 0;
                StateChanges.Clear();
                // 回到 Idle 态
                if (CurrentState != AttackUnitState.Idle)
                {
                    CurrentState = AttackUnitState.Idle;
                    StateChanges.Add(AttackUnitState.Idle);
                }
            }

            /// <summary>直接设置当前状态（不记入 StateChanges，用于准备 Attack 态前置条件）。</summary>
            public void ForceState(AttackUnitState state)
            {
                CurrentState = state;
                StateChanges.Clear();
            }
        }

        // ====================================================================
        // FakeEnemy —— 最小 IEnemyEntity 测试替身（与 AttackResolverTests 一致）
        // ====================================================================

        /// <summary>
        /// 最小 IEnemyEntity 测试替身，带可观察的可变状态。
        /// </summary>
        private sealed class FakeEnemy : IEnemyEntity
        {
            public int Id { get; set; }
            public bool IsPlayerLane { get; set; }
            public int CurrentState { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; } = 40f;
            public float Height { get; set; } = 40f;
            public float ProjectileAimOffsetX => 0f;
            public float ProjectileAimOffsetY => 0f;
            public float RemainingPathDistance { get; set; } = float.PositiveInfinity;
            public int CurrentPathIndex { get; set; }
            public int Health { get; set; }
            public int MaxHealth { get; set; }
            public bool Targetable { get; set; } = true;

            public void Update(long deltaMs)
            {
                // AttackScheduler 不调用 Update，此处无需实现。
            }

            public bool Hit(int damage, int attackerId)
            {
                if (Health <= 0 || damage <= 0)
                {
                    return false;
                }

                Health = Math.Max(0, Health - damage);
                if (Health <= 0)
                {
                    CurrentState = 4; // DEAD
                }
                return true;
            }

            public bool GameOver()
            {
                CurrentState = 4; // DEAD
                Targetable = false;
                return true;
            }

            public bool IsTargetableBy(bool playerSide)
            {
                return CurrentState != 0 && CurrentState != 4
                    && Targetable && IsPlayerLane == playerSide;
            }
        }

        // ====================================================================
        // 测试常量与辅助
        // ====================================================================

        /// <summary>默认空间单元边长（与 EnemyManager.DefaultGridSize 一致）。</summary>
        private const int GridSize = 80;

        /// <summary>敌人格子宽/高（对应 map.gridWidth/gridHeight）。</summary>
        private const float CellWidth = 80f;
        private const float CellHeight = 80f;

        /// <summary>
        /// 构造一个 FakeEnemy，位于指定位置。
        /// </summary>
        private static FakeEnemy MakeEnemy(
            int id, bool isPlayerLane, float x, float y,
            int health = 100, int state = 1)
        {
            return new FakeEnemy
            {
                Id = id,
                IsPlayerLane = isPlayerLane,
                X = x,
                Y = y,
                Health = health,
                CurrentState = state,
            };
        }

        /// <summary>
        /// 构造已登记一个敌人的 EnemyManager，供攻击调度测试使用。
        /// <para>敌人在玩家方车道（isPlayerLane=true），位于 (40,40)，
        /// 玩家方单位（Side=true）查询对手方敌人时 IsTargetableBy 匹配。</para>
        /// </summary>
        private static EnemyManager MakeManagerWithEnemy(out FakeEnemy enemy)
        {
            var mgr = new EnemyManager(GridSize);
            enemy = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            mgr.Register(enemy);
            return mgr;
        }

        /// <summary>
        /// 构造测试用 AttackScheduler，注入真实 BattleActionScheduler、AttackResolver 与格子尺寸。
        /// </summary>
        /// <param name="actionScheduler">冷却时钟源（非 null）。</param>
        /// <returns>AttackScheduler 实例。</returns>
        private static AttackScheduler CreateScheduler(BattleActionScheduler actionScheduler)
        {
            return new AttackScheduler(actionScheduler, new AttackResolver(), CellWidth, CellHeight);
        }

        // ====================================================================
        // 场景 1：守卫跳过（null/非活动/禁用/回池单位不触发攻击）
        // ====================================================================

        [Test]
        [Description("null 单位不触发攻击，Update 返回 0。")]
        public void Guard_NullUnit_SkipsAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            // 列表包含 null 单位。
            var units = new List<IAttackUnit> { null };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "null 单位不应触发攻击");
        }

        [Test]
        [Description("非活动单位（IsActive=false）不触发攻击。")]
        public void Guard_InactiveUnit_SkipsAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit { IsActive = false };
            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "非活动单位不应触发攻击");
            Assert.AreEqual(0, unit.AttackCount, "非活动单位不应调用 Attack()");
        }

        [Test]
        [Description("禁用单位（Disabled=true）不触发攻击。")]
        public void Guard_DisabledUnit_SkipsAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit { Disabled = true };
            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "禁用单位不应触发攻击");
            Assert.AreEqual(0, unit.AttackCount, "禁用单位不应调用 Attack()");
        }

        [Test]
        [Description("回池单位（InPool=true）不触发攻击。")]
        public void Guard_InPoolUnit_SkipsAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit { InPool = true };
            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "回池单位不应触发攻击");
            Assert.AreEqual(0, unit.AttackCount, "回池单位不应调用 Attack()");
        }

        // ====================================================================
        // 场景 2：非 Attack 态 + 有目标 + 冷却完毕 → 切换 Attack 态，本子步不攻击
        // ====================================================================

        [Test]
        [Description("非 Attack 态 + 有目标 + 冷却完毕 → 切换 Attack 态，本子步不攻击。")]
        public void NonAttackState_HasTarget_CooldownReady_SwitchesToAttack_NoAttackThisSubstep()
        {
            var actionScheduler = new BattleActionScheduler();
            // 设帧时间戳 1000ms，单位上次攻击时间 0，间隔 1s=1000ms → 冷却完毕。
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                CenterX = 40f,
                CenterY = 40f,
                AttackRange = 50f,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            // 确保处于 Idle 态。
            unit.ForceState(AttackUnitState.Idle);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            // 非 Attack 态本子步不攻击。
            Assert.AreEqual(0, count, "非 Attack 态本子步不应攻击");
            Assert.AreEqual(0, unit.AttackCount, "非 Attack 态不应调用 Attack()");
            // 有目标且冷却完毕 → 切换到 Attack 态。
            Assert.AreEqual(1, unit.StateChanges.Count, "应切换一次状态");
            Assert.AreEqual(AttackUnitState.Attack, unit.StateChanges[0], "应切换到 Attack 态");
            Assert.AreEqual(AttackUnitState.Attack, unit.CurrentState, "当前态应为 Attack");
        }

        // ====================================================================
        // 场景 3：非 Attack 态 + 无目标 → 不切换
        // ====================================================================

        [Test]
        [Description("非 Attack 态 + 无目标 → 不切换状态。")]
        public void NonAttackState_NoTarget_DoesNotSwitchState()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            // 空 EnemyManager → 无目标。
            var mgr = new EnemyManager(GridSize);

            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Idle);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "无目标不应攻击");
            Assert.AreEqual(0, unit.AttackCount, "无目标不应调用 Attack()");
            Assert.AreEqual(0, unit.StateChanges.Count, "无目标不应切换状态");
            Assert.AreEqual(AttackUnitState.Idle, unit.CurrentState, "应保持 Idle 态");
        }

        // ====================================================================
        // 场景 4：Attack 态 + 冷却未完毕 → 不攻击
        // ====================================================================

        [Test]
        [Description("Attack 态 + 冷却未完毕 → 不攻击。")]
        public void AttackState_CooldownNotReady_DoesNotAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            // 帧时间戳 500ms，上次攻击 0，间隔 1000ms → 500 < 1000 冷却未完毕。
            actionScheduler.BeginFrame(500);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "冷却未完毕不应攻击");
            Assert.AreEqual(0, unit.AttackCount, "冷却未完毕不应调用 Attack()");
            // 冷却未完毕不切换状态，保持 Attack 态。
            Assert.AreEqual(0, unit.StateChanges.Count, "冷却未完毕不应切换状态");
            Assert.AreEqual(AttackUnitState.Attack, unit.CurrentState, "应保持 Attack 态");
            // 冷却未完毕不写回 LastAttackTimeMs。
            Assert.AreEqual(0, unit.LastAttackTimeMs, "冷却未完毕不应写回 LastAttackTimeMs");
        }

        // ====================================================================
        // 场景 5：Attack 态 + 冷却完毕 + 有目标 → 触发一次 Attack()，写回 LastAttackTimeMs=FrameNowMs
        // ====================================================================

        [Test]
        [Description("Attack 态 + 冷却完毕 + 有目标 → 触发一次 Attack()，写回 LastAttackTimeMs=FrameNowMs。")]
        public void AttackState_CooldownReady_HasTarget_AttacksOnce_WritesBackLastAttackTimeMs()
        {
            var actionScheduler = new BattleActionScheduler();
            // 帧时间戳 1000ms，上次攻击 0，间隔 1000ms → 冷却完毕。
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                CenterX = 40f,
                CenterY = 40f,
                AttackRange = 50f,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(1, count, "应触发一次攻击");
            Assert.AreEqual(1, unit.AttackCount, "Attack() 应只调用一次");
            // 写回 LastAttackTimeMs = FrameNowMs = 1000。
            Assert.AreEqual(1000, unit.LastAttackTimeMs, "应写回 LastAttackTimeMs=FrameNowMs(1000)");
            // 有目标时不切换状态，保持 Attack 态。
            Assert.AreEqual(0, unit.StateChanges.Count, "有目标攻击不应切换状态");
            // design 决策 2：调度器把选定的第一个目标作为本次攻击初始目标传入单位。
            Assert.AreEqual(1, unit.LastAttackTargetId, "应把第一个目标作为初始目标传入 Attack");
        }

        // ====================================================================
        // 场景 6：Attack 态 + 冷却完毕 + 无目标 → 切换 Idle，不攻击
        // ====================================================================

        [Test]
        [Description("Attack 态 + 冷却完毕 + 无目标 → 切换 Idle，不攻击。")]
        public void AttackState_CooldownReady_NoTarget_SwitchesIdle_NoAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            // 空 EnemyManager → 无目标。
            var mgr = new EnemyManager(GridSize);

            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "无目标不应攻击");
            Assert.AreEqual(0, unit.AttackCount, "无目标不应调用 Attack()");
            // 无目标 → 切换 Idle。
            Assert.AreEqual(1, unit.StateChanges.Count, "应切换一次状态");
            Assert.AreEqual(AttackUnitState.Idle, unit.StateChanges[0], "应切换到 Idle 态");
            Assert.AreEqual(AttackUnitState.Idle, unit.CurrentState, "当前态应为 Idle");
            // 冷却完毕时写回 LastAttackTimeMs 发生在查目标之前（AttackScheduler.cs:514），
            // 即使最终无目标也已写回（对应 JS 第 31 行 unit.lastAttackTime = currentTime 先于目标查询）。
            Assert.AreEqual(1000, unit.LastAttackTimeMs, "冷却完毕应已写回 LastAttackTimeMs");
        }

        // ====================================================================
        // 场景 7：Attack 态 + disabled/inPool → 切换 Idle
        // ====================================================================

        [Test]
        [Description("Attack 态 + Disabled=true → 切换 Idle，不攻击。")]
        public void AttackState_Disabled_SwitchesIdle_NoAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit
            {
                IsActive = true,
                Disabled = true,
                Side = true,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "禁用单位不应攻击");
            Assert.AreEqual(0, unit.AttackCount, "禁用单位不应调用 Attack()");
            Assert.AreEqual(1, unit.StateChanges.Count, "应切换一次状态");
            Assert.AreEqual(AttackUnitState.Idle, unit.StateChanges[0], "应切换到 Idle 态");
        }

        [Test]
        [Description("Attack 态 + InPool=true → 切换 Idle，不攻击。")]
        public void AttackState_InPool_SwitchesIdle_NoAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit
            {
                IsActive = true,
                InPool = true,
                Side = true,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "回池单位不应攻击");
            Assert.AreEqual(0, unit.AttackCount, "回池单位不应调用 Attack()");
            Assert.AreEqual(1, unit.StateChanges.Count, "应切换一次状态");
            Assert.AreEqual(AttackUnitState.Idle, unit.StateChanges[0], "应切换到 Idle 态");
        }

        // ====================================================================
        // 场景 8：冻结（IsFrozen=true）→ Update 返回 0，不遍历
        // ====================================================================

        [Test]
        [Description("冻结（IsFrozen=true）→ Update 返回 0，不遍历任何单位。")]
        public void Frozen_Update_ReturnsZero_NoIteration()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            // 冻结调度器。
            actionScheduler.Freeze();
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(0, count, "冻结时 Update 应返回 0");
            Assert.AreEqual(0, unit.AttackCount, "冻结时不应调用 Attack()");
            Assert.AreEqual(0, unit.StateChanges.Count, "冻结时不应切换状态");
            // 冻结时不应写回 LastAttackTimeMs。
            Assert.AreEqual(0, unit.LastAttackTimeMs, "冻结时不应写回 LastAttackTimeMs");
        }

        // ====================================================================
        // 场景 9：遍历中冻结 → 停止剩余单位
        // ====================================================================

        [Test]
        [Description("遍历中冻结 → 停止剩余单位迭代，已触发的攻击正常生效。")]
        public void FreezeDuringIteration_StopsRemainingUnits()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            // 第一个单位：Attack 态 + 冷却完毕 + 有目标 → 触发攻击。
            // 在 Attack() 内触发冻结（模拟攻击导致目标死亡 → TryFreeze）。
            var unit1 = new FreezeOnAttackUnit(actionScheduler)
            {
                IsActive = true,
                Side = true,
                CenterX = 40f,
                CenterY = 40f,
                AttackRange = 50f,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit1.ForceState(AttackUnitState.Attack);

            // 第二个单位：Attack 态 + 冷却完毕 + 有目标，正常情况下会攻击。
            // 但因 unit1 攻击后冻结，unit2 不应被遍历。
            var unit2 = new FakeUnit
            {
                IsActive = true,
                Side = true,
                CenterX = 40f,
                CenterY = 40f,
                AttackRange = 50f,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit2.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit1, unit2 };
            int count = scheduler.Update(units, mgr);

            // unit1 触发了攻击。
            Assert.AreEqual(1, count, "只有 unit1 应触发攻击");
            Assert.AreEqual(1, unit1.AttackCount, "unit1 应攻击一次");
            // unit2 不应被遍历。
            Assert.AreEqual(0, unit2.AttackCount, "冻结后 unit2 不应被遍历或攻击");
            Assert.AreEqual(0, unit2.StateChanges.Count, "冻结后 unit2 不应切换状态");
            Assert.IsTrue(actionScheduler.IsFrozen, "调度器应已冻结");
        }

        // ====================================================================
        // 场景 10：同帧多次子步 → FrameNowMs 不变，冷却判断一致，同单位同帧不重复攻击
        // ====================================================================

        [Test]
        [Description("同帧多次子步 → FrameNowMs 不变，冷却判断一致，同单位同帧不重复攻击。")]
        public void SameFrame_MultipleSubsteps_FrameNowMsUnchanged_NoRepeatAttack()
        {
            var actionScheduler = new BattleActionScheduler();
            // 帧时间戳 1000ms，同一帧所有子步共享此值。
            actionScheduler.BeginFrame(1000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                CenterX = 40f,
                CenterY = 40f,
                AttackRange = 50f,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };

            // 第一次子步：冷却完毕（1000-0>=1000）→ 触发攻击，写回 LastAttackTimeMs=1000。
            int count1 = scheduler.Update(units, mgr);
            Assert.AreEqual(1, count1, "第一次子步应攻击");
            Assert.AreEqual(1, unit.AttackCount, "第一次子步 Attack() 应调用一次");
            Assert.AreEqual(1000, unit.LastAttackTimeMs, "应写回 LastAttackTimeMs=1000");

            // 第二次子步（同帧，FrameNowMs 仍为 1000）：
            // 冷却判断 1000-1000=0 < 1000 → 冷却未完毕 → 不攻击。
            // 这是"同帧固定 frameNowMs 判断冷却"的核心：写回后同帧后续子步冷却拒绝。
            int count2 = scheduler.Update(units, mgr);
            Assert.AreEqual(0, count2, "同帧第二次子步不应攻击（冷却拒绝）");
            Assert.AreEqual(1, unit.AttackCount, "同帧第二次子步不应增加 Attack() 次数");

            // 第三次子步（同帧，FrameNowMs 仍为 1000）：冷却仍拒绝。
            int count3 = scheduler.Update(units, mgr);
            Assert.AreEqual(0, count3, "同帧第三次子步仍不应攻击");
            Assert.AreEqual(1, unit.AttackCount, "同帧第三次子步 Attack() 次数不变");

            // FrameNowMs 在整个同帧期间不变。
            Assert.AreEqual(1000, actionScheduler.FrameNowMs, "FrameNowMs 应保持 1000 不变");
        }

        // ====================================================================
        // 场景 11：每子步每单位只调用一次 Attack（计数器验证）
        // ====================================================================

        [Test]
        [Description("每子步每单位只调用一次 Attack：多单位多子步，每单位每子步 Attack() 最多一次。")]
        public void EachSubstep_EachUnit_AttackAtMostOnce()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(2000);
            AttackScheduler scheduler = CreateScheduler(actionScheduler);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            // 三个单位均在 Attack 态、冷却完毕、有目标。
            var unit1 = new FakeUnit
            {
                IsActive = true, Side = true,
                CenterX = 40f, CenterY = 40f, AttackRange = 50f,
                AttackIntervalSeconds = 1f, LastAttackTimeMs = 0,
            };
            var unit2 = new FakeUnit
            {
                IsActive = true, Side = true,
                CenterX = 40f, CenterY = 40f, AttackRange = 50f,
                AttackIntervalSeconds = 1f, LastAttackTimeMs = 0,
            };
            var unit3 = new FakeUnit
            {
                IsActive = true, Side = true,
                CenterX = 40f, CenterY = 40f, AttackRange = 50f,
                AttackIntervalSeconds = 1f, LastAttackTimeMs = 0,
            };
            unit1.ForceState(AttackUnitState.Attack);
            unit2.ForceState(AttackUnitState.Attack);
            unit3.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit1, unit2, unit3 };

            // 一个子步：三个单位各攻击一次。
            int count = scheduler.Update(units, mgr);
            Assert.AreEqual(3, count, "三个单位应各攻击一次，共 3 次");
            Assert.AreEqual(1, unit1.AttackCount, "unit1 应只攻击一次");
            Assert.AreEqual(1, unit2.AttackCount, "unit2 应只攻击一次");
            Assert.AreEqual(1, unit3.AttackCount, "unit3 应只攻击一次");

            // 同帧第二个子步：冷却拒绝（2000-2000=0 < 1000），不攻击。
            int count2 = scheduler.Update(units, mgr);
            Assert.AreEqual(0, count2, "同帧第二个子步冷却拒绝，不攻击");
            Assert.AreEqual(1, unit1.AttackCount, "unit1 第二个子步不应增加攻击次数");
            Assert.AreEqual(1, unit2.AttackCount, "unit2 第二个子步不应增加攻击次数");
            Assert.AreEqual(1, unit3.AttackCount, "unit3 第二个子步不应增加攻击次数");
        }

        // ====================================================================
        // 辅助：攻击时触发冻结的 FakeUnit 变体（场景 9 专用）
        // ====================================================================

        /// <summary>
        /// 在 Attack() 内调用 <see cref="BattleActionScheduler.Freeze"/> 的 FakeUnit 变体。
        /// <para>用于模拟攻击导致目标死亡 → TryFreeze → 遍历中冻结中止场景。</para>
        /// </summary>
        private sealed class FreezeOnAttackUnit : IAttackUnit
        {
            private readonly BattleActionScheduler _actionScheduler;

            public FreezeOnAttackUnit(BattleActionScheduler actionScheduler)
            {
                _actionScheduler = actionScheduler;
            }

            public bool IsActive { get; set; } = true;
            public bool Disabled { get; set; }
            public bool InPool { get; set; }
            public bool Side { get; set; } = true;
            public float CenterX { get; set; } = 40f;
            public float CenterY { get; set; } = 40f;
            public float AttackRange { get; set; } = 50f;
            public float AttackIntervalSeconds { get; set; } = 1f;
            public long LastAttackTimeMs { get; set; }
            public AttackUnitState CurrentState { get; private set; } = AttackUnitState.Idle;

            public int AttackCount;
            public readonly List<AttackUnitState> StateChanges = new List<AttackUnitState>();

            public void SetState(AttackUnitState state)
            {
                CurrentState = state;
                StateChanges.Add(state);
            }

            public void Attack(EnemyTargetDto initialTarget)
            {
                AttackCount++;
                // 模拟攻击导致目标死亡 → TryFreeze 成功。
                _ = initialTarget;
                _actionScheduler.Freeze();
            }

            public void ForceState(AttackUnitState state)
            {
                CurrentState = state;
                StateChanges.Clear();
            }
        }

        // ====================================================================
        // 场景 12：普通兵在有 GeneralSkillRuntime 的 AttackScheduler 中行为不变
        // ====================================================================

        /// <summary>
        /// 构造带 GeneralSkillRuntime 的 AttackScheduler（skillRuntime 非 null）。
        /// </summary>
        private static AttackScheduler CreateSchedulerWithSkillRuntime(
            BattleActionScheduler actionScheduler, GeneralSkillRuntime skillRuntime)
        {
            return new AttackScheduler(actionScheduler, new AttackResolver(), CellWidth, CellHeight, skillRuntime);
        }

        [Test]
        [Description("普通兵（FakeUnit，非 SoldierBase）在有 GeneralSkillRuntime 的 AttackScheduler 中行为不变。")]
        public void NormalUnit_WithSkillRuntime_BehaviorUnchanged()
        {
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            // 构造一个非 null 但无绑定的 GeneralSkillRuntime（不影响 FakeUnit）。
            var skillCatalog = new SkillCatalogSnapshot(new[]
            {
                new SkillDefinitionSnapshot("AlphaStrike", SkillCategory.Active, 0, "AlphaStrike", null, null,
                    rangeTiles: null, triggerAttackCount: 3),
            });
            var skillRegistry = new SkillHandlerRegistry();
            var skillRunner = new SkillRunner(skillCatalog, skillRegistry, actionScheduler);
            var skillRuntime = new GeneralSkillRuntime(skillRunner, skillCatalog);
            AttackScheduler scheduler = CreateSchedulerWithSkillRuntime(actionScheduler, skillRuntime);
            EnemyManager mgr = MakeManagerWithEnemy(out _);

            // FakeUnit 不是 SoldierBase，技能路径跳过，走普通 Attack。
            var unit = new FakeUnit
            {
                IsActive = true,
                Side = true,
                CenterX = 40f,
                CenterY = 40f,
                AttackRange = 50f,
                AttackIntervalSeconds = 1f,
                LastAttackTimeMs = 0,
            };
            unit.ForceState(AttackUnitState.Attack);

            var units = new List<IAttackUnit> { unit };
            int count = scheduler.Update(units, mgr);

            Assert.AreEqual(1, count, "普通兵应触发一次普通攻击");
            Assert.AreEqual(1, unit.AttackCount, "普通兵应调用 Attack() 一次");
        }

        // ====================================================================
        // 场景 13/14：武将技能成功替代普通 Attack / 失败继续普通 Attack 且累计
        // --------------------------------------------------------------------
        // 这两个测试需要真实 SoldierBase（AttackScheduler 通过 `is SoldierBase` 判断
        // 是否走技能路径），使用与 UnitFactoryAndRegistryTests 一致的真实基础设施。
        // ====================================================================

        /// <summary>技能替换攻击槽测试专用 fixture（真实 SoldierBase + SkillRuntime）。</summary>
        [Test]
        [Description("武将技能成功替代普通 Attack：AttackCount 达阈值后技能激活，跳过普通 Attack。")]
        public void SkillActivation_Success_ReplacesNormalAttack()
        {
            SkillAttackTestContext ctx = SkillAttackTestContext.Create(
                triggerAttackCount: 2, cooldownMs: 0);

            // 绑定武将并累计到阈值。
            ctx.Runtime.Bind(unitId: 100, ctx.Soldier, "AlphaStrike");
            ctx.Runtime.OnBasicAttack(ctx.Soldier);
            ctx.Runtime.OnBasicAttack(ctx.Soldier);

            ctx.ActionScheduler.BeginFrame(1000);

            // 武将处于 Attack 态、冷却完毕、有目标 → 先 TryActivate（成功）。
            int count = ctx.AttackScheduler.Update(
                new List<IAttackUnit> { ctx.Soldier }, ctx.EnemyManager);

            Assert.AreEqual(1, count, "应触发一次攻击槽（被技能消费）");
            // Flush 到期动作以执行 effect 回调。
            ctx.ActionScheduler.FlushDueActions(1);
            Assert.AreEqual(1, ctx.Handler.EffectCount, "技能 Effect 应调用一次");
            // 技能成功消费槽，不调用普通 Attack。
            // AttackCount 应已清零。
            ctx.Dispose();
        }

        [Test]
        [Description("武将技能失败（未达阈值）继续普通 Attack 且累计不清零。")]
        public void SkillActivation_Failure_ContinuesNormalAttack_AccumulatesNoClear()
        {
            SkillAttackTestContext ctx = SkillAttackTestContext.Create(
                triggerAttackCount: 5, cooldownMs: 0);

            // 绑定武将，累计但未达阈值。
            ctx.Runtime.Bind(unitId: 100, ctx.Soldier, "AlphaStrike");
            ctx.Runtime.OnBasicAttack(ctx.Soldier);
            ctx.Runtime.OnBasicAttack(ctx.Soldier);

            ctx.ActionScheduler.BeginFrame(1000);

            // 武将处于 Attack 态、冷却完毕、有目标 → TryActivate（失败）→ 普通 Attack + OnBasicAttack。
            int count = ctx.AttackScheduler.Update(
                new List<IAttackUnit> { ctx.Soldier }, ctx.EnemyManager);

            Assert.AreEqual(1, count, "应触发一次普通攻击");
            Assert.AreEqual(0, ctx.Handler.EffectCount, "技能不应激活");
            // 普通 Attack 后 OnBasicAttack 累计（从 2 到 3）。
            // 阈值 5，3 < 5 仍不可激活。
            bool canActivate = ctx.Runtime.TryActivateInsteadOfAttack(ctx.Soldier);
            Assert.IsFalse(canActivate, "累计 3 < 5 不应激活");

            ctx.Dispose();
        }

        /// <summary>
        /// 技能替换攻击槽测试上下文：持有真实 SoldierBase、SkillRuntime 和 AttackScheduler。
        /// </summary>
        private sealed class SkillAttackTestContext : IDisposable
        {
            private const float UnitWidth = 40f;
            private const float UnitHeight = 40f;
            private const float CellSize = 80f;
            private const int GridSize = 80;

            private readonly BattlePoolScope _poolScope;
            private readonly UnitFactory _factory;

            internal BattleActionScheduler ActionScheduler;
            internal GeneralSkillRuntime Runtime;
            internal AttackScheduler AttackScheduler;
            internal SoldierBase Soldier;
            internal EnemyManager EnemyManager;
            internal RecordingSkillHandler Handler;

            private SkillAttackTestContext(
                BattlePoolScope poolScope,
                UnitFactory factory)
            {
                _poolScope = poolScope;
                _factory = factory;
            }

            internal static SkillAttackTestContext Create(int triggerAttackCount, long cooldownMs)
            {
                var poolScope = new BattlePoolScope();
                var idAllocator = new RuntimeIdAllocator();
                var enemyManager = new EnemyManager(GridSize);
                var attackResolver = new AttackResolver();
                var attackEffectManager = new AttackEffectManager();
                var arrowPool = poolScope.GetPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
                var projectileFactory = new ProjectileFactory(
                    idAllocator, arrowPool, enemyManager, CellSize);
                var projectileManager = new ProjectileManager(projectileFactory);

                var factory = new UnitFactory(
                    idAllocator,
                    poolScope.GetPool<KnifeSoldier>(() => new KnifeSoldier()),
                    poolScope.GetPool<BowSoldier>(() => new BowSoldier()),
                    poolScope.GetPool<SpearSoldier>(() => new SpearSoldier()),
                    poolScope.GetPool<CavalrySoldier>(() => new CavalrySoldier()),
                    enemyManager, attackResolver, attackEffectManager,
                    projectileFactory, projectileManager,
                    CellSize, 1);

                var config = new UnitConfigSnapshot(0, "刀", "knife", 1.5f, 3, 0.8f, "单体", "nearest");

                // 创建武将技能运行时。
                var catalog = new SkillCatalogSnapshot(new[]
                {
                    new SkillDefinitionSnapshot("AlphaStrike", SkillCategory.Active, cooldownMs,
                        "AlphaStrike", null, null, rangeTiles: null, triggerAttackCount: triggerAttackCount),
                });
                var skillRegistry = new SkillHandlerRegistry();
                var handler = new RecordingSkillHandler();
                skillRegistry.Register("AlphaStrike", handler);

                var actionScheduler = new BattleActionScheduler();
                var skillRunner = new SkillRunner(catalog, skillRegistry, actionScheduler);
                var runtime = new GeneralSkillRuntime(skillRunner, catalog);
                var attackScheduler = new AttackScheduler(
                    actionScheduler, attackResolver, CellSize, CellSize, runtime);

                // 创建并激活刀兵。
                SoldierBase soldier = factory.Acquire(SoldierType.Knife, config, true, UnitWidth, UnitHeight);
                soldier.SetPlacement(0, 0);
                soldier.ActivatePlacement(0, 0);
                soldier.SetState(AttackUnitState.Attack);

                // 注册一个在玩家方车道、位于 (40,40) 的敌人供目标查询。
                var enemy = new TestEnemy
                {
                    Id = 1,
                    IsPlayerLane = true,
                    X = 40,
                    Y = 40,
                    Health = 100,
                    CurrentState = 1,
                };
                enemyManager.Register(enemy);

                return new SkillAttackTestContext(poolScope, factory)
                {
                    ActionScheduler = actionScheduler,
                    Runtime = runtime,
                    AttackScheduler = attackScheduler,
                    Soldier = soldier,
                    EnemyManager = enemyManager,
                    Handler = handler,
                };
            }

            public void Dispose()
            {
                _factory?.Release(Soldier);
            }
        }

        /// <summary>最小 IEnemyEntity 测试替身（与 FakeEnemy 一致，供技能替换测试使用）。</summary>
        private sealed class TestEnemy : IEnemyEntity
        {
            public int Id { get; set; }
            public bool IsPlayerLane { get; set; }
            public int CurrentState { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; } = 40f;
            public float Height { get; set; } = 40f;
            public float ProjectileAimOffsetX => 0f;
            public float ProjectileAimOffsetY => 0f;
            public float RemainingPathDistance { get; set; } = float.PositiveInfinity;
            public int CurrentPathIndex { get; set; }
            public int Health { get; set; }
            public int MaxHealth { get; set; }
            public bool Targetable { get; set; } = true;

            public void Update(long deltaMs) { }

            public bool Hit(int damage, int attackerId)
            {
                if (Health <= 0 || damage <= 0) return false;
                Health = Math.Max(0, Health - damage);
                if (Health <= 0) CurrentState = 4;
                return true;
            }

            public bool GameOver()
            {
                CurrentState = 4;
                Targetable = false;
                return true;
            }

            public bool IsTargetableBy(bool playerSide)
            {
                return CurrentState != 0 && CurrentState != 4
                    && Targetable && IsPlayerLane == playerSide;
            }
        }

        /// <summary>记录 ISkillHandler 调用的测试 handler（技能替换测试专用）。</summary>
        private sealed class RecordingSkillHandler : ISkillHandler
        {
            internal int EffectCount;
            internal int CompleteCount;
            internal int CancelCount;

            public void Effect(SkillActivationContext context)
            {
                EffectCount++;
            }

            public void Complete(SkillActivationContext context)
            {
                CompleteCount++;
            }

            public void Cancel(SkillActivationContext context, bool effectCommitted)
            {
                CancelCount++;
            }
        }
    }
}
