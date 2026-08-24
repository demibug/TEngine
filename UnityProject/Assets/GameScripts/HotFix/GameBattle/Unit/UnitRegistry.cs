using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.3：UnitRegistry —— 管理单位注册、放置、移动、删除和战斗结束清理
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 185 行 / Unit/UnitRegistry.cs）：
    //   管理单位注册、放置、移动、删除和战斗结束清理。
    //   维护稳定有序集合（不依赖 Dictionary/HashSet 未定义顺序决定遍历）。
    //   纯逻辑，不持有 UnityEngine GameObject/表现组件。
    //
    // 来源证据（还原工程 UnitRegistry.js:1-382）：
    //   UnitRegistry 继承 SingletonBase，持有：
    //     - soldiers: Map<id, SoldierBase>        // PA
    //     - secondaryUnits: Map<id, GeneralPart>  // AA
    //     - generals: Map<id, GeneralUnit>        // BM
    //     - farmers: Map<id, Farmer>              // EA
    //     - placementReservations: Set            // pR
    //     - initialized: false
    //   核心方法：
    //     - configure({unitFactory, gameData, eventBus, placementReservations, ...})
    //     - init()：标记 initialized=true
    //     - createUnit(containerType, text, side, gridX, gridY, level, buffData)
    //         → createFromDescriptor({...})
    //     - createFromDescriptor：
    //         1. classifyText → category（Soldier/GeneralPart/Farmer）
    //         2. 校验 isGameOver / canPlace / hasBattleOccupant
    //         3. unitFactory.createByText(text) 或 generalFactory.createPart
    //         4. unit.setPlacement(containerType, gridX, gridY)
    //         5. unit.initialize(text, side)
    //         6. register(unit, category)
    //         7. place(unit, containerType, side, gridX, gridY)
    //         8. levelUp / buff / weapon（本期裁剪）
    //     - register(unit, category)：按 category 分到 soldiers/secondaryUnits/farmers Map
    //     - place(unit, containerType, side, gridX, gridY)：
    //         parent = parentResolver(side)
    //         pixelX = gridX * map.gridWidth, pixelY = gridY * map.gridHeight
    //         unit.activatePlacement({parent, pixelX, pixelY, zIndex: gridY})
    //     - moveUnit(id, gridX, gridY)：校验 → setPlacement → reposition
    //     - hasBattleOccupant(side, gridX, gridY)：遍历全部 Map 检查
    //     - getUnit(id)：从 soldiers/secondaryUnits/farmers 查找
    //     - removeUnit(id) → removeSoldier/removeGeneral/removeSecondary/removeFarmer
    //     - removeSoldier(id)：
    //         placementReservations.delete(key)
    //         weaponManager.remove(unit.weapon)  // 本期裁剪
    //         unit.gameOver()
    //         soldiers.delete(id)
    //     - gameOver()：
    //         遍历 generals → gameOver + clear
    //         遍历 soldiers → removeSoldier
    //         遍历 secondaryUnits → removeSecondary
    //         遍历 farmers → removeFarmer
    //         clear 全部 Map
    //     - allUnits()：[...soldiers.values(), ...secondaryUnits.values(), ...farmers.values()]
    //     - unitsBySide(side)：allUnits().filter(side)
    //     - count / playerSoldierCount
    //
    // 本期裁剪（design.md 决策 5 / task 6.3 约束）：
    //   - 删除 SingletonBase：改为 internal sealed class，构造注入。
    //   - 删除 GeneralPart/GeneralUnit/Farmer：本期只覆盖四兵种士兵
    //     （design.md 决策 5 / Non-Goals "不移植 Boss、Skill、AI、General"）。
    //   - 删除 EventBus：design 决策 4，一致性操作使用直接调用。
    //   - 删除 WeaponManager/SkillManager；BuffManager 仅以可选构造依赖接入
    //     IBuffTarget 注册与 pre-remove 清理，不恢复全局单例。
    //   - 删除 MapTileManager：可建造性校验由 BattleInputController（task 6.7）
    //     在调用 UnitRegistry 前完成，UnitRegistry 只管理注册/放置/移除。
    //   - 删除 parentResolver：表现层 parent 由 Presenter 通过端口同步，
    //     规则层只记录网格坐标与逻辑像素位置。
    //   - 删除 classifyText：本期只有 Soldier，无 GeneralPart/Farmer 分类。
    //   - 删除 levelUp/buff/weapon：本期 Level 固定 1（task 1.5 延后）。
    //
    // 稳定有序集合（task 4.6 约束 / design.md 第 185 行）：
    //   JS 用 Map<id, unit> 维护单位集合，Map 保留插入顺序。
    //   C# 移植用 List<SoldierBase> 维护稳定有序集合，供 AttackScheduler 遍历。
    //   不依赖 Dictionary/HashSet 的未定义遍历顺序决定目标（与 EnemyManager 模式一致，task 4.6）。
    //   另用 Dictionary<int, int>（id→索引）做 O(1) 查找，与 List 配合实现
    //   稳定遍历 + 快速查找/移除（移除用末尾交换法，保持遍历顺序稳定但不保证原始顺序）。
    //
    // 不变量：
    //   1. 稳定有序集合：List 供 AttackScheduler 遍历，顺序由插入决定。
    //   2. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/表现组件。
    //   3. 每局新建/销毁：不跨局复用，重开由 BattleRuntimeFactory 新建。
    //   4. 注册/放置/移除对称：每个注册的单位最终必须被移除并归还池。
    //   5. 战斗结束清理：GameOver 遍历全部单位移除并归还池。
    // ============================================================================

    /// <summary>
    /// 管理单位注册、放置、移动、删除和战斗结束清理（task 6.3 / design.md 第 185 行）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 185 行）：</b>管理单位注册、放置、移动、删除和
    /// 战斗结束清理。替代还原工程 <c>UnitRegistry.js</c>（UnitRegistry.js:1-382）。</para>
    ///
    /// <para><b>稳定有序集合（task 4.6 约束）：</b>
    /// 使用 <see cref="List{SoldierBase}"/> 维护稳定有序集合，供 AttackScheduler 遍历。
    /// 不依赖 Dictionary/HashSet 的未定义遍历顺序决定目标
    /// （与 EnemyManager 模式一致，task 4.6）。</para>
    ///
    /// <para><b>本期裁剪：</b>
    /// 删除 GeneralPart/GeneralUnit/Farmer/WeaponManager/SkillManager/
    /// MapTileManager/EventBus/parentResolver（design.md 决策 5 / Non-Goals / task 1.5）。
    /// 只管理四兵种 SoldierBase。</para>
    ///
    /// <para><b>纯逻辑（design.md 第 9 行）：</b>
    /// 不持有 Unity GameObject 或表现组件。放置只记录网格坐标与逻辑像素位置，
    /// 表现层通过端口同步。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部
    /// BattleRuntimeFactory / BattleInputController 使用。</para>
    /// </remarks>
    internal sealed class UnitRegistry
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀。</summary>
        private const string LogTag = "[UnitRegistry]";

        // ====================================================================
        // 稳定有序集合（供 AttackScheduler 遍历）
        // ====================================================================

        /// <summary>
        /// 已注册士兵的稳定有序列表。
        /// <para>对应还原工程 <c>UnitRegistry.soldiers</c>（Map&lt;id, SoldierBase&gt;，
        /// UnitRegistry.js:17），Map 保留插入顺序。C# 用 List 保持插入顺序遍历。</para>
        /// <para><b>稳定有序（task 4.6 约束）：</b>AttackScheduler 每子步遍历此列表
        /// 驱动单位攻击。顺序由注册（插入）决定，不依赖 Dictionary/HashSet 的
        /// 未定义遍历顺序。</para>
        /// <para><b>移除策略：</b>用末尾交换法 O(1) 移除，保持剩余元素顺序不变
        /// （被移除位置由末尾元素填补）。这保证遍历中移除不影响后续元素顺序。</para>
        /// </summary>
        private readonly List<SoldierBase> _soldiers = new List<SoldierBase>();

        /// <summary>
        /// ID → 列表索引映射，供 O(1) 查找与移除。
        /// <para>与 <see cref="_soldiers"/> 配合：List 保证遍历顺序，
        /// Dictionary 保证查找/移除效率。</para>
        /// <para><b>移除时同步更新：</b>末尾交换法移除时，被交换的末尾元素的索引
        /// 需更新到此映射。</para>
        /// </summary>
        private readonly Dictionary<int, int> _idToIndex = new Dictionary<int, int>();

        /// <summary>
        /// BattleUnit.UnitId → 战斗运行时实例映射（最终方案"增加 UnitId → BattleRuntimeId 映射"）。
        /// <para>战场槽换位时复用同一战斗实例；下场时解除战斗实例但保留 BattleUnit。</para>
        /// <para>键为 <see cref="BattleUnit.UnitId"/>（局内单位权威 ID），值为对应
        /// <see cref="SoldierBase"/> 实例（其 <see cref="UnitBase.Id"/> 是运行时 ID）。</para>
        /// </summary>
        private readonly Dictionary<int, SoldierBase> _unitIdToSoldier =
            new Dictionary<int, SoldierBase>();

        // ====================================================================
        // 注入依赖
        // ====================================================================

        /// <summary>单位工厂，供创建士兵。</summary>
        private readonly UnitFactory _unitFactory;

        /// <summary>格子尺寸（像素，对应 map.gridWidth=80）。放置时计算逻辑像素位置。</summary>
        private readonly float _cellSize;

        /// <summary>本局 Buff 所有者；未装配时保持旧测试行为。</summary>
        private readonly BuffManager _buffManager;

        /// <summary>玩家默认基础武器 resolver；未装配时保持旧测试行为（不装配武器）。</summary>
        private readonly BasicWeaponResolver _weaponResolver;

        /// <summary>
        /// 武将主动技能运行时（可为 null；未装配时保持旧测试行为，不绑定技能租期）。
        /// <para>装配后，General 首次上场绑定技能租期，下场解除，GameOver 清局。
        /// 普通兵与未装配 runtime 的旧路径行为不变。</para>
        /// </summary>
        private GeneralSkillRuntime _skillRuntime;

        /// <summary>单位完成放置后的低频表现事实（末位为等级，修复 P0）。</summary>
        internal event Action<int, bool, int, int, int, int> UnitPlaced;

        /// <summary>携带武将身份、配置 Prefab 地址和动画键的上场事实。</summary>
        internal event Action<UnitSpawnViewData> ConfiguredUnitPlaced;

        /// <summary>单位从活动集合移除后的低频表现事实。</summary>
        internal event Action<int> UnitRemoved;

        /// <summary>战场单位移动到新战场格后的低频表现事实（最终方案"战场槽换位"）。</summary>
        internal event Action<int, int, int> UnitMoved;

        /// <summary>战场单位等级变化后的低频表现事实（最终方案"等级数值和等级表现"）。</summary>
        internal event Action<int, int> UnitLevelChanged;

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>
        /// 当前已注册士兵数量（对应 UnitRegistry.js:316 count）。
        /// </summary>
        internal int Count => _soldiers.Count;

        /// <summary>
        /// 玩家方士兵数量（对应 UnitRegistry.js:317 playerSoldierCount）。
        /// </summary>
        internal int PlayerSoldierCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _soldiers.Count; i++)
                {
                    if (_soldiers[i].Side)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造单位注册表。
        /// </summary>
        /// <param name="unitFactory">单位工厂。不可为 null。</param>
        /// <param name="cellSize">格子尺寸（像素，对应 map.gridWidth=80）。</param>
        /// <param name="buffManager">本局 Buff 所有者（可为 null，未装配时保持旧测试行为）。</param>
        /// <param name="weaponResolver">玩家默认基础武器 resolver（可为 null；null 时保持
        /// 旧测试行为，不装配玩家默认武器）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="unitFactory"/> 为 null。</exception>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitRegistry.configure({unitFactory, ...})</c>
        /// （UnitRegistry.js:29-57），但从全局 SingletonBase + 多依赖注入改为
        /// 构造注入强类型依赖。本期裁剪了 EventBus/gameData/placementReservations/
        /// parentResolver 等依赖（design.md 决策 5 / Non-Goals）。</para>
        /// <para><b>每局新建/销毁：</b>由 BattleRuntimeFactory 在每局构造时创建新实例，
        /// 不跨局复用（spec "Restart creates clean per-battle state"）。</para>
        /// <para><b>玩家默认武器（本 change 起）：</b>生产路径由 BattleRuntimeFactory
        /// 从配置快照构造 <see cref="BasicWeaponResolver"/> 注入；玩家 Soldier 在
        /// Acquire 之后、Register 之前应用默认武器，对手跳过。解析失败沿创建事务
        /// 回滚（<see cref="ReleasePrepared"/> 归还池并 ResetState），不 fallback。</para>
        /// </remarks>
        internal UnitRegistry(
            UnitFactory unitFactory,
            float cellSize,
            BuffManager buffManager = null,
            BasicWeaponResolver weaponResolver = null)
        {
            _unitFactory = unitFactory ?? throw new ArgumentNullException(nameof(unitFactory));
            _cellSize = cellSize > 0 ? cellSize : 80f;
            _buffManager = buffManager;
            _weaponResolver = weaponResolver;
        }

        // ====================================================================
        // 装配武将技能运行时（窄方法，一次性）
        // --------------------------------------------------------------------
        // Wave 3：武将主动技能普通攻击计数与生命周期绑定。本方法由
        // BattleRuntimeFactory 在构造后一次性装配，不影响旧测试行为（不装配时
        // _skillRuntime 为 null，所有技能路径跳过）。
        // ====================================================================

        /// <summary>
        /// 一次性装配武将主动技能运行时（窄方法）。
        /// </summary>
        /// <param name="skillRuntime">武将技能运行时（非 null）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="skillRuntime"/> 为 null。</exception>
        /// <exception cref="InvalidOperationException">重复装配。</exception>
        /// <remarks>
        /// <para>装配后，General 首次上场时按 config.SkillId 绑定技能租期；
        /// 场内换位复用不重复绑定；Deactivate/Remove 前解除租期；GameOver 清局。</para>
        /// <para><b>未装配时旧行为不变：</b>普通兵与未装配 runtime 的旧测试不受影响。</para>
        /// </remarks>
        internal void AssembleGeneralSkillRuntime(GeneralSkillRuntime skillRuntime)
        {
            if (skillRuntime == null)
            {
                throw new ArgumentNullException(nameof(skillRuntime));
            }

            if (_skillRuntime != null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} GeneralSkillRuntime 已装配，禁止重复装配。");
            }

            _skillRuntime = skillRuntime;
        }

        // ====================================================================
        // ApplyDefaultWeapon —— 玩家默认基础武器应用（Acquire 后、Register 前）
        // --------------------------------------------------------------------
        // design.md 决策 3/4 / spec "Player Soldiers receive one default weapon value"：
        //   玩家 Soldier 完成配置和等级初始化后，由本方法调用 resolver 写入武器；
        //   对手跳过；解析失败抛 InvalidOperationException（不隐式 fallback），
        //   由调用方沿创建事务回滚（Release/ResetState 保证无残留）。
        // ====================================================================

        /// <summary>
        /// 为玩家 Soldier 应用默认基础武器（对手跳过；解析失败抛错，不 fallback）。
        /// </summary>
        /// <param name="soldier">已 Acquire 并完成配置/等级初始化的士兵。</param>
        /// <param name="type">兵种类型。</param>
        /// <exception cref="InvalidOperationException">玩家兵种无法解析默认武器
        /// （resolver 已装配但映射缺失）。</exception>
        /// <remarks>
        /// <para><b>旧兼容语义：</b>resolver 未装配（旧测试直接构造 UnitRegistry）时
        /// 本方法为空操作，保持既有行为；生产路径恒注入 resolver。</para>
        /// <para><b>对手跳过：</b><see cref="SoldierBase.Side"/> 为 false 时不装配，
        /// 对手 Soldier 保持无武器（spec "Opponent Soldiers SHALL store no player
        /// weapon contribution"）。</para>
        /// </remarks>
        private void ApplyDefaultWeapon(SoldierBase soldier, SoldierType type)
        {
            if (_weaponResolver == null || !soldier.Side)
            {
                return;
            }

            if (!_weaponResolver.TryResolve(type, out WeaponDefinitionSnapshot definition))
            {
                throw new InvalidOperationException(
                    $"{LogTag} 玩家兵种 {type} 缺少默认基础武器配置，禁止隐式 fallback");
            }

            soldier.ApplyBasicWeapon(definition.Id, definition.AddAttackPower);
        }

        // ====================================================================
        // CreateAndPlace —— 创建并放置单位（对应 JS createUnit → createFromDescriptor）
        // ====================================================================

        /// <summary>
        /// 创建一个士兵并放置到指定网格坐标。
        /// </summary>
        /// <param name="type">强类型兵种 ID。</param>
        /// <param name="config">单位配置快照。</param>
        /// <param name="side">阵营：true=玩家方，false=对手方。</param>
        /// <param name="gridX">网格 X（列）。</param>
        /// <param name="gridY">网格 Y（行）。</param>
        /// <param name="unitWidth">逻辑宽度。</param>
        /// <param name="unitHeight">逻辑高度。</param>
        /// <returns>已创建并放置的士兵。</returns>
        /// <exception cref="InvalidOperationException">指定格子已被占用。</exception>
        /// <remarks>
        /// <para><b>对应 JS <c>createUnit → createFromDescriptor</c>（UnitRegistry.js:68-107）：</b>
        /// 创建 → setPlacement → register → place。本期裁剪了
        /// isGameOver 守卫 / classifyText / levelUp / buff / weapon。</para>
        ///
        /// <para><b>放置冲突检测（对应 JS <c>hasBattleOccupant</c>，UnitRegistry.js:150-158）：</b>
        /// 同一格子不可被重复占用。若已占用抛 <see cref="InvalidOperationException"/>。
        /// 对应 spec "Input commands are atomic" 要求不留下半注册单位。</para>
        ///
        /// <para><b>放置坐标计算（对应 JS <c>place</c>，UnitRegistry.js:122-130）：</b>
        /// pixelX = gridX * cellSize, pixelY = gridY * cellSize。
        /// C# 移植删除 parent/zIndex（表现层），只设置逻辑像素位置。</para>
        ///
        /// <para><b>调用方职责：</b>调用方（BattleInputController，task 6.7）在调用本方法前
        /// 应已完成格子可建造性校验（MapData.IsBuildableForSide）与格子预留
        /// （PlacementReservationRegistry）。本方法只负责创建/注册/放置。</para>
        /// </remarks>
        internal SoldierBase CreateAndPlace(
            SoldierType type,
            UnitConfigSnapshot config,
            bool side,
            int gridX,
            int gridY,
            float unitWidth,
            float unitHeight)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            // 放置冲突检测（对应 JS hasBattleOccupant，UnitRegistry.js:150-158）。
            if (HasOccupant(side, gridX, gridY))
            {
                throw new InvalidOperationException(
                    $"{LogTag} 格子 ({gridX},{gridY}) 已被占用 side={side}");
            }

            // 创建单位（对应 JS unitFactory.createByText，UnitRegistry.js:95）。
            SoldierBase unit = _unitFactory.Acquire(type, config, side, unitWidth, unitHeight);

            // 玩家默认武器：Acquire 后、Register 前应用（对手跳过）。
            // 解析失败时归还本次租借到池（ResetState 清除状态）再重新抛出，
            // 沿现有创建事务回滚，不留下半初始化单位。
            try
            {
                ApplyDefaultWeapon(unit, type);
            }
            catch
            {
                _unitFactory.Release(unit);
                throw;
            }

            // 设置网格坐标（对应 JS unit.setPlacement，UnitRegistry.js:96）。
            unit.SetPlacement(gridX, gridY);

            // 注册到集合（对应 JS register，UnitRegistry.js:98）。
            Register(unit);

            // 放置：设置逻辑像素位置并激活（对应 JS place，UnitRegistry.js:122-130）。
            float pixelX = gridX * _cellSize;
            float pixelY = gridY * _cellSize;
            unit.ActivatePlacement(pixelX, pixelY);
            UnitPlaced?.Invoke(unit.Id, side, (int)type, gridX, gridY, unit.UnitLevel);

            return unit;
        }

        // ====================================================================
        // ActivateBattleUnit / DeactivateBattleUnit —— 激活/解除战场槽中的战斗实例
        // --------------------------------------------------------------------
        // 最终方案"UnitRegistry 从'创建并购买'改成'激活战场槽中的单位'"：
        //   - 战场槽换位时复用同一战斗实例（UnitId → BattleRuntimeId 映射）。
        //   - 下场时解除战斗实例，但保留 BattleUnit（槽位状态由 UnitSlotBoard 维护）。
        //   - GetActiveUnits 仍只返回战场实例，待上场单位永远不会进入攻击调度。
        // ====================================================================

        /// <summary>
        /// 激活/复用指定局内单位在指定战场格子的战斗实例（最终方案）。
        /// </summary>
        /// <param name="unit">局内单位权威数据。</param>
        /// <param name="config">单位配置快照。</param>
        /// <param name="levelService">等级数值服务（应用局内等级倍率）。</param>
        /// <param name="gridX">战场格子列索引。</param>
        /// <param name="gridY">战场格子行索引。</param>
        /// <param name="unitWidth">逻辑宽度。</param>
        /// <param name="unitHeight">逻辑高度。</param>
        /// <returns>激活或复用的战斗实例。</returns>
        /// <remarks>
        /// <para><b>战场槽换位时复用同一战斗实例：</b>若该 <see cref="BattleUnit.UnitId"/>
        /// 已有活动战斗实例，只重新放置（SetPlacement + ActivatePlacement），不重新 Acquire。</para>
        /// <para><b>首次上场：</b>经 <see cref="UnitFactory.Acquire(BattleUnit, UnitConfigSnapshot, float, float)"/>
        /// 创建并应用等级，再放置到战场格。</para>
        /// </remarks>
        internal SoldierBase ActivateBattleUnit(
            BattleUnit unit,
            UnitConfigSnapshot config,
            UnitLevelService levelService,
            int gridX,
            int gridY,
            float unitWidth,
            float unitHeight)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            // 战场槽换位复用：该单位已有活动战斗实例则重新放置。
            if (_unitIdToSoldier.TryGetValue(unit.UnitId, out SoldierBase existing))
            {
                // 复用路径不取消待释放攻击：需求只要求"被消耗的源单位"（合并源/下场）
                // 取消尚未释放的攻击，由 DeactivateBattleUnit 处理；移动/合并保留方
                // 保留待释放攻击，已发射投射物继续飞行。
                existing.SetPlacement(gridX, gridY);
                float pixelX = gridX * _cellSize;
                float pixelY = gridY * _cellSize;
                existing.ActivatePlacement(pixelX, pixelY);
                // 等级可能因合并提升，重新应用数值。
                existing.ConfigureLevel(levelService);
                existing.ApplyLevel(unit.Level);
                _buffManager?.RefreshTarget(
                    ((IBuffTarget)existing).Handle,
                    new[]
                    {
                        BuffNumericChannel.AttackPower,
                        BuffNumericChannel.AttackSpeed,
                        BuffNumericChannel.AttackRange,
                    });
                // 战场换格不导入 Board 冷却（Board 冷却已由调用方同步为实时值）。
                // 战场槽换位复用同一战斗实例，发布位置与等级变化表现事实。
                UnitMoved?.Invoke(existing.Id, gridX, gridY);
                UnitLevelChanged?.Invoke(existing.Id, unit.Level);
                return existing;
            }

            // 首次上场：创建并应用等级，再放置。
            SoldierBase soldier = _unitFactory.Acquire(unit, config, unitWidth, unitHeight);
            // 玩家默认武器：Acquire 后、Register 前应用（对手跳过）；失败归还池再抛出。
            try
            {
                if (unit.Kind == UnitKind.Soldier)
                {
                    ApplyDefaultWeapon(soldier, unit.SoldierType);
                }
            }
            catch
            {
                _unitFactory.Release(soldier);
                throw;
            }

            // 修复 P0：首次上场导入冷却（上下场不刷新攻击冷却）。
            soldier.ImportAttackCooldown(unit.LastAttackTimeMs);
            soldier.SetPlacement(gridX, gridY);
            Register(soldier);
            float px = gridX * _cellSize;
            float py = gridY * _cellSize;
            soldier.ActivatePlacement(px, py);
            _unitIdToSoldier[unit.UnitId] = soldier;

            // Wave 3：武将首次上场绑定技能租期（普通兵或无 SkillKey 跳过）。
            // 绑定失败沿创建事务完整回滚（移除注册集合/Buff/映射并归还池），
            // 不发布 UnitPlaced/ConfiguredUnitPlaced，不留下半绑定单位。
            if (_skillRuntime != null && unit.Kind == UnitKind.General
                && config.SkillId.HasValue)
            {
                try
                {
                    _skillRuntime.Bind(unit.UnitId, soldier, config.SkillId.Value);
                }
                catch
                {
                    // 完整注册回滚：移除 _soldiers/_idToIndex/Buff target/_unitIdToSoldier，
                    // 只 Release 一次；Unbind 幂等（Bind 失败时租期已原子回滚）。
                    RemoveFromRegistry(soldier);
                    _unitFactory.Release(soldier);
                    throw;
                }
            }

            UnitPlaced?.Invoke(soldier.Id, unit.Side, (int)unit.SoldierType, gridX, gridY, unit.Level);
            PublishConfiguredUnitPlaced(soldier.Id, unit, config, gridX, gridY);
            return soldier;
        }

        // ====================================================================
        // 原子事务分段：Prepare / ReleasePrepared / ActivatePrepared
        // --------------------------------------------------------------------
        // 修复 P0：把"创建并激活"拆成可回滚的两段，使 BattleInputController 能在
        // Commit Board 之前完成全部可能抛错的对象池 Acquire，任何失败都不影响槽位。
        // ====================================================================

        /// <summary>
        /// 准备（但不注册/放置）一个新战斗实例。可抛错，供 Controller 在 Commit Board 前调用。
        /// </summary>
        /// <param name="unit">局内单位权威数据。</param>
        /// <param name="config">单位配置快照（非 null）。</param>
        /// <param name="unitWidth">逻辑宽度。</param>
        /// <param name="unitHeight">逻辑高度。</param>
        /// <returns>已 Acquire + Configure + Init + ApplyLevel + 导入冷却的士兵（尚未注册/放置）。</returns>
        /// <remarks>
        /// <para>返回的实例尚未 Register（不在 <see cref="_soldiers"/> 中，不在攻击调度内）。
        /// 调用方必须在 <see cref="ActivatePrepared"/> 前持有它；若事务失败，
        /// 调用方应经 <see cref="ReleasePrepared"/> 归还池。</para>
        /// </remarks>
        internal SoldierBase PrepareBattleInstance(
            BattleUnit unit,
            UnitConfigSnapshot config,
            float unitWidth,
            float unitHeight)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            SoldierBase soldier = _unitFactory.Acquire(unit, config, unitWidth, unitHeight);
            // 玩家默认武器：Acquire 后、Register 前应用（对手跳过）。解析失败时归还
            // 本次租借到池（ResetState 清除状态）再重新抛出，使调用方事务可经
            // ReleasePrepared 回滚且不留武器残留。
            try
            {
                if (unit.Kind == UnitKind.Soldier)
                {
                    ApplyDefaultWeapon(soldier, unit.SoldierType);
                }
            }
            catch
            {
                _unitFactory.Release(soldier);
                throw;
            }

            soldier.ImportAttackCooldown(unit.LastAttackTimeMs);
            return soldier;
        }

        /// <summary>
        /// 归还一个已准备但未激活的战斗实例到池（事务失败回滚用）。
        /// </summary>
        /// <param name="soldier">已准备但未激活的实例。</param>
        internal void ReleasePrepared(SoldierBase soldier)
        {
            if (soldier == null || soldier.InPool)
            {
                return;
            }

            soldier.CancelUnreleasedAttacks();
            _unitFactory.Release(soldier);
        }

        /// <summary>
        /// 激活一个已准备的战斗实例：注册 + 放置 + 激活 + 事实发布。
        /// </summary>
        /// <param name="unit">局内单位权威数据。</param>
        /// <param name="soldier">已准备实例。</param>
        /// <param name="levelService">等级数值服务。</param>
        /// <param name="gridX">战场格子列索引。</param>
        /// <param name="gridY">战场格子行索引。</param>
        /// <returns>已激活的战斗实例。</returns>
        internal SoldierBase ActivatePrepared(
            BattleUnit unit,
            SoldierBase soldier,
            UnitLevelService levelService,
            int gridX,
            int gridY,
            UnitConfigSnapshot config = null)
        {
            if (soldier == null)
            {
                throw new ArgumentNullException(nameof(soldier));
            }

            soldier.SetPlacement(gridX, gridY);
            Register(soldier);
            float px = gridX * _cellSize;
            float py = gridY * _cellSize;
            soldier.ActivatePlacement(px, py);
            _unitIdToSoldier[unit.UnitId] = soldier;

            // Wave 3：武将首次上场绑定技能租期（普通兵或无 SkillKey 跳过）。
            // 绑定失败沿创建事务完整回滚（移除注册集合/Buff/映射并归还池），
            // 不发布 UnitPlaced/ConfiguredUnitPlaced；外层 ReleasePrepared 因池级
            // 重复 Release 守卫幂等，不会双重入池。
            if (_skillRuntime != null && unit.Kind == UnitKind.General
                && config != null && config.SkillId.HasValue)
            {
                try
                {
                    _skillRuntime.Bind(unit.UnitId, soldier, config.SkillId.Value);
                }
                catch
                {
                    // 完整注册回滚：移除 _soldiers/_idToIndex/Buff target/_unitIdToSoldier，
                    // 只 Release 一次；Unbind 幂等（Bind 失败时租期已原子回滚）。
                    RemoveFromRegistry(soldier);
                    _unitFactory.Release(soldier);
                    throw;
                }
            }

            UnitPlaced?.Invoke(soldier.Id, unit.Side, (int)unit.SoldierType, gridX, gridY, unit.Level);
            PublishConfiguredUnitPlaced(soldier.Id, unit, config, gridX, gridY);
            return soldier;
        }

        private void PublishConfiguredUnitPlaced(
            int runtimeId,
            BattleUnit unit,
            UnitConfigSnapshot config,
            int gridX,
            int gridY)
        {
            ConfiguredUnitPlaced?.Invoke(new UnitSpawnViewData(
                runtimeId,
                unit.Side,
                (int)unit.Kind,
                unit.GeneralIndex,
                unit.Kind == UnitKind.General ? unit.GeneralName : unit.SoldierText,
                (int)unit.SoldierType,
                config?.PrefabAddress,
                config?.AnimationKey,
                gridX,
                gridY,
                unit.Level));
        }

        /// <summary>
        /// 按 SoldierBase 实例反向查找 UnitId（供 Remove 中的技能 Unbind 使用）。
        /// </summary>
        /// <param name="soldier">要查找的战斗实例。</param>
        /// <returns>匹配的 UnitId；未找到返回 0。</returns>
        private int FindUnitIdBySoldier(SoldierBase soldier)
        {
            foreach (var pair in _unitIdToSoldier)
            {
                if (ReferenceEquals(pair.Value, soldier))
                {
                    return pair.Key;
                }
            }

            return 0;
        }

        /// <summary>
        /// 解除指定局内单位的战斗实例（最终方案"下场时解除战斗实例，但保留 BattleUnit"）。
        /// </summary>
        /// <param name="unitId">局内单位权威 ID（BattleUnit.UnitId）。</param>
        /// <returns>
        /// 成功时返回该单位的冷却时间戳（调用方写回 BattleUnit）；无活动实例返回 -1。
        /// </returns>
        /// <remarks>
        /// <para><b>修复 P0（冷却写回）：</b>下场前导出 <see cref="SoldierBase.LastAttackTimeMs"/>，
        /// 供调用方经 <see cref="BattleUnit.WithAttackCooldown"/> 写回 BattleUnit，保证
        /// "上下场不刷新攻击冷却"。随后取消未释放攻击（已发射投射物继续飞行）并回池。</para>
        /// <para>槽位中的 <see cref="BattleUnit"/> 保留（由 UnitSlotBoard 维护）。</para>
        /// </remarks>
        internal long DeactivateBattleUnit(int unitId)
        {
            if (!_unitIdToSoldier.TryGetValue(unitId, out SoldierBase soldier))
            {
                return -1L;
            }

            _unitIdToSoldier.Remove(unitId);

            // Wave 3：池回收前解除武将技能租期（保留 AttackCount）。
            _skillRuntime?.Unbind(unitId, soldier);

            long cooldown = soldier.LastAttackTimeMs;
            soldier.CancelUnreleasedAttacks();
            Remove(soldier.Id);
            return cooldown;
        }

        /// <summary>按局内单位权威 ID 查找活动战斗实例（无则返回 null）。</summary>
        internal SoldierBase GetActiveByUnitId(int unitId)
        {
            return _unitIdToSoldier.TryGetValue(unitId, out SoldierBase soldier) ? soldier : null;
        }

        /// <summary>读取活动战斗实例的实时攻击冷却（修复 P0：战斗内冷却同步）。</summary>
        /// <param name="unitId">局内单位权威 ID。</param>
        /// <param name="lastAttackTimeMs">输出实时冷却；无活动实例时为 0。</param>
        /// <returns>true=该单位当前有活动战斗实例。</returns>
        internal bool TryGetLiveCooldown(int unitId, out long lastAttackTimeMs)
        {
            if (_unitIdToSoldier.TryGetValue(unitId, out SoldierBase soldier))
            {
                lastAttackTimeMs = soldier.LastAttackTimeMs;
                return true;
            }

            lastAttackTimeMs = 0L;
            return false;
        }

        // ====================================================================
        // Register —— 注册单位到集合（对应 JS register，UnitRegistry.js:114-120）
        // ====================================================================

        /// <summary>
        /// 注册士兵到稳定有序集合。
        /// </summary>
        /// <param name="unit">要注册的士兵。不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="unit"/> 为 null。</exception>
        /// <exception cref="InvalidOperationException">单位 ID 已存在（重复注册）。</exception>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitRegistry.register</c>（UnitRegistry.js:114-120），
        /// JS 中 <c>soldiers.set(unit.id, unit)</c>。C# 移植用 List + Dictionary
        /// 保持稳定有序遍历 + O(1) 查找。</para>
        /// <para><b>重复注册守卫：</b>同一 ID 不可被重复注册。</para>
        /// </remarks>
        internal void Register(SoldierBase unit)
        {
            if (unit == null)
            {
                throw new ArgumentNullException(nameof(unit));
            }

            if (unit.Id <= 0)
            {
                throw new InvalidOperationException(
                    $"{LogTag} 注册失败：单位 ID 无效 ({unit.Id})，需先由 UnitFactory.Acquire 分配 ID");
            }

            if (_idToIndex.ContainsKey(unit.Id))
            {
                throw new InvalidOperationException(
                    $"{LogTag} 注册失败：单位 ID {unit.Id} 已存在（重复注册）");
            }

            if (_buffManager != null)
            {
                BuffOperationResult result = _buffManager.RegisterTarget(unit);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"{LogTag} 注册 Buff 目标失败：{result.Status} {result.Message}");
                }
            }

            _idToIndex[unit.Id] = _soldiers.Count;
            _soldiers.Add(unit);
        }

        // ====================================================================
        // Remove —— 移除单位并归还池（对应 JS removeSoldier，UnitRegistry.js:197-205）
        // ====================================================================

        /// <summary>
        /// 按运行时 ID 移除士兵并归还池。
        /// </summary>
        /// <param name="id">运行时 ID。</param>
        /// <returns>true=找到并移除；false=未找到。</returns>
        /// <remarks>
        /// <para><b>对应 JS <c>removeSoldier</c>（UnitRegistry.js:197-205）：</b>
        /// placementReservations.delete → unit.gameOver → soldiers.delete。
        /// C# 移植：GameOver → 末尾交换移除 → UnitFactory.Release。</para>
        ///
        /// <para><b>GameOver 调用（对应 JS unit.gameOver()）：</b>
        /// 先调 <see cref="SoldierBase.GameOver"/> 取消本单位发起的活动攻击效果
        /// 并标记 inPool/destroyed，再从集合移除并归还池。</para>
        ///
        /// <para><b>末尾交换法：</b>用末尾元素填补被移除位置，O(1) 移除。
        /// 保持剩余元素顺序不变。同步更新被交换元素的索引映射。</para>
        ///
        /// <para><b>Wave 3：同步清除 UnitId 映射。</b>反查到 UnitId 后解除技能租期，
        /// 并同步删除 <see cref="_unitIdToSoldier"/> 中的匹配项，避免直接 Remove
        /// 留下陈旧映射。</para>
        /// </remarks>
        internal bool Remove(int id)
        {
            if (!_idToIndex.TryGetValue(id, out int index))
            {
                return false;
            }

            SoldierBase unit = _soldiers[index];
            RemoveFromRegistry(unit);

            // 先发布移除事实，再归还对象池；回收后运行时 ID 会被重置。
            UnitRemoved?.Invoke(id);

            // 归还池（对应 JS objectPool.recoverByClass，经 UnitFactory.Release）。
            _unitFactory.Release(unit);

            return true;
        }

        /// <summary>
        /// 把单位从注册集合移除并解除 Buff 目标与武将技能租期；不发布事件、不归还池。
        /// </summary>
        /// <remarks>
        /// <para><b>共享回滚路径：</b><see cref="Remove"/> 与两个激活路径的 Bind 失败
        /// 回滚（<see cref="ActivateBattleUnit"/> / <see cref="ActivatePrepared"/>）共用本方法：
        /// 解除 Buff 目标、解除技能租期、同步删除 <see cref="_unitIdToSoldier"/> 映射、
        /// GameOver 标记回收、末尾交换法从 <see cref="_soldiers"/> 移除。</para>
        /// <para>事件发布与池归还由调用方决定：Remove 发布 UnitRemoved 并 Release；
        /// 回滚路径不发布任何事件且只 Release 一次。</para>
        /// </remarks>
        private void RemoveFromRegistry(SoldierBase unit)
        {
            // Wave 3：池回收前解除武将技能租期（保留 AttackCount）。
            // 通过反向查找 UnitId；Deactivate 已解除时 Unbind 幂等跳过。
            int unitId = FindUnitIdBySoldier(unit);
            if (_skillRuntime != null && unitId > 0)
            {
                _skillRuntime.Unbind(unitId, unit);
            }

            // Wave 3：同步删除 UnitId → 战斗实例映射，避免陈旧映射残留。
            if (unitId > 0)
            {
                _unitIdToSoldier.Remove(unitId);
            }

            _buffManager?.UnregisterTarget(((IBuffTarget)unit).Handle);

            // 调 GameOver 取消活动攻击效果并标记回收（对应 JS unit.gameOver()）。
            unit.GameOver();

            // 末尾交换法移除（保持剩余元素顺序稳定）。
            int index = _idToIndex[unit.Id];
            int lastIndex = _soldiers.Count - 1;
            if (index != lastIndex)
            {
                SoldierBase lastUnit = _soldiers[lastIndex];
                _soldiers[index] = lastUnit;
                _idToIndex[lastUnit.Id] = index;
            }

            _soldiers.RemoveAt(lastIndex);
            _idToIndex.Remove(unit.Id);
        }

        // ====================================================================
        // GetUnit —— 按运行时 ID 查找（对应 JS getUnit，UnitRegistry.js:160-162）
        // ====================================================================

        /// <summary>
        /// 按运行时 ID 查找士兵。
        /// </summary>
        /// <param name="id">运行时 ID。</param>
        /// <returns>找到返回士兵；未找到返回 null。</returns>
        /// <remarks>
        /// 对应还原工程 <c>UnitRegistry.getUnit</c>（UnitRegistry.js:160-162），
        /// JS 中 <c>soldiers.get(id) || ...</c>。本期只有 Soldier，直接从 _idToIndex 查找。
        /// </remarks>
        internal SoldierBase GetUnit(int id)
        {
            if (_idToIndex.TryGetValue(id, out int index))
            {
                return _soldiers[index];
            }

            return null;
        }

        // ====================================================================
        // HasOccupant —— 格子占用检查（对应 JS hasBattleOccupant，UnitRegistry.js:150-158）
        // ====================================================================

        /// <summary>
        /// 判断指定阵营的网格格子是否已被占用。
        /// </summary>
        /// <param name="side">阵营。</param>
        /// <param name="gridX">网格 X。</param>
        /// <param name="gridY">网格 Y。</param>
        /// <returns>已占用返回 true。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitRegistry.hasBattleOccupant</c>（UnitRegistry.js:150-158）。
        /// JS 遍历全部 Map 检查 side/gridX/gridY。C# 遍历 _soldiers 列表。</para>
        /// </remarks>
        internal bool HasOccupant(bool side, int gridX, int gridY)
        {
            for (int i = 0; i < _soldiers.Count; i++)
            {
                SoldierBase unit = _soldiers[i];
                if (unit.Side == side && unit.GridX == gridX && unit.GridY == gridY)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // MoveUnit —— 移动单位到新格子（对应 JS moveUnit，UnitRegistry.js:142-148）
        // ====================================================================

        /// <summary>
        /// 移动指定单位到新网格坐标。
        /// </summary>
        /// <param name="id">运行时 ID。</param>
        /// <param name="gridX">新网格 X。</param>
        /// <param name="gridY">新网格 Y。</param>
        /// <returns>true=移动成功；false=单位不存在或目标格子已被占用。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitRegistry.moveUnit</c>（UnitRegistry.js:142-148）：
        /// getUnit → canPlace → hasBattleOccupant → setPlacement → reposition。
        /// C# 移植裁剪 canPlace（由调用方校验），保留 hasBattleOccupant + setPlacement
        /// + 重新激活放置位置。</para>
        /// <para><b>本期使用场景：</b>本期 BattleInputController 不实现移动命令
        /// （task 6.4 裁剪），本方法保留供后续扩展和测试验证。</para>
        /// </remarks>
        internal bool MoveUnit(int id, int gridX, int gridY)
        {
            SoldierBase unit = GetUnit(id);
            if (unit == null)
            {
                return false;
            }

            // 目标格子已被其他单位占用时拒绝（排除自身）。
            if (HasOccupant(unit.Side, gridX, gridY))
            {
                // 自身已在目标格子时视为成功（幂等）。
                if (unit.GridX == gridX && unit.GridY == gridY)
                {
                    return true;
                }

                return false;
            }

            unit.SetPlacement(gridX, gridY);
            float pixelX = gridX * _cellSize;
            float pixelY = gridY * _cellSize;
            unit.ActivatePlacement(pixelX, pixelY);
            return true;
        }

        // ====================================================================
        // GetActiveUnits —— 获取活动单位只读列表（供 AttackScheduler 遍历）
        // ====================================================================

        /// <summary>
        /// 获取活动单位的只读列表快照，供 AttackScheduler 遍历。
        /// </summary>
        /// <returns>活动单位只读列表。</returns>
        /// <remarks>
        /// <para><b>稳定有序（task 4.6 约束）：</b>返回列表顺序由注册顺序决定，
        /// 不依赖 Dictionary/HashSet 的未定义遍历顺序。AttackScheduler 据此
        /// 每子步遍历驱动单位攻击。</para>
        /// <para><b>只读快照：</b>返回只读包装，调用方不可修改集合。
        /// 遍历中如需移除单位，应通过 <see cref="Remove"/> 而非直接操作列表。</para>
        /// </remarks>
        internal IReadOnlyList<SoldierBase> GetActiveUnits()
        {
            return _soldiers;
        }

        // ====================================================================
        // UnitsBySide —— 按阵营筛选（对应 JS unitsBySide，UnitRegistry.js:184）
        // ====================================================================

        /// <summary>
        /// 获取指定阵营的全部士兵（对应 JS <c>unitsBySide</c>，UnitRegistry.js:184）。
        /// </summary>
        /// <param name="side">阵营。</param>
        /// <returns>该阵营的士兵列表（新建列表，不影响内部集合）。</returns>
        internal List<SoldierBase> UnitsBySide(bool side)
        {
            var result = new List<SoldierBase>();
            for (int i = 0; i < _soldiers.Count; i++)
            {
                if (_soldiers[i].Side == side)
                {
                    result.Add(_soldiers[i]);
                }
            }

            return result;
        }

        // ====================================================================
        // GameOver —— 战斗结束清理（对应 JS gameOver，UnitRegistry.js:287-310）
        // ====================================================================

        /// <summary>
        /// 战斗结束清理：移除全部士兵并归还池。
        /// </summary>
        /// <remarks>
        /// <para><b>对应 JS <c>UnitRegistry.gameOver</c>（UnitRegistry.js:287-310）：</b>
        /// JS 遍历 generals/soldiers/secondaryUnits/farmers 逐一移除，再 clear 全部 Map。
        /// C# 移植只遍历 _soldiers，逐一 GameOver + Release，再清空集合。</para>
        ///
        /// <para><b>幂等：</b>重复调用安全。已空集合直接返回。</para>
        ///
        /// <para><b>清理顺序（对应 JS 顺序）：</b>
        /// 先收集 ID 列表（避免遍历中修改集合），再逐一移除。</para>
        ///
        /// <para><b>Settling 静默清理（design.md 决策 3）：</b>
        /// 本方法由 BattleRuntime 在 Settling 阶段调用，清理全部活动单位。
        /// 清理后 AttackScheduler 不再有任何活动单位可遍历。</para>
        /// </remarks>
        internal void GameOver()
        {
            // 收集 ID 列表（对应 JS scratchIds 复制 keys，UnitRegistry.js:294-295）。
            // 避免遍历中修改集合。
            var ids = new List<int>(_idToIndex.Keys.Count);
            foreach (int id in _idToIndex.Keys)
            {
                ids.Add(id);
            }

            // 逐一移除并归还池（对应 JS removeSoldier，UnitRegistry.js:296）。
            for (int i = 0; i < ids.Count; i++)
            {
                Remove(ids[i]);
            }

            // 清空集合（对应 JS soldiers.clear，UnitRegistry.js:306）。
            // Remove 已清空，此处防御性确保。
            _soldiers.Clear();
            _idToIndex.Clear();
            _unitIdToSoldier.Clear();

            // Wave 3：最终清局——清理全部武将技能租期与累计计数（幂等）。
            _skillRuntime?.Clear();
        }

        // ====================================================================
        // ClearForSettling —— Settling 阶段清理（与 GameOver 等价，语义别名）
        // ====================================================================

        /// <summary>
        /// Settling 静默清理：等价于 <see cref="GameOver"/>。
        /// </summary>
        /// <remarks>
        /// <para>由 <see cref="BattleRuntime"/> 在 Settling 阶段调用
        /// （design.md 决策 3 / spec "Runtime quiescence and cleanup"）。
        /// 语义与 <see cref="GameOver"/> 相同：移除全部活动单位并归还池。</para>
        /// <para>分离方法名以区分调用语义：GameOver 由战斗结束规则触发，
        /// ClearForSettling 由 Settling 静默清理流程触发。</para>
        /// </remarks>
        internal void ClearForSettling()
        {
            GameOver();
        }
    }
}
