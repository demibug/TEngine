using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Presentation
{
    // ============================================================================
    // 任务 8.x：BattleViewSynchronizer 单位状态边沿复位测试
    // ----------------------------------------------------------------------------
    // 验证目标：
    //   1. 状态从 Attack → Idle 时，Synchronizer 恰好调用一次 ResetUnitPose。
    //   2. Idle 持续时不重复调用（不每帧复位待机动画到第 0 帧）。
    //   3. 首次同步不触发复位（回池/再次创建时 AcquireFromPool 已复位表现）。
    //   4. 攻击（Idle → Attack）不触发复位。
    // ============================================================================

    /// <summary>
    /// BattleViewSynchronizer 单位状态边沿复位测试。
    /// </summary>
    /// <remarks>
    /// 使用真实 BattleViewRegistry + 自定义 stub 实现 IViewReadModelProvider /
    /// IViewObjectSync，验证 Synchronizer 在状态变化边沿的行为。不依赖 Unity 场景。
    /// </remarks>
    [TestFixture]
    internal class BattleViewStateSyncTests
    {
        // ====================================================================
        // 测试桩
        // ====================================================================

        /// <summary>
        /// 可控单位状态只读提供者。
        /// </summary>
        private sealed class StubReadModelProvider : IViewReadModelProvider
        {
            internal readonly Dictionary<int, AttackUnitState> UnitStates =
                new Dictionary<int, AttackUnitState>();

            internal bool HasUnitState = true;

            public bool TryGetUnitState(int runtimeId, out AttackUnitState state)
            {
                state = AttackUnitState.Idle;
                if (!HasUnitState)
                {
                    return false;
                }

                return UnitStates.TryGetValue(runtimeId, out state);
            }

            public bool TryGetEnemyPosition(int runtimeId, out float x, out float y)
            {
                x = 0f;
                y = 0f;
                return false;
            }

            public bool TryGetUnitPosition(int runtimeId, out float x, out float y)
            {
                x = 0f;
                y = 0f;
                return false;
            }

            public bool TryGetUnitAttackTime(int runtimeId, out long attackTimeMs)
            {
                attackTimeMs = 0L;
                return false;
            }

            public bool TryGetUnitBodyRotation(int runtimeId, out float angleDegrees)
            {
                angleDegrees = 0f;
                return false;
            }

            public bool TryGetUnitWeaponAim(int runtimeId, out float angleDegrees)
            {
                angleDegrees = 0f;
                return false;
            }

            public bool TryGetUnitAttackIntervalSeconds(int runtimeId, out float intervalSeconds)
            {
                intervalSeconds = 1f;
                return false;
            }

            public bool TryGetProjectileState(int runtimeId, out float x, out float y, out float rotation)
            {
                x = 0f;
                y = 0f;
                rotation = 0f;
                return false;
            }

            public bool TryGetEnemyHealthRatio(int runtimeId, out float ratio)
            {
                ratio = 0f;
                return false;
            }
        }

        /// <summary>
        /// 记录 ResetUnitPose 调用次数的表现对象同步桩。
        /// </summary>
        private sealed class StubObjectSync : IViewObjectSync
        {
            internal int ResetPoseCallCount;

            public void ResetUnitPose(object viewObject)
            {
                ResetPoseCallCount++;
            }

            public void SetPosition(object viewObject, float logicX, float logicY) { }
            public void SetHealthRatio(object viewObject, float ratio) { }
            public void SetBodyRotation(object viewObject, float angleDegrees) { }
            public void SetWeaponAim(object viewObject, float angleDegrees) { }
            public void SetProjectileRotation(object viewObject, float angleDegrees) { }
            public void SetAttackIntervalSeconds(object viewObject, float intervalSeconds) { }
            public void SetUnitAttackTime(object viewObject, long attackTimeMs) { }
        }

        // ====================================================================
        // 测试夹具
        // ====================================================================

        private const int UnitRuntimeId = 10;
        private static readonly object UnitViewObject = new object();

        private sealed class Fixture
        {
            internal BattleViewRegistry Registry { get; } = new BattleViewRegistry();
            internal StubReadModelProvider ReadModel { get; } = new StubReadModelProvider();
            internal StubObjectSync ObjectSync { get; } = new StubObjectSync();
            internal BattleViewSynchronizer Synchronizer { get; }

            internal Fixture()
            {
                Synchronizer = new BattleViewSynchronizer(
                    Registry, ReadModel, ObjectSync);
                Registry.Register(
                    ViewObjectCategory.Unit, UnitRuntimeId, UnitViewObject);
            }
        }

        // ====================================================================
        // 测试
        // ====================================================================

        [Test]
        [Description("单位状态从 Attack 变为 Idle 时，Synchronizer 恰好调用一次 ResetUnitPose（姿态复位）。")]
        public void Sync_AttackToIdle_TriggersResetOnce()
        {
            var fixture = new Fixture();
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Attack;

            // 第一帧：首次同步 Attack 态，不触发复位（首帧复位见首次同步测试）。
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(0, fixture.ObjectSync.ResetPoseCallCount,
                "首次同步 Attack 态不应触发姿态复位");

            // 第二帧：状态切回 Idle，触发一次姿态复位。
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Idle;
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(1, fixture.ObjectSync.ResetPoseCallCount,
                "Attack → Idle 边沿应触发一次姿态复位");
        }

        [Test]
        [Description("Idle 持续时不再重复触发复位，避免待机动画每帧回到第 0 帧。")]
        public void Sync_IdleStays_DoesNotRepeatReset()
        {
            var fixture = new Fixture();
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Attack;

            // 首次同步 Attack 态：不触发复位（首帧复位见首次同步测试）。
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(0, fixture.ObjectSync.ResetPoseCallCount,
                "首次同步 Attack 态不应触发待机复位");

            // 切 Idle → 触发一次复位。
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Idle;
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(1, fixture.ObjectSync.ResetPoseCallCount,
                "Attack → Idle 应触发一次待机复位");

            // 持续 Idle 多帧：不再触发。
            fixture.Synchronizer.Sync(0f);
            fixture.Synchronizer.Sync(0f);
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(1, fixture.ObjectSync.ResetPoseCallCount,
                "Idle 持续不应重复触发待机复位");
        }

        [Test]
        [Description("首次同步不触发复位：回池/再次创建时 AcquireFromPool 已复位表现，"
            + "且首次观测即 Attack 态时复位会错误取消攻击动画。")]
        public void Sync_FirstObserved_DoesNotTriggerReset()
        {
            var fixture = new Fixture();
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Idle;

            // 首次同步：不触发复位（表现已在 AcquireFromPool 复位）。
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(0, fixture.ObjectSync.ResetPoseCallCount,
                "首次同步不应触发待机复位");

            // 首次观测 Attack 态：同样不触发。
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Attack;
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(0, fixture.ObjectSync.ResetPoseCallCount,
                "首次观测 Attack 态不应触发待机复位");
        }

        [Test]
        [Description("Idle → Attack 状态变化不触发复位（复位只针对进入待机）。")]
        public void Sync_IdleToAttack_DoesNotTriggerReset()
        {
            var fixture = new Fixture();
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Attack;

            // 首次同步 Attack 态：不触发复位。
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(0, fixture.ObjectSync.ResetPoseCallCount,
                "首次同步 Attack 态不应触发待机复位");

            // 切 Idle → 触发一次复位。
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Idle;
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(1, fixture.ObjectSync.ResetPoseCallCount,
                "Attack → Idle 应触发一次待机复位");

            // 再次进入 Attack 态：不触发复位（复位只针对进入待机）。
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Attack;
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(1, fixture.ObjectSync.ResetPoseCallCount,
                "Idle → Attack 不应触发待机复位");
        }

        [Test]
        [Description("单位被移除后重新登记（回池复用），再次同步不触发复位"
            + "（AcquireFromPool 已复位表现）。")]
        public void Sync_ReRegisteredAfterRemoval_DoesNotTriggerReset()
        {
            var fixture = new Fixture();
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Attack;
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(0, fixture.ObjectSync.ResetPoseCallCount,
                "首次同步 Attack 不应触发复位");

            // 状态切 Idle → 触发一次复位。
            fixture.ReadModel.UnitStates[UnitRuntimeId] = AttackUnitState.Idle;
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(1, fixture.ObjectSync.ResetPoseCallCount,
                "Attack → Idle 应触发一次待机复位");

            // 单位移除（回池），从注册表注销。
            fixture.Registry.Unregister(ViewObjectCategory.Unit, UnitRuntimeId);

            // 重新登记（再次创建）：同步器无上次状态 → 首次同步不触发复位
            // （AcquireFromPool 已复位表现，避免重复复位）。
            var viewObject = new object();
            fixture.Registry.Register(ViewObjectCategory.Unit, UnitRuntimeId, viewObject);
            fixture.Synchronizer.Sync(0f);
            Assert.AreEqual(1, fixture.ObjectSync.ResetPoseCallCount,
                "回池后再次创建不应触发额外待机复位");
        }
    }
}
