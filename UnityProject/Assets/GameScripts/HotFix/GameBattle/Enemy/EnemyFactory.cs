using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.4/3.5：EnemyFactory —— 封闭 key 注册表 + 四个独立类型池的敌人工厂
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 6 / specs/configured-enemy-spawning/spec.md）：
    //   - 新链：由 EnemyCatalogSnapshot 建封闭注册表（enemyKey → acquire/release 委托
    //     + 定义快照），Mob0～Mob3 各自从 BattlePoolScope.GetPool<T> 获取独立池；
    //     按 EnemySpawnRequest 完成租借、分配 runtimeId、解析数值、注入地图/目标/回调、
    //     初始化车道与 waveOrder、开始移动；任一步失败都回滚本次租借。
    //   - Release 按实际固定 key/type 分发，禁止 Mob0 强转。
    //   - 旧链临时兼容：保留 RuntimeFactory/既有测试所需的旧构造/Acquire/Release
    //     调用面（Acquire 返回 Mob0Enemy），下一波迁移并删除旧路径。
    //
    // 不变量：
    //   1. 封闭注册表：未知 enemyKey 在 Acquire 时显式失败，不创建占位敌人。
    //   2. 每次租借分配新 RuntimeId（RuntimeIdAllocator，从 1 单调递增）。
    //   3. Acquire/Release 对称：每次租借恰好一次 Release；初始化失败回滚到正确池。
    //   4. 数值解析唯一：新链数值统一经 EnemyStatsResolver，不反查可变全局表。
    //   5. 不预热：池在首次 Acquire 时才创建对象。
    // ============================================================================

    /// <summary>
    /// 封闭 key 注册表 + 四个独立类型池的敌人工厂（Mob0～Mob3）。
    /// </summary>
    /// <remarks>
    /// <para><b>新链（task 3.4）：</b>以 <see cref="EnemyCatalogSnapshot"/> 构造封闭注册表，
    /// 每个普通敌人键绑定独立的 <see cref="BattleObjectPool{T}"/>（经
    /// <see cref="BattlePoolScope.GetPool{T}"/> 惰性创建）。<see cref="Acquire(EnemySpawnRequest)"/>
    /// 按请求完成租借→分配 runtimeId→解析数值→注入依赖→初始化车道/waveOrder→开始移动；
    /// 任一步失败都把本次租借归还正确池后重新抛出。<see cref="Release(ConfiguredEnemyBase)"/>
    /// 按敌人自身的固定键分发，不依赖 Mob0 强转。</para>
    ///
    /// <para><b>旧链临时兼容（本 change 独立编译需要）：</b>保留
    /// <c>EnemyFactory(RuntimeIdAllocator, BattleObjectPool&lt;Mob0Enemy&gt;)</c> 构造与
    /// <c>Acquire()/Release(Mob0Enemy)</c> 调用面，供 <see cref="BattleRuntimeFactory"/>
    /// 与既有测试继续使用。下一波迁移后会删除旧反射/固定数值生产路径。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 EnemyManager / BattleRuntimeFactory
    /// 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class EnemyFactory
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选敌人工厂相关条目。
        /// </summary>
        private const string LogTag = "[EnemyFactory]";

        // ====================================================================
        // 固定类型索引表（供注册表一致性校验）
        // ====================================================================

        /// <summary>Mob0～Mob3 固定键与类型索引映射（键 → 类型索引）。</summary>
        private static readonly IReadOnlyDictionary<string, int> FixedTypeIndexByKey =
            new Dictionary<string, int>
            {
                ["Mob0"] = 0,
                ["Mob1"] = 1,
                ["Mob2"] = 2,
                ["Mob3"] = 3,
            };

        // ====================================================================
        // 注入依赖
        // ====================================================================

        /// <summary>
        /// 运行时 ID 分配器。每次租借分配新 ID，保证池复用不复用旧 ID。
        /// </summary>
        private readonly RuntimeIdAllocator _idAllocator;

        /// <summary>
        /// 封闭注册表：普通敌人键 → 租借/回收委托 + 定义快照。
        /// </summary>
        private readonly IReadOnlyDictionary<int, EnemyTypeRegistration> _registry;

        /// <summary>
        /// 旧链 Mob0 池（仅旧构造路径使用；新构造路径为 null）。
        /// </summary>
        private readonly BattleObjectPool<Mob0Enemy> _legacyMob0Pool;

        // ====================================================================
        // 诊断日志（对应 EnemyFactory.js createLog/recoverLog）
        // ====================================================================

        private readonly List<string> _createLog = new List<string>();

        private readonly List<string> _recoverLog = new List<string>();

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>已创建（Acquire）累计次数（诊断用）。</summary>
        internal int CreateCount => _createLog.Count;

        /// <summary>已回收（Release）累计次数（诊断用）。</summary>
        internal int RecoverCount => _recoverLog.Count;

        // ====================================================================
        // 构造 —— 旧链临时兼容
        // ====================================================================

        /// <summary>
        /// 【临时兼容】构造只注册 Mob0 的旧链敌人工厂。
        /// </summary>
        /// <param name="idAllocator">运行时 ID 分配器（不可为 null）。</param>
        /// <param name="mob0Pool">Mob0 对象池（不可为 null）。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        /// <remarks>
        /// <para>供 <see cref="BattleRuntimeFactory"/> 与既有测试使用；新链应使用
        /// <see cref="EnemyFactory(RuntimeIdAllocator, EnemyCatalogSnapshot, BattlePoolScope)"/>。
        /// 下一波迁移后本构造删除。</para>
        /// </remarks>
        internal EnemyFactory(RuntimeIdAllocator idAllocator, BattleObjectPool<Mob0Enemy> mob0Pool)
        {
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _legacyMob0Pool = mob0Pool ?? throw new ArgumentNullException(nameof(mob0Pool));
            _registry = new Dictionary<int, EnemyTypeRegistration>();
        }

        // ====================================================================
        // 构造 —— 新链封闭注册表
        // ====================================================================

        /// <summary>
        /// 以敌人目录建封闭注册表并构造敌人工厂：Mob0～Mob3 各自从
        /// <see cref="BattlePoolScope.GetPool{T}"/> 获取独立池。
        /// </summary>
        /// <param name="idAllocator">运行时 ID 分配器（不可为 null）。</param>
        /// <param name="catalog">不可变敌人目录（不可为 null）。</param>
        /// <param name="poolScope">战斗对象池作用域（不可为 null，跨局复用池容量）。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        /// <exception cref="ArgumentException">目录含未支持普通敌人键、key/typeIndex 不一致或重复。</exception>
        /// <remarks>
        /// <para>每个目录定义键都绑定一个独立 <see cref="BattleObjectPool{T}"/>（惰性创建、
        /// 不预热）。目录为空时注册表为空，<see cref="Acquire(EnemySpawnRequest)"/> 对任意
        /// 键显式失败。</para>
        /// </remarks>
        internal EnemyFactory(
            RuntimeIdAllocator idAllocator,
            EnemyCatalogSnapshot catalog,
            BattlePoolScope poolScope)
        {
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _legacyMob0Pool = null;
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (poolScope == null)
            {
                throw new ArgumentNullException(nameof(poolScope));
            }

            _registry = BuildRegistry(catalog, poolScope);
        }

        /// <summary>
        /// 由目录定义构建封闭注册表：按固定键绑定独立类型池，校验 key/typeIndex 一致。
        /// </summary>
        private static IReadOnlyDictionary<int, EnemyTypeRegistration> BuildRegistry(
            EnemyCatalogSnapshot catalog,
            BattlePoolScope poolScope)
        {
            var registry = new Dictionary<int, EnemyTypeRegistration>();
            foreach (EnemyDefinitionSnapshot definition in catalog.Definitions)
            {
                if (!FixedTypeIndexByKey.TryGetValue(definition.ResourceAddress, out int expectedTypeIndex))
                {
                    throw new ArgumentException(
                        $"{LogTag} 敌人目录 id={definition.Id} 的资源 '{definition.ResourceAddress}' 不受支持（只支持 Mob0～Mob3）");
                }

                if (definition.TypeIndex != expectedTypeIndex)
                {
                    throw new ArgumentException(
                        $"{LogTag} 目录 id={definition.Id} 的 typeIndex={definition.TypeIndex} " +
                        $"与固定类型索引 {expectedTypeIndex} 不一致");
                }

                if (registry.ContainsKey(definition.Id))
                {
                    throw new ArgumentException(
                        $"{LogTag} 敌人目录存在重复普通敌人 id={definition.Id}");
                }

                registry.Add(definition.Id, CreateRegistration(definition.ResourceAddress, definition, poolScope));
            }

            return registry;
        }

        /// <summary>
        /// 按固定键创建租借/回收委托并绑定独立类型池。
        /// </summary>
        private static EnemyTypeRegistration CreateRegistration(
            string key,
            EnemyDefinitionSnapshot definition,
            BattlePoolScope poolScope)
        {
            switch (key)
            {
                case "Mob0":
                {
                    BattleObjectPool<Mob0Enemy> pool = poolScope.GetPool(() => new Mob0Enemy());
                    return new EnemyTypeRegistration(
                        definition,
                        acquire: () => pool.Acquire(),
                        release: enemy => pool.Release((Mob0Enemy)enemy));
                }

                case "Mob1":
                {
                    BattleObjectPool<Mob1Enemy> pool = poolScope.GetPool(() => new Mob1Enemy());
                    return new EnemyTypeRegistration(
                        definition,
                        acquire: () => pool.Acquire(),
                        release: enemy => pool.Release((Mob1Enemy)enemy));
                }

                case "Mob2":
                {
                    BattleObjectPool<Mob2Enemy> pool = poolScope.GetPool(() => new Mob2Enemy());
                    return new EnemyTypeRegistration(
                        definition,
                        acquire: () => pool.Acquire(),
                        release: enemy => pool.Release((Mob2Enemy)enemy));
                }

                case "Mob3":
                {
                    BattleObjectPool<Mob3Enemy> pool = poolScope.GetPool(() => new Mob3Enemy());
                    return new EnemyTypeRegistration(
                        definition,
                        acquire: () => pool.Acquire(),
                        release: enemy => pool.Release((Mob3Enemy)enemy));
                }

                default:
                    throw new ArgumentException(
                        $"{LogTag} 未知普通敌人键 '{key}'（只支持 Mob0～Mob3）");
            }
        }

        // ====================================================================
        // 新链 Acquire —— 按 EnemySpawnRequest 完成租借与初始化
        // ====================================================================

        /// <summary>
        /// 按出生请求租借并初始化一个普通敌人：租借 → 分配 runtimeId → 解析数值 →
        /// 注入地图/终点/回调 → 初始化车道与 waveOrder → 开始移动。
        /// </summary>
        /// <param name="request">出生请求（携带已解析敌人键、车道、waveOrder、难度、
        /// 策略 profile 与初始化依赖；不可为 null）。</param>
        /// <returns>已初始化并开始移动的普通敌人（供调用方登记到 EnemyManager）。</returns>
        /// <exception cref="ArgumentNullException">request 为 null。</exception>
        /// <exception cref="ArgumentException">请求敌人键未注册（unknown key）。</exception>
        /// <exception cref="EnemyStatsResolutionException">difficultyIndex 越界（不夹取）。</exception>
        /// <exception cref="ArgumentNullException">请求携带的地图/目标/回调为 null（初始化失败）。</exception>
        /// <remarks>
        /// <para><b>失败回滚（design.md 决策 6）：</b>任一步失败都先把本次租借归还到
        /// 正确池（Reset + 入池），再重新抛出异常，保证池租借对称。</para>
        /// <para><b>generation（task 3.6）：</b>由 <see cref="ConfiguredEnemyBase.ConfiguredInit"/>
        /// 在每次租借递增 generation 并携带 waveOrder；返回后可通过
        /// <c>enemy.CurrentLease</c> 读取 <see cref="EnemyLeaseIdentity"/>。</para>
        /// </remarks>
        internal ConfiguredEnemyBase Acquire(EnemySpawnRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!_registry.TryGetValue(request.EnemyId, out EnemyTypeRegistration registration))
            {
                // 封闭注册表：未知键显式失败，不创建占位敌人（spec "Reject an unsupported normal enemy"）。
                throw new ArgumentException(
                    $"{LogTag} 未知敌人 id={request.EnemyId}（未注册，禁止创建占位敌人）");
            }

            ConfiguredEnemyBase enemy = registration.Acquire();
            if (enemy == null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} 池返回 null 对象 id={request.EnemyId}");
            }

            try
            {
                // 分配新运行时 ID（池复用不复用旧 ID）。
                int runtimeId = _idAllocator.Allocate();
                enemy.AssignRuntimeId(runtimeId);

                // 唯一数值解析器：difficultyIndex 同时索引血量曲线/策略乘数/早期乘数。
                ConfiguredEnemyResolvedStats stats = EnemyStatsResolver.Resolve(
                    registration.Definition,
                    request.DifficultyIndex,
                    request.StrategyProfile);

                // 注入数值与依赖、初始化车道与 waveOrder（generation 递增）。
                // resourceAddress 与 stats 来自同一 EnemyDefinitionSnapshot（task 5.4），
                // 薄类型不硬编码资源地址。
                enemy.ConfiguredInit(
                    request.Map,
                    request.CellSize,
                    request.EndPointTarget,
                    request.OnEnemyKilled,
                    request.OnDeathRequested,
                    stats,
                    registration.Definition.Id,
                    registration.Definition.ResourceAddress,
                    request.IsPlayerLane,
                    request.WaveOrder,
                    request.Width,
                    request.Height);

                // 开始移动（SPAWNING → MOVING）。
                enemy.BeginMoving();

                _createLog.Add(registration.Definition.ResourceAddress);
                return enemy;
            }
            catch
            {
                // 失败回滚：把本次租借归还正确池（Reset + 入池），再重新抛出。
                registration.Release(enemy);
                throw;
            }
        }

        // ====================================================================
        // 新链 Release —— 按实际固定 key/type 分发，禁止 Mob0 强转
        // ====================================================================

        /// <summary>
        /// 归还一个普通敌人到其正确类型的池（按 <see cref="ConfiguredEnemyBase.ResName"/> 分发）。
        /// </summary>
        /// <param name="enemy">要归还的普通敌人。null 或已归还返回 false。</param>
        /// <returns>成功归还返回 true；null、键未注册或重复 Release 返回 false。</returns>
        /// <remarks>
        /// <para>按敌人自身的固定键分发到注册表对应的独立池，不依赖 Mob0 强转；
        /// 池内部先执行 ResetState（清除本次 callbacks/waveOrder/迟到表现状态），再入池。</para>
        /// </remarks>
        internal bool Release(ConfiguredEnemyBase enemy)
        {
            if (enemy == null)
            {
                return false;
            }

            if (!_registry.TryGetValue(enemy.EnemyId, out EnemyTypeRegistration registration))
            {
                return false;
            }

            bool recovered = registration.Release(enemy);
            if (recovered)
            {
                _recoverLog.Add(enemy.ResName);
            }

            return recovered;
        }

        // ====================================================================
        // 旧链 Acquire/Release —— 临时兼容
        // ====================================================================

        /// <summary>
        /// 【临时兼容】旧链 Acquire：获取一个 <see cref="Mob0Enemy"/> 并分配新运行时 ID。
        /// </summary>
        /// <returns>已分配新运行时 ID 的 <see cref="Mob0Enemy"/>。</returns>
        /// <remarks>
        /// <para>只供旧构造路径使用（<see cref="EnemyFactory(RuntimeIdAllocator, BattleObjectPool{Mob0Enemy})"/>）；
        /// 新构造路径调用本方法抛 <see cref="InvalidOperationException"/>。下一波迁移删除。</para>
        /// </remarks>
        internal Mob0Enemy Acquire()
        {
            if (_legacyMob0Pool == null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} 旧 Acquire() 仅在旧构造路径可用（新构造请使用 Acquire(EnemySpawnRequest)）");
            }

            Mob0Enemy enemy = _legacyMob0Pool.Acquire();
            if (enemy == null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} 池返回 null 对象 type={nameof(Mob0Enemy)}");
            }

            int newId = _idAllocator.Allocate();
            enemy.AssignRuntimeId(newId);

            _createLog.Add(nameof(Mob0Enemy));
            return enemy;
        }

        /// <summary>
        /// 【临时兼容】旧链 Release：归还 <see cref="Mob0Enemy"/> 到池。
        /// </summary>
        /// <param name="enemy">要归还的 <see cref="Mob0Enemy"/>。null 或已归还返回 false。</param>
        /// <returns>成功归还返回 true；null 或重复 Release 返回 false。</returns>
        /// <remarks>
        /// <para>旧构造路径归还到旧链 Mob0 池；新构造路径（旧链池为 null）按键分发到
        /// 注册表 Mob0 池。下一波迁移删除旧重载。</para>
        /// </remarks>
        internal bool Release(Mob0Enemy enemy)
        {
            if (enemy == null)
            {
                return false;
            }

            if (_legacyMob0Pool != null)
            {
                bool recovered = _legacyMob0Pool.Release(enemy);
                if (recovered)
                {
                    _recoverLog.Add(nameof(Mob0Enemy));
                }

                return recovered;
            }

            return Release((ConfiguredEnemyBase)enemy);
        }

        // ====================================================================
        // ResetForTests —— 测试重置（对应 EnemyFactory.js:66-71）
        // ====================================================================

        /// <summary>
        /// 重置工厂诊断日志（仅供测试使用）。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyFactory.resetForTests()</c>。不重置池与 ID 分配器
        /// （其生命周期由 BattleRuntimeFactory / BattlePoolScope 管理）。</para>
        /// </remarks>
        internal void ResetForTests()
        {
            _createLog.Clear();
            _recoverLog.Clear();
        }

        // ====================================================================
        // 内部注册类型
        // ====================================================================

        /// <summary>
        /// 单类型注册：定义快照 + acquire/release 委托（绑定独立类型池）。
        /// </summary>
        private sealed class EnemyTypeRegistration
        {
            /// <summary>该键的敌人定义快照（数值解析来源）。</summary>
            public readonly EnemyDefinitionSnapshot Definition;

            /// <summary>租借委托（从独立类型池 Acquire）。</summary>
            public readonly Func<ConfiguredEnemyBase> Acquire;

            /// <summary>回收委托（归还到独立类型池，先 Reset 再入池；返回是否成功归还）。</summary>
            public readonly Func<ConfiguredEnemyBase, bool> Release;

            /// <summary>构造单类型注册。</summary>
            internal EnemyTypeRegistration(
                EnemyDefinitionSnapshot definition,
                Func<ConfiguredEnemyBase> acquire,
                Func<ConfiguredEnemyBase, bool> release)
            {
                Definition = definition;
                Acquire = acquire;
                Release = release;
            }
        }
    }
}
