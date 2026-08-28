using System;
using System.Collections.Generic;

namespace GameBattle
{
    internal enum OpenTileRejectReason
    {
        None = 0,
        OutsideMap = 1,
        NotCultivable = 2,
        WrongSide = 3,
        AlreadyOpened = 4,
        RevisionConflict = 5,
    }

    internal readonly struct OpenTileChange
    {
        internal readonly GridPosition Position;
        internal readonly BuildableSide Side;
        internal readonly int RevisionBefore;
        internal readonly int RevisionAfter;

        internal bool IsValid => RevisionAfter > RevisionBefore;

        internal OpenTileChange(
            GridPosition position,
            BuildableSide side,
            int revisionBefore,
            int revisionAfter)
        {
            Position = position;
            Side = side;
            RevisionBefore = revisionBefore;
            RevisionAfter = revisionAfter;
        }
    }

    internal readonly struct OpenTileResult
    {
        internal bool IsSuccess => RejectReason == OpenTileRejectReason.None;
        internal readonly OpenTileRejectReason RejectReason;
        internal readonly OpenTileChange Change;

        private OpenTileResult(OpenTileRejectReason rejectReason, OpenTileChange change)
        {
            RejectReason = rejectReason;
            Change = change;
        }

        internal static OpenTileResult Ok(OpenTileChange change)
        {
            return new OpenTileResult(OpenTileRejectReason.None, change);
        }

        internal static OpenTileResult Fail(OpenTileRejectReason rejectReason)
        {
            return new OpenTileResult(rejectReason, default);
        }
    }

    /// <summary>
    /// 单局地图运行状态。不可变 <see cref="MapData"/> 作为模板，开垦结果只保存在本局覆盖中。
    /// </summary>
    internal sealed class BattleMapState
    {
        private readonly Dictionary<GridPosition, BuildableSide> _openedTiles
            = new Dictionary<GridPosition, BuildableSide>();

        internal MapData Template { get; }
        internal int Revision { get; private set; }
        internal int OpenedTileCount => _openedTiles.Count;

        internal BattleMapState(MapData template)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
        }

        internal GridCell GetEffectiveCell(GridPosition position)
        {
            if (!Template.IsInside(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, "地图坐标越界");
            }

            if (_openedTiles.TryGetValue(position, out BuildableSide side))
            {
                return new GridCell(GridCellKind.Buildable, side);
            }

            return Template.GetCell(position);
        }

        internal bool IsBuildableForSide(bool playerSide, GridPosition position)
        {
            return Template.IsInside(position)
                   && GetEffectiveCell(position).IsBuildableForSide(playerSide);
        }

        internal bool IsOpened(GridPosition position)
        {
            return _openedTiles.ContainsKey(position);
        }

        internal OpenTileRejectReason CanOpenTile(bool playerSide, GridPosition position)
        {
            if (!Template.IsInside(position))
            {
                return OpenTileRejectReason.OutsideMap;
            }

            if (_openedTiles.ContainsKey(position))
            {
                return OpenTileRejectReason.AlreadyOpened;
            }

            GridCell templateCell = Template.GetCell(position);
            if (!templateCell.IsCultivable)
            {
                return OpenTileRejectReason.NotCultivable;
            }

            if (!templateCell.BelongsToSide(playerSide))
            {
                return OpenTileRejectReason.WrongSide;
            }

            return OpenTileRejectReason.None;
        }

        internal OpenTileResult TryOpenTile(bool playerSide, GridPosition position)
        {
            OpenTileRejectReason rejectReason = CanOpenTile(playerSide, position);
            if (rejectReason != OpenTileRejectReason.None)
            {
                return OpenTileResult.Fail(rejectReason);
            }

            BuildableSide side = playerSide ? BuildableSide.Player : BuildableSide.Opponent;
            int revisionBefore = Revision;
            _openedTiles.Add(position, side);
            Revision++;

            var change = new OpenTileChange(position, side, revisionBefore, Revision);
            return OpenTileResult.Ok(change);
        }

        internal bool TryRollback(OpenTileChange change)
        {
            if (!change.IsValid
                || Revision != change.RevisionAfter
                || !_openedTiles.TryGetValue(change.Position, out BuildableSide side)
                || side != change.Side)
            {
                return false;
            }

            _openedTiles.Remove(change.Position);
            Revision++;
            return true;
        }

        internal void Clear()
        {
            if (_openedTiles.Count == 0)
            {
                return;
            }

            _openedTiles.Clear();
            Revision++;
        }
    }
}
