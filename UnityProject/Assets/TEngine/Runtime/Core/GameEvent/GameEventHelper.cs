using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine.Scripting;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 游戏事件注册入口（由热更入口 GameApp.Entrance 最先调用）。
    /// Init() 仅允许在启动阶段（模块系统运行中）调用一次：全量注册已加载程序集的事件接口，
    /// 任一程序集失败则清理已注册部分并抛聚合异常（交由启动流程失败处理）。
    /// 运行期动态加载的新程序集请使用 RegisterAssembly() 单独注册，禁止再次调用 Init()。
    /// 所有入口必须在 Unity 主线程执行。
    /// </summary>
    [Preserve]
    public static class GameEventHelper
    {
        /// <summary>
        /// 主线程 ID。默认取静态类首次加载时的线程（域加载在主线程，作为非 Unity 宿主下的兜底）；
        /// 进入播放模式时经 SubsystemRegistration 回调在主线程重新捕获，
        /// 防止该类首次被后台线程触发（如从热更 DLL 内先访问）导致基线失真。
        /// </summary>
        private static int _mainThreadId = Environment.CurrentManagedThreadId;

        /// <summary>
        /// Init 执行中标记，防止重入。
        /// </summary>
        private static bool _initializing;

        /// <summary>
        /// Init 成功完成标记：同一游戏会话只允许成功一次。
        /// </summary>
        private static bool _initialized;

        /// <summary>
        /// 全量注册已加载程序集的事件接口（启动阶段唯一入口）。
        /// 任一程序集注册失败：清理已注册部分（GameEvent.Shutdown）并抛聚合异常快速失败。
        /// </summary>
        public static void Init()
        {
            EnsureMainThread("Init");

            if (_initializing)
            {
                throw new InvalidOperationException("GameEventHelper.Init 正在执行中，禁止重入。");
            }

            if (_initialized)
            {
                throw new InvalidOperationException(
                    "GameEventHelper.Init 只允许在启动阶段调用一次；动态加载程序集请使用 RegisterAssembly。");
            }

            EnsureRunning("Init");

            _initializing = true;
            try
            {
                var dispatcher = GameEvent.EventMgr.GetDispatcher();
                var errors = new List<Exception>();

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        RegisterAssemblyCore(assembly, dispatcher);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(new InvalidOperationException(
                            $"注册程序集 {assembly.FullName} 事件失败。", exception));
                    }
                }

                if (errors.Count > 0)
                {
                    // 启动期失败：清掉半初始化状态，交由启动流程失败处理（不允许带着部分注册继续运行）。
                    GameEvent.Shutdown();
                    throw new AggregateException("事件接口注册失败。", errors);
                }

                _initialized = true;
            }
            finally
            {
                _initializing = false;
            }
        }

        /// <summary>
        /// 运行期注册单个程序集（动态加载后调用）。
        /// 失败仅抛异常并允许留下部分注册（半注册语义，非原子），绝不清理全局监听；
        /// RegWrapInterface 幂等覆盖写，修复原因后再次调用可补全该程序集剩余接口。
        /// </summary>
        /// <param name="assembly">要注册的程序集。</param>
        public static void RegisterAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            EnsureMainThread("RegisterAssembly");

            if (!_initialized)
            {
                throw new InvalidOperationException("请先完成 GameEventHelper.Init() 再注册动态程序集。");
            }

            EnsureRunning("RegisterAssembly");
            RegisterAssemblyCore(assembly, GameEvent.EventMgr.GetDispatcher());
        }

        /// <summary>
        /// 单个程序集的注册实现：读取程序集级注册标记并调用其 Registrar。
        /// </summary>
        private static void RegisterAssemblyCore(Assembly assembly, EventDispatcher dispatcher)
        {
            var attribute = assembly.GetCustomAttribute<EventAssemblyRegistrarAttribute>();
            if (attribute == null || attribute.RegistrarType == null)
            {
                return;
            }

            var registrar = (IEventAssemblyRegistrar)Activator.CreateInstance(attribute.RegistrarType);
            registrar.Register(dispatcher);
        }

        /// <summary>
        /// 事件系统重置（GameEvent.Shutdown 与新会话初始化回调调用），允许重新 Init。
        /// </summary>
        internal static void OnEventSystemReset()
        {
            _initializing = false;
            _initialized = false;
        }

        /// <summary>
        /// 新会话重置（复用域重载/进入播放模式时清理静态状态）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            // SubsystemRegistration 回调固定在 Unity 主线程执行，这里重新捕获主线程基线。
            _mainThreadId = Environment.CurrentManagedThreadId;
            OnEventSystemReset();
        }

        /// <summary>
        /// 校验主线程。
        /// </summary>
        private static void EnsureMainThread(string entry)
        {
            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException($"GameEventHelper.{entry} 必须在 Unity 主线程执行。");
            }
        }

        /// <summary>
        /// 校验模块系统运行态。
        /// </summary>
        private static void EnsureRunning(string entry)
        {
            if (!ModuleSystem.IsRunning)
            {
                throw new InvalidOperationException($"GameEventHelper.{entry} 要求模块系统处于运行状态。");
            }
        }
    }
}