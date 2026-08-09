using System.Collections.Generic;
using GameCommon.Battle;

namespace GameBattle
{
    // ============================================================================
    // 任务 8.1：BattleTraceRecorder —— 确定性轨迹记录器
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 224 行 / Diagnostics/BattleTraceRecorder.cs）：
    //   在阶段边界记录黄金轨迹，不改变模拟顺序。
    //
    // 稳定序列化要求（task 8.1）：
    //   1. 版本化：生成的快照含 SchemaVersion 字段。
    //   2. 固定字段顺序：快照的 SerializeToText 按显式声明顺序输出。
    //   3. 排除对象地址：只从 Manager 只读快照收集 RuntimeId 与标量，不记录对象地址。
    //   4. 排除 Dictionary 未定义顺序：实体集合在收集后按 Id 排序再写入快照。
    //   5. 排除真实时间噪声：只使用 BattleSimulation 的逻辑时间，不读取 DateTime.Now 等。
    //
    // 设计依据：
    //   - design.md 第 224 行："在阶段边界记录黄金轨迹，不改变模拟顺序。"
    //   - spec battle-parity-verification "Trace captures behaviorally significant state"：
    //     轨迹至少包含逻辑时间、波次、双方生命和金币、手牌、实体状态、位置、目标、投射物、
    //     攻击效果、公共事实、池统计和最终结果。
    //   - design.md 决策 0.3：阶段顺序固定（BattleUpdatePhase）。
    //   - design.md 决策 0.9：逻辑时间源唯一为 elapseSeconds，frameNowMs 同帧不变。
    //   - task 4.6 约束：稳定有序集合，禁止依赖 Dictionary/HashSet 未定义遍历顺序。
    //
    // 不改变模拟顺序：
    //   本类型只在阶段边界（BattleSimulation 回调前后）只读快照状态，不修改任何 Manager
    //   集合、不触发伤害或状态变更、不注册额外 callback。记录操作是纯只读副作用。
    //
    // 可访问性约束：
    //   本类型只使用现有 Manager/ReadModel 的 public/internal 只读属性与 Snapshot 方法，
    //   不新增也不修改其他类型的成员。当前无法从现有接口直接获取的字段使用 0/-1 占位
    //   （如 AttackEffect 的 EffectId/Kind/ElapsedMs 因 IAttackEffect 未暴露而记 0），
    //   task 8.2 对照工具比较时按字段容差处理。Projectile 的 TargetId 已通过
    //   ProjectileBase.TargetId 虚属性（SimpleDynamicArrow 重写为 Movement.TargetId）采集真实值。
    //
    // 已批准偏差（详见 design.md "已批准偏差"表）：
    //   - 攻击效果 EffectId/Kind/ElapsedMs 占位：IAttackEffect 未暴露这些只读属性，
    //     扩展接口需修改 5 个效果类，超出 task 8.1 最小修改范围，记为已批准偏差。
    //   - poolStats 逐池统计延后：BattlePoolScope 当前只暴露 PoolCount 标量，
    //     逐池明细需后续扩展，记为已批准偏差。
    //
    // 不变量：
    //   1. 只读快照：Record / CaptureSnapshot 只读取 Manager 只读属性与 Snapshot 方法，
    //      不修改任何规则状态。
    //   2. 确定性排序：实体集合按 Id 升序排序后写入快照，排除 Dictionary 未定义顺序。
    //   3. 逻辑时间源：只使用 BattleSimulation.FrameNowMs / StepMs / ElapsedGameTimeMs，
    //      不读取真实时间。
    //   4. 每局新建/销毁：不跨局复用，重开由 BattleRuntimeFactory 新建。
    //
    // 本类型为 internal：只供 GameBattle 内部 BattleRuntime / 测试使用。
    // ============================================================================

