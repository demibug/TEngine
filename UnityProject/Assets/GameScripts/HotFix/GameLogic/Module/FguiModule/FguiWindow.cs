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

        public GComponent View { get; private set; }
        public FguiLifetimeScope Lifetime => _lifetime;
        public bool IsVisible => View != null && View.visible;
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
        }

        internal async UniTask InternalCreateAsync(object userData, CancellationToken cancellationToken)
        {
            await OnCreateAsync(userData, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await OnRefreshAsync(userData, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal async UniTask InternalRefreshAsync(object userData, CancellationToken cancellationToken)
        {
            await OnRefreshAsync(userData, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal void InternalSetVisible(bool visible)
        {
            if (_destroyed || View == null)
                return;
            View.visible = visible;
            View.touchable = visible;
            OnSetVisible(visible);
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
                Log.Warning($"FairyGUI window '{GetType().Name}' cleanup hook failed after complete cleanup: {firstException}");
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
                Log.Warning($"FairyGUI widget '{GetType().Name}' cleanup hook failed after complete cleanup: {firstException}");
        }
    }
}
