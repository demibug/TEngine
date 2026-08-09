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
        private const int MaxLevel = 3;

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
        [Description("合并失败：目标已满级返回 MaxLevelReached，不修改状态。")]
        public void DropUnit_Merge_MaxLevel_Rejects()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源与目标都是 3 级 = 最大等级。
            FillReserve(source, MakeKnife(100, 3), target, MakeKnife(200, 3));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsFalse(result.Success, "满级目标应失败");
            Assert.AreEqual(SlotDropRejectReason.MaxLevelReached, result.RejectReason);
            Assert.IsFalse(_board.GetSlot(source.SlotId).IsEmpty, "源槽未变");
            Assert.AreEqual(3, _board.GetSlot(target.SlotId).Occupant.Value.Level, "目标等级未变");
        }

        [Test]
        [Description("合并失败：不同兵种返回 TargetMismatch，不修改状态。")]
        public void DropUnit_Merge_DifferentType_Rejects()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源 1 级弓兵，目标 1 级刀兵。
            FillReserve(source, MakeUnit(200, true, SoldierType.Bow, "弓", 1), target, MakeKnife(100, 1));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsFalse(result.Success, "不同兵种应失败");
            Assert.AreEqual(SlotDropRejectReason.TargetMismatch, result.RejectReason);
            Assert.IsFalse(_board.GetSlot(source.SlotId).IsEmpty, "源槽未变");
        }

        [Test]
        [Description("合并失败：不同等级返回 TargetMismatch，不修改状态。")]
        public void DropUnit_Merge_DifferentLevel_Rejects()
        {
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源 2 级刀兵，目标 1 级刀兵。
            FillReserve(source, MakeKnife(200, 2), target, MakeKnife(100, 1));

            SlotDropResult result = _board.DropUnit(source.SlotId, target.SlotId);

            Assert.IsFalse(result.Success, "不同等级应失败");
            Assert.AreEqual(SlotDropRejectReason.TargetMismatch, result.RejectReason);
            Assert.IsFalse(_board.GetSlot(source.SlotId).IsEmpty, "源槽未变");
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
    }
}