    /// <summary>
    /// 确定性轨迹记录器：在阶段边界只读快照战斗状态，生成稳定、版本化的轨迹行
    /// （task 8.1 / design.md 第 224 行）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 224 行）：</b>在阶段边界记录黄金轨迹，不改变模拟顺序。
    /// 替代还原工程中分散在 Manager 内的 <c>console.log</c> / <c>updateLog</c> 诊断输出，
    /// 统一为可序列化、可跨 JS/C# 比较的 <see cref="BattleTraceSnapshot"/>。</para>
    ///
    /// <para><b>不改变模拟顺序：</b>本类型只在阶段边界只读快照状态，不修改任何 Manager 集合、
    /// 不触发伤害或状态变更、不注册额外 callback。记录操作是纯只读副作用，
    /// 不会影响黄金轨迹的可复现性。</para>
    ///
    /// <para><b>稳定序列化（task 8.1）：</b></para>
    /// <list type="bullet">
    /// <item><b>版本化</b>：生成的快照含 <see cref="BattleTraceSnapshot.SchemaVersion"/>。</item>
    /// <item><b>固定字段顺序</b>：快照的 <see cref="BattleTraceSnapshot.SerializeToText"/>
    /// 按显式声明顺序输出。</item>
    /// <item><b>排除对象地址</b>：只从 Manager 只读快照收集 RuntimeId 与标量，
    /// 不记录 <c>GetHashCode</c> 或对象指针。</item>
    /// <item><b>排除 Dictionary 未定义顺序</b>：实体集合在收集后按 Id 排序再写入快照。</item>
    /// <item><b>排除真实时间噪声</b>：只使用 <see cref="BattleSimulation.FrameNowMs"/>/
    /// <see cref="BattleSimulation.StepMs"/>/<see cref="BattleSimulation.ElapsedGameTimeMs"/>，
    /// 不读取 <c>DateTime.Now</c>/<c>Time.realtimeSinceStartup</c>。</item>
    /// </list>
    ///
    /// <para><b>每局新建/销毁：</b>不跨局复用，重开由 <see cref="BattleRuntimeFactory"/> 新建。
    /// 已收集的轨迹行列表随局销毁清空。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleRuntime / 测试使用。</para>
    /// </remarks>
    internal sealed class BattleTraceRecorder
    {
        // ====================================================================
        // 只读依赖
        // ====================================================================

        /// <summary>
        /// 战斗模拟器（逻辑时钟源）。只读访问 <see cref="BattleSimulation.FrameNowMs"/>/
        /// <see cref="BattleSimulation.StepMs"/>/<see cref="BattleSimulation.ElapsedGameTimeMs"/>，
        /// 不调用 <c>Advance</c> 或 <c>TryFreeze</c>。
        /// </summary>
        private readonly BattleSimulation _simulation;

        /// <summary>只读状态视图，供快照标量。</summary>
        private readonly BattleReadModel _readModel;

        /// <summary>敌人管理器，供快照敌人集合。</summary>
        private readonly EnemyManager _enemyManager;

        /// <summary>投射物管理器，供快照投射物集合。</summary>
        private readonly ProjectileManager _projectileManager;

        /// <summary>攻击效果管理器，供快照攻击效果集合。</summary>
        private readonly AttackEffectManager _attackEffectManager;

        /// <summary>槽位面板，供快照待上场/战场槽位（修复 P0 替代旧 DeckManager）。</summary>
        private readonly UnitSlotBoard _slotBoard;

        /// <summary>单位注册表，供快照单位计数。</summary>
        private readonly UnitRegistry _unitRegistry;

        /// <summary>格子预留注册表，供快照预留计数。</summary>
        private readonly PlacementReservationRegistry _reservationRegistry;

        /// <summary>池作用域，供快照池统计。</summary>
        private readonly BattlePoolScope _poolScope;

        /// <summary>结果冻结器，供快照冻结状态与最终结果。</summary>
        private readonly BattleResultBuilder _resultBuilder;

        // ====================================================================
        // 已收集的轨迹行列表
        // --------------------------------------------------------------------
        // 按 Record 调用顺序追加，不排序（轨迹行本身的时间顺序由 frameNowMs/stepMs/phase 标记）。
        // 序列化单行快照时排除无序集合；列表本身的顺序是调用顺序，确定。
        // ====================================================================

