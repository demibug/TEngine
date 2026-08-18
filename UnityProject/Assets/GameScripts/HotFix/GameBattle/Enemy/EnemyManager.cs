using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.6：EnemyManager —— 敌人集合、空间索引、目标查询、伤害提交、移除队列与幂等清理
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Enemy/EnemyManager.cs）：
    //   维护敌人集合和空间索引，提供稳定查询、伤害入口、生成与清理。
    //
    // 来源证据（EnemyManager.js:1-317）：
    //   还原工程 EnemyManager 继承 SingletonBase，由 GameLoop 字符串 callback Map 驱动。
    //   核心数据结构：
    //     - enemies: Map<id, enemy>                      // 主集合
    //     - updateBuffer: array                          // 更新快照缓冲，避免遍历中修改
    //     - queryBuffer: array                           // 查询复用缓冲
    //     - cellToEnemyIds: Map<cellKey, Set<id>>        // 空间单元→敌人 ID 集合
    //     - enemyIdToCell: Map<id, cellKey>              // 敌人 ID→所在单元
    //     - gridSize: 80                                 // 空间单元边长（= map.gridWidth）
    //   核心方法：
    //     - update(deltaMs): 快照遍历 enemies，跳过 DEAD/SPAWNING，调用 enemy.update
    //     - spawn/spawnByKey: 经 factory.create 创建并 init、登记到集合与空间索引
    //     - queryTargets/queryEnemyObjects/queryAroundEnemy: 空间单元扫描 + circleIntersectsRect 精筛
    //     - applyDamage(damage, targetDtos, attacker): 按 DTO.id 查找敌人并调用 enemy.hit
    //     - forceRemove(id): 调用 enemy.gameOver()
    //     - closestToEnd/randomTarget/lowestHealthTarget/frontmostPathPosition: 选择性目标查询
    //     - gameOver(): 清空全部集合与索引（幂等）
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase / CombatServices，改为强类型注入的 internal 类。
    //   - design.md 决策 4 / spec battle-event-boundary：敌人注册、空间索引、伤害、回收等
    //     一致性操作使用直接调用，不通过全局事件总线。
    //   - task 4.6 约束：稳定有序集合，禁止 Dictionary/HashSet 的未定义遍历顺序决定目标。
    //   - spec battle-simulation "Simulation is reproducible"：不依赖无序集合遍历决定目标、
    //     伤害和胜负。
    //   - 决策 0.3：Enemy 阶段执行顺序已冻结（BattleUpdatePhase.Enemy）。
    //   - 决策 0.4：TryFreeze 后中止——本类型在更新遍历中若检测到冻结标志，停止剩余迭代。
    //
    // 稳定有序集合设计（task 4.6 核心约束）：
    //   JS 原版 enemies 为 Map<id, enemy>，Map 保留插入顺序，遍历顺序确定。
    //   C# Dictionary<TKey,TValue> 在 .NET 中按插入顺序遍历是"通常但不保证"的行为，
    //   HashSet<T> 遍历顺序更是未定义。为满足"禁止依赖未定义遍历顺序决定目标"的约束，
    //   本类型采用：
    //     1. _enemiesById: Dictionary<int, IEnemyEntity>  —— 仅用于 O(1) 按 ID 查找，
    //        查找结果与遍历顺序无关，不用于决定目标选择。
    //     2. _orderedIds: List<int>                       —— 按 spawn 登记顺序的稳定 ID 序列，
    //        所有需要确定性遍历的查询（closestToEnd / randomTarget / lowestHealthTarget /
    //        frontmostPathPosition / update）都基于此列表遍历。移除时从列表删除（O(n)，
    //        单局敌人数量可控，可接受）。
    //     3. _cellToEnemyIds: Dictionary<string, List<int>> —— 空间单元→有序 ID 列表。
    //        使用 List<int> 而非 HashSet<int>，保证空间扫描的候选顺序确定。
    //        空间查询的最终结果不依赖单元格遍历顺序——queryTargets 等方法对候选做
    //        circleIntersectsRect 精筛后返回，closestToEnd 等方法对候选排序后选择，
    //        排序键（remainingPathDistance / health / currentPathIndex）是确定性的。
    //
    // 并行任务契约推断（task 54/55/56/57 同批次并行）：
    //   本类型引用的 EnemyBase / Mob0Enemy / EnemyFactory / BattleTarget 类型由并行任务创建，
    //   当前尚未存在。为使本类型可独立编译与测试，定义最小契约接口 IEnemyEntity，
    //   EnemyBase（task 4.3）将实现该接口。接口成员依据 EnemyBase.js 推断：
    //     - Id / IsPlayerLane / Targetable / CurrentState / Health
    //     - RemainingPathDistance / CurrentPathIndex
    //     - X / Y / Width / Height（逻辑位置，替代 JS visual.x/y/width/height）
    //     - Update / Hit / GameOver / IsTargetableBy
    //
    // 不变量：
    //   1. 稳定有序：所有目标选择遍历基于 _orderedIds（插入顺序），不依赖 Dictionary/HashSet 遍历顺序。
    //   2. 空间索引一致性：登记的敌人必有单元映射；移除时同步清除单元映射。
    //   3. 幂等清理：Clear / GameOver 可重复调用，后续调用为空操作。
    //   4. 移除队列：同子步内多个死亡请求不重入修改集合，先入队再统一处理。
    // ============================================================================

    /// <summary>
    /// 敌人运行时实体最小契约：EnemyManager 依赖的敌人属性与行为。
    /// </summary>
    /// <remarks>
    /// <para><b>契约来源（EnemyBase.js 推断）：</b>
    /// EnemyBase（task 4.3 并行创建）将实现本接口。接口成员对应 EnemyBase.js 的字段：</para>
    /// <para><b>本接口为 internal：</b>只供 GameBattle 内部 EnemyManager 与测试使用。</para>
    /// </remarks>
    internal interface IEnemyEntity
    {
        /// <summary>运行时 ID（由 RuntimeIdAllocator 分配，池复用不复用旧 ID）。</summary>
        int Id { get; }

        /// <summary>是否玩家方车道（对应 isPlayerLane）。</summary>
        bool IsPlayerLane { get; }

        /// <summary>
        /// 当前运行时状态（对应 currentState）。枚举值见 EnemyBase.js:12-18：
        /// 0=SPAWNING, 1=MOVING, 2=SKILL, 3=STUNNED, 4=DEAD。
        /// </summary>
        int CurrentState { get; }

        /// <summary>逻辑位置 X（对应 visual.x，C# 移植为纯逻辑属性）。</summary>
        float X { get; }

        /// <summary>逻辑位置 Y（对应 visual.y）。</summary>
        float Y { get; }

        /// <summary>逻辑宽度（对应 visual.width，用于中心点与空间单元计算）。</summary>
        float Width { get; }

        /// <summary>逻辑高度（对应 visual.height）。</summary>
        float Height { get; }

        /// <summary>投射物瞄准点 X 偏移（相对 X；表现锚点，默认 0 表示矩形左边缘）。</summary>
        float ProjectileAimOffsetX { get; }

        /// <summary>投射物瞄准点 Y 偏移（相对 Y；表现锚点，默认 0 表示矩形上边缘）。</summary>
        float ProjectileAimOffsetY { get; }

        /// <summary>剩余路径距离（对应 remainingPathDistance，Infinity 表示未初始化）。</summary>
        float RemainingPathDistance { get; }

        /// <summary>当前路径点索引（对应 currentPathIndex）。</summary>
        int CurrentPathIndex { get; }

        /// <summary>当前血量（对应 health）。</summary>
        int Health { get; }

        /// <summary>
        /// 最大血量（对应 maxHealthBase，由子类初始化时设置）。
        /// <para>供表现层计算真实血量比例（current / max），替代存活/死亡二值。</para>
        /// </summary>
        int MaxHealth { get; }

        /// <summary>
        /// 推进一帧（对应 enemy.update(deltaMs)）。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        void Update(long deltaMs);

        /// <summary>
        /// 受击（对应 enemy.hit(damage, attacker)）。返回是否实际扣血。
        /// </summary>
        /// <param name="damage">伤害值（正数）。</param>
        /// <param name="attackerId">攻击者运行时 ID（无攻击者传 -1）。</param>
        /// <returns>true=本次受击生效（血量大于 0 且扣血成功）；false=未生效（已死亡或无效）。</returns>
        bool Hit(int damage, int attackerId);

        /// <summary>
        /// 强制结束并回收（对应 enemy.gameOver()）。幂等：重复调用返回 false。
        /// </summary>
        /// <returns>true=首次回收成功；false=已回收或无效。</returns>
        bool GameOver();

        /// <summary>
        /// 是否可被指定阵营攻击（对应 enemy.isTargetableBy(playerLane)）。
        /// </summary>
        /// <param name="playerSide">true=玩家方攻击者，false=对手方攻击者。</param>
        /// <returns>非 SPAWNING/DEAD 且阵营匹配且 targetable 时返回 true。</returns>
        bool IsTargetableBy(bool playerSide);
    }

    /// <summary>
    /// 波次拥有的敌对实体窄契约：暴露当前租借身份与波次实体种类（Normal/Boss）。
    /// </summary>
    /// <remarks>
    /// <para>design.md 决策 1 / specs/zhang-liang-boss-runtime/spec.md：普通配置敌人
    /// 返回 <see cref="WaveEntityKind.Normal"/>，<see cref="BossBase"/> 返回
    /// <see cref="WaveEntityKind.Boss"/>。EnemyManager 移除事实据此发布完整
    /// <see cref="WaveEntityHandle"/>（runtimeId + generation + waveOrder + kind），
    /// WaveManager 以完整值幂等匹配，stale generation/kind 的迟到事实被忽略。</para>
    /// <para>本接口为 internal：只供 GameBattle 内部 EnemyManager 与 Boss 实现使用。</para>
    /// </remarks>
    internal interface IWaveOwnedEnemyEntity : IEnemyEntity
    {
        /// <summary>当前租借身份（runtimeId + generation + waveOrder；池复用后更新）。</summary>
        EnemyLeaseIdentity CurrentLease { get; }

        /// <summary>波次实体种类（普通敌人=Normal，Boss=Boss）。</summary>
        WaveEntityKind WaveKind { get; }
    }

    /// <summary>
    /// 敌人目标查询结果 DTO（对应 JS <c>{id, x, y, Bm}</c>）。
    /// </summary>
    /// <remarks>
    /// <para>供攻击调度 / 投射物命中策略 / 近战效果等消费的只读目标信息。
    /// 字段对应 EnemyManager.js:202/225/259/279 的查询返回结构。</para>
    /// <para>不可变值类型，避免在查询结果传递中暴露敌人实体引用。</para>
    /// </remarks>
    internal readonly struct EnemyTargetDto
    {
        /// <summary>敌人运行时 ID。</summary>
        public readonly int Id;

        /// <summary>逻辑位置 X。</summary>
        public readonly float X;

        /// <summary>逻辑位置 Y。</summary>
        public readonly float Y;

        /// <summary>剩余路径距离（对应 Bm，越小越接近终点）。</summary>
        public readonly float RemainingPathDistance;

        /// <summary>构造目标 DTO。</summary>
        internal EnemyTargetDto(int id, float x, float y, float remainingPathDistance)
        {
            Id = id;
            X = x;
            Y = y;
            RemainingPathDistance = remainingPathDistance;
        }

        /// <summary>无效目标哨兵（对应 JS <c>{id: -1, x: 0, y: 0, Bm: Infinity}</c>）。</summary>
        internal static EnemyTargetDto Invalid =>
            new EnemyTargetDto(-1, 0f, 0f, float.PositiveInfinity);

        /// <summary>是否有效（Id &gt; 0）。</summary>
        internal bool IsValid => Id > 0;
    }

    /// <summary>
    /// 敌人集合与空间索引管理器：提供稳定有序查询、伤害提交、移除队列与幂等清理。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>维护敌人集合和空间索引，提供稳定查询、
    /// 伤害入口、生成与清理。替代还原工程 <c>EnemyManager.js</c> 全局单例
    /// （<c>EnemyManager.js:23-315</c>）。</para>
    ///
    /// <para><b>稳定有序集合（task 4.6 核心约束）：</b>
    /// 所有目标选择遍历基于 <c>_orderedIds</c>（按 spawn 登记顺序的 List），不依赖
    /// Dictionary/HashSet 的未定义遍历顺序。空间单元内使用 <c>List&lt;int&gt;</c> 而非
    /// HashSet，保证候选顺序确定。最终目标选择基于确定性排序键
    /// （remainingPathDistance / health / currentPathIndex）。</para>
    ///
    /// <para><b>更新阶段（决策 0.3 / spec "Update phases are explicit"）：</b>
    /// <see cref="Update"/> 由 <see cref="BattleSimulation"/> 在
    /// <see cref="BattleUpdatePhase.Enemy"/> 阶段回调，每子步一次。位移读 stepMs，
    /// 接触冷却读 frameNowMs（由 EnemyBase 内部管理，本类型只转交 stepMs）。</para>
    ///
    /// <para><b>冻结中止（决策 0.4 / spec "Battle result is frozen once"）：</b>
    /// 若 <see cref="IsFrozen"/> 为 true，<see cref="Update"/> 直接返回，不推进剩余敌人。
    /// 冻结标志由外部（BattleRuntime.Settling）设置。本类型不在遍历内重入销毁集合，
    /// 移除请求入 <c>_removeQueue</c>，由 <see cref="ProcessRemoveQueue"/> 统一处理。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 EnemyManager。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleRuntime 在阶段回调中调用，
    /// 不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class EnemyManager
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>默认空间单元边长（对应 EnemyManager.js:32 gridSize=80）。</summary>
        public const int DefaultGridSize = 80;

        /// <summary>SPAWNING 状态值（EnemyBase.js:12）。</summary>
        private const int StateSpawning = 0;

        /// <summary>DEAD 状态值（EnemyBase.js:17）。</summary>
        private const int StateDead = 4;

        // ====================================================================
        // 配置（不可变，构造时注入）
        // ====================================================================

        /// <summary>
        /// 空间单元边长（对应 EnemyManager.js:32 gridSize，由 map.gridWidth 初始化）。
        /// <para>用于空间单元索引：cellX = floor(centerX / gridSize)。</para>
        /// </summary>
        private readonly int _gridSize;

        /// <summary>
        /// 随机源（对应 EnemyManager.js:44 randomSource）。
        /// <para>用于 <see cref="RandomTarget"/> 选择随机目标。注入确定性随机源保证可复现。</para>
        /// </summary>
        private readonly Func<float> _randomSource;

        /// <summary>本局 Buff 所有者；未装配时保持旧测试行为。</summary>
        private readonly BuffManager _buffManager;

        // ====================================================================
        // 主集合：按 ID 查找 + 有序 ID 列表
        // ====================================================================

        /// <summary>
        /// 按 ID 查找敌人（对应 enemies: Map&lt;id, enemy&gt;）。
        /// <para><b>仅用于 O(1) 按 ID 查找</b>，查找结果与遍历顺序无关，不用于决定目标选择。
        /// 目标选择遍历基于 <see cref="_orderedIds"/>。</para>
        /// </summary>
        private readonly Dictionary<int, IEnemyEntity> _enemiesById =
            new Dictionary<int, IEnemyEntity>();

        /// <summary>
        /// 按 spawn 登记顺序的稳定 ID 序列（task 4.6 核心约束）。
        /// <para>所有需要确定性遍历的查询（closestToEnd / randomTarget / lowestHealthTarget /
        /// frontmostPathPosition / Update）都基于此列表遍历。移除时从列表删除。</para>
        /// <para>对应 JS Map 的插入顺序遍历——JS Map 保留插入顺序，C# Dictionary 不保证，
        /// 故显式维护 List 保证确定性。</para>
        /// </summary>
        private readonly List<int> _orderedIds = new List<int>();

        // ====================================================================
        // 更新快照缓冲（对应 EnemyManager.js:27 updateBuffer）
        // ====================================================================

        /// <summary>
        /// 更新快照缓冲：遍历前复制 _orderedIds，避免遍历中集合修改导致迭代异常。
        /// <para>对应 EnemyManager.js:70 <c>this.updateBuffer.length = 0; for (...) push</c>。</para>
        /// </summary>
        private readonly List<int> _updateBuffer = new List<int>();

        // ====================================================================
        // 空间索引：单元格→有序 ID 列表 + ID→单元格
        // ====================================================================

        /// <summary>
        /// 空间单元→敌人 ID 有序列表（对应 cellToEnemyIds: Map&lt;cellKey, Set&lt;id&gt;&gt;）。
        /// <para>使用 List&lt;int&gt; 而非 HashSet&lt;int&gt;，保证空间扫描的候选顺序确定。
        /// 空间查询的最终结果不依赖单元格遍历顺序——queryTargets 对候选做 circleIntersectsRect
        /// 精筛，closestToEnd 等对候选按确定性键排序后选择。</para>
        /// </summary>
        private readonly Dictionary<string, List<int>> _cellToEnemyIds =
            new Dictionary<string, List<int>>();

        /// <summary>
        /// 敌人 ID→所在单元键（对应 enemyIdToCell: Map&lt;id, cellKey&gt;）。
        /// <para>用于移除时快速定位并清除单元映射，避免遍历全部单元。</para>
        /// </summary>
        private readonly Dictionary<int, string> _enemyIdToCell =
            new Dictionary<int, string>();

        // ====================================================================
        // 租借身份索引（design.md 决策 5 / task 3.6/3.7）
        // ====================================================================

        /// <summary>
        /// 敌人运行时 ID → 该 ID 当前登记时记录的租借身份。
        /// <para>仅用于诊断与移除事实；移除队列处理时的世代守卫读取的是
        /// <b>实体自身的当前租借身份</b>（<see cref="GetCurrentLease"/>），
        /// 而非本索引，以防池复用后本索引滞后。</para>
        /// </summary>
        private readonly Dictionary<int, EnemyLeaseIdentity> _leaseById =
            new Dictionary<int, EnemyLeaseIdentity>();

        // ====================================================================
        // 移除队列：同子步死亡请求先入队，遍历结束后统一处理
        // ====================================================================

        /// <summary>
        /// 延迟移除队列：移除请求入队，遍历结束后由 ProcessRemoveQueue 统一处理。
        /// <para>对应决策 0.4 "TryFreeze 不在嵌套伤害调用栈内重入销毁集合"——
        /// 同子步内多个移除请求不重入修改集合，先入队再统一处理。</para>
        /// <para>队列元素携带 <see cref="EnemyLeaseIdentity"/>（generation-aware 租借身份，
        /// task 3.6/3.7）+ <see cref="EnemyRemovalReason"/>。同一 ID 无论重复请求多少次都只
        /// 入队一次；处理时核对当前实体租借身份，旧世代迟到请求幂等忽略
        /// （spec "Ignore a stale removal callback"）。</para>
        /// </summary>
        private readonly List<(EnemyLeaseIdentity identity, EnemyRemovalReason reason)> _removeQueue =
            new List<(EnemyLeaseIdentity identity, EnemyRemovalReason reason)>();

        /// <summary>敌人完成登记后的低频表现事实（携带不可变出生表现 DTO）。</summary>
        /// <remarks>
        /// <para><b>task 5.1：</b>出生参数收敛为 <see cref="EnemySpawnViewData"/>（runtimeId +
        /// enemyKey/resourceAddress + 车道 + 逻辑坐标），表现层不再接收散参数。</para>
        /// <para>配置化普通敌人（<see cref="ConfiguredEnemyBase"/>）携带其固定 EnemyKey 与
        /// 本次租借注入的 ResourceAddress；测试替身/非普通实体为空串（Unity 端口显式失败，
        /// 禁止静默回退 Mob0）。</para>
        /// </remarks>
        internal event Action<EnemySpawnViewData> EnemySpawned;

        /// <summary>敌人从活动集合移除后的低频表现事实。</summary>
        internal event Action<int, bool> EnemyRemoved;

        /// <summary>Boss 技能开始/结束的低频表现意图。</summary>
        internal event Action<int, string, bool> BossSkillIntentChanged;

        /// <summary>
        /// 波次所有权内部移除事实：敌人以指定原因离场（恰好一次）。
        /// </summary>
        /// <remarks>
        /// <para><b>内部事实（design.md 决策 5 / task 3.7/5.2）：</b>携带完整
        /// <see cref="WaveEntityHandle"/>（runtimeId + generation + waveOrder + kind，
        /// kind 来自 <see cref="IWaveOwnedEnemyEntity.WaveKind"/>：普通敌人=Normal、
        /// Boss=Boss）与 <see cref="EnemyRemovalReason"/>（Killed/ReachedEndPoint/
        /// Forced 可区分），供下一波 WaveManager 以完整 handle 幂等解除波次活动计数，
        /// 抵抗池复用迟到回调。本事实不承载表现：既有 <see cref="EnemyRemoved"/>
        /// 表现事实签名不变。</para>
        /// <para><b>恰好一次：</b>由 <see cref="ProcessRemoveQueue"/>（入队请求）与
        /// <see cref="GameOver"/>（战斗结束批量清理，原因 Forced）保证每个敌人只触发一次；
        /// 旧世代迟到请求在队列处理时被世代守卫忽略，不触发本事实。</para>
        /// <para><b>不引入全局 GameEvent：</b>本事件为单局内部一对多事实，不跨程序集
        /// 广播（design 决策 4：内部一致性优先直接调用）。</para>
        /// </remarks>
        internal event Action<WaveEntityHandle, EnemyRemovalReason> WaveEntityRemoved;

        /// <summary>敌人血量变化后的低频表现事实。</summary>
        /// <remarks>
        /// <para>仅在敌人真正受击扣血后触发（低频，非每帧），参数依次为
        /// runtimeId / currentHealth / maxHealth / delta（delta 为负=受伤）。
        /// 由 <see cref="EnemyBase.Hit"/> 成功扣血后经本事件转发给表现层，
        /// 供敌人头顶血条按真实比例更新并触发"显示—延时隐藏"。</para>
        /// <para>死亡时（血量归零）也会触发一次，此时 currentHealth=0，
        /// 表现层据此立即隐藏血条并复位。</para>
        /// </remarks>
        internal event Action<int, int, int, int> EnemyHealthChanged;

        /// <summary>
        /// 敌军归还对象池回调：由 BattleRuntimeFactory 装配时桥接到 EnemyFactory.Release。
        /// <para>在敌军从活动集合注销后调用，保证每次 Acquire 恰好对应一次 Release
        /// （池租借对称契约）。同一 ID 由 <see cref="ProcessRemoveQueue"/> / <see cref="GameOver"/>
        /// 保证只归还一次。</para>
        /// </summary>
        internal Action<IEnemyEntity> ReleaseEnemy { get; set; }

        // ====================================================================
        // 冻结标志（决策 0.4）
        // ====================================================================

        /// <summary>
        /// 是否已冻结。冻结后 Update 直接返回，不再推进剩余敌人。
        /// <para>由外部（BattleRuntime.EnterSettling）设置。对应决策 0.4
        /// "TryFreeze 后中止当前 phase 剩余迭代"。</para>
        /// </summary>
        internal bool IsFrozen { get; set; }

        /// <summary>
        /// 是否已清理（Clear/GameOver 已调用）。幂等清理标志。
        /// </summary>
        internal bool IsCleared { get; private set; }

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>当前敌人数量（对应 enemies.size）。</summary>
        internal int Count => _enemiesById.Count;

        /// <summary>空间单元数量（对应 spatialCellCount）。</summary>
        internal int SpatialCellCount => _cellToEnemyIds.Count;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造敌人管理器，注入空间单元边长与随机源。
        /// </summary>
        /// <param name="gridSize">
        /// 空间单元边长（对应 map.gridWidth=80）。用于空间单元索引。
        /// </param>
        /// <param name="randomSource">
        /// 随机源（对应 randomSource=Math.random）。用于 RandomTarget。
        /// 若为 null 则使用默认 <see cref="DefaultRandom"/>（0..1）。
        /// </param>
        /// <remarks>
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在每次 Create 时构造新实例，
        /// 保证每局独立。gridSize 从 <see cref="MapData"/> 的等价配置注入。</para>
        /// <para><b>并行任务契约推断：</b>MapData 当前未暴露 GridWidth 属性，
        /// 调用方（Factory）从配置快照的等价字段注入。task 4.3/4.4 的 EnemyBase/Mob0Enemy
        /// 实现后，spawn 流程由 EnemyFactory（task 4.5）接入。</para>
        /// </remarks>
        internal EnemyManager(
            int gridSize = DefaultGridSize,
            Func<float> randomSource = null,
            BuffManager buffManager = null)
        {
            _gridSize = gridSize > 0 ? gridSize : DefaultGridSize;
            _randomSource = randomSource ?? DefaultRandom;
            _buffManager = buffManager;
            IsFrozen = false;
            IsCleared = false;
        }

        // ====================================================================
        // 登记 / 注销 —— 由 EnemyFactory/EnemyBase.init 调用
        // --------------------------------------------------------------------
        // 对应 EnemyManager.js:126-134 _onEnemyRegistered / _onEnemyRemoved。
        // C# 移植不通过 EventBus，改为直接调用（design 决策 4：一致性操作使用直接调用）。
        // ====================================================================

        /// <summary>
        /// 登记一个已初始化的敌人到集合与空间索引（对应 _onEnemyRegistered）。
        /// </summary>
        /// <param name="enemy">已 init 的敌人实体（非 null，Id &gt; 0）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="enemy"/> 为 null。</exception>
        /// <exception cref="ArgumentException">敌人 Id 已存在或 Id &lt;= 0。</exception>
        /// <remarks>
        /// <para>由 EnemyFactory.Acquire + EnemyBase.init 后调用。登记后敌人参与
        /// Update 推进与目标查询。</para>
        /// <para><b>稳定有序：</b>Id 追加到 <see cref="_orderedIds"/> 末尾，保证遍历顺序 = spawn 顺序。</para>
        /// </remarks>
        internal void Register(IEnemyEntity enemy)
        {
            if (enemy == null)
            {
                throw new ArgumentNullException(nameof(enemy));
            }

            int id = enemy.Id;
            if (id <= 0)
            {
                throw new ArgumentException($"敌人 Id 必须 > 0，实际 {id}", nameof(enemy));
            }

            if (_enemiesById.ContainsKey(id))
            {
                throw new ArgumentException($"敌人 Id={id} 已登记，不可重复登记", nameof(enemy));
            }

            if (_buffManager != null && enemy is IBuffTarget buffTarget)
            {
                BuffOperationResult result = _buffManager.RegisterTarget(buffTarget);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"敌人 Id={id} 注册 Buff 目标失败：{result.Status} {result.Message}");
                }
            }

            _enemiesById[id] = enemy;
            _orderedIds.Add(id);
            IndexEnemy(id, enemy);
            _leaseById[id] = GetCurrentLease(enemy, id);

            // 注入血量变化回调：受击扣血后经本管理器统一转发低频表现事实。
            if (enemy is EnemyBase enemyBase)
            {
                enemyBase.SetHealthChangedCallback((changedId, current, max, delta) =>
                    EnemyHealthChanged?.Invoke(changedId, current, max, delta));
            }

            if (enemy is BossBase registeredBoss)
            {
                registeredBoss.SkillIntentChanged += OnBossSkillIntentChanged;
            }

            // task 5.1：出生表现 DTO 贯通。配置化普通敌人携带固定 EnemyKey 与本次租借注入
            // 的 ResourceAddress；Boss 携带 BossKey 与资源路径；其余实体（测试替身/技能召唤
            // 占位）为空串，表现端口显式失败而非静默回退 Mob0。
            string enemyKey = string.Empty;
            string resourceAddress = string.Empty;
            if (enemy is BossBase boss)
            {
                resourceAddress = boss.ResourceAddress;
            }
            else if (enemy is ConfiguredEnemyBase configured)
            {
                enemyKey = configured.EnemyKey;
                resourceAddress = configured.ResourceAddress;
            }

            BossDefinitionSnapshot bossDefinition = (enemy as BossBase)?.Definition;
            EnemySpawned?.Invoke(new EnemySpawnViewData(
                id,
                enemyKey,
                resourceAddress,
                enemy.IsPlayerLane,
                enemy.X,
                enemy.Y,
                bossDefinition == null ? EnemyPresentationKind.Normal : EnemyPresentationKind.Boss,
                bossDefinition?.Key,
                bossDefinition?.LogicalWidth ?? 0f,
                bossDefinition?.LogicalHeight ?? 0f,
                bossDefinition?.IdleAnimation,
                bossDefinition?.AttackAnimation));
        }

        /// <summary>
        /// 读取实体当前租借身份：波次拥有的敌对实体（配置化敌人/Boss）返回其
        /// <see cref="IWaveOwnedEnemyEntity.CurrentLease"/>，其余实体（测试替身/旧链集成敌）
        /// 返回合成身份（generation=0、waveOrder=0）。
        /// </summary>
        /// <remarks>
        /// <para><b>世代守卫的数据源（design.md 决策 5）：</b>移除队列处理时读取的是
        /// 实体<b>当前</b>租借身份，而非登记时索引——池复用后实体被重新租借（新 runtimeId/
        /// generation/waveOrder），实体自身身份与旧登记/旧请求不一致时即视为迟到请求。</para>
        /// </remarks>
        private static EnemyLeaseIdentity GetCurrentLease(IEnemyEntity enemy, int id)
        {
            if (enemy is IWaveOwnedEnemyEntity waveOwned)
            {
                return waveOwned.CurrentLease;
            }

            return new EnemyLeaseIdentity(id, 0, 0);
        }

        /// <summary>
        /// 按实体当前波次种类构造完整移除 handle（普通敌人=Normal，Boss=Boss）。
        /// </summary>
        private static WaveEntityHandle BuildRemovalHandle(IEnemyEntity enemy, EnemyLeaseIdentity identity)
        {
            WaveEntityKind kind = enemy is IWaveOwnedEnemyEntity waveOwned
                ? waveOwned.WaveKind
                : WaveEntityKind.Normal;
            return new WaveEntityHandle(
                identity.RuntimeId, identity.Generation, identity.WaveOrder, kind);
        }

        /// <summary>
        /// 注销敌人并从空间索引移除（对应 _onEnemyRemoved）。
        /// </summary>
        /// <param name="id">敌人运行时 ID。</param>
        /// <remarks>
        /// <para>从 _enemiesById、_orderedIds、空间索引中移除。若 ID 不存在则为空操作（幂等）。</para>
        /// <para>通常由 <see cref="ProcessRemoveQueue"/> 统一调用，不建议在遍历中直接调用。</para>
        /// </remarks>
        internal void Unregister(int id, bool? playDeathEffect = null)
        {
            if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
            {
                // 幂等：ID 不存在则空操作。
                return;
            }

            if (_buffManager != null && enemy is IBuffTarget buffTarget)
            {
                _buffManager.UnregisterTarget(buffTarget.Handle);
            }

            if (enemy is BossBase boss)
            {
                boss.SkillIntentChanged -= OnBossSkillIntentChanged;
            }

            UnindexEnemy(id);
            _enemiesById.Remove(id);
            _orderedIds.Remove(id);
            _leaseById.Remove(id);
            EnemyRemoved?.Invoke(id, playDeathEffect ?? (enemy.CurrentState == StateDead));
        }

        private void OnBossSkillIntentChanged(int runtimeId, string animationKey, bool active)
        {
            BossSkillIntentChanged?.Invoke(runtimeId, animationKey, active);
        }

        // ====================================================================
        // Update —— 由 BattleSimulation 在 Enemy 阶段回调
        // ====================================================================

        /// <summary>
        /// 推进一帧：快照遍历敌人，跳过 DEAD/SPAWNING，调用 Update。
        /// </summary>
        /// <param name="stepMs">子步时长（毫秒），驱动敌人移动。</param>
        /// <remarks>
        /// <para><b>阶段（决策 0.3）：</b>由 <see cref="BattleSimulation"/> 在
        /// <see cref="BattleUpdatePhase.Enemy"/> 阶段回调，每子步一次。</para>
        ///
        /// <para><b>快照遍历（EnemyManager.js:69-75）：</b>
        /// 遍历前复制 _orderedIds 到 _updateBuffer，避免遍历中集合修改导致迭代异常。
        /// 对应 JS <c>for (const enemy of this.enemies.values()) updateBuffer.push(enemy)</c>。</para>
        ///
        /// <para><b>状态过滤（EnemyManager.js:73）：</b>
        /// 跳过 currentState === DEAD(4) 或 SPAWNING(0) 的敌人。对应 JS
        /// <c>if (enemy.currentState !== 4 && enemy.currentState !== 0) enemy.update(deltaMs)</c>。</para>
        ///
        /// <para><b>冻结中止（决策 0.4）：</b>
        /// 若 <see cref="IsFrozen"/> 为 true，直接返回不推进。遍历中若检测到冻结，
        /// 停止剩余迭代。对应 spec "Freeze occurs inside a manager update"。</para>
        /// </remarks>
        internal void Update(long stepMs)
        {
            if (IsFrozen || IsCleared)
            {
                return;
            }

            // 快照遍历：复制 _orderedIds 到 _updateBuffer，避免遍历中修改集合。
            _updateBuffer.Clear();
            foreach (int id in _orderedIds)
            {
                _updateBuffer.Add(id);
            }

            int count = _updateBuffer.Count;
            for (int i = 0; i < count; i++)
            {
                // 冻结中止：遍历中若冻结则停止剩余迭代（决策 0.4）。
                if (IsFrozen)
                {
                    break;
                }

                int id = _updateBuffer[i];
                if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    // 可能在遍历中被移除（如接触终点后 gameOver），跳过。
                    continue;
                }

                int state = enemy.CurrentState;
                if (state != StateDead && state != StateSpawning)
                {
                    enemy.Update(stepMs);
                }
            }

            // 遍历结束后处理移除队列。
            ProcessRemoveQueue();
        }

        // ====================================================================
        // 空间索引维护
        // ====================================================================

        /// <summary>
        /// 计算敌人的空间单元坐标（对应 _cellCoordinates）。
        /// </summary>
        /// <param name="enemy">敌人实体。</param>
        /// <returns>单元坐标 (cellX, cellY)。</returns>
        /// <remarks>
        /// 对应 EnemyManager.js:143-147：
        /// <c>centerX = visual.x + visual.width/2; cellX = floor(centerX / gridSize)</c>。
        /// C# 移植使用逻辑位置 X/Y/Width/Height。
        /// </remarks>
        private (int cellX, int cellY) CellCoordinates(IEnemyEntity enemy)
        {
            float centerX = enemy.X + enemy.Width / 2f;
            float centerY = enemy.Y + enemy.Height / 2f;
            int cellX = (int)Math.Floor(centerX / _gridSize);
            int cellY = (int)Math.Floor(centerY / _gridSize);
            return (cellX, cellY);
        }

        /// <summary>
        /// 构造空间单元键（对应 _cellKey）。
        /// </summary>
        private static string CellKey(int cellX, int cellY)
        {
            return cellX + "_" + cellY;
        }

        /// <summary>
        /// 索引敌人到空间单元（对应 _indexEnemy）。
        /// </summary>
        /// <param name="id">敌人 ID。</param>
        /// <param name="enemy">敌人实体。</param>
        /// <remarks>
        /// 先取消旧索引（若存在），再登记到新单元。单元内使用 List&lt;int&gt; 保持顺序确定。
        /// </remarks>
        private void IndexEnemy(int id, IEnemyEntity enemy)
        {
            UnindexEnemy(id);

            (int cellX, int cellY) = CellCoordinates(enemy);
            string key = CellKey(cellX, cellY);

            if (!_cellToEnemyIds.TryGetValue(key, out List<int> ids))
            {
                ids = new List<int>();
                _cellToEnemyIds[key] = ids;
            }
            ids.Add(id);
            _enemyIdToCell[id] = key;
        }

        /// <summary>
        /// 取消敌人的空间索引（对应 _unindexEnemy）。
        /// </summary>
        /// <param name="id">敌人 ID。</param>
        private void UnindexEnemy(int id)
        {
            if (!_enemyIdToCell.TryGetValue(id, out string key))
            {
                return;
            }

            if (_cellToEnemyIds.TryGetValue(key, out List<int> ids))
            {
                ids.Remove(id);
                if (ids.Count == 0)
                {
                    _cellToEnemyIds.Remove(key);
                }
            }
            _enemyIdToCell.Remove(id);
        }

        /// <summary>
        /// 更新敌人的空间索引（对应 _onEnemyMovedCell）。
        /// </summary>
        /// <param name="id">敌人 ID。</param>
        /// <remarks>
        /// 敌人移动后调用：若所在单元变化，重新索引。由 EnemyBase 在路径索引变化时调用，
        /// 或由 EnemyManager 在 Update 后批量刷新（本实现采用按需刷新）。
        /// </remarks>
        internal void RefreshCellIndex(int id)
        {
            if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
            {
                return;
            }

            (int cellX, int cellY) = CellCoordinates(enemy);
            string newKey = CellKey(cellX, cellY);
            if (_enemyIdToCell.TryGetValue(id, out string oldKey) && oldKey == newKey)
            {
                return;
            }

            IndexEnemy(id, enemy);
        }

        // ====================================================================
        // 空间查询
        // ====================================================================

        /// <summary>
        /// 收集指定中心与半径覆盖的候选敌人 ID（对应 _candidateIds）。
        /// </summary>
        /// <param name="centerX">查询中心 X。</param>
        /// <param name="centerY">查询中心 Y。</param>
        /// <param name="radius">查询半径。</param>
        /// <returns>候选敌人 ID 列表（按单元扫描顺序，确定但不应直接用于目标选择）。</returns>
        /// <remarks>
        /// 对应 EnemyManager.js:175-188。扫描覆盖半径内的所有空间单元，收集单元内的敌人 ID。
        /// 候选顺序由单元扫描顺序决定——但最终目标选择不依赖此顺序，queryTargets 等方法
        /// 对候选做 circleIntersectsRect 精筛，closestToEnd 等对候选按确定性键排序后选择。
        /// </remarks>
        private List<int> CandidateIds(float centerX, float centerY, float radius)
        {
            var result = new List<int>();
            int minX = (int)Math.Floor((centerX - radius) / _gridSize);
            int maxX = (int)Math.Floor((centerX + radius) / _gridSize);
            int minY = (int)Math.Floor((centerY - radius) / _gridSize);
            int maxY = (int)Math.Floor((centerY + radius) / _gridSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    string key = CellKey(x, y);
                    if (_cellToEnemyIds.TryGetValue(key, out List<int> ids))
                    {
                        // 按单元内登记顺序收集候选。单元内 List 顺序确定。
                        foreach (int id in ids)
                        {
                            result.Add(id);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 圆与矩形相交测试（对应 circleIntersectsRect）。
        /// </summary>
        /// <param name="radius">圆半径。</param>
        /// <param name="centerX">圆心 X。</param>
        /// <param name="centerY">圆心 Y。</param>
        /// <param name="rectX">矩形左上 X（敌人逻辑位置）。</param>
        /// <param name="rectY">矩形左上 Y。</param>
        /// <param name="rectWidth">矩形宽（对应 map.gridWidth）。</param>
        /// <param name="rectHeight">矩形高（对应 map.gridHeight）。</param>
        /// <returns>相交返回 true。</returns>
        /// <remarks>
        /// 对应 EnemyManager.js:7-15。CONFIRMED：先将半径减 1，再做 circle-vs-AABB 最近点测试。
        /// </remarks>
        internal static bool CircleIntersectsRect(
            float radius, float centerX, float centerY,
            float rectX, float rectY, float rectWidth, float rectHeight)
        {
            float r = radius - 1f;
            float rectRight = rectX + rectWidth;
            float rectBottom = rectY + rectHeight;
            float dx = centerX - Math.Max(rectX, Math.Min(centerX, rectRight));
            float dy = centerY - Math.Max(rectY, Math.Min(centerY, rectBottom));
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>
        /// 查询指定中心与半径内的可攻击目标（对应 queryTargets）。
        /// </summary>
        /// <param name="centerX">查询中心 X。</param>
        /// <param name="centerY">查询中心 Y。</param>
        /// <param name="radius">查询半径。</param>
        /// <param name="playerSide">攻击者阵营：true=玩家方攻击者（查询对手方敌人），false=对手方。</param>
        /// <param name="cellWidth">敌人格子宽（对应 map.gridWidth，用于 circleIntersectsRect）。</param>
        /// <param name="cellHeight">敌人格子高（对应 map.gridHeight）。</param>
        /// <returns>目标 DTO 列表（按候选扫描顺序，不排序——对应 JS 行为）。</returns>
        /// <remarks>
        /// <para>对应 EnemyManager.js:195-205。返回值不排序；顺序来自空间单元扫描与单元内顺序。
        /// 候选经 <see cref="CircleIntersectsRect"/> 精筛后返回。</para>
        /// <para><b>稳定有序：</b>单元内 ID 顺序确定（List 登记顺序），候选扫描顺序确定，
        /// 但 JS 原版不对此结果排序——本实现保持一致，消费方按需排序。</para>
        /// </remarks>
        internal List<EnemyTargetDto> QueryTargets(
            float centerX, float centerY, float radius, bool playerSide,
            float cellWidth, float cellHeight)
        {
            var results = new List<EnemyTargetDto>();
            List<int> candidates = CandidateIds(centerX, centerY, radius);

            foreach (int id in candidates)
            {
                if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    continue;
                }

                if (!enemy.IsTargetableBy(playerSide))
                {
                    continue;
                }

                if (!CircleIntersectsRect(radius, centerX, centerY,
                        enemy.X, enemy.Y, cellWidth, cellHeight))
                {
                    continue;
                }

                results.Add(new EnemyTargetDto(id, enemy.X, enemy.Y, enemy.RemainingPathDistance));
            }
            return results;
        }

        /// <summary>
        /// 查询指定中心与半径内的敌人对象（对应 queryEnemyObjects）。
        /// </summary>
        /// <param name="centerX">查询中心 X。</param>
        /// <param name="centerY">查询中心 Y。</param>
        /// <param name="radius">查询半径。</param>
        /// <param name="playerSide">攻击者阵营。</param>
        /// <param name="cellWidth">敌人格子宽。</param>
        /// <param name="cellHeight">敌人格子高。</param>
        /// <param name="output">输出列表（复用缓冲，对应 JS output 参数）。若为 null 则内部新建。</param>
        /// <returns>敌人对象列表（追加到 output）。</returns>
        internal List<IEnemyEntity> QueryEnemyObjects(
            float centerX, float centerY, float radius, bool playerSide,
            float cellWidth, float cellHeight,
            List<IEnemyEntity> output)
        {
            List<IEnemyEntity> results = output ?? new List<IEnemyEntity>();
            List<int> candidates = CandidateIds(centerX, centerY, radius);

            foreach (int id in candidates)
            {
                if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    continue;
                }

                if (enemy.IsTargetableBy(playerSide) &&
                    CircleIntersectsRect(radius, centerX, centerY,
                        enemy.X, enemy.Y, cellWidth, cellHeight))
                {
                    results.Add(enemy);
                }
            }
            return results;
        }

        /// <summary>
        /// 查询某敌人周围的可攻击目标（对应 queryAroundEnemy）。
        /// </summary>
        /// <param name="sourceId">源敌人 ID（排除自身）。</param>
        /// <param name="sourceX">源中心 X。</param>
        /// <param name="sourceY">源中心 Y。</param>
        /// <param name="radius">查询半径。</param>
        /// <param name="playerSide">攻击者阵营。</param>
        /// <param name="cellWidth">敌人格子宽。</param>
        /// <param name="cellHeight">敌人格子高。</param>
        /// <returns>目标 DTO 列表（排除源敌人自身）。</returns>
        internal List<EnemyTargetDto> QueryAroundEnemy(
            int sourceId, float sourceX, float sourceY, float radius,
            bool playerSide, float cellWidth, float cellHeight)
        {
            var results = new List<EnemyTargetDto>();
            List<int> candidates = CandidateIds(sourceX, sourceY, radius);

            foreach (int id in candidates)
            {
                if (id == sourceId)
                {
                    continue;
                }

                if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    continue;
                }

                if (enemy.IsTargetableBy(playerSide) &&
                    CircleIntersectsRect(radius, sourceX, sourceY,
                        enemy.X, enemy.Y, cellWidth, cellHeight))
                {
                    results.Add(new EnemyTargetDto(id, enemy.X, enemy.Y, enemy.RemainingPathDistance));
                }
            }
            return results;
        }

        // ====================================================================
        // 选择性目标查询
        // ====================================================================

        /// <summary>
        /// 查询最接近终点的若干目标（对应 closestToEnd）。
        /// </summary>
        /// <param name="count">返回数量。</param>
        /// <param name="playerSide">攻击者阵营。</param>
        /// <returns>按 remainingPathDistance 升序排列的前 count 个目标 DTO。</returns>
        /// <remarks>
        /// <para>对应 EnemyManager.js:247-252。遍历全部敌人，过滤可攻击阵营，按
        /// remainingPathDistance 升序排序，取前 count 个。</para>
        /// <para><b>稳定有序（task 4.6）：</b>遍历基于 <see cref="_orderedIds"/>（spawn 顺序），
        /// 排序键 remainingPathDistance 确定性。相同距离时按 _orderedIds 顺序（spawn 顺序）
        /// 稳定排序——C# <c>List&lt;T&gt;.Sort</c> 不保证稳定，故在比较器中以收集顺序
        /// （即 spawn 顺序）作为次级键，保证相同距离的敌人保持 spawn 顺序。</para>
        /// </remarks>
        internal List<EnemyTargetDto> ClosestToEnd(int count, bool playerSide)
        {
            // entries 按 _orderedIds 顺序收集，index 即 spawn 顺序。
            var entries = new List<(int id, float dist, int order)>();
            int order = 0;
            foreach (int id in _orderedIds)
            {
                if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    continue;
                }

                if (enemy.IsTargetableBy(playerSide))
                {
                    entries.Add((id, enemy.RemainingPathDistance, order));
                    order++;
                }
            }

            // 按 remainingPathDistance 升序；相同距离按 order（spawn 顺序）升序，保证稳定。
            entries.Sort((a, b) =>
            {
                int cmp = a.dist.CompareTo(b.dist);
                return cmp != 0 ? cmp : a.order.CompareTo(b.order);
            });

            int take = Math.Min(count, entries.Count);
            var result = new List<EnemyTargetDto>(take);
            for (int i = 0; i < take; i++)
            {
                IEnemyEntity enemy = _enemiesById[entries[i].id];
                result.Add(new EnemyTargetDto(
                    enemy.Id, enemy.X, enemy.Y, enemy.RemainingPathDistance));
            }
            return result;
        }

        /// <summary>
        /// 随机选择一个可攻击目标（对应 randomTarget）。
        /// </summary>
        /// <param name="playerSide">攻击者阵营。</param>
        /// <returns>随机目标 DTO；无候选返回 <see cref="EnemyTargetDto.Invalid"/>。</returns>
        /// <remarks>
        /// <para>对应 EnemyManager.js:254-260。遍历 _orderedIds 收集可攻击候选，
        /// 从中随机选一个。无候选返回 {id:-1, x:0, y:0, Bm:Infinity}。</para>
        /// <para><b>稳定有序：</b>候选收集基于 _orderedIds（spawn 顺序），随机索引确定。
        /// 注入确定性随机源（如 SeededRandomSource）保证可复现。</para>
        /// </remarks>
        internal EnemyTargetDto RandomTarget(bool playerSide)
        {
            // 复用 _updateBuffer 作为查询缓冲（对应 queryBuffer）。
            _updateBuffer.Clear();
            foreach (int id in _orderedIds)
            {
                if (_enemiesById.TryGetValue(id, out IEnemyEntity enemy) &&
                    enemy.IsTargetableBy(playerSide))
                {
                    _updateBuffer.Add(id);
                }
            }

            if (_updateBuffer.Count == 0)
            {
                return EnemyTargetDto.Invalid;
            }

            int index = (int)Math.Floor(_randomSource() * _updateBuffer.Count);
            // 钳制索引到合法范围，防止随机源返回 1.0 导致越界。
            if (index < 0)
            {
                index = 0;
            }
            else if (index >= _updateBuffer.Count)
            {
                index = _updateBuffer.Count - 1;
            }

            IEnemyEntity selected = _enemiesById[_updateBuffer[index]];
            return new EnemyTargetDto(
                selected.Id, selected.X, selected.Y, selected.RemainingPathDistance);
        }

        /// <summary>
        /// 查询血量最低的可攻击目标（对应 lowestHealthTarget）。
        /// </summary>
        /// <param name="playerSide">攻击者阵营。</param>
        /// <returns>血量最低目标 DTO；无候选返回 <see cref="EnemyTargetDto.Invalid"/>。</returns>
        /// <remarks>
        /// <para>对应 EnemyManager.js:262-268。遍历 _orderedIds，选择 Health 最小的可攻击敌人。
        /// 相同血量时取 _orderedIds 中先出现的（稳定选择）。</para>
        /// </remarks>
        internal EnemyTargetDto LowestHealthTarget(bool playerSide)
        {
            IEnemyEntity selected = null;
            foreach (int id in _orderedIds)
            {
                if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    continue;
                }

                if (!enemy.IsTargetableBy(playerSide))
                {
                    continue;
                }

                if (selected == null || enemy.Health < selected.Health)
                {
                    selected = enemy;
                }
            }

            if (selected == null)
            {
                return EnemyTargetDto.Invalid;
            }

            return new EnemyTargetDto(
                selected.Id, selected.X, selected.Y, selected.RemainingPathDistance);
        }

        /// <summary>
        /// 查询路径进度最靠前的敌人位置（对应 frontmostPathPosition）。
        /// </summary>
        /// <param name="playerSide">阵营（注意：JS 用 isPlayerLane === playerSide，非 isTargetableBy）。</param>
        /// <returns>最靠前敌人的路径索引与位置；无候选返回 null。</returns>
        /// <remarks>
        /// <para>对应 EnemyManager.js:270-276。遍历 _orderedIds，选择 currentPathIndex 最大的
        /// 同阵营敌人。相同索引时取 _orderedIds 中先出现的（稳定选择）。</para>
        /// <para>注意：本方法用 <c>IsPlayerLane</c> 而非 <c>IsTargetableBy</c> 过滤，
        /// 对应 JS <c>enemy.isPlayerLane === Boolean(playerSide)</c>。</para>
        /// </remarks>
        internal (int index, float x, float y)? FrontmostPathPosition(bool playerSide)
        {
            IEnemyEntity selected = null;
            foreach (int id in _orderedIds)
            {
                if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    continue;
                }

                if (enemy.IsPlayerLane != playerSide)
                {
                    continue;
                }

                if (selected == null || enemy.CurrentPathIndex > selected.CurrentPathIndex)
                {
                    selected = enemy;
                }
            }

            if (selected == null)
            {
                return null;
            }

            return (selected.CurrentPathIndex, selected.X, selected.Y);
        }

        // ====================================================================
        // 按 ID 查找 / 伤害提交 / 强制移除
        // ====================================================================

        /// <summary>
        /// 按 ID 查找敌人（对应 getById）。
        /// </summary>
        /// <param name="id">敌人运行时 ID。</param>
        /// <returns>敌人实体；不存在返回 null。</returns>
        internal IEnemyEntity GetById(int id)
        {
            _enemiesById.TryGetValue(id, out IEnemyEntity enemy);
            return enemy;
        }

        /// <summary>
        /// 对一组目标提交伤害（对应 applyDamage）。
        /// </summary>
        /// <param name="damage">伤害值（正数）。</param>
        /// <param name="targetDtos">目标 DTO 列表（按 Id 查找敌人）。</param>
        /// <param name="attackerId">攻击者运行时 ID（无攻击者传 -1）。</param>
        /// <remarks>
        /// <para>对应 EnemyManager.js:235-240。对每个 DTO 按 Id 查找敌人，调用 enemy.hit。
        /// 不存在的 Id 静默跳过（幂等）。</para>
        /// <para><b>不在此处触发死亡清理：</b>enemy.hit 内部若血量归零会进入 DEAD 状态，
        /// 但不在伤害调用栈内重入销毁集合（决策 0.4）。死亡敌人的移除由
        /// <see cref="ForceRemove"/> 入队、<see cref="ProcessRemoveQueue"/> 统一处理。</para>
        /// </remarks>
        internal void ApplyDamage(int damage, List<EnemyTargetDto> targetDtos, int attackerId)
        {
            if (damage <= 0 || targetDtos == null)
            {
                return;
            }

            foreach (EnemyTargetDto dto in targetDtos)
            {
                if (!_enemiesById.TryGetValue(dto.Id, out IEnemyEntity enemy))
                {
                    continue;
                }

                enemy.Hit(damage, attackerId);
            }
        }

        /// <summary>
        /// 请求强制移除敌人（对应 forceRemove）。
        /// </summary>
        /// <param name="id">敌人运行时 ID。</param>
        /// <remarks>
        /// <para>对应 EnemyManager.js:242-245。调用 enemy.gameOver() 并入移除队列。
        /// 幂等：ID 不存在或已入队则跳过。</para>
        /// <para><b>不重入销毁集合：</b>不在遍历中直接修改 _enemiesById/_orderedIds，
        /// 只入队 _removeQueue，由 <see cref="ProcessRemoveQueue"/> 统一处理。</para>
        /// </remarks>
        internal void ForceRemove(int id)
        {
            RequestRemoveEnemy(id, EnemyRemovalReason.Forced);
        }

        /// <summary>
        /// 按 Boss kind 强制移除全部活动 Boss 并处理移除队列（Boss 波端口 Cleanup 使用）。
        /// </summary>
        /// <returns>被强制移除的 Boss 数量。</returns>
        /// <remarks>
        /// <para>design.md 决策 1：<see cref="ZhangLiangBossWavePort"/> 不维护第二个
        /// active dictionary，Cleanup 经本方法请求 EnemyManager 强制移除 Boss-kind 实体；
        /// 实际释放仍由唯一移除点（<see cref="ProcessRemoveQueue"/> → ReleaseEnemy →
        /// Boss 工厂池）完成。幂等：无活动 Boss 时返回 0。</para>
        /// </remarks>
        internal int ForceRemoveBosses()
        {
            var bossIds = new List<int>();
            foreach (int id in _orderedIds)
            {
                if (_enemiesById.TryGetValue(id, out IEnemyEntity enemy)
                    && enemy is IWaveOwnedEnemyEntity waveOwned
                    && waveOwned.WaveKind == WaveEntityKind.Boss)
                {
                    bossIds.Add(id);
                }
            }

            for (int i = 0; i < bossIds.Count; i++)
            {
                ForceRemove(bossIds[i]);
            }

            ProcessRemoveQueue();
            return bossIds.Count;
        }

        /// <summary>
        /// 请求按指定原因移除敌人（供敌人死亡/终点攻击回调注入）。
        /// </summary>
        /// <param name="id">敌人运行时 ID。</param>
        /// <param name="reason">移除原因（驱动表现与回收语义）。</param>
        /// <remarks>
        /// <para>由 <see cref="EnemyBase"/> 在血量归零（Killed）或抵达路径终点
        /// （ReachedEndPoint）时通过注入的回调调用本方法，内部委托入队。入队后由
        /// <see cref="ProcessRemoveQueue"/> 在遍历结束后统一处理，避免在伤害调用栈内
        /// 重入销毁集合（决策 0.4）。</para>
        /// <para>幂等：ID 不存在或已入队则跳过——同一 ID 无论重复请求多少次都只回收一次。</para>
        /// </remarks>
        internal void RequestRemoveEnemy(int id, EnemyRemovalReason reason)
        {
            if (!_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
            {
                return;
            }

            // 捕获当前租借身份（generation-aware，task 3.6/3.7）。
            // 入队身份 = 当前实体身份；处理时再核对实体当前身份，旧世代迟到请求幂等忽略。
            EnemyLeaseIdentity identity = GetCurrentLease(enemy, id);

            // 入队移除队列，遍历结束后统一处理。重复请求同 ID 只入队一次（首次 reason 生效）。
            for (int index = 0; index < _removeQueue.Count; index++)
            {
                if (_removeQueue[index].identity.RuntimeId == id)
                {
                    return;
                }
            }

            // 只在首次请求时调用 gameOver，避免重复请求重复清理实体内部状态。
            enemy.GameOver();
            _removeQueue.Add((identity, reason));
        }

        /// <summary>
        /// 处理移除队列：统一注销并归还所有待移除敌人。
        /// </summary>
        /// <remarks>
        /// <para>由 <see cref="Update"/> 在遍历结束后调用。也可由外部在安全点显式调用。
        /// 处理后清空队列。幂等：队列中的 ID 若已不存在则跳过。</para>
        /// <para>按 <see cref="EnemyRemovalReason"/> 决定 <see cref="Unregister"/> 的
        /// <c>playDeathEffect</c>（Killed 保留死亡表现，其余不播），并从活动集合注销后
        /// 经 <see cref="ReleaseEnemy"/> 归还对象池（恰好一次）。</para>
        /// </remarks>
        internal void ProcessRemoveQueue()
        {
            if (_removeQueue.Count == 0)
            {
                return;
            }

            foreach ((EnemyLeaseIdentity identity, EnemyRemovalReason reason) in _removeQueue)
            {
                if (!_enemiesById.TryGetValue(identity.RuntimeId, out IEnemyEntity enemy))
                {
                    // 幂等：ID 不存在则跳过（可能在遍历中被移除）。
                    continue;
                }

                // 世代守卫（design.md 决策 5 / spec "Ignore a stale removal callback"）：
                // 核对当前字典实体的租借身份；与入队身份不一致 = 池复用后的新租借，
                // 迟到旧世代请求幂等忽略，不得误删新租借、不得触发移除事实。
                EnemyLeaseIdentity current = GetCurrentLease(enemy, identity.RuntimeId);
                if (current != identity)
                {
                    continue;
                }

                Unregister(identity.RuntimeId, playDeathEffect: reason == EnemyRemovalReason.Killed);
                // task 5.2：以完整 handle（runtimeId+generation+waveOrder+kind）发布移除事实。
                WaveEntityRemoved?.Invoke(BuildRemovalHandle(enemy, identity), reason);
                ReleaseEnemy?.Invoke(enemy);
            }
            _removeQueue.Clear();
        }

        // ====================================================================
        // 诊断查询
        // ====================================================================

        /// <summary>敌人是否在空间索引中登记（对应 hasSpatialRegistration）。</summary>
        internal bool HasSpatialRegistration(int id) => _enemyIdToCell.ContainsKey(id);

        /// <summary>获取敌人在空间索引中的单元键（对应 spatialKeyFor）。</summary>
        internal string SpatialKeyFor(int id)
        {
            _enemyIdToCell.TryGetValue(id, out string key);
            return key;
        }

        /// <summary>
        /// 获取所有敌人 ID 的只读快照（按 spawn 顺序，诊断用）。
        /// </summary>
        internal IReadOnlyList<int> GetOrderedIdsSnapshot()
        {
            return _orderedIds.ToArray();
        }

        /// <summary>
        /// 获取指定 ID 登记时记录的租借身份（诊断/测试用）。
        /// </summary>
        /// <param name="id">敌人运行时 ID。</param>
        /// <param name="identity">命中的登记身份；未命中时为空。</param>
        /// <returns>ID 已登记时返回 true。</returns>
        internal bool TryGetLeaseIdentity(int id, out EnemyLeaseIdentity identity)
        {
            return _leaseById.TryGetValue(id, out identity);
        }

        // ====================================================================
        // 清理 —— 幂等，可重复调用
        // ====================================================================

        /// <summary>
        /// 清空全部集合与索引（对应 gameOver）。幂等。
        /// </summary>
        /// <remarks>
        /// <para>对应 EnemyManager.js:288-298。清空 enemies、updateBuffer、queryBuffer、
        /// cellToEnemyIds、enemyIdToCell、deferredBuffs、spawnCalls。</para>
        /// <para><b>幂等：</b>重复调用为空操作。由 BattleRuntime.EnterSettling 在
        /// "清理 EnemyManager" 步骤调用，也可由测试重置调用。</para>
        /// <para><b>不调用 enemy.GameOver：</b>本方法只清空管理器侧集合，不触发敌人自身清理。
        /// 敌人自身清理（回收表现、清除定时器）应在 ForceRemove 或 EnemyBase.GameOver 中完成。
        /// 若需在清理时逐个通知敌人，应先遍历调用 ForceRemove 再 Clear（对应 JS gameOver 的
        /// <c>for (const enemy of [...this.enemies.values()]) enemy.gameOver()</c>）。</para>
        /// </remarks>
        internal void Clear()
        {
            if (IsCleared)
            {
                return;
            }

            _enemiesById.Clear();
            _orderedIds.Clear();
            _updateBuffer.Clear();
            _cellToEnemyIds.Clear();
            _enemyIdToCell.Clear();
            _leaseById.Clear();
            _removeQueue.Clear();
            IsCleared = true;
            // 不在此设置 IsFrozen：IsFrozen 语义是"冻结以进入 Settling"，
            // IsCleared 语义是"已清空"。Update 同时检查两者，清理后不推进。
        }

        /// <summary>
        /// 战斗结束清理：逐个通知敌人 gameOver，再清空管理器集合（对应 JS gameOver 语义）。
        /// </summary>
        /// <remarks>
        /// <para>对应 EnemyManager.js:288-298 <c>gameOver()</c> 的完整语义：
        /// 先 <c>for (const enemy of [...this.enemies.values()]) enemy.gameOver()</c>，
        /// 再 <c>enemies.clear()</c> 等。</para>
        /// <para>本方法先快照遍历调用每个敌人的 GameOver（触发敌人自身清理），再调用
        /// <see cref="Clear"/> 清空管理器集合。幂等。</para>
        /// <para>由 BattleRuntime.EnterSettling 在"清理 EnemyManager"步骤调用。</para>
        /// </remarks>
        internal void GameOver()
        {
            if (IsCleared)
            {
                return;
            }

            // 快照遍历，避免遍历中 GameOver 修改集合。
            _updateBuffer.Clear();
            foreach (int id in _orderedIds)
            {
                _updateBuffer.Add(id);
            }

            foreach (int id in _updateBuffer)
            {
                if (_enemiesById.TryGetValue(id, out IEnemyEntity enemy))
                {
                    EnemyLeaseIdentity identity = GetCurrentLease(enemy, id);
                    if (_buffManager != null && enemy is IBuffTarget buffTarget)
                    {
                        _buffManager.UnregisterTarget(buffTarget.Handle);
                    }

                    enemy.GameOver();
                    // 结算清理是静默回收，不播放死亡表现；注销后归还对象池（恰好一次）。
                    Unregister(id, playDeathEffect: false);
                    // 战斗结束批量清理也提交 Forced 移除事实（恰好一次），
                    // 供下一波 WaveManager 在停止状态下解除波次活动计数。
                    WaveEntityRemoved?.Invoke(BuildRemovalHandle(enemy, identity), EnemyRemovalReason.Forced);
                    ReleaseEnemy?.Invoke(enemy);
                }
            }

            Clear();
        }

        // ====================================================================
        // 默认随机源（fallback，生产应注入确定性随机源）
        // ====================================================================

        /// <summary>
        /// 默认随机源（对应 Math.random），返回 [0, 1)。
        /// <para>生产环境应通过构造函数注入确定性随机源（如 SeededRandomSource，
        /// Ports/SeededRandomSource.cs，后续 task），保证可复现。本 fallback 仅在未注入时使用。</para>
        /// <para>注：JS 原版 EnemyManager.setRandomSource 在运行时替换随机源。C# 移植改为
        /// 构造注入——运行时替换随机源会破坏可复现性，且 _randomSource 为 readonly。
        /// 测试通过构造注入确定性随机源。</para>
        /// </summary>
        private static float DefaultRandom()
        {
            // 使用实例 Random 避免对 Random.Shared（.NET 6+）的依赖，保持 Unity 兼容。
            // 生产环境应注入确定性随机源，此 fallback 不要求线程安全的高质量随机。
            return (float)s_defaultRandom.NextDouble();
        }

        /// <summary>默认随机源实例（fallback 用，非线程安全；生产注入确定性随机源）。</summary>
        private static readonly Random s_defaultRandom = new Random();
    }
}
