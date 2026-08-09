namespace GameBattle
{
    // ============================================================================
    // 征兵领域：RecruitDefinitions —— 四兵池与默认参数的规范化定义
    // ----------------------------------------------------------------------------
    // 职责（修复阶段 P0"删除旧牌池状态"）：
    //   替代旧 Deck/DeckDefinitions.cs，只保留四兵 fallback、默认等级、默认槽数、
    //   最大等级。不再包含手牌、消费、补牌语义。
    //
    // 与配置的关系：
    //   DeckConfigSnapshot（Luban deck 表）仍作为配置输入提供征兵池和待上场槽数量，
    //   本类型提供代码内硬编码的 fallback 常量（黄金基线值）。
    //
    // 不变量：
    //   1. 只定义均匀四兵池与默认参数，不含手牌/消费/补牌语义。
    //   2. BaseSoldierTexts 为 4 元素 ['刀','弓','枪','骑']。
    //   3. 常量不可变（readonly/const）。
    // ============================================================================

    /// <summary>
    /// 征兵四兵池与默认参数（fallback 常量，替代旧 DeckDefinitions）。
    /// </summary>
    internal static class RecruitDefinitions
    {
        /// <summary>
        /// 均匀四兵基础兵字表（4 元素）。
        /// </summary>
        internal static readonly string[] BaseSoldierTexts =
        {
            "刀",
            "弓",
            "枪",
            "骑",
        };

        /// <summary>每侧待上场槽数量（fallback = 5）。</summary>
        internal const int ReserveSlotCount = 5;

        /// <summary>征兵生成单位默认等级（fallback = 1）。</summary>
        internal const int DefaultLevel = 1;

        /// <summary>最大等级（合并上限，fallback = 3）。</summary>
        internal const int MaxLevel = 3;
    }
}
