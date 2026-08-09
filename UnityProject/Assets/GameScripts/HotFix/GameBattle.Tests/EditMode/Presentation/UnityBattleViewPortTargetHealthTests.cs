using GameBattle;
using NUnit.Framework;
using UnityEngine;

namespace GameBattle.Tests.EditMode.Presentation
{
    /// <summary>
    /// UnityBattleViewPort 双侧阿斗血量刷新测试。
    /// </summary>
    /// <remarks>
    /// 验证开局双方各三颗心满血；我方扣血只隐藏我方阿斗的心，对方扣血只隐藏
    /// 对方阿斗的心。使用真实 BattleMapBindings 构造 + 真 UnityBattleViewPort，
    /// 不依赖场景与资源加载（血量节点只需存在 SpriteRenderer）。
    /// </remarks>
    [TestFixture]
    internal sealed class UnityBattleViewPortTargetHealthTests
    {
        private GameObject _map;
        private BattleMapBindings _bindings;

        [SetUp]
        public void SetUp()
        {
            _map = CreateMapWithDou();
            BattleMapBindingResult result = BattleMapBindings.TryCreate(_map.transform);
            Assert.IsTrue(result.IsValid, result.DiagnosticMessage);
            _bindings = result.Bindings;
        }

        [TearDown]
        public void TearDown()
        {
            if (_map != null)
            {
                Object.DestroyImmediate(_map);
            }
        }

        [Test]
        public void OnBattleStarted_InitializesBothSidesFullHealth()
        {
            var viewPort = new UnityBattleViewPort(_bindings);
            try
            {
                viewPort.OnBattleStarted(0, 3, 3);

                AssertAllEnabled(GetHealthPoints(_bindings.PlayerEnd));
                AssertAllEnabled(GetHealthPoints(_bindings.OpponentEnd));
            }
            finally
            {
                viewPort.Clear();
            }
        }

        [Test]
        public void OnHealthChanged_PlayerDamageHidesOnlyPlayerHearts()
        {
            var viewPort = new UnityBattleViewPort(_bindings);
            try
            {
                viewPort.OnBattleStarted(0, 3, 3);
                viewPort.OnHealthChanged(true, 2, 3, -1);

                AssertVisibleCount(GetHealthPoints(_bindings.PlayerEnd), 2);
                AssertAllEnabled(GetHealthPoints(_bindings.OpponentEnd));
            }
            finally
            {
                viewPort.Clear();
            }
        }

        [Test]
        public void OnHealthChanged_OpponentDamageHidesOnlyOpponentHearts()
        {
            var viewPort = new UnityBattleViewPort(_bindings);
            try
            {
                viewPort.OnBattleStarted(0, 3, 3);
                viewPort.OnHealthChanged(false, 1, 3, -1);

                AssertVisibleCount(GetHealthPoints(_bindings.OpponentEnd), 1);
                AssertAllEnabled(GetHealthPoints(_bindings.PlayerEnd));
            }
            finally
            {
                viewPort.Clear();
            }
        }

        [Test]
        public void OnHealthChanged_BothSidesTrackIndependently()
        {
            var viewPort = new UnityBattleViewPort(_bindings);
            try
            {
                viewPort.OnBattleStarted(0, 3, 3);
                viewPort.OnHealthChanged(true, 2, 3, -1);
                viewPort.OnHealthChanged(false, 2, 3, -1);

                AssertVisibleCount(GetHealthPoints(_bindings.PlayerEnd), 2);
                AssertVisibleCount(GetHealthPoints(_bindings.OpponentEnd), 2);

                viewPort.OnHealthChanged(true, 1, 3, -1);

                AssertVisibleCount(GetHealthPoints(_bindings.PlayerEnd), 1);
                AssertVisibleCount(GetHealthPoints(_bindings.OpponentEnd), 2);
            }
            finally
            {
                viewPort.Clear();
            }
        }

        [Test]
        public void Clear_ResetsCaches_AndCanRestart()
        {
            var viewPort = new UnityBattleViewPort(_bindings);
            try
            {
                viewPort.OnBattleStarted(0, 3, 3);
                viewPort.OnHealthChanged(true, 1, 3, -1);
                viewPort.Clear();

                // 重启后重新初始化双方满血，不再残留上一局隐藏状态。
                viewPort.OnBattleStarted(0, 3, 3);
                AssertAllEnabled(GetHealthPoints(_bindings.PlayerEnd));
                AssertAllEnabled(GetHealthPoints(_bindings.OpponentEnd));
            }
            finally
            {
                viewPort.Clear();
            }
        }