        /// <summary>
        /// 已收集的轨迹行列表。按 <see cref="Record"/> 调用顺序追加。
        /// <para>列表顺序 = 调用顺序 = 阶段边界顺序，确定。不在此处排序（排序会破坏时间因果链）。</para>
        /// </summary>
        private readonly List<BattleTraceSnapshot> _records = new List<BattleTraceSnapshot>();

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造轨迹记录器，注入所需只读依赖。
        /// </summary>
        /// <param name="simulation">战斗模拟器（逻辑时钟源）。</param>
        /// <param name="readModel">只读状态视图。</param>
        /// <param name="enemyManager">敌人管理器。</param>
        /// <param name="projectileManager">投射物管理器。</param>
        /// <param name="attackEffectManager">攻击效果管理器。</param>
        /// <param name="deckManager">牌组管理器。</param>
        /// <param name="unitRegistry">单位注册表。</param>
        /// <param name="reservationRegistry">格子预留注册表。</param>
        /// <param name="poolScope">池作用域。</param>
        /// <param name="resultBuilder">结果冻结器。</param>
        /// <remarks>
        /// 由 <see cref="BattleRuntimeFactory"/> 在每次 Create 时构造新实例，保证每局独立。
        /// 所有依赖均为只读访问，记录器不修改任何依赖状态。
        /// </remarks>
        internal BattleTraceRecorder(
            BattleSimulation simulation,
            BattleReadModel readModel,
            EnemyManager enemyManager,
            ProjectileManager projectileManager,
            AttackEffectManager attackEffectManager,
            UnitSlotBoard slotBoard,
            UnitRegistry unitRegistry,
            PlacementReservationRegistry reservationRegistry,
            BattlePoolScope poolScope,
            BattleResultBuilder resultBuilder)
        {
            _simulation = simulation;
            _readModel = readModel;
            _enemyManager = enemyManager;
            _projectileManager = projectileManager;
            _attackEffectManager = attackEffectManager;
            _slotBoard = slotBoard;
            _unitRegistry = unitRegistry;
            _reservationRegistry = reservationRegistry;
            _poolScope = poolScope;
            _resultBuilder = resultBuilder;
        }

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>
        /// 已收集的轨迹行数量。
        /// </summary>
        internal int Count => _records.Count;

        // ====================================================================
        // 记录入口 —— 在阶段边界只读快照
        // --------------------------------------------------------------------
        // 不改变模拟顺序：只读取 Manager 只读属性与 Snapshot 方法，不修改任何规则状态。
        // ====================================================================

        /// <summary>
        /// 在阶段边界记录一帧轨迹行（只读快照，不改变模拟顺序）。
        /// </summary>
        /// <param name="updatePhase">当前更新阶段（BattleUpdatePhase 枚举整数值）。</param>
        /// <remarks>
        /// <para><b>不改变模拟顺序（design.md 第 224 行）：</b>本方法只读取 Manager 只读属性
        /// 与 Snapshot 方法，不修改任何集合、不触发伤害或状态变更。可安全在
        /// <see cref="BattleSimulation"/> 阶段回调前后调用。</para>
        ///
        /// <para><b>逻辑时间源（task 8.1 排除真实时间噪声）：</b>从 <see cref="BattleSimulation"/>
        /// 读取 <see cref="BattleSimulation.FrameNowMs"/>/<see cref="BattleSimulation.StepMs"/>/
        /// <see cref="BattleSimulation.ElapsedGameTimeMs"/>，不读取 <c>DateTime.Now</c>。</para>
        ///
        /// <para><b>排除对象地址（task 8.1）：</b>实体集合只收集 RuntimeId 与标量状态，
        /// 不记录 <c>GetHashCode</c> 或对象指针。</para>
        ///
        /// <para><b>排除 Dictionary 未定义顺序（task 8.1）：</b>实体集合在收集后按 Id 排序，
        /// 再写入快照。手牌按 CardId 排序。池统计按类型名排序。</para>
        /// </remarks>
        internal void Record(int updatePhase)
        {
            BattleTraceSnapshot snapshot = CaptureSnapshot(updatePhase);
            _records.Add(snapshot);
        }

