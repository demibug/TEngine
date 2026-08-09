using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.3：IBattleAudioPort —— 战斗音频意图端口
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Ports/IBattleAudioPort.cs）：
    //   战斗音频意图端口；逻辑测试可使用空实现。逻辑层通过本端口向表现层发送
    //   "播放 BGM / 播放 SFX / 停止 BGM" 等低频音频意图，不直接持有 AudioSource、
    //   AudioClip 或任何 Unity 音频对象。表现层实现本端口，把意图翻译成实际音频
    //   播放，且不得回写规则状态（design.md:217）。
    //
    // 来源证据（design.md 决策 4 / specs/battle-event-boundary/spec.md）：
    //   - design.md:9 "逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象或
    //     Time.deltaTime；表现通过端口和 Presenter 同步"
    //   - design.md:215 "战斗音频意图端口；逻辑测试可使用空实现"
    //   - spec battle-runtime-lifecycle "Exit releases battle-owned state"：
    //     退出时取消异步操作和表现回调
    //
    // 异步与取消语义（任务 7.3 要求）：
    //   所有异步操作接收 Runtime 或 Module CancellationToken。Runtime Token 在
    //   Settling / Exit 时 Cancel，使迟到的音频加载回调失效。Null 实现的异步方法
    //   立即返回，不执行任何 IO。
    //
    // 设计考量：
    //   - 本端口只表达"意图"（Play/Stop），不表达具体 AudioClip 路径。具体资源
    //     映射由真实实现根据 <paramref name="audioId"/> 查表完成，使逻辑层与
    //     资源命名解耦。
    //   - 高频 SFX（如每次命中）可通过 <see cref="PlaySfx"/> 触发，但真实实现
    //     应做限流/去重，避免同帧大量 AudioSource 创建（task 7.9 性能 profile）。
    //
    // 不变量：
    //   1. 逻辑层单向调用：逻辑层只调本端口发送音频意图，不从端口读规则状态。
    //   2. 不持有音频对象引用：接口参数只包含逻辑标量（audioId / 循环标志），
    //      不传 AudioClip / AudioSource / GameObject。
    //   3. 异步操作可取消：所有 async 方法接收 CancellationToken。
    //   4. 线程安全不要求：所有调用在 Unity 主线程的 Runtime 串行队列中执行。
    // ============================================================================

    /// <summary>
    /// 战斗音频意图端口（design.md Ports/IBattleAudioPort.cs）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:215）：</b>逻辑层通过本端口向表现层发送音频播放/停止意图。
    /// 表现层实现本端口，把意图翻译成实际音频播放，不回写规则状态。</para>
    ///
    /// <para><b>意图式设计：</b>本端口只表达 Play/Stop 意图，不表达具体资源路径。
    /// 真实实现根据 <c>audioId</c> 查表映射到 AudioClip，使逻辑层与资源命名解耦。</para>
    ///
    /// <para><b>异步与取消（任务 7.3）：</b>异步预加载接收 Runtime/Module CancellationToken。
    /// Runtime Token 在 Settling / Exit 时 Cancel（spec "Exit releases battle-owned state"）。</para>
    ///
    /// <para><b>线程安全：</b>不要求。所有调用在 Unity 主线程的 Runtime 串行队列中执行。</para>
    ///
    /// <para><b>实现：</b></para>
    /// <list type="bullet">
    /// <item><see cref="NullBattleAudioPort"/> —— 纯逻辑 Null/Test 实现，用于 EditMode 测试
    /// （design.md:215 "逻辑测试可使用空实现"）。</item>
    /// <item>TODO Phase 6 task 7.4/7.6：Unity 真实实现（由 BattlePresenter 组合实现），
    /// 用于 PlayMode/生产。真实实现将通过 <c>GameModule.Audio</c> 或直接 AudioSource 播放，
    /// 资源经 <c>GameModule.Resource.LoadAssetAsync</c> 加载并登记到
    /// <see cref="BattleRuntimeScope"/> 随局释放。当前 asmdef 未引用 FairyGUI，音频真实
    /// 实现不依赖 FairyGUI，可由后续 task 在 Presentation/ 目录接入。</item>
    /// </list>
    /// </remarks>
    public interface IBattleAudioPort
    {
        // ====================================================================
        // 生命周期与预加载
        // ====================================================================

        /// <summary>
        /// 异步预加载本端口所需的音频资源（BGM / SFX AudioClip）。
        /// </summary>
        /// <param name="cancellationToken">
        /// Runtime 或 Module 取消令牌。取消时抛出 <see cref="System.OperationCanceledException"/>。
        /// </param>
        /// <remarks>
        /// <para>由 BattleRuntimeFactory / BattleModule 在 Entering 阶段调用。
        /// 加载失败向上传播为结构化 <c>BattleOperationResult</c>，使部分初始化回滚
        /// （spec "Partial initialization is recoverable"）。</para>
        /// <para>Null 实现立即返回 <see cref="UniTask.CompletedTask"/>。</para>
        /// </remarks>
        UniTask PreloadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 播放战斗 BGM（背景音乐）。
        /// </summary>
        /// <param name="bgmId">BGM 标识（逻辑层定义的稳定字符串 ID，如 "battle_normal"）。</param>
        /// <param name="loop">是否循环播放。</param>
        /// <remarks>
        /// <para>由 BattleRuntime 在进入 Running 时调用。真实实现应支持淡入过渡。
        /// Null 实现为空操作。</para>
        /// </remarks>
        void PlayBgm(string bgmId, bool loop);

        /// <summary>
        /// 停止战斗 BGM。
        /// </summary>
        /// <param name="fadeOut">是否淡出（真实实现可做 fade）。</param>
        /// <remarks>
        /// <para>由 BattleRuntime 在 Settling / Exit 时调用。幂等：重复调用安全。
        /// Null 实现为空操作。</para>
        /// </remarks>
        void StopBgm(bool fadeOut);

        // ====================================================================
        // SFX 意图
        // ====================================================================

        /// <summary>
        /// 播放一次性 SFX（音效）。
        /// </summary>
        /// <param name="sfxId">
        /// SFX 标识（逻辑层定义的稳定字符串 ID，如 "hit_melee"、"enemy_die"、"unit_place"、"refresh"）。
        /// </param>
        /// <param name="volumeScale">音量缩放（0~1，默认 1）。</param>
        /// <remarks>
        /// <para>对应逻辑层攻击命中、敌人死亡、单位放置、刷新等事实的音频反馈。
        /// 真实实现应做限流/去重，避免同帧大量 AudioSource 创建（task 7.9 性能 profile）。
        /// Null 实现为空操作。</para>
        /// </remarks>
        void PlaySfx(string sfxId, float volumeScale = 1f);

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 停止本端口播放的全部音频，用于 Settling 静默清理与 Exit。
        /// </summary>
        /// <remarks>
        /// <para>由 BattleRuntime 在 Settling 或 BattleModule 在 Exit 时调用。
        /// 真实实现应停止 BGM、停止全部活动 SFX、释放临时 AudioSource。
        /// 幂等：重复调用安全。异步资源卸载由 <see cref="BattleRuntimeScope"/> 逆序释放。</para>
        /// </remarks>
        void Clear();
    }
}