        private static SpriteRenderer[] GetHealthPoints(Transform endPoint)
        {
            Transform healthRoot = endPoint.Find("ADou/HealthRoot");
            var healthPoints = new SpriteRenderer[3];
            for (int index = 0; index < healthPoints.Length; index++)
            {
                Transform healthPoint = healthRoot.Find($"HealthPoint{index + 1}");
                healthPoints[index] = healthPoint.GetComponent<SpriteRenderer>();
            }

            return healthPoints;
        }

        private static void AssertAllEnabled(SpriteRenderer[] healthPoints)
        {
            AssertVisibleCount(healthPoints, healthPoints.Length);
        }

        private static void AssertVisibleCount(SpriteRenderer[] healthPoints, int visibleCount)
        {
            for (int index = 0; index < healthPoints.Length; index++)
            {
                Assert.AreEqual(
                    index < visibleCount,
                    healthPoints[index].enabled,
                    $"HealthPoint{index + 1} 显隐不符，期望可见数 {visibleCount}");
            }
        }

        private static GameObject CreateMapWithDou()
        {
            GameObject map = new GameObject("BattleMap0");
            AddPath(map.transform, "BackgroundRoot/Background");
            AddPath(map.transform, "BackgroundRoot/ThemeRoot/Mountains");
            AddPath(map.transform, "BackgroundRoot/ThemeRoot/Birds");
            AddPath(map.transform, "BackgroundRoot/ThemeRoot/Deer");
            AddPath(map.transform, "BoardRoot/Ground");
            AddPath(map.transform, "BoardRoot/Road");
            AddPath(map.transform, "BoardRoot/HighGround");
            AddPath(map.transform, "BoardRoot/Divide");
            AddPath(map.transform, "BoardRoot/UnitSlotRoot");
            AddEndpoint(map.transform, "BoardRoot/SpawnPointRoot/PlayerSpawn", 0, 8);
            AddEndpoint(map.transform, "BoardRoot/SpawnPointRoot/OpponentSpawn", 7, 1);
            AddEndpoint(map.transform, "BoardRoot/EndPointRoot/PlayerEnd", 7, 9);
            AddEndpoint(map.transform, "BoardRoot/EndPointRoot/OpponentEnd", 0, 0);
            AddAnchor(map.transform, "BoardRoot/PathAnchorRoot/PlayerEndAnchor", 7, 9);
            AddAnchor(map.transform, "BoardRoot/PathAnchorRoot/OpponentEndAnchor", 0, 0);
            AddPath(map.transform, "RuntimeRoot/EnemyRoot");
            AddPath(map.transform, "RuntimeRoot/SoldierRoot");
            AddPath(map.transform, "RuntimeRoot/ProjectileRoot");
            AddPath(map.transform, "RuntimeRoot/EffectRoot");

            AddAdou(map.transform.Find("BoardRoot/EndPointRoot/PlayerEnd"));
            AddAdou(map.transform.Find("BoardRoot/EndPointRoot/OpponentEnd"));
            return map;
        }

        private static void AddAdou(Transform endPoint)
        {
            Transform adou = AddPath(endPoint, "ADou/HealthRoot");
            for (int index = 1; index <= 3; index++)
            {
                GameObject healthPoint = new GameObject($"HealthPoint{index}");
                healthPoint.transform.SetParent(adou, false);
                healthPoint.AddComponent<SpriteRenderer>();
            }
        }

        private static void AddEndpoint(Transform root, string path, int gridX, int gridY)
        {
            Transform endpoint = AddPath(root, path);
            endpoint.position = new Vector3(gridX + 0.5f - 4f, 5f - (gridY + 0.5f), 0f);
        }

        private static void AddAnchor(Transform root, string path, int gridX, int gridY)
        {
            Transform anchor = AddPath(root, path);
            anchor.position = new Vector3(gridX + 0.5f - 4f, 5f - (gridY + 0.5f), 0f);
        }

        private static Transform AddPath(Transform root, string path)
        {
            Transform current = root;
            string[] segments = path.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                Transform child = current.Find(segments[index]);
                if (child == null)
                {
                    child = new GameObject(segments[index]).transform;
                    child.SetParent(current, false);
                }

                current = child;
            }

            return current;
        }
    }
}
