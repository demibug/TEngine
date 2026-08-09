using System;

namespace GameBattle
{
    // ============================================================================
    // 表现层：BattleDragController —— 拖拽状态机（最终方案"表现层拖拽"）
    // ----------------------------------------------------------------------------
    // 职责（修复阶段 P0 四向拖拽）：
    //   只保存"源槽位 ID + touchId"的纯表现拖拽状态，统一处理 Reserve 与 Battle 源。
    //   拖动过程中不修改任何规则状态；松手时解析任意目标槽位（Reserve 或 Battle），
    //   解析失败或无目标则弹回（不提交命令）。
    //
    // 与输入层的关系：
    //   本控制器只持有源槽位 ID 与 touchId，不直接操作 UnitSlotBoard / UnitRegistry。
    //   松手时经注入的 DropUnit 委托提交 DropUnit(sourceSlotId, targetSlotId) 命令，
    //   由 BattleInputController 原子执行。
    //
    // 不变量：
    //   1. 拖动中（BeginDrag 之后 EndDrag/Cancel 之前）不调用任何规则修改方法。
    //   2. 一次 BeginDrag 只能有一次 EndDrag 或 Cancel。
    //   3. 未开始拖动时 EndDrag 为空操作（返回 null）。
    // ============================================================================

    /// <summary>
    /// 拖拽状态机：保存源槽位与 touchId，松手时提交 DropUnit 命令（最终方案四向拖拽）。
    /// </summary>
    /// <remarks>
    /// <para><b>纯表现状态：</b>只保存源槽位 ID 与 touchId，不持有 UnitSlotBoard /
    /// UnitRegistry 引用，不修改规则状态。</para>
    /// <para><b>统一源：</b>Reserve 卡与战场单位都通过 <see cref="BeginDrag"/> 进入，
    /// 源槽位既可以是 Reserve 槽也可以是 Battle 槽（四向：R→R、R→B、B→B、B→R）。</para>
    /// <para><b>目标解析：</b>松手时经 <paramref name="resolveTargetSlot"/> 把舞台坐标
    /// 解析为任意目标槽位；未命中返回 -1（弹回，不提交）。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleHudPanel /
    /// BattlePresenter 使用。</para>
    /// </remarks>
    internal sealed class BattleDragController
    {
        /// <summary>提交 DropUnit 命令的委托（源槽位 ID、目标槽位 ID）。</summary>
        private readonly Func<int, int, BattleInputResult> _dropUnit;

        /// <summary>把舞台坐标解析为目标槽位 ID 的委托；未命中返回 -1。</summary>
        private readonly Func<float, float, int> _resolveTargetSlot;

        /// <summary>当前拖拽源槽位 ID；-1 表示未在拖拽中。</summary>
        private int _sourceSlotId = -1;

        /// <summary>当前拖拽 touchId（诊断用）。</summary>
        private int _touchId = -1;

        /// <summary>
        /// 构造拖拽控制器。
        /// </summary>
        /// <param name="dropUnit">提交 DropUnit 命令的委托（非 null）。</param>
        /// <param name="resolveTargetSlot">把舞台坐标解析为目标槽位 ID 的委托（非 null）。</param>
        internal BattleDragController(
            Func<int, int, BattleInputResult> dropUnit,
            Func<float, float, int> resolveTargetSlot)
        {
            _dropUnit = dropUnit ?? throw new ArgumentNullException(nameof(dropUnit));
            _resolveTargetSlot = resolveTargetSlot ?? throw new ArgumentNullException(nameof(resolveTargetSlot));
        }

        /// <summary>是否正在拖拽。</summary>
        internal bool IsDragging => _sourceSlotId >= 0;

        /// <summary>当前拖拽源槽位 ID（诊断用；未拖拽为 -1）。</summary>
        internal int SourceSlotId => _sourceSlotId;

        /// <summary>
        /// 开始一次拖拽。
        /// </summary>
        /// <param name="sourceSlotId">源槽位 ID（Reserve 或 Battle 槽）。</param>
        /// <param name="touchId">输入 touchId。</param>
        /// <remarks>
        /// 覆盖进行中的拖拽（先取消再开始）。拖动中不修改规则状态。
        /// </remarks>
        internal void BeginDrag(int sourceSlotId, int touchId)
        {
            _sourceSlotId = sourceSlotId;
            _touchId = touchId;
        }

        /// <summary>
        /// 松手结束拖拽：解析目标槽位并提交 DropUnit 命令。
        /// </summary>
        /// <param name="stageX">FairyGUI Stage X 坐标。</param>
        /// <param name="stageY">FairyGUI Stage Y 坐标。</param>
        /// <returns>
        /// 已提交时返回命令执行结果；未在拖拽或目标未命中（弹回）时返回 null。
        /// </returns>
        internal BattleInputResult? EndDrag(float stageX, float stageY)
        {
            if (_sourceSlotId < 0)
            {
                return null;
            }

            int sourceSlotId = _sourceSlotId;
            _sourceSlotId = -1;
            _touchId = -1;

            // 解析目标槽位；未命中（-1）或与源相同 → 弹回，不提交。
            int targetSlotId = _resolveTargetSlot(stageX, stageY);
            if (targetSlotId < 0 || targetSlotId == sourceSlotId)
            {
                return null;
            }

            return _dropUnit(sourceSlotId, targetSlotId);
        }

        /// <summary>
        /// 取消当前拖拽（不提交命令）。
        /// </summary>
        internal void Cancel()
        {
            _sourceSlotId = -1;
            _touchId = -1;
        }
    }
}
