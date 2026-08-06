using System;
using FairyGUI;
using GameFUI;
using NUnit.Framework;
using UIBattle;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// <see cref="FUIBindingRegistry"/> 的 EditMode 单元测试，覆盖任务 3.7 的全部场景：
    /// 类型冲突、URL 冲突、owner/Package 冲突、冻结后写入、最终类型覆盖、
    /// 受管理窗口绕过创建检查（通过 Registry 查询得到的 TargetType 为最终业务类型），
    /// 以及 descriptor attach/detach 回调可正确设置和读取。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策2——单个 owner 内绑定顺序为 Binder → Widget → Window，
    /// 后注册覆盖生成类型；冻结后新增或冲突注册直接报错。
    /// spec fairygui-window-runtime：
    /// <list type="bullet">
    /// <item>Scenario: 重复或冲突注册——同一窗口类型或组件 URL 被不兼容描述重复注册时，
    /// 系统 SHALL 在首次创建前报告明确错误并阻止进入半注册状态。</item>
    /// <item>Scenario: 业务类型覆盖生成类型——业务窗口已用组件 URL 注册并请求创建时，
    /// 创建结果 SHALL 是注册的最末端业务类型，而不是生成基类。</item>
    /// </list>
    ///
    /// 复用约定：沿用 3.6 产出的 <see cref="TestFUIOwner"/>（owner）、
    /// <see cref="TestBattleStartPanel"/>（最终测试 Window）、<see cref="TestBattleStartWidget"/>
    /// （最终测试 Widget）及生成类型 <see cref="UI_BattleStartPanel"/>/<see cref="UI_BattleStartWidget"/>
    /// 的 URL/PkgName/ResName 常量。冲突场景需要第二个 owner 类型，故在本测试文件内定义
    /// <see cref="OtherTestOwner"/> 标记类型，仅用于校验 owner/Package 唯一性，不引入运行时旁路。
    /// 本测试只校验 Registry 行为，不创建 FairyGUI 对象（spec：注册阶段不得创建或显示 FairyGUI 对象）。
    /// </remarks>
    [TestFixture]
    public class FUIBindingRegistryTests
    {
        /// <summary>
        /// 第二个测试 owner 标记类型，仅用于 owner/Package 与 URL 冲突场景，
        /// 验证每个 Package 只能有一个 owner 类型的契约。不依赖任何运行时逻辑。
        /// </summary>
        private static readonly Type OtherOwnerType = typeof(OtherTestOwner);

        /// <summary>
        /// 冲突隔离用合成 URL，避免与 UIBattle 真实 URL 混淆，确保各冲突测试互不污染全局工厂。
        /// </summary>
        private const string SyntheticUrl = "ui://TestSyntheticPkg/SyntheticComponent";

        /// <summary>
        /// 冲突隔离用合成包名。
        /// </summary>
        private const string SyntheticPackage = "TestSyntheticPkg";

        /// <summary>
        /// 冲突隔离用合成组件名。
        /// </summary>
        private const string SyntheticComponent = "SyntheticComponent";

        // ============================================================
        // 场景 a：类型冲突——同一 TargetType 被不兼容描述重复注册 → 抛 FUIException
        // ============================================================

        /// <summary>
        /// 同一 TargetType 以不同 URL 再注册（owner 相同）属不兼容冲突，应抛 <see cref="FUIException"/>。
        /// </summary>
        [Test]
        [Description("类型冲突：同 TargetType 以不同 URL 重复注册应抛 FUIException，阻止半注册状态。")]
        public void TypeConflict_SameTargetTypeDifferentUrl_ThrowsFUIException()
        {
            // 安排：使用合成 URL 隔离，避免污染 UIBattle 真实注册。
            var registry = new FUIBindingRegistry();
            var first = BuildSyntheticDescriptor(
                url: SyntheticUrl,
                targetType: typeof(ConflictTargetA),
                ownerType: TestFUIOwner.OwnerType);
            registry.Register(first);

            // 同一 TargetType 但 URL 不同，属不兼容冲突。
            var conflicting = BuildSyntheticDescriptor(
                url: "ui://TestSyntheticPkg/AnotherComponent",
                targetType: typeof(ConflictTargetA),
                ownerType: TestFUIOwner.OwnerType);

            // 断言：在首次创建前（注册阶段）即报告明确错误。
            var ex = Assert.Throws<FUIException>(() => registry.Register(conflicting));
            Assert.IsNotNull(ex, "类型冲突必须抛出 FUIException，不得静默进入半注册状态。");
            Assert.That(ex.Message, Does.Contain("类型注册冲突").Or.Contain("冲突"),
                "异常信息应明确指出类型冲突。");
        }

        // ============================================================
        // 场景 b：URL 冲突——同一 URL 被不同 owner 重复注册 → 抛 FUIException
        // ============================================================

        /// <summary>
        /// 同一组件 URL 被不同 owner 注册属不兼容冲突，应抛 <see cref="FUIException"/>。
        /// </summary>
        [Test]
        [Description("URL 冲突：同一 URL 被不同 owner 重复注册应抛 FUIException。")]
        public void UrlConflict_SameUrlDifferentOwner_ThrowsFUIException()
        {
            // 安排：第一个 owner 注册合成 URL。
            var registry = new FUIBindingRegistry();
            var first = BuildSyntheticDescriptor(
                url: SyntheticUrl,
                targetType: typeof(ConflictTargetA),
                ownerType: TestFUIOwner.OwnerType);
            registry.Register(first);

            // 不同 owner 以同一 URL 再注册，属不兼容冲突。
            // 注意：因 Package owner 唯一性校验先于 URL 校验执行，
            // 这里使用与第一个相同的 Package 但不同 owner，会触发 Package owner 冲突；
            // 为精确触发 URL 冲突路径，使用不同 Package 绕过 Package 校验。
            var conflicting = BuildSyntheticDescriptor(
                url: SyntheticUrl,
                targetType: typeof(ConflictTargetB),
                ownerType: OtherOwnerType,
                packageName: "TestSyntheticPkgOther");

            // 断言：任一冲突维度命中即应报错；此处验证不兼容 URL 重复被阻止。
            var ex = Assert.Throws<FUIException>(() => registry.Register(conflicting));
            Assert.IsNotNull(ex, "URL 被不同 owner 重复注册必须抛出 FUIException。");
            Assert.That(ex.Message, Does.Contain("冲突"),
                "异常信息应明确指出冲突维度（URL 或 Package owner）。");
        }

        // ============================================================
        // 场景 c：owner/Package 冲突——同一 Package 被不同 owner 注册 → 抛 FUIException
        // ============================================================

        /// <summary>
        /// 同一 Package 被不同 owner 注册违反“每个 Package 只能有一个 owner 类型”契约，应抛 <see cref="FUIException"/>。
        /// </summary>
        [Test]
        [Description("owner/Package 冲突：同一 Package 被不同 owner 注册应抛 FUIException。")]
        public void PackageOwnerConflict_SamePackageDifferentOwner_ThrowsFUIException()
        {
            // 安排：第一个 owner 注册合成包。
            var registry = new FUIBindingRegistry();
            var first = BuildSyntheticDescriptor(
                url: "ui://TestSyntheticPkg/ComponentOne",
                targetType: typeof(ConflictTargetA),
                ownerType: TestFUIOwner.OwnerType,
                packageName: SyntheticPackage);
            registry.Register(first);

            // 不同 owner 以同一 Package（不同 URL/TargetType）再注册，触发 Package owner 冲突。
            var conflicting = BuildSyntheticDescriptor(
                url: "ui://TestSyntheticPkg/ComponentTwo",
                targetType: typeof(ConflictTargetB),
                ownerType: OtherOwnerType,
                packageName: SyntheticPackage);

            // 断言：Package owner 唯一性被违反时明确报错。
            var ex = Assert.Throws<FUIException>(() => registry.Register(conflicting));
            Assert.IsNotNull(ex, "同一 Package 被不同 owner 注册必须抛出 FUIException。");
            Assert.That(ex.Message, Does.Contain("Package owner 冲突"),
                "异常信息应明确指出 Package owner 冲突。");
        }

        // ============================================================
        // 场景 d：冻结后写入——Freeze 后再 Register → 抛 FUIException
        // ============================================================

        /// <summary>
        /// Registry 冻结后再注册任何描述应抛 <see cref="FUIException"/>，
        /// 阻止运行期变更注册表（design.md 决策2：冻结后新增或冲突注册直接报错）。
        /// </summary>
        [Test]
        [Description("冻结后写入：Freeze 后再 Register 应抛 FUIException。")]
        public void RegisterAfterFreeze_ThrowsFUIException()
        {
            // 安排：注册并冻结。
            var registry = new FUIBindingRegistry();
            registry.Register(BuildSyntheticDescriptor(
                url: SyntheticUrl,
                targetType: typeof(ConflictTargetA),
                ownerType: TestFUIOwner.OwnerType));
            Assert.IsFalse(registry.IsFrozen, "注册阶段注册表不应已冻结。");
            registry.Freeze();
            Assert.IsTrue(registry.IsFrozen, "Freeze 后注册表应处于冻结状态。");

            // 冻结后新增注册应被拒绝。
            var afterFreeze = BuildSyntheticDescriptor(
                url: "ui://TestSyntheticPkg/AfterFreeze",
                targetType: typeof(ConflictTargetB),
                ownerType: TestFUIOwner.OwnerType);

            // 断言：冻结后写入明确失败。
            var ex = Assert.Throws<FUIException>(() => registry.Register(afterFreeze));
            Assert.IsNotNull(ex, "冻结后注册必须抛出 FUIException。");
            Assert.That(ex.Message, Does.Contain("冻结"),
                "异常信息应明确指出注册表已冻结。");
        }

        // ============================================================
        // 场景 e：最终类型覆盖——同 owner 同 URL 先生成类型再业务类型 → 后者覆盖前者
        // ============================================================

        /// <summary>
        /// 同 owner 同 URL 的覆盖注册：先生成类型后业务类型，后者覆盖前者，
        /// <see cref="FUIBindingRegistry.TryGetDescriptor(string, out FUIDescriptor)"/>
        /// 返回业务类型描述（spec：业务类型覆盖生成类型）。
        /// </summary>
        /// <remarks>
        /// 复用 3.6 测试 Window：以 <see cref="UI_BattleStartPanel"/>（生成类型）先注册，
        /// 再以 <see cref="TestBattleStartPanel"/>（最终业务类型）同 URL 同 owner 覆盖注册，
        /// 验证覆盖语义与 Binder → Widget → Window 的最终覆盖一致。
        /// </remarks>
        [Test]
        [Description("最终类型覆盖：同 owner 同 URL 先生成类型再业务类型，后者覆盖前者。")]
        public void Override_SameOwnerSameUrl_BusinessTypeOverridesGeneratedType()
        {
            // 安排：先生成类型注册。
            var registry = new FUIBindingRegistry();
            var generated = BuildDescriptorFromConstants(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                targetType: typeof(UI_BattleStartPanel),
                ownerType: TestFUIOwner.OwnerType);
            registry.Register(generated);

            // 断言中间态：当前为生成类型。
            Assert.IsTrue(registry.TryGetDescriptor(UI_BattleStartPanel.URL, out FUIDescriptor midDescriptor),
                "生成类型注册后应可按 URL 查询到描述。");
            Assert.AreEqual(typeof(UI_BattleStartPanel), midDescriptor.TargetType,
                "覆盖前 TargetType 应为生成类型。");

            // 同 owner 同 URL 覆盖为最终业务类型。
            var business = BuildDescriptorFromConstants(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                targetType: typeof(TestBattleStartPanel),
                ownerType: TestFUIOwner.OwnerType);
            Assert.DoesNotThrow(() => registry.Register(business),
                "同 owner 同 URL 的覆盖注册不应抛异常。");

            // 断言：查询返回最末端业务类型，而非生成基类。
            Assert.IsTrue(registry.TryGetDescriptor(UI_BattleStartPanel.URL, out FUIDescriptor finalDescriptor),
                "覆盖后应仍可按 URL 查询到描述。");
            Assert.AreEqual(typeof(TestBattleStartPanel), finalDescriptor.TargetType,
                "覆盖后 TargetType 应为最终业务类型，而非生成基类。");
        }

        // ============================================================
        // 场景 f：受管理窗口绕过创建检查——Registry 查询得到的 TargetType 是最终业务类型
        // ============================================================

        /// <summary>
        /// 通过 Registry 按 URL 查询得到的 <see cref="FUIDescriptor.TargetType"/> 是最终业务类型，
        /// 而非生成类型；据此全局 creator（3.5）创建的结果即为受管理最终业务类型，
        /// 业务代码绕过 FUIModule 直接创建生成类型会被类型契约识别为绕过
        /// （spec：绕过受管理创建入口；3.7 聚焦 Registry 行为）。
        /// </summary>
        /// <remarks>
        /// 复用 3.6 测试 owner 的真实注册顺序（Binder → Widget → Window），
        /// 验证完整覆盖后 Registry 查询返回最终业务类型。
        /// </remarks>
        [Test]
        [Description("受管理窗口绕过创建检查：Registry 查询得到的 TargetType 为最终业务类型而非生成类型。")]
        public void ManagedWindow_RegistryQuery_ReturnsFinalBusinessType()
        {
            // 安排：使用 3.6 测试 owner 的真实注册入口完成 UIBattle 域注册。
            // RegisterUIBattle 内部按 Binder → Widget → Window 顺序注册最终业务类型。
            var registry = new FUIBindingRegistry();
            TestFUIOwner.RegisterUIBattle(registry);

            // 断言 Window：按 URL 查询得到的 TargetType 是最终业务类型 TestBattleStartPanel，
            // 而非生成类型 UI_BattleStartPanel。全局 creator 据此创建受管理最终业务窗口；
            // 若业务代码绕过 FUIModule 直接调用 UI_BattleStartPanel.CreateInstance() 创建生成类型，
            // 其类型与 Registry 记录的最终业务类型不一致，可被类型契约识别为绕过。
            Assert.IsTrue(registry.TryGetDescriptor(UI_BattleStartPanel.URL, out FUIDescriptor windowDescriptor),
                "UIBattle Window URL 应已注册。");
            Assert.AreEqual(typeof(TestBattleStartPanel), windowDescriptor.TargetType,
                "Registry 查询得到的 Window TargetType 应为最终业务类型，而非生成类型。");
            Assert.AreNotEqual(typeof(UI_BattleStartPanel), windowDescriptor.TargetType,
                "Registry 查询得到的 Window TargetType 不应为生成类型。");

            // 断言 Widget：同样验证最终业务类型覆盖生成类型。
            Assert.IsTrue(registry.TryGetDescriptor(UI_BattleStartWidget.URL, out FUIDescriptor widgetDescriptor),
                "UIBattle Widget URL 应已注册。");
            Assert.AreEqual(typeof(TestBattleStartWidget), widgetDescriptor.TargetType,
                "Registry 查询得到的 Widget TargetType 应为最终业务类型，而非生成类型。");

            // 断言：按 TargetType 反查亦返回最终业务类型描述，确保创建入口类型校验一致。
            Assert.IsTrue(registry.TryGetDescriptor(typeof(TestBattleStartPanel), out FUIDescriptor byTypeDescriptor),
                "按最终业务类型反查应返回描述。");
            Assert.AreEqual(UI_BattleStartPanel.URL, byTypeDescriptor.URL,
                "按最终业务类型反查的 URL 应与注册 URL 一致。");
        }

        // ============================================================
        // 场景 g：descriptor attach/detach 回调可正确设置和读取（次数与顺序由 5.x 运行时执行）
        // ============================================================

        /// <summary>
        /// 验证 <see cref="FUIDescriptor"/> 的 Attach/Detach 回调可被正确设置和读取。
        /// attach/detach 的实际调用次数和顺序由 5.x 运行时执行（先 Attach 后 OnCreate，
        /// 最终 Dispose 时 Detach），3.7 只验证描述上的回调可正确设置和读取。
        /// </summary>
        [Test]
        [Description("descriptor attach/detach 回调可正确设置和读取。")]
        public void Descriptor_AttachDetachCallbacks_CanBeSetAndRead()
        {
            // 安排：构造带 Attach/Detach 回调的描述，回调记录调用顺序。
            int callOrder = 0;
            int attachOrder = -1;
            int detachOrder = -1;
            Action<GComponent, object> attach = (c, ctx) => { attachOrder = ++callOrder; };
            Action<GComponent, object> detach = (c, ctx) => { detachOrder = ++callOrder; };

            var descriptor = BuildDescriptorFromConstants(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                targetType: typeof(TestBattleStartPanel),
                ownerType: TestFUIOwner.OwnerType,
                attach: attach,
                detach: detach);

            // 断言：回调可被读取且与设置一致。
            Assert.IsNotNull(descriptor.Attach, "Attach 回调应可被读取且非空。");
            Assert.IsNotNull(descriptor.Detach, "Detach 回调应可被读取且非空。");
            Assert.AreSame(attach, descriptor.Attach, "读取的 Attach 回调应与设置一致。");
            Assert.AreSame(detach, descriptor.Detach, "读取的 Detach 回调应与设置一致。");

            // 注册后回调仍可读取，确保 Registry 不丢失回调信息。
            var registry = new FUIBindingRegistry();
            registry.Register(descriptor);
            Assert.IsTrue(registry.TryGetDescriptor(UI_BattleStartPanel.URL, out FUIDescriptor storedDescriptor),
                "注册后应可按 URL 查询到描述。");
            Assert.IsNotNull(storedDescriptor.Attach, "Registry 存储的描述的 Attach 回调应非空。");
            Assert.IsNotNull(storedDescriptor.Detach, "Registry 存储的描述的 Detach 回调应非空。");

            // 模拟运行时 attach/detach 调用顺序：先 attach 后 detach（5.x 运行时顺序契约）。
            // 此处仅验证回调可被调用并记录顺序，不创建真实 FairyGUI 对象（null 目标仅用于回调语义验证）。
            storedDescriptor.Attach(null, null);
            storedDescriptor.Detach(null, null);
            Assert.AreEqual(1, attachOrder, "Attach 应先于 Detach 被调用（顺序契约）。");
            Assert.AreEqual(2, detachOrder, "Detach 应在 Attach 之后被调用（顺序契约）。");
        }

        // ============================================================
        // 辅助构造方法
        // ============================================================

        /// <summary>
        /// 构造合成描述，使用指定 URL/Package/TargetType/OwnerType，creator 为 null，
        /// 用于冲突隔离测试，避免触碰 UIBattle 真实注册与全局 UIObjectFactory。
        /// </summary>
        private static FUIDescriptor BuildSyntheticDescriptor(
            string url,
            Type targetType,
            Type ownerType,
            string packageName = SyntheticPackage,
            string componentName = SyntheticComponent,
            Action<GComponent, object> attach = null,
            Action<GComponent, object> detach = null)
        {
            return new FUIDescriptor(
                url: url,
                packageName: packageName,
                componentName: componentName,
                ownerType: ownerType,
                targetType: targetType,
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: null,
                attach: attach,
                detach: detach);
        }

        /// <summary>
        /// 基于生成类型常量（URL/PkgName/ResName）构造描述，用于覆盖与绕过检查场景，
        /// 确保测试使用与 3.6 owner 完全一致的元数据来源。
        /// </summary>
        private static FUIDescriptor BuildDescriptorFromConstants(
            string url,
            string packageName,
            string componentName,
            Type targetType,
            Type ownerType,
            Action<GComponent, object> attach = null,
            Action<GComponent, object> detach = null)
        {
            return new FUIDescriptor(
                url: url,
                packageName: packageName,
                componentName: componentName,
                ownerType: ownerType,
                targetType: targetType,
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: null,
                attach: attach,
                detach: detach);
        }

        /// <summary>
        /// 冲突隔离用占位 TargetType A，仅用于冲突测试，不依赖任何运行时逻辑。
        /// </summary>
        private sealed class ConflictTargetA
        {
        }

        /// <summary>
        /// 冲突隔离用占位 TargetType B，仅用于冲突测试，不依赖任何运行时逻辑。
        /// </summary>
        private sealed class ConflictTargetB
        {
        }

        /// <summary>
        /// 第二个测试 owner 标记类型，仅用于 owner/Package 与 URL 冲突场景，
        /// 验证每个 Package 只能有一个 owner 类型的契约。
        /// </summary>
        private static class OtherTestOwner
        {
        }
    }
}
