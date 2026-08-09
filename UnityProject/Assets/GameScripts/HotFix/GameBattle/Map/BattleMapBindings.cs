using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// BattleMap0 固定节点的绑定结果。
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
    /// </summary>
    internal sealed class BattleMapBindings
    {
        private const float LogicCellSize = 80f;
        private const int GridWidth = 8;
        private const int GridHeight = 10;
        private const float UnitVisualAnchorOffsetY = 0.35f;

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

        private BattleMapBindings(Transform mapRoot, IReadOnlyDictionary<string, Transform> nodes)
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
        }

        /// <summary>
        /// 将 8×10 棋盘格映射到 BattleMap0 的 XY 世界坐标。
        /// </summary>
        internal Vector3 CellToWorld(int gridX, int gridY, float sortingDepth = 0f)
        {
            if (TryGetEndpointPosition(gridX, gridY, sortingDepth, out Vector3 endpointPosition))
            {
                return endpointPosition;
            }

            return MapRoot.TransformPoint(new Vector3(
                gridX + 0.5f - GridWidth * 0.5f,
                GridHeight * 0.5f - (gridY + 0.5f),
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
        /// 将战斗逻辑使用的连续像素坐标映射到 BattleMap0 的 XY 世界坐标。
        /// </summary>
        internal Vector3 LogicToWorld(float logicX, float logicY, float sortingDepth = 0f)
        {
            float gridX = logicX / LogicCellSize;
            float gridY = logicY / LogicCellSize;
            int roundedGridX = Mathf.RoundToInt(gridX);
            int roundedGridY = Mathf.RoundToInt(gridY);
            if (Mathf.Abs(gridX - roundedGridX) <= 0.0001f
                && Mathf.Abs(gridY - roundedGridY) <= 0.0001f)
            {
                return CellToWorld(roundedGridX, roundedGridY, sortingDepth);
            }

            return MapRoot.TransformPoint(new Vector3(
                gridX + 0.5f - GridWidth * 0.5f,
                GridHeight * 0.5f - (gridY + 0.5f),
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
            // 出生点返回出生点自身位置；终点返回路径终点锚点位置（可见路径尽头，
            // 即阿斗所在位置）。终点格与锚点重合由 TryCreate 校验保证。
            Transform endpoint = null;
            if (gridX == 0 && gridY == 8) endpoint = PlayerSpawn;
            else if (gridX == 7 && gridY == 9) endpoint = PlayerEndAnchor;
            else if (gridX == 7 && gridY == 1) endpoint = OpponentSpawn;
            else if (gridX == 0 && gridY == 0) endpoint = OpponentEndAnchor;

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
            return new Vector2(gridX * LogicCellSize, gridY * LogicCellSize);
        }

        /// <summary>取得包含士兵视觉锚点补偿的逻辑坐标，供逐帧同步使用。</summary>
        internal Vector2 UnitCellToLogic(int gridX, int gridY)
        {
            return new Vector2(
                gridX * LogicCellSize,
                gridY * LogicCellSize + UnitVisualAnchorOffsetY * LogicCellSize);
        }

        /// <summary>
        /// 将主相机屏幕坐标映射为棋盘格。仅接受 BattleMap0 8×10 范围内的点击。
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
            int gridX = Mathf.FloorToInt(localPoint.x + GridWidth * 0.5f);
            int gridY = Mathf.FloorToInt(GridHeight * 0.5f - localPoint.y);
            if (gridX < 0 || gridX >= GridWidth || gridY < 0 || gridY >= GridHeight)
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
        /// 校验固定路径并在全部路径唯一时创建绑定。
        /// </summary>
        internal static BattleMapBindingResult TryCreate(Transform mapRoot)
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
                ? new BattleMapBindings(mapRoot, nodes)
                : null;
            if (bindings != null)
            {
                ValidateEndpoint(bindings, "BoardRoot/SpawnPointRoot/PlayerSpawn", 0, 8, invalidPaths);
                ValidateEndpoint(bindings, "BoardRoot/SpawnPointRoot/OpponentSpawn", 7, 1, invalidPaths);
                ValidateAnchorEndpoint(bindings, "BoardRoot/EndPointRoot/PlayerEnd", invalidPaths);
                ValidateAnchorEndpoint(bindings, "BoardRoot/EndPointRoot/OpponentEnd", invalidPaths);
                if (invalidPaths.Count > 0)
                {
                    bindings = null;
                }
            }

            return new BattleMapBindingResult(bindings, missingPaths, duplicatePaths, invalidPaths);
        }

        private static void ValidateEndpoint(
            BattleMapBindings bindings,
            string path,
            int gridX,
            int gridY,
            List<string> invalidPaths)
        {
            Transform endpoint = path.EndsWith("PlayerSpawn", StringComparison.Ordinal)
                ? bindings.PlayerSpawn
                : bindings.OpponentSpawn;
            Vector3 expected = bindings.MapRoot.TransformPoint(new Vector3(
                gridX + 0.5f - GridWidth * 0.5f,
                GridHeight * 0.5f - (gridY + 0.5f),
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
