using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.4：MeleeAttackEffect —— 即时近战命中效果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Combat/Effects/MeleeAttackEffect.cs）：
    //   延迟近战命中的共享逻辑和完整 Reset/Cancel 契约。
    //   本类型为"即时近战"变体：首次 Update 即命中并完成。
    //
    // 来源证据（还原工程 MeleeAttackEffect.js:1-91）：
    //   MeleeAttackEffect 是可复用的延迟/即时近战命中效果，核心字段：
    //     - owner / enemyManager / resolver
    //     - damage / multiplier / radius
    //     - hitSet: Set<id>       已命中敌人 ID 集合（防止同效果重复命中）
    //     - active / elapsed / durationMs / hitAtMs / hitTriggered
    //   核心方法：
    //     - launch({owner, enemyManager, damage, multiplier, radius, durationMs, hitAtMs})
    //     - update(deltaMs): elapsed += delta; elapsed>=hitAtMs 且未命中 → hit();
    //                        elapsed>=durationMs → cleanup('duration-complete')
    //     - hit(): resolver.queryEnemyObjects(center, range, side) → 遍历目标 →
    //              hitSet 去重 → resolver.hit(target, damage*multiplier, owner)
    //     - cleanup(): active=false; owner=null; enemyManager=null; hitSet.clear()
    //     - reset(): 全字段清空，等价新构造（池复用）
    //
    // 决策依据：
    //   - design.md 目录表：MeleeAttackEffect 为"延迟近战命中的共享逻辑和完整
    //     Reset/Cancel 契约"。PikeAttackEffect/CavalrySweepEffect 在 JS 中继承自
    //     MeleeAttackEffect，C# 因 IAttackEffect 接口约束与 sealed 设计改为各自
    //     独立 sealed class，共享逻辑以本类型为参考蓝本。
    //   - task 5.4 约束：实现 IAttackEffect（Active/Owner/Update/Cancel）+
    //     IPoolableBattleObject.ResetState；伤害经 AttackResolver 提交（不直接
    //     EnemyBase.Hit 绕过 resolver）；尊重 Settling/freeze（Cancel 标记非活动，
    //     不造成伤害）；中文注释、UTF-8、LF。
    //   - design.md 决策 0.4 / spec "Battle result is frozen once"：Cancel 只停止
    //     活动状态、释放引用，不造成伤害。Settling 静默清理经 AttackEffectManager.Clear
    //     调 Cancel，满足 "Settling has no gameplay damage authority"。
    //   - spec battle-simulation "Melee effect is created by attack scheduling"：
    //     单位攻击调度在当前子步创建的近战效果，在当前子步 AttackEffect 阶段立即累计
    //     一次本子步 stepMs（由 AttackEffectManager.Update 快照遍历保证）。
    //   - task 5.5（后续）：KnifeAttackTimeline 合入 KnifeAttackEffect，禁止双轨推进。
    //     本类型作为 Manager 唯一推进的效果，不自行注册 Timer。
    //
    // C# 与 JS 的差异：
    //   1. owner 位置/阵营：JS 从 owner.displayObject.x/y 与 owner.side 读取。
    //      C# 单位（task 6.2 未创建）尚未存在，定义 IAttackEffectOwner 最小契约
    //      （RuntimeId/Side/CenterX/CenterY）供效果读取，由单位实现。
    //   2. cellWidth/cellHeight：JS EnemyManager.queryTargets 从 gameData.map 读取
    //      gridWidth/gridHeight。C# EnemyManager.QueryTargets 显式接收，故 Launch 时
    //      注入，由调用方（单位）从配置/MapData 提供。
    //   3. attackerId：JS 传 owner 对象引用给 target.hit(damage, attacker)。
    //      C# 改为 int attackerId（与 IEnemyEntity.Hit(int,int) 一致），从 owner.RuntimeId 取。
    //   4. 即时变体：JS 默认 durationMs=180/hitAtMs=45。本类型按 task 5.4 "即时近战"
    //      要求默认 hitAtMs=0/durationMs=0：首次 Update 即命中并完成。
    //
    // 不变量：
    //   1. 唯一推进：Update 只由 AttackEffectManager.Update 在 AttackEffect 阶段调用。
    //   2. 伤害经 resolver：hit() 只调 resolver.QueryEnemyObjects/resolver.Hit，
    //      不直接 EnemyBase.Hit 或 EnemyManager.ApplyDamage，统一死亡守卫与目标查询。
    //   3. hitSet 去重：同效果内同一敌人只命中一次（对应 JS hitSet.has/add）。
    //   4. Cancel 不伤害：Cancel 只置 active=false、释放引用，不调 hit/Update。
    //   5. ResetState 完整清空：回收后等价新构造，无残留引用。
    //   6. 幂等 Cancel/ResetState：重复调用安全。
    // ============================================================================

    /// <summary>
    /// 攻击效果所有者最小契约：效果在命中时读取所有者的位置、阵营与运行时 ID。
    /// </summary>
    /// <remarks>
    /// <para><b>契约来源：</b>对应还原工程 <c>MeleeAttackEffect.js:69</c>
    /// <c>this.owner.displayObject || this.owner.combatPosition</c>（位置）与
    /// <c>this.owner.side</c>（阵营）及 <c>this.owner.id</c>（攻击者 ID）。
    /// C# 单位（task 6.2 SoldierBase）尚未创建，本接口隔离效果对单位具体类型的依赖。</para>
    ///
    /// <para><b>字段说明：</b>
    /// <list type="bullet">
    /// <item><see cref="RuntimeId"/> ← <c>owner.id</c>（攻击者运行时 ID，透传给
    ///   <see cref="IEnemyEntity.Hit(int,int)"/> 的 attackerId）</item>
    /// <item><see cref="Side"/> ← <c>owner.side</c>（true=玩家方，用于目标查询阵营过滤）</item>
    /// <item><see cref="CenterX"/>/<see cref="CenterY"/> ← <c>displayObject.x + width/2</c>
    ///   等（C# 移植为纯逻辑中心坐标，作为命中查询中心）</item>
    /// </list></para>
    ///
    /// <para><b>本接口为 internal：</b>只供 GameBattle 内部攻击效果与单位实现使用。
    /// 定义于 MeleeAttackEffect.cs 供同目录效果共享；task 6.2 的 SoldierBase 实现本接口。</para>
    /// </remarks>
    internal interface IAttackEffectOwner
    {
        /// <summary>运行时 ID（对应 <c>owner.id</c>），作为 attackerId 透传给伤害提交。</summary>
        int RuntimeId { get; }

        /// <summary>阵营：true=玩家方，false=对手方（对应 <c>owner.side</c>）。</summary>
        bool Side { get; }

        /// <summary>逻辑中心 X（对应 <c>displayObject.x + width/2</c>）。</summary>
        float CenterX { get; }

        /// <summary>逻辑中心 Y（对应 <c>displayObject.y + height/2</c>）。</summary>
        float CenterY { get; }
    }

    /// <summary>
    /// 即时近战命中效果：首次 Update 即命中并完成（task 5.4）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>延迟近战命中的共享逻辑和完整 Reset/Cancel
    /// 契约。本类型为"即时近战"变体——首次 <see cref="Update"/> 即命中并完成。
    /// 替代还原工程 <c>MeleeAttackEffect.js</c>（<c>MeleeAttackEffect.js:6-89</c>）。</para>
    ///
    /// <para><b>即时语义（task 5.4 "MeleeAttackEffect: 即时近战"）：</b>
    /// 默认 <c>hitAtMs=0</c>、<c>durationMs=0</c>。首次 <see cref="Update"/> 累计 stepMs 后
    /// <c>elapsed &gt;= hitAtMs(0)</c> 触发命中，随即 <c>elapsed &gt;= durationMs(0)</c>
    /// 完成并标记非活动。对应 JS <c>MeleeAttackEffect.update</c> 的命中/完成判定逻辑，
    /// 仅常量改为即时。</para>
    ///
    /// <para><b>伤害经 AttackResolver（task 5.4 约束）：</b>
    /// <see cref="Hit"/> 只调 <see cref="AttackResolver.QueryEnemyObjects"/> 查询目标、
    /// <see cref="AttackResolver.Hit"/> 提交伤害，不直接 <see cref="IEnemyEntity.Hit"/>
    /// 或 <see cref="EnemyManager.ApplyDamage"/>，统一死亡守卫与稳定目标查询。</para>
    ///
    /// <para><b>hitSet 去重（对应 JS <c>hitSet: Set</c>）：</b>
    /// 同一效果内已命中的敌人 ID 不重复命中。即使效果跨多子步（如 Pike/Cavalry），
    /// 同一敌人只受一次该效果伤害。</para>
    ///
    /// <para><b>Cancel 不伤害（spec "Settling has no gameplay damage authority"）：</b>
    /// <see cref="Cancel"/> 只置 <c>active=false</c>、释放 owner/enemyManager/hitSet 引用，
    /// 不调 <see cref="Hit"/>/<see cref="Update"/>。Settling 静默清理经
    /// <see cref="AttackEffectManager.Clear"/> 调 Cancel，不造成伤害。</para>
    ///
    /// <para><b>池复用（task 4.1）：</b>
    /// <see cref="ResetState"/> 清空全部可变状态（owner/enemyManager/resolver/damage/
    /// multiplier/radius/elapsed/duration/hitSet/cellWidth/cellHeight/attackerId），
    /// 回收后等价新构造。由 <see cref="BattleObjectPool{T}.Release"/> 在归还前调用。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部使用。</para>
    /// </remarks>
    internal sealed class MeleeAttackEffect : IAttackEffect
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀，便于在日志中筛选近战效果相关条目。</summary>
        private const string LogTag = "[MeleeAttackEffect]";

        // ====================================================================
        // 活动状态与所有者（IAttackEffect 契约）
        // ====================================================================

        /// <summary>
        /// 是否活动（对应 <c>this.active</c>）。
        /// <para>false 表示已完成（duration 到期/命中已结算）或已被 Cancel。
        /// AttackEffectManager.Update 遍历时对 Active=false 的效果执行移除回收。</para>
        /// </summary>
        private bool _active;

        /// <summary>
        /// 所有者引用（对应 <c>this.owner</c>），用于 CancelOwner 匹配与命中位置/阵营读取。
        /// <para>Cancel/ResetState 后置 null。</para>
        /// </summary>
        private IAttackEffectOwner _owner;

        // ====================================================================
        // 外部依赖（命中时使用）
        // ====================================================================

        /// <summary>攻击解析服务（对应 JS <c>this.resolver</c>），提供目标查询与伤害提交。</summary>
        private AttackResolver _resolver;

        /// <summary>敌人管理器（对应 JS <c>this.enemyManager</c>），命中查询目标。</summary>
        private EnemyManager _enemyManager;

        // ====================================================================
        // 攻击参数
        // ====================================================================

        /// <summary>基础伤害值（对应 <c>this.damage</c>）。</summary>
        private int _damage;

        /// <summary>伤害倍率（对应 <c>this.multiplier</c>，默认 1）。最终伤害 = damage * multiplier。</summary>
        private float _multiplier;

        /// <summary>命中半径（对应 <c>this.radius</c>），查询 center 周围 radius 内的敌人。</summary>
        private float _radius;

        /// <summary>敌人格子宽（对应 <c>map.gridWidth</c>），透传给 QueryEnemyObjects。</summary>
        private float _cellWidth;

        /// <summary>敌人格子高（对应 <c>map.gridHeight</c>），透传给 QueryEnemyObjects。</summary>
        private float _cellHeight;

        // ====================================================================
        // 时序状态
        // ====================================================================

        /// <summary>已累计时长（对应 <c>this.elapsed</c>，毫秒）。</summary>
        private long _elapsed;

        /// <summary>效果总持续时间（对应 <c>this.durationMs</c>）。elapsed &gt;= durationMs 时完成。</summary>
        private long _durationMs;

        /// <summary>命中时机（对应 <c>this.hitAtMs</c>）。elapsed &gt;= hitAtMs 时触发命中。</summary>
        private long _hitAtMs;

        /// <summary>是否已命中（对应 <c>this.hitTriggered</c>），防止重复命中。</summary>
        private bool _hitTriggered;

        // ====================================================================
        // 已命中敌人 ID 集合（对应 JS hitSet: Set<id>）
        // ====================================================================

        /// <summary>
        /// 已命中敌人 ID 集合（对应 <c>this.hitSet</c>）。
        /// <para>同一效果内同一敌人只命中一次。使用 HashSet&lt;int&gt; O(1) 查重，
        /// 仅用于去重查找，不参与遍历顺序决定。</para>
        /// </summary>
        private readonly HashSet<int> _hitSet = new HashSet<int>();

        // ====================================================================
        // IAttackEffect 属性
        // ====================================================================

        /// <inheritdoc />
        /// <summary>是否活动。</summary>
        public bool Active => _active;

        /// <inheritdoc />
        /// <summary>所有者引用（用于 CancelOwner 匹配）；非活动时可能为 null。</summary>
        public object Owner => _owner;

        // ====================================================================
        // 构造（池工厂调用）
        // ====================================================================

        /// <summary>
        /// 构造近战攻击效果。池工厂 <c>()-&gt;new MeleeAttackEffect()</c> 调用。
        /// </summary>
        /// <remarks>与 JS <c>constructor</c> 一致调用 <see cref="ResetState"/> 清空状态。
        /// 由 <see cref="BattleObjectPool{MeleeAttackEffect}"/> 工厂委托创建。</remarks>
        internal MeleeAttackEffect()
        {
            ResetState();
        }

        // ====================================================================
        // Launch —— 由单位在 Acquire 后调用，配置攻击参数并激活
        // --------------------------------------------------------------------
        // 对应 JS launch({owner, enemyManager, damage, multiplier, radius,
        //   durationMs, hitAtMs})。C# 额外接收 resolver/cellWidth/cellHeight
        //   （JS 中 resolver 在构造注入、cellWidth 在 enemyManager 内部读取）。
        // ====================================================================

        /// <summary>
        /// 启动近战攻击效果：配置参数并激活（对应 JS <c>launch</c>）。
        /// </summary>
        /// <param name="owner">所有者（非 null），提供位置/阵营/运行时 ID。</param>
        /// <param name="resolver">攻击解析服务（非 null），提供目标查询与伤害提交。</param>
        /// <param name="enemyManager">敌人管理器（非 null），命中查询目标。</param>
        /// <param name="damage">基础伤害值（正数）。</param>
        /// <param name="cellWidth">敌人格子宽（对应 <c>map.gridWidth</c>）。</param>
        /// <param name="cellHeight">敌人格子高（对应 <c>map.gridHeight</c>）。</param>
        /// <param name="multiplier">伤害倍率（默认 1）。</param>
        /// <param name="radius">命中半径（默认 48，对应 JS <c>radius=48</c>）。</param>
        /// <param name="durationMs">
        /// 效果持续时间（毫秒）。默认 0 表示即时——首次 Update 即完成。
        /// 对应 JS <c>durationMs=180</c>，本类型按 task 5.4 即时语义默认 0。
        /// </param>
        /// <param name="hitAtMs">
        /// 命中时机（毫秒）。默认 0 表示首次 Update 即命中。
        /// 对应 JS <c>hitAtMs=durationMs*0.25</c>，本类型按即时语义默认 0。
        /// </param>
        /// <returns>this（便于链式调用）。</returns>
        /// <remarks>
        /// <para><b>对应 JS <c>launch</c>：</b>设置 owner/enemyManager/damage/multiplier/
        /// radius/durationMs/hitAtMs，重置 elapsed/hitTriggered，置 active=true。</para>
        /// <para><b>即时默认（task 5.4）：</b>durationMs=0/hitAtMs=0 使本类型为即时近战。
        /// PikeAttackEffect/CavalrySweepEffect 各自硬编码非即时常量。</para>
        /// </remarks>
        internal MeleeAttackEffect Launch(
            IAttackEffectOwner owner,
            AttackResolver resolver,
            EnemyManager enemyManager,
            int damage,
            float cellWidth,
            float cellHeight,
            float multiplier = 1f,
            float radius = 48f,
            long durationMs = 0,
            long hitAtMs = 0)
        {
            _owner = owner;
            _resolver = resolver;
            _enemyManager = enemyManager;
            _damage = damage;
            _multiplier = multiplier;
            _radius = radius;
            _cellWidth = cellWidth;
            _cellHeight = cellHeight;
            _durationMs = durationMs < 0 ? 0 : durationMs;
            _hitAtMs = hitAtMs < 0 ? 0 : hitAtMs;
            _elapsed = 0;
            _hitTriggered = false;
            _hitSet.Clear();
            _active = true;
            return this;
        }

        // ====================================================================
        // Update —— 唯一推进入口，由 AttackEffectManager.Update 调用
        // ====================================================================

        /// <inheritdoc />
        /// <summary>
        /// 推进一帧：累计 elapsed，到达 hitAtMs 触发命中，到达 durationMs 完成并标记非活动。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒），驱动效果累计与命中时机判断。</param>
        /// <remarks>
        /// <para><b>对应 JS <c>update(deltaMs)</c>：</b>
        /// <code>
        /// this.elapsed += Math.max(0, deltaMs);
        /// if (!this.hitTriggered && this.elapsed >= this.hitAtMs) {
        ///   this.hitTriggered = true; this.hit();
        /// }
        /// if (this.elapsed >= this.durationMs) this.cleanup('duration-complete');
        /// </code></para>
        /// <para><b>即时语义：</b>默认 hitAtMs=0/durationMs=0，首次 Update 即命中并完成。</para>
        /// <para><b>命中作为同步副作用：</b>Hit 在本方法内同步调用，伤害立即生效，不推迟到帧末
        /// （spec "Update phases are explicit and single-owned"）。</para>
        /// <para><b>非活动守卫：</b>active=false 时直接返回，不累计不命中（对应 JS <c>if (!this.active) return false</c>）。</para>
        /// </remarks>
        public void Update(long deltaMs)
        {
            if (!_active)
            {
                return;
            }

            // 累计逻辑时间（对应 JS this.elapsed += Math.max(0, deltaMs)）。
            if (deltaMs > 0)
            {
                _elapsed += deltaMs;
            }

            // 命中时机判定：未命中且 elapsed >= hitAtMs → 触发命中（对应 JS 第 59-62 行）。
            if (!_hitTriggered && _elapsed >= _hitAtMs)
            {
                _hitTriggered = true;
                Hit();
            }

            // 完成判定：elapsed >= durationMs → 标记非活动（对应 JS 第 63 行 cleanup）。
            // Cancel 不在此调用——cleanup 的"释放引用"由 Cancel 统一负责；
            // 此处只置 _active=false，Manager 遍历后检测到非活动会入移除队列并调 Cancel。
            if (_elapsed >= _durationMs)
            {
                _active = false;
            }
        }

        // ====================================================================
        // Hit —— 经 AttackResolver 查询目标并提交伤害
        // --------------------------------------------------------------------
        // 对应 JS hit()：resolver.queryEnemyObjects → 遍历 → hitSet 去重 → resolver.hit。
        // C# 经 AttackResolver 统一目标查询与死亡守卫，不直接 EnemyBase.Hit。
        // ====================================================================

        /// <summary>
        /// 命中结算：查询半径内敌人，经 <see cref="AttackResolver"/> 提交伤害（对应 JS <c>hit()</c>）。
        /// </summary>
        /// <remarks>
        /// <para><b>对应 JS <c>hit()</c>：</b>
        /// <code>
        /// const node = this.owner.displayObject || this.owner.combatPosition || {x:0,y:0};
        /// const targets = this.resolver.queryEnemyObjects({
        ///   enemyManager, center: {x: node.x, y: node.y}, range: this.radius, side: this.owner.side });
        /// for (const target of targets) {
        ///   if (this.hitSet.has(target.id)) continue;
        ///   this.hitSet.add(target.id);
        ///   this.resolver.hit(target, this.damage * this.multiplier, this.owner);
        /// }
        /// </code></para>
        /// <para><b>伤害经 resolver（task 5.4 约束）：</b>不直接 <see cref="IEnemyEntity.Hit"/>，
        /// 经 <see cref="AttackResolver.Hit"/> 统一死亡守卫（入口 null/伤害非正检查）+
        /// <see cref="IEnemyEntity.Hit"/> 内部死亡守卫（health&lt;=0 拒绝）。</para>
        /// <para><b>稳定有序：</b>目标顺序由 <see cref="AttackResolver.QueryEnemyObjects"/>
        /// 委托 <see cref="EnemyManager"/> 的稳定有序查询决定，本方法按返回顺序遍历。</para>
        /// <para><b>hitSet 去重：</b>同效果内同一敌人 ID 只命中一次（对应 JS <c>hitSet.has/add</c>）。</para>
        /// </remarks>
        private void Hit()
        {
            // 守卫：非活动/无所有者/无敌人管理器/无 resolver → 不命中（对应 JS 第 68 行）。
            if (!_active || _owner == null || _enemyManager == null || _resolver == null)
            {
                return;
            }

            // 查询半径内可攻击目标（对应 JS resolver.queryEnemyObjects）。
            // center = owner 逻辑中心，range = radius，side = owner.side。
            List<IEnemyEntity> targets = _resolver.QueryEnemyObjects(
                _enemyManager,
                _owner.CenterX, _owner.CenterY,
                _radius,
                _owner.Side,
                _cellWidth, _cellHeight,
                null);

            if (targets == null || targets.Count == 0)
            {
                return;
            }

            // 最终伤害 = damage * multiplier（对应 JS this.damage * this.multiplier）。
            int finalDamage = (int)(_damage * _multiplier);

            // 遍历目标，hitSet 去重，经 resolver 提交伤害。
            // 对应 JS for (const target of targets) { if (hitSet.has) continue; hitSet.add; resolver.hit }
            int count = targets.Count;
            for (int i = 0; i < count; i++)
            {
                IEnemyEntity target = targets[i];
                if (target == null)
                {
                    continue;
                }

                // hitSet 去重：同效果内同一敌人只命中一次（对应 JS hitSet.has(target.id)）。
                if (_hitSet.Contains(target.Id))
                {
                    continue;
                }

                _hitSet.Add(target.Id);

                // 经 resolver 提交伤害（对应 JS resolver.hit(target, damage*multiplier, owner)）。
                // C# attackerId = owner.RuntimeId（对应 JS owner 对象引用 → C# int）。
                // resolver.Hit 内部死亡守卫：null/伤害非正/已死亡目标返回 false。
                _resolver.Hit(target, finalDamage, _owner.RuntimeId);
            }
        }

        // ====================================================================
        // Cancel —— 取消并清理（不造成伤害），由 AttackEffectManager 在移除时调用
        // ====================================================================

        /// <inheritdoc />
        /// <summary>
        /// 取消并清理效果（对应 JS <c>cleanup()</c>）。不造成伤害，幂等。
        /// </summary>
        /// <param name="reason">取消原因（如 "effect-complete"、"game-over"、"owner-removed"），供诊断。</param>
        /// <remarks>
        /// <para><b>不造成伤害（spec "Settling has no gameplay damage authority"）：</b>
        /// 只置 <c>active=false</c>、释放 owner/enemyManager/resolver/hitSet 引用，
        /// 不调 <see cref="Hit"/>/<see cref="Update"/>。Settling 静默清理调本方法安全。</para>
        /// <para><b>幂等：</b>已 Cancel 的效果再次 Cancel 为空操作（active 已 false，引用已 null）。</para>
        /// <para><b>与 <see cref="ResetState"/> 的区别：</b>Cancel 停止活动并释放运行时引用；
        /// ResetState 清空全部可变状态（含 damage/radius/elapsed/cellWidth 等配置）供池复用。
        /// 池 Release 先调 ResetState；管理器移除先调 Cancel。</para>
        /// </remarks>
        public void Cancel(string reason)
        {
            // 幂等守卫：已非活动直接返回（对应 JS cleanup 无 active 检查但重复调用安全）。
            if (!_active)
            {
                return;
            }

            _active = false;
            // 释放运行时引用（对应 JS cleanup: owner=null; enemyManager=null; hitSet.clear()）。
            // resolver 一并释放——JS 中 resolver 为构造注入不清理，C# 池复用需清空避免跨局引用。
            _owner = null;
            _enemyManager = null;
            _resolver = null;
            _hitSet.Clear();
            // 注：不清理 damage/radius/elapsed 等配置字段——由 ResetState 在池回收时完整清空。
            // Cancel 的职责是"停止活动 + 释放外部引用"，ResetState 是"等价新构造"。
        }

        // ====================================================================
        // ResetState —— 池回收时清空全部可变状态（IPoolableBattleObject 契约）
        // --------------------------------------------------------------------
        // 对应 JS reset()：全字段清空，return this。由 BattleObjectPool.Release 归还前调用。
        // ====================================================================

        /// <inheritdoc />
        /// <summary>
        /// 重置对象到等价于新构造的状态（对应 JS <c>reset()</c>）。
        /// </summary>
        /// <remarks>
        /// <para><b>调用时机：</b>由 <see cref="BattleObjectPool{MeleeAttackEffect}.Release"/>
        /// 在归还前调用。Acquire 复用对象时不再调用。</para>
        /// <para><b>完整性（还原工程池复位契约）：</b>清空全部可变状态：
        /// owner/enemyManager/resolver（外部引用）、damage/multiplier/radius/cellWidth/
        /// cellHeight（攻击参数）、elapsed/durationMs/hitAtMs/hitTriggered（时序状态）、
        /// hitSet（已命中集合）、active（活动标志）。回收后等价新构造。</para>
        /// <para><b>幂等：</b>多次调用安全。</para>
        /// <para><b>不抛出：</b>实现约定不抛异常。</para>
        /// </remarks>
        public void ResetState()
        {
            _active = false;
            _owner = null;
            _resolver = null;
            _enemyManager = null;
            _damage = 0;
            _multiplier = 0f;
            _radius = 0f;
            _cellWidth = 0f;
            _cellHeight = 0f;
            _elapsed = 0;
            _durationMs = 0;
            _hitAtMs = 0;
            _hitTriggered = false;
            _hitSet.Clear();
        }
    }
}
