using TEngine;

namespace Procedure
{
    public abstract class ProcedureBase : TEngine.ProcedureBase
    {
        /// <summary>
        /// 获取流程是否使用原生对话框
        /// 在一些特殊的流程（如游戏逻辑对话框资源更新完成前的流程）中，可以考虑调用原生对话框进行消息提示行为
        /// </summary>
        public abstract bool UseNativeDialog { get; }
        
        protected readonly IResourceModule _resourceModule = ModuleSystem.GetModule<IResourceModule>();
    }

    /// <summary>
    /// 启动流程的一次独立尝试。
    /// <remarks>
    /// 终态提交和取消都只能发生一次；流程离开后，旧尝试仍可能收到异步完成通知，
    /// 但调用方必须通过 <see cref="IsRunning"/> 丢弃这些通知。
    /// </remarks>
    /// </summary>
    internal sealed class StartupAttempt : System.IDisposable
    {
        public enum TerminalState
        {
            Running,
            Succeeded,
            Failed,
            Cancelled,
        }

        private readonly System.Threading.CancellationTokenSource _cancellationTokenSource =
            new System.Threading.CancellationTokenSource();
        private readonly System.Threading.CancellationToken _token;
        private bool _disposed;

        public StartupAttempt(int id)
        {
            Id = id;
            _token = _cancellationTokenSource.Token;
        }

        public int Id { get; }

        public TerminalState State { get; private set; } = TerminalState.Running;

        public System.Threading.CancellationToken Token => _token;

        public bool IsRunning => State == TerminalState.Running && !_disposed;

        public bool TrySucceed()
        {
            return TrySetTerminal(TerminalState.Succeeded);
        }

        public bool TryFail()
        {
            return TrySetTerminal(TerminalState.Failed);
        }

        public bool TryCancel()
        {
            return TrySetTerminal(TerminalState.Cancelled);
        }

        public void Invalidate()
        {
            if (State == TerminalState.Running)
            {
                State = TerminalState.Cancelled;
            }

            CancelToken();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelToken();
            _cancellationTokenSource.Dispose();
        }

        private bool TrySetTerminal(TerminalState terminalState)
        {
            if (State != TerminalState.Running || _disposed)
            {
                return false;
            }

            State = terminalState;
            if (terminalState != TerminalState.Succeeded)
            {
                CancelToken();
            }

            return true;
        }

        private void CancelToken()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (System.ObjectDisposedException)
            {
                // Dispose 后旧回调只会通过 IsRunning 被丢弃；这里不再向旧回调传播异常。
            }
        }
    }

    /// <summary>
    /// 热更入口的精确反射契约。
    /// </summary>
    internal static class StartupEntryContract
    {
        private static readonly System.Type[] EntranceParameterTypes = { typeof(object[]) };

        public static bool TryFindEntrance(
            System.Type appType,
            out System.Reflection.MethodInfo entranceMethod,
            out string error)
        {
            entranceMethod = null;
            error = null;

            if (appType == null)
            {
                error = "GameApp type is missing.";
                return false;
            }

            System.Reflection.MethodInfo[] candidates = appType.GetMethods(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.DeclaredOnly);

            int namedCount = 0;
            int matchingCount = 0;
            foreach (System.Reflection.MethodInfo candidate in candidates)
            {
                if (!string.Equals(candidate.Name, "Entrance", System.StringComparison.Ordinal))
                {
                    continue;
                }

                namedCount++;
                System.Reflection.ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.ReturnType != typeof(void) || candidate.IsGenericMethod ||
                    parameters.Length != EntranceParameterTypes.Length ||
                    parameters[0].ParameterType != EntranceParameterTypes[0])
                {
                    continue;
                }

                matchingCount++;
                entranceMethod = candidate;
            }

            if (namedCount == 1 && matchingCount == 1)
            {
                return true;
            }

            entranceMethod = null;
            error = matchingCount == 0
                ? "GameApp must declare exactly one public static void Entrance(object[]) method."
                : namedCount > 1
                    ? "GameApp declares ambiguous Entrance overloads; only one exact Entrance(object[]) method is allowed."
                    : "GameApp declares an Entrance method with an invalid signature.";
            return false;
        }
    }
}
