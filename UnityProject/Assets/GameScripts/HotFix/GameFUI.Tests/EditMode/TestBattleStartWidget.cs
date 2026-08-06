using GameFUI;
using UIBattle;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// 最终测试业务 Widget，继承生成类型 <see cref="UI_BattleStartWidget"/>（其又继承 <see cref="FUIWidget"/>）。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策2——后注册的最终 creator 覆盖生成类型，使创建结果为最末端业务类型
    /// 而非生成基类（spec fairygui-window-runtime / Scenario: 业务类型覆盖生成类型）。
    /// 本类型由测试 owner 注册为 <see cref="UI_BattleStartWidget"/>.URL 的最终业务类型，
    /// 在测试窗口 <see cref="TestBattleStartPanel"/> 构造时作为受管理嵌套 Widget 一并创建。
    ///
    /// 共存验证（任务 3.6 验收标准5）：本 Widget 作为受管理子节点与窗口内原生
    /// <see cref="FairyGUI.GButton"/>（<see cref="UI_BattleStartPanel.m_btn"/>）并列存在，
    /// 验证受管理 Widget 与原生控件能在同一窗口共存。
    ///
    /// 生命周期契约（spec / Requirement: Widget 在生命周期前获得所属窗口）：
    /// 每个受管理 Widget SHALL 在其业务生命周期（<see cref="FUIWidget.OnCreate"/>）开始前
    /// 获得所属窗口和运行时上下文。业务不得在构造函数或 ConstructFromXML 中依赖 OwnerWindow。
    /// 本类型遵循该约束，不在构造或 XML 解析阶段访问 <see cref="FUIWidget.OwnerWindow"/>。
    ///
    /// 本类型不创建或修改 GameBattle/BattleModule，也不依赖 GameLogic/GamePlay/GameBattle。
    /// </remarks>
    public class TestBattleStartWidget : UI_BattleStartWidget
    {
        /// <summary>
        /// 实例生命周期：创建回调，在幂等 Attach 设置 <see cref="FUIWidget.OwnerWindow"/> 与
        /// <see cref="FUIWidget.Context"/> 之后执行，仅执行一次。
        /// <para>同步回调，不得在此启动需要被框架等待的异步任务。
        /// 此处可安全访问 <see cref="FUIWidget.OwnerWindow"/>（spec / Scenario: 初始嵌套 Widget——
        /// Widget SHALL 在执行自身 OnCreate 前获得正确的 OwnerWindow）。</para>
        /// </summary>
        protected override void OnCreate()
        {
            base.OnCreate();

            // 共存断言占位：受管理 Widget 在 OnCreate 时已具备 OwnerWindow，
            // 证明它与窗口内原生 GButton 在同一受管理窗口内共存。
            // 实际断言由调用方测试在 Show 完成后通过 TestBattleStartPanel.HasGComponentButtonWidgetCoexistence() 验证。
        }
    }
}
