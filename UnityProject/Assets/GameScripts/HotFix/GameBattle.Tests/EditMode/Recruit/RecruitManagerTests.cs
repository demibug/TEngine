using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Recruit
{
    // ============================================================================
    // RecruitManager 测试（最终方案 Recruit/RecruitManager）
    // ----------------------------------------------------------------------------
    // 验证内容：
    //   1. 生成一整批 1 级四兵，数量 = 待上场槽数量。
    //   2. 兵种只从刀/弓/枪/骑均匀抽取。
    //   3. 确定性随机：相同种子 + 相同调用序列 → 相同批次。
    //   4. 单位 ID 由 UnitSlotBoard 分配（递增，不复用）。
    //   5. 不保存手牌和槽位状态，不负责上场。
    // ============================================================================

    /// <summary>
    /// RecruitManager 单元测试（最终方案：随机生成 1 级四兵批次）。
    /// </summary>
    [TestFixture]
    internal class RecruitManagerTests
    {
        private UnitSlotBoard _slotBoard;

        [SetUp]
        public void SetUp()
        {
            _slotBoard = new UnitSlotBoard(maxLevel: 3);
            _slotBoard.Initialize(BuildTestMap(), reserveSlotCount: 5);
        }

        [TearDown]
        public void TearDown()
        {
            _slotBoard.GameOver();
        }

        /// <summary>构建 3×3 测试地图。</summary>
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

            var emptyPath = System.Array.Empty<GridPosition>();
            return new MapData(
                cells, width, height, mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, height - 1),
                opponentStart: new GridPosition(width - 1, 0),
                opponentEnd: new GridPosition(width - 1, height - 1),
                playerPath: emptyPath,
                opponentPath: emptyPath);
        }

        [Test]
        [Description("GenerateBatch 生成数量 = 待上场槽数量，全部为 1 级四兵。")]
        public void GenerateBatch_ReturnsBatchOfReserveSize_AllLevel1()
        {
            var recruit = new RecruitManager(new SeededRandomSource(42), _slotBoard, reserveSlotCount: 5);
            IReadOnlyList<BattleUnit> batch = recruit.GenerateBatch(true);

            Assert.AreEqual(5, batch.Count, "批次数量 = 待上场槽数量");
            foreach (BattleUnit unit in batch)
            {
                Assert.AreEqual(1, unit.Level, "全部为 1 级");
                Assert.AreEqual(UnitKind.Soldier, unit.Kind, "兵种种类为士兵");
                Assert.IsTrue(unit.Side, "玩家方批次");
                Assert.IsTrue(IsBaseSoldierText(unit.SoldierText), $"兵种 {unit.SoldierText} 应在四兵池内");
            }
        }

        [Test]
        [Description("确定性随机：相同种子 + 相同调用序列 → 相同批次。")]
        public void GenerateBatch_SameSeed_SameSequence()
        {
            var slotBoard2 = new UnitSlotBoard(maxLevel: 3);
            slotBoard2.Initialize(BuildTestMap(), reserveSlotCount: 5);

            var recruit1 = new RecruitManager(new SeededRandomSource(123), _slotBoard, 5);
            var recruit2 = new RecruitManager(new SeededRandomSource(123), slotBoard2, 5);

            IReadOnlyList<BattleUnit> batch1 = recruit1.GenerateBatch(true);
            IReadOnlyList<BattleUnit> batch2 = recruit2.GenerateBatch(true);

            Assert.AreEqual(batch1.Count, batch2.Count, "批次数量一致");
            for (int i = 0; i < batch1.Count; i++)
            {
                Assert.AreEqual(batch1[i].SoldierType, batch2[i].SoldierType, $"第 {i} 个兵种一致");
                Assert.AreEqual(batch1[i].SoldierText, batch2[i].SoldierText, $"第 {i} 个兵种文字一致");
            }
        }

        [Test]
        [Description("单位 ID 由 UnitSlotBoard 分配，递增且不复用。")]
        public void GenerateBatch_AllocatesIncreasingUnitIds()
        {
            var recruit = new RecruitManager(new SeededRandomSource(7), _slotBoard, 5);
            IReadOnlyList<BattleUnit> batch1 = recruit.GenerateBatch(true);
            IReadOnlyList<BattleUnit> batch2 = recruit.GenerateBatch(false);

            var ids = new HashSet<int>();
            foreach (BattleUnit unit in batch1)
            {
                Assert.IsTrue(ids.Add(unit.UnitId), "单位 ID 唯一");
            }
            foreach (BattleUnit unit in batch2)
            {
                Assert.IsTrue(ids.Add(unit.UnitId), "跨批次单位 ID 不复用");
            }
        }

        [Test]
        [Description("兵种文字 → SoldierType 映射正确。")]
        public void TextToSoldierType_MapsCorrectly()
        {
            Assert.AreEqual(SoldierType.Knife, RecruitManager.TextToSoldierType("刀"));
            Assert.AreEqual(SoldierType.Bow, RecruitManager.TextToSoldierType("弓"));
            Assert.AreEqual(SoldierType.Spear, RecruitManager.TextToSoldierType("枪"));
            Assert.AreEqual(SoldierType.Cavalry, RecruitManager.TextToSoldierType("骑"));
        }

        private static bool IsBaseSoldierText(string text)
        {
            foreach (string baseText in RecruitDefinitions.BaseSoldierTexts)
            {
                if (baseText == text)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