        /// <summary>
        /// 捕获当前状态的轨迹快照（不追加到记录列表，供按需诊断）。
        /// </summary>
        /// <param name="updatePhase">当前更新阶段。</param>
        /// <returns>不可变轨迹快照。</returns>
        /// <remarks>
        /// 与 <see cref="Record"/> 的区别：本方法只返回快照不追加到列表，
        /// 供调用方按需获取单帧诊断。序列化稳定性与 <see cref="Record"/> 一致。
        /// </remarks>
        internal BattleTraceSnapshot CaptureSnapshot(int updatePhase)
        {
            // 1. 逻辑时间（排除真实时间噪声）
            long frameNowMs = _simulation.FrameNowMs;
            long stepMs = _simulation.StepMs;
            long elapsedGameTimeMs = _simulation.ElapsedGameTimeMs;

            // 2. 战斗状态快照（不可变值类型副本）
            BattleStateSnapshot state = _readModel.Snapshot();

            // 3. 敌人集合（按 Id 升序排序，排除 Dictionary 未定义顺序）
            List<BattleTraceSnapshot.EnemyTraceRow> enemies = CollectEnemyRows();

            // 4. 投射物集合（按 Id 升序排序）
            List<BattleTraceSnapshot.ProjectileTraceRow> projectiles = CollectProjectileRows();

            // 5. 攻击效果集合（按 OwnerId 升序排序）
            List<BattleTraceSnapshot.AttackEffectTraceRow> attackEffects = CollectAttackEffectRows();

            // 6. 槽位占用单位（按 UnitId 升序排序，替代旧手牌；待上场 + 战场）。
            List<BattleTraceSnapshot.UnitCardTraceRow> playerHand = ToSlotRows(isPlayerSide: true);
            List<BattleTraceSnapshot.UnitCardTraceRow> opponentHand = ToSlotRows(isPlayerSide: false);

            // 7. 池统计（BattlePoolScope 当前只暴露 PoolCount 标量，逐池明细需后续扩展；
            //    此处返回空列表占位，PoolTypeCount 标量仍写入诊断快照。
            //    逐池统计延后为已批准偏差，详见 design.md "已批准偏差"表）
            List<BattleTraceSnapshot.PoolStatTraceRow> poolStats =
                new List<BattleTraceSnapshot.PoolStatTraceRow>(0);

            // 8. 公共事实
            bool isFrozen = _simulation.IsFrozen;
            bool isResultFrozen = _resultBuilder.IsFrozen;

            // 9. 最终结果（仅在结算后填充）
            BattleResultDto? finalResult = _resultBuilder.IsFrozen
                ? _resultBuilder.FrozenResult
                : null;

            return new BattleTraceSnapshot(
                frameNowMs: frameNowMs,
                stepMs: stepMs,
                elapsedGameTimeMs: elapsedGameTimeMs,
                updatePhase: updatePhase,
                state: state,
                enemies: enemies,
                projectiles: projectiles,
                attackEffects: attackEffects,
                playerHand: playerHand,
                opponentHand: opponentHand,
                poolStats: poolStats,
                isFrozen: isFrozen,
                isResultFrozen: isResultFrozen,
                finalResult: finalResult);
        }

        // ====================================================================
        // 诊断快照入口
        // ====================================================================

        /// <summary>
        /// 捕获当前状态的诊断快照（开发诊断用，不作为正式业务 API）。
        /// </summary>
        /// <param name="isSettling">是否已进入 Settling（由调用方传入）。</param>
        /// <returns>不可变诊断快照。</returns>
        /// <remarks>
        /// <para><b>不作为正式业务 API（design.md 第 226 行）：</b>诊断快照仅供 Editor 调试视图
        /// 与本地问题排查使用，不参与正式黄金对照。</para>
        /// <para><b>稳定序列化：</b>与 <see cref="CaptureSnapshot"/> 规则一致，
        /// 排除对象地址、Dictionary 未定义顺序与真实时间噪声。</para>
        /// </remarks>
        internal BattleDebugSnapshot CaptureDebugSnapshot(bool isSettling)
        {
            // 逐池统计延后为已批准偏差，详见 design.md "已批准偏差"表。
            List<BattleTraceSnapshot.PoolStatTraceRow> poolStats =
                new List<BattleTraceSnapshot.PoolStatTraceRow>(0);

            return new BattleDebugSnapshot(
                frameNowMs: _simulation.FrameNowMs,
                elapsedGameTimeMs: _simulation.ElapsedGameTimeMs,
                enemyCount: _enemyManager.Count,
                enemySpatialCellCount: _enemyManager.SpatialCellCount,
                projectileActiveCount: _projectileManager.ActiveCount,
                projectileUpdateCount: _projectileManager.UpdateCount,
                attackEffectActiveCount: _attackEffectManager.ActiveCount,
                attackEffectUpdateCount: _attackEffectManager.UpdateCount,
                unitCount: _unitRegistry.Count,
                unitPlayerCount: _unitRegistry.PlayerSoldierCount,
                reservationCount: _reservationRegistry.Count,
                poolTypeCount: _poolScope.PoolCount,
                currentRound: _readModel.CurrentRound,
                playerHealth: _readModel.PlayerHealth,
                opponentHealth: _readModel.OpponentHealth,
                playerGold: _readModel.PlayerGold,
                opponentGold: _readModel.OpponentGold,
                killCount: _readModel.KillCount,
                isFrozen: _simulation.IsFrozen,
                isResultFrozen: _resultBuilder.IsFrozen,
                isSettling: isSettling,
                poolStats: poolStats);
        }

