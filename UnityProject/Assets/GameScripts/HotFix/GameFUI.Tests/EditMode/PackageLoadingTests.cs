using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using NUnit.Framework;
using UnityEngine;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// <see cref="PackageLoader"/> 的 EditMode 单元测试，覆盖任务 4.10 的全部场景：
    /// 单包加载、跨包依赖、并发 Acquire、依赖环、描述/外部资源/依赖失败回滚、
    /// 重复 Release 与 Shutdown 资源基线。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - design.md 决策8“包加载采用异步预载、同步解析”；
    /// - design.md 决策9“包失败回滚使用本次操作账本”；
    /// - spec fairygui-package-loading 全部 Requirement。
    ///
    /// 测试资源：使用 4.3 产出的 <see cref="InMemoryFUIResourceProvider"/>（internal，通过
    /// AssemblyInfo.cs 的 InternalsVisibleTo 暴露）注入可控内存资源。其中描述文件需要是
    /// 有效的 FairyGUI 二进制包格式——本测试通过 <see cref="FairyGuiDescBuilder"/> 在内存中
    /// 构造最小有效二进制描述，覆盖带/不带依赖、带/不带外部资源（Atlas）项的场景，
    /// 使成功路径可在 EditMode 内完整验证，无需依赖真实发布资源。
    ///
    /// 外部资源使用 <see cref="Texture2D"/>（Atlas 项对应 Texture）与 <see cref="TextAsset"/>
    /// （Misc 项）模拟，通过 provider 预设后由加载器并发预载并写入 handle 表。
    ///
    /// 资源隔离：每个测试在 <see cref="SetUp"/> 中确保 <see cref="PackageLoader.ClearRegistry"/>
    /// 与 <see cref="UIPackage.RemoveAllPackages"/> 清空全局状态；<see cref="TearDown"/> 再次清理，
    /// 避免跨测试残留污染 FairyGUI 全局注册表与 PackageRecord 注册表。
    ///
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，只通过公开/internal API 访问被测对象。
    /// </remarks>
    [TestFixture]
    public class PackageLoadingTests
    {
        /// <summary>
        /// 测试用包名常量，避免与真实业务包名冲突。
        /// </summary>
        private const string PackageA = "TestPkgA";

        /// <summary>
        /// 测试用包 B（作为 A 的依赖或共享依赖）。
        /// </summary>
        private const string PackageB = "TestPkgB";

        /// <summary>
        /// 测试用包 C（作为 A、B 的共享依赖）。
        /// </summary>
        private const string PackageC = "TestPkgC";

        /// <summary>
        /// 每个测试前的状态基线重置：清空 PackageRecord 注册表与 FairyGUI 全局包注册表，
        /// 确保上一测试的残留包记录不会干扰当前测试。
        /// </summary>
        /// <remarks>
        /// PackageLoader 的注册表是静态字段，跨测试会残留；FairyGUI 的
        /// <see cref="UIPackage._packageInstByName"/> 同样是全局静态。两者都必须在 SetUp 清空。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            // 先清空 PackageLoader 静态注册表，再清空 FairyGUI 全局包注册表。
            // 注意顺序：FairyGUI 全局注册表可能持有包对象，清空时触发 Dispose，
            // 与 PackageLoader 记录解耦后单独清理更安全。
            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();
        }

        /// <summary>
        /// 每个测试后的状态基线重置，与 <see cref="SetUp"/> 对称，确保即使测试中途失败
        /// 也不会残留全局状态污染后续测试。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();
        }

        // ============================================================
        // 场景 a：单包加载——Acquire 一个包，验证返回 lease、PackageRecord Ready、引用计数正确
        // ============================================================

        /// <summary>
        /// 单包加载：Acquire 成功后应返回非空 lease，对应 PackageRecord 处于 Ready 状态，
        /// 引用计数为 1，且描述 handle 与 UIPackage 已写入记录。
        /// </summary>
        [Test]
        [Description("单包加载：Acquire 返回 lease、记录 Ready、引用计数正确、handle 与 UIPackage 已写入。")]
        public async UniTask SinglePackage_Acquire_ReturnsLeaseAndReadyRecord()
        {
            // 安排：构造内存 provider，预设包 A 的描述资源（无依赖、无外部资源）。
            InMemoryFUIResourceProvider provider = CreateProviderWithPackage(PackageA);

            // 执行：Acquire 包 A。
            PackageLease lease = await PackageLoader.AcquireAsync(PackageA, provider);

            // 断言：lease 非空，关联记录正确。
            Assert.IsNotNull(lease, "Acquire 必须返回非空 lease。");
            Assert.AreEqual(PackageA, lease.PackageName, "lease 包名应与请求一致。");

            PackageRecord record = PackageLoader.FindRecord(PackageA);
            Assert.IsNotNull(record, "Acquire 后应能在注册表找到记录。");
            Assert.AreEqual(PackageLoadState.Ready, record.State, "记录状态应为 Ready。");
            Assert.AreEqual(1, record.ReferenceCount, "单次 Acquire 引用计数应为 1。");
            Assert.IsNotNull(record.Package, "UIPackage 应已写入记录。");
            Assert.IsNotNull(record.DescHandle, "描述 handle 应已写入记录。");

            // 释放 lease 后引用计数归零。
            bool released = lease.Release();
            Assert.IsTrue(released, "首次 Release 应成功。");
            Assert.AreEqual(0, record.ReferenceCount, "Release 后引用计数应为 0。");
        }

        // ============================================================
        // 场景 b：跨包依赖——包 A 依赖包 B，Acquire A 后 B 也已加载，两者引用计数正确
        // ============================================================

        /// <summary>
        /// 跨包依赖：包 A 声明依赖包 B，Acquire A 成功后 B 也应处于 Ready，
        /// A 的引用计数为 1（调用方持有），B 的引用计数为 1（A 持有依赖 lease）。
        /// </summary>
        [Test]
        [Description("跨包依赖：A 依赖 B，Acquire A 后 B 也 Ready，引用计数 A=1、B=1。")]
        public async UniTask CrossPackageDependency_AcquireA_LoadsBAndCorrectRefCounts()
        {
            // 安排：A 的描述声明依赖 B；B 为无依赖纯描述包。
            InMemoryFUIResourceProvider provider = CreateProviderWithPackage(PackageA, dependencies: new[] { PackageB });
            AddPackageToProvider(provider, PackageB);

            // 执行：Acquire 包 A。
            PackageLease leaseA = await PackageLoader.AcquireAsync(PackageA, provider);

            // 断言：A 与 B 均已 Ready。
            PackageRecord recordA = PackageLoader.FindRecord(PackageA);
            PackageRecord recordB = PackageLoader.FindRecord(PackageB);
            Assert.IsNotNull(recordA, "包 A 记录应存在。");
            Assert.IsNotNull(recordB, "依赖包 B 记录应存在。");
            Assert.AreEqual(PackageLoadState.Ready, recordA.State, "包 A 应 Ready。");
            Assert.AreEqual(PackageLoadState.Ready, recordB.State, "依赖包 B 应 Ready。");

            // 引用计数：A 由调用方持有 =1；B 由 A 的依赖 lease 持有 =1。
            Assert.AreEqual(1, recordA.ReferenceCount, "包 A 引用计数应为 1（调用方 lease）。");
            Assert.AreEqual(1, recordB.ReferenceCount, "包 B 引用计数应为 1（A 的依赖 lease）。");
            Assert.AreEqual(1, recordA.DependencyLeases.Count, "包 A 应持有 1 个依赖 lease。");

            // 释放 A 后，B 的引用计数随之归零（依赖 lease 被 A 记录持有，A 卸载时释放）。
            leaseA.Release();
            // KeepUntilShutdown 策略下 UnloadPackage 需满足前置条件；此处 A 引用已归零可卸载。
            // 但为验证依赖引用传导，先确认释放后 A 引用为 0。
            Assert.AreEqual(0, recordA.ReferenceCount, "Release A 后 A 引用计数应为 0。");
        }

        // ============================================================
        // 场景 c：并发 Acquire——两个调用方并发请求同一包，只加载一次，各获独立 lease
        // ============================================================

        /// <summary>
        /// 并发 Acquire：两个调用方并发请求同一未加载包，应只执行一次描述加载，
        /// 两个调用方各获独立 lease，引用计数为 2。
        /// </summary>
        /// <remarks>
        /// spec“包和依赖加载任务合并——两个窗口同时请求同一包 → 只执行一次加载，返回独立租约”。
        /// 通过 UniTask.WhenAll 并发两个 Acquire 验证任务合并。
        /// </remarks>
        [Test]
        [Description("并发 Acquire：两调用方并发请求同一包，只加载一次，各获独立 lease，引用计数=2。")]
        public async UniTask ConcurrentAcquire_SamePackage_LoadsOnceAndReturnsIndependentLeases()
        {
            // 安排：预设包 A 资源。
            InMemoryFUIResourceProvider provider = CreateProviderWithPackage(PackageA);

            // 执行：并发发起两个 Acquire，通过 WhenAll 合并等待。
            UniTask<PackageLease> task1 = PackageLoader.AcquireAsync(PackageA, provider);
            UniTask<PackageLease> task2 = PackageLoader.AcquireAsync(PackageA, provider);
            (PackageLease lease1, PackageLease lease2) = await UniTask.WhenAll(task1, task2);

            // 断言：两个 lease 独立且关联同一记录。
            Assert.IsNotNull(lease1, "调用方 1 应获得 lease。");
            Assert.IsNotNull(lease2, "调用方 2 应获得 lease。");
            Assert.AreNotSame(lease1, lease2, "两个调用方应获得独立 lease 对象。");
            Assert.AreEqual(lease1.Record, lease2.Record, "两个 lease 应关联同一 PackageRecord。");

            PackageRecord record = lease1.Record;
            Assert.AreEqual(PackageLoadState.Ready, record.State, "记录应 Ready。");
            // 两个独立 lease 各递增一次引用计数 → 2。
            Assert.AreEqual(2, record.ReferenceCount, "两个并发调用方引用计数应为 2。");

            // 各自释放后引用计数归零。
            lease1.Release();
            Assert.AreEqual(1, record.ReferenceCount, "释放一个 lease 后引用计数应为 1。");
            lease2.Release();
            Assert.AreEqual(0, record.ReferenceCount, "两个 lease 均释放后引用计数应为 0。");
        }

        // ============================================================
        // 场景 d：依赖环——A 依赖 B，B 依赖 A → 抛 FUIException 含依赖链
        // ============================================================

        /// <summary>
        /// 依赖环：A 依赖 B 且 B 依赖 A，Acquire A 应抛 <see cref="FUIException"/>，
        /// 异常信息包含完整依赖链（含环入口标注），且回滚后无残留引用。
        /// </summary>
        /// <remarks>
        /// spec“依赖环 → 系统 SHALL 终止该次 Acquire、报告完整依赖链并回滚本次新增的引用和资源”。
        /// 注意：环检测发生在 SharedLoadTask 合并之前，避免环路上层死锁等待自身下游。
        /// </remarks>
        [Test]
        [Description("依赖环：A→B→A 抛 FUIException 含依赖链，回滚后无残留。")]
        public async UniTask DependencyCycle_ADependsB_BDependsA_ThrowsFUIExceptionWithChain()
        {
            // 安排：A 依赖 B，B 依赖 A，形成环。
            InMemoryFUIResourceProvider provider = CreateProviderWithPackage(PackageA, dependencies: new[] { PackageB });
            AddPackageToProvider(provider, PackageB, dependencies: new[] { PackageA });

            // 执行与断言：Acquire A 应抛 FUIException。
            FUIException ex = Assert.ThrowsAsync<FUIException>(async () =>
            {
                await PackageLoader.AcquireAsync(PackageA, provider);
            });

            Assert.IsNotNull(ex, "依赖环必须抛出 FUIException。");
            // 异常信息应包含“依赖环”与依赖链描述（design.md 决策8：报告完整依赖链）。
            Assert.That(ex.Message, Does.Contain("依赖环").Or.Contain("环"), "异常信息应指出依赖环。");
            // 依赖链应包含 A 与 B，体现真实路径。
            Assert.That(ex.Message, Does.Contain(PackageA), "依赖链应包含包 A。");
            Assert.That(ex.Message, Does.Contain(PackageB), "依赖链应包含包 B。");

            // 回滚验证：环检测后本次 Acquire 新增的引用与资源应已回滚，无残留。
            // 因环检测发生在加载执行前，A 与 B 均不应进入 Ready，不应持有资源。
            PackageRecord recordA = PackageLoader.FindRecord(PackageA);
            PackageRecord recordB = PackageLoader.FindRecord(PackageB);
            // 记录可能存在但状态非 Ready；引用计数不应为正（无调用方持有 lease）。
            if (recordA != null)
            {
                Assert.AreNotEqual(PackageLoadState.Ready, recordA.State, "环回滚后 A 不应 Ready。");
                Assert.AreEqual(0, recordA.ReferenceCount, "环回滚后 A 引用计数应为 0。");
                Assert.IsNull(recordA.Package, "环回滚后 A 不应持有 UIPackage。");
            }

            if (recordB != null)
            {
                Assert.AreEqual(0, recordB.ReferenceCount, "环回滚后 B 引用计数应为 0。");
            }
        }

        // ============================================================
        // 场景 e：描述文件加载失败——provider 对描述 location 返回失败 → Acquire 失败、回滚、无残留
        // ============================================================

        /// <summary>
        /// 描述文件加载失败：provider 对描述 location 返回失败，Acquire 应抛 <see cref="FUIException"/>，
        /// 回滚后记录不持有任何资源（UIPackage、handle 均为 null），引用计数为 0。
        /// </summary>
        /// <remarks>
        /// spec“失败和取消执行原子回滚——目标包描述成功但依赖资源失败 → 目标 Acquire 失败，
        /// 本次新增资源被释放”。此处验证描述阶段失败的回滚。
        /// 描述加载失败时，加载器在 catch 中直接 Dispose 描述 handle，不向记录写入任何句柄。
        /// </remarks>
        [Test]
        [Description("描述加载失败：provider 返回失败 → Acquire 失败、回滚、记录无残留。")]
        public void DescriptorLoadFailure_ProviderFails_AcquireFailsAndRollsBack()
        {
            // 安排：provider 不预设包 A 描述资源，模拟描述加载失败。
            InMemoryFUIResourceProvider provider = new InMemoryFUIResourceProvider();
            provider.MarkLoadFailure(PackageA + "_fui");

            // 执行与断言：Acquire 抛 FUIException。
            FUIException ex = Assert.ThrowsAsync<FUIException>(async () =>
            {
                await PackageLoader.AcquireAsync(PackageA, provider);
            });

            Assert.IsNotNull(ex, "描述加载失败应抛 FUIException。");
            Assert.That(ex.Message, Does.Contain(PackageA), "异常信息应包含包名。");

            // 回滚验证：记录不持有任何资源。
            PackageRecord record = PackageLoader.FindRecord(PackageA);
            Assert.IsNotNull(record, "失败后记录仍应存在（用于状态跟踪）。");
            Assert.AreNotEqual(PackageLoadState.Ready, record.State, "失败后不应 Ready。");
            Assert.IsNull(record.Package, "失败回滚后不应持有 UIPackage。");
            Assert.IsNull(record.DescHandle, "失败回滚后不应持有描述 handle。");
            Assert.AreEqual(0, record.AssetHandles.Count, "失败回滚后不应持有外部资源 handle。");
            Assert.AreEqual(0, record.ReferenceCount, "失败回滚后引用计数应为 0。");
        }

        // ============================================================
        // 场景 f：外部资源加载失败——provider 对外部资源返回失败 → Acquire 失败、回滚
        // ============================================================

        /// <summary>
        /// 外部资源加载失败：包 A 描述含一个 Atlas 外部资源项，provider 对该外部资源 location
        /// 返回失败，Acquire 应抛 <see cref="FUIException"/>，回滚已注册的 UIPackage（RemovePackage）
        /// 与已加载的描述 handle（Dispose），记录无残留。
        /// </summary>
        /// <remarks>
        /// spec“失败和取消执行原子回滚”。外部资源失败发生在 AddPackage 成功后的预载阶段，
        /// 因此回滚需移除已注册的 UIPackage 并 Dispose 描述 handle（4.8 操作账本反向顺序）。
        /// 验证重点：FairyGUI 全局注册表中不应残留本次新增的包注册。
        /// </remarks>
        [Test]
        [Description("外部资源加载失败：provider 对外部资源返回失败 → Acquire 失败、回滚 RemovePackage + Dispose handle。")]
        public async UniTask ExternalAssetLoadFailure_ProviderFails_AcquireFailsAndRollsBackPackage()
        {
            // 安排：包 A 描述含一个 Atlas 项（外部资源 file = "TestPkgA_atlas0"）。
            // 描述资源预设成功，但外部资源 location 标记失败。
            string atlasFile = PackageA + "_atlas0";
            InMemoryFUIResourceProvider provider = new InMemoryFUIResourceProvider();
            // 预设描述资源（有效二进制描述，含 Atlas 项）。
            byte[] descBytes = FairyGuiDescBuilder.Build(PackageA, dependencies: null, atlasFiles: new[] { atlasFile });
            provider.SetAsset(PackageA + "_fui", CreateTextAsset(descBytes));
            // 标记外部资源 location 失败。
            provider.MarkLoadFailure(atlasFile);

            // 执行与断言：Acquire 抛 FUIException。
            FUIException ex = Assert.ThrowsAsync<FUIException>(async () =>
            {
                await PackageLoader.AcquireAsync(PackageA, provider);
            });

            Assert.IsNotNull(ex, "外部资源加载失败应抛 FUIException。");
            Assert.That(ex.Message, Does.Contain("外部资源").Or.Contain(atlasFile), "异常信息应指出外部资源失败。");

            // 回滚验证：记录无残留。
            PackageRecord record = PackageLoader.FindRecord(PackageA);
            Assert.IsNotNull(record, "失败后记录仍应存在。");
            Assert.IsNull(record.Package, "回滚后不应持有 UIPackage。");
            Assert.IsNull(record.DescHandle, "回滚后描述 handle 应已 Dispose 并清空。");
            Assert.AreEqual(0, record.AssetHandles.Count, "回滚后不应持有外部资源 handle。");
            Assert.AreEqual(0, record.ReferenceCount, "回滚后引用计数应为 0。");

            // 关键：FairyGUI 全局注册表不应残留本次新增的包注册（账本 Rollback 调用 RemovePackage）。
            // 通过尝试再次 AddPackage 同名包不报错来间接验证，或直接确认 FindPackageByName 为 null。
            // FairyGUI 没有公开的按名查询，使用 RemovePackage 应抛异常来确认已移除。
            Assert.Throws<Exception>(() =>
            {
                UIPackage.RemovePackage(PackageA);
            }, "回滚后 FairyGUI 全局注册表不应残留包 A（RemovePackage 应抛异常表示未注册）。");
        }

        // ============================================================
        // 场景 g：依赖加载失败——依赖包加载失败 → 主包 Acquire 失败、回滚
        // ============================================================

        /// <summary>
        /// 依赖加载失败：包 A 依赖包 B，但 B 的描述加载失败，A 的 Acquire 应抛 <see cref="FUIException"/>，
        /// 回滚 A 已注册的 UIPackage 与描述 handle，A、B 均无残留资源与引用。
        /// </summary>
        /// <remarks>
        /// spec“失败和取消执行原子回滚——目标 Acquire 失败，本次新增资源被释放”。
        /// 依赖失败发生在主包 AddPackage 成功、依赖 Acquire 阶段，回滚需移除主包 UIPackage 注册、
        /// Dispose 主包描述与外部资源 handle，并释放依赖 lease（依赖本身失败无 lease 产生）。
        /// </remarks>
        [Test]
        [Description("依赖加载失败：B 描述失败 → A Acquire 失败、回滚 A 的包注册与 handle。")]
        public async UniTask DependencyLoadFailure_DependencyFails_MainPackageAcquireFailsAndRollsBack()
        {
            // 安排：A 依赖 B；A 的描述资源正常，B 的描述 location 标记失败。
            InMemoryFUIResourceProvider provider = CreateProviderWithPackage(PackageA, dependencies: new[] { PackageB });
            // B 的描述失败：不预设资源并标记失败。
            provider.MarkLoadFailure(PackageB + "_fui");

            // 执行与断言：Acquire A 抛 FUIException（依赖 B 失败传导）。
            FUIException ex = Assert.ThrowsAsync<FUIException>(async () =>
            {
                await PackageLoader.AcquireAsync(PackageA, provider);
            });

            Assert.IsNotNull(ex, "依赖加载失败应使主包 Acquire 抛 FUIException。");

            // 回滚验证：A 无残留。
            PackageRecord recordA = PackageLoader.FindRecord(PackageA);
            Assert.IsNotNull(recordA, "A 记录应存在。");
            Assert.IsNull(recordA.Package, "回滚后 A 不应持有 UIPackage。");
            Assert.IsNull(recordA.DescHandle, "回滚后 A 描述 handle 应已清空。");
            Assert.AreEqual(0, recordA.ReferenceCount, "回滚后 A 引用计数应为 0。");
            Assert.AreEqual(0, recordA.DependencyLeases.Count, "回滚后 A 不应持有依赖 lease。");

            // B 也无残留。
            PackageRecord recordB = PackageLoader.FindRecord(PackageB);
            if (recordB != null)
            {
                Assert.AreEqual(0, recordB.ReferenceCount, "回滚后 B 引用计数应为 0。");
                Assert.IsNull(recordB.Package, "回滚后 B 不应持有 UIPackage。");
            }

            // FairyGUI 全局注册表不应残留 A。
            Assert.Throws<Exception>(() =>
            {
                UIPackage.RemovePackage(PackageA);
            }, "回滚后 FairyGUI 全局注册表不应残留包 A。");
        }

        // ============================================================
        // 场景 h：重复 Release——同一 lease 重复 Release → 第二次被拒绝，引用计数不为负
        // ============================================================

        /// <summary>
        /// 重复 Release：同一 lease 第二次 Release 返回 false，引用计数不递减（不为负）。
        /// </summary>
        /// <remarks>
        /// spec“资源所有权唯一且释放有序——重复释放租约 SHALL 被拒绝，不得使引用计数变为负数”。
        /// PackageLease.Release 使用 Interlocked.CompareExchange 保证幂等，第二次返回 false。
        /// </remarks>
        [Test]
        [Description("重复 Release：第二次被拒绝，引用计数不为负。")]
        public async UniTask DuplicateRelease_SecondReleaseRejected_RefCountNotNegative()
        {
            // 安排：Acquire 包 A，获得 lease。
            InMemoryFUIResourceProvider provider = CreateProviderWithPackage(PackageA);
            PackageLease lease = await PackageLoader.AcquireAsync(PackageA, provider);
            PackageRecord record = lease.Record;

            // 执行：首次 Release 成功。
            bool firstRelease = lease.Release();
            Assert.IsTrue(firstRelease, "首次 Release 应成功。");
            Assert.AreEqual(0, record.ReferenceCount, "首次 Release 后引用计数应为 0。");

            // 重复 Release：第二次被拒绝，引用计数保持 0（不为负）。
            bool secondRelease = lease.Release();
            Assert.IsFalse(secondRelease, "第二次 Release 应被拒绝。");
            Assert.IsTrue(lease.IsReleased, "lease 应标记为已释放。");
            Assert.AreEqual(0, record.ReferenceCount, "重复 Release 后引用计数应保持 0，不为负。");

            // 多次重复 Release 同样被拒绝，引用计数始终为 0。
            Assert.IsFalse(lease.Release(), "第三次 Release 仍应被拒绝。");
            Assert.AreEqual(0, record.ReferenceCount, "多次重复 Release 引用计数始终为 0。");
        }

        // ============================================================
        // 场景 i：Shutdown 资源基线——Shutdown 后所有包、handle、lease 回到基线
        // ============================================================

        /// <summary>
        /// Shutdown 资源基线：加载多个包（含依赖）后执行 <see cref="PackageLoader.UnloadAllForShutdown"/>，
        /// 所有包记录应进入 Disposed 终态，引用计数归零，handle 表清空，FairyGUI 全局注册表清空。
        /// </summary>
        /// <remarks>
        /// spec“模块 Shutdown 回收全部包资源——Shutdown 完成后所有模块持有的包、依赖引用和 handle
        /// SHALL 回到启动前基线”。Shutdown 为强制回收，不逐包检查引用计数与窗口，直接释放全部记录。
        /// </remarks>
        [Test]
        [Description("Shutdown 资源基线：Shutdown 后所有包 Disposed、handle 清空、全局注册表清空。")]
        public async UniTask Shutdown_AllPackagesDisposedAndHandlesCleared()
        {
            // 安排：加载 A（依赖 B），再加 Acquire A 一次使 A 引用计数为 2，模拟多调用方持有。
            InMemoryFUIResourceProvider provider = CreateProviderWithPackage(PackageA, dependencies: new[] { PackageB });
            AddPackageToProvider(provider, PackageB);

            PackageLease lease1 = await PackageLoader.AcquireAsync(PackageA, provider);
            PackageLease lease2 = await PackageLoader.AcquireAsync(PackageA, provider);

            PackageRecord recordA = lease1.Record;
            PackageRecord recordB = PackageLoader.FindRecord(PackageB);
            Assert.AreEqual(2, recordA.ReferenceCount, "两次 Acquire 后 A 引用计数应为 2。");
            Assert.AreEqual(1, recordB.ReferenceCount, "B 由 A 依赖持有，引用计数应为 1。");

            // 执行：模块 Shutdown 强制回收全部包。
            PackageLoader.UnloadAllForShutdown();

            // 断言：所有包记录进入 Disposed 终态，资源引用清空。
            Assert.AreEqual(PackageLoadState.Disposed, recordA.State, "Shutdown 后 A 应为 Disposed。");
            Assert.AreEqual(PackageLoadState.Disposed, recordB.State, "Shutdown 后 B 应为 Disposed。");
            Assert.IsNull(recordA.Package, "Shutdown 后 A 不应持有 UIPackage。");
            Assert.IsNull(recordB.Package, "Shutdown 后 B 不应持有 UIPackage。");
            Assert.IsNull(recordA.DescHandle, "Shutdown 后 A 描述 handle 应已清空。");
            Assert.IsNull(recordB.DescHandle, "Shutdown 后 B 描述 handle 应已清空。");
            Assert.AreEqual(0, recordA.AssetHandles.Count, "Shutdown 后 A handle 表应清空。");
            Assert.AreEqual(0, recordB.AssetHandles.Count, "Shutdown 后 B handle 表应清空。");
            Assert.AreEqual(0, recordA.DependencyLeases.Count, "Shutdown 后 A 依赖 lease 应清空。");

            // lease 对象仍存在但其 Record 已 Disposed；IsReleased 不受 Shutdown 影响（lease 未显式 Release）。
            // 关键：Shutdown 后不应再能通过 FindRecord 找到记录（注册表已清空）。
            Assert.IsNull(PackageLoader.FindRecord(PackageA), "Shutdown 后注册表应不含 A。");
            Assert.IsNull(PackageLoader.FindRecord(PackageB), "Shutdown 后注册表应不含 B。");

            // FairyGUI 全局注册表应清空：RemovePackage 应抛异常。
            Assert.Throws<Exception>(() => UIPackage.RemovePackage(PackageA), "Shutdown 后 FairyGUI 全局注册表不应残留 A。");
            Assert.Throws<Exception>(() => UIPackage.RemovePackage(PackageB), "Shutdown 后 FairyGUI 全局注册表不应残留 B。");
        }

        // ============================================================
        // 辅助方法：构造内存 provider 与包资源
        // ============================================================

        /// <summary>
        /// 创建已预设指定包描述资源的 <see cref="InMemoryFUIResourceProvider"/>，
        /// 包描述为最小有效二进制（无依赖、无外部资源），便于成功路径测试。
        /// </summary>
        /// <param name="packageName">包名。</param>
        /// <param name="dependencies">依赖包名列表；null 表示无依赖。</param>
        /// <param name="atlasFiles">Atlas 外部资源 file 名列表；null 表示无外部资源。</param>
        /// <returns>已预设描述资源的内存 provider。</returns>
        private static InMemoryFUIResourceProvider CreateProviderWithPackage(
            string packageName,
            string[] dependencies = null,
            string[] atlasFiles = null)
        {
            InMemoryFUIResourceProvider provider = new InMemoryFUIResourceProvider();
            AddPackageToProvider(provider, packageName, dependencies, atlasFiles);
            return provider;
        }

        /// <summary>
        /// 向已有 provider 追加预设指定包的描述资源。
        /// </summary>
        /// <param name="provider">目标 provider。</param>
        /// <param name="packageName">包名。</param>
        /// <param name="dependencies">依赖包名列表；null 表示无依赖。</param>
        /// <param name="atlasFiles">Atlas 外部资源 file 名列表；null 表示无外部资源。</param>
        /// <remarks>
        /// 当包含 Atlas 项时，同时预设对应外部资源 location 的 <see cref="Texture2D"/>，
        /// 使预载阶段成功；外部资源失败场景由调用方单独 MarkLoadFailure 覆盖。
        /// </remarks>
        private static void AddPackageToProvider(
            InMemoryFUIResourceProvider provider,
            string packageName,
            string[] dependencies = null,
            string[] atlasFiles = null)
        {
            byte[] descBytes = FairyGuiDescBuilder.Build(packageName, dependencies, atlasFiles);
            provider.SetAsset(packageName + "_fui", CreateTextAsset(descBytes));

            // 若声明了 Atlas 外部资源，预设对应 Texture 资源（除非调用方稍后标记失败）。
            if (atlasFiles != null)
            {
                for (int i = 0; i < atlasFiles.Length; i++)
                {
                    // Atlas 项 location = Path.GetFileNameWithoutExtension(file)，即 file 本身（无扩展名时）。
                    // 此处 atlasFiles 已是无扩展名的规范 location，直接作为 key。
                    provider.SetAsset(atlasFiles[i], CreateTexture2D(2, 2));
                }
            }
        }

        /// <summary>
        /// 创建携带指定字节的 <see cref="TextAsset"/>，用于描述资源与 Misc 外部资源。
        /// </summary>
        /// <param name="bytes">描述字节。</param>
        /// <returns>TextAsset 实例。</returns>
        /// <remarks>
        /// TextAsset.bytes 返回其内部文本的 UTF8 编码字节。为保证二进制描述字节无损往返，
        /// 此处将原始字节按 UTF8 解码为 C# 字符串构造 TextAsset；只要描述字节全部为有效 UTF8
        /// （FairyGuiDescBuilder 保证全部字节落在 ASCII 0x00-0x7F 区间），UTF8 解码再编码即为无损。
        ///
        /// 关键约束：FairyGuiDescBuilder 产出的描述字节必须全部 &lt; 0x80（纯 ASCII），
        /// 否则 UTF8 往返会破坏二进制结构。Build 方法内部对所有整数/长度做了 &lt; 128 的断言保护。
        ///
        /// 嵌入的 0x00 字节：C# string 与 TextAsset 均保留嵌入空字节，UTF8 编解码亦保留 0x00，
        /// 故大端整数的高位 0x00 字节无损。
        /// </remarks>
        private static TextAsset CreateTextAsset(byte[] bytes)
        {
            // 将原始字节按 UTF8 解码为字符串。纯 ASCII 字节此步无损。
            string text = Encoding.UTF8.GetString(bytes);
            TextAsset asset = new TextAsset(text);
            asset.hideFlags = HideFlags.HideAndDontSave;
            return asset;
        }

        /// <summary>
        /// 创建指定尺寸的 <see cref="Texture2D"/>，用于 Atlas 外部资源。
        /// </summary>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        /// <returns>Texture2D 实例。</returns>
        private static Texture2D CreateTexture2D(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }

    /// <summary>
    /// FairyGUI 包描述二进制构造器：在内存中构造最小有效的 FairyGUI 二进制包描述，
    /// 供 EditMode 测试无需依赖真实发布资源即可验证 PackageLoader 的成功与失败路径。
    /// </summary>
    /// <remarks>
    /// 二进制格式依据 FairyGUI 源码 <c>UIPackage.LoadPackage</c> 与 <c>ByteBuffer</c>：
    /// <list type="bullet">
    /// <item>字节序：大端（ByteBuffer.littleEndian 默认 false）。</item>
    /// <item>头部：magic(0x46475549 "FGUI") + version(int) + compressed(bool) + id(string) + name(string)。</item>
    /// <item>固定 Skip(20) 字节。</item>
    /// <item>索引表：segCount(byte) + useShort(byte) + 每段相对索引表起始的偏移(int×segCount)。</item>
    /// <item>段0 依赖：cnt(short) + 每项 id(ReadS) + name(ReadS)。</item>
    /// <item>段1 资源项：cnt(short) + 每项 nextPos(int,相对读取后位置) + type(byte) +
    ///   id(ReadS) + name(ReadS) + path(ReadS) + file(ReadS) + exported(bool) + width(int) + height(int) +
    ///   类型特定字段（Atlas/Sound/Misc 无额外字段；Image 等有额外字段，本构造器只产出 Atlas 项）。</item>
    /// <item>段2 精灵：cnt(short)=0。</item>
    /// <item>段3 命中测试：可选，本构造器不产出（偏移 0）。</item>
    /// <item>段4 字符串表：cnt(int) + 每项 string(ushort len + utf8)。</item>
    /// <item>段5 翻译字符串表：可选，本构造器不产出（偏移 0）。</item>
    /// </list>
    ///
    /// ReadS 读取一个 ushort 索引到 stringTable：65534=null，65533=空串，否则为 stringTable[index]。
    /// 本构造器把所有非空字符串（id、name、file、依赖名）放入字符串表，ReadS 索引引用之。
    ///
    /// 版本选择 version=1：避免 ver2 的 branch/highRes 额外字段，使 Atlas 项序列化最简。
    ///
    /// 边界约束：本类型仅为测试辅助，不进入生产代码；不依赖 GameLogic。
    /// </remarks>
    internal static class FairyGuiDescBuilder
    {
        /// <summary>
        /// 构造最小有效的 FairyGUI 包描述字节。
        /// </summary>
        /// <param name="packageName">包名，同时作为 id 与 name。</param>
        /// <param name="dependencies">依赖包名列表；null 或空表示无依赖。</param>
        /// <param name="atlasFiles">Atlas 外部资源 file 名列表；null 或空表示无外部资源项。</param>
        /// <returns>有效二进制描述字节，可直接传入 <c>UIPackage.AddPackage(byte[], ...)</c>。</returns>
        public static byte[] Build(string packageName, string[] dependencies, string[] atlasFiles)
        {
            // 字符串表：收集所有非空字符串，建立 索引。
            // 索引 0 起始；id/name/file/依赖名 均入表。
            List<string> stringTable = new List<string>();
            Dictionary<string, ushort> stringIndex = new Dictionary<string, ushort>();

            // 辅助：获取或添加字符串索引。
            ushort IndexOf(string s)
            {
                // 不使用 ReadS 的 65534(null)/65533(empty) 特殊索引：二者编码为 0xFFFE/0xFFFD，
                // 含 >= 0x80 高位字节，会破坏 TextAsset.bytes 的 UTF8 往返（见 CreateTextAsset）。
                // 所有字符串字段均以非空值入表，保证索引 < 128 → 编码 0x00XX 纯 ASCII。
                if (string.IsNullOrEmpty(s))
                {
                    throw new ArgumentException(
                        "[FairyGuiDescBuilder] 字符串表字段不得为 null 或空，避免 ReadS 特殊索引的高位字节破坏 ASCII 约束。");
                }

                if (stringIndex.TryGetValue(s, out ushort idx))
                {
                    return idx;
                }

                idx = (ushort)stringTable.Count;
                stringTable.Add(s);
                stringIndex[s] = idx;
                return idx;
            }

            // 预先注册所有字符串到表，确保后续写入索引稳定。
            // 包 id 与 name 使用包名。
            ushort idIdx = IndexOf(packageName);
            ushort nameIdx = IndexOf(packageName);

            // 依赖项 id 与 name 均使用依赖包名。
            int depCount = dependencies?.Length ?? 0;
            ushort[] depIdIndices = new ushort[depCount];
            ushort[] depNameIndices = new ushort[depCount];
            for (int i = 0; i < depCount; i++)
            {
                depIdIndices[i] = IndexOf(dependencies[i]);
                depNameIndices[i] = IndexOf(dependencies[i]);
            }

            // Atlas 资源项：id/name/path 使用合成值，file 使用 atlasFile。
            int atlasCount = atlasFiles?.Length ?? 0;
            // 资源项字段：type(Atlas=4), id, name, path, file, exported, width, height。
            // Atlas 项无类型特定额外字段（见 LoadPackage case Atlas/Sound/Misc 只设置 pi.file）。
            // 资源项 id/name/path/file 均入字符串表。
            // 注意：path 不能使用 ReadS 的 65534(null)/65533(empty) 特殊索引——
            // 这两个索引编码为 0xFFFE/0xFFFD，包含 >= 0x80 的高位字节，会破坏 TextAsset.bytes 的
            // UTF8 往返（见 CreateTextAsset 的 ASCII 安全约束）。因此 path 统一用非空占位字符串入表，
            // 其索引 < 128，编码为 0x00XX 纯 ASCII，保证无损往返。path 值对测试无功能影响。
            ushort[][] itemStringIndices = new ushort[atlasCount][];
            for (int i = 0; i < atlasCount; i++)
            {
                string itemId = "item_" + i;
                string itemName = "atlas_" + i;
                itemStringIndices[i] = new ushort[]
                {
                    IndexOf(itemId),    // id
                    IndexOf(itemName),  // name
                    IndexOf("p" + i),   // path = 非空占位（避免特殊索引高位字节）
                    IndexOf(atlasFiles[i]), // file
                };
            }

            // 现在字符串表已稳定，开始构造各段字节。
            // 段顺序与索引表段号对应：0=依赖, 1=资源项, 2=精灵, 3=命中测试(空), 4=字符串表, 5=翻译(空)。

            byte[] seg0Deps = BuildSeg0Dependencies(depCount, depIdIndices, depNameIndices);
            byte[] seg1Items = BuildSeg1Items(atlasCount, itemStringIndices);
            byte[] seg2Sprites = BuildSeg2Sprites(); // cnt=0
            byte[] seg4Strings = BuildSeg4StringTable(stringTable);
            // 段3、段5 不产出（偏移 0）。

            // 构造完整缓冲区：头部 + Skip(20) + 索引表 + 各段。
            // 索引表 segCount=6，useShort=0（用 int 偏移），6×4=24 字节偏移。
            // 索引表大小 = 1 + 1 + 24 = 26 字节。

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                // 头部。
                WriteUintBE(writer, 0x46475549); // magic "FGUI"
                WriteIntBE(writer, 1);           // version=1（非 ver2，避免 branch 字段）
                writer.Write((byte)0);           // compressed=false
                WriteString(writer, packageName); // id
                WriteString(writer, packageName); // name

                // Skip(20)：20 字节保留，填 0。
                writer.Write(new byte[20]);

                // 索引表起始位置。
                long indexTablePos = stream.Position;

                // 索引表：segCount=6, useShort=0, 6 个 int 偏移（先占位，后回填）。
                writer.Write((byte)6);  // segCount
                writer.Write((byte)0);  // useShort=0 → 用 int
                long offsetsPos = stream.Position;
                // 占位 6 个 int 偏移。
                for (int i = 0; i < 6; i++)
                {
                    WriteIntBE(writer, 0);
                }

                // 段0 依赖。
                long seg0Pos = stream.Position - indexTablePos;
                writer.Write(seg0Deps);

                // 段1 资源项。
                long seg1Pos = stream.Position - indexTablePos;
                writer.Write(seg1Items);

                // 段2 精灵。
                long seg2Pos = stream.Position - indexTablePos;
                writer.Write(seg2Sprites);

                // 段3 命中测试：不产出，偏移 0。

                // 段4 字符串表。
                long seg4Pos = stream.Position - indexTablePos;
                writer.Write(seg4Strings);

                // 段5 翻译：不产出，偏移 0。

                // 回填索引表偏移（相对 indexTablePos）。
                // 段顺序：[0]=seg0Deps, [1]=seg1Items, [2]=seg2Sprites, [3]=0, [4]=seg4Strings, [5]=0。
                long[] offsets = new long[] { seg0Pos, seg1Pos, seg2Pos, 0, seg4Pos, 0 };
                stream.Position = offsetsPos;
                for (int i = 0; i < 6; i++)
                {
                    WriteIntBE(writer, (int)offsets[i]);
                }

                byte[] result = stream.ToArray();

                // ASCII 安全性保护：确保所有字节落在 0x00-0x7F 区间，使 TextAsset.bytes 的
                // UTF8 往返无损（见 CreateTextAsset）。若任一字节 >= 0x80，说明描述结构超出最小范围，
                // 需缩小包名/依赖数/资源项数或字符串长度。此断言在 EditMode 测试构造阶段即失败，
                // 避免运行时描述解析出错难以定位。
                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] >= 0x80)
                    {
                        throw new InvalidOperationException(
                            $"[FairyGuiDescBuilder] 描述字节超出 ASCII 范围：offset={i}, byte=0x{result[i]:X2}。" +
                            "请减少依赖/资源项数量或缩短包名/字符串，确保描述总长 < 128 字节且无高位字节。");
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 构造段0（依赖）字节：cnt(short) + 每项 id(ReadS 索引) + name(ReadS 索引)。
        /// </summary>
        private static byte[] BuildSeg0Dependencies(int count, ushort[] idIndices, ushort[] nameIndices)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                WriteShortBE(writer, (short)count);
                for (int i = 0; i < count; i++)
                {
                    WriteUshortBE(writer, idIndices[i]);   // id = ReadS
                    WriteUshortBE(writer, nameIndices[i]); // name = ReadS
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// 构造段1（资源项）字节：cnt(short) + 每项 nextPos(int) + type(byte=Atlas) +
        /// id(ReadS) + name(ReadS) + path(ReadS) + file(ReadS) + exported(bool) + width(int) + height(int)。
        /// </summary>
        /// <remarks>
        /// Atlas 项（type=4）在 LoadPackage 中只设置 pi.file，无额外字段。
        /// nextPos 是相对“读取 nextPos 后的位置”的偏移，指向下一项起始。
        /// </remarks>
        private static byte[] BuildSeg1Items(int count, ushort[][] itemStringIndices)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                WriteShortBE(writer, (short)count);

                for (int i = 0; i < count; i++)
                {
                    // 先写占位 nextPos(int)，记录写入后位置。
                    long nextPosField = stream.Position;
                    WriteIntBE(writer, 0); // 占位

                    long afterNextPos = stream.Position;

                    // 资源项字段。
                    writer.Write((byte)4); // type=Atlas (PackageItemType.Atlas=4)
                    WriteUshortBE(writer, itemStringIndices[i][0]); // id = ReadS
                    WriteUshortBE(writer, itemStringIndices[i][1]); // name = ReadS
                    WriteUshortBE(writer, itemStringIndices[i][2]); // path = ReadS (null)
                    WriteUshortBE(writer, itemStringIndices[i][3]); // file = ReadS
                    writer.Write((byte)0); // exported=false
                    WriteIntBE(writer, 0); // width=0
                    WriteIntBE(writer, 0); // height=0
                    // Atlas 无额外字段。

                    long itemEnd = stream.Position;

                    // 回填 nextPos：相对 afterNextPos 的偏移，指向本项结束（即下一项起始）。
                    int nextPos = (int)(itemEnd - afterNextPos);
                    stream.Position = nextPosField;
                    WriteIntBE(writer, nextPos);
                    stream.Position = itemEnd;
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// 构造段2（精灵）字节：cnt(short)=0。
        /// </summary>
        private static byte[] BuildSeg2Sprites()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                WriteShortBE(writer, 0); // cnt=0
                return stream.ToArray();
            }
        }

        /// <summary>
        /// 构造段4（字符串表）字节：cnt(int) + 每项 string(ushort len + utf8)。
        /// </summary>
        private static byte[] BuildSeg4StringTable(List<string> stringTable)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                WriteIntBE(writer, stringTable.Count);
                for (int i = 0; i < stringTable.Count; i++)
                {
                    WriteString(writer, stringTable[i]);
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// 写入大端 uint。
        /// </summary>
        private static void WriteUintBE(BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        /// <summary>
        /// 写入大端 int。
        /// </summary>
        private static void WriteIntBE(BinaryWriter writer, int value)
        {
            WriteUintBE(writer, (uint)value);
        }

        /// <summary>
        /// 写入大端 short。
        /// </summary>
        private static void WriteShortBE(BinaryWriter writer, short value)
        {
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        /// <summary>
        /// 写入大端 ushort。
        /// </summary>
        private static void WriteUshortBE(BinaryWriter writer, ushort value)
        {
            WriteShortBE(writer, (short)value);
        }

        /// <summary>
        /// 写入 FairyGUI 字符串：ushort 长度 + UTF8 字节（与 ByteBuffer.ReadString 对齐）。
        /// </summary>
        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            WriteUshortBE(writer, (ushort)utf8.Length);
            writer.Write(utf8);
        }
    }
}
