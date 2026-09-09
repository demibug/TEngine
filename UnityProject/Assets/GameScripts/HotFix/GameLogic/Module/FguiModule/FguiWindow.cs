using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using TEngine;

namespace GameLogic
{
    public abstract class FguiWindow
    {
        private readonly List<FguiWidget> _widgets = new List<FguiWidget>();
        private FguiLifetimeScope _lifetime;
        private FguiPackageLease _packageLease;
        private bool _destroyed;
        private bool _cleanupStarted;
        private UniTask _refreshTail = UniTask.CompletedTask;

        public GComponent View { get; private set; }
        public FguiLifetimeScope Lifetime => _lifetime;
        public bool IsVisible => View != null && View.visible;
        internal bool IsDestroyed => _destroyed;
        internal CancellationToken LifecycleToken => _lifetime?.Token ?? CancellationToken.None;
        internal bool ModalEngaged { get; set; }

        protected virtual UniTask OnCreateAsync(object userData, CancellationToken cancellationToken) => UniTask.CompletedTask;
        protected virtual UniTask OnRefreshAsync(object userData, CancellationToken cancellationToken) => UniTask.CompletedTask;
        protected virtual void OnSetVisible(bool visible) { }
        protected virtual void OnDestroy() { }

        protected TWidget CreateWidget<TWidget>(GComponent view) where TWidget : FguiWidget, new()
        {
            if (_destroyed)
                throw new ObjectDisposedException(GetType().Name);
            var widget = new TWidget();
            widget.InternalCreate(view);
            _widgets.Add(widget);
            return widget;
        }

        internal void InternalAttach(GComponent view, FguiPackageLease packageLease)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            _packageLease = packageLease ?? throw new ArgumentNullException(nameof(packageLease));
            _lifetime = new FguiLifetimeScope();
            _destroyed = false;
            _cleanupStarted = false;
            _refreshTail = UniTask.CompletedTask;
        }

        internal async UniTask InternalCreateAsync(object userData, CancellationToken cancellationToken)
        {
            ThrowIfInvalid(cancellationToken);
            await OnCreateAsync(userData, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfInvalid(cancellationToken);
            await OnRefreshAsync(userData, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfInvalid(cancellationToken);
        }

        internal async UniTask InternalRefreshAsync(object userData, CancellationToken cancellationToken)
        {
            ThrowIfInvalid(cancellationToken);
            await OnRefreshAsync(userData, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfInvalid(cancellationToken);
        }

        internal UniTask EnqueueRefreshAsync(object userData, CancellationToken callerToken,
            Action<Exception> failureHandler)
        {
            if (_destroyed || _lifetime == null || View == null)
            {
                var canceled = new UniTaskCompletionSource();
                canceled.TrySetCanceled(GetCancellationToken(callerToken,
                    _lifetime?.Token ?? CancellationToken.None));
                return canceled.Task;
            }

            UniTask previous = _refreshTail;
            var result = new UniTaskCompletionSource();
            var finished = new UniTaskCompletionSource();
            _refreshTail = finished.Task;
            CancellationToken lifetimeToken = _lifetime.Token;
            RunRefreshAsync(previous, userData, callerToken, lifetimeToken, result, finished, failureHandler).Forget();
            return result.Task;
        }

        private async UniTaskVoid RunRefreshAsync(UniTask previous, object userData,
            CancellationToken callerToken, CancellationToken lifetimeToken,
            UniTaskCompletionSource result, UniTaskCompletionSource finished,
            Action<Exception> failureHandler)
        {
            CancellationTokenSource linkedCts = null;
            try
            {
                await previous;
                if (_destroyed || View == null || lifetimeToken.IsCancellationRequested ||
                    callerToken.IsCancellationRequested)
                {
                    result.TrySetCanceled(GetCancellationToken(callerToken, lifetimeToken));
                    return;
                }

                CancellationToken refreshToken = lifetimeToken;
                if (callerToken.CanBeCanceled)
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, callerToken);
                    refreshToken = linkedCts.Token;
                }

                await InternalRefreshAsync(userData, refreshToken);
                if (_destroyed || View == null || lifetimeToken.IsCancellationRequested ||
                    callerToken.IsCancellationRequested)
                {
                    result.TrySetCanceled(GetCancellationToken(callerToken, lifetimeToken));
                    return;
                }

                result.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                result.TrySetCanceled(GetCancellationToken(callerToken, lifetimeToken));
            }
            catch (Exception exception)
            {
                try
                {
                    failureHandler?.Invoke(exception);
                }
                catch (Exception handlerException)
                {
                    LogWarningSafely($"FairyGUI refresh failure handler raised an exception: {handlerException}");
                }

                result.TrySetException(exception);
            }
            finally
            {
                linkedCts?.Dispose();
                finished.TrySetResult();
            }
        }

        internal void InternalSetVisible(bool visible)
        {
            if (_destroyed || View == null)
                return;
            View.visible = visible;
            View.touchable = visible;
            OnSetVisible(visible);
        }

        private void ThrowIfInvalid(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_destroyed || View == null)
                throw new OperationCanceledException(cancellationToken);
            LifecycleToken.ThrowIfCancellationRequested();
        }

