using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>目标明确声明的 Buff 通道能力，不暴露可变集合。</summary>
    internal sealed class BuffTargetCapabilities
    {
        internal BuffTargetCapabilities(
            IReadOnlyList<BuffNumericChannel> numericChannels,
            IReadOnlyList<BuffStateChannel> stateChannels)
        {
            NumericChannels = CopyAndSort(numericChannels);
            StateChannels = CopyAndSort(stateChannels);
        }

        internal IReadOnlyList<BuffNumericChannel> NumericChannels { get; }
        internal IReadOnlyList<BuffStateChannel> StateChannels { get; }

        internal bool Supports(BuffNumericChannel channel)
        {
            for (int i = 0; i < NumericChannels.Count; i++)
            {
                if (NumericChannels[i] == channel)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool Supports(BuffStateChannel channel)
        {
            for (int i = 0; i < StateChannels.Count; i++)
            {
                if (StateChannels[i] == channel)
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<T> CopyAndSort<T>(IReadOnlyList<T> source) where T : struct
        {
            IReadOnlyList<T> input = source ?? Array.Empty<T>();
            var copy = new T[input.Count];
            for (int i = 0; i < input.Count; i++)
            {
                copy[i] = input[i];
            }

            Array.Sort(copy);
            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>BuffManager 可见的目标窄端口；不泄漏 Unity 对象或属性路径。</summary>
    internal interface IBuffTarget
    {
        BuffTargetHandle Handle { get; }
        bool IsAvailable { get; }
        BuffTargetCapabilities Capabilities { get; }

        bool TryGetNumericBase(BuffNumericChannel channel, out double value);

        void CommitNumericAggregate(
            BuffNumericChannel channel,
            double effectiveValue,
            BuffSourceHandle source);

        void CommitStateAggregate(
            BuffStateChannel channel,
            bool active,
            BuffInstanceSnapshot payloadSource);

        void ClearBuffAggregates();
    }
}
