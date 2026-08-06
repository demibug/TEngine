namespace GameBattle
{
    // ============================================================================
    // 任务 6.5：UnitCard —— 不可变牌值对象
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Deck/UnitCard.cs）：
    //   不可变牌值对象，表示一张可被消耗、放置的手牌卡。
    //   对应还原工程 UnitCard.js，但 C# 移植为 readonly struct（不可变值类型），
    //   而非 JS 的可变 class（JS UnitCard 有 locked 字段可变，C# 移植将 lock 语义
    //   外移到 DeckManager 的槽位状态，保持卡牌本身不可变）。
    //
    // 来源证据（UnitCard.js:1-9）：
    //   class UnitCard {
    //     constructor({ id, text, level = 1, cost = level, source = 'deck' } = {}) {
    //       this.id = id; this.text = text; this.level = level; this.cost = cost;
    //       this.source = source; this.locked = false;
    //     }
    //     clone() { return new UnitCard({ ... }); }
    //     toJSON() { return { id, text, level, cost, source, locked }; }
    //   }
    //
    //   C# 移植决策：
    //     - id → CardId（int，由 DeckManager 递增分配）
    //     - text → SoldierText（string，兵种文字，如 "刀"/"弓"/"枪"/"骑"）
    //     - level → Level（int，等级，本期最简模式固定 1）
    //     - cost → Cost（int，消耗，最简模式 = baseUnitCost = 1）
    //     - source → SourceSide（bool，true=玩家侧，false=对手侧；对应 JS 'player'/'opponent'）
    //     - locked → 不放入 UnitCard，外移到 DeckManager 的槽位状态（lock 是槽位属性，
    //       不是卡牌本身属性；不可变卡牌无法被 lock/unlock）
    //
    // 不可变性：
    //   1. readonly struct，全部字段 readonly，构造后不可修改。
    //   2. 不持有可变集合或可变引用。
    //   3. JS 的 clone() 在 C# 中不必要（struct 是值类型，赋值即拷贝）。
    //
    // 与 DeckDefinitions 的关系：
    //   UnitCard 只定义卡牌数据结构，不含牌池定义或抽牌逻辑。
    //   牌池定义见 DeckDefinitions，抽牌/消耗/补牌见 DeckManager。
    //
    // 最简模式约束（task 6.5 / spec 6.5）：
    //   本期只支持均匀四兵最简牌组（刀/弓/枪/骑），不消费完整 108 项牌池。
    //   SoldierText 只会是 "刀"/"弓"/"枪"/"骑" 之一（由 DeckManager.DrawText 保证）。
    // ============================================================================

    /// <summary>
    /// 不可变牌值对象，表示一张可被消耗、放置的手牌卡。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Deck/UnitCard.cs）：</b>
    /// 对应还原工程 <c>UnitCard.js</c>，C# 移植为 readonly struct（不可变值类型）。
    /// JS 原版为可变 class（含 <c>locked</c> 字段），C# 移植将 lock 语义外移到
    /// <see cref="DeckManager"/> 的槽位状态，保持卡牌本身不可变。</para>
    ///
    /// <para><b>不可变性：</b>
    /// 全部字段为 readonly，构造后不可修改。struct 值类型赋值即拷贝，
    /// 不需要 JS 原版的 <c>clone()</c> 方法。</para>
    ///
    /// <para><b>最简模式约束（task 6.5 / spec 6.5）：</b>
    /// 本期只支持均匀四兵最简牌组（刀/弓/枪/骑），<see cref="SoldierText"/> 只会是
    /// "刀"/"弓"/"枪"/"骑" 之一（由 <see cref="DeckManager.DrawText"/> 保证）。
    /// 不消费完整 108 项牌池。</para>
    /// </remarks>
    internal readonly struct UnitCard
    {
        // ====================================================================
        // 不可变字段
        // ====================================================================

        /// <summary>
        /// 卡牌唯一标识（由 <see cref="DeckManager"/> 递增分配）。
        /// <para>对应 JS <c>UnitCard.id</c>。每张新卡分配一个新 ID，
        /// 消耗后补牌的卡获得新 ID，不复用旧 ID。</para>
        /// </summary>
        public readonly int CardId;

        /// <summary>
        /// 兵种文字（"刀"/"弓"/"枪"/"骑" 之一）。
        /// <para>对应 JS <c>UnitCard.text</c>。最简模式下只为基础四兵，
        /// 不含铲/农/武将字（task 6.5 约束）。</para>
        /// <para>由 <see cref="DeckManager.DrawText"/> 从 <see cref="DeckDefinitions.BaseSoldierTexts"/>
        /// 均匀抽取确定。</para>
        /// </summary>
        public readonly string SoldierText;

        /// <summary>
        /// 卡牌等级。本期最简模式固定为 1
        /// （<see cref="DeckDefinitions.DefaultLevel"/>）。
        /// <para>对应 JS <c>UnitCard.level</c>。升级/合并在本期延后（task 6.4 排除）。</para>
        /// </summary>
        public readonly int Level;

        /// <summary>
        /// 卡牌消耗（招募费用）。
        /// <para>对应 JS <c>UnitCard.cost</c>（默认 <c>= level</c>）。
        /// 最简模式下由 <see cref="DeckDefinitions.BaseUnitCost"/> 确定（=1）。</para>
        /// </summary>
        public readonly int Cost;

        /// <summary>
        /// 是否玩家侧卡牌。true=玩家侧，false=对手侧。
        /// <para>对应 JS <c>UnitCard.source</c>（'player'/'opponent'）。
        /// C# 移植为 bool 值类型，避免字符串比较。</para>
        /// </summary>
        public readonly bool IsPlayerSide;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造不可变牌。
        /// </summary>
        /// <param name="cardId">卡牌唯一标识。</param>
        /// <param name="soldierText">兵种文字（"刀"/"弓"/"枪"/"骑"）。</param>
        /// <param name="level">等级（最简模式固定 1）。</param>
        /// <param name="cost">消耗（最简模式 = <see cref="DeckDefinitions.BaseUnitCost"/>）。</param>
        /// <param name="isPlayerSide">是否玩家侧。</param>
        internal UnitCard(int cardId, string soldierText, int level, int cost, bool isPlayerSide)
        {
            CardId = cardId;
            SoldierText = soldierText ?? string.Empty;
            Level = level;
            Cost = cost;
            IsPlayerSide = isPlayerSide;
        }

        // ====================================================================
        // 值语义
        // ====================================================================

        /// <summary>
        /// 判断两张卡是否相等。全部字段相同才相等。
        /// </summary>
        public bool Equals(UnitCard other)
            => CardId == other.CardId
               && SoldierText == other.SoldierText
               && Level == other.Level
               && Cost == other.Cost
               && IsPlayerSide == other.IsPlayerSide;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is UnitCard other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CardId;
                hash = (hash * 397) ^ (SoldierText?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ Level;
                hash = (hash * 397) ^ Cost;
                hash = (hash * 397) ^ IsPlayerSide.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// 相等运算符。
        /// </summary>
        public static bool operator ==(UnitCard left, UnitCard right) => left.Equals(right);

        /// <summary>
        /// 不等运算符。
        /// </summary>
        public static bool operator !=(UnitCard left, UnitCard right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"[UnitCard Id={CardId} Text={SoldierText} Lv={Level} Cost={Cost} Player={IsPlayerSide}]";
    }
}
