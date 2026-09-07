using System;
using System.Collections.Generic;
using System.Threading;
using FairyGUI;
using TEngine;

namespace GameLogic
{
    public sealed class FguiLifetimeScope : IDisposable
    {
        private readonly GameEventMgr _events = new GameEventMgr();
        private readonly List<Action> _cleanup = new List<Action>();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _disposed;

        public CancellationToken Token => _cancellation.Token;

        public void AddUIEvent(int eventType, Action handler)
        {
            ThrowIfDisposed();
            _events.AddEvent(eventType, handler);
        }

        public void AddUIEvent<T>(int eventType, Action<T> handler)
        {
            ThrowIfDisposed();
            _events.AddEvent(eventType, handler);
        }

        public void AddUIEvent<T1, T2>(int eventType, Action<T1, T2> handler)
        {
            ThrowIfDisposed();
            _events.AddEvent(eventType, handler);
        }

        public void AddUIEvent<T1, T2, T3>(int eventType, Action<T1, T2, T3> handler)
        {
            ThrowIfDisposed();
            _events.AddEvent(eventType, handler);
        }

        public void AddUIEvent<T1, T2, T3, T4>(int eventType, Action<T1, T2, T3, T4> handler)
        {
            ThrowIfDisposed();
            _events.AddEvent(eventType, handler);
        }

        public void AddListener(EventListener listener, EventCallback0 callback)
        {
            ThrowIfDisposed();
            listener.Add(callback);
            AddCleanup(() => listener.Remove(callback));
        }

        public void AddListener(EventListener listener, EventCallback1 callback)
        {
            ThrowIfDisposed();
            listener.Add(callback);
            AddCleanup(() => listener.Remove(callback));
        }

        public int AddTimer(TimerHandler callback, float seconds, bool loop = false, bool unscaled = false,
            params object[] args)
        {
            ThrowIfDisposed();
            int timerId = GameModule.Timer.AddTimer(callback, seconds, loop, unscaled, args);
            AddCleanup(() => GameModule.Timer.RemoveTimer(timerId));
            return timerId;
        }

        public void Add(IDisposable disposable)
        {
            if (disposable == null)
                return;
            AddCleanup(disposable.Dispose);
        }

        public void AddCleanup(Action cleanup)
        {
            if (cleanup == null)
                return;
            ThrowIfDisposed();
            _cleanup.Add(cleanup);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try { _cancellation.Cancel(); }
            catch (Exception exception) { Log.Warning($"FairyGUI lifetime cancellation failed: {exception}"); }

            for (int i = _cleanup.Count - 1; i >= 0; i--)
            {
                try { _cleanup[i](); }
                catch (Exception exception) { Log.Warning($"FairyGUI lifetime cleanup failed: {exception}"); }
            }
            _cleanup.Clear();
            try { _events.Clear(); }
            catch (Exception exception) { Log.Warning($"FairyGUI event cleanup failed: {exception}"); }
            finally { _cancellation.Dispose(); }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FguiLifetimeScope));
        }
    }
}
