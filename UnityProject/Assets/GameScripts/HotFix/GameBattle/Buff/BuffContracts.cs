using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>可参与 Buff 身份的实体类别。</summary>
    internal enum BuffEntityKind
    {
        None = 0,
        Unit = 1,
        Enemy = 2,
    }

    /// <summary>带生命周期世代的 Buff 目标句柄。</summary>
    internal readonly struct BuffTargetHandle : IEquatable<BuffTargetHandle>
    {
        internal BuffTargetHandle(BuffEntityKind entityKind, int runtimeId, long generation)
        {
            EntityKind = entityKind;
            RuntimeId = runtimeId;
            Generation = generation;
        }

        internal BuffEntityKind EntityKind { get; }
        internal int RuntimeId { get; }
        internal long Generation { get; }

        internal bool IsValid => (EntityKind == BuffEntityKind.Unit || EntityKind == BuffEntityKind.Enemy)
            && RuntimeId > 0
            && Generation > 0;

        public bool Equals(BuffTargetHandle other)
        {
            return EntityKind == other.EntityKind
                && RuntimeId == other.RuntimeId
                && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is BuffTargetHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)EntityKind;
                hash = hash * 397 ^ RuntimeId;
                hash = hash * 397 ^ Generation.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(BuffTargetHandle left, BuffTargetHandle right) => left.Equals(right);
        public static bool operator !=(BuffTargetHandle left, BuffTargetHandle right) => !left.Equals(right);

        public override string ToString()
        {
            return $"BuffTargetHandle(kind={EntityKind}, runtimeId={RuntimeId}, generation={Generation})";
        }
    }

    /// <summary>Buff 来源身份：一个稳定来源 ID，以及可选的攻击者运行时 ID。</summary>
    internal readonly struct BuffSourceHandle : IEquatable<BuffSourceHandle>
    {
        internal BuffSourceHandle(int sourceId, int attackerRuntimeId = -1)
        {
            SourceId = sourceId;
            AttackerRuntimeId = attackerRuntimeId;
        }

        internal int SourceId { get; }
        internal int AttackerRuntimeId { get; }
        internal bool IsValid => SourceId >= 0 && AttackerRuntimeId >= -1;

        public bool Equals(BuffSourceHandle other)
        {
            return SourceId == other.SourceId
                && AttackerRuntimeId == other.AttackerRuntimeId;
        }

        public override bool Equals(object obj)
        {
            return obj is BuffSourceHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (SourceId * 397) ^ AttackerRuntimeId;
            }
        }

        public static bool operator ==(BuffSourceHandle left, BuffSourceHandle right) => left.Equals(right);
        public static bool operator !=(BuffSourceHandle left, BuffSourceHandle right) => !left.Equals(right);
    }

    internal enum BuffValueMode
    {
        Flat = 0,
        Ratio = 1,
    }

    internal enum BuffTimeMode
    {
        Permanent = 0,
        DurationMs = 1,
    }

    internal enum BuffRequestValidationCode
    {
        None = 0,
        InvalidBuffType = 1,
        InvalidTarget = 2,
        InvalidSource = 3,
        UnknownValueMode = 4,
        NonFiniteValue = 5,
        InvalidRatioFactor = 6,
        UnknownTimeMode = 7,
        InvalidDuration = 8,
    }

    internal readonly struct BuffRequestValidationResult
    {
        private BuffRequestValidationResult(BuffRequestValidationCode code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        internal BuffRequestValidationCode Code { get; }
        internal string Message { get; }
        internal bool IsValid => Code == BuffRequestValidationCode.None;

        internal static BuffRequestValidationResult Valid()
        {
            return new BuffRequestValidationResult(BuffRequestValidationCode.None, string.Empty);
        }

        internal static BuffRequestValidationResult Invalid(BuffRequestValidationCode code, string message)
        {
            if (code == BuffRequestValidationCode.None)
            {
                throw new ArgumentException("Invalid result requires a non-None code.", nameof(code));
            }

            return new BuffRequestValidationResult(code, message);
        }
    }

    /// <summary>一次 Buff 申请的完整、不可变运行时数据。</summary>
    internal sealed class BuffApplyRequest
    {
        internal BuffApplyRequest(
            int buffType,
            BuffTargetHandle target,
            BuffSourceHandle source,
            double value,
            BuffValueMode valueMode,
            BuffTimeMode timeMode,
            long durationMs,
            IReadOnlyList<byte> customPayload = null)
        {
            BuffType = buffType;
            Target = target;
            Source = source;
            Value = value;
            ValueMode = valueMode;
            TimeMode = timeMode;
            DurationMs = timeMode == BuffTimeMode.Permanent ? 0 : durationMs;

            IReadOnlyList<byte> sourcePayload = customPayload ?? Array.Empty<byte>();
            var payloadCopy = new byte[sourcePayload.Count];
            for (int i = 0; i < sourcePayload.Count; i++)
            {
                payloadCopy[i] = sourcePayload[i];
            }

            CustomPayload = Array.AsReadOnly(payloadCopy);
        }

        internal int BuffType { get; }
        internal BuffTargetHandle Target { get; }
        internal BuffSourceHandle Source { get; }
        internal double Value { get; }
        internal BuffValueMode ValueMode { get; }
        internal BuffTimeMode TimeMode { get; }
        internal long DurationMs { get; }
        internal IReadOnlyList<byte> CustomPayload { get; }

        internal BuffRequestValidationResult Validate()
        {
            if (BuffType < 0)
            {
                return BuffRequestValidationResult.Invalid(
                    BuffRequestValidationCode.InvalidBuffType, $"BuffType={BuffType} must be non-negative.");
            }

            if (!Target.IsValid)
            {
                return BuffRequestValidationResult.Invalid(
                    BuffRequestValidationCode.InvalidTarget, $"Target is invalid: {Target}.");
            }

            if (!Source.IsValid)
            {
                return BuffRequestValidationResult.Invalid(
                    BuffRequestValidationCode.InvalidSource, "Source handle is invalid.");
            }

            if (ValueMode != BuffValueMode.Flat && ValueMode != BuffValueMode.Ratio)
            {
                return BuffRequestValidationResult.Invalid(
                    BuffRequestValidationCode.UnknownValueMode, $"ValueMode={ValueMode} is unknown.");
            }

            if (double.IsNaN(Value) || double.IsInfinity(Value))
            {
                return BuffRequestValidationResult.Invalid(
                    BuffRequestValidationCode.NonFiniteValue, $"Value={Value} must be finite.");
            }

            if (ValueMode == BuffValueMode.Ratio)
            {
                double factor = 1d + Value;
                if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0d)
                {
                    return BuffRequestValidationResult.Invalid(
                        BuffRequestValidationCode.InvalidRatioFactor,
                        $"Ratio value={Value} produces invalid factor={factor}.");
                }
            }

            if (TimeMode != BuffTimeMode.Permanent && TimeMode != BuffTimeMode.DurationMs)
            {
                return BuffRequestValidationResult.Invalid(
                    BuffRequestValidationCode.UnknownTimeMode, $"TimeMode={TimeMode} is unknown.");
            }

            if (TimeMode == BuffTimeMode.DurationMs && DurationMs <= 0)
            {
                return BuffRequestValidationResult.Invalid(
                    BuffRequestValidationCode.InvalidDuration,
                    $"DurationMs={DurationMs} must be positive for a duration Buff.");
            }

            return BuffRequestValidationResult.Valid();
        }
    }

    /// <summary>活动 Buff 实例的只读副本。</summary>
    internal sealed class BuffInstanceSnapshot
    {
        internal BuffInstanceSnapshot(long instanceId, BuffApplyRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            InstanceId = instanceId;
            Request = new BuffApplyRequest(
                request.BuffType,
                request.Target,
                request.Source,
                request.Value,
                request.ValueMode,
                request.TimeMode,
                request.DurationMs,
                request.CustomPayload);
        }

        internal long InstanceId { get; }
        internal BuffApplyRequest Request { get; }
    }

    internal enum BuffOperationStatus
    {
        Applied = 0,
        Refreshed = 1,
        Rejected = 2,
        Removed = 3,
        NotFound = 4,
        StaleTarget = 5,
        UnsupportedTarget = 6,
    }

    /// <summary>Buff 命令的结构化结果，不用异常表达普通拒绝。</summary>
    internal readonly struct BuffOperationResult
    {
        private BuffOperationResult(
            BuffOperationStatus status,
            long instanceId,
            int affectedCount,
            BuffRequestValidationCode validationCode,
            string message)
        {
            Status = status;
            InstanceId = instanceId;
            AffectedCount = affectedCount;
            ValidationCode = validationCode;
            Message = message ?? string.Empty;
        }

        internal BuffOperationStatus Status { get; }
        internal long InstanceId { get; }
        internal int AffectedCount { get; }
        internal BuffRequestValidationCode ValidationCode { get; }
        internal string Message { get; }
        internal bool IsSuccess => Status == BuffOperationStatus.Applied
            || Status == BuffOperationStatus.Refreshed
            || Status == BuffOperationStatus.Removed;

        internal static BuffOperationResult Applied(long instanceId) =>
            new BuffOperationResult(BuffOperationStatus.Applied, instanceId, 1, BuffRequestValidationCode.None, string.Empty);

        internal static BuffOperationResult Refreshed(long instanceId) =>
            new BuffOperationResult(BuffOperationStatus.Refreshed, instanceId, 1, BuffRequestValidationCode.None, string.Empty);

        internal static BuffOperationResult Rejected(BuffRequestValidationResult validation) =>
            new BuffOperationResult(BuffOperationStatus.Rejected, 0, 0, validation.Code, validation.Message);

        internal static BuffOperationResult Rejected(string message) =>
            new BuffOperationResult(BuffOperationStatus.Rejected, 0, 0, BuffRequestValidationCode.None, message);

        internal static BuffOperationResult Removed(long instanceId, int affectedCount = 1) =>
            new BuffOperationResult(BuffOperationStatus.Removed, instanceId, affectedCount, BuffRequestValidationCode.None, string.Empty);

        internal static BuffOperationResult NotFound(string message = null) =>
            new BuffOperationResult(BuffOperationStatus.NotFound, 0, 0, BuffRequestValidationCode.None, message);

        internal static BuffOperationResult StaleTarget(string message = null) =>
            new BuffOperationResult(BuffOperationStatus.StaleTarget, 0, 0, BuffRequestValidationCode.None, message);

        internal static BuffOperationResult UnsupportedTarget(string message = null) =>
            new BuffOperationResult(BuffOperationStatus.UnsupportedTarget, 0, 0, BuffRequestValidationCode.None, message);
    }
}
