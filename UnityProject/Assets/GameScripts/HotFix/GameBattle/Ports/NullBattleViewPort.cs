using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.3：NullBattleViewPort —— 纯逻辑 Null/Test 视图端口实现
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / design.md:215）：
    //   IBattleViewPort 的纯逻辑空实现，用于 EditMode 测试与无表现逻辑闭环。
    //   所有方法为空操作或立即返回，不执行任何 IO、不创建任何 Unity 对象、
    //   不依赖 UnityEngine / FairyGUI。使战斗模拟在无表现环境下可运行与测试
    //   （design.md:9 "逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象"）。
    //
    // 来源证据：
    //   - design.md:215 "逻辑测试可使用空实现"
    //   - design.md:9 "逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象或
    //     Time.deltaTime；表现通过端口和 Presenter 同步"
    //   - task 7.3 要求：每个端口至少具有纯逻辑 Null/Test 实现
    //
    // 使用方式：
    //   - EditMode 测试：BattleRuntimeFactory 注入 NullBattleViewPort，使逻辑闭环
    //     不依赖 Unity 表现层。
    //   - 无表现逻辑验证：Phase 1~5 的黄金轨迹对照运行使用 Null 实现。
    //   - 生产环境：由 Unity/FairyGUI 真实实现替换（TODO task 7.4/7.6）。
    //
    // 不变量：
    //   1. 所有同步方法为空操作（no-op）。
    //   2. 所有异步方法立即返回 CompletedTask，不执行 IO。
    //   3. 不持有任何状态或引用。
    //   4. 不依赖 UnityEngine / FairyGUI / YooAsset。
    //   5. 幂等：Clear 可重复调用。
    // ============================================================================

    /// <summary>
    /// <see cref="IBattleViewPort"/> 的纯逻辑 Null/Test 实现，用于 EditMode 测试与无表现逻辑闭环。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:215）：</b>所有方法为空操作或立即返回，不执行任何 IO。
    /// 使战斗模拟在无表现环境下可运行与测试。</para>
    ///
    /// <para><b>使用场景：</b></para>
    /// <list type="bullet">
    /// <item>EditMode 测试：注入本类型使逻辑闭环不依赖 Unity 表现层。</item>
    /// <item>无表现逻辑验证：Phase 1~5 的黄金轨迹对照运行使用本类型。</item>
    /// <item>生产环境：由 Unity/FairyGUI 真实实现替换（TODO task 7.4/7.6）。</item>
    /// </list>
    ///
    /// <para><b>不依赖 Unity：</b>本类型不引用 UnityEngine / FairyGUI / YooAsset，
    /// 可在纯逻辑 EditMode 测试程序集中使用。</para>
    /// </remarks>
    internal sealed class NullBattleViewPort : IBattleViewPort
    {
        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>Null 实现：立即返回 <see cref="UniTask.CompletedTask"/>，不执行任何 IO。</remarks>
        public UniTask PreloadAsync(
            IReadOnlyList<string> enemyResourceAddresses,
            IReadOnlyList<string> generalResourceAddresses,
            IReadOnlyList<string> generalPartWords)
        {
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnBattleStarted(int maxRounds, int playerMaxHealth, int opponentMaxHealth)
        {
            // 空操作：不创建任何表现对象。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnBattleFinished(bool playerWin, int resultStar)
        {
            // 空操作：不播放结算动画。
        }

        // ====================================================================
        // 实体生成与回收
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作，不创建敌人表现对象。</remarks>
        public void OnEnemySpawned(EnemySpawnViewData dto)
        {
            // 空操作：不创建敌人表现对象。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作，不创建伤害飘字表现。</remarks>
        public void OnEnemyRemoved(int runtimeId, bool playDeathEffect)
        {
            // 空操作：不销毁敌人表现对象。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作，不创建伤害飘字表现。</remarks>
        public void OnEnemyDamaged(EnemyDamageViewData dto)
        {
            // 空操作：纯逻辑运行不创建伤害飘字。
        }

        public void OnBossSkillIntent(int runtimeId, string animationKey, bool active)
        {
            // 空操作：纯逻辑运行不创建 Boss 表现。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnUnitPlaced(int runtimeId, bool isPlayerSide, int soldierType, int gridX, int gridY, int level)
        {
            // 空操作：不创建单位表现对象。
        }

        public void OnConfiguredUnitPlaced(UnitSpawnViewData dto)
        {
            // 空操作：纯逻辑运行不创建配置化单位表现。
        }

        public void OnBattleGeneralPartGlyphChanged(int slotId, bool isPlayerSide, int gridX, int gridY, string partWord)
        {
            // 空操作：纯逻辑运行不创建武将字字形表现。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnUnitRemoved(int runtimeId)
        {
            // 空操作：不销毁单位表现对象。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnUnitMoved(int runtimeId, int gridX, int gridY)
        {
            // 空操作：不移动单位表现对象。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnUnitLevelChanged(int runtimeId, int newLevel)
        {
            // 空操作：不更新等级表现。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnProjectileFired(int runtimeId, float fromX, float fromY, bool isPlayerSide)
        {
            // 空操作：不创建投射物表现对象。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnProjectileRemoved(int runtimeId)
        {
            // 空操作：不销毁投射物表现对象。
        }

        // ====================================================================
        // 状态同步
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnHealthChanged(bool isPlayerSide, int currentHealth, int maxHealth, int delta)
        {
            // 空操作：不更新血条 UI。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnGoldChanged(bool isPlayerSide, int currentGold, int delta)
        {
            // 空操作：不更新金币 UI。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnRoundChanged(int currentRound, int maxRounds)
        {
            // 空操作：不更新波次 UI。
        }

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。</remarks>
        public void OnHandUpdated(bool isPlayerSide, int handSlotCount)
        {
            // 空操作：不刷新手牌 UI。
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>Null 实现：空操作。幂等：可重复调用。</remarks>
        public void Clear()
        {
            // 空操作：无表现对象需清理。
        }
    }
}
