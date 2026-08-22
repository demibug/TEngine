using Cysharp.Threading.Tasks;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.3：IBattleVfxPort —— 战斗特效意图端口
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Ports/IBattleVfxPort.cs）：
    //   战斗特效意图端口；逻辑测试可使用空实现。逻辑层通过本端口向表现层发送
    //   "在某某位置播放命中特效 / 死亡特效 / 攻击特效 / 生成特效" 等低频视觉意图，
    //   不直接持有 ParticleSystem、GameObject 或任何 Unity 特效对象。表现层实现
    //   本端口，把意图翻译成实际特效播放，且不得回写规则状态（design.md:217）。
    //
    // 来源证据（design.md 决策 4 / specs/battle-event-boundary/spec.md）：
    //   - design.md:9 "逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象或
    //     Time.deltaTime；表现通过端口和 Presenter 同步"
    //   - design.md:216 "战斗特效意图端口；逻辑测试可使用空实现"
    //   - spec battle-runtime-lifecycle "Exit releases battle-owned state"：
    //     退出时清理特效对象和表现回调
    //   - spec battle-runtime-lifecycle "Settling has no gameplay damage authority"：
    //     Settling 只允许发布不可变结果、生成只读快照和执行不回写规则状态的表现收尾
    //
    // 异步语义（任务 7.3）：
    //   异步预加载通过 UniTask + lease.IsDone 轮询完成，不依赖取消令牌。
    //   Settling/Exit 的迟到特效回调经 Clear 幂等停止失效（spec "Exit releases battle-owned state"）。
    //   Null 实现的异步方法立即返回，不执行任何 IO。
    //
    // 设计考量：
    //   - 本端口只表达"意图"（在某位置播放某类特效），不表达具体 Prefab 路径。
    //     具体资源映射由真实实现根据 <paramref name="vfxId"/> 查表完成。
    //   - 特效播放是高频操作（每次命中/死亡/攻击都可能触发），真实实现必须使用
    //     对象池复用 ParticleSystem / GameObject，避免运行时 Instantiate
    //     （task 7.9 性能 profile）。
    //   - 位置参数使用逻辑坐标（与 EnemyBase.X/Y、UnitBase.CenterX/CenterY 一致），
    //     真实实现负责把逻辑坐标映射到 Unity 世界坐标。
    //
    // 不变量：
    //   1. 逻辑层单向调用：逻辑层只调本端口发送特效意图，不从端口读规则状态。
    //   2. 不持有特效对象引用：接口参数只包含逻辑标量与坐标，不传 GameObject /
    //      ParticleSystem / Transform。
    //   3. 异步操作不依赖取消令牌：异步预加载通过 lease.IsDone 轮询完成，Settling/Exit 的
    //      迟到回调由 Clear 幂等停止失效。
    //   4. 线程安全不要求：所有调用在 Unity 主线程的 Runtime 串行队列中执行。
    // ============================================================================

    /// <summary>
    /// 战斗特效意图端口（design.md Ports/IBattleVfxPort.cs）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:216）：</b>逻辑层通过本端口向表现层发送特效播放意图。
    /// 表现层实现本端口，把意图翻译成实际特效播放，不回写规则状态。</para>
    ///
    /// <para><b>意图式设计：</b>本端口只表达"在某位置播放某类特效"，不表达具体 Prefab 路径。
    /// 真实实现根据 <c>vfxId</c> 查表映射到特效 Prefab，使逻辑层与资源命名解耦。</para>
    ///
    /// <para><b>高频与对象池：</b>特效播放是高频操作，真实实现必须使用对象池复用
    /// ParticleSystem / GameObject，避免运行时 Instantiate（task 7.9 性能 profile）。</para>
    ///
    /// <para><b>异步（任务 7.3）：</b>异步预加载通过 lease.IsDone 轮询完成，不依赖
    /// 取消令牌。Settling/Exit 的迟到特效回调经 Clear 幂等停止失效
    /// （spec "Exit releases battle-owned state"）。</para>
    ///
    /// <para><b>线程安全：</b>不要求。所有调用在 Unity 主线程的 Runtime 串行队列中执行。</para>
    ///
    /// <para><b>实现：</b></para>
    /// <list type="bullet">
    /// <item><see cref="NullBattleVfxPort"/> —— 纯逻辑 Null/Test 实现，用于 EditMode 测试
    /// （design.md:216 "逻辑测试可使用空实现"）。</item>
    /// <item>TODO Phase 6 task 7.4/7.6：Unity 真实实现（由 BattlePresenter 组合实现），
    /// 用于 PlayMode/生产。真实实现将通过 <c>GameModule.Resource.LoadAssetAsync</c> 加载
    /// 特效 Prefab，使用对象池复用，并登记到 <see cref="BattleRuntimeScope"/> 随局释放。
    /// 当前 asmdef 未引用 FairyGUI，特效真实实现不依赖 FairyGUI（基于 UnityEngine
    /// ParticleSystem），可由后续 task 在 Presentation/ 目录接入。</item>
    /// </list>
    /// </remarks>
    public interface IBattleVfxPort
    {
        // ====================================================================
        // 生命周期与预加载
        // ====================================================================

        /// <summary>
        /// 异步预加载本端口所需的特效资源（命中/死亡/攻击/生成等 Prefab）。
        /// </summary>
        /// <remarks>
        /// <para>由 BattleRuntimeFactory / BattleModule 在 Entering 阶段调用。
        /// 加载失败向上传播为结构化 <c>BattleOperationResult</c>，使部分初始化回滚
        /// （spec "Partial initialization is recoverable"）。</para>
        /// <para>Null 实现立即返回 <see cref="UniTask.CompletedTask"/>。</para>
        /// </remarks>
        UniTask PreloadAsync();

        // ====================================================================
        // 特效意图
        // ====================================================================

        /// <summary>
        /// 在指定逻辑位置播放命中特效。
        /// </summary>
        /// <param name="vfxId">特效标识（如 "hit_melee"、"hit_arrow"、"hit_pike"）。</param>
        /// <param name="x">逻辑 X 坐标（命中点）。</param>
        /// <param name="y">逻辑 Y 坐标。</param>
        /// <remarks>
        /// <para>对应逻辑层攻击命中事实。真实实现使用对象池复用特效对象，在指定位置播放
        /// 并定时回收。Null 实现为空操作。</para>
        /// </remarks>
        void PlayHitEffect(string vfxId, float x, float y);

        /// <summary>
        /// 在指定逻辑位置播放死亡特效。
        /// </summary>
        /// <param name="vfxId">特效标识（如 "enemy_die"、"unit_die"）。</param>
        /// <param name="x">逻辑 X 坐标。</param>
        /// <param name="y">逻辑 Y 坐标。</param>
        void PlayDeathEffect(string vfxId, float x, float y);

        /// <summary>
        /// 在指定逻辑位置播放攻击释放特效（近战挥砍、弓箭发射等）。
        /// </summary>
        /// <param name="vfxId">特效标识（如 "attack_knife"、"attack_bow"、"attack_spear"、"attack_cavalry"）。</param>
        /// <param name="x">逻辑 X 坐标（攻击者中心）。</param>
        /// <param name="y">逻辑 Y 坐标。</param>
        void PlayAttackEffect(string vfxId, float x, float y);

        /// <summary>
        /// 在指定逻辑格子位置播放生成/放置特效。
        /// </summary>
        /// <param name="vfxId">特效标识（如 "enemy_spawn"、"unit_place"）。</param>
        /// <param name="gridX">格子列索引。</param>
        /// <param name="gridY">格子行索引。</param>
        void PlaySpawnEffect(string vfxId, int gridX, int gridY);

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 停止并回收本端口播放的全部特效，用于 Settling 静默清理与 Exit。
        /// </summary>
        /// <remarks>
        /// <para>由 BattleRuntime 在 Settling 或 BattleModule 在 Exit 时调用。
        /// 真实实现应停止全部活动 ParticleSystem、归还对象池、销毁临时特效对象。
        /// 幂等：重复调用安全。异步资源卸载由 <see cref="BattleRuntimeScope"/> 逆序释放。</para>
        /// <para>spec "Settling has no gameplay damage authority"：Settling 中只允许
        /// 表现收尾，不回写规则状态。本方法在 Settling 中调用时只做视觉清理，
        /// 不触发新的伤害或状态变更。</para>
        /// </remarks>
        void Clear();
    }
}
