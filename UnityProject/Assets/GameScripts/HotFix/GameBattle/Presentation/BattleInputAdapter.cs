using System;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.4：BattleInputAdapter —— Unity/FairyGUI 点击拖放到强类型战斗命令的转换器
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Presentation/BattleInputAdapter.cs）：
    //   把 Unity/FairyGUI 点击拖放转换成强类型战斗命令。
    //
    //   本类型是表现层→逻辑层的输入适配器。Unity/FairyGUI 的点击/拖放事件携带
    //   UI 坐标与组件引用，本类型把这些原始 UI 事件翻译成不可变的
    //   <see cref="BattleInputCommand"/>（本期覆盖 BuyPlace 与 Refresh 两种命令，
    //   task 6.6 定义），提交给 <see cref="BattleInputController"/>（task 6.7）执行。
    //
    // 命令集合约束（task 6.4 / design.md:206）：
    //   本期只生成 BuyPlace 与 Refresh 两种命令。升级、合并、移动、交换和拖拽命令
    //   不出现在本期命令集合中。本类型不实现拖拽相关命令（BeginDrag/MoveDrag/
    //   CommitPlacement/CancelDrag），UI 直接构造 BuyPlace 命令。
    //
    // CommandId 语义（决策 0.8）：
    //   每条命令携带单局 CommandId。同一 ID 重复提交返回首次结果；不同 ID 按独立
    //   命令处理。CommandId 由本类型在主线程构造命令时递增分配，单局内唯一。
    //   本类型维护一个 int 计数器，随 BattleInputController 随 Runtime 清理
    //   （不跨局保留，每局由 Factory 新建 Adapter）。
    //
    // 主线程串行执行（design.md:206 / task 6.6）：
    //   所有输入在 Unity 主线程通过 Runtime 串行队列执行。本类型为同步方法，
    ///   调用方在主线程串行调用即可保证命令串行执行。
    //
    // 不变量：
    //   1. 只转换 UI 事件为强类型命令，不执行命令（执行由 BattleInputController 负责）。
    //   2. 不回写规则状态（design.md:217）：本类型只构造命令并提交给 InputController，
    //      不直接修改 BattleState/Manager。
    //   3. CommandId 单局内唯一递增，不跨局保留。
    //   4. 坐标转换：UI 坐标 → GridPosition（X=列、Y=行）由真实实现注入的
    //      ICoordinateConverter 完成，使本类型与具体 UI 框架解耦。
    // ============================================================================

    /// <summary>
    /// UI 坐标到逻辑格子坐标的转换器，由真实表现层实现注入。
    /// </summary>
    /// <remarks>
    /// <para>Unity/FairyGUI 的点击/拖放携带屏幕坐标或本地坐标，本接口负责把这些
    /// 坐标转换成强类型 <see cref="GridPosition"/>（X=列、Y=行）。</para>
    /// <para>真实实现由 task 7.5/7.6 接入（Unity RectTransform.worldToLocalMatrix /
    /// FairyGUI GComponent.TouchPosToLocal）。</para>
    /// </remarks>
    internal interface ICoordinateConverter
    {
        /// <summary>
        /// 尝试把 UI 屏幕坐标转换成逻辑格子坐标。
        /// </summary>
        /// <param name="screenX">UI 屏幕 X 坐标。</param>
        /// <param name="screenY">UI 屏幕 Y 坐标。</param>
        /// <param name="position">输出逻辑格子坐标。</param>
        /// <returns>转换成功且格子合法返回 true；越界或无效返回 false。</returns>
        bool TryConvertToGrid(float screenX, float screenY, out GridPosition position);
    }

    /// <summary>
    /// Unity/FairyGUI 点击拖放到强类型战斗命令的转换器。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（最终方案）：</b>把 UI 点击/拖放转换成强类型战斗命令。
    /// 输入层只提交两个命令：<see cref="BattleInputCommandType.Recruit"/>（征兵）与
    /// <see cref="BattleInputCommandType.DropUnit"/>（换槽/合并）。</para>
    ///
    /// <para><b>CommandId 语义（决策 0.8）：</b>
    /// 每条命令携带单局 CommandId，由本类型在主线程递增分配，单局内唯一。</para>
    ///
    /// <para><b>主线程串行执行（design.md:206）：</b>
    /// 所有输入在 Unity 主线程通过 Runtime 串行队列执行。本类型为同步方法。</para>
    ///
    /// <para><b>不回写规则状态（design.md:217）：</b>
    /// 本类型只构造命令并提交给 <see cref="BattleInputController"/>，不直接修改规则状态。</para>
    /// </remarks>
    internal sealed class BattleInputAdapter
    {
        // ====================================================================
        // 只读依赖
        // ====================================================================

        /// <summary>
        /// 输入命令执行控制器，供 Adapter 提交转换后的命令。
        /// <para>由 BattleRuntimeFactory 构造并经 Assembly 注入，Adapter 接管引用。</para>
        /// </summary>
        private readonly BattleInputController _inputController;

        /// <summary>
        /// UI 坐标到逻辑格子坐标的转换器。
        /// <para>真实实现由 task 7.5/7.6 接入；本期可注入空实现（所有转换返回 false）。</para>
        /// </summary>
        private readonly ICoordinateConverter _coordinateConverter;

        // ====================================================================
        // 局内可变状态
        // ====================================================================

        /// <summary>
        /// 下一个待分配的 CommandId（单局内递增，不跨局保留）。
        /// <para>决策 0.8：每条命令携带单局 CommandId。本类型从 1 开始递增分配，
        /// 保证单局内唯一。0 保留为"未分配"哨兵值，不作为合法 CommandId。</para>
        /// <para>线程安全：所有输入在 Unity 主线程串行执行（design.md:206），无需同步。</para>
        /// </summary>
        private readonly BattleCommandIdAllocator _commandIdAllocator;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造输入适配器。
        /// </summary>
        /// <param name="inputController">输入命令执行控制器（非 null）。</param>
        /// <param name="coordinateConverter">UI 坐标转换器（非 null）。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        internal BattleInputAdapter(
            BattleInputController inputController,
            ICoordinateConverter coordinateConverter,
            BattleCommandIdAllocator commandIdAllocator)
        {
            _inputController = inputController
                ?? throw new ArgumentNullException(nameof(inputController));
            _coordinateConverter = coordinateConverter
                ?? throw new ArgumentNullException(nameof(coordinateConverter));
            _commandIdAllocator = commandIdAllocator
                ?? throw new ArgumentNullException(nameof(commandIdAllocator));
        }

        // ====================================================================
        // UI 事件 → 强类型命令转换
        // ====================================================================

        /// <summary>
        /// 处理"征兵"UI 点击：构造 Recruit 命令并提交。
        /// </summary>
        /// <param name="playerSide">是否玩家方。</param>
        /// <returns>命令执行结果。</returns>
        /// <remarks>
        /// <para>征兵命令不需要槽位坐标，直接分配 CommandId 并构造命令提交。</para>
        /// <para><b>不回写规则状态（design.md:217）：</b>本方法只构造命令并提交给
        /// InputController，不直接修改规则状态。</para>
        /// </remarks>
        internal BattleInputResult HandleRecruitClick(bool playerSide)
        {
            int commandId = AllocateCommandId();
            BattleInputCommand command = BattleInputCommand.CreateRecruit(commandId, playerSide);
            return _inputController.Execute(command);
        }

        /// <summary>
        /// 处理"换槽/合并"拖放提交：构造 DropUnit 命令并提交。
        /// </summary>
        /// <param name="sourceSlotId">源槽位固定标识。</param>
        /// <param name="targetSlotId">目标槽位固定标识。</param>
        /// <returns>命令执行结果。</returns>
        /// <remarks>
        /// <para><b>最终方案"输入层只提交两个命令"：</b>换槽/合并在松手时提交一次
        /// DropUnit(sourceSlotId, targetSlotId)，全程不访问经济模块。拖动过程中不修改
        /// 规则状态。</para>
        /// </remarks>
        internal BattleInputResult HandleDropUnit(int sourceSlotId, int targetSlotId)
        {
            int commandId = AllocateCommandId();
            BattleInputCommand command = BattleInputCommand.CreateDropUnit(commandId, sourceSlotId, targetSlotId);
            return _inputController.Execute(command);
        }

        /// <summary>处理铲子拖放：先把 Stage 坐标转换为地图格，再提交强类型命令。</summary>
        internal BattleInputResult HandleUseShovel(int sourceReserveSlotId, float screenX, float screenY)
        {
            int commandId = AllocateCommandId();
            if (!_coordinateConverter.TryConvertToGrid(screenX, screenY, out GridPosition position))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidShovelTarget,
                    $"铲子未命中有效地图格 stage=({screenX:F1},{screenY:F1})");
            }

            BattleInputCommand command = BattleInputCommand.CreateUseShovel(
                commandId,
                sourceReserveSlotId,
                position);
            return _inputController.Execute(command);
        }

        /// <summary>
        /// 尝试把 UI 屏幕坐标转换成逻辑格子坐标（最终方案：屏幕坐标只负责识别 SlotId）。
        /// </summary>
        /// <param name="screenX">UI 屏幕 X 坐标。</param>
        /// <param name="screenY">UI 屏幕 Y 坐标。</param>
        /// <param name="position">输出逻辑格子坐标。</param>
        /// <returns>转换成功且格子合法返回 true；越界或无效返回 false。</returns>
        internal bool TryConvertToGrid(float screenX, float screenY, out GridPosition position)
        {
            return _coordinateConverter.TryConvertToGrid(screenX, screenY, out position);
        }

        // ====================================================================
        // CommandId 分配
        // ====================================================================

        /// <summary>
        /// 分配下一个单局 CommandId（递增，单局内唯一）。
        /// </summary>
        /// <returns>新的 CommandId（从 1 开始递增）。</returns>
        /// <remarks>
        /// <para>决策 0.8：每条命令携带单局 CommandId。本类型从 1 开始递增分配，
        /// 保证单局内唯一。0 保留为"未分配"哨兵值，不作为合法 CommandId。</para>
        /// <para>线程安全：所有输入在 Unity 主线程串行执行（design.md:206），无需同步。
        /// int 递增在 32 位平台上非原子，但主线程串行调用保证无竞争。</para>
        /// <para>不跨局保留：随 Runtime 销毁，每局由 Factory 新建 Adapter，计数器重置为 1。</para>
        /// </remarks>
        private int AllocateCommandId()
        {
            return _commandIdAllocator.Allocate();
        }
    }
}
