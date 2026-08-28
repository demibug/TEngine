using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Map
{
    [TestFixture]
    internal sealed class BattleMapStateTests
    {
        private static MapData CreateMap()
        {
            IReadOnlyList<IReadOnlyList<string>> grid = new[]
            {
                (IReadOnlyList<string>)new[] { "2_0", "0_0" },
                (IReadOnlyList<string>)new[] { "2_1", "1_0" },
            };

            return MapData.FromColumnMajorGrid(
                grid,
                BattleConfigNormalizer.DecodeCell,
                mapIndex: 0,
                playerStart: new GridPosition(0, 1),
                playerEnd: new GridPosition(0, 1),
                opponentStart: new GridPosition(0, 1),
                opponentEnd: new GridPosition(0, 1),
                playerPath: Array.Empty<GridPosition>(),
                opponentPath: Array.Empty<GridPosition>());
        }

        [Test]
        public void DecodeCell_CultivablePreservesCanonicalSide()
        {
            GridCell player = BattleConfigNormalizer.DecodeCell("2_0");
            GridCell opponent = BattleConfigNormalizer.DecodeCell("2_1");

            Assert.AreEqual(GridCellKind.Cultivable, player.Kind);
            Assert.AreEqual(BuildableSide.Player, player.Side);
            Assert.AreEqual(GridCellKind.Cultivable, opponent.Kind);
            Assert.AreEqual(BuildableSide.Opponent, opponent.Side);
        }

        [Test]
        public void TryOpenTile_ChangesEffectiveCellWithoutMutatingTemplate()
        {
            MapData template = CreateMap();
            var state = new BattleMapState(template);
            var position = new GridPosition(0, 0);

            OpenTileResult result = state.TryOpenTile(playerSide: true, position);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(state.IsBuildableForSide(true, position));
            Assert.IsFalse(state.IsBuildableForSide(false, position));
            Assert.IsTrue(template.GetCell(position).IsCultivable);
            Assert.AreEqual(1, state.OpenedTileCount);
        }

        [TestCase(true, -1, 0, OpenTileRejectReason.OutsideMap)]
        [TestCase(true, 0, 1, OpenTileRejectReason.NotCultivable)]
        [TestCase(true, 1, 1, OpenTileRejectReason.NotCultivable)]
        [TestCase(true, 1, 0, OpenTileRejectReason.WrongSide)]
        [TestCase(false, 0, 0, OpenTileRejectReason.WrongSide)]
        public void TryOpenTile_InvalidTargetDoesNotChangeState(
            bool playerSide,
            int x,
            int y,
            OpenTileRejectReason expected)
        {
            var state = new BattleMapState(CreateMap());

            OpenTileResult result = state.TryOpenTile(playerSide, new GridPosition(x, y));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(expected, result.RejectReason);
            Assert.AreEqual(0, state.OpenedTileCount);
            Assert.AreEqual(0, state.Revision);
        }

        [Test]
        public void TryOpenTile_DuplicateRejectedAndRollbackIsSingleUse()
        {
            var state = new BattleMapState(CreateMap());
            var position = new GridPosition(0, 0);

            OpenTileResult first = state.TryOpenTile(true, position);
            OpenTileResult duplicate = state.TryOpenTile(true, position);

            Assert.IsTrue(first.IsSuccess);
            Assert.AreEqual(OpenTileRejectReason.AlreadyOpened, duplicate.RejectReason);
            Assert.IsTrue(state.TryRollback(first.Change));
            Assert.IsFalse(state.TryRollback(first.Change));
            Assert.IsTrue(state.GetEffectiveCell(position).IsCultivable);
            Assert.AreEqual(0, state.OpenedTileCount);
        }

        [Test]
        public void OpenTile_DoesNotChangeConfiguredPaths()
        {
            MapData template = CreateMap();
            IReadOnlyList<GridPosition> playerPath = template.GetPlayerPath();
            IReadOnlyList<GridPosition> opponentPath = template.GetOpponentPath();
            var state = new BattleMapState(template);

            state.TryOpenTile(true, new GridPosition(0, 0));

            Assert.AreSame(playerPath, template.GetPlayerPath());
            Assert.AreSame(opponentPath, template.GetOpponentPath());
        }
    }
}
