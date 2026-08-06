using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.5：DeckManager —— 牌组抽牌/消耗/补牌/刷新管理
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Deck/DeckManager.cs）：
    //   用注入随机源完成抽牌、消耗、补牌和刷新。Draw 返回均匀四兵分布的卡牌。
    //
    // 来源证据（DeckManager.js:1-379）：
    //   - constructor({ gameData, economy, randomSource = Math.random, ...,
    //       minimalMode = false })：构造注入随机源与最简模式开关
    //   - startGame()：初始化两侧手牌（drawHand）
    //   - drawText(side)：minimalMode=true 时从 BASE_SOLDIER_TEXTS（4 元素）均匀抽取
    //   - drawHand(side)：抽 handSize 张卡填充手牌
    //   - createCard(text, level, source)：创建新 UnitCard，nextCardId 递增
    //   - consume(side, slot)：消耗指定槽卡牌并补抽一张新卡
    //   - refresh(side)：刷新手牌（payRefresh + clearSlotsTwoPhase + 重抽填槽）
    //   - hand(side) / getCard(side, slot)：查询手牌
    //   - gameOver()：清空手牌与牌池
    //   - snapshot()：只读快照
    //
    // C# 移植决策（task 6.5 核心约束）：
    //   "不得直接消费完整 108 项牌池改变最简模式分布"
    //
    //   本类型只实现 minimalMode=true 路径：
    //     - DrawText 只从 DeckDefinitions.BaseSoldierTexts（4 元素）均匀抽取
    //     - 不实现 poolForSide / drawCardNoRepeat / injectShovel / copyGeneralChars /
    //       aiRearrange（这些是 108 牌池完整模式逻辑，本期延后）
    //     - 不持有 kO/SO 可变牌池（108 元素副本），最简模式无需运行时牌池
    //     - refresh 只做清槽 + 重抽填槽（玩家侧），AI 侧重排延后
    //
    //   完整 108 牌池逻辑在后续 Change 引入时再扩展，不预先实现空壳方法。
    //
    // 随机源注入（spec battle-simulation "Simulation is reproducible"）：
    //   构造注入 IRandomSource（确定性 SeededRandomSource），不使用全局 Math.random
    //   或 UnityEngine.Random。相同种子 + 相同调用序列 → 相同抽牌序列。
    //   对应 JS DeckManager.js:7 randomSource = Math.random（构造注入），
    //   C# 替换为强类型 IRandomSource 接口注入。
    //
    // 与配置的关系：
    //   从注入的 DeckConfigSnapshot 读取 handSize / defaultLevel / baseUnitCost。
    //   若配置为 null，fallback 到 DeckDefinitions 硬编码常量（黄金基线值）。
    //
    // 与 BattleEconomy 的关系：
    //   refresh 调用 BattleEconomy.PayRefresh 扣费。本类型只负责牌组管理，
    //   不持有金币余额副本。扣费失败时 refresh 返回失败结果，不修改手牌。
    //   （JS DeckManager.js:351-373 refresh 逻辑）
    //
    // 不变量：
    //   1. 确定性：相同 IRandomSource 序列 + 相同配置 → 相同抽牌序列。
    //   2. 均匀四兵：DrawText 只从 4 元素 BaseSoldierTexts 均匀抽取，不消费 108 牌池。
    //   3. 每局新建/销毁：由 BattleRuntimeFactory 构造，随 Runtime 销毁。
    //   4. 手牌满槽契约：startGame / consume / refresh 后手牌长度 = handSize。
    //   5. CardId 递增：每次 createCard 分配新 ID，不复用旧 ID。
    // ============================================================================

    /// <summary>
    /// 牌组管理器：用注入随机源完成抽牌、消耗、补牌和刷新。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Deck/DeckManager.cs）：</b>
    /// 对应还原工程 <c>DeckManager.js</c>，C# 移植只实现最简模式路径
    /// （minimalMode=true，均匀四兵牌池），不消费完整 108 牌池。</para>
    ///
    /// <para><b>核心约束（task 6.5）：</b>
    /// <b>不得直接消费完整 108 项牌池改变最简模式分布。</b>
    /// <see cref="DrawText"/> 只从 <see cref="DeckDefinitions.BaseSoldierTexts"/>
    /// （刀/弓/枪/骑 4 元素）均匀抽取。不实现 108 牌池的 poolForSide /
    /// drawCardNoRepeat / injectShovel / copyGeneralChars / aiRearrange 逻辑。</para>
    ///
    /// <para><b>确定性随机（spec battle-simulation "Simulation is reproducible"）：</b>
    /// 构造注入 <see cref="IRandomSource"/>（确定性 <see cref="SeededRandomSource"/>），
    /// 不使用全局 <c>Math.random</c> 或 <c>UnityEngine.Random</c>。
    /// 相同种子 + 相同调用序列 → 相同抽牌序列。</para>
    ///
    /// <para><b>每局新建/销毁：</b>
    /// 由 <c>BattleRuntimeFactory</c> 构造，随 Runtime 销毁。不跨局复用。</para>
    ///
    /// <para><b>手牌满槽契约：</b>
    /// <see cref="StartGame"/> / <see cref="Consume"/> / <see cref="Refresh"/> 后
    /// 手牌长度 = <c>handSize</c>。</para>
    /// </remarks>
    internal sealed class DeckManager
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[DeckManager]";

        // ====================================================================
        // 注入依赖（不可变）
        // ====================================================================

        /// <summary>
        /// 注入的确定性随机源。
        /// <para>对应 JS <c>DeckManager.js:7 randomSource</c>。
        /// C# 替换为强类型 <see cref="IRandomSource"/> 接口注入，
        /// 不使用全局 <c>Math.random</c>。</para>
        /// </summary>
        private readonly IRandomSource _randomSource;

        /// <summary>
        /// 手牌大小（每侧手牌槽位数）。
        /// <para>由配置注入，fallback 到 <see cref="DeckDefinitions.HandSize"/>。</para>
        /// </summary>
        private readonly int _handSize;

        /// <summary>
        /// 默认卡牌等级。
        /// <para>由配置注入，fallback 到 <see cref="DeckDefinitions.DefaultLevel"/>。</para>
        /// </summary>
        private readonly int _defaultLevel;

        /// <summary>
        /// 基础单位消耗。
        /// <para>由配置注入，fallback 到 <see cref="DeckDefinitions.BaseUnitCost"/>。</para>
        /// </summary>
        private readonly int _baseUnitCost;

        // ====================================================================
        // 局内可变状态
        // ====================================================================

        /// <summary>
        /// 玩家侧手牌（可变列表）。
        /// <para>对应 JS <c>this.hands.player</c>。</para>
        /// </summary>
        private readonly List<UnitCard> _playerHand;

        /// <summary>
        /// 对手侧手牌（可变列表）。
        /// <para>对应 JS <c>this.hands.opponent</c>。</para>
        /// </summary>
        private readonly List<UnitCard> _opponentHand;

        /// <summary>
        /// 下一个卡牌 ID（递增分配）。
        /// <para>对应 JS <c>this.nextCardId</c>。每次 <see cref="CreateCard"/> 分配新 ID。</para>
        /// </summary>
        private int _nextCardId;

        /// <summary>
        /// 是否已调用 <see cref="StartGame"/>。
        /// <para>对应 JS <c>this.started</c>。<see cref="Refresh"/> 要求先 <see cref="StartGame"/>。</para>
        /// </summary>
        private bool _started;

        // ====================================================================
        // 只读查询
        // ====================================================================

        /// <summary>
        /// 是否已启动（调用 <see cref="StartGame"/>）。
        /// </summary>
        internal bool IsStarted => _started;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造牌组管理器，注入确定性随机源与配置。
        /// </summary>
        /// <param name="randomSource">
        /// 确定性随机源（非 null）。由 <c>BattleRuntimeFactory</c> 从
        /// <c>BattleLoadoutDto.RandomSeed</c> 构造 <see cref="SeededRandomSource"/> 注入。
        /// </param>
        /// <param name="deckConfig">
        /// 牌组配置快照（可为 null）。null 时 fallback 到
        /// <see cref="DeckDefinitions"/> 硬编码黄金基线值。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="randomSource"/> 为 null。</exception>
        /// <remarks>
        /// <para>由 <c>BattleRuntimeFactory</c> 在每次 Create 时构造新实例。
        /// 每局新建，不跨局复用。</para>
        /// </remarks>
        internal DeckManager(IRandomSource randomSource, DeckConfigSnapshot deckConfig)
        {
            _randomSource = randomSource
                ?? throw new ArgumentNullException(nameof(randomSource));

            // 从配置读取参数，null 时 fallback 到 DeckDefinitions 硬编码常量。
            _handSize = deckConfig?.HandSize > 0
                ? deckConfig.HandSize
                : DeckDefinitions.HandSize;
            _defaultLevel = deckConfig?.DefaultLevel > 0
                ? deckConfig.DefaultLevel
                : DeckDefinitions.DefaultLevel;
            _baseUnitCost = deckConfig?.BaseUnitCost > 0
                ? deckConfig.BaseUnitCost
                : DeckDefinitions.BaseUnitCost;

            _playerHand = new List<UnitCard>(_handSize);
            _opponentHand = new List<UnitCard>(_handSize);
            _nextCardId = 1;
            _started = false;
        }

        // ====================================================================
        // 启动
        // ====================================================================

        /// <summary>
        /// 启动牌组：初始化两侧手牌。
        /// </summary>
        /// <remarks>
        /// <para>对应 JS <c>DeckManager.js:58-66 startGame()</c>。</para>
        /// <para>最简模式不调用 <c>injectShovel</c>（108 牌池逻辑，本期延后）。</para>
        /// <para>幂等：重复调用安全，重置手牌与 nextCardId。</para>
        /// </remarks>
        internal void StartGame()
        {
            _started = true;
            _nextCardId = 1;
            _playerHand.Clear();
            _opponentHand.Clear();

            // 两侧各抽 handSize 张卡填充手牌。
            DrawHandInternal(_playerHand, isPlayerSide: true);
            DrawHandInternal(_opponentHand, isPlayerSide: false);
        }

        // ====================================================================
        // 抽牌
        // ====================================================================

        /// <summary>
        /// 从均匀四兵牌池抽取一个兵种文字。
        /// </summary>
        /// <param name="isPlayerSide">是否玩家侧（最简模式下两侧牌池相同，参数保留用于完整模式扩展）。</param>
        /// <returns>兵种文字（"刀"/"弓"/"枪"/"骑" 之一）。空池兜底返回 "刀"。</returns>
        /// <remarks>
        /// <para><b>对应 JS <c>DeckManager.js:98-108 drawText(side)</c> minimalMode 路径。</b></para>
        /// <para>最简模式：从 <see cref="DeckDefinitions.BaseSoldierTexts"/>（4 元素）
        /// 均匀抽取，不消费 108 牌池。对应 JS：</para>
        /// <code>
        /// if (this.minimalMode) {
        ///   const pool = BASE_SOLDIER_TEXTS;
        ///   const r = Math.max(0, Math.min(0.999999999, Number(this.randomSource()) || 0));
        ///   return pool[Math.floor(r * pool.length)] || '刀';
        /// }
        /// </code>
        /// <para><b>确定性：</b>使用注入的 <see cref="IRandomSource.NextUnit"/>
        /// 产生 [0, 1) 随机数，乘以 4 取 floor 得到均匀索引。</para>
        /// </remarks>
        internal string DrawText(bool isPlayerSide)
        {
            // 最简模式：只从 4 元素 BaseSoldierTexts 均匀抽取。
            // 不调用 poolForSide（108 牌池逻辑，本期不实现）。
            string[] pool = DeckDefinitions.BaseSoldierTexts;

            // 对应 JS: r = Math.max(0, Math.min(0.999999999, Number(this.randomSource()) || 0))
            // IRandomSource.NextUnit 已保证 [0, 1) 半开区间。
            float r = _randomSource.NextUnit();

            int index = (int)Math.Floor(r * pool.Length);
            // 钳制索引到合法范围，防止随机源异常导致越界。
            if (index < 0)
            {
                index = 0;
            }
            else if (index >= pool.Length)
            {
                index = pool.Length - 1;
            }

            // 空池兜底（理论不发生，防御性）。
            return pool[index] ?? "刀";
        }

        /// <summary>
        /// 创建一张新卡牌，分配递增 CardId。
        /// </summary>
        /// <param name="soldierText">兵种文字。</param>
        /// <param name="isPlayerSide">是否玩家侧。</param>
        /// <returns>新卡牌。</returns>
        /// <remarks>
        /// <para>对应 JS <c>DeckManager.js:110 createCard(text, level, source)</c>。</para>
        /// <para>cost = max(baseUnitCost, level)，最简模式下 = baseUnitCost = 1。</para>
        /// </remarks>
        internal UnitCard CreateCard(string soldierText, bool isPlayerSide)
        {
            int cost = Math.Max(_baseUnitCost, _defaultLevel);
            return new UnitCard(
                cardId: _nextCardId++,
                soldierText: soldierText,
                level: _defaultLevel,
                cost: cost,
                isPlayerSide: isPlayerSide);
        }

        /// <summary>
        /// 抽 handSize 张卡填充指定手牌列表。
        /// </summary>
        /// <param name="hand">待填充的手牌列表（会被清空后重填）。</param>
        /// <param name="isPlayerSide">是否玩家侧。</param>
        private void DrawHandInternal(List<UnitCard> hand, bool isPlayerSide)
        {
            hand.Clear();
            for (int i = 0; i < _handSize; i++)
            {
                string text = DrawText(isPlayerSide);
                hand.Add(CreateCard(text, isPlayerSide));
            }
        }

        // ====================================================================
        // 手牌查询
        // ====================================================================

        /// <summary>
        /// 获取指定侧手牌的只读快照副本。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家侧，false=对手侧。</param>
        /// <returns>手牌只读列表副本（修改副本不影响内部状态）。</returns>
        /// <remarks>
        /// <para>对应 JS <c>DeckManager.js:112 hand(side)</c>。
        /// C# 返回副本而非内部引用，避免外部修改内部状态。</para>
        /// </remarks>
        internal IReadOnlyList<UnitCard> GetHand(bool isPlayerSide)
        {
            List<UnitCard> hand = isPlayerSide ? _playerHand : _opponentHand;
            return hand.ToArray();
        }

        /// <summary>
        /// 获取指定侧指定槽位的卡牌。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家侧，false=对手侧。</param>
        /// <param name="slot">槽位索引（从 0 开始）。</param>
        /// <returns>卡牌；槽位非法或为空返回 null。</returns>
        /// <remarks>
        /// <para>对应 JS <c>DeckManager.js:113 getCard(side, slot)</c>。</para>
        /// </remarks>
        internal UnitCard? GetCard(bool isPlayerSide, int slot)
        {
            List<UnitCard> hand = isPlayerSide ? _playerHand : _opponentHand;
            if (slot < 0 || slot >= hand.Count)
            {
                return null;
            }
            return hand[slot];
        }

        // ====================================================================
        // 消耗与补牌
        // ====================================================================

        /// <summary>
        /// 消耗指定槽位的卡牌并补抽一张新卡。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家侧，false=对手侧。</param>
        /// <param name="slot">槽位索引（从 0 开始）。</param>
        /// <returns>被消耗的卡牌；槽位非法返回 null。</returns>
        /// <remarks>
        /// <para>对应 JS <c>DeckManager.js:375 consume(side, slot)</c>：</para>
        /// <code>
        /// const card = hand[slot];
        /// if (!card) return null;
        /// hand[slot] = this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent');
        /// return card;
        /// </code>
        /// <para>消耗后立即补抽一张新卡填充同一槽位，保持手牌满槽契约。</para>
        /// </remarks>
        internal UnitCard? Consume(bool isPlayerSide, int slot)
        {
            List<UnitCard> hand = isPlayerSide ? _playerHand : _opponentHand;
            if (slot < 0 || slot >= hand.Count)
            {
                return null;
            }

            UnitCard card = hand[slot];
            // 补抽一张新卡填充同一槽位。
            string newText = DrawText(isPlayerSide);
            hand[slot] = CreateCard(newText, isPlayerSide);
            return card;
        }

        // ====================================================================
        // 刷新
        // ====================================================================

        /// <summary>
        /// 刷新指定侧手牌（清槽 + 重抽填槽）。扣费由调用方（BattleInputController）经
        /// BattleEconomy.PayRefresh 完成，本方法只负责牌组操作。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家侧，false=对手侧。</param>
        /// <remarks>
        /// <para><b>对应 JS <c>DeckManager.js:351-373 refresh(side)</c> 玩家侧重抽路径。</b></para>
        /// <para>JS refresh 内部调用 economy.payRefresh 扣费，失败返回失败结果。
        /// C# 移植将扣费职责保留在 BattleInputController（task 6.7 原子事务编排），
        /// 本方法只做牌组操作：清槽 + 重抽填槽。</para>
        /// <para><b>AI 侧重排（aiRearrange）本期延后</b>：最简模式不实现 AI 难度分桶
        /// 与铲前置排序，两侧均走简单重抽路径。</para>
        /// <para><b>未启动异常：</b>未调用 <see cref="StartGame"/> 即刷新抛出
        /// <see cref="InvalidOperationException"/>（对应 JS <c>refresh</c> 的 throw）。</para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">未调用 <see cref="StartGame"/>。</exception>
        internal void Refresh(bool isPlayerSide)
        {
            if (!_started)
            {
                throw new InvalidOperationException(
                    "DeckManager.StartGame() 必须在 Refresh 之前调用");
            }

            List<UnitCard> hand = isPlayerSide ? _playerHand : _opponentHand;

            // 两阶段清除：清空全部槽位（对应 JS NY clearSlotsTwoPhase）。
            // 最简模式无 locked 概念（lock 是 108 牌池完整模式语义），直接清空。
            hand.Clear();

            // 重抽填槽（玩家侧路径，对应 JS refresh 的 while 循环重抽）。
            DrawHandInternal(hand, isPlayerSide);
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 战斗结束清理：清空两侧手牌，标记未启动。
        /// </summary>
        /// <remarks>
        /// <para>对应 JS <c>DeckManager.js:377 gameOver()</c>。</para>
        /// <para>幂等：重复调用安全。不释放随机源（随机源归 BattleRuntime 所有）。</para>
        /// </remarks>
        internal void GameOver()
        {
            _started = false;
            _playerHand.Clear();
            _opponentHand.Clear();
        }

        // ====================================================================
        // 快照
        // ====================================================================

        /// <summary>
        /// 生成两侧手牌只读快照。
        /// </summary>
        /// <returns>手牌快照结构。</returns>
        /// <remarks>
        /// <para>对应 JS <c>DeckManager.js:378 snapshot()</c>。</para>
        /// </remarks>
        internal DeckSnapshot Snapshot()
        {
            return new DeckSnapshot(
                player: _playerHand.ToArray(),
                opponent: _opponentHand.ToArray());
        }
    }

    // ========================================================================
    // 手牌快照
    // ========================================================================

    /// <summary>
    /// 两侧手牌只读快照。
    /// </summary>
    /// <remarks>
    /// <para>对应 JS <c>DeckManager.snapshot()</c> 返回的 <c>{ player, opponent }</c>。</para>
    /// <para>不可变：返回后与 DeckManager 内部状态解耦，修改快照不影响内部手牌。</para>
    /// </remarks>
    internal readonly struct DeckSnapshot
    {
        /// <summary>玩家侧手牌快照。</summary>
        public readonly IReadOnlyList<UnitCard> Player;

        /// <summary>对手侧手牌快照。</summary>
        public readonly IReadOnlyList<UnitCard> Opponent;

        /// <summary>构造手牌快照。</summary>
        internal DeckSnapshot(IReadOnlyList<UnitCard> player, IReadOnlyList<UnitCard> opponent)
        {
            Player = player ?? Array.Empty<UnitCard>();
            Opponent = opponent ?? Array.Empty<UnitCard>();
        }
    }
}