        private static CancellationToken GetCancellationToken(CancellationToken callerToken,
            CancellationToken lifetimeToken)
        {
            if (callerToken.IsCancellationRequested)
                return callerToken;
            if (lifetimeToken.IsCancellationRequested)
                return lifetimeToken;
            return CancellationToken.None;
        }

        internal void InternalDestroy()
        {
            if (_cleanupStarted)
                return;

            BeginDestroy();
            _cleanupStarted = true;

            Exception firstException = null;
            try
            {
                try
                {
                    _lifetime?.Dispose();
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }

                for (int i = _widgets.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _widgets[i].InternalDestroy();
                    }
                    catch (Exception exception)
                    {
                        firstException ??= exception;
                    }
                }

                _widgets.Clear();
                try
                {
                    OnDestroy();
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
            }
            finally
            {
                try
                {
                    if (View != null && !View.isDisposed)
                        View.Dispose();
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
                finally
                {
                    View = null;
                    try
                    {
                        _packageLease?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        firstException ??= exception;
                    }
                    finally
                    {
                        _packageLease = null;
                        _lifetime = null;
                    }
                }
            }

            if (firstException != null)
                LogWarningSafely($"FairyGUI window '{GetType().Name}' cleanup hook failed after complete cleanup: {firstException}");
        }

        private static void LogWarningSafely(string message)
        {
            try { Log.Warning(message); }
            catch { }
        }

        internal bool BeginDestroy()
        {
            if (_destroyed)
                return false;

            _destroyed = true;
            return true;
        }
    }

    public abstract class FguiWidget
    {
        private bool _destroyed;
        public GComponent View { get; private set; }
        public FguiLifetimeScope Lifetime { get; private set; }

        protected virtual void OnCreate() { }
        protected virtual void OnDestroy() { }

        internal void InternalCreate(GComponent view)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            Lifetime = new FguiLifetimeScope();
            try
            {
                OnCreate();
            }
            catch
            {
                Lifetime.Dispose();
                Lifetime = null;
                View = null;
                _destroyed = true;
                throw;
            }
        }

        internal void InternalDestroy()
        {
            if (_destroyed)
                return;
            _destroyed = true;

            Exception firstException = null;
            try
            {
                try
                {
                    Lifetime?.Dispose();
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }

                try
                {
                    OnDestroy();
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
            }
            finally
            {
                Lifetime = null;
                View = null;
            }

            if (firstException != null)
                LogWarningSafely($"FairyGUI widget '{GetType().Name}' cleanup hook failed after complete cleanup: {firstException}");
        }

        private static void LogWarningSafely(string message)
        {
            try { Log.Warning(message); }
            catch { }
        }
    }
}
