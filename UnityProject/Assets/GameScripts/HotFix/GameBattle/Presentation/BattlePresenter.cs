using System;
using System.Collections.Generic;
using GameCommon.Battle;
using TEngine;
using UnityEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.4：BattlePresenter —— 表现层组装器
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Presentation/BattlePresenter.cs）：
    //   把只读状态/事实翻译成视图操作，不回写规则状态。
    //
    // 关键不变量（design.md:217 "BattlePresenter 把只读状态/事实翻译成视图操作，
    //   不回写规则状态" / spec "Settling has no gameplay damage authority"）：
    //   1. Presenter 只读 BattleReadModel，不直接访问 BattleState 或 Manager。
    //   2. 表现回调（经 IBattleViewPort/IBattleAudioPort/IBattleVfxPort 收到的事实）
    //      只翻译成视图操作（创建/移除表现对象、播放音频/特效），不回写规则状态。
    //   3. Settling 中只允许表现收尾（design.md:219 / spec "Settling has no gameplay
    //      damage authority"），Presenter 不触发新的伤害或状态变更。
    //
    // 组合关系（design.md 第 1 节目录表）：
    //   Presenter 持有三个协作者：
    //   - BattleViewRegistry（task 7.4）：维护 runtimeId → 表现对象映射。
    //   - BattleViewSynchronizer（task 7.4）：Unity 帧中插值/同步表现，不推进战斗逻辑。
    //   - BattleInputAdapter（task 7.4）：把 UI 点击/拖放转换成强类型战斗命令。
    //   三个协作者由 Presenter 在构造时创建并持有，随 Presenter Dispose 释放。
    //
    // 与逻辑层的关系（design.md 第 4 节 / spec battle-event-boundary）：
    //   BattleEventBridge 负责把 SignalHub 事实转发给 FairyGUI UI；Presenter 额外订阅
    //   HealthChanged，将同一低频事实转发给世界视图端口。二者均为只读表现消费者，
    //   不回写规则状态；订阅随 Presenter Dispose 解除。
    //   高频逐实体位置同步由 BattleViewSynchronizer 在 Unity 帧中直接读取只读位置查询
    //   完成，不通过事件（design.md 第 4 节 / task 7.9 性能 profile）。
    //
    // 异步生命周期（task 7.3 端口语义）：
    //   三个端口的异步操作在 Unity PlayerLoop 上等待。Presenter 在 Dispose 时调用三个端口
    //   的 Clear，使迟到表现不再提交。Presenter 自身经 Scope.TrackDisposable 登记释放。
    //
    // 框架解耦（design.md:9）：
    //   Presenter 不直接引用 UnityEngine 或 FairyGUI 类型。三个端口为抽象接口，
    //   真实实现由 task 7.5/7.6 接入。当前默认注入 Null 实现，使纯逻辑闭环不依赖
    //   Unity 表现层。
    // ============================================================================

    /// <summary>
    /// 表现层组装器：把只读状态/事实翻译成视图操作，不回写规则状态。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:217）：</b>把只读状态/事实翻译成视图操作，不回写规则状态。</para>
    ///
    /// <para><b>只读 BattleReadModel：</b>Presenter 只持 ReadModel 引用，不直接访问
    /// BattleState 或 Manager。ReadModel 提供标量只读属性与不可变快照方法，
    /// Presenter 无法通过它修改规则状态（task 3.7 不变量）。</para>
    ///
    /// <para><b>不回写规则状态（design.md:217 / spec "Settling has no gameplay damage authority"）：</b>
    /// 表现回调（经 IBattleViewPort/IBattleAudioPort/IBattleVfxPort 收到的事实）只翻译成
    /// 视图操作，不回写 BattleState 或 Manager 状态。Settling 中只允许表现收尾。</para>
    ///
    /// <para><b>组合协作者：</b>
    /// <list type="bullet">
    /// <item><see cref="Registry"/>：维护 runtimeId → 表现对象映射。</item>
    /// <item><see cref="Synchronizer"/>：Unity 帧中插值/同步表现，不推进战斗逻辑。</item>
    /// <item><see cref="InputAdapter"/>：把 UI 点击/拖放转换成强类型战斗命令。</item>
    /// </list>
    /// 三个协作者随 Presenter Dispose 释放。</para>
    ///
    /// <para><b>生命周期：</b>随 BattleRuntimeFactory 构造，经 Assembly 注入 BattleRuntime。
    /// 经 <see cref="BattleRuntimeScope.TrackDisposable"/> 登记到 Scope，在失败回滚/Settling/
    /// Dispose 时调用 <see cref="Dispose"/> 清理表现对象与监听。</para>
    /// </remarks>
    internal sealed class BattlePresenter : IDisposable
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[BattlePresenter]";

        // ====================================================================
        // 只读依赖
        // ====================================================================

        /// <summary>
        /// 本局只读状态视图。Presenter 只读本属性访问 BattleState 快照，不直接访问 BattleState。
        /// </summary>
        /// <remarks>
        /// <para><b>只读不变量（task 3.7 / task 7.4 验证要求）：</b>
        /// Presenter 只调 ReadModel 的只读属性与 <see cref="BattleReadModel.Snapshot"/>
        /// 快照方法，不调任何 setter/Apply。ReadModel 不暴露内部集合或实体引用，
        /// Presenter 无法通过它修改规则状态。</para>
        /// </remarks>
        private readonly BattleReadModel _readModel;

        /// <summary>
        /// 视图表现端口，接收逻辑层实体生成/移除/状态变化等低频事实。
        /// <para>Presenter 实现本端口（或委托给 ViewRegistry/Synchronizer），
        /// 把逻辑事实翻译成视图操作。</para>
        /// </remarks>
        private readonly IBattleViewPort _viewPort;

        /// <summary>
        /// 音频表现端口，接收逻辑层 BGM/SFX 播放停止意图。
        /// </summary>
        private readonly IBattleAudioPort _audioPort;

        /// <summary>
        /// 特效表现端口，接收逻辑层特效播放意图。
        /// </summary>
        private readonly IBattleVfxPort _vfxPort;

        /// <summary>逻辑实体生命周期事实源；仅用于订阅/退订，不用于回写规则状态。</summary>
        private readonly EnemyManager _enemyManager;
        private readonly UnitRegistry _unitRegistry;
        private readonly ProjectileManager _projectileManager;
        private IUnsubscribeHandle _healthChangedHandle;
        private IUnsubscribeHandle _slotChangedHandle;
        private IUnsubscribeHandle _unitMergedHandle;

        // ====================================================================
        // 协作者（Presenter 在构造时创建并持有）
        // ====================================================================

        /// <summary>
        /// 表现对象注册表：维护 runtimeId → 表现对象映射。
        /// <para>由 Presenter 在构造时创建，供 ViewPort 实现登记/注销表现对象，
        /// 供 Synchronizer 按 runtimeId 查找表现对象做同步。</para>
        /// </summary>
        internal BattleViewRegistry Registry { get; }

        /// <summary>
        /// 表现同步器：Unity 帧中插值/同步表现，不推进战斗逻辑。
        /// <para>由 Presenter 在构造时创建，每 Unity 帧由 BattleModule/外部驱动调用
        /// <see cref="BattleViewSynchronizer.Sync"/>，读取 ReadModel 同步表现对象视觉。</para>
        /// </summary>
        internal BattleViewSynchronizer Synchronizer { get; }

        /// <summary>
        /// 输入适配器：把 UI 点击/拖放转换成强类型战斗命令。
        /// <para>由 Presenter 在构造时创建，供 UI 层调用 HandleBuyPlaceClick/HandleRefreshClick
        /// 提交命令。Adapter 经 <see cref="BattleInputController"/> 执行原子事务。</para>
        /// </summary>
        internal BattleInputAdapter InputAdapter { get; }

        // ====================================================================
        // 生命周期状态
        // ====================================================================

        /// <summary>
        /// 是否已 Dispose（Presenter 已销毁）。
        /// <para>Dispose 后所有视图操作为空操作，重复 Dispose 幂等。
        /// BattleRuntime 经 Scope 在 Settling/Dispose 时调用本方法。</para>
        /// </summary>
        public bool IsDisposed { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造表现层组装器，创建 ViewRegistry/Synchronizer/InputAdapter 三个协作者。
        /// </summary>
        /// <param name="readModel">本局只读状态视图（非 null）。Presenter 只读访问。</param>
        /// <param name="viewPort">视图表现端口（非 null）。当前为 Null 实现或真实实现。</param>
        /// <param name="audioPort">音频表现端口（非 null）。</param>
        /// <param name="vfxPort">特效表现端口（非 null）。</param>
        /// <param name="inputController">输入命令执行控制器（非 null），供 InputAdapter 提交命令。</param>
        /// <param name="enemyManager">敌人管理器（task 7.6 接入真实位置查询）。可为 null（降级为 Null）。</param>
        /// <param name="unitRegistry">单位注册表（task 7.6 接入真实位置查询）。可为 null（降级为 Null）。</param>
        /// <param name="projectileManager">投射物管理器（task 7.6 接入真实位置查询）。可为 null（降级为 Null）。</param>
        /// <param name="signalHub">本局低频事实信号；用于把生命变化转发给世界视图。可为 null。</param>
        /// <exception cref="ArgumentNullException">必填参数为 null。</exception>
        /// <remarks>
        /// <para><b>只读 ReadModel（task 7.4 验证要求）：</b>
        /// Presenter 持 ReadModel 引用只做只读访问，不调任何 setter/Apply。
        /// ReadModel 由 Factory 在步骤 7 构造并与 ResultBuilder 共享同一实例。</para>
        /// <para>本构造函数不执行 IO 或资源加载（端口 PreloadAsync 由
        /// BattleRuntimeFactory/BattleModule 在 Entering 阶段调用）。</para>
        /// <para><b>task 7.6 真实位置查询：</b>当 enemyManager/unitRegistry/projectileManager
        /// 非 null 时，构造 ReadModelProvider 查询真实逻辑位置；为 null 时降级为 NullProvider
        /// （纯逻辑测试场景）。NullViewObjectSync 在 ViewPort 为 UnityBattleViewPort 时
        /// 替换为 UnityViewObjectSync（操作 Transform），仍为 Null 时保持空操作。</para>
        /// </remarks>
        internal BattlePresenter(
            BattleReadModel readModel,
            IBattleViewPort viewPort,
            IBattleAudioPort audioPort,
            IBattleVfxPort vfxPort,
            BattleInputController inputController,
            EnemyManager enemyManager = null,
            UnitRegistry unitRegistry = null,
            ProjectileManager projectileManager = null,
            BattleMapBindings bindings = null,
            BattleInternalSignalHub signalHub = null)
        {
            _readModel = readModel ?? throw new ArgumentNullException(nameof(readModel));
            _viewPort = viewPort ?? throw new ArgumentNullException(nameof(viewPort));
            _audioPort = audioPort ?? throw new ArgumentNullException(nameof(audioPort));
            _vfxPort = vfxPort ?? throw new ArgumentNullException(nameof(vfxPort));
            if (inputController == null)
            {
                throw new ArgumentNullException(nameof(inputController));
            }

            _enemyManager = enemyManager;
            _unitRegistry = unitRegistry;
            _projectileManager = projectileManager;

            // 创建三个协作者。
            Registry = new BattleViewRegistry();
            if (viewPort is UnityBattleViewPort unityViewPort)
            {
                unityViewPort.ConfigureRegistry(Registry);
            }

            // task 7.6：使用真实 ReadModelProvider 查询 Manager 只读位置。
            // 当 Manager 非 null 时构造真实 Provider；为 null（纯逻辑测试）时降级为 Null。
            IViewReadModelProvider readModelProvider;
            if (enemyManager != null)
            {
                readModelProvider = new RuntimeReadModelProvider(
                    enemyManager, unitRegistry, projectileManager, bindings);
            }
            else
            {
                readModelProvider = new NullViewReadModelProvider();
            }

            // task 7.6：当 ViewPort 为 UnityBattleViewPort 时使用 UnityViewObjectSync
            // 操作 Transform；否则保持 NullViewObjectSync（纯逻辑测试场景）。
            IViewObjectSync objectSync;
            if (viewPort is UnityBattleViewPort && bindings != null)
            {
                objectSync = new UnityViewObjectSync(bindings);
            }
            else
            {
                objectSync = new NullViewObjectSync();
            }

            Synchronizer = new BattleViewSynchronizer(Registry, readModelProvider, objectSync);

            ICoordinateConverter coordinateConverter = bindings == null
                ? new NullCoordinateConverter()
                : new UnityCoordinateConverter(bindings);
            InputAdapter = new BattleInputAdapter(inputController, coordinateConverter);

            SubscribeLifecycleFacts();
            if (signalHub != null)
            {
                _healthChangedHandle = signalHub.HealthChanged.Subscribe(OnHealthChanged);
                _slotChangedHandle = signalHub.SlotChanged.Subscribe(OnSlotChanged);
                _unitMergedHandle = signalHub.UnitMerged.Subscribe(OnUnitMerged);
            }

            Log.Info($"{LogTag} 构造完成，协作者 ViewRegistry/ViewSynchronizer/InputAdapter 已创建" +
                $"（ReadModelProvider={readModelProvider.GetType().Name}, ObjectSync={objectSync.GetType().Name}）");
        }

        // ====================================================================
        // 表现帧同步入口（由 BattleModule/外部每 Unity 帧调用）
        // ====================================================================

        /// <summary>
        /// 每 Unity 帧调用一次：驱动 Synchronizer 同步表现对象视觉。
        /// </summary>
        /// <param name="deltaSeconds">本 Unity 帧的渲染帧时间增量（秒）。</param>
        /// <remarks>
        /// <para><b>不推进战斗逻辑（design.md:219）：</b>
        /// 本方法只驱动 Synchronizer 读取 ReadModel 同步表现对象视觉，不调用
        /// BattleSimulation.Advance 或任何 Manager.Update。逻辑推进由 BattleRuntime.Advance
        /// 唯一驱动。</para>
        /// <para>由 BattleModule 在 Running 状态的 OnUpdate 中调用（与 BattleRuntime.Advance
        /// 并列，但二者独立：Advance 推进逻辑，SyncFrame 推进表现）。</para>
        /// </remarks>
        internal void SyncFrame(float deltaSeconds)
        {
            if (IsDisposed)
            {
                return;
            }

            // 委托给 Synchronizer 做实际同步。Synchronizer 只读 ReadModel/位置查询，
            // 不推进战斗逻辑（design.md:219）。
            Synchronizer.Sync(deltaSeconds);
        }

        /// <summary>
        /// 把战斗开始事实转交给视图端口，不修改规则状态。
        /// </summary>
        internal void NotifyBattleStarted(
            int maxRounds,
            int playerMaxHealth,
            int opponentMaxHealth)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                _viewPort.OnBattleStarted(maxRounds, playerMaxHealth, opponentMaxHealth);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 通知 ViewPort 战斗开始异常: {ex}");
            }
        }

        /// <summary>
        /// 把生命变化事实转交给视图端口，不修改规则状态。
        /// </summary>
        private void OnHealthChanged(HealthChangedFact fact)
        {
            if (IsDisposed)
            {
                return;
            }

            int maxHealth = fact.IsPlayerSide
                ? _readModel.PlayerMaxHealth
                : _readModel.OpponentMaxHealth;
            try
            {
                _viewPort.OnHealthChanged(
                    fact.IsPlayerSide,
                    fact.CurrentHealth,
                    maxHealth,
                    fact.Delta);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 通知 ViewPort 生命变化异常: {ex}");
            }
        }

        /// <summary>
        /// 槽位占用变化事实转发到视图端口（最终方案：只读表现同步，不回写规则状态）。
        /// </summary>
        private void OnSlotChanged(SlotChangedFact fact)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                // 只处理战场槽：未合成武将字部件以单格字形显示并可再次起拖；
                // 空槽/士兵/已合成武将传入空 partWord 以移除旧字形。战场单位
                // （士兵/武将）仍由 UnitRegistry 的 UnitMoved/UnitLevelChanged 同步。
                if (fact.SlotId.Zone != SlotZone.Battle)
                {
                    return;
                }

                string partWord = fact.Occupant.HasValue && fact.Occupant.Value.Kind == UnitKind.GeneralPart
                    ? fact.Occupant.Value.GeneralPartText
                    : null;
                _viewPort.OnBattleGeneralPartGlyphChanged(
                    fact.SlotId.Id,
                    fact.SlotId.Side,
                    fact.SlotId.GridPosition.X,
                    fact.SlotId.GridPosition.Y,
                    partWord);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 槽位变化表现异常: {ex}");
            }
        }

        /// <summary>
        /// 合并升级事实：目标在战场时播放合并特效（最终方案"目标在战场时播放合并升级特效"）。
        /// </summary>
        private void OnUnitMerged(UnitMergedFact fact)
        {
            if (IsDisposed)
            {
                return;
            }

            if (fact.TargetSlotId.Zone == SlotZone.Battle)
            {
                GridPosition grid = fact.TargetSlotId.GridPosition;
                try
                {
                    _vfxPort.PlaySpawnEffect("unit_merge", grid.X, grid.Y);
                }
                catch (Exception ex)
                {
                    Log.Error($"{LogTag} 合并升级特效异常: {ex}");
                }
            }
        }

        /// <summary>
        /// 把已冻结的战斗完成事实转交给视图端口，不修改规则状态。
        /// </summary>
        internal void NotifyBattleFinished(BattleResultDto result)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                _viewPort.OnBattleFinished(result.IsWin, result.Star);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 通知 ViewPort 战斗完成异常: {ex}");
            }
        }

        // ====================================================================
        // 只读状态查询（供 UI 层通过 Presenter 读取，不暴露 ReadModel 引用）
        // ====================================================================

        /// <summary>
        /// 生成当前状态的不可变快照，供 UI 安全持有。
        /// </summary>
        /// <returns>不可变状态快照结构。</returns>
        /// <remarks>
        /// <para>委托给 <see cref="BattleReadModel.Snapshot"/>。UI 层经 Presenter 获取快照，
        /// 不直接持有 ReadModel 引用，保证 UI 无法通过快照回写规则状态
        /// （BattleStateSnapshot 为 readonly struct，task 3.7 不变量）。</para>
        /// </remarks>
        internal BattleStateSnapshot GetStateSnapshot()
        {
            return _readModel.Snapshot();
        }

        /// <summary>接收 HUD 的征兵请求并提交真实 Recruit 命令（最终方案）。</summary>
        internal BattleInputResult HandleRecruitClick(bool playerSide)
        {
            return IsDisposed
                ? BattleInputResult.Fail(0, BattleInputRejectReason.Unknown, "战斗表现层已释放")
                : InputAdapter.HandleRecruitClick(playerSide);
        }

        /// <summary>接收 HUD 的换槽/合并拖放提交并提交真实 DropUnit 命令（最终方案）。</summary>
        internal BattleInputResult HandleDropUnit(int sourceSlotId, int targetSlotId)
        {
            return IsDisposed
                ? BattleInputResult.Fail(0, BattleInputRejectReason.Unknown, "战斗表现层已释放")
                : InputAdapter.HandleDropUnit(sourceSlotId, targetSlotId);
        }

        /// <summary>获取槽位只读快照，供 HUD 渲染待上场槽与战场槽（最终方案）。</summary>
        internal UnitSlotSnapshot GetSlotSnapshot()
        {
            return _readModel.SlotSnapshot();
        }

        /// <summary>
        /// 尝试把 UI 屏幕坐标解析为玩家方战场槽位标识（最终方案）。
        /// </summary>
        /// <param name="screenX">Stage X 坐标。</param>
        /// <param name="screenY">Stage Y 坐标。</param>
        /// <param name="targetSlotId">解析出的玩家战场槽位固定标识；未命中为 -1。</param>
        /// <returns>解析成功且命中战场槽返回 true。</returns>
        /// <remarks>
        /// <para>经 <see cref="BattleInputAdapter.TryConvertToGrid"/> 把 Stage 坐标转换为格子，
        /// 再经 <see cref="BattleReadModel.SlotSnapshot"/> 查找玩家侧对应战场槽。</para>
        /// </remarks>
        internal bool TryResolvePlayerBattleSlot(float screenX, float screenY, out int targetSlotId)
        {
            targetSlotId = -1;
            if (IsDisposed)
            {
                return false;
            }

            if (!InputAdapter.TryConvertToGrid(screenX, screenY, out GridPosition grid))
            {
                return false;
            }

            IReadOnlyList<UnitSlot> battleSlots = GetSlotSnapshot().GetSlots(isPlayerSide: true, SlotZone.Battle);
            for (int i = 0; i < battleSlots.Count; i++)
            {
                if (battleSlots[i].SlotId.GridPosition == grid)
                {
                    targetSlotId = battleSlots[i].SlotId.Id;
                    return true;
                }
            }

            return false;
        }

        private void SubscribeLifecycleFacts()
        {
            if (_enemyManager != null)
            {
                _enemyManager.EnemySpawned += OnEnemySpawned;
                _enemyManager.EnemyRemoved += OnEnemyRemoved;
                _enemyManager.EnemyHealthChanged += OnEnemyHealthChanged;
                _enemyManager.BossSkillIntentChanged += OnBossSkillIntentChanged;
            }

            if (_unitRegistry != null)
            {
                _unitRegistry.ConfiguredUnitPlaced += OnConfiguredUnitPlaced;
                _unitRegistry.UnitRemoved += OnUnitRemoved;
                _unitRegistry.UnitMoved += OnUnitMoved;
                _unitRegistry.UnitLevelChanged += OnUnitLevelChanged;
            }

            if (_projectileManager != null)
            {
                _projectileManager.ProjectileFired += OnProjectileFired;
                _projectileManager.ProjectileRemoved += OnProjectileRemoved;
            }
        }

        private void UnsubscribeLifecycleFacts()
        {
            if (_enemyManager != null)
            {
                _enemyManager.EnemySpawned -= OnEnemySpawned;
                _enemyManager.EnemyRemoved -= OnEnemyRemoved;
                _enemyManager.EnemyHealthChanged -= OnEnemyHealthChanged;
                _enemyManager.BossSkillIntentChanged -= OnBossSkillIntentChanged;
            }

            if (_unitRegistry != null)
            {
                _unitRegistry.ConfiguredUnitPlaced -= OnConfiguredUnitPlaced;
                _unitRegistry.UnitRemoved -= OnUnitRemoved;
                _unitRegistry.UnitMoved -= OnUnitMoved;
                _unitRegistry.UnitLevelChanged -= OnUnitLevelChanged;
            }

            if (_projectileManager != null)
            {
                _projectileManager.ProjectileFired -= OnProjectileFired;
                _projectileManager.ProjectileRemoved -= OnProjectileRemoved;
            }
        }

        private void OnEnemySpawned(EnemySpawnViewData dto)
        {
            try { _viewPort.OnEnemySpawned(dto); }
            catch (Exception ex) { Log.Error($"{LogTag} 创建敌人表现异常: {ex}"); }
        }

        private void OnEnemyRemoved(int runtimeId, bool playDeathEffect)
        {
            try { _viewPort.OnEnemyRemoved(runtimeId, playDeathEffect); }
            catch (Exception ex) { Log.Error($"{LogTag} 移除敌人表现异常: {ex}"); }
        }

        private void OnBossSkillIntentChanged(int runtimeId, string animationKey, bool active)
        {
            try { _viewPort.OnBossSkillIntent(runtimeId, animationKey, active); }
            catch (Exception ex) { Log.Error($"{LogTag} Boss 技能表现异常: {ex}"); }
        }

        private void OnEnemyHealthChanged(int runtimeId, int currentHealth, int maxHealth, int delta)
        {
            try
            {
                // 经表现对象查找：若敌人在 Registry 中且有血条组件，则显示血条并按真实比例更新。
                object viewObject = Registry.Find(ViewObjectCategory.Enemy, runtimeId);
                if (viewObject is GameObject go && go != null)
                {
                    EnemyHealthBarView healthBar = go.GetComponent<EnemyHealthBarView>();
                    if (healthBar != null)
                    {
                        float ratio = maxHealth > 0
                            ? Mathf.Clamp01((float)currentHealth / maxHealth)
                            : (currentHealth > 0 ? 1f : 0f);
                        if (ratio <= 0f)
                        {
                            healthBar.ResetAndHide();
                        }
                        else
                        {
                            healthBar.ShowWithRatio(ratio);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 敌人血量变化表现异常: {ex}");
            }
        }

        private void OnUnitPlaced(int runtimeId, bool isPlayerSide, int soldierType, int gridX, int gridY, int level)
        {
            try { _viewPort.OnUnitPlaced(runtimeId, isPlayerSide, soldierType, gridX, gridY, level); }
            catch (Exception ex) { Log.Error($"{LogTag} 创建士兵表现异常: {ex}"); }
        }

        private void OnConfiguredUnitPlaced(UnitSpawnViewData dto)
        {
            try { _viewPort.OnConfiguredUnitPlaced(dto); }
            catch (Exception ex) { Log.Error($"{LogTag} 创建配置化单位表现异常: {ex}"); }
        }

        private void OnUnitRemoved(int runtimeId)
        {
            try { _viewPort.OnUnitRemoved(runtimeId); }
            catch (Exception ex) { Log.Error($"{LogTag} 移除士兵表现异常: {ex}"); }
        }

        private void OnUnitMoved(int runtimeId, int gridX, int gridY)
        {
            try { _viewPort.OnUnitMoved(runtimeId, gridX, gridY); }
            catch (Exception ex) { Log.Error($"{LogTag} 移动士兵表现异常: {ex}"); }
        }

        private void OnUnitLevelChanged(int runtimeId, int newLevel)
        {
            try { _viewPort.OnUnitLevelChanged(runtimeId, newLevel); }
            catch (Exception ex) { Log.Error($"{LogTag} 士兵等级变化表现异常: {ex}"); }
        }

        private void OnProjectileFired(int runtimeId, float fromX, float fromY)
        {
            try { _viewPort.OnProjectileFired(runtimeId, fromX, fromY, isPlayerSide: true); }
            catch (Exception ex) { Log.Error($"{LogTag} 创建投射物表现异常: {ex}"); }
        }

        private void OnProjectileRemoved(int runtimeId)
        {
            try { _viewPort.OnProjectileRemoved(runtimeId); }
            catch (Exception ex) { Log.Error($"{LogTag} 移除投射物表现异常: {ex}"); }
        }

        // ====================================================================
        // Dispose —— 清理表现对象与监听
        // ====================================================================

        /// <summary>
        /// 销毁本表现层组装器，清理表现对象与监听。
        /// </summary>
        /// <remarks>
        /// <para><b>销毁时机（spec "Event subscriptions follow runtime lifetime" /
        /// spec "Exit releases battle-owned state"）：</b>
        /// 由 <see cref="BattleRuntimeScope"/> 经 TrackDisposable 登记，在 Settling 静默清理、
        /// 失败回滚或 Dispose 时调用。</para>
        ///
        /// <para><b>清理顺序：</b></para>
        /// <list type="number">
        /// <item>停止 Synchronizer（Stop 置位，后续 Sync 为空操作）。</item>
        /// <item>调用三个端口 Clear（清理表现对象/音频/特效，端口 Clear 幂等）。</item>
        /// <item>清空 ViewRegistry 映射（不销毁表现对象本身，销毁由端口 Clear 负责）。</item>
        /// </list>
        ///
        /// <para><b>幂等：</b>重复 Dispose 安全。</para>
        /// <para><b>不回写规则状态：</b>清理过程只释放表现资源，不触发新的伤害或状态变更
        /// （spec "Settling has no gameplay damage authority"）。</para>
        /// </remarks>
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            _healthChangedHandle?.Unsubscribe();
            _healthChangedHandle = null;
            _slotChangedHandle?.Unsubscribe();
            _slotChangedHandle = null;
            _unitMergedHandle?.Unsubscribe();
            _unitMergedHandle = null;

            // 生命周期事实来自局部 Manager，必须先解除订阅，避免清理过程再回调表现端口。
            UnsubscribeLifecycleFacts();

            // 步骤 1：停止 Synchronizer。
            try
            {
                Synchronizer?.Stop();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Dispose 停止 Synchronizer 异常: {ex}");
            }

            // 步骤 2：调用三个端口 Clear（清理表现对象/音频/特效）。
            // 端口 Clear 幂等，含 try-catch 防御，不阻断后续清理。
            try
            {
                _viewPort?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Dispose 清理 ViewPort 异常: {ex}");
            }

            try
            {
                _audioPort?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Dispose 清理 AudioPort 异常: {ex}");
            }

            try
            {
                _vfxPort?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Dispose 清理 VfxPort 异常: {ex}");
            }

            // 步骤 3：清空 ViewRegistry 映射。
            // 不销毁表现对象本身——表现对象的销毁/归还池由端口 Clear 负责。
            // 此处只清空 ID→引用映射，使后续 Sync 查询返回 null（防御性）。
            try
            {
                Registry?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Dispose 清空 ViewRegistry 异常: {ex}");
            }

            Log.Info($"{LogTag} Dispose 完成，表现对象与监听已清理");
        }

        // ====================================================================
        // task 7.6：真实 ReadModelProvider 与 UnityViewObjectSync 实现
        // --------------------------------------------------------------------
        // 替换 NullViewReadModelProvider/NullViewObjectSync，使 Synchronizer 能
        // 查询 Manager 只读位置并操作 Unity Transform。NullCoordinateConverter 保留
        // （坐标转换需 UI 层屏幕坐标→格子映射，属 task 7.5 FairyGUI Change 范围）。
        // ====================================================================

        /// <summary>
        /// 基于 Manager 只读查询的真实 <see cref="IViewReadModelProvider"/> 实现（task 7.6）。
        /// <para>查询 EnemyManager/UnitRegistry/ProjectileManager 的活动实体位置与血量，
        /// 供 BattleViewSynchronizer 每帧同步表现对象视觉。只读查询，不回写规则状态。</para>
        /// </summary>
        private sealed class RuntimeReadModelProvider : IViewReadModelProvider
        {
            private readonly EnemyManager _enemyManager;
            private readonly UnitRegistry _unitRegistry;
            private readonly ProjectileManager _projectileManager;
            private readonly BattleMapBindings _bindings;

            internal RuntimeReadModelProvider(
                EnemyManager enemyManager,
                UnitRegistry unitRegistry,
                ProjectileManager projectileManager,
                BattleMapBindings bindings)
            {
                _enemyManager = enemyManager;
                _unitRegistry = unitRegistry;
                _projectileManager = projectileManager;
                _bindings = bindings;
            }

            public bool TryGetEnemyPosition(int runtimeId, out float x, out float y)
            {
                x = 0f;
                y = 0f;
                if (_enemyManager == null)
                {
                    return false;
                }

                IEnemyEntity enemy = _enemyManager.GetById(runtimeId);
                if (enemy == null)
                {
                    return false;
                }

                x = enemy.X;
                y = enemy.Y;
                return true;
            }

            public bool TryGetUnitPosition(int runtimeId, out float x, out float y)
            {
                x = 0f;
                y = 0f;
                if (_unitRegistry == null)
                {
                    return false;
                }

                SoldierBase unit = _unitRegistry.GetUnit(runtimeId);
                if (unit == null)
                {
                    return false;
                }

                if (_bindings != null)
                {
                    Vector2 logicPosition = _bindings.UnitCellToLogic(unit.GridX, unit.GridY);
                    x = logicPosition.x;
                    y = logicPosition.y;
                }
                else
                {
                    x = unit.CenterX;
                    y = unit.CenterY;
                }
                return true;
            }

            public bool TryGetUnitAttackTime(int runtimeId, out long attackTimeMs)
            {
                attackTimeMs = 0L;
                if (_unitRegistry == null)
                {
                    return false;
                }

                SoldierBase unit = _unitRegistry.GetUnit(runtimeId);
                if (unit == null)
                {
                    return false;
                }

                attackTimeMs = unit.LastAttackTimeMs;
                return true;
            }

            public bool TryGetUnitBodyRotation(int runtimeId, out float angleDegrees)
            {
                angleDegrees = 0f;
                if (_unitRegistry == null)
                {
                    return false;
                }

                SoldierBase unit = _unitRegistry.GetUnit(runtimeId);
                if (unit == null || !unit.HasBodyRotation)
                {
                    return false;
                }

                angleDegrees = unit.BodyRotationDegrees;
                return true;
            }

            public bool TryGetUnitWeaponAim(int runtimeId, out float angleDegrees)
            {
                angleDegrees = 0f;
                if (_unitRegistry == null)
                {
                    return false;
                }

                SoldierBase unit = _unitRegistry.GetUnit(runtimeId);
                if (unit == null || !unit.HasWeaponAim)
                {
                    return false;
                }

                angleDegrees = unit.WeaponAimDegrees;
                return true;
            }

            public bool TryGetUnitAttackIntervalSeconds(int runtimeId, out float intervalSeconds)
            {
                intervalSeconds = 1f;
                if (_unitRegistry == null)
                {
                    return false;
                }

                SoldierBase unit = _unitRegistry.GetUnit(runtimeId);
                if (unit == null)
                {
                    return false;
                }

                intervalSeconds = unit.AttackIntervalSeconds;
                return true;
            }

            public bool TryGetUnitState(int runtimeId, out AttackUnitState state)
            {
                state = AttackUnitState.Idle;
                if (_unitRegistry == null)
                {
                    return false;
                }

                SoldierBase unit = _unitRegistry.GetUnit(runtimeId);
                if (unit == null)
                {
                    return false;
                }

                state = unit.CurrentState;
                return true;
            }

            public bool TryGetProjectileState(int runtimeId, out float x, out float y, out float rotation)
            {
                x = 0f;
                y = 0f;
                rotation = 0f;
                if (_projectileManager == null)
                {
                    return false;
                }

                // ProjectileManager 不提供按 ID 查找，遍历快照查找匹配。
                // 高频路径优化留给后续 task；本期最小实现用遍历。
                IReadOnlyList<ProjectileBase> projectiles = _projectileManager.GetProjectilesSnapshot();
                for (int i = 0; i < projectiles.Count; ++i)
                {
                    ProjectileBase p = projectiles[i];
                    if (p != null && p.ProjectileId == runtimeId)
                    {
                        x = p.X;
                        y = p.Y;
                        rotation = p.Rotation;
                        return true;
                    }
                }

                return false;
            }

            public bool TryGetEnemyHealthRatio(int runtimeId, out float ratio)
            {
                ratio = 0f;
                if (_enemyManager == null)
                {
                    return false;
                }

                IEnemyEntity enemy = _enemyManager.GetById(runtimeId);
                if (enemy == null)
                {
                    return false;
                }

                // 真实血量比例 = 当前血量 / 最大血量（IEnemyEntity 已暴露 MaxHealth）。
                int maxHealth = enemy.MaxHealth;
                ratio = maxHealth > 0
                    ? Mathf.Clamp01((float)enemy.Health / maxHealth)
                    : (enemy.Health > 0 ? 1f : 0f);
                return true;
            }
        }

        /// <summary>
        /// 基于 Unity Transform 的真实 <see cref="IViewObjectSync"/> 实现（task 7.6）。
        /// <para>把表现对象（GameObject）的 Transform 位置设置为逻辑坐标映射的世界坐标，
        /// 把血量比例设置到 HealthBar 子组件（若存在）。不回写规则状态。</para>
        /// <para>弓兵本体旋转：攻击时经 <see cref="SetBodyRotation"/> 立即转向目标；
        /// Attack→Idle 时经 <see cref="ResetUnitPose"/> 启动平滑回正，由
        /// <see cref="SoldierBodyRotationReturn"/> 以恒定角速度逐帧 RotateTowards
        /// 回到初始朝向，不再一帧 snap。新目标出现时 SetBodyRotation 立即取消回正并转向。</para>
        /// </summary>
        private sealed class UnityViewObjectSync : IViewObjectSync
        {
            /// <summary>回正角速度（度/秒）：Idle 时弓兵本体从攻击角平滑回到初始朝向。</summary>
            private const float BodyRotationReturnDegreesPerSecond = 60f;

            private readonly BattleMapBindings _bindings;
            private readonly Dictionary<GameObject, long> _attackTimes =
                new Dictionary<GameObject, long>();
            private readonly Dictionary<GameObject, Quaternion> _initialRotations =
                new Dictionary<GameObject, Quaternion>();

            internal UnityViewObjectSync(BattleMapBindings bindings)
            {
                _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            }

            /// <summary>
            /// 缓存表现对象的初始 localRotation，供 <see cref="ResetUnitPose"/> 复位。
            /// </summary>
            private Quaternion GetInitialRotation(GameObject go)
            {
                if (!_initialRotations.TryGetValue(go, out Quaternion initialRotation))
                {
                    initialRotation = go.transform.localRotation;
                    _initialRotations.Add(go, initialRotation);
                }

                return initialRotation;
            }

            public void SetPosition(object viewObject, float logicX, float logicY)
            {
                if (viewObject is GameObject go && go != null)
                {
                    go.transform.position = _bindings.LogicToWorld(logicX, logicY);
                }
            }

            public void SetHealthRatio(object viewObject, float ratio)
            {
                if (viewObject is GameObject go && go != null)
                {
                    // 每帧只更新填充宽度，不显示血条、不刷新隐藏计时。
                    // 显示与隐藏计时由低频受击事实驱动（EnemyHealthBarView.ShowWithRatio）。
                    go.GetComponent<EnemyHealthBarView>()?.SetRatio(ratio);
                }
            }

            public void SetBodyRotation(object viewObject, float angleDegrees)
            {
                if (viewObject is GameObject go && go != null)
                {
                    Quaternion initialRotation = GetInitialRotation(go);
                    Quaternion target = initialRotation * _bindings.LogicAngleToWorld(angleDegrees);
                    SoldierBodyRotationReturn returner = go.GetComponent<SoldierBodyRotationReturn>();
                    if (returner != null)
                    {
                        // 立即取消进行中的回正并转向新目标：新目标必须立即可见，不能被旧回正覆盖。
                        returner.SetImmediate(target);
                    }
                    else
                    {
                        go.transform.localRotation = target;
                    }
                }
            }

            public void SetWeaponAim(object viewObject, float angleDegrees)
            {
                if (viewObject is GameObject go && go != null)
                {
                    // 逻辑坐标 Y 向下、Unity 世界坐标 Y 向上，Z 轴旋转角必须取反
                    // （与箭矢共用 LogicAngleToWorld 的 -logicDegrees 一致）。
                    // 逻辑角 -61° → Unity 角 +61°。
                    go.GetComponent<SpearWeaponView>()?.SetAim(-angleDegrees);
                }
            }

            public void SetProjectileRotation(object viewObject, float angleDegrees)
            {
                if (!(viewObject is GameObject go) || go == null)
                {
                    return;
                }

                Transform orientationRoot = go.transform.Find("OrientationRoot");
                if (orientationRoot != null)
                {
                    // DisplayAngle 逻辑角（0°朝上/90°朝右）→ Unity Z 旋转（Sprite 默认朝上）。
                    orientationRoot.localRotation = _bindings.LogicAngleToWorld(angleDegrees);
                }
            }

            public void SetAttackIntervalSeconds(object viewObject, float intervalSeconds)
            {
                if (viewObject is GameObject go && go != null)
                {
                    go.GetComponent<SoldierSpriteAnimator>()?.SetAttackIntervalSeconds(intervalSeconds);
                }
            }

            public void SetUnitAttackTime(object viewObject, long attackTimeMs)
            {
                if (!(viewObject is GameObject go) || go == null)
                {
                    return;
                }

                bool hasPreviousTime = _attackTimes.TryGetValue(go, out long previousTime);
                if (!hasPreviousTime)
                {
                    _attackTimes.Add(go, attackTimeMs);
                }
                else
                {
                    _attackTimes[go] = attackTimeMs;
                }

                if (!ShouldPlayUnitAttackAnimation(
                        hasPreviousTime,
                        previousTime, attackTimeMs))
                {
                    return;
                }

                go.GetComponent<SoldierSpriteAnimator>()?.PlayAttack();
                go.GetComponent<GeneralSpineAnimator>()?.PlayAttack();
                go.GetComponent<SpearWeaponView>()?.PlayAttack();
            }

            public void ResetUnitPose(object viewObject)
            {
                if (!(viewObject is GameObject go) || go == null)
                {
                    return;
                }

                // design 决策 5：普通 Attack→Idle 只恢复根节点朝向到默认（弓兵攻击会按目标方向
                // 旋转角色本体）。不调用 SoldierSpriteAnimator.ResetToIdle() 或
                // SpearWeaponView.ResetView()——时间驱动的攻击动画与武器表现由各自 Update
                // 自然完成，普通 Idle 不等于正在播放的表现必须被取消。
                // 回池/Settling 的强制完整复位由 UnityBattleViewPort 的 Acquire/ReturnToPool 承担。
                if (_initialRotations.TryGetValue(go, out Quaternion initialRotation))
                {
                    SoldierBodyRotationReturn returner = go.GetComponent<SoldierBodyRotationReturn>();
                    if (returner == null)
                    {
                        returner = go.AddComponent<SoldierBodyRotationReturn>();
                    }

                    // 不在此处 snap 回初始旋转，改为启动平滑回正：由 SoldierBodyRotationReturn
                    // 以恒定角速度逐帧 RotateTowards 回初始朝向。
                    returner.BeginReturn(initialRotation, BodyRotationReturnDegreesPerSecond);
                }
            }
        }

        /// <summary>
        /// 弓兵本体旋转平滑回正组件（纯表现层，无独立资产）。
        /// <para>由 <see cref="UnityViewObjectSync.ResetUnitPose"/> 在 Attack→Idle 时
        /// <see cref="BeginReturn"/> 启动：不从当前角度 snap，而是由 Update 以恒定角速度
        /// <see cref="Quaternion.RotateTowards"/> 逐帧回到目标（初始）旋转，避免帧率依赖的
        /// 渐近式 Lerp。到达目标后停用，保证池化对象复用/新目标不会覆盖新朝向。</para>
        /// </summary>
        internal sealed class SoldierBodyRotationReturn : MonoBehaviour
        {
            private const float CompletionAngleDegrees = 0.01f;

            private Quaternion _targetRotation;
            private float _degreesPerSecond;
            private bool _isReturning;

            /// <summary>是否正在回正（测试断言用）。</summary>
            internal bool IsReturning => _isReturning;

            /// <summary>
            /// 开始平滑回正到 <paramref name="targetRotation"/>：不从当前角度 snap，
            /// 后续由 Update/测试驱动的 <see cref="Tick"/> 逐帧逼近。
            /// </summary>
            internal void BeginReturn(Quaternion targetRotation, float degreesPerSecond)
            {
                _targetRotation = targetRotation;
                _degreesPerSecond = degreesPerSecond > 0f ? degreesPerSecond : 0f;
                _isReturning = true;
            }

            /// <summary>
            /// 取消回正并立即把 localRotation 置为 <paramref name="targetRotation"/>。
            /// 总是取消恢复（即使未在回正），避免池化复用后旧回正覆盖新朝向。
            /// </summary>
            internal void SetImmediate(Quaternion targetRotation)
            {
                _isReturning = false;
                transform.localRotation = targetRotation;
            }

            /// <summary>
            /// 确定性推进一帧回正。供 Update 与测试注入固定 deltaSeconds，保证可测。
            /// </summary>
            internal void Tick(float deltaSeconds)
            {
                if (!_isReturning || deltaSeconds <= 0f)
                {
                    return;
                }

                transform.localRotation = Quaternion.RotateTowards(
                    transform.localRotation,
                    _targetRotation,
                    _degreesPerSecond * deltaSeconds);

                if (Quaternion.Angle(transform.localRotation, _targetRotation) <= CompletionAngleDegrees)
                {
                    // 到达目标后停用恢复：后续即使组件仍在对象上，也不会再覆盖新朝向。
                    transform.localRotation = _targetRotation;
                    _isReturning = false;
                }
            }

            private void Update()
            {
                Tick(Time.deltaTime);
            }

            private void OnDisable()
            {
                // 对象回池/停用时停止回正：即使对象复用，旧的恢复状态也不能残留到下一次
                // 战斗（新朝向由 SetBodyRotation/SetImmediate 重新建立）。
                _isReturning = false;
            }
        }

        /// <summary>判断攻击时间变化是否应触发一次攻击动画。</summary>
        internal static bool ShouldPlayUnitAttackAnimation(
            bool hasPreviousTime, long previousTime, long attackTimeMs)
        {
            return attackTimeMs > 0L && (!hasPreviousTime || attackTimeMs != previousTime);
        }

        // ====================================================================
        // Null 实现（task 7.4 框架就绪，真实实现由 task 7.5/7.6 接入）
        // --------------------------------------------------------------------
        // 三个 Null 实现使 Presenter/Synchronizer/InputAdapter 框架就绪，
        // 不依赖 Unity/FairyGUI 表现层。真实实现由 task 7.5/7.6 接入后替换。
        // ====================================================================

        /// <summary>
        /// Null 只读位置查询提供者，使 Synchronizer 框架就绪。
        /// <para>所有位置查询返回 false，Synchronizer.Sync 遍历为空操作。
        /// 真实实现由 task 7.5/7.6 接入后替换为查询 Manager 只读位置的真实 Provider。</para>
        /// </summary>
        private sealed class NullViewReadModelProvider : IViewReadModelProvider
        {
            public bool TryGetEnemyPosition(int runtimeId, out float x, out float y)
            {
                x = 0f;
                y = 0f;
                return false;
            }

            public bool TryGetUnitPosition(int runtimeId, out float x, out float y)
            {
                x = 0f;
                y = 0f;
                return false;
            }

            public bool TryGetUnitAttackTime(int runtimeId, out long attackTimeMs)
            {
                attackTimeMs = 0L;
                return false;
            }

            public bool TryGetUnitBodyRotation(int runtimeId, out float angleDegrees)
            {
                angleDegrees = 0f;
                return false;
            }

            public bool TryGetUnitWeaponAim(int runtimeId, out float angleDegrees)
            {
                angleDegrees = 0f;
                return false;
            }

            public bool TryGetUnitAttackIntervalSeconds(int runtimeId, out float intervalSeconds)
            {
                intervalSeconds = 1f;
                return false;
            }

            public bool TryGetUnitState(int runtimeId, out AttackUnitState state)
            {
                state = AttackUnitState.Idle;
                return false;
            }

            public bool TryGetProjectileState(int runtimeId, out float x, out float y, out float rotation)
            {
                x = 0f;
                y = 0f;
                rotation = 0f;
                return false;
            }

            public bool TryGetEnemyHealthRatio(int runtimeId, out float ratio)
            {
                ratio = 0f;
                return false;
            }
        }

        /// <summary>
        /// Null 表现对象同步委托，使 Synchronizer 框架就绪。
        /// <para>所有更新操作为空操作。真实实现由 task 7.5/7.6 接入后替换为
        /// Unity Transform / FairyGUI GComponent 的真实 Sync。</para>
        /// </summary>
        private sealed class NullViewObjectSync : IViewObjectSync
        {
            public void SetPosition(object viewObject, float logicX, float logicY)
            {
                // 空操作：Null 实现。
            }

            public void SetHealthRatio(object viewObject, float ratio)
            {
                // 空操作：Null 实现。
            }

            public void SetBodyRotation(object viewObject, float angleDegrees)
            {
                // 空操作：Null 实现。
            }

            public void SetWeaponAim(object viewObject, float angleDegrees)
            {
                // 空操作：Null 实现。
            }

            public void SetProjectileRotation(object viewObject, float angleDegrees)
            {
                // 空操作：Null 实现。
            }

            public void SetAttackIntervalSeconds(object viewObject, float intervalSeconds)
            {
                // 空操作：Null 实现。
            }

            public void SetUnitAttackTime(object viewObject, long attackTimeMs)
            {
                // 空操作：Null 实现。
            }

            public void ResetUnitPose(object viewObject)
            {
                // 空操作：Null 实现。
            }
        }

        /// <summary>
        /// Null UI 坐标转换器，使 InputAdapter 框架就绪。
        /// <para>所有坐标转换返回 false，HandleBuyPlaceClick 返回 InvalidCell 失败结果。
        /// 真实实现由 task 7.5/7.6 接入后替换为 Unity/FairyGUI 真实坐标转换器。</para>
        /// </summary>
        private sealed class NullCoordinateConverter : ICoordinateConverter
        {
            public bool TryConvertToGrid(float screenX, float screenY, out GridPosition position)
            {
                position = default;
                return false;
            }
        }

        /// <summary>
        /// FairyGUI Stage 的屏幕坐标到 BattleMap0 格子的适配器。
        /// FairyGUI 使用左上角为原点，Unity 主相机使用左下角为原点，转换只在边界完成一次。
        /// </summary>
        private sealed class UnityCoordinateConverter : ICoordinateConverter
        {
            private readonly BattleMapBindings _bindings;

            internal UnityCoordinateConverter(BattleMapBindings bindings)
            {
                _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            }

            public bool TryConvertToGrid(float screenX, float screenY, out GridPosition position)
            {
                Camera camera = Camera.main;
                float unityScreenY = Screen.height - screenY;
                bool converted = _bindings.TryScreenToCell(
                    camera, screenX, unityScreenY, out position);
                Log.Info(
                    $"[BattleDiagnostic] 放置坐标转换 " +
                    $"stage=({screenX:F1},{screenY:F1}) " +
                    $"unity=({screenX:F1},{unityScreenY:F1}) " +
                    $"screen=({Screen.width},{Screen.height}) " +
                    $"converted={converted} grid={position}");
                return converted;
            }
        }
    }
}
