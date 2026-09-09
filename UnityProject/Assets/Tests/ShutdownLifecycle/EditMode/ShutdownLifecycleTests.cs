using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.ShutdownLifecycleTests
{
    public sealed class ModuleSystemShutdownTests
    {
        private static readonly MethodInfo ResetForNewSessionMethod =
            typeof(ModuleSystem).GetMethod("ResetForNewSession", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            if (ModuleSystem.IsRunning)
            {
                ModuleSystem.Shutdown();
            }

            ResetForNewSession();
        }

        [Test]
        public void Shutdown_IsOrdered_Idempotent_AndBlocksLateWork()
        {
            var calls = new List<string>();
            var module = new RecordingModule(() => calls.Add("module"));
            bool lateListenerInvoked = false;

            RootModule.BeforeShutdown += () =>
            {
                calls.Add("before");
                RootModule.BeforeShutdown += () => lateListenerInvoked = true;
            };

            ModuleSystem.RegisterModule<IOrderedModule>(module);
            ModuleSystem.Shutdown();

            Assert.AreEqual(ModuleSystemState.Stopped, ModuleSystem.State);
            Assert.AreEqual(ModuleShutdownPhase.Stopped, ModuleSystem.ShutdownPhase);
            Assert.AreEqual(1, module.ShutdownCount);
            Assert.AreEqual(new[] { "before", "module" }, calls);
            Assert.IsFalse(lateListenerInvoked, "关闭阶段新增的 BeforeShutdown 订阅不得执行。");

            ModuleSystem.Shutdown();
            Assert.AreEqual(1, module.ShutdownCount, "重复退出不得重复清理模块。");
            Assert.IsNull(ModuleSystem.TryGetExistingModule<IOrderedModule>());

            Assert.Throws<GameFrameworkException>(() => ModuleSystem.RegisterModule<ILateModule>(new RecordingModule(null)));
            Assert.Throws<GameFrameworkException>(() => ModuleSystem.GetModule<IUpdateDriver>());
        }

        [Test]
        public void Shutdown_ContinuesAfterException_AndRejectsReentry()
        {
            var calls = new List<string>();
            var failing = new ThrowingShutdownModule(calls);
            var following = new RecordingModule(() => calls.Add("following"));
            bool secondBeforeListenerRan = false;

            RootModule.BeforeShutdown += () => throw new InvalidOperationException("before listener failure");
            RootModule.BeforeShutdown += () => secondBeforeListenerRan = true;

            ModuleSystem.RegisterModule<IFailingModule>(failing);
            ModuleSystem.RegisterModule<IFollowingModule>(following);

            LogAssert.Expect(LogType.Error,
                new Regex("BeforeShutdown listener failed: .*before listener failure"));
            LogAssert.Expect(LogType.Error,
                new Regex("module '.*ThrowingShutdownModule' shutdown failed: .*module shutdown failure"));
            ModuleSystem.Shutdown();

            Assert.IsTrue(failing.ReentrantShutdownReturned);
            Assert.IsTrue(failing.LateRegistrationRejected);
            Assert.AreEqual(1, failing.ShutdownCount);
            Assert.AreEqual(1, following.ShutdownCount, "前一个模块异常不得跳过后续模块。");
            Assert.IsTrue(secondBeforeListenerRan, "一个 BeforeShutdown 异常不得跳过其他订阅者。");
            Assert.AreEqual(ModuleSystemState.Stopped, ModuleSystem.State);
            Assert.GreaterOrEqual(ModuleSystem.ShutdownErrors.Length, 2,
                "模块异常和 BeforeShutdown 异常都应进入关闭诊断汇总。");
        }

        [Test]
        public void RegisterModule_OnInitFailure_CleansPartialModule()
        {
            var module = new FailingInitModule();

            Assert.Throws<InvalidOperationException>(() => ModuleSystem.RegisterModule<IFailingInitModule>(module));

            Assert.AreEqual(1, module.OnInitCount);
            Assert.AreEqual(1, module.ShutdownCount, "部分初始化模块必须执行一次局部清理。");
            Assert.IsNull(ModuleSystem.TryGetExistingModule<IFailingInitModule>());
        }

        [Test]
        public void Update_StopsImmediatelyWhenShutdownBegins()
        {
            var module = new UpdatingModule();
            ModuleSystem.RegisterModule<IUpdatingModule>(module);

            ModuleSystem.Update(0.016f, 0.016f);
            Assert.AreEqual(1, module.UpdateCount);

            ModuleSystem.Shutdown();
            ModuleSystem.Update(0.016f, 0.016f);

            Assert.AreEqual(1, module.UpdateCount, "Stopped 状态不得继续调用模块更新。");
        }

        [Test]
        public void Update_ShutdownReentryFromCallback_DoesNotVisitClearedList()
        {
            var stoppingModule = new ShutdownDuringUpdateModule();
            var followingModule = new UpdatingModule();
            ModuleSystem.RegisterModule<IShutdownDuringUpdateModule>(stoppingModule);
            ModuleSystem.RegisterModule<IUpdatingModule>(followingModule);

            Assert.DoesNotThrow(() => ModuleSystem.Update(0.016f, 0.016f));

            Assert.AreEqual(1, stoppingModule.UpdateCount);
            Assert.AreEqual(0, followingModule.UpdateCount, "更新回调内退出后不得继续访问旧执行列表。");
            Assert.AreEqual(ModuleSystemState.Stopped, ModuleSystem.State);
        }

        [Test]
        public void ShutdownQueriesExcludeClosedModulesButKeepOpenDependencies()
        {
            var earlier = new EarlierQueryModule();
            var later = new LaterQueryModule();
            var openDependency = new OpenDependencyModule();

            // ShutdownOtherModules uses reverse priority order: earlier closes before later,
            // while the higher-priority dependency remains available to later cleanup.
            ModuleSystem.RegisterModule<IEarlierModuleAlias>(earlier);
            ModuleSystem.RegisterModule<ILaterQueryModule>(later);
            ModuleSystem.RegisterModule<IOpenDependencyModule>(openDependency);

            ModuleSystem.Shutdown();

            Assert.AreEqual(1, earlier.ShutdownCount);
            Assert.AreEqual(1, later.ShutdownCount);
            Assert.AreEqual(1, openDependency.ShutdownCount);
            Assert.IsTrue(later.DirectAliasLookupMissing,
                "TryGetExistingModule 的字典路径不得返回已经关闭的模块。");
            Assert.IsTrue(later.ListAliasLookupMissing,
                "TryGetExistingModule 的列表回退路径不得返回已经关闭的模块。");
            Assert.IsTrue(later.GetClosedAliasRejected,
                "GetModule 的已缓存接口路径不得返回已经关闭的模块。");
            Assert.IsTrue(later.OpenDependencyAvailable,
                "清理期间仍允许取得尚未关闭的依赖模块。");
            Assert.IsTrue(later.OpenDependencyGetAvailable,
                "GetModule 仍应返回清理所需的尚未关闭依赖。");
        }

        private static void ResetForNewSession()
        {
            Assert.IsNotNull(ResetForNewSessionMethod, "测试必须能调用框架的显式会话重置点。");
            ResetForNewSessionMethod.Invoke(null, null);
        }

        private interface IOrderedModule { }
        private interface ILateModule { }
        private interface IFailingModule { }
        private interface IFollowingModule { }
        private interface IFailingInitModule { }
        private interface IUpdatingModule { }
        private interface IShutdownDuringUpdateModule { }
        private interface IEarlierModuleAlias { }
        private interface IEarlierModule { }
        private interface ILaterQueryModule { }
        private interface IOpenDependencyModule { }

        private sealed class RecordingModule : Module, IOrderedModule, ILateModule, IFollowingModule
        {
            private readonly Action _onShutdown;

            public RecordingModule(Action onShutdown)
            {
                _onShutdown = onShutdown;
            }

            public int ShutdownCount { get; private set; }

            public override void OnInit()
            {
            }

            public override void Shutdown()
            {
                ShutdownCount++;
                _onShutdown?.Invoke();
            }
        }

        private sealed class ThrowingShutdownModule : Module, IFailingModule
        {
            private readonly List<string> _calls;

            public ThrowingShutdownModule(List<string> calls)
            {
                _calls = calls;
            }

            public int ShutdownCount { get; private set; }
            public bool ReentrantShutdownReturned { get; private set; }
            public bool LateRegistrationRejected { get; private set; }

            public override void OnInit()
            {
            }

            public override void Shutdown()
            {
                ShutdownCount++;
                _calls.Add("failing");
                ModuleSystem.Shutdown();
                ReentrantShutdownReturned = true;
                try
                {
                    ModuleSystem.RegisterModule<ILateModule>(new RecordingModule(null));
                }
                catch (GameFrameworkException)
                {
                    LateRegistrationRejected = true;
                }

                throw new InvalidOperationException("module shutdown failure");
            }
        }

        private sealed class FailingInitModule : Module, IFailingInitModule
        {
            public int OnInitCount { get; private set; }
            public int ShutdownCount { get; private set; }

            public override void OnInit()
            {
                OnInitCount++;
                throw new InvalidOperationException("partial initialization failure");
            }

            public override void Shutdown()
            {
                ShutdownCount++;
            }
        }

        private sealed class UpdatingModule : Module, IUpdatingModule, IUpdateModule
        {
            public int UpdateCount { get; private set; }

            public override void OnInit()
            {
            }

            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                UpdateCount++;
            }

            public override void Shutdown()
            {
            }
        }

        private sealed class ShutdownDuringUpdateModule : Module, IShutdownDuringUpdateModule, IUpdateModule
        {
            public int UpdateCount { get; private set; }

            public override void OnInit()
            {
            }

            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                UpdateCount++;
                ModuleSystem.Shutdown();
            }

            public override void Shutdown()
            {
            }
        }

        private sealed class EarlierQueryModule : Module, IEarlierModuleAlias, IEarlierModule
        {
            public override int Priority => 0;
            public int ShutdownCount { get; private set; }

            public override void OnInit()
            {
            }

            public override void Shutdown()
            {
                ShutdownCount++;
            }
        }

        private sealed class LaterQueryModule : Module, ILaterQueryModule
        {
            public override int Priority => 1;
            public int ShutdownCount { get; private set; }
            public bool DirectAliasLookupMissing { get; private set; }
            public bool ListAliasLookupMissing { get; private set; }
            public bool GetClosedAliasRejected { get; private set; }
            public bool OpenDependencyAvailable { get; private set; }
            public bool OpenDependencyGetAvailable { get; private set; }

            public override void OnInit()
            {
            }

            public override void Shutdown()
            {
                ShutdownCount++;
                DirectAliasLookupMissing = ModuleSystem.TryGetExistingModule<IEarlierModuleAlias>() == null;
                ListAliasLookupMissing = ModuleSystem.TryGetExistingModule<IEarlierModule>() == null;

                try
                {
                    ModuleSystem.GetModule<IEarlierModuleAlias>();
                }
                catch (GameFrameworkException)
                {
                    GetClosedAliasRejected = true;
                }

                OpenDependencyAvailable = ModuleSystem.TryGetExistingModule<IOpenDependencyModule>() != null;
                OpenDependencyGetAvailable = ModuleSystem.GetModule<IOpenDependencyModule>() != null;
            }
        }

        private sealed class OpenDependencyModule : Module, IOpenDependencyModule
        {
            public override int Priority => 2;
            public int ShutdownCount { get; private set; }

            public override void OnInit()
            {
            }

            public override void Shutdown()
            {
                ShutdownCount++;
            }
        }
    }
}
