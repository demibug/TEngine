using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 框架会话状态。
    /// </summary>
    public enum ModuleSystemState
    {
        Running,
        ShuttingDown,
        Stopped,
    }

    /// <summary>
    /// 框架关闭阶段。
    /// </summary>
    public enum ModuleShutdownPhase
    {
        None,
        StopWork,
        StopProcedures,
        BeforeShutdown,
        ShutdownModules,
        ReturnResourceInstances,
        ShutdownObjectPools,
        FinalizeResources,
        ClearState,
        Stopped,
    }

    /// <summary>
    /// 游戏框架模块实现类管理系统。
    /// <remarks>
    /// 这里也是框架会话的唯一关闭协调器。RootModule、Application.Quit 和显式调用都必须
    /// 进入同一个 Shutdown 路径；模块自己的 Shutdown 只负责本模块资源，不能改变阶段顺序。
    /// </remarks>
    /// </summary>
    public static class ModuleSystem
    {
        /// <summary>
        /// 默认设计的模块数量。
        /// <remarks>有增删可以自行修改减少内存分配与GCAlloc。</remarks>
        /// </summary>
        internal const int DESIGN_MODULE_COUNT = 16;

        private static readonly Dictionary<Type, Module> _moduleMaps = new Dictionary<Type, Module>(DESIGN_MODULE_COUNT);
        private static readonly LinkedList<Module> _modules = new LinkedList<Module>();
        private static readonly LinkedList<Module> _updateModules = new LinkedList<Module>();
        private static readonly List<IUpdateModule> _updateExecuteList = new List<IUpdateModule>(DESIGN_MODULE_COUNT);
        private static readonly HashSet<Module> _shutdownModules = new HashSet<Module>();
        private static readonly List<Exception> _shutdownErrors = new List<Exception>();
        private static readonly List<Module> _moduleSnapshot = new List<Module>(DESIGN_MODULE_COUNT);

        private static bool _isExecuteListDirty;
        private static bool _isShuttingDown;
        private static RootModule _sessionRoot;
        private static ModuleSystemState _state = ModuleSystemState.Running;
        private static ModuleShutdownPhase _shutdownPhase = ModuleShutdownPhase.None;

        /// <summary>
        /// 当前框架会话状态。
        /// </summary>
        public static ModuleSystemState State => _state;

        /// <summary>
        /// 当前关闭阶段。
        /// </summary>
        public static ModuleShutdownPhase ShutdownPhase => _shutdownPhase;

        /// <summary>
        /// 当前是否允许启动新工作。
        /// </summary>
        public static bool IsRunning => _state == ModuleSystemState.Running;

        /// <summary>
        /// 当前是否正在关闭。
        /// </summary>
        public static bool IsShuttingDown => _state == ModuleSystemState.ShuttingDown;

        /// <summary>
        /// 最近一次关闭期间收集到的异常。关闭完成后仍保留，便于诊断；下一次会话开始时清空。
        /// </summary>
        public static Exception[] ShutdownErrors => _shutdownErrors.ToArray();

        /// <summary>
        /// Unity 在下一次独立运行会话开始时调用。即使关闭期间有延迟回调，也不把旧会话的
        /// 模块字典、错误列表和 Root 所有权带入新会话。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            _moduleMaps.Clear();
            _modules.Clear();
            _updateModules.Clear();
            _updateExecuteList.Clear();
            _shutdownModules.Clear();
            _shutdownErrors.Clear();
            _moduleSnapshot.Clear();
            _isExecuteListDirty = false;
            _isShuttingDown = false;
            _sessionRoot = null;
            _state = ModuleSystemState.Running;
            _shutdownPhase = ModuleShutdownPhase.None;

            try { GameEvent.Shutdown(); }
            catch { }
            try { MemoryPool.ClearAll(); }
            catch { }
            try { Utility.Marshal.FreeCachedHGlobal(); }
            catch { }

            // AssetsReference 的静态表保存的是 Unity 对象引用，必须和模块表同时换代。
            try { AssetsReference.ResetForNewSession(); }
            catch { }
        }

        /// <summary>
        /// 由 RootModule 登记当前会话所有权。
        /// </summary>
        internal static bool TryBeginSession(RootModule root)
        {
            if (root == null || !IsRunning)
            {
                return false;
            }

            if (_sessionRoot == null)
            {
                _sessionRoot = root;
                return true;
            }

            return ReferenceEquals(_sessionRoot, root);
        }

        /// <summary>
        /// 所有者 Root 触发统一关闭。过期 Root 的延迟 OnDestroy 不得关闭新会话。
        /// </summary>
        internal static void Shutdown(RootModule owner)
        {
            if (owner == null || !ReferenceEquals(_sessionRoot, owner))
            {
                return;
            }

            Shutdown();
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public static void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (!IsRunning)
            {
                return;
            }

            if (_isExecuteListDirty)
            {
                _isExecuteListDirty = false;
                BuildExecuteList();
            }

            int executeCount = _updateExecuteList.Count;
            // 一个模块的 Update 可能直接触发退出；Shutdown 会清空执行列表，
            // 因此循环必须同时检查会话状态和当前列表长度，不能继续访问旧索引。
            for (int i = 0; i < executeCount && IsRunning && i < _updateExecuteList.Count; i++)
            {
                _updateExecuteList[i].Update(elapseSeconds, realElapseSeconds);
            }
        }

        /// <summary>
        /// 关闭并清理所有游戏框架模块。
        /// <remarks>重复和重入调用直接返回；每个阶段和每个模块都隔离异常。</remarks>
        /// </summary>
        public static void Shutdown()
        {
            if (!IsRunning || _isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            _state = ModuleSystemState.ShuttingDown;
            _shutdownPhase = ModuleShutdownPhase.StopWork;
            _shutdownErrors.Clear();

            // 从这一行开始 Update、GetModule 的创建路径和 RegisterModule 都失效。
            _updateExecuteList.Clear();
            _isExecuteListDirty = false;

            try
            {
                RunShutdownPhase(ModuleShutdownPhase.StopWork, StopNewWork);
                RunShutdownPhase(ModuleShutdownPhase.StopProcedures, StopProcedures);
                RunShutdownPhase(ModuleShutdownPhase.BeforeShutdown, RootModule.DispatchBeforeShutdown);
                RunShutdownPhase(ModuleShutdownPhase.ShutdownModules, ShutdownOtherModules);
                RunShutdownPhase(ModuleShutdownPhase.ReturnResourceInstances, ReturnResourceInstances);
                RunShutdownPhase(ModuleShutdownPhase.ShutdownObjectPools, ShutdownObjectPools);
                RunShutdownPhase(ModuleShutdownPhase.FinalizeResources, FinalizeResources);
                RunShutdownPhase(ModuleShutdownPhase.ClearState, ClearFrameworkState);
            }
            finally
            {
                // 无论哪个阶段或错误回调抛出，系统都不能卡在 ShuttingDown。
                _moduleMaps.Clear();
                _modules.Clear();
                _updateModules.Clear();
                _updateExecuteList.Clear();
                _shutdownModules.Clear();
                _moduleSnapshot.Clear();
                _isExecuteListDirty = false;
                _sessionRoot = null;
                _shutdownPhase = ModuleShutdownPhase.Stopped;
                _state = ModuleSystemState.Stopped;
                _isShuttingDown = false;
            }
        }

        /// <summary>
        /// 获取已存在的模块，不触发创建。关闭流程和晚回调只能使用此入口。
        /// </summary>
        public static T TryGetExistingModule<T>() where T : class
        {
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameFrameworkException(Utility.Text.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (_moduleMaps.TryGetValue(interfaceType, out Module module))
            {
                if (IsAvailableModule(module) && module is T existing)
                {
                    return existing;
                }
            }

            foreach (Module candidate in _modules)
            {
                if (IsAvailableModule(candidate) && candidate is T existing)
                {
                    _moduleMaps[interfaceType] = candidate;
                    return existing;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取游戏框架模块。
        /// </summary>
        /// <typeparam name="T">要获取的游戏框架模块类型。</typeparam>
        /// <returns>要获取的游戏框架模块。</returns>
        /// <remarks>如果要获取的游戏框架模块不存在，则自动创建该游戏框架模块。</remarks>
        public static T GetModule<T>() where T : class
        {
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameFrameworkException(Utility.Text.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (_moduleMaps.TryGetValue(interfaceType, out Module module))
            {
                if (IsAvailableModule(module) && module is T existing)
                {
                    return existing;
                }
            }

            if (!IsRunning)
            {
                throw new GameFrameworkException(Utility.Text.Format(
                    "Can not create module '{0}' while module system is {1}.", interfaceType.FullName, _state));
            }

            string moduleName = Utility.Text.Format("{0}.{1}, {2}", interfaceType.Namespace, interfaceType.Name.Substring(1), interfaceType.Assembly.GetName().Name);
            Type moduleType = Type.GetType(moduleName);
            if (moduleType == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Can not find Game Framework module type '{0}'.", moduleName));
            }

            return GetModule(moduleType, interfaceType) as T;
        }

        /// <summary>
        /// 获取游戏框架模块。
        /// </summary>
        private static Module GetModule(Type moduleType, Type interfaceType)
        {
            if (_moduleMaps.TryGetValue(moduleType, out Module module) && IsAvailableModule(module))
            {
                _moduleMaps[interfaceType] = module;
                return module;
            }

            return CreateModule(moduleType, interfaceType);
        }

        /// <summary>
        /// 创建游戏框架模块。
        /// </summary>
        private static Module CreateModule(Type moduleType, Type requestedInterface)
        {
            Module module = (Module)Activator.CreateInstance(moduleType);
            if (module == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Can not create module '{0}'.", moduleType.FullName));
            }

            _moduleMaps[moduleType] = module;
            if (requestedInterface != null)
            {
                _moduleMaps[requestedInterface] = module;
            }

            try
            {
                RegisterUpdate(module);
                return module;
            }
            catch
            {
                RemoveModule(module);
                TryShutdownFailedModule(module);
                throw;
            }
        }

        /// <summary>
        /// 注册自定义Module。
        /// </summary>
        /// <param name="module">Module。</param>
        /// <returns>Module实例。</returns>
        /// <exception cref="GameFrameworkException">框架异常。</exception>
        public static T RegisterModule<T>(Module module) where T : class
        {
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameFrameworkException(Utility.Text.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (module == null)
            {
                throw new GameFrameworkException("Module is invalid.");
            }

            if (!IsRunning)
            {
                throw new GameFrameworkException(Utility.Text.Format(
                    "Can not register module '{0}' while module system is {1}.", module.GetType().FullName, _state));
            }

            if (_moduleMaps.TryGetValue(interfaceType, out Module existing))
            {
                if (ReferenceEquals(existing, module))
                {
                    return module as T;
                }

                throw new GameFrameworkException(Utility.Text.Format("Module interface '{0}' is already registered.", interfaceType.FullName));
            }

            _moduleMaps[interfaceType] = module;
            _moduleMaps[module.GetType()] = module;
            try
            {
                RegisterUpdate(module);
                return module as T;
            }
            catch
            {
                RemoveModule(module);
                TryShutdownFailedModule(module);
                throw;
            }
        }

        private static void RegisterUpdate(Module module)
        {
            LinkedListNode<Module> current = _modules.First;
            while (current != null)
            {
                if (module.Priority > current.Value.Priority)
                {
                    break;
                }

                current = current.Next;
            }

            if (current != null)
            {
                _modules.AddBefore(current, module);
            }
            else
            {
                _modules.AddLast(module);
            }

            Type interfaceType = typeof(IUpdateModule);
            bool implementsInterface = interfaceType.IsInstanceOfType(module);

            if (implementsInterface)
            {
                LinkedListNode<Module> currentUpdate = _updateModules.First;
                while (currentUpdate != null)
                {
                    if (module.Priority > currentUpdate.Value.Priority)
                    {
                        break;
                    }

                    currentUpdate = currentUpdate.Next;
                }

                if (currentUpdate != null)
                {
                    _updateModules.AddBefore(currentUpdate, module);
                }
                else
                {
                    _updateModules.AddLast(module);
                }

                _isExecuteListDirty = true;
            }

            // OnInit 失败时由调用方移除整个半初始化模块，并尝试本模块的局部回收。
            module.OnInit();
        }

        private static void StopNewWork()
        {
            _moduleSnapshot.Clear();
            foreach (Module module in _modules)
            {
                _moduleSnapshot.Add(module);
            }

            foreach (Module module in _moduleSnapshot)
            {
                if (module is ResourceModule resourceModule)
                {
                    RunSafe("resource stop", resourceModule.BeginShutdown);
                }
                else if (module is ObjectPoolModule objectPoolModule)
                {
                    RunSafe("object pool stop", objectPoolModule.BeginShutdown);
                }
                else if (module is UpdateDriver updateDriver)
                {
                    RunSafe("update driver stop", updateDriver.BeginShutdown);
                }
            }

            _moduleSnapshot.Clear();
        }

        private static void StopProcedures()
        {
            ShutdownExistingModule<IProcedureModule>();
            ShutdownExistingModule<IFsmModule>();
        }

        private static void ShutdownOtherModules()
        {
            _moduleSnapshot.Clear();
            foreach (Module module in _modules)
            {
                _moduleSnapshot.Add(module);
            }

            // 保持旧 ModuleSystem 的逆序关闭语义；资源模块与对象池由后续冻结阶段接管。
            for (int i = _moduleSnapshot.Count - 1; i >= 0; i--)
            {
                Module module = _moduleSnapshot[i];
                if (module is ResourceModule || module is ObjectPoolModule || module is ProcedureModule || module is FsmModule)
                {
                    continue;
                }

                ShutdownModule(module);
            }

            _moduleSnapshot.Clear();
        }

        private static void ReturnResourceInstances()
        {
            ResourceModule resourceModule = FindExistingModule<ResourceModule>();
            if (resourceModule != null)
            {
                RunSafe("resource instance return", resourceModule.ReleaseOwnedInstances);
            }
        }

        private static void ShutdownObjectPools()
        {
            ObjectPoolModule objectPoolModule = FindExistingModule<ObjectPoolModule>();
            if (objectPoolModule != null)
            {
                ShutdownModule(objectPoolModule);
            }

            ResourceModule resourceModule = FindExistingModule<ResourceModule>();
            if (resourceModule != null)
            {
                RunSafe("resource pool close", resourceModule.MarkAssetPoolClosed);
            }
        }

        private static void FinalizeResources()
        {
            ResourceModule resourceModule = FindExistingModule<ResourceModule>();
            if (resourceModule != null)
            {
                ShutdownModule(resourceModule);
                RunSafe("resource finalization", resourceModule.FinalizeShutdown);
            }
        }

        private static void ClearFrameworkState()
        {
            RunSafe("game event clear", GameEvent.Shutdown);
            _updateExecuteList.Clear();
            _isExecuteListDirty = false;
            RunSafe("memory pool clear", MemoryPool.ClearAll);
            RunSafe("marshal cache clear", Utility.Marshal.FreeCachedHGlobal);
        }

        private static void ShutdownExistingModule<T>() where T : class
        {
            T existing = FindExistingModule<T>();
            if (existing is Module module)
            {
                ShutdownModule(module);
            }
        }

        private static T FindExistingModule<T>() where T : class
        {
            foreach (Module module in _modules)
            {
                if (IsAvailableModule(module) && module is T result)
                {
                    return result;
                }
            }

            return null;
        }

        private static bool IsAvailableModule(Module module)
        {
            return module != null && !_shutdownModules.Contains(module);
        }

        private static void ShutdownModule(Module module)
        {
            if (module == null || !_shutdownModules.Add(module))
            {
                return;
            }

            RunSafe(Utility.Text.Format("module '{0}' shutdown", module.GetType().FullName), module.Shutdown);
        }

        private static void RunShutdownPhase(ModuleShutdownPhase phase, Action action)
        {
            _shutdownPhase = phase;
            RunSafe(Utility.Text.Format("shutdown phase '{0}'", phase), action);
        }

        private static void RunSafe(string operation, Action action)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                _shutdownErrors.Add(exception);
                try
                {
                    Log.Error("{0} failed: {1}", operation, exception);
                }
                catch
                {
                    // 日志系统也可能正在收尾，错误已保存在 ShutdownErrors。
                }
            }
        }

        internal static void RecordShutdownError(Exception exception)
        {
            if (exception != null)
            {
                _shutdownErrors.Add(exception);
            }
        }

        private static void RemoveModule(Module module)
        {
            _modules.Remove(module);
            _updateModules.Remove(module);
            _isExecuteListDirty = true;

            var keysToRemove = new List<Type>();
            foreach (KeyValuePair<Type, Module> pair in _moduleMaps)
            {
                if (ReferenceEquals(pair.Value, module))
                {
                    keysToRemove.Add(pair.Key);
                }
            }

            foreach (Type key in keysToRemove)
            {
                _moduleMaps.Remove(key);
            }
        }

        private static void TryShutdownFailedModule(Module module)
        {
            if (module == null)
            {
                return;
            }

            try
            {
                module.Shutdown();
            }
            catch (Exception exception)
            {
                try
                {
                    Log.Error("Failed module '{0}' cleanup failed: {1}", module.GetType().FullName, exception);
                }
                catch
                {
                    // 保留原 OnInit 异常作为主异常，日志失败不改变清理结果。
                }
            }
        }

        /// <summary>
        /// 构造执行队列。
        /// </summary>
        private static void BuildExecuteList()
        {
            _updateExecuteList.Clear();
            foreach (Module updateModule in _updateModules)
            {
                _updateExecuteList.Add(updateModule as IUpdateModule);
            }
        }
    }
}
