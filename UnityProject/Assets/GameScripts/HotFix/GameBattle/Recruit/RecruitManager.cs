using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 征兵领域：RecruitManager —— 随机生成一整批 1 级四兵
    // ----------------------------------------------------------------------------
    // 职责（最终方案"计划新增文件"Recruit/RecruitManager.cs）：
    //   只负责随机生成一整批 1 级刀/弓/枪/骑。不保存手牌和槽位状态，不负责上场。
    //
    // 与旧 DeckManager 的关系：
    //   DeckManager 由 RecruitManager 替代。旧 DeckManager 的"随机四兵算法"
    //   （从 RecruitDefinitions.BaseSoldierTexts 均匀抽取）迁移到本类型。手牌/补牌/
    //   刷新语义删除——征兵只生成一批 1 级单位，由 UnitSlotBoard.ReplaceReserve
    //   填满待上场槽。
    //
    // 确定性随机（spec "Simulation is reproducible"）：
    //   构造注入 IRandomSource（确定性 SeededRandomSource），不使用全局随机源。
    //   相同种子 + 相同调用序列 → 相同征兵批次。
    //
    // 不变量：
    //   1. 只生成 1 级单位（开局与征兵都生成 1 级）。
    //   2. 从 RecruitDefinitions.BaseSoldierTexts（4 元素）均匀抽取。
    //   3. 不保存手牌与槽位状态，不负责上场（上场由 UI 拖拽 → DropUnit 完成）。
    //   4. 每局新建/销毁：由 BattleRuntimeFactory 构造，随 Runtime 销毁。
    // ============================================================================

    /// <summary>
    /// 征兵服务：随机生成一整批 1 级刀/弓/枪/骑。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（最终方案）：</b>只负责随机生成一整批 1 级四兵，不保存手牌和
    /// 槽位状态，不负责上场。替代旧 <see cref="DeckManager"/> 的抽牌语义。</para>
    /// <para><b>单位 ID：</b>由注入的 <see cref="UnitSlotBoard"/> 分配（与待上场槽的
    /// 单位共用同一局内 ID 序列）。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 UnitSlotBoard /
    /// BattleInputController 使用。</para>
    /// </remarks>
    internal sealed class RecruitManager
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[RecruitManager]";

        // ====================================================================
        // 注入依赖
        // ====================================================================

        /// <summary>确定性随机源（非 null）。从 RecruitDefinitions.BaseSoldierTexts 均匀抽取。</summary>
        private readonly IRandomSource _randomSource;

        /// <summary>槽位面板（非 null）。征兵批次单位 ID 由它分配。</summary>
        private readonly UnitSlotBoard _slotBoard;

        /// <summary>待上场槽数量（每侧）。</summary>
        private readonly int _reserveSlotCount;

        /// <summary>仅玩家侧参与加权抽取的武将字条目。</summary>
        private readonly IReadOnlyList<GeneralPartRecruitEntry> _partRecruitEntries;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造征兵服务。
        /// </summary>
        /// <param name="randomSource">确定性随机源（非 null）。</param>
        /// <param name="slotBoard">槽位面板（非 null），供分配单位 ID。</param>
        /// <param name="reserveSlotCount">每侧待上场槽数量（<=0 时回退 RecruitDefinitions.ReserveSlotCount）。</param>
        internal RecruitManager(
            IRandomSource randomSource,
            UnitSlotBoard slotBoard,
            int reserveSlotCount,
            IReadOnlyList<GeneralPartRecruitEntry> partRecruitEntries = null)
        {
            _randomSource = randomSource ?? throw new ArgumentNullException(nameof(randomSource));
            _slotBoard = slotBoard ?? throw new ArgumentNullException(nameof(slotBoard));
            _reserveSlotCount = reserveSlotCount > 0 ? reserveSlotCount : RecruitDefinitions.ReserveSlotCount;
            _partRecruitEntries = partRecruitEntries ?? Array.Empty<GeneralPartRecruitEntry>();
        }

        // ====================================================================
        // 生成征兵批次
        // ====================================================================

        /// <summary>
        /// 生成一整批 1 级四兵（数量 = 待上场槽数量）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <returns>新生成的 1 级单位列表（长度 = 待上场槽数量）。</returns>
        /// <remarks>
        /// <para><b>均匀四兵（最终方案）：</b>每张从 <see cref="RecruitDefinitions.BaseSoldierTexts"/>
        /// （刀/弓/枪/骑 4 元素）均匀抽取，不消费 108 牌池。</para>
        /// <para><b>确定性：</b>使用注入的 <see cref="IRandomSource"/>，相同种子 + 相同
        /// 调用序列 → 相同批次。</para>
        /// </remarks>
        internal IReadOnlyList<BattleUnit> GenerateBatch(bool isPlayerSide)
        {
            var batch = new List<BattleUnit>(_reserveSlotCount);
            for (int i = 0; i < _reserveSlotCount; i++)
            {
                batch.Add(GenerateSingle(isPlayerSide));
            }

            return batch;
        }

        /// <summary>
        /// 生成单个 1 级单位（从四兵池均匀抽取）。
        /// </summary>
        private BattleUnit GenerateSingle(bool isPlayerSide)
        {
            string[] pool = RecruitDefinitions.BaseSoldierTexts;
            int totalWeight = pool.Length;
            if (isPlayerSide)
            {
                for (int i = 0; i < _partRecruitEntries.Count; i++)
                {
                    totalWeight += Math.Max(0, _partRecruitEntries[i].Weight);
                }
            }

            float r = _randomSource.NextUnit();
            int selected = (int)Math.Floor(r * totalWeight);
            if (selected < 0)
            {
                selected = 0;
            }
            else if (selected >= totalWeight)
            {
                selected = totalWeight - 1;
            }

            if (selected >= pool.Length && isPlayerSide)
            {
                int cursor = pool.Length;
                for (int i = 0; i < _partRecruitEntries.Count; i++)
                {
                    GeneralPartRecruitEntry entry = _partRecruitEntries[i];
                    cursor += Math.Max(0, entry.Weight);
                    if (selected < cursor)
                    {
                        return BattleUnit.CreateGeneralPart(
                            _slotBoard.AllocateUnitId(), true, entry.PartText);
                    }
                }
            }

            string text = pool[selected] ?? "刀";
            SoldierType type = TextToSoldierType(text);

            return new BattleUnit(
                unitId: _slotBoard.AllocateUnitId(),
                side: isPlayerSide,
                kind: UnitKind.Soldier,
                soldierType: type,
                soldierText: text,
                level: RecruitDefinitions.DefaultLevel);
        }

        /// <summary>兵种文字 → SoldierType（与 RecruitDefinitions.BaseSoldierTexts 顺序一致）。</summary>
        internal static SoldierType TextToSoldierType(string text)
        {
            switch (text)
            {
                case "刀":
                    return SoldierType.Knife;
                case "弓":
                    return SoldierType.Bow;
                case "枪":
                    return SoldierType.Spear;
                case "骑":
                    return SoldierType.Cavalry;
                default:
                    return SoldierType.Knife;
            }
        }
    }
}
