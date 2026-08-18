using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>自定义 Buff 只能经 Manager 托管的同步生命周期访问窄目标端口。</summary>
    internal interface ICustomBuffHandler
    {
        void ApplyOrRefresh(
            BuffInstanceSnapshot instance,
            IBuffTarget target);

        void Remove(
            BuffInstanceSnapshot instance,
            IBuffTarget target);

        void Clear(
            BuffInstanceSnapshot instance,
            IBuffTarget target);
    }

    internal interface ICustomBuffHandlerRegistry
    {
        int Count { get; }
        void Register(int buffType, ICustomBuffHandler handler);
        bool TryGet(int buffType, out ICustomBuffHandler handler);
    }

    /// <summary>以 Buff type 唯一登记自定义处理器；不提供缺失 fallback。</summary>
    internal sealed class CustomBuffHandlerRegistry : ICustomBuffHandlerRegistry
    {
        private readonly Dictionary<int, ICustomBuffHandler> _handlers =
            new Dictionary<int, ICustomBuffHandler>();

        public int Count => _handlers.Count;

        public void Register(int buffType, ICustomBuffHandler handler)
        {
            if (buffType < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(buffType), buffType, "Buff type must be non-negative.");
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (_handlers.ContainsKey(buffType))
            {
                throw new InvalidOperationException($"Custom Buff handler already registered for type={buffType}.");
            }

            _handlers.Add(buffType, handler);
        }

        public bool TryGet(int buffType, out ICustomBuffHandler handler)
        {
            return _handlers.TryGetValue(buffType, out handler);
        }
    }
}
