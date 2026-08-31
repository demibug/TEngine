using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Recruit
{
    [TestFixture]
    internal sealed class OpponentAIDeckTests
    {
        private UnitSlotBoard _slotBoard;

        [SetUp]
        public void SetUp()
        {
            _slotBoard = new UnitSlotBoard(maxLevel: 5);
            _slotBoard.Initialize(BuildTestMap(), reserveSlotCount: 5);
        }

        [TearDown]
        public void TearDown()
        {
            _slotBoard.GameOver();
        }

        [Test]
        public void OriginalPool_Has108Cards_WithBundleCounts()
        {
            var deck = new OpponentDeckManager(new FixedRandomSource(0.25f));
            Assert.AreEqual(108, OpponentDeckManager.OriginalPoolSize);
            Assert.AreEqual(108, deck.RemainingCount);

            IReadOnlyList<OpponentDeckCard> cards = deck.RemainingCards;
            Assert.AreEqual(21, Count(cards, "刀"));
            Assert.AreEqual(19, Count(cards, "弓"));
            Assert.AreEqual(18, Count(cards, "枪"));
            Assert.AreEqual(17, Count(cards, "骑"));
            Assert.AreEqual(11, Count(cards, "铲"));
            Assert.AreEqual(22, CountGeneralParts(cards));
        }

        [Test]
        public void GeneralPartCopy_IsIndependentAndDoesNotMutateSourceDeck()
        {
            var first = new OpponentDeckManager(new FixedRandomSource(0f));
            var second = new OpponentDeckManager(new FixedRandomSource(0f));

            first.CopyGeneralParts();

            Assert.AreEqual(130, first.RemainingCount, "固定 0 随机应复制全部 22 张字牌");
            Assert.AreEqual(108, second.RemainingCount, "两侧牌库必须独立");
        }

        [Test]
        public void Hand_IsFixedCapacity_AndTracksReserveProjectionIds()
        {
            var hand = new OpponentHand(5);
            var cards = new List<OpponentDeckCard>
            {
                new OpponentDeckCard(1, "刀", OpponentCardKind.Soldier),
                new OpponentDeckCard(2, "张", OpponentCardKind.GeneralPart),
                new OpponentDeckCard(3, "铲", OpponentCardKind.Shovel),
            };
            hand.ReplaceAll(cards);

            Assert.AreEqual(5, hand.Capacity);
            Assert.AreEqual(3, hand.Count);

            var units = new[]
            {
                new BattleUnit(10, false, UnitKind.Soldier, SoldierType.Knife, "刀", 1),
                BattleUnit.CreateGeneralPart(11, false, "张"),
                BattleUnit.CreateShovel(12, false),
            };
            hand.BindUnitIds(units);

            Assert.IsTrue(hand.TryRemoveByUnitId(11, out OpponentDeckCard removed));
            Assert.AreEqual("张", removed.Text);
            Assert.AreEqual(2, hand.Count);
            Assert.IsFalse(hand.TryRemoveByUnitId(11, out _));
        }

        [Test]
        public void RecruitManager_ProjectsOpponentHandIntoFiveReserveUnits()
        {
            var deck = new OpponentDeckManager(
                new FixedRandomSource(0.1f),
                allowGeneralParts: false,
                allowFarmer: false,
                includeShovels: false);
            var hand = new OpponentHand(5);
            var recruit = new RecruitManager(
                new FixedRandomSource(0.2f),
                new FixedRandomSource(0.2f),
                _slotBoard,
                5,
                partRecruitEntries: null,
                includeInitialPlayerShovel: false,
                deck,
                hand);

            IReadOnlyList<BattleUnit> batch = recruit.GenerateBatch(isPlayerSide: false);

            Assert.AreEqual(5, batch.Count);
            Assert.AreEqual(5, hand.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                Assert.IsFalse(batch[i].Side);
                Assert.AreEqual(UnitKind.Soldier, batch[i].Kind);
            }
        }

        [Test]
        public void RecruitManager_CanCreateFarmerCardWithoutTouchingBoard()
        {
            var recruit = new RecruitManager(new FixedRandomSource(0f), _slotBoard, 1);
            BattleUnit farmer = recruit.CreateUnitFromCard(
                new OpponentDeckCard(99, "农", OpponentCardKind.Farmer));

            Assert.AreEqual(UnitKind.Farmer, farmer.Kind);
            Assert.IsFalse(farmer.Side);
            Assert.AreEqual(UnitSlot.InvalidUnitId,
                _slotBoard.GetSlots(false, SlotZone.Battle)[0].OccupantUnitId);
        }

        private static int Count(IReadOnlyList<OpponentDeckCard> cards, string text)
        {
            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Text == text)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountGeneralParts(IReadOnlyList<OpponentDeckCard> cards)
        {
            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Kind == OpponentCardKind.GeneralPart)
                {
                    count++;
                }
            }

            return count;
        }

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

            cells[0] = new GridCell(GridCellKind.Buildable, BuildableSide.Player);
            cells[1] = new GridCell(GridCellKind.Buildable, BuildableSide.Player);
            cells[2] = new GridCell(GridCellKind.Buildable, BuildableSide.Opponent);
            return new MapData(
                cells,
                width,
                height,
                mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, 2),
                opponentStart: new GridPosition(2, 0),
                opponentEnd: new GridPosition(2, 2),
                playerPath: Array.Empty<GridPosition>(),
                opponentPath: Array.Empty<GridPosition>());
        }

        private sealed class FixedRandomSource : IRandomSource
        {
            private readonly float _value;

            internal FixedRandomSource(float value)
            {
                _value = value;
            }

            public float NextUnit() => _value;
            public int Next(int max) => (int)Math.Floor(_value * max);
            public int Next(int min, int max) => min + Next(max - min);
            public void Shuffle<T>(IList<T> list) { }
        }
    }
}