        // ====================================================================
        // 已收集轨迹访问
        // ====================================================================

        /// <summary>
        /// 获取已收集轨迹行的只读视图（按调用顺序，不排序）。
        /// </summary>
        /// <returns>轨迹行只读列表。</returns>
        /// <remarks>
        /// 列表顺序 = <see cref="Record"/> 调用顺序 = 阶段边界顺序，确定。
        /// 返回的只读列表不可修改内部记录。
        /// </remarks>
        internal IReadOnlyList<BattleTraceSnapshot> GetRecords()
        {
            return _records;
        }

        /// <summary>
        /// 将全部已收集轨迹行序列化为文本（每行以 '--- trace[N] ---' 分隔）。
        /// </summary>
        /// <returns>全部轨迹行的稳定文本表示。</returns>
        /// <remarks>
        /// <para><b>固定顺序：</b>按 <see cref="Record"/> 调用顺序（索引升序）输出，
        /// 不排序（排序会破坏时间因果链）。</para>
        /// <para>每行轨迹的内部字段顺序由 <see cref="BattleTraceSnapshot.SerializeToText"/> 保证固定。</para>
        /// </remarks>
        internal string SerializeAllToText()
        {
            var sb = new System.Text.StringBuilder(_records.Count * 512);
            for (int i = 0; i < _records.Count; i++)
            {
                sb.Append("--- trace[").Append(i).Append("] ---\n");
                sb.Append(_records[i].SerializeToText());
            }
            return sb.ToString();
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 清空已收集的轨迹行。供 Settling 静默清理或测试重置使用。
        /// </summary>
        internal void Clear()
        {
            _records.Clear();
        }

        // ====================================================================
        // 实体集合收集（按 Id 排序，排除 Dictionary 未定义顺序）
        // --------------------------------------------------------------------
        // 从 Manager 只读快照方法获取集合，拷贝为轨迹行后按 Id 排序。
        // 只记录 RuntimeId 与标量，不记录对象地址。
        //
        // 可访问性说明：
        //   - EnemyManager.GetOrderedIdsSnapshot() / GetById(id) 已存在，可获取全部标量。
        //   - ProjectileManager.GetProjectilesSnapshot() 已存在；ProjectileBase 暴露
        //     ProjectileId/AttackerId/X/Y/IsActive/TargetId（TargetId 为虚属性，
        //     SimpleDynamicArrow 重写为 Movement.TargetId，基类返回 -1）。
        //   - AttackEffectManager.GetEffectsSnapshot() 已存在；IAttackEffect 只暴露
        //     Active 与 Owner（IAttackEffectOwner）。EffectId/Kind/ElapsedMs 未在接口暴露，
        //     故 EffectId 记 0、Kind 记 0、ElapsedMs 记 0（已批准偏差，见文件头注释）。
        //     OwnerId 从 Owner.RuntimeId 读取。
        //   - 上述占位不影响 task 8.1 稳定序列化目标：相同状态产生相同输出，
        //     对照工具按字段容差比较。后续如需精确值，可在 IAttackEffect 扩展只读属性后补全。
        // ====================================================================

        /// <summary>
        /// 从 EnemyManager 收集敌人轨迹行并按 Id 升序排序。
        /// </summary>
        private List<BattleTraceSnapshot.EnemyTraceRow> CollectEnemyRows()
        {
            IReadOnlyList<int> orderedIds = _enemyManager.GetOrderedIdsSnapshot();
            var rows = new List<BattleTraceSnapshot.EnemyTraceRow>(orderedIds.Count);

            foreach (int id in orderedIds)
            {
                IEnemyEntity enemy = _enemyManager.GetById(id);
                if (enemy == null)
                {
                    continue;
                }

                rows.Add(new BattleTraceSnapshot.EnemyTraceRow(
                    id: enemy.Id,
                    isPlayerLane: enemy.IsPlayerLane,
                    state: enemy.CurrentState,
                    x: enemy.X,
                    y: enemy.Y,
                    health: enemy.Health,
                    pathIndex: enemy.CurrentPathIndex,
                    remainingPathDistance: enemy.RemainingPathDistance));
            }

            // 排除 Dictionary 未定义顺序：按 Id 升序排序。
            // _orderedIds 已是 spawn 顺序，但黄金对照要求按 Id 升序（确定性），
            // 故显式排序以保证跨端一致。
            rows.Sort((a, b) => a.Id.CompareTo(b.Id));
            return rows;
        }

        /// <summary>
        /// 从 ProjectileManager 收集投射物轨迹行并按 Id 升序排序。
        /// </summary>
        private List<BattleTraceSnapshot.ProjectileTraceRow> CollectProjectileRows()
        {
            IReadOnlyList<ProjectileBase> projectiles = _projectileManager.GetProjectilesSnapshot();
            var rows = new List<BattleTraceSnapshot.ProjectileTraceRow>(projectiles.Count);

            foreach (ProjectileBase projectile in projectiles)
            {
                if (projectile == null)
                {
                    continue;
                }

                // TargetId 从 ProjectileBase.TargetId 虚属性采集真实值
                // （SimpleDynamicArrow 重写为 Movement.TargetId；基类/未绑定返回 -1）。
                rows.Add(new BattleTraceSnapshot.ProjectileTraceRow(
                    id: projectile.ProjectileId,
                    attackerId: projectile.AttackerId,
                    targetId: projectile.TargetId,
                    x: projectile.X,
                    y: projectile.Y,
                    active: projectile.IsActive));
            }

            rows.Sort((a, b) => a.Id.CompareTo(b.Id));
            return rows;
        }

        /// <summary>
        /// 从 AttackEffectManager 收集攻击效果轨迹行并按 OwnerId 升序排序。
        /// </summary>
        private List<BattleTraceSnapshot.AttackEffectTraceRow> CollectAttackEffectRows()
        {
            IReadOnlyList<IAttackEffect> effects = _attackEffectManager.GetEffectsSnapshot();
            var rows = new List<BattleTraceSnapshot.AttackEffectTraceRow>(effects.Count);

            foreach (IAttackEffect effect in effects)
            {
                if (effect == null)
                {
                    continue;
                }

                // IAttackEffect 只暴露 Active 与 Owner（IAttackEffectOwner）。
                // EffectId/Kind/ElapsedMs 未在接口暴露，记 0 占位（已批准偏差，见文件头注释）。
                // OwnerId 从 Owner.RuntimeId 读取（若 Owner 为 IAttackEffectOwner）。
                int ownerId = -1;
                if (effect.Owner is IAttackEffectOwner owner)
                {
                    ownerId = owner.RuntimeId;
                }

                rows.Add(new BattleTraceSnapshot.AttackEffectTraceRow(
                    effectId: 0,
                    ownerId: ownerId,
                    kind: 0,
                    active: effect.Active,
                    elapsedMs: 0));
            }

            // 按 OwnerId 升序；同 OwnerId 按 EffectId(=0) 升序。
            rows.Sort((a, b) =>
            {
                int cmp = a.OwnerId.CompareTo(b.OwnerId);
                return cmp != 0 ? cmp : a.EffectId.CompareTo(b.EffectId);
            });
            return rows;
        }

        // ====================================================================
        // 待上场/战场槽位单位转换（按 UnitId 升序排序，替代旧手牌）
        // ====================================================================

        /// <summary>
        /// 将指定阵营的槽位占用单位转换为轨迹行并按 UnitId 升序排序（修复 P0 替代旧手牌）。
        /// </summary>
        private List<BattleTraceSnapshot.UnitCardTraceRow> ToSlotRows(bool isPlayerSide)
        {
            var rows = new List<BattleTraceSnapshot.UnitCardTraceRow>();
            if (_slotBoard != null)
            {
                foreach (UnitSlot slot in _slotBoard.GetAllSlots())
                {
                    if (slot.SlotId.Side != isPlayerSide || !slot.Occupant.HasValue)
                    {
                        continue;
                    }

                    BattleUnit unit = slot.Occupant.Value;
                    rows.Add(new BattleTraceSnapshot.UnitCardTraceRow(
                        cardId: unit.UnitId,
                        soldierText: unit.SoldierText,
                        level: unit.Level,
                        cost: 0,
                        isPlayerSide: unit.Side));
                }
            }

            // 按 UnitId 升序排序，排除集合未定义顺序。
            rows.Sort((a, b) => a.CardId.CompareTo(b.CardId));
            return rows;
        }
    }
}
