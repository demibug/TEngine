using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.3：UnitFactory —— 由强类型兵种 ID 创建/回收四兵；未知兵种显式失败
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 184 行 / Unit/UnitFactory.cs）：
    //   由强类型兵种 ID 创建/回收四兵；未知兵种显式失败。
    //   使用 BattleObjectPool 池化四兵，Acquire 分配新 RuntimeId，
    //   Release 回收并 Reset。按兵种分支调用各士兵的 Configure。
    //
    // 来源证据（还原工程 UnitFactory.js:1-81）：
    //   UnitFactory 继承 SingletonBase，持有：
    //     - byIndex: Map<number, {index, text, ClassType}>
    //     - byText: Map<string, {index, text, ClassType}>
    //     - creationLog: []
    //     - _configured: false
    //   核心方法：
    //     - configure({objectPool, dependencyResolver})：
    //         校验 → 注册四兵 (0,'刀',KnifeSoldier)(1,'弓',BowSoldier)
    //                        (2,'枪',SpearSoldier)(3,'骑',CavalrySoldier)
    //     - register(index, text, ClassType)：注册到 byIndex/byText（重复抛错）
    //     - createByIndex(index)：未知 index 抛 UnresolvedFriendlyUnitTypeError
    //     - createByText(text)：未知 text 抛 UnresolvedFriendlyUnitTypeError
    //     - _create(entry)：
    //         unit = objectPool.takeByClass(entry.ClassType)
    //         unit.configure(dependencyResolver(unit))
    //         creationLog.push({index, text, unit})
    //         return unit
    //     - resetForTests()：清空注册表/日志
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase/CombatServices，改为强类型注入的
    //     internal sealed class，由 BattleRuntimeFactory 构造注入。
    //   - design.md 第 184 行：由强类型兵种 ID 创建/回收四兵；未知兵种显式失败。
    //   - design.md 第 9 行：规则层不持有 Unity GameObject 或表现组件。
    //   - task 6.3 约束：
    //     * 只识别四个强类型兵种 ID（刀/弓/枪/骑），未知兵种显式失败。
    //     * 使用 BattleObjectPool 池化四兵。
    //     * Acquire 分配新 RuntimeId（RuntimeIdAllocator）。
    //     * Release 回收并 Reset。
    //     * 按兵种分支调用各士兵的 Configure（注意 BowSoldier 多
    //       ProjectileFactory/ProjectileManager 参数）。
    //
    // 与 EnemyFactory 的模式对称（task 4.5 已 PASS）：
    //   - 构造注入 RuntimeIdAllocator + 四个 BattleObjectPool<具体兵种>。
    //   - Acquire：池取出 → 分配新 ID → Configure → Init → InitializeStats。
    //   - Release：委托池 Release（内部调 ResetState）。
    //   - 未知兵种显式失败：通过枚举 + switch 分派，default 分支抛异常。
    //
    // C# 与 JS 的差异：
    //   1. 删除 SingletonBase：改为 internal sealed class，构造注入。
    //   2. 删除 dependencyResolver 回调：JS 经 dependencyResolver(unit) 返回
    //      依赖配置对象。C# 改为按兵种分支直接调用各士兵的 Configure 方法，
    //      显式传递强类型依赖（BowSoldier 多 ProjectileFactory/ProjectileManager）。
    //   3. 删除 byIndex/byText Map 注册表：JS 用 Map 保存 index→ClassType 映射。
    //      C# 用 SoldierType 枚举 + switch 分派，编译期保证只有四兵种。
    //   4. Acquire 分配新 RuntimeId：JS 在 initialize 中分配 ID。
    //      C# 移植将 ID 分配提到 Factory.Acquire（与 EnemyFactory 模式一致，task 4.5），
    //      使池复用不复用旧 ID 的契约更显式。
    //   5. 创建日志：JS creationLog 保存 unit 引用。C# 只保存统计计数，
    //      避免持有活动对象引用造成 GC 污染。
    //
    // 不变量：
    //   1. 只识别四个强类型兵种 ID：SoldierType.Knife/Bow/Spear/Cavalry。
    //   2. 每次 Acquire 分配新 ID：通过 RuntimeIdAllocator.Allocate()。
    //   3. Release 后旧 ID/目标引用失效：BattleObjectPool.Release 调用 ResetState。
    //   4. Acquire/Release 对称：每次 Acquire 对应恰好一次 Release。
    //   5. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/表现组件。
    // ============================================================================

    /// <summary>
    /// 强类型兵种 ID 枚举。只识别刀/弓/枪/骑四种（design.md 第 184 行）。
    /// </summary>
    /// <remarks>
    /// <para>对应还原工程 UnitConfig.js BASE_SOLDIER_TEXTS 的四个索引
    /// （0=刀, 1=弓, 2=枪, 3=骑，UnitConfig.js:8）。</para>
    /// <para><b>未知兵种显式失败（design.md 第 184 行）：</b>
    /// <see cref="UnitFactory.Acquire"/> 只接受本枚举的四个值，
    /// 不存在"未知"值（枚举本身保证）。若未来扩展新兵种，需同步更新
    /// <see cref="UnitFactory.Acquire"/> 的 switch 分支。</para>
    /// </remarks>
    internal enum SoldierType
    {
        /// <summary>刀兵（index=0，对应 '刀'/KnifeSoldier）。</summary>
        Knife = 0,

        /// <summary>弓兵（index=1，对应 '弓'/BowSoldier）。</summary>
        Bow = 1,

        /// <summary>枪兵（index=2，对应 '枪'/SpearSoldier）。</summary>
        Spear = 2,

        /// <summary>骑兵（index=3，对应 '骑'/CavalrySoldier）。</summary>
        Cavalry = 3,
    }

    /// <summary>
    /// 由强类型兵种 ID 创建/回收四兵的工厂；未知兵种显式失败（task 6.3 / design.md 第 184 行）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 184 行）：</b>由强类型兵种 ID 创建/回收四兵；
    /// 未知兵种显式失败。替代还原工程 <c>UnitFactory.js</c>（UnitFactory.js:1-81）。</para>
    ///
    /// <para><b>池化四兵（task 6.3）：</b>使用四个独立的
    /// <see cref="BattleObjectPool{T}"/>（每种兵种一个池），Acquire 优先复用空闲对象，
    /// Release 归还并执行 <see cref="IPoolableBattleObject.ResetState"/>。
    /// 对应还原工程 <c>objectPool.takeByClass(ClassType)</c> + <c>recoverByClass</c>，
    /// 但从全局反射式 ObjectPool 改为按明确类型的强类型池（design 决策 5）。</para>
    ///
    /// <para><b>按兵种分支 Configure（task 6.3）：</b>
    /// <see cref="Acquire"/> 内按 <see cref="SoldierType"/> 分支调用各士兵的 Configure：
    /// <list type="bullet">
    /// <item>KnifeSoldier/SpearSoldier/CavalrySoldier：调用 5 参数 Configure
    ///   (enemyManager, attackResolver, attackEffectManager, cellSize, opponentAttackMultiplier)。</item>
    /// <item>BowSoldier：调用 7 参数 Configure（多 ProjectileFactory/ProjectileManager）。</item>
    /// </list></para>
    ///
    /// <para><b>每次 Acquire 分配新 ID（task 6.3）：</b>
    /// 取得对象后立即调用 <see cref="RuntimeIdAllocator.Allocate"/> 分配新 ID，
    /// 并通过 <c>AssignRuntimeId</c> 写入。池复用不复用旧 ID
    /// （与 EnemyFactory 模式一致，task 4.5）。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部 UnitRegistry /
    /// BattleRuntimeFactory 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class UnitFactory
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀，便于在日志中筛选单位工厂相关条目。</summary>
        private const string LogTag = "[UnitFactory]";

        // ====================================================================
        // 运行时 ID 分配器（每局新建，注入）
        // ====================================================================

        /// <summary>
        /// 运行时 ID 分配器。每次 <see cref="Acquire"/> 后分配新 ID，保证池复用不复用旧 ID。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在每局构造时注入，每局独立。</para>
        /// </summary>
        private readonly RuntimeIdAllocator _idAllocator;

        // ====================================================================
        // 四兵种对象池（跨局复用容量，注入）
        // ====================================================================

        /// <summary>刀兵对象池。</summary>
        private readonly BattleObjectPool<KnifeSoldier> _knifePool;

        /// <summary>弓兵对象池。</summary>
        private readonly BattleObjectPool<BowSoldier> _bowPool;

        /// <summary>枪兵对象池。</summary>
        private readonly BattleObjectPool<SpearSoldier> _spearPool;

        /// <summary>骑兵对象池。</summary>
        private readonly BattleObjectPool<CavalrySoldier> _cavalryPool;

        // ====================================================================
        // 注入依赖（Configure 各兵种时传递）
        // ====================================================================

        /// <summary>敌人管理器，供所有兵种 Configure 注入。</summary>
        private readonly EnemyManager _enemyManager;

        /// <summary>攻击解析服务，供所有兵种 Configure 注入。</summary>
        private readonly AttackResolver _attackResolver;

        /// <summary>攻击效果管理器，供所有兵种 Configure 注入。</summary>
        private readonly AttackEffectManager _attackEffectManager;

        /// <summary>投射物工厂，仅供弓兵 Configure 注入。</summary>
        private readonly ProjectileFactory _projectileFactory;

        /// <summary>投射物管理器，仅供弓兵 Configure 注入。</summary>
        private readonly ProjectileManager _projectileManager;

        /// <summary>格子尺寸（像素，对应 map.gridWidth=80）。</summary>
        private readonly float _cellSize;

        /// <summary>对手方攻击倍率（本期固定 1）。</summary>
        private readonly int _opponentAttackMultiplier;

        // ====================================================================
        // 诊断日志（对应 UnitFactory.js creationLog）
        // ====================================================================

        /// <summary>
        /// 创建日志：记录每次 Acquire 的兵种类型。
        /// <para>对应还原工程 <c>UnitFactory.creationLog</c>（UnitFactory.js:25）。
        /// 诊断用，不参与规则逻辑。只保存计数，不持有对象引用。</para>
        /// </summary>
        private readonly List<SoldierType> _createLog = new List<SoldierType>();

        /// <summary>
        /// 回收日志：记录每次 Release 的兵种类型。
        /// <para>诊断用，不参与规则逻辑。</para>
        /// </summary>
        private readonly List<SoldierType> _recoverLog = new List<SoldierType>();

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>已创建（Acquire）累计次数（诊断用）。</summary>
        internal int CreateCount => _createLog.Count;

        /// <summary>已回收（Release）累计次数（诊断用）。</summary>
        internal int RecoverCount => _recoverLog.Count;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造只识别四个强类型兵种 ID 的单位工厂。
        /// </summary>
        /// <param name="idAllocator">运行时 ID 分配器。每局新建，从 1 单调递增。不可为 null。</param>
        /// <param name="knifePool">刀兵对象池。不可为 null。</param>
        /// <param name="bowPool">弓兵对象池。不可为 null。</param>
        /// <param name="spearPool">枪兵对象池。不可为 null。</param>
        /// <param name="cavalryPool">骑兵对象池。不可为 null。</param>
        /// <param name="enemyManager">敌人管理器。不可为 null。</param>
        /// <param name="attackResolver">攻击解析服务。不可为 null。</param>
        /// <param name="attackEffectManager">攻击效果管理器。不可为 null。</param>
        /// <param name="projectileFactory">投射物工厂（弓兵专用）。不可为 null。</param>
        /// <param name="projectileManager">投射物管理器（弓兵专用）。不可为 null。</param>
        /// <param name="cellSize">格子尺寸（像素，对应 map.gridWidth=80）。</param>
        /// <param name="opponentAttackMultiplier">对手方攻击倍率（本期固定 1）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitFactory.configure({objectPool, dependencyResolver})</c>
        /// （UnitFactory.js:29-41），但从全局 SingletonBase + 字符串 ObjectPool +
        /// dependencyResolver 回调改为构造注入强类型依赖。</para>
        /// <para><b>只识别四兵种：</b>构造即绑定四个具体兵种池，不提供运行时注册
        /// 其他类型的能力。未知兵种在 <see cref="Acquire"/> 时由枚举保证不可达。</para>
        /// </remarks>
        internal UnitFactory(
            RuntimeIdAllocator idAllocator,
            BattleObjectPool<KnifeSoldier> knifePool,
            BattleObjectPool<BowSoldier> bowPool,
            BattleObjectPool<SpearSoldier> spearPool,
            BattleObjectPool<CavalrySoldier> cavalryPool,
            EnemyManager enemyManager,
            AttackResolver attackResolver,
            AttackEffectManager attackEffectManager,
            ProjectileFactory projectileFactory,
            ProjectileManager projectileManager,
            float cellSize,
            int opponentAttackMultiplier)
        {
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _knifePool = knifePool ?? throw new ArgumentNullException(nameof(knifePool));
            _bowPool = bowPool ?? throw new ArgumentNullException(nameof(bowPool));
            _spearPool = spearPool ?? throw new ArgumentNullException(nameof(spearPool));
            _cavalryPool = cavalryPool ?? throw new ArgumentNullException(nameof(cavalryPool));
            _enemyManager = enemyManager ?? throw new ArgumentNullException(nameof(enemyManager));
            _attackResolver = attackResolver ?? throw new ArgumentNullException(nameof(attackResolver));
            _attackEffectManager = attackEffectManager ?? throw new ArgumentNullException(nameof(attackEffectManager));
            _projectileFactory = projectileFactory ?? throw new ArgumentNullException(nameof(projectileFactory));
            _projectileManager = projectileManager ?? throw new ArgumentNullException(nameof(projectileManager));
            _cellSize = cellSize > 0 ? cellSize : 80f;
            _opponentAttackMultiplier = opponentAttackMultiplier > 0 ? opponentAttackMultiplier : 1;
        }

        // ====================================================================
        // Acquire —— 获取一个士兵（池复用 + 分配新 ID + Configure + Init + InitStats）
        // ====================================================================

        /// <summary>
        /// 按强类型兵种 ID 获取一个士兵。优先复用空闲对象，否则新建。
        /// 取得后分配新运行时 ID、Configure、Init、InitializeStats。
        /// </summary>
        /// <param name="type">强类型兵种 ID（刀/弓/枪/骑）。</param>
        /// <param name="config">单位配置快照（供 InitializeStats 读取数值）。</param>
        /// <param name="side">阵营：true=玩家方，false=对手方。</param>
        /// <param name="unitWidth">逻辑宽度（用于中心点计算）。</param>
        /// <param name="unitHeight">逻辑高度。</param>
        /// <returns>已分配新运行时 ID、已 Configure/Init/InitializeStats 的士兵。</returns>
        /// <remarks>
        /// <para><b>只识别四个强类型兵种 ID（design.md 第 184 行）：</b>
        /// <see cref="SoldierType"/> 枚举只有四个值，不存在"未知"值。
        /// 若未来新增兵种枚举值但未更新本方法的 switch 分支，default 分支抛出
        /// <see cref="ArgumentOutOfRangeException"/>，显式失败。</para>
        ///
        /// <para><b>按兵种分支 Configure（task 6.3）：</b>
        /// 弓兵多 <see cref="ProjectileFactory"/>/<see cref="ProjectileManager"/> 参数
        /// （BowSoldier.cs:100-123），其余三兵种只传通用 5 参数。</para>
        ///
        /// <para><b>每次 Acquire 分配新 ID（task 6.3）：</b>
        /// 取得对象后立即调用 <see cref="RuntimeIdAllocator.Allocate"/> 分配新 ID。
        /// 池复用不复用旧 ID（与 EnemyFactory 模式一致，task 4.5）。</para>
        ///
        /// <para><b>对应还原工程：</b>
        /// UnitFactory._create(entry)（UnitFactory.js:63-69） +
        /// UnitBase.initialize(unitText, side)（UnitBase.js:113-137）。
        /// C# 移植将 ID 分配从 initialize 提到 Factory.Acquire。</para>
        ///
        /// <para><b>对称契约：</b>每次 Acquire MUST 对应恰好一次 <see cref="Release"/>。
        /// 调用方（UnitRegistry）负责在 Settling/Exit 时归还全部活动单位。</para>
        /// </remarks>
        internal SoldierBase Acquire(
            SoldierType type,
            UnitConfigSnapshot config,
            bool side,
            float unitWidth,
            float unitHeight)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            SoldierBase unit;
            switch (type)
            {
                case SoldierType.Knife:
                {
                    KnifeSoldier soldier = _knifePool.Acquire();
                    if (soldier == null)
                    {
                        throw new InvalidOperationException(
                            $"{LogTag} 池返回 null 对象 type={nameof(KnifeSoldier)}");
                    }

                    soldier.AssignRuntimeIdForTest(_idAllocator.Allocate());
                    soldier.Configure(
                        _enemyManager, _attackResolver, _attackEffectManager,
                        _cellSize, _opponentAttackMultiplier);
                    soldier.InitForTest(config.Text, side, unitWidth, unitHeight);
                    soldier.InitStats(config);
                    unit = soldier;
                    break;
                }

                case SoldierType.Bow:
                {
                    BowSoldier soldier = _bowPool.Acquire();
                    if (soldier == null)
                    {
                        throw new InvalidOperationException(
                            $"{LogTag} 池返回 null 对象 type={nameof(BowSoldier)}");
                    }

                    soldier.AssignRuntimeIdForTest(_idAllocator.Allocate());
                    // 弓兵多 ProjectileFactory/ProjectileManager 参数（BowSoldier.cs:100-123）。
                    soldier.Configure(
                        _enemyManager, _attackResolver, _attackEffectManager,
                        _projectileFactory, _projectileManager,
                        _cellSize, _opponentAttackMultiplier);
                    soldier.InitForTest(config.Text, side, unitWidth, unitHeight);
                    soldier.InitStats(config);
                    unit = soldier;
                    break;
                }

                case SoldierType.Spear:
                {
                    SpearSoldier soldier = _spearPool.Acquire();
                    if (soldier == null)
                    {
                        throw new InvalidOperationException(
                            $"{LogTag} 池返回 null 对象 type={nameof(SpearSoldier)}");
                    }

                    soldier.AssignRuntimeIdForTest(_idAllocator.Allocate());
                    soldier.Configure(
                        _enemyManager, _attackResolver, _attackEffectManager,
                        _cellSize, _opponentAttackMultiplier);
                    soldier.InitForTest(config.Text, side, unitWidth, unitHeight);
                    soldier.InitStats(config);
                    unit = soldier;
                    break;
                }

                case SoldierType.Cavalry:
                {
                    CavalrySoldier soldier = _cavalryPool.Acquire();
                    if (soldier == null)
                    {
                        throw new InvalidOperationException(
                            $"{LogTag} 池返回 null 对象 type={nameof(CavalrySoldier)}");
                    }

                    soldier.AssignRuntimeIdForTest(_idAllocator.Allocate());
                    soldier.Configure(
                        _enemyManager, _attackResolver, _attackEffectManager,
                        _cellSize, _opponentAttackMultiplier);
                    soldier.InitForTest(config.Text, side, unitWidth, unitHeight);
                    soldier.InitStats(config);
                    unit = soldier;
                    break;
                }

                default:
                    // 未知兵种显式失败（design.md 第 184 行）。
                    // 枚举保证不可达，但 default 分支防御性抛出，确保未来新增枚举值不被静默忽略。
                    throw new ArgumentOutOfRangeException(
                        nameof(type), type, $"{LogTag} 未知兵种类型 {type}");
            }

            _createLog.Add(type);
            return unit;
        }

        // ====================================================================
        // Release —— 归还士兵到池（先 Reset 再入池，旧 ID/目标引用失效）
        // ====================================================================

        /// <summary>
        /// 归还一个士兵到对应兵种的池。先执行 <c>ResetState</c> 清除全部可变状态
        /// （包括运行时 ID、目标引用、攻击效果引用），再入池。
        /// </summary>
        /// <param name="unit">要归还的士兵。null 或已归还返回 false。</param>
        /// <returns>成功归还返回 true；null 或已归还（重复 Release）返回 false。</returns>
        /// <remarks>
        /// <para><b>旧 ID/目标引用失效（task 6.3 / 池复位契约）：</b>
        /// <see cref="BattleObjectPool{T}.Release"/> 在入池前调用
        /// <c>ResetState</c>，清除运行时 ID、阵营、目标列表、攻击效果引用等
        /// 全部可变状态。回收后对象等价于新构造，旧 ID/目标引用不得继续有效。</para>
        ///
        /// <para><b>按兵种分派池：</b>通过 <c>is</c> 类型检查分派到对应兵种的池。
        /// 若类型不匹配（不应发生），记录警告返回 false。</para>
        ///
        /// <para><b>重复 Release 安全：</b>已归还对象再次 Release 返回 false
        /// （对应 ObjectPool.js <c>__InPool</c> 语义）。</para>
        /// </remarks>
        internal bool Release(SoldierBase unit)
        {
            if (unit == null)
            {
                return false;
            }

            bool recovered;
            SoldierType type;

            switch (unit)
            {
                case KnifeSoldier knife:
                    recovered = _knifePool.Release(knife);
                    type = SoldierType.Knife;
                    break;

                case BowSoldier bow:
                    recovered = _bowPool.Release(bow);
                    type = SoldierType.Bow;
                    break;

                case SpearSoldier spear:
                    recovered = _spearPool.Release(spear);
                    type = SoldierType.Spear;
                    break;

                case CavalrySoldier cavalry:
                    recovered = _cavalryPool.Release(cavalry);
                    type = SoldierType.Cavalry;
                    break;

                default:
                    Log.Warning($"{LogTag} Release 收到未知士兵类型 {unit.GetType().Name}，忽略");
                    return false;
            }

            if (recovered)
            {
                _recoverLog.Add(type);
            }

            return recovered;
        }

        // ====================================================================
        // ResetForTests —— 测试重置（对应 UnitFactory.js:71-78）
        // ====================================================================

        /// <summary>
        /// 重置工厂诊断日志（仅供测试使用）。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitFactory.resetForTests()</c>（UnitFactory.js:71-78）。
        /// 清空 createLog/recoverLog。</para>
        /// <para><b>不重置池与 ID 分配器：</b>池和 ID 分配器的生命周期由
        /// <see cref="BattleRuntimeFactory"/> / <see cref="BattlePoolScope"/> 管理，
        /// 不由工厂自行重置。测试中通过新建工厂实例实现重置。</para>
        /// </remarks>
        internal void ResetForTests()
        {
            _createLog.Clear();
            _recoverLog.Clear();
        }
    }
}
