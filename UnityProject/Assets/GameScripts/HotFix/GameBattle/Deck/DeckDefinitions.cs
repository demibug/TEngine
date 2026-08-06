namespace GameBattle
{
    // ============================================================================
    // 任务 6.5：DeckDefinitions —— 均匀四兵最简牌组定义
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Deck/DeckDefinitions.cs）：
    //   手牌大小和最简四兵牌池的规范化定义。NOT the full 108-card pool.
    //
    // 来源证据（DeckDefinitions.js:1-64）：
    //   - BASE_POOL = Object.freeze(['刀', '弓', '枪', '骑'])  // 4 元素回退牌池
    //   - BASE_SOLDIER_TEXTS = BASE_POOL  // 最简战斗模式基础兵字表
    //   - DeckDefinitions = Object.freeze({
    //       handSize: 5,            // s4.fe
    //       basePool: DECK_POOL,    // 108 元素牌池（完整模式）
    //       defaultLevel: 1,
    //       baseUnitCost: 1,
    //       maxLevel: 3,
    //     })
    //
    //   JS DeckManager minimalMode=true 时 drawText 只从 BASE_SOLDIER_TEXTS
    //   （刀/弓/枪/骑 4 元素）抽取，不抽农/铲/武将字，绕过 108 元素 poolForSide
    //   （DeckManager.js:99-103）。
    //
    // C# 移植决策（task 6.5 核心约束）：
    //   "不得直接消费完整 108 项牌池改变最简模式分布"
    //
    //   本类型只定义最简四兵牌池，不定义 108 元素完整牌池。
    //   - BaseSoldierTexts = ['刀', '弓', '枪', '骑']  // 均匀四兵，4 元素
    //   - HandSize = 5
    //   - DefaultLevel = 1
    //   - BaseUnitCost = 1
    //   - MaxLevel = 3
    //
    //   不引入 DECK_POOL（108 元素）常量，不引入 loadDeckPool() 函数。
    //   完整 108 牌池在后续 Change 引入时再扩展，不预先定义。
    //
    // 与配置的关系：
    //   DeckConfigSnapshot（task 3.3）包含从 Luban/JSON 读取的牌组配置字段
    //   （MinimalMode/BaseSoldierTexts/HandSize/DefaultLevel/BaseUnitCost）。
    //   本类型为代码内硬编码的规范化常量，与 DeckConfigSnapshot 黄金基线等价：
    //     - MinimalMode = true
    //     - BaseSoldierTexts = ['刀','弓','枪','骑']
    //     - HandSize = 5
    //     - DefaultLevel = 1
    //     - BaseUnitCost = 1
    //
    //   DeckManager 从注入的 DeckConfigSnapshot 读取运行时配置值，本类型提供
    //   代码内常量供默认构造与测试参照。两者字段等价通过配置快照对照验证。
    //
    // 不变量：
    //   1. 只定义均匀四兵最简牌池，不定义 108 元素完整牌池。
    //   2. BaseSoldierTexts 为 4 元素 ['刀','弓','枪','骑']，不含铲/农/武将字。
    //   3. 常量不可变（readonly/const）。
    // ============================================================================

    /// <summary>
    /// 均匀四兵最简牌组定义（手牌大小、基础兵字表、默认等级、基础消耗）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Deck/DeckDefinitions.cs）：</b>
    /// 手牌大小和最简四兵牌池的规范化定义。对应还原工程 <c>DeckDefinitions.js</c>
    /// 的 <c>BASE_POOL</c> / <c>BASE_SOLDIER_TEXTS</c> 与 <c>DeckDefinitions</c> 对象。</para>
    ///
    /// <para><b>核心约束（task 6.5）：</b>
    /// <b>不得直接消费完整 108 项牌池改变最简模式分布。</b>
    /// 本类型只定义均匀四兵最简牌池（刀/弓/枪/骑 4 元素等概率），不定义 108 元素
    /// 完整牌池（含铲/农/武将字）。完整 108 牌池在后续 Change 引入时再扩展。</para>
    ///
    /// <para><b>与 <see cref="DeckConfigSnapshot"/> 的关系：</b>
    /// <see cref="Config.DeckConfigSnapshot"/> 包含从 Luban/JSON 读取的牌组配置字段。
    /// 本类型为代码内硬编码的规范化常量，与黄金基线等价。
    /// <see cref="DeckManager"/> 从注入的 <c>DeckConfigSnapshot</c> 读取运行时配置值，
    /// 本类型提供代码内常量供默认构造与测试参照。</para>
    ///
    /// <para><b>均匀分布（spec 6.5）：</b>
    /// <see cref="BaseSoldierTexts"/> 为 4 元素 ['刀','弓','枪','骑']，
    /// <see cref="DeckManager.DrawText"/> 从中均匀抽取（4 选 1 等概率），
    /// 不按 108 牌池权重分布（刀21/弓19/枪18/骑17/铲11+武将22）。</para>
    /// </remarks>
    internal static class DeckDefinitions
    {
        // ====================================================================
        // 均匀四兵基础牌池
        // ====================================================================

        /// <summary>
        /// 最简四兵基础牌字表（均匀分布，4 元素）。
        /// </summary>
        /// <remarks>
        /// <para>对应 JS <c>DeckDefinitions.BASE_POOL</c> / <c>BASE_SOLDIER_TEXTS</c>
        /// （<c>DeckDefinitions.js:10</c>：<c>Object.freeze(['刀', '弓', '枪', '骑'])</c>）。</para>
        ///
        /// <para><b>均匀分布：</b>4 元素等概率抽取，不按 108 牌池权重分布。
        /// <see cref="DeckManager.DrawText"/> 从此数组按 <c>floor(random * 4)</c>
        /// 均匀抽取（对应 <c>DeckManager.js:101-103</c> minimalMode 路径）。</para>
        ///
        /// <para><b>不消费 108 牌池（task 6.5 核心约束）：</b>
        /// 不含铲('铲')、农('农')、武将字。完整 108 牌池在后续 Change 引入时再扩展，
        /// 本类型不预先定义 <c>DECK_POOL</c> 常量。</para>
        /// </remarks>
        internal static readonly string[] BaseSoldierTexts =
        {
            "刀",
            "弓",
            "枪",
            "骑",
        };

        // ====================================================================
        // 牌组参数
        // ====================================================================

        /// <summary>
        /// 手牌大小（每侧手牌槽位数）。
        /// <para>对应 JS <c>DeckDefinitions.handSize = 5</c>（<c>s4.fe</c>）。
        /// 黄金基线值 = 5，与 <c>DeckConfigSnapshot.HandSize</c> 等价。</para>
        /// </summary>
        internal const int HandSize = 5;

        /// <summary>
        /// 默认卡牌等级。
        /// <para>对应 JS <c>DeckDefinitions.defaultLevel = 1</c>。
        /// 最简模式下所有卡牌等级固定为 1（升级/合并本期延后，task 6.4 排除）。</para>
        /// </summary>
        internal const int DefaultLevel = 1;

        /// <summary>
        /// 基础单位消耗（招募费用）。
        /// <para>对应 JS <c>DeckDefinitions.baseUnitCost = 1</c>。
        /// 最简模式下每张卡消耗 = 1（与 <c>DeckConfigSnapshot.BaseUnitCost</c> 等价）。</para>
        /// </summary>
        internal const int BaseUnitCost = 1;

        /// <summary>
        /// 最大等级。
        /// <para>对应 JS <c>DeckDefinitions.maxLevel = 3</c>。
        /// 本期升级/合并延后（task 6.4），此常量仅供参照。</para>
        /// </summary>
        internal const int MaxLevel = 3;
    }
}
