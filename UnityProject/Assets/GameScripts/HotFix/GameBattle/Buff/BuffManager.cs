using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>单局 Buff 的唯一所有者。</summary>
    /// <remarks>
    /// 单机小游戏的目标和 Buff 数量很少。这里只保存目标列表和实例表，
    /// 按目标、类型或来源操作时直接按实例 ID 排序扫描，避免维护多套同步索引。
    /// </remarks>
    internal sealed class BuffManager : IDisposable
    {
        private readonly BuffCatalogSnapshot _catalog;
        private readonly BattleActionScheduler _scheduler;
        private readonly ICustomBuffHandlerRegistry _customHandlers;
        private readonly List<IBuffTarget> _targets = new List<IBuffTarget>();
        private readonly Dictionary<long, ActiveBuffInstance> _instances =
            new Dictionary<long, ActiveBuffInstance>();
        private readonly Dictionary<StateAggregateKey, long> _stateAggregates =
            new Dictionary<StateAggregateKey, long>();

        private long _nextInstanceId = 1;
        private long _nextScheduleId = 1;
        private bool _stopped;
        private bool _disposed;

        internal BuffManager(
            BuffCatalogSnapshot catalog,
            BattleActionScheduler scheduler,
            ICustomBuffHandlerRegistry customHandlers = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _customHandlers = customHandlers ?? new CustomBuffHandlerRegistry();
        }

        internal int ActiveInstanceCount => _instances.Count;
        internal int RegisteredTargetCount => _targets.Count;
        internal bool IsDisposed => _disposed;

        internal int OwnedScheduleCount
        {
            get
            {
                int count = 0;
                foreach (ActiveBuffInstance instance in _instances.Values)
                {
                    if (instance.ScheduleHandle != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        internal BuffOperationResult RegisterTarget(IBuffTarget target)
        {
            if (_stopped || _disposed)
            {
                return BuffOperationResult.Rejected("BuffManager is stopped or disposed.");
            }

            if (target == null || !target.Handle.IsValid || !target.IsAvailable)
            {
                return BuffOperationResult.Rejected("Target is null, unavailable, or invalid.");
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                BuffTargetHandle current = _targets[i].Handle;
                if (HasSameIdentity(current, target.Handle))
                {
                    return BuffOperationResult.Rejected(
                        $"Target identity is already registered: {current}.");
                }
            }

            _targets.Add(target);
            return BuffOperationResult.Applied(0);
        }

        internal BuffOperationResult UnregisterTarget(BuffTargetHandle handle)
        {
            if (!TryFindExactTarget(handle, out IBuffTarget target))
            {
                for (int i = 0; i < _targets.Count; i++)
                {
                    if (HasSameIdentity(_targets[i].Handle, handle))
                    {
                        return BuffOperationResult.StaleTarget(
                            $"Requested generation={handle.Generation}, current={_targets[i].Handle.Generation}.");
                    }
                }

                return BuffOperationResult.NotFound($"Target is not registered: {handle}.");
            }

            List<long> ids = FindInstanceIds(active => active.Request.Target == handle);
            RemoveSelected(ids, useClearCallback: true, recompute: false);
            target.ClearBuffAggregates();
            _targets.Remove(target);
            RemoveStateCache(handle);
            return BuffOperationResult.Removed(0, 1);
        }

        internal BuffOperationResult Apply(BuffApplyRequest request)
        {
            if (_stopped || _disposed)
            {
                return BuffOperationResult.Rejected("BuffManager is stopped or disposed.");
            }

            if (request == null)
            {
                return BuffOperationResult.Rejected("Buff request is null.");
            }

            BuffRequestValidationResult validation = request.Validate();
            if (!validation.IsValid)
            {
                return BuffOperationResult.Rejected(validation);
            }

            if (!_catalog.TryGetByType(request.BuffType, out BuffDefinitionSnapshot definition))
            {
                return BuffOperationResult.Rejected(
                    $"Buff definition type={request.BuffType} was not found.");
            }

            if (!TryResolveTarget(request.Target, out IBuffTarget target, out BuffOperationResult targetFailure))
            {
                return targetFailure;
            }

            if (!ValidateTargetCapabilities(definition, target, out BuffOperationResult capabilityFailure))
            {
                return capabilityFailure;
            }

            ICustomBuffHandler customHandler = null;
            if (definition.Kind == BuffKind.Custom
                && !_customHandlers.TryGet(definition.Type, out customHandler))
            {
                return BuffOperationResult.Rejected(
                    $"Custom Buff type={definition.Type} has no registered handler.");
            }

            List<long> targetIds = FindInstanceIds(
                active => active.Request.Target == request.Target);
            ActiveBuffInstance refresh = null;
            var removals = new List<long>();

            for (int i = 0; i < targetIds.Count; i++)
            {
                ActiveBuffInstance active = _instances[targetIds[i]];
                if (definition.StackPolicy == BuffStackPolicy.Refresh
                    && active.Definition.Type == definition.Type
                    && active.Request.Source == request.Source)
                {
                    refresh = active;
                    break;
                }
            }

            if (definition.StackPolicy == BuffStackPolicy.Add)
            {
                int sameTypeCount = 0;
                for (int i = 0; i < targetIds.Count; i++)
                {
                    if (_instances[targetIds[i]].Definition.Type == definition.Type)
                    {
                        sameTypeCount++;
                    }
                }

                if (sameTypeCount >= definition.MaxStacks)
                {
                    return BuffOperationResult.Rejected(
                        $"Buff type={definition.Type} reached maxStacks={definition.MaxStacks}.");
                }
            }

            if (definition.StackPolicy == BuffStackPolicy.Replace)
            {
                AddMatches(targetIds, removals, active => active.Definition.Type == definition.Type);
            }

            if (!string.IsNullOrEmpty(definition.ConflictKey))
            {
                AddMatches(
                    targetIds,
                    removals,
                    active => active.Definition.Type != definition.Type
                        && string.Equals(
                            active.Definition.ConflictKey,
                            definition.ConflictKey,
                            StringComparison.Ordinal));
            }

            if (!TryCreateSchedule(
                    request,
                    refresh?.InstanceId ?? _nextInstanceId,
                    out ScheduledActionHandle schedule,
                    out long scheduleId,
                    out BuffOperationResult scheduleFailure))
            {
                return scheduleFailure;
            }

            if (refresh != null)
            {
                CancelSchedule(refresh);
                refresh.Request = CloneRequest(request);
                refresh.ScheduleHandle = schedule;
                refresh.ScheduleId = scheduleId;

                var affected = new AffectedChannels();
                affected.Include(refresh.Definition);
                InvalidateStateAggregates(request.Target, affected.State);
                Recompute(request.Target, target, affected, request.Source);
                customHandler?.ApplyOrRefresh(refresh.ToSnapshot(), target);
                return BuffOperationResult.Refreshed(refresh.InstanceId);
            }

            SortAndUnique(removals);
            var combined = new AffectedChannels();
            RemoveSelected(removals, useClearCallback: false, recompute: false, combined);

            long instanceId = _nextInstanceId++;
            var created = new ActiveBuffInstance(instanceId, definition, CloneRequest(request))
            {
                ScheduleHandle = schedule,
                ScheduleId = scheduleId,
            };
            _instances.Add(instanceId, created);

            combined.Include(definition);
            Recompute(request.Target, target, combined, request.Source);
            customHandler?.ApplyOrRefresh(created.ToSnapshot(), target);
            return BuffOperationResult.Applied(instanceId);
        }

        internal BuffOperationResult RemoveInstance(long instanceId)
        {
            if (!_instances.ContainsKey(instanceId))
            {
                return BuffOperationResult.NotFound(
                    $"Buff instanceId={instanceId} was not found.");
            }

            return RemoveResult(new List<long> { instanceId });
        }

        internal BuffOperationResult RemoveByTargetAndType(BuffTargetHandle target, int buffType)
        {
            if (!TryResolveTarget(target, out IBuffTarget _, out BuffOperationResult failure))
            {
                return failure;
            }

            return RemoveResult(FindInstanceIds(
                active => active.Request.Target == target && active.Definition.Type == buffType));
        }

        internal BuffOperationResult RemoveByTargetAndSource(
            BuffTargetHandle target,
            BuffSourceHandle source)
        {
            if (!TryResolveTarget(target, out IBuffTarget _, out BuffOperationResult failure))
            {
                return failure;
            }

            return RemoveResult(FindInstanceIds(
                active => active.Request.Target == target && active.Request.Source == source));
        }

        internal BuffOperationResult ClearTarget(BuffTargetHandle target)
        {
            if (!TryResolveTarget(target, out IBuffTarget _, out BuffOperationResult failure))
            {
                return failure;
            }

            return ClearTargetInternal(target, useClearCallback: true);
        }

        internal BuffOperationResult ClearSource(BuffSourceHandle source)
        {
            List<long> ids = FindInstanceIds(active => active.Request.Source == source);
            if (ids.Count == 0)
            {
                return BuffOperationResult.NotFound("No active Buff belongs to the source.");
            }

            int count = RemoveSelected(ids, useClearCallback: true, recompute: true);
            return BuffOperationResult.Removed(ids[0], count);
        }

        internal IReadOnlyList<BuffInstanceSnapshot> GetAllSnapshots()
        {
            return BuildSnapshots(FindInstanceIds(active => true));
        }

        internal IReadOnlyList<BuffInstanceSnapshot> GetTargetSnapshots(BuffTargetHandle target)
        {
            return BuildSnapshots(FindInstanceIds(active => active.Request.Target == target));
        }

        internal BuffOperationResult RefreshTarget(
            BuffTargetHandle handle,
            IReadOnlyList<BuffNumericChannel> changedChannels)
        {
            if (!TryResolveTarget(handle, out IBuffTarget target, out BuffOperationResult failure))
            {
                return failure;
            }

            var affected = new AffectedChannels();
            IReadOnlyList<BuffNumericChannel> channels =
                changedChannels ?? Array.Empty<BuffNumericChannel>();
            for (int i = 0; i < channels.Count; i++)
            {
                if (HasActiveNumericLayer(handle, channels[i]))
                {
                    affected.Add(channels[i]);
                }
            }

            if (affected.Numeric.Count == 0)
            {
                return BuffOperationResult.Refreshed(0);
            }

            Recompute(handle, target, affected, new BuffSourceHandle(0));
            return BuffOperationResult.Refreshed(0);
        }

        private bool HasActiveNumericLayer(BuffTargetHandle handle, BuffNumericChannel channel)
        {
            foreach (ActiveBuffInstance active in _instances.Values)
            {
                if (active.Request.Target == handle
                    && active.Definition.Kind == BuffKind.Numeric
                    && active.Definition.HasChannel((int)channel))
                {
                    return true;
                }
            }

            return false;
        }

        internal void GameOver()
        {
            if (_stopped || _disposed)
            {
                return;
            }

            _stopped = true;
            ClearAll();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _stopped = true;
            ClearAll();
            _disposed = true;
        }

        private bool TryCreateSchedule(
            BuffApplyRequest request,
            long instanceId,
            out ScheduledActionHandle handle,
            out long scheduleId,
            out BuffOperationResult failure)
        {
            handle = null;
            scheduleId = 0;
            failure = default;
            if (request.TimeMode == BuffTimeMode.Permanent)
            {
                return true;
            }

            if (_scheduler.IsFrozen || request.DurationMs > long.MaxValue - _scheduler.FrameNowMs)
            {
                failure = BuffOperationResult.Rejected(
                    "Duration cannot be scheduled on the current battle clock.");
                return false;
            }

            scheduleId = _nextScheduleId++;
            long capturedScheduleId = scheduleId;
            BuffTargetHandle capturedTarget = request.Target;
            handle = _scheduler.Schedule(
                _scheduler.FrameNowMs + request.DurationMs,
                () => OnExpiration(instanceId, capturedTarget, capturedScheduleId));
            if (handle != null)
            {
                return true;
            }

            failure = BuffOperationResult.Rejected("Duration schedule was rejected.");
            scheduleId = 0;
            return false;
        }

        private void OnExpiration(
            long instanceId,
            BuffTargetHandle target,
            long scheduleId)
        {
            if (_stopped || _disposed
                || !_instances.TryGetValue(instanceId, out ActiveBuffInstance active)
                || active.ScheduleId != scheduleId
                || active.Request.Target != target)
            {
                return;
            }

            if (!TryResolveTarget(target, out IBuffTarget _, out BuffOperationResult _))
            {
                return;
            }

            RemoveResult(new List<long> { instanceId });
        }

        private bool TryResolveTarget(
            BuffTargetHandle requested,
            out IBuffTarget target,
            out BuffOperationResult failure)
        {
            target = null;
            if (_stopped || _disposed)
            {
                failure = BuffOperationResult.Rejected("BuffManager is stopped or disposed.");
                return false;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                IBuffTarget current = _targets[i];
                if (!HasSameIdentity(current.Handle, requested))
                {
                    continue;
                }

                if (current.Handle != requested || !current.IsAvailable)
                {
                    failure = BuffOperationResult.StaleTarget(
                        $"Requested generation={requested.Generation}, current={current.Handle.Generation}.");
                    return false;
                }

                target = current;
                failure = default;
                return true;
            }

            failure = BuffOperationResult.NotFound($"Target is not registered: {requested}.");
            return false;
        }

        private static bool HasSameIdentity(BuffTargetHandle left, BuffTargetHandle right)
        {
            return left.EntityKind == right.EntityKind && left.RuntimeId == right.RuntimeId;
        }

        private static bool ValidateTargetCapabilities(
            BuffDefinitionSnapshot definition,
            IBuffTarget target,
            out BuffOperationResult failure)
        {
            if (definition.Kind == BuffKind.Numeric)
            {
                for (int i = 0; i < definition.Channels.Count; i++)
                {
                    var channel = (BuffNumericChannel)definition.Channels[i];
                    if (!target.Capabilities.Supports(channel))
                    {
                        failure = BuffOperationResult.UnsupportedTarget(
                            $"Target does not support numeric channel={channel}.");
                        return false;
                    }
                }
            }
            else if (definition.Kind == BuffKind.State)
            {
                for (int i = 0; i < definition.Channels.Count; i++)
                {
                    var channel = (BuffStateChannel)definition.Channels[i];
                    if (channel == BuffStateChannel.DamageImpulse
                        || channel == BuffStateChannel.KnockbackImpulse
                        || !target.Capabilities.Supports(channel))
                    {
                        failure = BuffOperationResult.UnsupportedTarget(
                            $"Target does not support stable state channel={channel}; impulse channels are Custom-only.");
                        return false;
                    }
                }
            }

            failure = default;
            return true;
        }

        private BuffOperationResult RemoveResult(List<long> ids)
        {
            if (ids.Count == 0)
            {
                return BuffOperationResult.NotFound();
            }

            int count = RemoveSelected(ids, useClearCallback: false, recompute: true);
            return count == 0
                ? BuffOperationResult.NotFound()
                : BuffOperationResult.Removed(ids[0], count);
        }

        private int RemoveSelected(
            List<long> ids,
            bool useClearCallback,
            bool recompute,
            AffectedChannels combined = null)
        {
            SortAndUnique(ids);
            var targetOrder = new List<BuffTargetHandle>();
            var affectedByTarget = new Dictionary<BuffTargetHandle, AffectedChannels>();
            var sourceByTarget = new Dictionary<BuffTargetHandle, BuffSourceHandle>();
            int removed = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                if (!_instances.TryGetValue(ids[i], out ActiveBuffInstance active))
                {
                    continue;
                }

                BuffTargetHandle handle = active.Request.Target;
                if (!affectedByTarget.TryGetValue(handle, out AffectedChannels affected))
                {
                    affected = new AffectedChannels();
                    affectedByTarget.Add(handle, affected);
                    targetOrder.Add(handle);
                }

                affected.Include(active.Definition);
                combined?.Include(active.Definition);
                sourceByTarget[handle] = active.Request.Source;

                BuffInstanceSnapshot snapshot = active.ToSnapshot();
                CancelSchedule(active);
                _instances.Remove(active.InstanceId);

                if (active.Definition.Kind == BuffKind.Custom
                    && _customHandlers.TryGet(active.Definition.Type, out ICustomBuffHandler handler)
                    && TryFindExactTarget(handle, out IBuffTarget customTarget))
                {
                    if (useClearCallback)
                    {
                        handler.Clear(snapshot, customTarget);
                    }
                    else
                    {
                        handler.Remove(snapshot, customTarget);
                    }
                }

                removed++;
            }

            if (recompute)
            {
                for (int i = 0; i < targetOrder.Count; i++)
                {
                    BuffTargetHandle handle = targetOrder[i];
                    if (TryFindExactTarget(handle, out IBuffTarget target) && target.IsAvailable)
                    {
                        Recompute(handle, target, affectedByTarget[handle], sourceByTarget[handle]);
                    }
                }
            }

            return removed;
        }

        private BuffOperationResult ClearTargetInternal(
            BuffTargetHandle target,
            bool useClearCallback)
        {
            List<long> ids = FindInstanceIds(active => active.Request.Target == target);
            if (ids.Count == 0)
            {
                return BuffOperationResult.Removed(0, 0);
            }

            int count = RemoveSelected(ids, useClearCallback, recompute: true);
            return BuffOperationResult.Removed(ids[0], count);
        }

        private void ClearAll()
        {
            List<long> ids = FindInstanceIds(active => true);
            RemoveSelected(ids, useClearCallback: true, recompute: false);

            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].IsAvailable)
                {
                    _targets[i].ClearBuffAggregates();
                }
            }

            _instances.Clear();
            _targets.Clear();
            _stateAggregates.Clear();
        }

        private void Recompute(
            BuffTargetHandle handle,
            IBuffTarget target,
            AffectedChannels affected,
            BuffSourceHandle operationSource)
        {
            List<long> ids = FindInstanceIds(active => active.Request.Target == handle);

            affected.Numeric.Sort();
            for (int i = 0; i < affected.Numeric.Count; i++)
            {
                BuffNumericChannel channel = affected.Numeric[i];
                if (!target.TryGetNumericBase(channel, out double baseValue))
                {
                    continue;
                }

                double flat = 0d;
                double ratio = 1d;
                for (int j = 0; j < ids.Count; j++)
                {
                    ActiveBuffInstance active = _instances[ids[j]];
                    if (active.Definition.Kind != BuffKind.Numeric
                        || !active.Definition.HasChannel((int)channel))
                    {
                        continue;
                    }

                    if (active.Request.ValueMode == BuffValueMode.Flat)
                    {
                        flat += active.Request.Value;
                    }
                    else
                    {
                        ratio *= 1d + active.Request.Value;
                    }
                }

                double value = (baseValue + flat) * ratio;
                if (IsIntegerChannel(channel))
                {
                    value = Math.Round(value, MidpointRounding.AwayFromZero);
                }

                target.CommitNumericAggregate(channel, value, operationSource);
            }

            affected.State.Sort();
            for (int i = 0; i < affected.State.Count; i++)
            {
                BuffStateChannel channel = affected.State[i];
                ActiveBuffInstance newest = null;
                for (int j = 0; j < ids.Count; j++)
                {
                    ActiveBuffInstance active = _instances[ids[j]];
                    if (active.Definition.Kind == BuffKind.State
                        && active.Definition.HasChannel((int)channel))
                    {
                        newest = active;
                    }
                }

                var key = new StateAggregateKey(handle, channel);
                long nextInstanceId = newest?.InstanceId ?? 0;
                bool hadPrevious = _stateAggregates.TryGetValue(key, out long previousInstanceId);
                if ((nextInstanceId == 0 && !hadPrevious)
                    || (nextInstanceId != 0 && previousInstanceId == nextInstanceId))
                {
                    continue;
                }

                if (nextInstanceId != 0)
                {
                    _stateAggregates[key] = nextInstanceId;
                }
                else
                {
                    _stateAggregates.Remove(key);
                }

                target.CommitStateAggregate(channel, nextInstanceId != 0, newest?.ToSnapshot());
            }
        }

        private void AddMatches(
            List<long> sourceIds,
            List<long> destination,
            Predicate<ActiveBuffInstance> predicate)
        {
            for (int i = 0; i < sourceIds.Count; i++)
            {
                ActiveBuffInstance active = _instances[sourceIds[i]];
                if (predicate(active))
                {
                    destination.Add(active.InstanceId);
                }
            }
        }

        private List<long> FindInstanceIds(Predicate<ActiveBuffInstance> predicate)
        {
            var ids = new List<long>();
            foreach (ActiveBuffInstance active in _instances.Values)
            {
                if (predicate(active))
                {
                    ids.Add(active.InstanceId);
                }
            }

            ids.Sort();
            return ids;
        }

        private bool TryFindExactTarget(BuffTargetHandle handle, out IBuffTarget target)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Handle == handle)
                {
                    target = _targets[i];
                    return true;
                }
            }

            target = null;
            return false;
        }

        private IReadOnlyList<BuffInstanceSnapshot> BuildSnapshots(List<long> ids)
        {
            var snapshots = new BuffInstanceSnapshot[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                snapshots[i] = _instances[ids[i]].ToSnapshot();
            }

            return Array.AsReadOnly(snapshots);
        }

        private void CancelSchedule(ActiveBuffInstance active)
        {
            if (active.ScheduleHandle == null)
            {
                return;
            }

            _scheduler.Cancel(active.ScheduleHandle);
            active.ScheduleHandle = null;
            active.ScheduleId = 0;
        }

        private void RemoveStateCache(BuffTargetHandle handle)
        {
            var keys = new List<StateAggregateKey>();
            foreach (StateAggregateKey key in _stateAggregates.Keys)
            {
                if (key.Target == handle)
                {
                    keys.Add(key);
                }
            }

            for (int i = 0; i < keys.Count; i++)
            {
                _stateAggregates.Remove(keys[i]);
            }
        }

        private void InvalidateStateAggregates(
            BuffTargetHandle handle,
            IReadOnlyList<BuffStateChannel> channels)
        {
            for (int i = 0; i < channels.Count; i++)
            {
                _stateAggregates.Remove(new StateAggregateKey(handle, channels[i]));
            }
        }

        private static BuffApplyRequest CloneRequest(BuffApplyRequest request)
        {
            return new BuffApplyRequest(
                request.BuffType,
                request.Target,
                request.Source,
                request.Value,
                request.ValueMode,
                request.TimeMode,
                request.DurationMs,
                request.CustomPayload);
        }

        private static bool IsIntegerChannel(BuffNumericChannel channel)
        {
            return channel == BuffNumericChannel.AttackPower
                || channel == BuffNumericChannel.MaxHealth
                || channel == BuffNumericChannel.CurrentHealth;
        }

        private static void SortAndUnique(List<long> ids)
        {
            ids.Sort();
            for (int i = ids.Count - 1; i > 0; i--)
            {
                if (ids[i] == ids[i - 1])
                {
                    ids.RemoveAt(i);
                }
            }
        }

        private sealed class ActiveBuffInstance
        {
            internal ActiveBuffInstance(
                long instanceId,
                BuffDefinitionSnapshot definition,
                BuffApplyRequest request)
            {
                InstanceId = instanceId;
                Definition = definition;
                Request = request;
            }

            internal long InstanceId { get; }
            internal BuffDefinitionSnapshot Definition { get; }
            internal BuffApplyRequest Request { get; set; }
            internal ScheduledActionHandle ScheduleHandle { get; set; }
            internal long ScheduleId { get; set; }

            internal BuffInstanceSnapshot ToSnapshot()
            {
                return new BuffInstanceSnapshot(InstanceId, Request);
            }
        }

        private sealed class AffectedChannels
        {
            internal List<BuffNumericChannel> Numeric { get; } = new List<BuffNumericChannel>();
            internal List<BuffStateChannel> State { get; } = new List<BuffStateChannel>();

            internal void Include(BuffDefinitionSnapshot definition)
            {
                for (int i = 0; i < definition.Channels.Count; i++)
                {
                    if (definition.Kind == BuffKind.Numeric)
                    {
                        Add((BuffNumericChannel)definition.Channels[i]);
                    }
                    else if (definition.Kind == BuffKind.State)
                    {
                        Add((BuffStateChannel)definition.Channels[i]);
                    }
                }
            }

            internal void Add(BuffNumericChannel channel)
            {
                if (!Numeric.Contains(channel))
                {
                    Numeric.Add(channel);
                }
            }

            private void Add(BuffStateChannel channel)
            {
                if (!State.Contains(channel))
                {
                    State.Add(channel);
                }
            }
        }

        private readonly struct StateAggregateKey : IEquatable<StateAggregateKey>
        {
            internal StateAggregateKey(BuffTargetHandle target, BuffStateChannel channel)
            {
                Target = target;
                Channel = channel;
            }

            internal BuffTargetHandle Target { get; }
            private BuffStateChannel Channel { get; }

            public bool Equals(StateAggregateKey other)
            {
                return Target == other.Target && Channel == other.Channel;
            }

            public override bool Equals(object obj)
            {
                return obj is StateAggregateKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Target.GetHashCode() * 397) ^ (int)Channel;
                }
            }
        }

    }
}
