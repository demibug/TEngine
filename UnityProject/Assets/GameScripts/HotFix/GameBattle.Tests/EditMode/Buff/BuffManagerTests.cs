using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Buff
{
    [TestFixture]
    internal class BuffManagerTests
    {
        [Test]
        public void AddPolicy_RejectsBeyondCapWithoutChangingInstancesOrAggregate()
        {
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Add, 2);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(target);

            BuffOperationResult first = manager.Apply(Request(0, target.Handle, Source(1), 10));
            BuffOperationResult second = manager.Apply(Request(0, target.Handle, Source(2), 20));
            int commitsBeforeReject = target.NumericCommits.Count;
            BuffOperationResult rejected = manager.Apply(Request(0, target.Handle, Source(3), 30));

            Assert.AreEqual(BuffOperationStatus.Applied, first.Status);
            Assert.AreEqual(BuffOperationStatus.Applied, second.Status);
            Assert.AreEqual(BuffOperationStatus.Rejected, rejected.Status);
            Assert.AreEqual(2, manager.ActiveInstanceCount);
            Assert.AreEqual(commitsBeforeReject, target.NumericCommits.Count);
            Assert.AreEqual(130d, target.LastNumericValue(BuffNumericChannel.AttackPower));
        }

        [Test]
        public void RefreshPolicy_PreservesIdentityReplacesValueAndRestartsDuration()
        {
            var scheduler = new BattleActionScheduler();
            scheduler.BeginFrame(0);
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Refresh, 1);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(scheduler, null, definition);
            manager.RegisterTarget(target);

            BuffOperationResult applied = manager.Apply(Request(
                0, target.Handle, Source(1), 10, timeMode: BuffTimeMode.DurationMs, durationMs: 100));
            scheduler.BeginFrame(50);
            BuffOperationResult refreshed = manager.Apply(Request(
                0, target.Handle, Source(1), 20, timeMode: BuffTimeMode.DurationMs, durationMs: 100));

            Assert.AreEqual(applied.InstanceId, refreshed.InstanceId);
            Assert.AreEqual(BuffOperationStatus.Refreshed, refreshed.Status);
            Assert.AreEqual(1, manager.ActiveInstanceCount);
            Assert.AreEqual(1, manager.OwnedScheduleCount);
            Assert.AreEqual(120d, target.LastNumericValue(BuffNumericChannel.AttackPower));

            scheduler.BeginFrame(100);
            Assert.AreEqual(0, scheduler.FlushDueActions(50), "旧 schedule 已取消");
            Assert.AreEqual(1, manager.ActiveInstanceCount);
            scheduler.BeginFrame(150);
            Assert.AreEqual(1, scheduler.FlushDueActions(50));
            Assert.AreEqual(0, manager.ActiveInstanceCount);
            Assert.AreEqual(0, manager.OwnedScheduleCount);
            Assert.AreEqual(100d, target.LastNumericValue(BuffNumericChannel.AttackPower));
        }

        [Test]
        public void ConflictReplacement_RemovesOldLayersInInstanceOrderBeforeNewApply()
        {
            BuffDefinitionSnapshot firstType = CustomDef(7, BuffStackPolicy.Add, 2, "exclusive");
            BuffDefinitionSnapshot replacement = CustomDef(8, BuffStackPolicy.Add, 1, "exclusive");
            var handler = new RecordingCustomHandler();
            var registry = new CustomBuffHandlerRegistry();
            registry.Register(7, handler);
            registry.Register(8, handler);
            RecordingTarget target = Target(1, 1);
            BuffManager manager = Manager(new BattleActionScheduler(), registry, firstType, replacement);
            manager.RegisterTarget(target);

            manager.Apply(Request(7, target.Handle, Source(1), 0));
            manager.Apply(Request(7, target.Handle, Source(2), 0));
            BuffOperationResult result = manager.Apply(Request(8, target.Handle, Source(3), 0));

            CollectionAssert.AreEqual(
                new[] { "apply:1", "apply:2", "remove:1", "remove:2", "apply:3" },
                handler.Events);
            Assert.AreEqual(BuffOperationStatus.Applied, result.Status);
            Assert.AreEqual(1, manager.ActiveInstanceCount);
            Assert.AreEqual(8, manager.GetAllSnapshots()[0].Request.BuffType);
        }

        [Test]
        public void ReplacePolicy_RemovesSameTypeBeforeCreatingNewIdentity()
        {
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Replace, 1);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(target);

            long first = manager.Apply(Request(0, target.Handle, Source(1), 10)).InstanceId;
            BuffOperationResult replacement = manager.Apply(Request(0, target.Handle, Source(2), 20));

            Assert.Greater(replacement.InstanceId, first);
            Assert.AreEqual(1, manager.ActiveInstanceCount);
            Assert.AreEqual(replacement.InstanceId, manager.GetAllSnapshots()[0].InstanceId);
            Assert.AreEqual(120d, target.LastNumericValue(BuffNumericChannel.AttackPower));
        }

        [Test]
        public void RejectedCustomOrUnsupportedRequest_DoesNotRemoveConflict()
        {
            BuffDefinitionSnapshot existing = NumericDef(
                0, BuffStackPolicy.Add, 2, "exclusive", BuffNumericChannel.AttackPower);
            BuffDefinitionSnapshot missingCustom = CustomDef(7, BuffStackPolicy.Add, 1, "exclusive");
            BuffDefinitionSnapshot unsupported = NumericDef(
                1, BuffStackPolicy.Add, 1, "exclusive", BuffNumericChannel.MoveSpeed);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(existing, missingCustom, unsupported);
            manager.RegisterTarget(target);
            BuffOperationResult old = manager.Apply(Request(0, target.Handle, Source(1), 5));

            BuffOperationResult customRejected = manager.Apply(Request(7, target.Handle, Source(2), 0));
            BuffOperationResult unsupportedRejected = manager.Apply(Request(1, target.Handle, Source(2), 5));

            Assert.AreEqual(BuffOperationStatus.Rejected, customRejected.Status);
            Assert.AreEqual(BuffOperationStatus.UnsupportedTarget, unsupportedRejected.Status);
            Assert.AreEqual(1, manager.ActiveInstanceCount);
            Assert.AreEqual(old.InstanceId, manager.GetAllSnapshots()[0].InstanceId);
            Assert.AreEqual(105d, target.LastNumericValue(BuffNumericChannel.AttackPower));
        }

        [Test]
        public void StableSelectors_RemoveOnlySelectedLayersAndRepeatedRemovalIsIdempotent()
        {
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Add, 10);
            RecordingTarget firstTarget = Target(1, 1, BuffNumericChannel.AttackPower);
            RecordingTarget secondTarget = Target(2, 1, BuffNumericChannel.AttackPower);
            firstTarget.SetBase(BuffNumericChannel.AttackPower, 100);
            secondTarget.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffSourceHandle sourceA = Source(1);
            BuffSourceHandle sourceB = Source(2);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(firstTarget);
            manager.RegisterTarget(secondTarget);
            long firstId = manager.Apply(Request(0, firstTarget.Handle, sourceA, 10)).InstanceId;
            manager.Apply(Request(0, firstTarget.Handle, sourceB, 20));
            manager.Apply(Request(0, secondTarget.Handle, sourceA, 30));

            Assert.AreEqual(1, manager.RemoveByTargetAndSource(firstTarget.Handle, sourceA).AffectedCount);
            Assert.AreEqual(1, manager.ClearSource(sourceA).AffectedCount);
            Assert.AreEqual(1, manager.RemoveByTargetAndType(firstTarget.Handle, 0).AffectedCount);
            Assert.AreEqual(0, manager.ActiveInstanceCount);
            Assert.AreEqual(BuffOperationStatus.NotFound, manager.RemoveInstance(firstId).Status);

            manager.Apply(Request(0, firstTarget.Handle, sourceB, 5));
            Assert.AreEqual(1, manager.ClearTarget(firstTarget.Handle).AffectedCount);
            Assert.AreEqual(0, manager.ActiveInstanceCount);
        }

        [Test]
        public void Snapshots_AreSortedCopiedAndNotWritable()
        {
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Add, 3);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(target);
            manager.Apply(Request(0, target.Handle, Source(1), 1, payload: new byte[] { 1 }));
            manager.Apply(Request(0, target.Handle, Source(2), 2, payload: new byte[] { 2 }));

            IReadOnlyList<BuffInstanceSnapshot> snapshots = manager.GetTargetSnapshots(target.Handle);

            Assert.AreEqual(1, snapshots[0].InstanceId);
            Assert.AreEqual(2, snapshots[1].InstanceId);
            Assert.Throws<NotSupportedException>(
                () => ((IList<BuffInstanceSnapshot>)snapshots)[0] = snapshots[1]);
            Assert.Throws<NotSupportedException>(
                () => ((IList<byte>)snapshots[0].Request.CustomPayload)[0] = 9);
            Assert.AreEqual(2, manager.ActiveInstanceCount);
        }

        [Test]
        public void NumericAggregation_UsesFlatThenRatioAndRefreshesFromCurrentBaseWithoutDrift()
        {
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Add, 4);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(target);
            long flatId = manager.Apply(Request(0, target.Handle, Source(1), 10)).InstanceId;
            manager.Apply(Request(
                0, target.Handle, Source(2), 0.2, valueMode: BuffValueMode.Ratio));

            Assert.AreEqual(132d, target.LastNumericValue(BuffNumericChannel.AttackPower));
            manager.RemoveInstance(flatId);
            Assert.AreEqual(120d, target.LastNumericValue(BuffNumericChannel.AttackPower));

            target.SetBase(BuffNumericChannel.AttackPower, 200);
            manager.RefreshTarget(target.Handle, new[] { BuffNumericChannel.AttackPower });
            Assert.AreEqual(240d, target.LastNumericValue(BuffNumericChannel.AttackPower));
            manager.RefreshTarget(target.Handle, new[] { BuffNumericChannel.AttackPower });
            Assert.AreEqual(240d, target.LastNumericValue(BuffNumericChannel.AttackPower));
        }

        [Test]
        public void NumericAggregate_IsClampedByTargetBoundary()
        {
            BuffDefinitionSnapshot definition = NumericDef(
                3, BuffStackPolicy.Add, 2, channel: BuffNumericChannel.MoveSpeed);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.MoveSpeed);
            target.SetBase(BuffNumericChannel.MoveSpeed, 1);
            target.SetClamp(BuffNumericChannel.MoveSpeed, 0.1, 10);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(target);

            manager.Apply(Request(3, target.Handle, Source(1), -10));

            Assert.AreEqual(0.1d, target.LastNumericValue(BuffNumericChannel.MoveSpeed));
        }

        [Test]
        public void StateAggregation_UsesNewestPayloadFallsBackAndClearsFinalLayerOnce()
        {
            BuffDefinitionSnapshot definition = StateDef(8, BuffStackPolicy.Add, 3);
            RecordingTarget target = Target(1, 1, state: BuffStateChannel.MovementDisabled);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(target);
            long first = manager.Apply(Request(8, target.Handle, Source(1), 0, payload: new byte[] { 1 })).InstanceId;
            long second = manager.Apply(Request(8, target.Handle, Source(2), 0, payload: new byte[] { 2 })).InstanceId;

            Assert.AreEqual(second, target.StateCommits[target.StateCommits.Count - 1].PayloadInstanceId);
            manager.RemoveInstance(second);
            StateCommit fallback = target.StateCommits[target.StateCommits.Count - 1];
            Assert.IsTrue(fallback.Active);
            Assert.AreEqual(first, fallback.PayloadInstanceId);
            int commitsBeforeFinal = target.StateCommits.Count;
            manager.RemoveInstance(first);
            Assert.AreEqual(commitsBeforeFinal + 1, target.StateCommits.Count);
            Assert.IsFalse(target.StateCommits[target.StateCommits.Count - 1].Active);
        }

        [Test]
        public void StateRefresh_PreservesIdentityAndPublishesReplacementPayload()
        {
            BuffDefinitionSnapshot definition = StateDef(8, BuffStackPolicy.Refresh, 1);
            RecordingTarget target = Target(1, 1, state: BuffStateChannel.MovementDisabled);
            BuffManager manager = Manager(definition);
            manager.RegisterTarget(target);
            BuffSourceHandle source = Source(1);
            long instanceId = manager.Apply(Request(
                8, target.Handle, source, 0, payload: new byte[] { 1 })).InstanceId;
            int beforeRefresh = target.StateCommits.Count;

            BuffOperationResult refreshed = manager.Apply(Request(
                8, target.Handle, source, 0, payload: new byte[] { 9 }));

            Assert.AreEqual(instanceId, refreshed.InstanceId);
            Assert.AreEqual(beforeRefresh + 1, target.StateCommits.Count);
            StateCommit latest = target.StateCommits[target.StateCommits.Count - 1];
            Assert.AreEqual(instanceId, latest.PayloadInstanceId);
            Assert.AreEqual(9, latest.PayloadFirstByte);
        }

        [Test]
        public void OldGenerationSchedule_CannotMutateNewRental()
        {
            var scheduler = new BattleActionScheduler();
            scheduler.BeginFrame(0);
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Add, 3);
            RecordingTarget oldTarget = Target(5, 1, BuffNumericChannel.AttackPower);
            oldTarget.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(scheduler, null, definition);
            manager.RegisterTarget(oldTarget);
            manager.Apply(Request(
                0, oldTarget.Handle, Source(1), 10,
                timeMode: BuffTimeMode.DurationMs, durationMs: 100));
            manager.UnregisterTarget(oldTarget.Handle);

            RecordingTarget newTarget = Target(5, 2, BuffNumericChannel.AttackPower);
            newTarget.SetBase(BuffNumericChannel.AttackPower, 200);
            manager.RegisterTarget(newTarget);
            long newId = manager.Apply(Request(0, newTarget.Handle, Source(2), 20)).InstanceId;
            scheduler.BeginFrame(100);

            Assert.AreEqual(0, scheduler.FlushDueActions(100));
            Assert.AreEqual(1, manager.ActiveInstanceCount);
            Assert.AreEqual(newId, manager.GetAllSnapshots()[0].InstanceId);
            Assert.AreEqual(220d, newTarget.LastNumericValue(BuffNumericChannel.AttackPower));
        }

        [Test]
        public void DurationCallbacks_ExpireOnlyAtBoundaryAndPreserveSameTimeFifo()
        {
            var scheduler = new BattleActionScheduler();
            scheduler.BeginFrame(0);
            BuffDefinitionSnapshot definition = CustomDef(7, BuffStackPolicy.Add, 2, "");
            var handler = new RecordingCustomHandler();
            var registry = new CustomBuffHandlerRegistry();
            registry.Register(7, handler);
            RecordingTarget target = Target(1, 1);
            BuffManager manager = Manager(scheduler, registry, definition);
            manager.RegisterTarget(target);
            manager.Apply(Request(
                7, target.Handle, Source(1), 0,
                timeMode: BuffTimeMode.DurationMs, durationMs: 100));
            manager.Apply(Request(
                7, target.Handle, Source(2), 0,
                timeMode: BuffTimeMode.DurationMs, durationMs: 100));
            handler.Events.Clear();

            scheduler.BeginFrame(99);
            Assert.AreEqual(0, scheduler.FlushDueActions(99));
            Assert.AreEqual(2, manager.ActiveInstanceCount);
            scheduler.BeginFrame(100);
            Assert.AreEqual(2, scheduler.FlushDueActions(1));

            CollectionAssert.AreEqual(new[] { "remove:1", "remove:2" }, handler.Events);
            Assert.AreEqual(0, manager.ActiveInstanceCount);
            Assert.AreEqual(0, manager.OwnedScheduleCount);
            Assert.AreEqual(0, scheduler.FlushDueActions(1), "到期回调必须 exactly-once");
        }

        [Test]
        public void ActiveRemoval_CancelsDurationAndPreventsSecondMutation()
        {
            var scheduler = new BattleActionScheduler();
            scheduler.BeginFrame(0);
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Add, 1);
            RecordingTarget target = Target(1, 1, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(scheduler, null, definition);
            manager.RegisterTarget(target);
            long instanceId = manager.Apply(Request(
                0, target.Handle, Source(1), 10,
                timeMode: BuffTimeMode.DurationMs, durationMs: 100)).InstanceId;
            manager.RemoveInstance(instanceId);
            int commitsAfterRemove = target.NumericCommits.Count;

            scheduler.BeginFrame(100);
            Assert.AreEqual(0, scheduler.FlushDueActions(100));
            Assert.AreEqual(commitsAfterRemove, target.NumericCommits.Count);
            Assert.AreEqual(0, manager.ActiveInstanceCount);
            Assert.AreEqual(0, manager.OwnedScheduleCount);
        }

        [Test]
        public void RegistrationAndShutdown_AreGenerationSafeAndIdempotent()
        {
            BuffDefinitionSnapshot definition = NumericDef(0, BuffStackPolicy.Add, 2);
            RecordingTarget target = Target(1, 2, BuffNumericChannel.AttackPower);
            target.SetBase(BuffNumericChannel.AttackPower, 100);
            BuffManager manager = Manager(definition);

            Assert.AreEqual(BuffOperationStatus.Applied, manager.RegisterTarget(target).Status);
            Assert.AreEqual(BuffOperationStatus.Rejected, manager.RegisterTarget(target).Status);
            BuffOperationResult stale = manager.Apply(Request(
                0,
                new BuffTargetHandle(BuffEntityKind.Unit, 1, 1),
                Source(1),
                10));
            Assert.AreEqual(BuffOperationStatus.StaleTarget, stale.Status);
            Assert.AreEqual(0, manager.ActiveInstanceCount);

            manager.Apply(Request(0, target.Handle, Source(1), 10));
            manager.GameOver();
            manager.GameOver();
            Assert.AreEqual(0, manager.ActiveInstanceCount);
            Assert.AreEqual(0, manager.RegisteredTargetCount);
            Assert.AreEqual(0, manager.OwnedScheduleCount);
            Assert.AreEqual(1, target.ClearCount);
            Assert.AreEqual(BuffOperationStatus.Rejected,
                manager.Apply(Request(0, target.Handle, Source(1), 10)).Status);
            manager.Dispose();
            manager.Dispose();
            Assert.IsTrue(manager.IsDisposed);
        }

        private static BuffManager Manager(params BuffDefinitionSnapshot[] definitions)
        {
            return Manager(new BattleActionScheduler(), null, definitions);
        }

        private static BuffManager Manager(
            BattleActionScheduler scheduler,
            ICustomBuffHandlerRegistry registry,
            params BuffDefinitionSnapshot[] definitions)
        {
            return new BuffManager(new BuffCatalogSnapshot(definitions), scheduler, registry);
        }

        private static BuffDefinitionSnapshot NumericDef(
            int type,
            BuffStackPolicy policy,
            int maxStacks,
            string conflictKey = "",
            BuffNumericChannel channel = BuffNumericChannel.AttackPower)
        {
            return new BuffDefinitionSnapshot(
                type, $"numeric{type}", "", BuffKind.Numeric, new[] { (int)channel },
                policy, maxStacks, conflictKey);
        }

        private static BuffDefinitionSnapshot StateDef(
            int type,
            BuffStackPolicy policy,
            int maxStacks)
        {
            return new BuffDefinitionSnapshot(
                type, $"state{type}", "", BuffKind.State,
                new[] { (int)BuffStateChannel.MovementDisabled }, policy, maxStacks, "");
        }

        private static BuffDefinitionSnapshot CustomDef(
            int type,
            BuffStackPolicy policy,
            int maxStacks,
            string conflictKey)
        {
            return new BuffDefinitionSnapshot(
                type, $"custom{type}", "", BuffKind.Custom, Array.Empty<int>(),
                policy, maxStacks, conflictKey);
        }

        private static RecordingTarget Target(
            int runtimeId,
            long generation,
            BuffNumericChannel? numeric = null,
            BuffStateChannel? state = null)
        {
            return new RecordingTarget(
                new BuffTargetHandle(BuffEntityKind.Unit, runtimeId, generation),
                numeric.HasValue ? new[] { numeric.Value } : Array.Empty<BuffNumericChannel>(),
                state.HasValue ? new[] { state.Value } : Array.Empty<BuffStateChannel>());
        }

        private static BuffSourceHandle Source(int id)
        {
            return new BuffSourceHandle(id);
        }

        private static BuffApplyRequest Request(
            int type,
            BuffTargetHandle target,
            BuffSourceHandle source,
            double value,
            BuffValueMode valueMode = BuffValueMode.Flat,
            BuffTimeMode timeMode = BuffTimeMode.Permanent,
            long durationMs = 0,
            IReadOnlyList<byte> payload = null)
        {
            return new BuffApplyRequest(
                type, target, source, value, valueMode, timeMode, durationMs, payload);
        }

        private readonly struct NumericCommit
        {
            internal NumericCommit(BuffNumericChannel channel, double value)
            {
                Channel = channel;
                Value = value;
            }

            internal BuffNumericChannel Channel { get; }
            internal double Value { get; }
        }

        private readonly struct StateCommit
        {
            internal StateCommit(
                BuffStateChannel channel,
                bool active,
                long payloadInstanceId,
                int payloadFirstByte)
            {
                Channel = channel;
                Active = active;
                PayloadInstanceId = payloadInstanceId;
                PayloadFirstByte = payloadFirstByte;
            }

            internal BuffStateChannel Channel { get; }
            internal bool Active { get; }
            internal long PayloadInstanceId { get; }
            internal int PayloadFirstByte { get; }
        }

        private sealed class RecordingTarget : IBuffTarget
        {
            private readonly Dictionary<BuffNumericChannel, double> _baseValues =
                new Dictionary<BuffNumericChannel, double>();
            private readonly Dictionary<BuffNumericChannel, Tuple<double, double>> _clamps =
                new Dictionary<BuffNumericChannel, Tuple<double, double>>();

            internal RecordingTarget(
                BuffTargetHandle handle,
                IReadOnlyList<BuffNumericChannel> numeric,
                IReadOnlyList<BuffStateChannel> state)
            {
                Handle = handle;
                Capabilities = new BuffTargetCapabilities(numeric, state);
            }

            public BuffTargetHandle Handle { get; }
            public bool IsAvailable { get; set; } = true;
            public BuffTargetCapabilities Capabilities { get; }
            internal List<NumericCommit> NumericCommits { get; } = new List<NumericCommit>();
            internal List<StateCommit> StateCommits { get; } = new List<StateCommit>();
            internal int ClearCount { get; private set; }

            internal void SetBase(BuffNumericChannel channel, double value) => _baseValues[channel] = value;

            internal void SetClamp(BuffNumericChannel channel, double minimum, double maximum)
            {
                _clamps[channel] = Tuple.Create(minimum, maximum);
            }

            internal double LastNumericValue(BuffNumericChannel channel)
            {
                for (int i = NumericCommits.Count - 1; i >= 0; i--)
                {
                    if (NumericCommits[i].Channel == channel)
                    {
                        return NumericCommits[i].Value;
                    }
                }

                throw new InvalidOperationException($"No commit for {channel}.");
            }

            public bool TryGetNumericBase(BuffNumericChannel channel, out double value)
            {
                return _baseValues.TryGetValue(channel, out value);
            }

            public void CommitNumericAggregate(
                BuffNumericChannel channel,
                double effectiveValue,
                BuffSourceHandle source)
            {
                if (_clamps.TryGetValue(channel, out Tuple<double, double> clamp))
                {
                    effectiveValue = Math.Max(clamp.Item1, Math.Min(clamp.Item2, effectiveValue));
                }

                NumericCommits.Add(new NumericCommit(channel, effectiveValue));
            }

            public void CommitStateAggregate(
                BuffStateChannel channel,
                bool active,
                BuffInstanceSnapshot payloadSource)
            {
                int payload = payloadSource != null && payloadSource.Request.CustomPayload.Count > 0
                    ? payloadSource.Request.CustomPayload[0]
                    : -1;
                StateCommits.Add(new StateCommit(
                    channel,
                    active,
                    payloadSource?.InstanceId ?? 0,
                    payload));
            }

            public void ClearBuffAggregates()
            {
                ClearCount++;
            }
        }

        private sealed class RecordingCustomHandler : ICustomBuffHandler
        {
            internal List<string> Events { get; } = new List<string>();

            public void ApplyOrRefresh(
                BuffInstanceSnapshot instance,
                IBuffTarget target)
            {
                Events.Add($"apply:{instance.InstanceId}");
            }

            public void Remove(
                BuffInstanceSnapshot instance,
                IBuffTarget target)
            {
                Events.Add($"remove:{instance.InstanceId}");
            }

            public void Clear(
                BuffInstanceSnapshot instance,
                IBuffTarget target)
            {
                Events.Add($"clear:{instance.InstanceId}");
            }
        }
    }
}
