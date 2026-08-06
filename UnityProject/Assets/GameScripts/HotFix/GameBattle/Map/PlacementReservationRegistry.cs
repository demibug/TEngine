using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.8：PlacementReservationRegistry —— 格子预留/提交/补偿/清理
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Map/PlacementReservationRegistry.cs）：
    //   管理购买放置事务中的临时格子预留；成功提交或失败回滚。
    //
    // 来源证据（PlacementReservationRegistry.js:1-17）：
    //   还原工程原 pR 是一个基于 Set 的简单注册表，提供 add/delete/clear/size。
    //   在 startGame/gameOver 时 clear。本期 C# 移植将其重构为强类型格子坐标预留，
    //   支持事务语义（预留 → 提交 / 回滚），对应决策 0.8 的原子事务需求。
    //
    // 决策依据：
    //   - 决策 0.8：购买放置命令携带 CommandId，预留-扣费-创建-放置是原子事务
    //     （task 77 完整事务，task 46 只做预留基础能力）。
    //   - spec battle-simulation "Input commands are atomic"：
    //     "Unit creation fails after reservation" → 系统释放预留、不扣除最终金币、
    //     不消耗卡牌且不留下半注册单位。
    //   - design.md 决策 5：删除 SingletonBase，改为强类型注入的独立对象。
    //
    // 与 MapData 的关系：
    //   预留基于 GridPosition 坐标。调用方（BattleInputController task 6.7）在预留前
    //   先校验格子可建造性（MapData.IsBuildableForSide），本注册表只负责管理预留状态，
    //   不重复可建造性校验。预留冲突（同一格子已被预留）由本注册表检测。
    //
    // 不变量：
    //   1. 独占单局状态：预留集合归属本实例，不跨局复用。
    //   2. 预留冲突检测：同一格子不可被重复预留。
    //   3. 提交/回滚后预留释放：Commit 和 Rollback 都会清除对应预留。
    //   4. Clear 清空全部预留：用于 startGame/gameOver/Settling 清理。
    // ============================================================================

    /// <summary>
    /// 管理购买放置事务中的临时格子预留；成功提交或失败回滚。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Map/PlacementReservationRegistry.cs）：</b>
    /// 管理购买放置事务中的临时格子预留；成功提交或失败回滚。
    /// 替代还原工程 <c>PlacementReservationRegistry.js</c> 全局单例
    /// （<c>PlacementReservationRegistry.js:6-15</c>）。</para>
    ///
    /// <para><b>原子事务支持（决策 0.8 / spec "Input commands are atomic"）：</b>
    /// 购买放置流程为：只读验证 → 格子预留 → 扣费 → 创建 → 放置 → 消耗卡牌 → 补牌 → 提交。
    /// 任一步失败时逆序补偿。本类型提供预留的 Reserve/Commit/Rollback 基础能力，
    /// 完整事务编排由 <c>BattleInputController</c>（task 6.7/77）实现。</para>
    ///
    /// <para><b>预留冲突检测：</b>同一格子不可被重复预留。<see cref="TryReserve"/>
    /// 在格子已被预留时返回失败，不修改状态。</para>
    ///
    /// <para><b>每局新建/销毁：</b>重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新实例。
    /// <see cref="Clear"/> 在 startGame/gameOver/Settling 清理时调用。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部规则服务使用。</para>
    /// </remarks>
    internal sealed class PlacementReservationRegistry
    {
        // ====================================================================
        // 预留集合
        // ====================================================================

        /// <summary>
        /// 已预留的格子坐标集合。使用 HashSet 保证 O(1) 查找与冲突检测。
        /// </summary>
        private readonly HashSet<GridPosition> _reserved = new HashSet<GridPosition>();

        // ====================================================================
        // 只读查询
        // ====================================================================

        /// <summary>
        /// 当前已预留格子数量（对应 PlacementReservationRegistry.js:14 size）。
        /// </summary>
        internal int Count => _reserved.Count;

        /// <summary>
        /// 判断指定格子是否已被预留。
        /// </summary>
        /// <param name="position">格子坐标。</param>
        /// <returns>已预留返回 true。</returns>
        internal bool IsReserved(GridPosition position)
        {
            return _reserved.Contains(position);
        }

        // ====================================================================
        // 预留 / 提交 / 回滚
        // ====================================================================

        /// <summary>
        /// 尝试预留格子（原子事务第一步的预留入口）。
        /// </summary>
        /// <param name="position">要预留的格子坐标。</param>
        /// <returns>
        /// 成功时 <see cref="ReservationResult.Success"/> 为 true；
        /// 格子已被预留时为 <see cref="ReservationFailureReason.AlreadyReserved"/>，
        /// 不修改状态。
        /// </returns>
        /// <remarks>
        /// <para><b>冲突检测（spec "Input commands are atomic"）：</b>
        /// 同一格子不可被重复预留。预留失败时不修改集合。</para>
        /// <para>调用方在调用本方法前应已通过 <see cref="MapData.IsBuildableForSide"/>
        /// 校验格子可建造性；本方法只负责预留状态管理。</para>
        /// </remarks>
        internal ReservationResult TryReserve(GridPosition position)
        {
            if (_reserved.Contains(position))
            {
                return ReservationResult.Failed(
                    ReservationFailureReason.AlreadyReserved,
                    position,
                    $"格子 {position} 已被预留");
            }

            _reserved.Add(position);
            return ReservationResult.Succeeded(position);
        }

        /// <summary>
        /// 批量预留多个格子。任一格子冲突时全部回滚（原子语义）。
        /// </summary>
        /// <param name="positions">要预留的格子坐标列表。</param>
        /// <returns>
        /// 全部成功时返回成功结果；
        /// 任一格子已被预留时返回失败并清除本次已临时预留的格子。
        /// </returns>
        /// <remarks>
        /// <para><b>原子批量预留：</b>若 positions 中有重复或与已预留格子冲突，
        /// 本次所有临时预留都会被回滚，保证不留下部分预留。</para>
        /// </remarks>
        internal ReservationResult TryReserveBatch(IReadOnlyList<GridPosition> positions)
        {
            if (positions == null)
            {
                return ReservationResult.Failed(
                    ReservationFailureReason.InvalidArgument,
                    GridPosition.FromColumnRow(-1, -1),
                    "positions 为 null");
            }

            // 先检查全部是否可预留（含批次内部重复）。
            var temporary = new List<GridPosition>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                GridPosition pos = positions[i];
                if (_reserved.Contains(pos) || temporary.Contains(pos))
                {
                    // 冲突：回滚已临时添加的格子（实际上尚未加入 _reserved）。
                    return ReservationResult.Failed(
                        ReservationFailureReason.AlreadyReserved,
                        pos,
                        $"格子 {pos} 已被预留或批次内重复");
                }

                temporary.Add(pos);
            }

            // 全部通过，正式加入预留集合。
            for (int i = 0; i < temporary.Count; i++)
            {
                _reserved.Add(temporary[i]);
            }

            return ReservationResult.Succeeded(GridPosition.FromColumnRow(0, 0));
        }

        /// <summary>
        /// 提交预留：确认放置成功，释放对应预留（对应事务成功提交）。
        /// </summary>
        /// <param name="position">要提交的格子坐标。</param>
        /// <remarks>
        /// <para>提交后格子从预留集合移除，表示放置已完成，格子不再处于"预留中"状态，
        /// 而是由 UnitRegistry 管理实际占用。</para>
        /// <para>若格子未被预留，本方法为空操作（幂等）。</para>
        /// </remarks>
        internal void Commit(GridPosition position)
        {
            _reserved.Remove(position);
        }

        /// <summary>
        /// 回滚预留：释放对应预留，事务失败时调用（对应 spec "Unit creation fails after reservation"）。
        /// </summary>
        /// <param name="position">要回滚的格子坐标。</param>
        /// <remarks>
        /// <para><b>补偿语义（spec "Unit creation fails after reservation"）：</b>
        /// 当格子预留成功但后续步骤（扣费/创建/放置）失败时，调用方调用本方法释放预留，
        /// 确保不留下半注册状态。</para>
        /// <para>若格子未被预留，本方法为空操作（幂等）。</para>
        /// </remarks>
        internal void Rollback(GridPosition position)
        {
            _reserved.Remove(position);
        }

        /// <summary>
        /// 回滚批量预留：释放多个格子预留。
        /// </summary>
        /// <param name="positions">要回滚的格子坐标列表。</param>
        internal void RollbackBatch(IReadOnlyList<GridPosition> positions)
        {
            if (positions == null)
            {
                return;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                _reserved.Remove(positions[i]);
            }
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 清空全部预留（对应 PlacementReservationRegistry.js:13 clear）。
        /// </summary>
        /// <remarks>
        /// <para>用于 startGame/gameOver/Settling 静默清理阶段。</para>
        /// <para>幂等：重复调用安全。</para>
        /// </remarks>
        internal void Clear()
        {
            _reserved.Clear();
        }
    }

    // ========================================================================
    // 预留操作结果结构
    // ========================================================================

    /// <summary>
    /// 预留操作失败原因。
    /// </summary>
    internal enum ReservationFailureReason
    {
        /// <summary>无失败。</summary>
        None = 0,

        /// <summary>格子已被预留（冲突）。</summary>
        AlreadyReserved = 1,

        /// <summary>参数无效。</summary>
        InvalidArgument = 2,
    }

    /// <summary>
    /// 预留操作结果，使用结构化结果而非异常表达正常校验失败。
    /// </summary>
    internal readonly struct ReservationResult
    {
        /// <summary>是否成功。</summary>
        public readonly bool Success;

        /// <summary>操作的格子坐标。</summary>
        public readonly GridPosition Position;

        /// <summary>失败原因（成功时为 None）。</summary>
        public readonly ReservationFailureReason FailureReason;

        /// <summary>失败诊断信息（成功时为空）。</summary>
        public readonly string FailureMessage;

        private ReservationResult(
            bool success,
            GridPosition position,
            ReservationFailureReason failureReason,
            string failureMessage)
        {
            Success = success;
            Position = position;
            FailureReason = failureReason;
            FailureMessage = failureMessage;
        }

        /// <summary>
        /// 构造成功结果。
        /// </summary>
        internal static ReservationResult Succeeded(GridPosition position)
        {
            return new ReservationResult(
                success: true,
                position: position,
                failureReason: ReservationFailureReason.None,
                failureMessage: string.Empty);
        }

        /// <summary>
        /// 构造失败结果。
        /// </summary>
        internal static ReservationResult Failed(
            ReservationFailureReason reason,
            GridPosition position,
            string message)
        {
            return new ReservationResult(
                success: false,
                position: position,
                failureReason: reason,
                failureMessage: message);
        }
    }
}
