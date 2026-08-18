using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 地图固定节点绑定结果（节点只提供层级与锚点，几何来自当前 MapData）。
    /// </summary>
    internal sealed class BattleMapBindingResult
    {
        public bool IsValid => Bindings != null
                               && MissingPaths.Count == 0
                               && DuplicatePaths.Count == 0
                               && InvalidPaths.Count == 0;

        public BattleMapBindings Bindings { get; }

        public IReadOnlyList<string> MissingPaths { get; }

        public IReadOnlyList<string> DuplicatePaths { get; }

        /// <summary>存在但未对齐约定逻辑格的端点路径。</summary>
        public IReadOnlyList<string> InvalidPaths { get; }

        public string DiagnosticMessage { get; }

        internal BattleMapBindingResult(
            BattleMapBindings bindings,
            List<string> missingPaths,
            List<string> duplicatePaths,
            List<string> invalidPaths = null)
        {
            Bindings = bindings;
            MissingPaths = Array.AsReadOnly((missingPaths ?? new List<string>()).ToArray());
            DuplicatePaths = Array.AsReadOnly((duplicatePaths ?? new List<string>()).ToArray());
            InvalidPaths = Array.AsReadOnly((invalidPaths ?? new List<string>()).ToArray());
            DiagnosticMessage = BuildDiagnosticMessage(MissingPaths, DuplicatePaths, InvalidPaths);
        }

        private static string BuildDiagnosticMessage(
            IReadOnlyList<string> missingPaths,
            IReadOnlyList<string> duplicatePaths,
            IReadOnlyList<string> invalidPaths)
        {
            if (missingPaths.Count == 0 && duplicatePaths.Count == 0 && invalidPaths.Count == 0)
            {
                return string.Empty;
            }

            string missing = missingPaths.Count == 0
                ? string.Empty
                : $"缺失路径：{string.Join(", ", missingPaths)}";
            string duplicate = duplicatePaths.Count == 0
                ? string.Empty
                : $"重复路径：{string.Join(", ", duplicatePaths)}";
            string invalid = invalidPaths.Count == 0
                ? string.Empty
                : $"端点坐标不匹配：{string.Join(", ", invalidPaths)}";
            string message = string.Empty;
            AppendDiagnostic(ref message, missing);
            AppendDiagnostic(ref message, duplicate);
            AppendDiagnostic(ref message, invalid);
            return message;
        }

        private static void AppendDiagnostic(ref string message, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            message = string.IsNullOrEmpty(message) ? value : $"{message}；{value}";
        }
    }

    /// <summary>
    /// BattleMap0 中所有运行时必需节点的只读绑定。
    /// <para>节点只提供表现层层级与锚点（design.md 决策 4）；格子尺寸、列数/行数、
    /// cell 宽高和双路端点由当前地图运行快照（<see cref="MapData"/>）决定，
    /// 不再隐含 map0 业务坐标。旧入口 <see cref="TryCreate(UnityEngine.Transform)"/>
    /// 保留为兼容 map0 数据的显式入口。</para>
    /// </summary>
    internal sealed class BattleMapBindings
    {
        private const float LegacyCellSize = 80f;
        private const int LegacyGridWidth = 8;
        private const int LegacyGridHeight = 10;
        private const float UnitVisualAnchorOffsetY = 0.35f;

        private static readonly GridPosition LegacyPlayerStart = new GridPosition(0, 8);
        private static readonly GridPosition LegacyPlayerEnd = new GridPosition(7, 9);
        private static readonly GridPosition LegacyOpponentStart = new GridPosition(7, 1);
        private static readonly GridPosition LegacyOpponentEnd = new GridPosition(0, 0);

        private static readonly string[] RequiredPaths =
        {
            "BackgroundRoot",
            "BackgroundRoot/Background",
            "BackgroundRoot/ThemeRoot",
            "BackgroundRoot/ThemeRoot/Mountains",
            "BackgroundRoot/ThemeRoot/Birds",
            "BackgroundRoot/ThemeRoot/Deer",
            "BoardRoot",
            "BoardRoot/Ground",
            "BoardRoot/Road",
            "BoardRoot/HighGround",
            "BoardRoot/Divide",
            "BoardRoot/UnitSlotRoot",
            "BoardRoot/SpawnPointRoot",
            "BoardRoot/SpawnPointRoot/PlayerSpawn",
            "BoardRoot/SpawnPointRoot/OpponentSpawn",
            "BoardRoot/PathAnchorRoot",
            "BoardRoot/PathAnchorRoot/PlayerEndAnchor",
            "BoardRoot/PathAnchorRoot/OpponentEndAnchor",
            "BoardRoot/EndPointRoot",
            "BoardRoot/EndPointRoot/PlayerEnd",
            "BoardRoot/EndPointRoot/OpponentEnd",
            "RuntimeRoot",
            "RuntimeRoot/EnemyRoot",
            "RuntimeRoot/SoldierRoot",
            "RuntimeRoot/ProjectileRoot",
            "RuntimeRoot/EffectRoot",
        };

        public Transform MapRoot { get; }
        public Transform BackgroundRoot { get; }
        public Transform Background { get; }
        public Transform ThemeRoot { get; }
        public Transform Mountains { get; }
        public Transform Birds { get; }
        public Transform Deer { get; }
        public Transform BoardRoot { get; }
        public Transform Ground { get; }
        public Transform Road { get; }
        public Transform HighGround { get; }
        public Transform Divide { get; }
        public Transform UnitSlotRoot { get; }
        public Transform SpawnPointRoot { get; }
        public Transform PlayerSpawn { get; }
        public Transform OpponentSpawn { get; }
        public Transform PathAnchorRoot { get; }
        public Transform PlayerEndAnchor { get; }
        public Transform OpponentEndAnchor { get; }
        public Transform EndPointRoot { get; }
        public Transform PlayerEnd { get; }
        public Transform OpponentEnd { get; }
        public Transform RuntimeRoot { get; }
        public Transform EnemyRoot { get; }
        public Transform SoldierRoot { get; }
        public Transform ProjectileRoot { get; }
        public Transform EffectRoot { get; }

        // ====================================================================
        // 当前地图运行几何（design.md 决策 5：坐标由 MapData 决定）
        // ====================================================================

        /// <summary>地图列数（x 维度）。</summary>
        private readonly int _gridWidth;

        /// <summary>地图行数（y 维度）。</summary>
        private readonly int _gridHeight;

        /// <summary>格子像素宽（驱动 X 逻辑坐标换算）。</summary>
        private readonly float _cellWidth;

        /// <summary>格子像素高（驱动 Y 逻辑坐标换算）。</summary>
        private readonly float _cellHeight;

        /// <summary>玩家路径起点格（PlayerSpawn 锚点格）。</summary>
        private readonly GridPosition _playerStartCell;

        /// <summary>玩家路径终点格（PlayerEndAnchor 锚点格）。</summary>
        private readonly GridPosition _playerEndCell;

        /// <summary>对手路径起点格（OpponentSpawn 锚点格）。</summary>
        private readonly GridPosition _opponentStartCell;

        /// <summary>对手路径终点格（OpponentEndAnchor 锚点格）。</summary>
        private readonly GridPosition _opponentEndCell;

        private BattleMapBindings(
            Transform mapRoot,
            IReadOnlyDictionary<string, Transform> nodes,
            MapData map)
        {
            MapRoot = mapRoot;
            BackgroundRoot = nodes["BackgroundRoot"];
            Background = nodes["BackgroundRoot/Background"];
            ThemeRoot = nodes["BackgroundRoot/ThemeRoot"];
            Mountains = nodes["BackgroundRoot/ThemeRoot/Mountains"];
            Birds = nodes["BackgroundRoot/ThemeRoot/Birds"];
            Deer = nodes["BackgroundRoot/ThemeRoot/Deer"];
            BoardRoot = nodes["BoardRoot"];
            Ground = nodes["BoardRoot/Ground"];
            Road = nodes["BoardRoot/Road"];
            HighGround = nodes["BoardRoot/HighGround"];
            Divide = nodes["BoardRoot/Divide"];
            UnitSlotRoot = nodes["BoardRoot/UnitSlotRoot"];
            SpawnPointRoot = nodes["BoardRoot/SpawnPointRoot"];
            PlayerSpawn = nodes["BoardRoot/SpawnPointRoot/PlayerSpawn"];
            OpponentSpawn = nodes["BoardRoot/SpawnPointRoot/OpponentSpawn"];
            PathAnchorRoot = nodes["BoardRoot/PathAnchorRoot"];
            PlayerEndAnchor = nodes["BoardRoot/PathAnchorRoot/PlayerEndAnchor"];
            OpponentEndAnchor = nodes["BoardRoot/PathAnchorRoot/OpponentEndAnchor"];
            EndPointRoot = nodes["BoardRoot/EndPointRoot"];
            PlayerEnd = nodes["BoardRoot/EndPointRoot/PlayerEnd"];
            OpponentEnd = nodes["BoardRoot/EndPointRoot/OpponentEnd"];
            RuntimeRoot = nodes["RuntimeRoot"];
            EnemyRoot = nodes["RuntimeRoot/EnemyRoot"];
            SoldierRoot = nodes["RuntimeRoot/SoldierRoot"];
            ProjectileRoot = nodes["RuntimeRoot/ProjectileRoot"];
            EffectRoot = nodes["RuntimeRoot/EffectRoot"];

            // 几何来自当前 MapData；旧入口（map==null）使用兼容 map0 数据。
            if (map != null)
            {
                _gridWidth = map.Width;
                _gridHeight = map.Height;
                _cellWidth = map.CellWidth > 0 ? map.CellWidth : LegacyCellSize;
                _cellHeight = map.CellHeight > 0 ? map.CellHeight : LegacyCellSize;
                _playerStartCell = map.PlayerStart;
                _playerEndCell = map.PlayerEnd;
                _opponentStartCell = map.OpponentStart;
                _opponentEndCell = map.OpponentEnd;
            }
            else
            {
                _gridWidth = LegacyGridWidth;
                _gridHeight = LegacyGridHeight;
                _cellWidth = LegacyCellSize;
                _cellHeight = LegacyCellSize;
                _playerStartCell = LegacyPlayerStart;
                _playerEndCell = LegacyPlayerEnd;
                _opponentStartCell = LegacyOpponentStart;
                _opponentEndCell = LegacyOpponentEnd;
            }
        }

        /// <summary>
        /// 将棋盘格映射到当前地图的 XY 世界坐标（尺寸与端点来自当前 MapData）。
        /// </summary>
        internal Vector3 CellToWorld(int gridX, int gridY, float sortingDepth = 0f)
        {
            if (TryGetEndpointPosition(gridX, gridY, sortingDepth, out Vector3 endpointPosition))
            {
                return endpointPosition;
            }

            return MapRoot.TransformPoint(new Vector3(
                gridX + 0.5f - _gridWidth * 0.5f,
                _gridHeight * 0.5f - (gridY + 0.5f),
                sortingDepth));
        }

        /// <summary>
        /// 将士兵格映射到表现根节点。士兵 Sprite 的身体中心位于根节点上方，
        /// 因此向下补偿锚点，使角色视觉中心与落点格中心重合。
        /// </summary>
        internal Vector3 UnitCellToWorld(int gridX, int gridY, float sortingDepth = 0f)
        {
            return CellToWorld(gridX, gridY, sortingDepth)
                   - MapRoot.up * UnitVisualAnchorOffsetY;
        }

        /// <summary>
        /// 将战斗逻辑使用的连续像素坐标映射到当前地图的 XY 世界坐标。
        /// <para>X 方向除以 CellWidth、Y 方向除以 CellHeight（design.md 决策 5）。</para>
        /// </summary>
        internal Vector3 LogicToWorld(float logicX, float logicY, float sortingDepth = 0f)
        {
            float gridX = logicX / _cellWidth;
            float gridY = logicY / _cellHeight;
            int roundedGridX = Mathf.RoundToInt(gridX);
            int roundedGridY = Mathf.RoundToInt(gridY);
            if (Mathf.Abs(gridX - roundedGridX) <= 0.0001f
                && Mathf.Abs(gridY - roundedGridY) <= 0.0001f)
            {
                return CellToWorld(roundedGridX, roundedGridY, sortingDepth);
            }

            return MapRoot.TransformPoint(new Vector3(
                gridX + 0.5f - _gridWidth * 0.5f,
                _gridHeight * 0.5f - (gridY + 0.5f),
                sortingDepth));
        }

        /// <summary>
        /// 将战斗逻辑角度（DisplayAngle 语义：0°朝上、90°朝右、Y 向下）转换为
        /// Unity 世界旋转（Z 轴，Sprite 默认朝上时为 0°）。
        /// </summary>
        /// <param name="logicDegrees">逻辑角度（度，DisplayAngle 语义）。</param>
        /// <returns>Unity Z 轴旋转角（度）。</returns>
        /// <remarks>
        /// <para>战斗逻辑用 DisplayAngle（0°朝上/90°朝右，ProjectileMath.DisplayAngle），
        /// Unity SpriteRenderer 默认朝向为"图片上方"，旋转经 Z 轴逆时针为正。
        /// 逻辑角度取负得到 Unity 旋转角（Y 轴翻转后顺时针旋转对应逻辑逆时针）。</para>
        /// <para>本方法集中所有"逻辑角 → Unity 旋转"换算，避免在表现层散落硬编码。</para>
        /// </remarks>
        internal Quaternion LogicAngleToWorld(float logicDegrees)
        {
            return Quaternion.Euler(0f, 0f, -logicDegrees);
        }

        private bool TryGetEndpointPosition(
            int gridX,
            int gridY,
            float sortingDepth,
            out Vector3 worldPosition)
        {
            // 出生点返回出生点自身位置；终点返回路径终点锚点位置（可见路径尽头）。
            // 端点格与锚点重合由绑定阶段保证（map0 旧入口由 TryCreate 校验；
            // 生产入口把通用节点放到所选地图的计算位置，决策 4）。
            Transform endpoint = null;
            if (gridX == _playerStartCell.X && gridY == _playerStartCell.Y) endpoint = PlayerSpawn;
            else if (gridX == _playerEndCell.X && gridY == _playerEndCell.Y) endpoint = PlayerEndAnchor;
            else if (gridX == _opponentStartCell.X && gridY == _opponentStartCell.Y) endpoint = OpponentSpawn;
            else if (gridX == _opponentEndCell.X && gridY == _opponentEndCell.Y) endpoint = OpponentEndAnchor;

            if (endpoint == null)
            {
                worldPosition = default;
                return false;
            }

            Vector3 position = endpoint.position;
            worldPosition = new Vector3(position.x, position.y, position.z + sortingDepth);
            return true;
        }

        /// <summary>将格子索引转换为战斗逻辑使用的像素原点。</summary>
        internal Vector2 CellToLogic(int gridX, int gridY)
        {
            return new Vector2(gridX * _cellWidth, gridY * _cellHeight);
        }

        /// <summary>取得包含士兵视觉锚点补偿的逻辑坐标，供逐帧同步使用。</summary>
        internal Vector2 UnitCellToLogic(int gridX, int gridY)
        {
            return new Vector2(
                gridX * _cellWidth,
                gridY * _cellHeight + UnitVisualAnchorOffsetY * _cellHeight);
        }

        /// <summary>
        /// 将主相机屏幕坐标映射为棋盘格。仅接受当前地图列数×行数范围内的点击。
        /// </summary>
        internal bool TryScreenToCell(
            Camera camera,
            float screenX,
            float screenY,
            out GridPosition position)
        {
            position = default;
            if (camera == null)
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
            Plane mapPlane = new Plane(MapRoot.forward, MapRoot.position);
            if (!mapPlane.Raycast(ray, out float distance))
            {
                return false;
            }

            Vector3 localPoint = MapRoot.InverseTransformPoint(ray.GetPoint(distance));
            int gridX = Mathf.FloorToInt(localPoint.x + _gridWidth * 0.5f);
            int gridY = Mathf.FloorToInt(_gridHeight * 0.5f - localPoint.y);
            if (gridX < 0 || gridX >= _gridWidth || gridY < 0 || gridY >= _gridHeight)
            {
                return false;
            }

            position = new GridPosition(gridX, gridY);
            return true;
        }

        /// <summary>取得指定阵营的出生点绑定。</summary>
        internal Transform GetSpawnPoint(bool playerSide)
        {
            return playerSide ? PlayerSpawn : OpponentSpawn;
        }

        /// <summary>取得指定阵营的终点绑定。</summary>
        internal Transform GetEndPoint(bool playerSide)
        {
            return playerSide ? PlayerEnd : OpponentEnd;
        }

        /// <summary>
        /// 校验固定路径并在全部路径唯一时创建绑定（兼容 map0 数据入口）。
        /// </summary>
        /// <remarks>
        /// 旧入口保留为兼容 map0 数据的显式入口（task 2.5）：使用 map0 几何与端点
        /// 校验，供尚未迁移的调用方与测试夹具使用。生产入口必须使用
        /// <see cref="TryCreate(UnityEngine.Transform, MapData)"/>。
        /// </remarks>
        internal static BattleMapBindingResult TryCreate(Transform mapRoot)
        {
            return CreateInternal(mapRoot, map: null);
        }

        /// <summary>
        /// 校验固定路径并以当前地图运行数据建立绑定（生产入口）。
        /// </summary>
        /// <param name="mapRoot">地图实例根节点。</param>
        /// <param name="map">当前地图运行快照（尺寸、cell 宽高、双路端点）。</param>
        /// <returns>绑定结果；生产路径会把通用端点节点放到所选地图的计算位置。</returns>
        /// <remarks>
        /// <para>节点只提供表现层层级与锚点（design.md 决策 4）：出生点、终点、尺寸
        /// 和坐标换算全部来自 <paramref name="map"/>，map0 不再拥有特殊分支
        /// （spec "Map bindings use runtime geometry"）。</para>
        /// </remarks>
        internal static BattleMapBindingResult TryCreate(Transform mapRoot, MapData map)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            return CreateInternal(mapRoot, map);
        }

        private static BattleMapBindingResult CreateInternal(Transform mapRoot, MapData map)
        {
            var missingPaths = new List<string>();
            var duplicatePaths = new List<string>();
            var invalidPaths = new List<string>();
            var nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);

            if (mapRoot == null)
            {
                missingPaths.Add("BattleMap0");
                return new BattleMapBindingResult(null, missingPaths, duplicatePaths, invalidPaths);
            }

            for (int index = 0; index < RequiredPaths.Length; index++)
            {
                string path = RequiredPaths[index];
                List<Transform> matches = FindMatches(mapRoot, path);
                if (matches.Count == 0)
                {
                    missingPaths.Add(path);
                }
                else if (matches.Count > 1)
                {
                    duplicatePaths.Add(path);
                }
                else
                {
                    nodes.Add(path, matches[0]);
                }
            }

            BattleMapBindings bindings = missingPaths.Count == 0 && duplicatePaths.Count == 0
                ? new BattleMapBindings(mapRoot, nodes, map)
                : null;
            if (bindings != null)
            {
                if (map == null)
                {
                    // 兼容 map0 路径：校验 prefab 端点与 map0 计算位置一致（保留既有语义）。
                    ValidateEndpoint(bindings, "BoardRoot/SpawnPointRoot/PlayerSpawn",
                        bindings._playerStartCell, invalidPaths);
                    ValidateEndpoint(bindings, "BoardRoot/SpawnPointRoot/OpponentSpawn",
                        bindings._opponentStartCell, invalidPaths);
                    ValidateAnchorEndpoint(bindings, "BoardRoot/EndPointRoot/PlayerEnd", invalidPaths);
                    ValidateAnchorEndpoint(bindings, "BoardRoot/EndPointRoot/OpponentEnd", invalidPaths);
                    if (invalidPaths.Count > 0)
                    {
                        bindings = null;
                    }
                }
                else
                {
                    // 生产路径：把通用端点节点放到所选地图的计算位置（设计决策 4）。
                    bindings.RepositionEndpoints();
                }
            }

            return new BattleMapBindingResult(bindings, missingPaths, duplicatePaths, invalidPaths);
        }

        /// <summary>
        /// 把通用端点节点放到当前地图的端点格计算位置（design.md 决策 4）。
        /// <para>出生点节点放到路径起点格，路径终点锚点与可见终点节点放到路径终点格。</para>
        /// </summary>
        private void RepositionEndpoints()
        {
            SetEndpointPosition(PlayerSpawn, _playerStartCell);
            SetEndpointPosition(OpponentSpawn, _opponentStartCell);
            SetEndpointPosition(PlayerEndAnchor, _playerEndCell);
            SetEndpointPosition(OpponentEndAnchor, _opponentEndCell);
            PlayerEnd.position = PlayerEndAnchor.position;
            OpponentEnd.position = OpponentEndAnchor.position;
        }

        /// <summary>按格子计算世界位置并写入节点。</summary>
        private void SetEndpointPosition(Transform node, GridPosition cell)
        {
            node.position = MapRoot.TransformPoint(new Vector3(
                cell.X + 0.5f - _gridWidth * 0.5f,
                _gridHeight * 0.5f - (cell.Y + 0.5f),
                0f));
        }

        private static void ValidateEndpoint(
            BattleMapBindings bindings,
            string path,
            GridPosition cell,
            List<string> invalidPaths)
        {
            Transform endpoint = path.EndsWith("PlayerSpawn", StringComparison.Ordinal)
                ? bindings.PlayerSpawn
                : bindings.OpponentSpawn;
            Vector3 expected = bindings.MapRoot.TransformPoint(new Vector3(
                cell.X + 0.5f - bindings._gridWidth * 0.5f,
                bindings._gridHeight * 0.5f - (cell.Y + 0.5f),
                0f));
            if ((endpoint.position - expected).sqrMagnitude > 0.000001f)
            {
                invalidPaths.Add(path);
            }
        }

        /// <summary>
        /// 校验终点节点（PlayerEnd/OpponentEnd）与对应路径终点锚点（PlayerEndAnchor/
        /// OpponentEndAnchor）重合，确保敌人走到逻辑路径末端时渲染位置准确落到
        /// 可见路径终点（即阿斗所在位置）。
        /// </summary>
        private static void ValidateAnchorEndpoint(
            BattleMapBindings bindings,
            string path,
            List<string> invalidPaths)
        {
            bool isPlayerEnd = path.EndsWith("PlayerEnd", StringComparison.Ordinal);
            Transform endpoint = isPlayerEnd ? bindings.PlayerEnd : bindings.OpponentEnd;
            Transform anchor = isPlayerEnd ? bindings.PlayerEndAnchor : bindings.OpponentEndAnchor;
            if ((endpoint.position - anchor.position).sqrMagnitude > 0.000001f)
            {
                invalidPaths.Add(path);
            }
        }

        private static List<Transform> FindMatches(Transform root, string path)
        {
            var current = new List<Transform> { root };
            string[] segments = path.Split('/');
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                string segment = segments[segmentIndex];
                var next = new List<Transform>();
                for (int parentIndex = 0; parentIndex < current.Count; parentIndex++)
                {
                    Transform parent = current[parentIndex];
                    for (int childIndex = 0; childIndex < parent.childCount; childIndex++)
                    {
                        Transform child = parent.GetChild(childIndex);
                        if (string.Equals(child.name, segment, StringComparison.Ordinal))
                        {
                            next.Add(child);
                        }
                    }
                }

                current = next;
                if (current.Count == 0)
                {
                    break;
                }
            }

            return current;
        }
    }
}
