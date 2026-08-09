using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.3：NullBattleVfxPort —— 纯逻辑 Null/Test 特效端口实现
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / design.md:216）：
    //   IBattleVfxPort 的纯逻辑空实现，用于 EditMode 测试与无表现逻辑闭环。
    //   所有方法为空操作或立即返回，不执行任何 IO、不创建任何 ParticleSystem、
    //   不依赖 UnityEngine.VFX / ParticleSystem。使战斗模拟在无特效环境下可运行与测试。
    //
    // 来源证据：
    //   - design.md:216 "战斗特效意图端口；逻辑测试可使用空实现"
    //   - design.md:9 "逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象"
    //   - task 7.3 要求：每个端口至少具有纯逻辑 Null/Test 实现
    //
    // 使用方式：
    //   - EditMode 测试：BattleRuntimeFactory 注入 NullBattleVfxPort。
    //   - 无表现逻辑验证：Phase 1~5 的黄金轨迹对照运行使用 Null 实现。
    //   - 生产环境：由 Unity 真实实现替换（TODO task 7.4/7.6）。
    //
    // 不变量：
    //   1. 所有同步方法为空操作（no-op）。
    //   2. 所有异步方法立即返回 CompletedTask，不执行 IO。
    //   3. 不持有任何状态或引用。
    //   4. 不依赖 UnityEngine / FairyGUI / YooAsset。
    //   5. 幂等：Clear 可重复调用。
    // ============================================================================

    /// <summary>
    /// <see cref="IBattleVfxPort"/> 的纯逻辑 Null/Test 实现，用于 EditMode 测试与无表现逻辑闭环。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:216）：</b>所有方法为空操作或立即返回，不执行任何 IO。
    /// 使战斗模拟在无特效环境下可运行与测试。</para>
    ///
    /// <para><b>使用场景：</b></para>
    /// <list type="bullet">
    /// <item>EditMode 测试：注入本类型使逻辑闭环不依赖 Unity 特效。</item>
    /// <item>无表现逻辑验证：Phase 1~5 的黄金轨迹对照运行使用本类型。</item>
    /// <item>生产环境：由 Unity 真实实现替换（TODO task 7.4/7.6）。</item>
    /// </list>
    ///
    /// <para><b>不依赖 Unity：</b>本类型不引用 UnityEngine / FairyGUI / YooAsset。</para>
    /// </remarks>
    internal sealed class NullBattleVfxPort : IBattleVfxPort
    {
        // ====================================================================
        // 生命周期与预加载
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>Null 实现：立即返回 <see cref="UniTask.CompletedTask"/>，不执行任何 IO。</remarks>
        public UniTask PreloadAsync(CancellationToken cancellationToken)
        {
            // 取消令牌已取消时保留取消异常语义。
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        // ====================================================================
        // 特效意图
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void PlayHitEffect(string vfxId, float x, float y)
        {
            // 空操作：不播放命中特效。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void PlayDeathEffect(string vfxId, float x, float y)
        {
            // 空操作：不播放死亡特效。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void PlayAttackEffect(string vfxId, float x, float y)
        {
            // 空操作：不播放攻击特效。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void PlaySpawnEffect(string vfxId, int gridX, int gridY)
        {
            // 空操作：不播放生成/放置特效。
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。幂等：可重复调用。</remarks>
        public void Clear()
        {
            // 空操作：无特效对象需清理。
        }
    }
}
