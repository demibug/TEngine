using FairyGUI;
using GameFUI;
using UIBattle;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// 最终测试业务窗口，继承生成类型 <see cref="UI_BattleStartPanel"/>（其又继承 <see cref="FUIWindow"/>）。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策2——后注册的最终 creator 覆盖生成类型，使创建结果为最末端业务类型
    /// 而非生成基类（spec fairygui-window-runtime / Scenario: 业务类型覆盖生成类型）。
    /// 本类型由测试 owner 注册为 <see cref="UI_BattleStartPanel"/>.URL 的最终业务类型，
    /// 调用方通过 <c>FUI.ShowAsync&lt;TestBattleStartPanel&gt;()</c> 直接打开
    /// （spec / Scenario: 直接打开最终测试业务窗口）。
    ///
    /// 共存验证（任务 3.6 验收标准5）：本窗口的生成基类 <see cref="UI_BattleStartPanel"/> 在
    /// <see cref="UI_BattleStartPanel.ConstructFromXML"/> 中已绑定三种 FairyGUI 元素，验证它们能在同一受管理窗口共存：
    /// <list type="bullet">
    /// <item>普通 <see cref="GComponent"/>：窗口自身即 GComponent 子类（FUIWindow : GComponent），
    ///   其子节点 0 的 <see cref="m_btn"/> 为原生 <see cref="GButton"/>。</item>
    /// <item>原生 <see cref="GButton"/>：<see cref="m_btn"/>，由生成代码以 <c>(GButton)GetChildAt(0)</c> 绑定，
    ///   代表非受管理的原生 FairyGUI 控件与受管理窗口共存。</item>
    /// <item>受管理 <see cref="UI_BattleStartWidget"/>：<see cref="m_widgetStart"/>，由生成代码以
    ///   <c>(UI_BattleStartWidget)GetChildAt(1)</c> 绑定，实际运行期被覆盖创建为 <see cref="TestBattleStartWidget"/>，
    ///   代表受管理 Widget 与窗口及原生控件共存。</item>
    /// </list>
    /// 三者共存是 FairyGUI 受管理窗口的核心结构契约：受管理 Window 不排斥普通 GComponent 与原生控件，
    /// 受管理 Widget 也可作为子节点与原生控件并列。本类型不创建或修改 GameBattle/BattleModule。
    /// </remarks>
    public class TestBattleStartPanel : UI_BattleStartPanel
    {
        /// <summary>
        /// 获取强类型的受管理测试 Widget。
        /// <para>生成基类字段 <see cref="m_widgetStart"/> 的编译期类型为 <see cref="UI_BattleStartWidget"/>，
        /// 但运行期实际对象在 Registry 覆盖注册后为 <see cref="TestBattleStartWidget"/>（业务类型覆盖生成类型）。
        /// 本属性提供强类型访问，便于测试断言受管理 Widget 在 <see cref="FUIWindow.OnCreate"/> 前已获得
        /// 正确的 OwnerWindow 与运行时上下文（spec / Scenario: 初始嵌套 Widget）。</para>
        /// </summary>
        public TestBattleStartWidget TestWidget => m_widgetStart as TestBattleStartWidget;

        /// <summary>
        /// 获取原生 <see cref="GButton"/>，验证原生控件与受管理窗口共存。
        /// </summary>
        public GButton NativeButton => m_btn;

        /// <summary>
        /// 验证窗口内普通 GComponent、原生 Button 与受管理 Widget 三者共存。
        /// </summary>
        /// <returns>三者均非空时返回 true，表示共存结构契约成立。</returns>
        /// <remarks>
        /// 供测试断言调用：窗口自身为 GComponent 子类（普通 GComponent 存在）、
        /// <see cref="NativeButton"/> 非空（原生 Button 存在）、<see cref="TestWidget"/> 非空（受管理 Widget 存在）。
        /// 不在此创建 FairyGUI 对象，仅做结构查询。
        /// </remarks>
        public bool HasGComponentButtonWidgetCoexistence()
        {
            // 窗口自身即普通 GComponent（FUIWindow : GComponent），故只需确认原生 Button 与受管理 Widget 均存在。
            return NativeButton != null && TestWidget != null;
        }
    }
}
