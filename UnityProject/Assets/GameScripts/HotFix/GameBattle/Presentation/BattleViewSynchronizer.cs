using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.4：BattleViewSynchronizer —— Unity 帧中表现同步器
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Presentation/BattleViewSynchronizer.cs）：
    //   在 Unity 帧中插值/同步表现；不得推进战斗逻辑。
    //
    // 关键不变量（design.md:219 "在 Unity 帧中插值/同步表现；不得推进战斗逻辑"）：
    //   1. 本类型只读取 BattleReadModel 的当前值或 BattleStateSnapshot 快照，
    //      据此对表现对象做位置/血条/动画同步。
    //   2. 本类型绝不调用 BattleSimulation.Advance、BattleRuntime.Advance 或任何
    //      Manager 的 Update/Apply 方法——那是逻辑时钟的职责。
    //   3. 表现插值（如敌人位置在两个逻辑子步间平滑）只影响表现对象的 Transform，
    //      不回写逻辑实体的 X/Y（design.md:217 "不回写规则状态"）。
    //
    // 与逻辑时钟的关系（design.md 第 2 节）：
    //   逻辑时钟由 BattleSimulation 唯一推进：BattleModule.OnUpdate → BattleRuntime.Advance
    //     → BattleSimulation.Advance → phase handlers → Manager.Update。
    //   表现同步由本类型在 Unity 帧中独立执行，与逻辑子步解耦：
    //     - 逻辑可能一帧推进 0 个或多个子步（80ms 拆步）。
    //     - 表现同步每 Unity 帧执行一次，读取 ReadModel 当前值做插值。
    //     - 视觉上的位置/血条变化滞后于逻辑最多 1 帧（可接受，design.md 第 7 节 Risk）。
    //
    // 高频逐实体推进不通过全局事件（design.md 第 4 节 / task 7.9 性能 profile）：
    //   IBattleViewPort 只承载低频事实（生成/移除/状态变化），每子步的位置/动画
    //   不经事件驱动。本类型每帧直接遍历 ViewRegistry 中登记的表现对象，按 runtimeId
    //   查询逻辑层只读位置（经 Presenter 提供的 IViewReadModelProvider 委托）做插值。
    //   这避免高频 GameEvent 分发与装箱/闭包分配（design.md 第 4 节风险）。
    //
    // 框架解耦（design.md:9）：
    //   本类型不直接引用 UnityEngine.Transform 或 FairyGUI GComponent。
    //   表现对象的实际位置/血条更新经 IViewObjectSync 委托由真实实现完成：
    //   - Unity 实现：IViewObjectSync.SetPosition(object, float, float) 内部 cast 为
    //     Transform 并赋值 localPosition。
    //   - FairyGUI 实现：IViewObjectSync 内部 cast 为 GComponent 并赋值 xy。
    //   - Null/Test 实现：不使用本类型（NullBattleViewPort 不创建表现对象）。
    // ============================================================================

    /// <summary>
    /// 表现对象位置/状态同步委托，由真实表现层实现注入。
    /// </summary>
    /// <remarks>
    /// <para>本接口由 BattleViewSynchronizer 调用以更新表现对象的视觉位置与状态，
    /// 使 Synchronizer 不直接依赖 UnityEngine 或 FairyGUI 类型。</para>
    /// <para>真实实现由 task 7.5/7.6 接入（Unity Transform / FairyGUI GComponent）。</para>
    /// </remarks>
    internal interface IViewObjectSync
    {
        /// <summary>
        /// 设置表现对象的逻辑坐标（真实实现负责映射到 Unity 世界坐标或 FairyGUI 坐标）。
        /// </summary>
        /// <param name="viewObject">表现对象引用（由 BattleViewRegistry 登记）。</param>
        /// <param name="logicX">逻辑 X 坐标。</param>
        /// <param name="logicY">逻辑 Y 坐标。</param>
        void SetPosition(object viewObject, float logicX, float logicY);

        /// <summary>
        /// 设置表现对象的血条比例（0~1）。
        /// </summary>
        /// <param name="viewObject">表现对象引用。</param>
        /// <param name="ratio">血量比例（当前生命/最大生命）。</param>
        void SetHealthRatio(object viewObject, float ratio);

        /// <summary>
        /// 设置角色本体朝向（仅弓兵使用：攻击时整个角色左右翻转）。
        /// </summary>
        /// <param name="viewObject">表现对象引用。</param>
        /// <param name="facingRight">true=朝右；false=朝左。</param>
        void SetBodyFacing(object viewObject, bool facingRight);

        /// <summary>
        /// 设置武器瞄准角度（仅枪兵武器使用：枪独立绕挂点旋转，角色本体不转）。
        /// </summary>
        /// <param name="viewObject">表现对象引用。</param>
        /// <param name="angleDegrees">角度（度，DisplayAngle 语义：0°朝上/90°朝右）。</param>
        void SetWeaponAim(object viewObject, float angleDegrees);

        /// <summary>
        /// 设置投射物旋转角度（仅箭矢使用：沿飞行轨迹切线旋转）。
        /// </summary>
        /// <param name="viewObject">表现对象引用。</param>
        /// <param name="angleDegrees">角度（度，DisplayAngle 语义：0°朝上/90°朝右）。</param>
        void SetProjectileRotation(object viewObject, float angleDegrees);

        /// <summary>
        /// 设置单位当前有效攻击间隔（供 SoldierSpriteAnimator 动态调整攻击帧时长）。
        /// </summary>
        /// <param name="viewObject">表现对象引用。</param>
        /// <param name="intervalSeconds">有效攻击间隔（秒）。</param>
        void SetAttackIntervalSeconds(object viewObject, float intervalSeconds);

        /// <summary>
        /// 同步单位最近一次实际出手时间；时间变化时触发一次攻击动画。
        /// </summary>
        void SetUnitAttackTime(object viewObject, long attackTimeMs);

        /// <summary>
        /// 执行一次单位待机复位：动画回待机、武器复位、根节点朝向恢复默认。
        /// 只在单位逻辑状态从攻击切回待机的状态边沿调用一次，不能每帧调用，
        /// 否则待机动画会不断回到第 0 帧。
        /// </summary>
        /// <param name="viewObject">表现对象引用。</param>
        void ResetUnitToIdle(object viewObject);
    }

    /// <summary>
    /// 只读位置查询委托，由 BattlePresenter 提供供 Synchronizer 查询逻辑实体位置。
    /// </summary>
    /// <remarks>
    /// <para>BattlePresenter 持有 BattleReadModel，但 ReadModel 不暴露实体引用或位置
    /// （只暴露标量状态与快照）。逻辑实体的位置由各自 Manager 的只读查询提供，
    /// Presenter 把这些查询封装为 IViewReadModelProvider 注入 Synchronizer，
    /// 使 Synchronizer 不直接依赖 Manager 类型。</para>
    /// <para>本期最小实现：只提供按 runtimeId 查询敌人/单位/投射物位置的委托。
    /// 高频逐实体位置查询不通过 IBattleViewPort（低频端口），而通过本委托直接读取，
    /// 避免高频事件分发（design.md 第 4 节 / task 7.9）。</para>
    /// </remarks>
    internal interface IViewReadModelProvider
    {
        /// <summary>
        /// 尝试查询敌人当前逻辑坐标。
        /// </summary>
        /// <param name="runtimeId">敌人运行时 ID。</param>
        /// <param name="x">输出逻辑 X。</param>
        /// <param name="y">输出逻辑 Y。</param>
        /// <returns>找到返回 true；否则 false（敌人已移除或未登记）。</returns>
        bool TryGetEnemyPosition(int runtimeId, out float x, out float y);

        /// <summary>
        /// 尝试查询单位当前逻辑坐标。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="x">输出逻辑 X。</param>
        /// <param name="y">输出逻辑 Y。</param>
        /// <returns>找到返回 true；否则 false。</returns>
        bool TryGetUnitPosition(int runtimeId, out float x, out float y);

        /// <summary>
        /// 尝试查询单位最近一次实际出手时间。
        /// </summary>
        bool TryGetUnitAttackTime(int runtimeId, out long attackTimeMs);

        /// <summary>
        /// 尝试查询单位角色本体朝向（弓兵攻击转向用）。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="facingRight">输出是否朝右。</param>
        /// <returns>找到返回 true；否则 false。</returns>
        bool TryGetUnitFacing(int runtimeId, out bool facingRight);

        /// <summary>
        /// 尝试查询单位武器瞄准角度（枪兵武器朝向用）。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="angleDegrees">输出角度（度，DisplayAngle 语义）。</param>
        /// <returns>找到且有瞄准目标返回 true；否则 false（无目标保持默认）。</returns>
        bool TryGetUnitWeaponAim(int runtimeId, out float angleDegrees);

        /// <summary>
        /// 尝试查询单位当前有效攻击间隔（秒，供攻击动画计算逐帧时长）。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="intervalSeconds">输出有效攻击间隔（秒）。</param>
        /// <returns>找到返回 true；否则 false。</returns>
        bool TryGetUnitAttackIntervalSeconds(int runtimeId, out float intervalSeconds);

        /// <summary>
        /// 尝试查询单位当前逻辑状态（Idle/Attack）。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="state">输出当前状态。</param>
        /// <returns>找到返回 true；否则 false。</returns>
        bool TryGetUnitState(int runtimeId, out AttackUnitState state);

        /// <summary>
        /// 尝试查询投射物当前逻辑坐标与旋转。
        /// </summary>
        /// <param name="runtimeId">投射物运行时 ID。</param>
        /// <param name="x">输出逻辑 X。</param>
        /// <param name="y">输出逻辑 Y。</param>
        /// <param name="rotation">输出旋转角度（度，DisplayAngle 语义：0°朝上/90°朝右）。</param>
        /// <returns>找到返回 true；否则 false。</returns>
        bool TryGetProjectileState(int runtimeId, out float x, out float y, out float rotation);

        /// <summary>
        /// 尝试查询敌人血量比例。
        /// </summary>
        /// <param name="runtimeId">敌人运行时 ID。</param>
        /// <param name="ratio">输出血量比例（0~1）。</param>
        /// <returns>找到返回 true；否则 false。</returns>
        bool TryGetEnemyHealthRatio(int runtimeId, out float ratio);
    }

    /// <summary>
    /// Unity 帧中表现同步器：读取只读状态并对表现对象做插值/同步，不推进战斗逻辑。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:219）：</b>在 Unity 帧中插值/同步表现；不得推进战斗逻辑。</para>
    ///
    /// <para><b>不推进战斗逻辑：</b>本类型只读 BattleReadModel/只读位置查询，绝不调用
    /// BattleSimulation.Advance、BattleRuntime.Advance 或任何 Manager 的 Update/Apply。
    /// 逻辑推进的唯一入口是 BattleSimulation（design.md 第 2 节）。</para>
    ///
    /// <para><b>不回写规则状态（design.md:217）：</b>表现插值只更新表现对象的视觉
    /// Transform/血条，不回写逻辑实体的 X/Y 或 BattleState。</para>
    ///
    /// <para><b>高频逐实体不通过事件（design.md 第 4 节 / task 7.9）：</b>每帧直接遍历
    /// ViewRegistry 中登记的表现对象，经 IViewReadModelProvider 查询逻辑位置做同步，
    /// 避免 GameEvent 高频分发。</para>
    ///
    /// <para><b>框架解耦（design.md:9）：</b>不直接引用 UnityEngine/FairyGUI 类型，
    /// 经 IViewObjectSync 委托由真实实现完成位置/血条/朝向更新。</para>
    ///
    /// <para><b>生命周期：</b>随 BattlePresenter 创建，随 BattleRuntimeScope 释放。</para>
    /// </remarks>
    internal sealed class BattleViewSynchronizer
    {
        // ====================================================================
        // 只读依赖
        // ====================================================================

        /// <summary>
        /// 表现对象注册表，供 Synchronizer 按 runtimeId 查找表现对象。
        /// </summary>
        private readonly BattleViewRegistry _registry;

        /// <summary>
        /// 只读位置查询提供者，由 BattlePresenter 注入。
        /// <para>Presenter 持有 BattleReadModel，但 ReadModel 不暴露实体位置；
        /// Presenter 把 Manager 的只读位置查询封装为本接口注入 Synchronizer。</para>
        /// </summary>
        private readonly IViewReadModelProvider _readModelProvider;

        /// <summary>
        /// 表现对象同步委托，由真实表现层实现注入。
        /// <para>Null/Test 实现可注入空实现（所有方法空操作）。</para>
        /// </summary>
        private readonly IViewObjectSync _objectSync;

        /// <summary>
        /// 帧同步复用缓冲。复制注册表快照后再同步，避免表现回调期间注册表变化导致枚举失效。
        /// </summary>
        private readonly List<KeyValuePair<int, object>> _entryBuffer =
            new List<KeyValuePair<int, object>>();

        /// <summary>
        /// 每个已注册单位的上次逻辑状态，供"状态变化边沿"检测（只在 Attack → Idle 时复位表现）。
        /// <para>首次观测到某 runtimeId 时不触发复位——单位回池/再次创建时
        /// <see cref="UnityBattleViewPort"/> 已在 AcquireFromPool 中复位过表现。
        /// 若首次观测即 Attack 态还复位，会错误取消进行中的攻击动画。</para>
        /// </summary>
        private readonly Dictionary<int, AttackUnitState> _lastUnitStates =
            new Dictionary<int, AttackUnitState>();

        // ====================================================================
        // 生命周期状态
        // ====================================================================

        /// <summary>
        /// 是否已停止同步（Dispose 后为 true，Sync 调用为空操作）。
        /// <para>由 BattlePresenter 在 StopPresentation/Dispose 时调用 Stop 置位。</para>
        /// </summary>
        public bool IsStopped { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造表现同步器。
        /// </summary>
        /// <param name="registry">表现对象注册表（非 null）。</param>
        /// <param name="readModelProvider">只读位置查询提供者（非 null）。</param>
        /// <param name="objectSync">表现对象同步委托（非 null）。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        internal BattleViewSynchronizer(
            BattleViewRegistry registry,
            IViewReadModelProvider readModelProvider,
            IViewObjectSync objectSync)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _readModelProvider = readModelProvider
                ?? throw new ArgumentNullException(nameof(readModelProvider));
            _objectSync = objectSync ?? throw new ArgumentNullException(nameof(objectSync));
        }

        // ====================================================================
        // 帧同步入口
        // ====================================================================

        /// <summary>
        /// 每 Unity 帧调用一次：读取只读状态并对表现对象做插值/同步。
        /// </summary>
        /// <param name="deltaSeconds">
        /// 本 Unity 帧的渲染帧时间增量（秒），来自 TEngine OnUpdate。
        /// 仅用于表现插值平滑，不作为逻辑时间源（逻辑时间源是 BattleSimulation）。
        /// </param>
        /// <remarks>
        /// <para><b>不推进战斗逻辑（design.md:219）：</b>
        /// 本方法只读 ReadModel/位置查询并更新表现对象视觉，不调用任何逻辑推进方法。</para>
        ///
        /// <para><b>高频逐实体不通过事件（design.md 第 4 节）：</b>
        /// 直接遍历 Registry 中登记的表现对象，经 ReadModelProvider 查询逻辑位置，
        /// 调用 ObjectSync 更新表现对象视觉。</para>
        ///
        /// <para><b>插值简化：</b>本期最小实现采用"对齐到逻辑位置"策略（无插值平滑），
        /// 即直接把表现对象位置设置为逻辑位置。后续可扩展为基于 deltaSeconds 的
        /// 线性插值/Lerp 平滑，但插值只影响表现对象，不回写逻辑实体。</para>
        /// </remarks>
        internal void Sync(float deltaSeconds)
        {
            if (IsStopped)
            {
                return;
            }

            // 同步敌人表现对象：遍历 Registry 中的敌人桶位，查询逻辑位置并更新。
            // 使用临时列表避免在遍历中修改字典（Unregister 可能在别处调用）。
            SyncCategory(ViewObjectCategory.Enemy,
                _readModelProvider.TryGetEnemyPosition,
                _readModelProvider.TryGetEnemyHealthRatio);

            // 同步单位表现对象。
            SyncCategory(ViewObjectCategory.Unit,
                _readModelProvider.TryGetUnitPosition,
                healthRatioProvider: null);
            SyncUnitStates();
            SyncUnitFacings();
            SyncUnitWeaponAims();
            SyncUnitAttackIntervals();
            SyncUnitAnimations();

            // 同步投射物表现对象（含旋转）。
            SyncProjectiles();
        }

        /// <summary>
        /// 同步单位逻辑状态：只在状态变化边沿（Attack → Idle）对表现对象执行一次待机复位。
        /// </summary>
        /// <remarks>
        /// <para><b>根因：</b>AttackScheduler 在目标丢失时调用
        /// <see cref="UnitBase.SetState(AttackUnitState.Idle)"/>，但旧版同步器只同步
        /// 最后朝向/瞄准角，没有同步状态，导致弓兵翻转、枪兵瞄准等旧表现残留。</para>
        /// <para><b>状态边沿触发：</b>保存每个已注册单位的上次状态，仅在
        /// "状态从 Attack 变为 Idle"时调用一次
        /// <see cref="IViewObjectSync.ResetUnitToIdle"/>。不能每帧调用，
        /// 否则待机动画会不断回到第 0 帧。</para>
        /// </remarks>
        private void SyncUnitStates()
        {
            _registry.CopyEntries(ViewObjectCategory.Unit, _entryBuffer);
            for (int index = 0; index < _entryBuffer.Count; index++)
            {
                KeyValuePair<int, object> entry = _entryBuffer[index];
                if (entry.Value == null
                    || !_readModelProvider.TryGetUnitState(entry.Key, out AttackUnitState currentState))
                {
                    continue;
                }

                bool isAttackToIdle =
                    _lastUnitStates.TryGetValue(entry.Key, out AttackUnitState previousState)
                    && previousState == AttackUnitState.Attack
                    && currentState == AttackUnitState.Idle;

                _lastUnitStates[entry.Key] = currentState;

                if (isAttackToIdle)
                {
                    _objectSync.ResetUnitToIdle(entry.Value);
                }
            }
        }

        /// <summary>
        /// 同步单位角色本体朝向（弓兵攻击转向）。
        /// </summary>
        private void SyncUnitFacings()
        {
            _registry.CopyEntries(ViewObjectCategory.Unit, _entryBuffer);
            for (int index = 0; index < _entryBuffer.Count; index++)
            {
                KeyValuePair<int, object> entry = _entryBuffer[index];
                if (entry.Value != null
                    && _readModelProvider.TryGetUnitFacing(entry.Key, out bool facingRight))
                {
                    _objectSync.SetBodyFacing(entry.Value, facingRight);
                }
            }
        }

        /// <summary>
        /// 同步单位武器瞄准角度（枪兵武器朝向）。
        /// </summary>
        private void SyncUnitWeaponAims()
        {
            _registry.CopyEntries(ViewObjectCategory.Unit, _entryBuffer);
            for (int index = 0; index < _entryBuffer.Count; index++)
            {
                KeyValuePair<int, object> entry = _entryBuffer[index];
                if (entry.Value != null
                    && _readModelProvider.TryGetUnitWeaponAim(entry.Key, out float angleDegrees))
                {
                    _objectSync.SetWeaponAim(entry.Value, angleDegrees);
                }
            }
        }

        /// <summary>
        /// 同步单位有效攻击间隔（随攻速动态调整攻击帧时长）。
        /// </summary>
        private void SyncUnitAttackIntervals()
        {
            _registry.CopyEntries(ViewObjectCategory.Unit, _entryBuffer);
            for (int index = 0; index < _entryBuffer.Count; index++)
            {
                KeyValuePair<int, object> entry = _entryBuffer[index];
                if (entry.Value != null
                    && _readModelProvider.TryGetUnitAttackIntervalSeconds(
                        entry.Key, out float intervalSeconds))
                {
                    _objectSync.SetAttackIntervalSeconds(entry.Value, intervalSeconds);
                }
            }
        }

        /// <summary>
        /// 同步投射物表现对象：查询逻辑位置与旋转，更新表现对象并应用旋转角度。
        /// </summary>
        private void SyncProjectiles()
        {
            _registry.CopyEntries(ViewObjectCategory.Projectile, _entryBuffer);
            for (int index = 0; index < _entryBuffer.Count; index++)
            {
                KeyValuePair<int, object> entry = _entryBuffer[index];
                if (entry.Value == null
                    || !_readModelProvider.TryGetProjectileState(
                        entry.Key, out float x, out float y, out float rotation))
                {
                    continue;
                }

                _objectSync.SetPosition(entry.Value, x, y);
                _objectSync.SetProjectileRotation(entry.Value, rotation);
            }
        }

        /// <summary>
        /// 按逻辑层实际出手时间触发单位攻击动画，不依赖持续攻击状态。
        /// </summary>
        private void SyncUnitAnimations()
        {
            _registry.CopyEntries(ViewObjectCategory.Unit, _entryBuffer);
            for (int index = 0; index < _entryBuffer.Count; index++)
            {
                KeyValuePair<int, object> entry = _entryBuffer[index];
                if (entry.Value != null
                    && _readModelProvider.TryGetUnitAttackTime(
                        entry.Key, out long attackTimeMs))
                {
                    _objectSync.SetUnitAttackTime(entry.Value, attackTimeMs);
                }
            }
        }

        /// <summary>
        /// 同步单个类别的表现对象。
        /// </summary>
        /// <param name="category">表现对象类别。</param>
        /// <param name="positionProvider">位置查询委托。</param>
        /// <param name="healthRatioProvider">血量比例查询委托（可为 null，表示该类别无血条）。</param>
        /// <remarks>
        /// 遍历 Registry 中该类别的全部表现对象，查询逻辑位置并更新表现对象视觉。
        /// 逻辑实体已移除但表现对象仍在 Registry 中的情况：positionProvider 返回 false，
        /// 跳过该对象（表现对象的移除由 Presenter 经 ViewPort.OnXxxRemoved 事实处理）。
        /// </remarks>
        private void SyncCategory(
            ViewObjectCategory category,
            TryGetPositionDelegate positionProvider,
            TryGetHealthRatioDelegate healthRatioProvider)
        {
            if (positionProvider == null)
            {
                return;
            }

            _registry.CopyEntries(category, _entryBuffer);
            for (int index = 0; index < _entryBuffer.Count; index++)
            {
                KeyValuePair<int, object> entry = _entryBuffer[index];
                if (entry.Value == null
                    || !positionProvider(entry.Key, out float x, out float y))
                {
                    // 逻辑对象已移除时等待对应低频移除事实回收表现对象。
                    // 不在帧同步路径反向销毁 ViewPort，避免和生命周期事实竞争。
                    continue;
                }

                _objectSync.SetPosition(entry.Value, x, y);
                if (healthRatioProvider != null
                    && healthRatioProvider(entry.Key, out float healthRatio))
                {
                    _objectSync.SetHealthRatio(entry.Value, healthRatio);
                }
            }
        }

        /// <summary>
        /// 位置查询委托类型（供 SyncCategory 使用）。
        /// </summary>
        private delegate bool TryGetPositionDelegate(int runtimeId, out float x, out float y);

        /// <summary>
        /// 血量比例查询委托类型（供 SyncCategory 使用）。
        /// </summary>
        private delegate bool TryGetHealthRatioDelegate(int runtimeId, out float ratio);

        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <summary>
        /// 停止同步：置位 IsStopped，后续 Sync 调用为空操作。
        /// </summary>
        /// <remarks>
        /// <para>由 BattlePresenter 在 StopPresentation/Dispose 时调用。</para>
        /// <para>幂等：重复调用安全。</para>
        /// <para>本方法不销毁表现对象——表现对象的销毁由真实 ViewPort.Clear 负责。
        /// 本方法只停止 Synchronizer 的帧同步循环。</para>
        /// </remarks>
        internal void Stop()
        {
            IsStopped = true;
        }
    }
}
