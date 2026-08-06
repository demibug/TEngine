using System;
using GameFUI;
using UIBattle;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// 测试 owner 类型，仅位于测试程序集，用于验证与未来业务 Module 完全相同的注册契约。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策2——本 change 使用仅位于测试程序集的 owner 类型验证相同契约。
    /// 测试 owner 使用与未来业务 owner 完全相同的 Binder、Descriptor、Freeze 和 Show API，
    /// 不引入测试专用运行时旁路（design.md Risks/Trade-offs：测试 owner 与未来真实业务 Module 存在装配差异）。
    ///
    /// spec fairygui-window-runtime / Scenario: 测试 owner 注册 UIBattle：
    /// Editor 测试 owner 初始化 UIBattle 的测试 UI 能力时，先调用 <see cref="UIBattleBinder"/>，
    /// 再注册测试业务 Widget 和 Window，且其他 owner 不得重复拥有 UIBattle。
    ///
    /// 本类型只提供 owner 标识与同构注册入口，不创建或修改 GameBattle/BattleModule，
    /// 也不依赖 GameLogic/GamePlay/GameBattle 任何类型。装配流程见
    /// design.md 决策10：FUI.RegisterModule -> 注册 UICommon 基础 Binder ->
    /// 测试 owner 调用 UIBattleBinder -> 注册最终测试 Widget/Window 与 attach/detach ->
    /// FreezeBindings -> ShowAsync。
    /// </remarks>
    public static class TestFUIOwner
    {
        /// <summary>
        /// 测试 owner 类型标识，用作 <see cref="FUIDescriptor.OwnerType"/>。
        /// <para>未来业务 owner 是所属业务 Module 类型；本 change 以测试类型占位，
        /// 验证每个 Package 只能有一个 owner 类型的契约。</para>
        /// </summary>
        public static readonly Type OwnerType = typeof(TestFUIOwner);

        /// <summary>
        /// 按 spec 绑定顺序注册 UIBattle 测试域：先调用生成 Binder，再注册最终测试 Widget，最后注册最终测试 Window。
        /// <para>注册阶段只完成绑定和同步描述写入，不得创建或显示 FairyGUI 对象（design.md 决策2）。
        /// 后注册的最终 creator 覆盖生成类型，使创建结果为最末端业务类型而非生成基类
        /// （spec：业务类型覆盖生成类型）。</para>
        /// <para>本方法只负责本包（UIBattle）的 owner 域注册；UICommon 基础 Binder 由 FUIModule 基础 owner
        /// 在 <see cref="FUI.RegisterModule"/> 中绑定，不由此测试 owner 重复拥有（design.md 决策10）。</para>
        /// </summary>
        /// <param name="registry">活动绑定注册表，由装配方在 Freeze 前传入。本 change 装配方为 PlayMode/EditMode 测试 harness。</param>
        public static void RegisterUIBattle(FUIBindingRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            // 1. 本包生成 Binder：把生成类型 URL 注册到全局 UIObjectFactory，
            //    使后续覆盖注册的最终业务类型可被 FairyGUI 创建。
            //    Binder 内部调用 UIObjectFactory.SetPackageItemExtension，不创建对象。
            UIBattleBinder.BindAll();

            // 2. 注册最终测试 Widget：覆盖生成类型 UI_BattleStartWidget，
            //    使创建结果为 TestBattleStartWidget。Widget 通常使用 Normal 层。
            registry.Register(new FUIDescriptor(
                url: UI_BattleStartWidget.URL,
                packageName: UI_BattleStartWidget.PkgName,
                componentName: UI_BattleStartWidget.ResName,
                ownerType: OwnerType,
                targetType: typeof(TestBattleStartWidget),
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: null));

            // 3. 注册最终测试 Window：覆盖生成类型 UI_BattleStartPanel，
            //    使创建结果为 TestBattleStartPanel。同包同 owner 的覆盖为合法覆盖。
            registry.Register(new FUIDescriptor(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                ownerType: OwnerType,
                targetType: typeof(TestBattleStartPanel),
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: null));
        }
    }
}
