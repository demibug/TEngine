using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Slot
{
    // ============================================================================
    // UnitSlotBoard 测试（最终方案"固定槽位"与"拖放规则"）
    // ----------------------------------------------------------------------------
    // 验证内容：
    //   1. 初始化生成固定战场槽与待上场槽，SlotId 单局内唯一且固定不变。
    //   2. 空目标槽换槽：源槽变空，目标槽承载单位，SlotId 不变。
    //   3. 有目标单位且满足合并条件：目标升一级，源单位消失，源槽变空。
    //   4. 有目标单位但不满足条件：不修改任何逻辑状态（满级/不同兵种/不同等级/跨阵营）。
    //   5. 合并条件：不同单位、同阵营、同兵种、同等级、低于配置最大等级。
    //   6. 合并免费且单次执行，不自动连锁。
    //   7. 合并结果保留目标 UnitId 和目标 SlotId。
    //   8. 征兵替换只处理待上场槽，绝不影响战场槽。
    //   9. 快照只读，禁止 UI 直接修改。
    // ============================================================================

    /// <summary>
    /// UnitSlotBoard 单元测试（最终方案：固定槽位、换槽、合并、征兵替换）。
    /// </summary>
    [TestFixture]
    internal class UnitSlotBoardTests
    {
        private const int MaxLevel = 5;

        private UnitSlotBoard _board;

        [SetUp]
        public void SetUp()
        {
            _board = new UnitSlotBoard(MaxLevel);
            _board.Initialize(BuildTestMap(), reserveSlotCount: 5);
        }

        [TearDown]
        public void TearDown()
        {
            _board.GameOver();
        }

        /// <summary>构建 3×3 测试地图：玩家可建造 (0,0)、(1,0)，对手可建造 (0,2)。</summary>
        private static MapData BuildTestMap()
        {
            const int width = 3;
            const int height = 3;
            var cells = new GridCell[width * height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    cells[x * height + y] = new GridCell(GridCellKind.Passage, BuildableSide.None);
                }
            }

            cells[0 * height + 0] = new GridCell(GridCellKind.Buildable, BuildableSide.Player);
            cells[1 * height + 0] = new GridCell(GridCellKind.Buildable, BuildableSide.Player);
            cells[0 * height + 2] = new GridCell(GridCellKind.Buildable, BuildableSide.Opponent);

            var emptyPath = Array.Empty<GridPosition>();
            return new MapData(
                cells, width, height, mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, height - 1),
                opponentStart: new GridPosition(width - 1, 0),
                opponentEnd: new GridPosition(width - 1, height - 1),
                playerPath: emptyPath,
                opponentPath: emptyPath);
        }

        private static BattleUnit MakeUnit(int unitId, bool side, SoldierType type, string text, int level)
        {
            return new BattleUnit(unitId, side, UnitKind.Soldier, type, text, level);
        }

        private static BattleUnit MakeKnife(int unitId, int level) => MakeUnit(unitId, true, SoldierType.Knife, "刀", level);

        /// <summary>
        /// 构造一个刚好填满全部待上场槽（5 个）的批次（ReplaceReserve 严格要求批次数量等于槽位数）。
        /// </summary>
        private static BattleUnit[] FullBatch(BattleUnit first, params BattleUnit[] rest)
        {
            var batch = new BattleUnit[5];
            batch[0] = first;
            for (int i = 1; i < 5; i++)
            {
                batch[i] = i - 1 < rest.Length
                    ? rest[i - 1]
                    : MakeKnife(3000 + i, 1);
            }

            return batch;
        }

        /// <summary>填充指定槽位的源/目标单位（清空其余槽）。</summary>
        private void FillReserve(UnitSlot sourceSlot, BattleUnit sourceUnit, UnitSlot targetSlot, BattleUnit targetUnit)
        {
            var batch = new BattleUnit[5];
            for (int i = 0; i < 5; i++)
            {
                batch[i] = MakeKnife(4000 + i, 1);
            }

            // 找到源槽与目标槽的索引，把对应单位放进去。
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            for (int i = 0; i < reserves.Count; i++)
            {
                if (reserves[i].SlotId.Id == sourceSlot.SlotId.Id)
                {
                    batch[i] = sourceUnit;
                }

                if (reserves[i].SlotId.Id == targetSlot.SlotId.Id)
                {
                    batch[i] = targetUnit;
                }
            }

            _board.ReplaceReserve(true, batch);
        }

        // ====================================================================
        // 初始化
        // ====================================================================

        [Test]
        [Description("初始化生成固定战场槽与待上场槽，SlotId 单局内唯一。")]
        public void Initialize_CreatesFixedSlots_UniqueIds()
        {
            IReadOnlyList<UnitSlot> playerBattle = _board.GetSlots(true, SlotZone.Battle);
            IReadOnlyList<UnitSlot> playerReserve = _board.GetSlots(true, SlotZone.Reserve);

            Assert.AreEqual(2, playerBattle.Count, "玩家 2 个战场槽（2 个可建造格）");
            Assert.AreEqual(5, playerReserve.Count, "玩家 5 个待上场槽");
            Assert.IsTrue(playerBattle[0].IsEmpty && playerBattle[1].IsEmpty, "开局战场槽为空");
            Assert.IsTrue(playerReserve[0].IsEmpty, "开局待上场槽为空（由征兵填满）");

            // SlotId 唯一性。
            var ids = new HashSet<int>();
            foreach (UnitSlot slot in _board.GetSlots(true, SlotZone.Battle))
            {
                Assert.IsTrue(ids.Add(slot.SlotId.Id), "战场槽 SlotId 唯一");
            }
            foreach (UnitSlot slot in _board.GetSlots(true, SlotZone.Reserve))
            {
                Assert.IsTrue(ids.Add(slot.SlotId.Id), "待上场槽 SlotId 唯一");
            }
        }

        [Test]
        [Description("战场槽 GridPosition 映射地图可建造格，待上场槽无 GridPosition。")]
        public void Initialize_BattleSlots_MapToBuildableCells()
        {
            IReadOnlyList<UnitSlot> playerBattle = _board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(playerBattle[0].SlotId.GridPosition == new GridPosition(0, 0)
                || playerBattle[0].SlotId.GridPosition == new GridPosition(1, 0),
                "战场槽映射玩家可建造格");
        }

        // ====================================================================
        // 换槽
        // ====================================================================

        [Test]
        [Description("空目标槽换槽：源槽变空，目标槽承载单位，SlotId 不变。")]
        public void DropUnit_EmptyTarget_MovesUnit_SourceEmpty()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            FillReserve(source, MakeKnife(100, 1), target, MakeKnife(999, 1));
            // 目标槽置空：先换走 target 的单位。
            _board.DropUnit(target.SlotId, reserves[2].SlotId);

            UnitSlot sourceAfterFill = _board.GetSlot(source.SlotId);
            Assert.IsFalse(sourceAfterFill.IsEmpty, "源槽已填入单位");
            Assert.IsTrue(_board.GetSlot(target.SlotId).IsEmpty, "目标槽为空");

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsTrue(result.Success, "换槽应成功");
            Assert.IsFalse(result.IsMerge, "空目标槽是换槽不是合并");
            Assert.IsTrue(_board.GetSlot(source.SlotId).IsEmpty, "源槽变空");
            Assert.AreEqual(100, _board.GetSlot(target.SlotId).OccupantUnitId, "目标槽承载被移动单位");
            Assert.AreEqual(source.SlotId.Id, _board.GetSlot(source.SlotId).SlotId.Id, "源槽 SlotId 不变");
            Assert.AreEqual(target.SlotId.Id, _board.GetSlot(target.SlotId).SlotId.Id, "目标槽 SlotId 不变");
        }

        [Test]
        [Description("换槽失败：源槽为空返回 SourceEmpty，不修改任何状态。")]
        public void DropUnit_EmptySource_ReturnsSourceEmpty()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsFalse(result.Success, "空源槽应失败");
            Assert.AreEqual(SlotDropRejectReason.SourceEmpty, result.RejectReason);
            Assert.IsTrue(_board.GetSlot(target.SlotId).IsEmpty, "目标槽未变");
        }

        [Test]
        [Description("换槽失败：源槽与目标槽相同返回 SameSlot。")]
        public void DropUnit_SameSlot_ReturnsSameSlot()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            FillReserve(reserves[0], MakeKnife(100, 1), reserves[1], MakeKnife(999, 1));

            SlotDropResult result = _board.DropUnit(reserves[0].SlotId, reserves[0].SlotId);

            Assert.IsFalse(result.Success, "相同槽位应失败");
            Assert.AreEqual(SlotDropRejectReason.SameSlot, result.RejectReason);
        }

        [Test]
        [Description("跨阵营空目标槽移动被拦截（修复 P0），返回 CrossSide。")]
        public void DropUnit_CrossSide_EmptyTarget_Rejects()
        {
            IReadOnlyList<UnitSlot> playerReserves = _board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> opponentReserves = _board.GetSlots(false, SlotZone.Reserve);
            UnitSlot source = playerReserves[0];
            UnitSlot target = opponentReserves[0];
            Assert.IsTrue(target.IsEmpty, "对手目标槽为空");

            FillReserve(source, MakeKnife(100, 1), playerReserves[1], MakeKnife(999, 1));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsFalse(result.Success, "跨阵营空槽移动应失败");
            Assert.AreEqual(SlotDropRejectReason.CrossSide, result.RejectReason);
            Assert.IsFalse(_board.GetSlot(source.SlotId).IsEmpty, "源槽未变");
        }

        // ====================================================================
        // 合并
        // ====================================================================

        [Test]
        [Description("合并成功：同兵种同等级目标升一级，源单位消失，保留目标 UnitId 与冷却。")]
        public void DropUnit_Merge_LevelsUpTarget_SourceDisappears()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源与目标都是 1 级刀兵，目标带冷却 500ms。
            FillReserve(source, MakeKnife(100, 1), target, new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1, 500L));

            SlotDropResult mergeResult = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsTrue(mergeResult.Success, "合并应成功");
            Assert.IsTrue(mergeResult.IsMerge, "应判定为合并");
            Assert.IsTrue(_board.GetSlot(source.SlotId).IsEmpty, "源槽变空（源单位消失）");
            Assert.AreEqual(200, _board.GetSlot(target.SlotId).OccupantUnitId, "结果保留目标 UnitId");
            Assert.AreEqual(2, _board.GetSlot(target.SlotId).Occupant.Value.Level, "目标单位升一级");
            Assert.AreEqual(500L, _board.GetSlot(target.SlotId).Occupant.Value.LastAttackTimeMs,
                "合并保留目标单位攻击冷却");
        }

        [Test]
        [Description("互换成功：不同兵种互换位置，双方单位属性不变。")]
        public void DropUnit_Swap_DifferentType_Swaps()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源 1 级刀兵，目标 1 级弓兵（不同兵种不可合并 → 互换）。
            FillReserve(source, MakeKnife(100, 1), target, MakeUnit(200, true, SoldierType.Bow, "弓", 1));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsTrue(result.Success, "不同兵种应互换而非拒绝");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(200, _board.GetSlot(source.SlotId).OccupantUnitId, "源槽换入目标单位");
            Assert.AreEqual(100, _board.GetSlot(target.SlotId).OccupantUnitId, "目标槽换入源单位");
            Assert.AreEqual(1, _board.GetSlot(target.SlotId).Occupant.Value.Level, "互换不改变等级");
        }

        [Test]
        [Description("互换成功：不同等级互换位置，双方单位属性不变。")]
        public void DropUnit_Swap_DifferentLevel_Swaps()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源 2 级刀兵，目标 1 级刀兵（不同等级不可合并 → 互换）。
            FillReserve(source, MakeKnife(200, 2), target, MakeKnife(100, 1));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsTrue(result.Success, "不同等级应互换而非拒绝");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(100, _board.GetSlot(source.SlotId).OccupantUnitId, "源槽换入目标单位");
            Assert.AreEqual(200, _board.GetSlot(target.SlotId).OccupantUnitId, "目标槽换入源单位");
            Assert.AreEqual(2, _board.GetSlot(target.SlotId).Occupant.Value.Level, "互换保留源等级");
        }

        [Test]
        [Description("互换成功：目标已满级互换位置，不修改任何单位属性。")]
        public void DropUnit_Swap_MaxLevel_Swaps()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源与目标都是 5 级 = 最大等级，不可合并 → 互换。
            FillReserve(source, MakeKnife(100, 5), target, MakeKnife(200, 5));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsTrue(result.Success, "满级目标应互换而非拒绝");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(200, _board.GetSlot(source.SlotId).OccupantUnitId, "源槽换入目标单位");
            Assert.AreEqual(100, _board.GetSlot(target.SlotId).OccupantUnitId, "目标槽换入源单位");
            Assert.AreEqual(5, _board.GetSlot(target.SlotId).Occupant.Value.Level, "互换不改变等级");
        }

        [Test]
        [Description("连续合并可从 1 级推进到原始行为的 5 级上限。")]
        public void DropUnit_Merge_ChainReachesFiveLevel()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(_board.ReplaceReserve(true, new BattleUnit[]
            {
                MakeKnife(100, 1),
                MakeKnife(101, 1),
                MakeKnife(102, 2),
                MakeKnife(103, 3),
                MakeKnife(104, 4),
            }));

            Assert.IsTrue(_board.DropUnit(reserves[0].SlotId, reserves[1].SlotId).IsMerge);
            Assert.AreEqual(2, _board.GetSlot(reserves[1].SlotId).Occupant.Value.Level);
            Assert.IsTrue(_board.DropUnit(reserves[2].SlotId, reserves[1].SlotId).IsMerge);
            Assert.AreEqual(3, _board.GetSlot(reserves[1].SlotId).Occupant.Value.Level);
            Assert.IsTrue(_board.DropUnit(reserves[3].SlotId, reserves[1].SlotId).IsMerge);
            Assert.AreEqual(4, _board.GetSlot(reserves[1].SlotId).Occupant.Value.Level);
            Assert.IsTrue(_board.DropUnit(reserves[4].SlotId, reserves[1].SlotId).IsMerge);
            Assert.AreEqual(5, _board.GetSlot(reserves[1].SlotId).Occupant.Value.Level);
        }

        [Test]
        [Description("合并失败：跨阵营返回 CrossSide，不修改状态。")]
        public void DropUnit_Merge_CrossSide_Rejects()
        {
            IReadOnlyList<UnitSlot> playerReserves = _board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> opponentReserves = _board.GetSlots(false, SlotZone.Reserve);
            UnitSlot source = playerReserves[0];
            UnitSlot target = opponentReserves[0];

            FillReserve(source, MakeKnife(100, 1), playerReserves[1], MakeKnife(999, 1));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsFalse(result.Success, "跨阵营应失败");
            Assert.AreEqual(SlotDropRejectReason.CrossSide, result.RejectReason);
        }

        // ====================================================================
        // 互换（Swap）四向覆盖
        // ====================================================================

        /// <summary>把指定待上场槽的单位换到第一个空战场槽（板级 DropUnit，Move），返回目标战场槽。</summary>
        private UnitSlot MoveReserveToBattle(UnitSlot reserve)
        {
            IReadOnlyList<UnitSlot> battles = _board.GetSlots(true, SlotZone.Battle);
            for (int i = 0; i < battles.Count; i++)
            {
                if (battles[i].IsEmpty)
                {
                    SlotDropResult result = _board.DropUnit(reserve.SlotId, battles[i].SlotId);
                    Assert.IsTrue(result.Success, "测试前置：Move 到战场槽应成功");
                    return _board.GetSlot(battles[i].SlotId);
                }
            }

            throw new InvalidOperationException("测试前置：无空战场槽可放入单位");
        }

        [Test]
        [Description("互换 R→R：待上场源与待上场目标互换位置。")]
        public void DropUnit_Swap_ReserveToReserve_SwapsOccupants()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            FillReserve(source, MakeKnife(100, 1), target, MakeUnit(200, true, SoldierType.Bow, "弓", 1));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsTrue(result.Success, "R→R 互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(200, _board.GetSlot(source.SlotId).OccupantUnitId, "源槽换入目标单位");
            Assert.AreEqual(100, _board.GetSlot(target.SlotId).OccupantUnitId, "目标槽换入源单位");
        }

        [Test]
        [Description("互换 R→B：待上场源与战场目标互换位置。")]
        public void DropUnit_Swap_ReserveToBattle_SwapsOccupants()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot targetBattle = reserves[1];

            // 先让目标单位（弓兵）上场到战场槽，再以刀兵源与它互换。
            FillReserve(source, MakeKnife(100, 1), targetBattle, MakeUnit(200, true, SoldierType.Bow, "弓", 1));
            UnitSlot battle = MoveReserveToBattle(targetBattle);
            Assert.IsFalse(battle.IsEmpty, "前置：战场槽已占用");

            SlotDropResult result = _board.DropUnit(source.SlotId, battle.SlotId);

            Assert.IsTrue(result.Success, "R→B 互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(200, _board.GetSlot(source.SlotId).OccupantUnitId, "源待上场槽换入战场单位");
            Assert.AreEqual(100, _board.GetSlot(battle.SlotId).OccupantUnitId, "战场槽换入待上场单位");
            Assert.AreEqual(SlotZone.Battle, _board.GetSlot(battle.SlotId).SlotId.Zone, "战场槽仍在战场区域");
        }

        [Test]
        [Description("互换 B→R：战场源与待上场目标互换位置。")]
        public void DropUnit_Swap_BattleToReserve_SwapsOccupants()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot sourceReserve = reserves[0];
            UnitSlot target = reserves[1];

            // 让源（刀兵）上场到战场槽，再与待上场弓兵目标互换。
            FillReserve(sourceReserve, MakeKnife(100, 1), target, MakeUnit(200, true, SoldierType.Bow, "弓", 1));
            UnitSlot sourceBattle = MoveReserveToBattle(sourceReserve);
            Assert.IsFalse(sourceBattle.IsEmpty, "前置：战场源槽已占用");

            SlotDropResult result = _board.DropUnit(sourceBattle.SlotId, target.SlotId);

            Assert.IsTrue(result.Success, "B→R 互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(200, _board.GetSlot(sourceBattle.SlotId).OccupantUnitId, "战场槽换入目标单位");
            Assert.AreEqual(100, _board.GetSlot(target.SlotId).OccupantUnitId, "待上场槽换入战场单位");
            Assert.AreEqual(SlotZone.Reserve, _board.GetSlot(target.SlotId).SlotId.Zone, "待上场槽仍在待上场区域");
        }

        [Test]
        [Description("互换 B→B：战场源与战场目标互换位置。")]
        public void DropUnit_Swap_BattleToBattle_SwapsOccupants()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot sourceReserve = reserves[0];
            UnitSlot targetReserve = reserves[1];

            // 让刀兵/弓兵分别上场到两个战场槽，再在战场间互换。
            FillReserve(sourceReserve, MakeKnife(100, 1), targetReserve, MakeUnit(200, true, SoldierType.Bow, "弓", 1));
            UnitSlot sourceBattle = MoveReserveToBattle(sourceReserve);
            UnitSlot targetBattle = MoveReserveToBattle(targetReserve);
            Assert.IsFalse(sourceBattle.IsEmpty, "前置：源战场槽已占用");
            Assert.IsFalse(targetBattle.IsEmpty, "前置：目标战场槽已占用");

            SlotDropResult result = _board.DropUnit(sourceBattle.SlotId, targetBattle.SlotId);

            Assert.IsTrue(result.Success, "B→B 互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(200, _board.GetSlot(sourceBattle.SlotId).OccupantUnitId, "源战场槽换入目标单位");
            Assert.AreEqual(100, _board.GetSlot(targetBattle.SlotId).OccupantUnitId, "目标战场槽换入源单位");
            Assert.AreEqual(SlotZone.Battle, _board.GetSlot(sourceBattle.SlotId).SlotId.Zone, "源战场槽仍在战场");
            Assert.AreEqual(SlotZone.Battle, _board.GetSlot(targetBattle.SlotId).SlotId.Zone, "目标战场槽仍在战场");
        }

        // ====================================================================
        // 征兵替换
        // ====================================================================

        [Test]
        [Description("征兵替换清除全部待上场单位（包括高级单位）并重新填满。")]
        public void ReplaceReserve_ClearsAllReserveUnits_Refills()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);

            // 放入一个 3 级高级单位 + 其余 1 级。
            var batch = new BattleUnit[5];
            for (int i = 0; i < 5; i++)
            {
                batch[i] = MakeKnife(4000 + i, 1);
            }

            batch[0] = MakeKnife(100, 3);
            Assert.IsTrue(_board.ReplaceReserve(true, batch), "征兵批次应填满");
            Assert.AreEqual(3, _board.GetSlot(reserves[0].SlotId).Occupant.Value.Level, "已放入 3 级单位");

            // 征兵替换为全新 1 级批次（数量 = 槽位数）。
            var newBatch = new BattleUnit[5];
            for (int i = 0; i < 5; i++)
            {
                newBatch[i] = MakeKnife(5000 + i, 1);
            }

            Assert.IsTrue(_board.ReplaceReserve(true, newBatch), "征兵替换应成功");

            Assert.IsFalse(_board.GetSlot(reserves[0].SlotId).IsEmpty, "槽 0 已填满");
            Assert.IsFalse(_board.GetSlot(reserves[1].SlotId).IsEmpty, "槽 1 已填满");
            Assert.AreEqual(1, _board.GetSlot(reserves[0].SlotId).Occupant.Value.Level, "新批次为 1 级");
            Assert.AreEqual(1, _board.GetSlot(reserves[1].SlotId).Occupant.Value.Level, "新批次为 1 级");
        }

        [Test]
        [Description("征兵替换批次数量与槽位数不一致时返回 false，不修改任何状态（修复 P0）。")]
        public void ReplaceReserve_BatchSizeMismatch_ReturnsFalse_NoChange()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);

            // 先填满 5 个 1 级单位。
            var fullBatch = new BattleUnit[5];
            for (int i = 0; i < 5; i++)
            {
                fullBatch[i] = MakeKnife(4000 + i, 1);
            }

            Assert.IsTrue(_board.ReplaceReserve(true, fullBatch), "满批次应成功");

            // 短批次（2 个）应失败。
            bool shortResult = _board.ReplaceReserve(true, new[] { MakeKnife(1, 1), MakeKnife(2, 1) });
            Assert.IsFalse(shortResult, "短批次应失败");

            // 槽位未变化。
            Assert.AreEqual(4000, _board.GetSlot(reserves[0].SlotId).OccupantUnitId, "槽 0 未变");
            Assert.AreEqual(4004, _board.GetSlot(reserves[4].SlotId).OccupantUnitId, "槽 4 未变");
        }

        [Test]
        [Description("征兵替换不影响战场槽。")]
        public void ReplaceReserve_DoesNotTouchBattleSlots()
        {
            // 先放一个单位到战场槽（通过换槽）。
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battleSlots = _board.GetSlots(true, SlotZone.Battle);
            FillReserve(reserves[0], MakeKnife(100, 1), reserves[1], MakeKnife(999, 1));
            _board.DropUnit(reserves[0].SlotId, battleSlots[0].SlotId);

            int battleOccupantId = _board.GetSlot(battleSlots[0].SlotId).OccupantUnitId;

            var newBatch = new BattleUnit[5];
            for (int i = 0; i < 5; i++)
            {
                newBatch[i] = MakeKnife(5000 + i, 1);
            }

            _board.ReplaceReserve(true, newBatch);

            Assert.AreEqual(battleOccupantId, _board.GetSlot(battleSlots[0].SlotId).OccupantUnitId,
                "征兵替换不影响战场槽");
        }

        // ====================================================================
        // 快照
        // ====================================================================

        [Test]
        [Description("快照提供只读槽位查询，不暴露可变内部状态。")]
        public void Snapshot_ProvidesReadOnlySlotQuery()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            FillReserve(reserves[0], MakeKnife(100, 1), reserves[1], MakeKnife(999, 1));

            UnitSlotSnapshot snapshot = _board.Snapshot();
            IReadOnlyList<UnitSlot> snapshotReserve = snapshot.GetSlots(true, SlotZone.Reserve);

            Assert.AreEqual(5, snapshotReserve.Count, "快照可见待上场槽");
            Assert.IsFalse(snapshotReserve[0].IsEmpty, "快照可见占用状态");
            Assert.AreEqual(100, snapshotReserve[0].OccupantUnitId, "快照可见占用单位 ID");
        }

        // ====================================================================
        // 武将字合成（有序配方：物理左字 + 右字，反序不合成）
        // ====================================================================

        private static GeneralConfigSnapshot ZhangFei()
            => new GeneralConfigSnapshot(
                1, "张飞", "张", new[] { "张", "飞" }, GeneralCombatArchetype.Pike,
                2.5f, 15, 1f, "近战枪击", "nearest", "SpearSoldier", "default", "", 0, 1);

        private static GeneralConfigSnapshot HuangZhong()
            => new GeneralConfigSnapshot(
                4, "黄忠", "黄", new[] { "黄", "忠" }, GeneralCombatArchetype.Bow,
                3.5f, 13, 0.8f, "单体", "nearest", "BowSoldier", "default",
                "SimpleDynamicArrow", 200, 1);

        private static UnitSlotBoard BuildGeneralBoard(params GeneralConfigSnapshot[] generals)
        {
            var board = new UnitSlotBoard(MaxLevel, new GeneralCatalogSnapshot(generals));
            board.Initialize(BuildTestMap(), 5);
            return board;
        }

        private static void AssertGeneralCell(
            UnitSlotBoard board,
            UnitSlotId slotId,
            int expectedCellIndex,
            int expectedUnitId,
            int expectedIndex,
            string expectedName,
            string expectedPartText)
        {
            BattleUnit cell = board.GetSlot(slotId).Occupant.Value;
            Assert.AreEqual(UnitKind.General, cell.Kind, "合成槽位应为武将半格");
            Assert.AreEqual(expectedCellIndex, cell.GeneralCellIndex, "半格序号应正确");
            Assert.AreEqual(expectedUnitId, cell.UnitId, "半格应复用目标单位 ID");
            Assert.AreEqual(expectedIndex, cell.GeneralIndex, "武将索引应正确");
            Assert.AreEqual(expectedName, cell.GeneralName, "武将名应正确");
            Assert.AreEqual(expectedPartText, cell.GeneralPartText, "半格字应与物理槽位一致");
        }

        private static void AssertGeneralPart(
            UnitSlotBoard board,
            UnitSlotId slotId,
            int unitId,
            string partText)
        {
            BattleUnit part = board.GetSlot(slotId).Occupant.Value;
            Assert.AreEqual(UnitKind.GeneralPart, part.Kind, "槽位应为独立武将字");
            Assert.AreEqual(unitId, part.UnitId, "独立字牌 UnitId 应正确");
            Assert.AreEqual(partText, part.GeneralPartText, "独立字牌文字应与槽位一致");
        }

        [Test]
        [Description("张左飞右时覆盖拖动：先交换成飞左张右，最终布局反序，不合成。")]
        public void DropUnit_GeneralParts_OrderedLayout_OverlaySwapsToReversed()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                BattleUnit.CreateGeneralPart(200, true, "飞"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, reserves[1].SlotId);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.IsSwap, "覆盖拖动的基础动作必须是交换");
            Assert.IsFalse(result.Plan.IsSynthesize, "交换后为飞|张，不得合成");
            Assert.AreEqual(200, board.GetSlot(reserves[0].SlotId).OccupantUnitId);
            Assert.AreEqual(100, board.GetSlot(reserves[1].SlotId).OccupantUnitId);
            board.GameOver();
        }

        [Test]
        [Description("张左飞右时反向覆盖拖动：同样只交换成飞左张右，不合成。")]
        public void DropUnit_GeneralParts_OrderedLayout_ReverseOverlaySwapsToReversed()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                BattleUnit.CreateGeneralPart(200, true, "飞"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[1].SlotId, reserves[0].SlotId);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.IsSwap);
            Assert.IsFalse(result.Plan.IsSynthesize);
            Assert.AreEqual(200, board.GetSlot(reserves[0].SlotId).OccupantUnitId);
            Assert.AreEqual(100, board.GetSlot(reserves[1].SlotId).OccupantUnitId);
            board.GameOver();
        }

        [Test]
        [Description("黄左忠右时覆盖拖动：交换后反序，不合成。")]
        public void DropUnit_GeneralParts_HuangZhongOrderedLayout_OverlaySwaps()
        {
            UnitSlotBoard board = BuildGeneralBoard(HuangZhong());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "黄"),
                BattleUnit.CreateGeneralPart(200, true, "忠"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, reserves[1].SlotId);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.IsSwap);
            Assert.IsFalse(result.Plan.IsSynthesize);
            board.GameOver();
        }

        [Test]
        [Description("刷新区飞左张右：覆盖拖动只交换成张左飞右，匹配配方也不合成。")]
        public void DropUnit_GeneralParts_ReversedLayout_InReserve_SwapsWithoutSynthesis()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "飞"),
                BattleUnit.CreateGeneralPart(200, true, "张"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, reserves[1].SlotId);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.IsSwap, "刷新区覆盖拖动仍按普通交换处理");
            Assert.IsFalse(result.Plan.IsSynthesize, "刷新区最终为张|飞也不得合成");
            Assert.AreEqual(UnitKind.GeneralPart, board.GetSlot(reserves[0].SlotId).Occupant.Value.Kind);
            Assert.AreEqual(UnitKind.GeneralPart, board.GetSlot(reserves[1].SlotId).Occupant.Value.Kind);
            Assert.AreEqual("张", board.GetSlot(reserves[0].SlotId).Occupant.Value.GeneralPartText);
            Assert.AreEqual("飞", board.GetSlot(reserves[1].SlotId).Occupant.Value.GeneralPartText);
            board.GameOver();
        }

        [Test]
        [Description("刷新区忠左黄右：覆盖拖动只交换成黄左忠右，不合成黄忠。")]
        public void DropUnit_GeneralParts_HuangZhongReversedLayout_InReserve_SwapsWithoutSynthesis()
        {
            UnitSlotBoard board = BuildGeneralBoard(HuangZhong());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "忠"),
                BattleUnit.CreateGeneralPart(200, true, "黄"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, reserves[1].SlotId);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.IsSwap);
            Assert.IsFalse(result.Plan.IsSynthesize, "刷新区最终为黄|忠也不得合成");
            Assert.AreEqual(UnitKind.GeneralPart, board.GetSlot(reserves[0].SlotId).Occupant.Value.Kind);
            Assert.AreEqual(UnitKind.GeneralPart, board.GetSlot(reserves[1].SlotId).Occupant.Value.Kind);
            Assert.AreEqual("黄", board.GetSlot(reserves[0].SlotId).Occupant.Value.GeneralPartText);
            Assert.AreEqual("忠", board.GetSlot(reserves[1].SlotId).Occupant.Value.GeneralPartText);
            board.GameOver();
        }

        [Test]
        [Description("刷新区 Move 后形成张左飞右：只移动字牌，不合成武将。")]
        public void DropUnit_GeneralParts_OrderedLayout_InReserve_MovesWithoutSynthesis()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                MakeKnife(300, 1),
                BattleUnit.CreateGeneralPart(200, true, "飞"),
                MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult clearMiddle = board.DropUnit(reserves[1].SlotId, battles[0].SlotId);
            Assert.IsTrue(clearMiddle.Success, "测试前置：先把中间士兵移到战场，制造空刷新槽");

            SlotDropResult result = board.DropUnit(reserves[2].SlotId, reserves[1].SlotId);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(SlotDropOperationType.Move, result.Plan.OperationType,
                "飞字移入空刷新槽应保持普通 Move 语义");
            Assert.IsFalse(result.Plan.IsSynthesize, "刷新区 Move 后最终为张|飞也不得合成");
            BattleUnit left = board.GetSlot(reserves[0].SlotId).Occupant.Value;
            BattleUnit right = board.GetSlot(reserves[1].SlotId).Occupant.Value;
            Assert.AreEqual(UnitKind.GeneralPart, left.Kind);
            Assert.AreEqual(UnitKind.GeneralPart, right.Kind);
            Assert.AreEqual("张", left.GeneralPartText);
            Assert.AreEqual("飞", right.GeneralPartText);
            board.GameOver();
        }

        [Test]
        [Description("合法配方但两字槽不相邻：不合成，执行原有 Swap。")]
        public void DropUnit_GeneralParts_NotHorizontallyAdjacent_Swaps()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                MakeKnife(2, 1),
                BattleUnit.CreateGeneralPart(200, true, "飞"),
                MakeKnife(4, 1),
                MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, reserves[2].SlotId);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.IsSwap, "非横向相邻不得合成，应互换");
            Assert.IsFalse(result.Plan.IsSynthesize, "非横向相邻不得合成");
            Assert.AreEqual(200, board.GetSlot(reserves[0].SlotId).OccupantUnitId, "源槽换入远槽字");
            Assert.AreEqual(100, board.GetSlot(reserves[2].SlotId).OccupantUnitId, "目标槽换入源槽字");
            board.GameOver();
        }

        [Test]
        [Description("单格武将字可 Move 到空战场槽：字牌上阵不消耗，源槽变空，目标战场槽承载字牌。")]
        public void DropUnit_GeneralPartToBattle_MovesToEmptyBattleSlot()
        {
            var board = new UnitSlotBoard(MaxLevel);
            board.Initialize(BuildTestMap(), 5);
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                MakeKnife(2, 1), MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, battles[0].SlotId);

            Assert.IsTrue(result.Success, "字牌应可 Move 到空战场槽");
            Assert.IsFalse(result.IsMerge, "空目标槽是换槽不是合并");
            Assert.IsTrue(board.GetSlot(reserves[0].SlotId).IsEmpty, "源槽变空");
            Assert.AreEqual(100, board.GetSlot(battles[0].SlotId).OccupantUnitId, "战场槽承载字牌");
            Assert.AreEqual(UnitKind.GeneralPart, board.GetSlot(battles[0].SlotId).Occupant.Value.Kind,
                "战场槽占用为字牌");
            board.GameOver();
        }

        [Test]
        [Description("战场士兵与待上场字牌互换：字牌进入战场、士兵回待上场，双方位置互换。")]
        public void DropUnit_GeneralPart_SwapWithBattleSoldier_Swaps()
        {
            var board = new UnitSlotBoard(MaxLevel);
            board.Initialize(BuildTestMap(), 5);
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                MakeKnife(2, 1), MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));
            SlotDropResult move = board.DropUnit(reserves[1].SlotId, battles[0].SlotId);
            Assert.IsTrue(move.Success, "前置：士兵先上场");

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, battles[0].SlotId);

            Assert.IsTrue(result.Success, "字牌与士兵互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(2, board.GetSlot(reserves[0].SlotId).OccupantUnitId, "待上场槽换入士兵");
            Assert.AreEqual(100, board.GetSlot(battles[0].SlotId).OccupantUnitId, "战场槽换入字牌");
            Assert.AreEqual(UnitKind.GeneralPart, board.GetSlot(battles[0].SlotId).Occupant.Value.Kind,
                "战场槽占用为字牌");
            board.GameOver();
        }

        // ====================================================================
        // 战场武将字合成与负例（放开战场区域限制后）
        // ====================================================================

        [Test]
        [Description("战场张左飞右：两字牌分别 Move 到相邻战场槽后合成双格张飞。")]
        public void DropUnit_GeneralParts_SynthesizeInBattle_OrderedRecipe()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                BattleUnit.CreateGeneralPart(200, true, "飞"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult moveZhang = board.DropUnit(reserves[0].SlotId, battles[0].SlotId);
            Assert.IsTrue(moveZhang.Success, "字牌应可 Move 到空战场槽");
            SlotDropResult moveFei = board.DropUnit(reserves[1].SlotId, battles[1].SlotId);
            Assert.IsTrue(moveFei.Success, "字牌应可 Move 到空战场槽");

            SlotDropResult result = moveFei;

            Assert.IsTrue(result.Plan.IsSynthesize, "飞字 Move 到张字右侧空格后应按最终布局合成");
            AssertGeneralCell(board, battles[0].SlotId, 0, 100, 1, "张飞", "张");
            AssertGeneralCell(board, battles[1].SlotId, 1, 100, 1, "张飞", "飞");
            board.GameOver();
        }

        [Test]
        [Description("战场反序（飞左张右）：覆盖交换后成张左飞右，触发合成。")]
        public void DropUnit_GeneralParts_ReversedLayout_InBattle_SwapsThenSynthesizes()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "飞"),
                BattleUnit.CreateGeneralPart(200, true, "张"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult moveFei = board.DropUnit(reserves[0].SlotId, battles[0].SlotId);
            Assert.IsTrue(moveFei.Success, "字牌应可 Move 到空战场槽");
            SlotDropResult moveZhang = board.DropUnit(reserves[1].SlotId, battles[1].SlotId);
            Assert.IsTrue(moveZhang.Success, "字牌应可 Move 到空战场槽");

            SlotDropResult result = board.DropUnit(battles[0].SlotId, battles[1].SlotId);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.Plan.IsSynthesize, "交换后最终为张|飞，应合成");
            AssertGeneralCell(board, battles[0].SlotId, 0, 200, 1, "张飞", "张");
            AssertGeneralCell(board, battles[1].SlotId, 1, 200, 1, "张飞", "飞");
            board.GameOver();
        }

        [Test]
        [Description("跨阵营字牌不合成：玩家字拖到对手字返回 CrossSide，双方不变化。")]
        public void DropUnit_GeneralParts_CrossSide_DoesNotSynthesize()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> playerReserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> opponentReserves = board.GetSlots(false, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                MakeKnife(2, 1), MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));
            Assert.IsTrue(board.ReplaceReserve(false, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(600, false, "飞"),
                new BattleUnit(601, false, UnitKind.Soldier, SoldierType.Knife, "刀", 1),
                new BattleUnit(602, false, UnitKind.Soldier, SoldierType.Knife, "刀", 1),
                new BattleUnit(603, false, UnitKind.Soldier, SoldierType.Knife, "刀", 1),
                new BattleUnit(604, false, UnitKind.Soldier, SoldierType.Knife, "刀", 1),
            }));

            SlotDropResult result = board.DropUnit(playerReserves[0].SlotId, opponentReserves[0].SlotId);

            Assert.IsFalse(result.Success, "跨阵营应失败");
            Assert.AreEqual(SlotDropRejectReason.CrossSide, result.RejectReason);
            Assert.AreEqual(100, board.GetSlot(playerReserves[0].SlotId).OccupantUnitId, "玩家字未移动");
            Assert.AreEqual(600, board.GetSlot(opponentReserves[0].SlotId).OccupantUnitId, "对手字未移动");
            board.GameOver();
        }

        [Test]
        [Description("完整武将单字拖到另一格（张→飞）：整将先解散，再按单格互换，最终反序飞|张且不重新合成。")]
        public void DropUnit_CompleteGeneral_DragCellOntoOtherCell_SwapsToReversedParts()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralCell(100, true, ZhangFei(), 0),
                BattleUnit.CreateGeneralCell(100, true, ZhangFei(), 1),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, reserves[1].SlotId);

            Assert.IsTrue(result.Success, "同将内部覆盖拖动必须成功");
            Assert.IsTrue(result.IsSwap, "同将内部覆盖拖动的基础动作必须是单格互换");
            Assert.IsFalse(result.Plan.IsSynthesize, "最终为飞|张反序，不得重新合成");
            AssertGeneralPart(board, reserves[0].SlotId, 100, "飞");
            AssertGeneralPart(board, reserves[1].SlotId, 100, "张");
            board.GameOver();
        }

        [Test]
        [Description("战场完整武将单字下阵到空槽：只移动点击格，武将解散为两个独立字牌；移回后按有序布局重新合成。")]
        public void DropUnit_GeneralCell_DragToEmptyReserve_Disassembles_ThenReSynthesizesOnMoveBack()
        {
            UnitSlotBoard board = BuildGeneralBoard(ZhangFei());
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(100, true, "张"),
                BattleUnit.CreateGeneralPart(200, true, "飞"),
                MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));
            Assert.IsTrue(board.DropUnit(reserves[0].SlotId, battles[0].SlotId).Success);
            Assert.IsTrue(board.DropUnit(reserves[1].SlotId, battles[1].SlotId).Plan.IsSynthesize,
                "武将必须先在战场完成合成");

            SlotDropResult moveDown = board.DropUnit(battles[0].SlotId, reserves[0].SlotId);
            Assert.IsTrue(moveDown.Success, "单字下阵应成功");
            Assert.IsTrue(board.GetSlot(battles[0].SlotId).IsEmpty, "点击格源战场槽变空");
            AssertGeneralPart(board, reserves[0].SlotId, 100, "张");
            AssertGeneralPart(board, battles[1].SlotId, 200, "飞");

            SlotDropResult moveUp = board.DropUnit(reserves[0].SlotId, battles[0].SlotId);
            Assert.IsTrue(moveUp.Success, "单字移回应成功");
            Assert.IsTrue(moveUp.Plan.IsSynthesize, "张字移回张左飞右后按有序布局重新合成");
            AssertGeneralCell(board, battles[0].SlotId, 0, 200, 1, "张飞", "张");
            AssertGeneralCell(board, battles[1].SlotId, 1, 200, 1, "张飞", "飞");
            board.GameOver();
        }

        [Test]
        [Description("两个完整武将互拖单字：双方都解散为独立字牌，只互换点击格。")]
        public void DropUnit_GeneralCell_SwapWithAnotherGeneralCell_DisassemblesBoth()
        {
            GeneralConfigSnapshot zhangFei = ZhangFei();
            GeneralConfigSnapshot huangZhong = HuangZhong();
            UnitSlotBoard board = BuildGeneralBoard(zhangFei, huangZhong);
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralCell(200, true, zhangFei, 0),
                BattleUnit.CreateGeneralCell(200, true, zhangFei, 1),
                BattleUnit.CreateGeneralCell(400, true, huangZhong, 0),
                BattleUnit.CreateGeneralCell(400, true, huangZhong, 1),
                MakeKnife(5, 1),
            }));

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, reserves[2].SlotId);

            Assert.IsTrue(result.Success, "两个武将单字互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为单格互换");
            Assert.IsFalse(result.Plan.IsSynthesize, "刷新区不合成");
            AssertGeneralPart(board, reserves[0].SlotId, 400, "黄");
            AssertGeneralPart(board, reserves[1].SlotId, 200, "飞");
            AssertGeneralPart(board, reserves[2].SlotId, 200, "张");
            AssertGeneralPart(board, reserves[3].SlotId, 400, "忠");
            board.GameOver();
        }

        [Test]
        [Description("战场完整武将单字拖到占位士兵：只交换点击格，武将解散，另一字牌保留。")]
        public void DropUnit_GeneralCell_SwapWithSoldier_DisassemblesGeneral()
        {
            GeneralConfigSnapshot zhangFei = ZhangFei();
            UnitSlotBoard board = BuildGeneralBoard(zhangFei);
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralCell(200, true, zhangFei, 0),
                BattleUnit.CreateGeneralCell(200, true, zhangFei, 1),
                MakeKnife(300, 1), MakeKnife(400, 1), MakeKnife(500, 1),
            }));
            Assert.IsTrue(board.DropUnit(reserves[2].SlotId, battles[0].SlotId).Success,
                "前置：目标槽放入士兵");

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, battles[0].SlotId);

            Assert.IsTrue(result.Success, "单字与士兵互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(UnitKind.Soldier, board.GetSlot(reserves[0].SlotId).Occupant.Value.Kind,
                "源待上场槽换入士兵");
            Assert.AreEqual(300, board.GetSlot(reserves[0].SlotId).OccupantUnitId,
                "源待上场槽换入左目标士兵");
            AssertGeneralPart(board, reserves[1].SlotId, 200, "飞");
            AssertGeneralPart(board, battles[0].SlotId, 200, "张");
            board.GameOver();
        }

        [Test]
        [Description("战场完整武将单字拖到占位士兵，另一格也被士兵占用：只交换点击格，不整体换位。")]
        public void DropUnit_GeneralCell_SwapWithSoldier_KeepsUnclickedCell()
        {
            GeneralConfigSnapshot zhangFei = ZhangFei();
            UnitSlotBoard board = BuildGeneralBoard(zhangFei);
            IReadOnlyList<UnitSlot> reserves = board.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = board.GetSlots(true, SlotZone.Battle);
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateGeneralCell(200, true, zhangFei, 0),
                BattleUnit.CreateGeneralCell(200, true, zhangFei, 1),
                MakeKnife(300, 1), MakeKnife(400, 1), MakeKnife(500, 1),
            }));
            Assert.IsTrue(board.DropUnit(reserves[2].SlotId, battles[0].SlotId).Success,
                "前置：左目标士兵上场");
            Assert.IsTrue(board.DropUnit(reserves[3].SlotId, battles[1].SlotId).Success,
                "前置：右目标士兵上场");

            SlotDropResult result = board.DropUnit(reserves[0].SlotId, battles[0].SlotId);

            Assert.IsTrue(result.Success, "单字与士兵互换应成功");
            Assert.IsTrue(result.IsSwap, "应判定为互换");
            Assert.AreEqual(UnitKind.Soldier, board.GetSlot(reserves[0].SlotId).Occupant.Value.Kind,
                "源待上场槽换入士兵");
            Assert.AreEqual(300, board.GetSlot(reserves[0].SlotId).OccupantUnitId,
                "源待上场槽换入左目标士兵");
            AssertGeneralPart(board, reserves[1].SlotId, 200, "飞");
            AssertGeneralPart(board, battles[0].SlotId, 200, "张");
            Assert.AreEqual(UnitKind.Soldier, board.GetSlot(battles[1].SlotId).Occupant.Value.Kind,
                "未点击格对应的战场士兵保持不动");
            Assert.AreEqual(400, board.GetSlot(battles[1].SlotId).OccupantUnitId,
                "未点击格对应的战场士兵保持不动");
            board.GameOver();
        }

        [Test]
        public void TryCommitShovelUse_ConsumesReserveAndAppendsEmptyBattleSlot()
        {
            var board = new UnitSlotBoard(MaxLevel);
            board.Initialize(BuildTestMap(), 5);
            UnitSlot source = board.GetSlots(true, SlotZone.Reserve)[0];
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateShovel(100, true),
                MakeKnife(2, 1), MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));
            int highestInitialSlotId = board.GetAllSlots()[board.GetAllSlots().Count - 1].SlotId.Id;

            bool committed = board.TryCommitShovelUse(
                source.SlotId.Id,
                new GridPosition(2, 2),
                out ShovelBoardChange change);

            Assert.IsTrue(committed);
            Assert.IsTrue(board.GetSlot(source.SlotId).IsEmpty, "铲子源槽应被消费");
            Assert.Greater(change.AddedBattleSlotId.Id, highestInitialSlotId, "动态槽 ID 必须追加且不碰撞");
            Assert.AreEqual(new GridPosition(2, 2), change.AddedBattleSlotId.GridPosition);
            Assert.IsTrue(board.GetSlot(change.AddedBattleSlotId).IsEmpty, "新开格应生成空战场槽");
            board.GameOver();
        }

        [Test]
        public void TryRollbackShovelUse_RestoresShovelButDoesNotReuseSlotId()
        {
            var board = new UnitSlotBoard(MaxLevel);
            board.Initialize(BuildTestMap(), 5);
            UnitSlot source = board.GetSlots(true, SlotZone.Reserve)[0];
            Assert.IsTrue(board.ReplaceReserve(true, new BattleUnit[]
            {
                BattleUnit.CreateShovel(100, true),
                MakeKnife(2, 1), MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));
            Assert.IsTrue(board.TryCommitShovelUse(source.SlotId.Id, new GridPosition(2, 2), out ShovelBoardChange first));
            Assert.IsTrue(board.TryRollbackShovelUse(first));
            Assert.IsTrue(board.GetSlot(source.SlotId).Occupant.Value.IsShovel);

            Assert.IsTrue(board.TryCommitShovelUse(source.SlotId.Id, new GridPosition(2, 1), out ShovelBoardChange second));
            Assert.Greater(second.AddedBattleSlotId.Id, first.AddedBattleSlotId.Id, "回滚后不得复用动态槽 ID");
            board.GameOver();
        }

        [Test]
        public void TryCommitFarmerUse_ConsumesReserveAndAppendsEmptyBattleSlot()
        {
            var board = new UnitSlotBoard(MaxLevel);
            board.Initialize(BuildTestMap(), 5);
            UnitSlot source = board.GetSlots(false, SlotZone.Reserve)[0];
            Assert.IsTrue(board.ReplaceReserve(false, new BattleUnit[]
            {
                BattleUnit.CreateFarmer(100, false),
                MakeKnife(2, 1), MakeKnife(3, 1), MakeKnife(4, 1), MakeKnife(5, 1),
            }));

            int highestInitialSlotId = board.GetAllSlots()[board.GetAllSlots().Count - 1].SlotId.Id;
            bool committed = board.TryCommitFarmerUse(
                source.SlotId.Id,
                new GridPosition(2, 1),
                out UnitSlotId addedBattleSlotId);

            Assert.IsTrue(committed);
            Assert.IsTrue(board.GetSlot(source.SlotId).IsEmpty, "农民源槽应被消费");
            Assert.Greater(addedBattleSlotId.Id, highestInitialSlotId, "动态槽 ID 必须追加且不碰撞");
            Assert.AreEqual(new GridPosition(2, 1), addedBattleSlotId.GridPosition);
            Assert.IsTrue(board.GetSlot(addedBattleSlotId).IsEmpty, "新开格应生成空战场槽");
            board.GameOver();
        }
    }
}
