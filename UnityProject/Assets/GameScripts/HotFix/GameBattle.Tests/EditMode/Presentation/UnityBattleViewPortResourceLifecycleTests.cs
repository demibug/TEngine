using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBattle;
using NUnit.Framework;
using UnityEngine;

namespace GameBattle.Tests.EditMode.Presentation
{
    [TestFixture]
    internal sealed class UnityBattleViewPortResourceLifecycleTests
    {
        private GameObject _map;
        private BattleMapBindings _bindings;
        private FakeAssetLoader _loader;
        private UnityBattleViewPort _viewPort;

        [SetUp]
        public void SetUp()
        {
            _map = CreateValidMap();
            BattleMapBindingResult result = BattleMapBindings.TryCreate(_map.transform);
            Assert.IsTrue(result.IsValid, result.DiagnosticMessage);
            _bindings = result.Bindings;
            _loader = new FakeAssetLoader();
            _viewPort = new UnityBattleViewPort(_bindings, _loader);
        }

        [TearDown]
        public void TearDown()
        {
            _viewPort?.Clear();
            _loader?.DestroyAssets();
            if (_map != null)
            {
                UnityEngine.Object.DestroyImmediate(_map);
            }
        }

        [Test]
        public async Task Preload_SpawnAndRemove_SelectsAddressPoolAndResetsReusedHealthBar()
        {
            await _viewPort.PreloadAsync(new[] { "Mob1", "Mob3" }, CancellationToken.None);

            _viewPort.OnEnemySpawned(new EnemySpawnViewData(101, "Mob1", "Mob1", true, 0f, 0f));
            GameObject firstMob1 = FindActiveEnemy("Mob1(Clone)");
            EnemyHealthBarView firstHealthBar = firstMob1.GetComponent<EnemyHealthBarView>();
            firstHealthBar.ShowWithRatio(0.25f);

            _viewPort.OnEnemyRemoved(101, true);
            Assert.IsFalse(firstMob1.activeSelf, "移除后敌人表现应进入非激活池。");
            Assert.IsFalse(firstHealthBar.IsVisible, "回池时必须隐藏旧血条。");
            Assert.AreEqual(1f, firstHealthBar.Ratio, 0.0001f, "回池时必须恢复满血比例。");

            _viewPort.OnEnemySpawned(new EnemySpawnViewData(102, "Mob1", "Mob1", true, 1f, 0f));
            GameObject reusedMob1 = FindActiveEnemy("Mob1(Clone)");
            Assert.AreSame(firstMob1, reusedMob1, "同一 resourceAddress 应复用同一表现池实例。");
            Assert.IsFalse(reusedMob1.GetComponent<EnemyHealthBarView>().IsVisible);
            Assert.AreEqual(1f, reusedMob1.GetComponent<EnemyHealthBarView>().Ratio, 0.0001f);

            _viewPort.OnEnemySpawned(new EnemySpawnViewData(103, "Mob3", "Mob3", false, 2f, 0f));
            Assert.IsNotNull(FindActiveEnemy("Mob3(Clone)"), "Mob3 必须按自己的资源地址选择 Prefab。");

            BattlePresentationLoadException exception = Assert.Throws<BattlePresentationLoadException>(() =>
                _viewPort.OnEnemySpawned(
                    new EnemySpawnViewData(104, "Mob2", "Mob2", true, 3f, 0f)));
            Assert.AreEqual("Mob2", exception.ResourceAddress, "未预加载地址必须显式失败，禁止回退 Mob0。");
        }

        [Test]
        public async Task Preload_InvalidLease_ReleasesPartialBatchAndCanRetry()
        {
            _loader.InvalidAtCall = 2;

            BattlePresentationLoadException loadException = null;
            try
            {
                await _viewPort.PreloadAsync(new[] { "Mob0", "Mob1" }, CancellationToken.None);
            }
            catch (BattlePresentationLoadException exception)
            {
                loadException = exception;
            }

            Assert.IsNotNull(loadException, "无效租约应中止预加载并抛出加载异常。");

            Assert.GreaterOrEqual(_loader.Leases.Count, 2);
            AssertAllDisposed(_loader.Leases, "失败回滚必须释放本次已取得的全部租约。");

            int failedBatchCount = _loader.Leases.Count;
            _loader.InvalidAtCall = null;
            await _viewPort.PreloadAsync(new[] { "Mob0", "Mob1" }, CancellationToken.None);

            Assert.Greater(_loader.Leases.Count, failedBatchCount, "失败后必须允许完整重试。");
            Assert.IsTrue(_loader.Leases.Exists(lease => lease.DisposeCount == 0),
                "成功预加载的租约应持有到 Clear，而不是提前释放。");

            _viewPort.Clear();
            AssertAllDisposed(_loader.Leases, "Clear 必须释放重试成功后持有的全部租约。");
            _viewPort.Clear();
            Assert.IsTrue(_loader.Leases.TrueForAll(lease => lease.DisposeCount == 1),
                "重复 Clear 必须保持释放幂等。");
        }

        [Test]
        public async Task Preload_CanceledAfterFirstLease_ReleasesAndCanRetry()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                _loader.OnLoad = callIndex =>
                {
                    if (callIndex == 1)
                    {
                        cancellation.Cancel();
                    }
                };

                bool canceled = false;
                try
                {
                    await _viewPort.PreloadAsync(new[] { "Mob0" }, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.IsTrue(canceled, "预加载应传播取消异常。");
            }

            Assert.GreaterOrEqual(_loader.Leases.Count, 1);
            AssertAllDisposed(_loader.Leases, "取消必须释放取消前已取得的租约。");

            int canceledBatchCount = _loader.Leases.Count;
            _loader.OnLoad = null;
            await _viewPort.PreloadAsync(new[] { "Mob0" }, CancellationToken.None);
            Assert.Greater(_loader.Leases.Count, canceledBatchCount, "取消后必须允许重新预加载。");
        }

        private GameObject FindActiveEnemy(string expectedName)
        {
            Transform root = _bindings.EnemyRoot;
            for (int index = 0; index < root.childCount; index++)
            {
                GameObject child = root.GetChild(index).gameObject;
                if (child.activeSelf && string.Equals(child.name, expectedName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            Assert.Fail($"未找到活动敌人表现 {expectedName}");
            return null;
        }

        private static void AssertAllDisposed(IReadOnlyList<FakeLease> leases, string message)
        {
            for (int index = 0; index < leases.Count; index++)
            {
                Assert.AreEqual(1, leases[index].DisposeCount, $"{message} leaseIndex={index}");
            }
        }

        private static GameObject CreateValidMap()
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
            AddEndpoint(map.transform, "BoardRoot/PathAnchorRoot/PlayerEndAnchor", 7, 9);
            AddEndpoint(map.transform, "BoardRoot/PathAnchorRoot/OpponentEndAnchor", 0, 0);
            AddPath(map.transform, "RuntimeRoot/EnemyRoot");
            AddPath(map.transform, "RuntimeRoot/SoldierRoot");
            AddPath(map.transform, "RuntimeRoot/ProjectileRoot");
            AddPath(map.transform, "RuntimeRoot/EffectRoot");
            return map;
        }

        private static void AddEndpoint(Transform root, string path, int gridX, int gridY)
        {
            Transform endpoint = AddPath(root, path);
            endpoint.position = new Vector3(gridX + 0.5f - 4f, 5f - (gridY + 0.5f), 0f);
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

        private sealed class FakeAssetLoader : IBattleViewAssetLoader
        {
            private readonly Dictionary<string, GameObject> _prefabs =
                new Dictionary<string, GameObject>(StringComparer.Ordinal);
            private readonly Texture2D _texture;
            private readonly Sprite _sprite;
            private int _callCount;

            internal FakeAssetLoader()
            {
                _texture = new Texture2D(2, 2);
                _sprite = Sprite.Create(
                    _texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
            }

            internal List<FakeLease> Leases { get; } = new List<FakeLease>();

            internal int? InvalidAtCall { get; set; }

            internal Action<int> OnLoad { get; set; }

            public IBattleAssetLease LoadAsync<T>(string location) where T : UnityEngine.Object
            {
                int callIndex = ++_callCount;
                OnLoad?.Invoke(callIndex);

                bool valid = InvalidAtCall != callIndex;
                object asset = null;
                if (valid && typeof(T) == typeof(GameObject))
                {
                    asset = GetOrCreatePrefab(location);
                }
                else if (valid && typeof(T) == typeof(Sprite))
                {
                    asset = _sprite;
                }

                var lease = new FakeLease(valid, asset);
                Leases.Add(lease);
                return lease;
            }

            internal void DestroyAssets()
            {
                foreach (GameObject prefab in _prefabs.Values)
                {
                    if (prefab != null)
                    {
                        UnityEngine.Object.DestroyImmediate(prefab);
                    }
                }

                _prefabs.Clear();
                if (_sprite != null)
                {
                    UnityEngine.Object.DestroyImmediate(_sprite);
                }

                if (_texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(_texture);
                }
            }

            private GameObject GetOrCreatePrefab(string location)
            {
                if (_prefabs.TryGetValue(location, out GameObject prefab))
                {
                    return prefab;
                }

                prefab = new GameObject(location);
                GameObject body = new GameObject("Body");
                body.transform.SetParent(prefab.transform, false);
                body.AddComponent<SpriteRenderer>().sprite = _sprite;

                Transform visualRoot = new GameObject("VisualRoot").transform;
                visualRoot.SetParent(prefab.transform, false);
                Transform healthRoot = new GameObject("hpBgImg").transform;
                healthRoot.SetParent(visualRoot, false);
                healthRoot.gameObject.AddComponent<SpriteRenderer>().sprite = _sprite;
                AddHealthFill(healthRoot, "hpImg1");
                AddHealthFill(healthRoot, "hpImg2");

                prefab.SetActive(false);
                _prefabs.Add(location, prefab);
                return prefab;
            }

            private void AddHealthFill(Transform parent, string name)
            {
                GameObject fill = new GameObject(name);
                fill.transform.SetParent(parent, false);
                SpriteRenderer renderer = fill.AddComponent<SpriteRenderer>();
                renderer.sprite = _sprite;
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.size = new Vector2(1f, 0.1f);
            }
        }

        private sealed class FakeLease : IBattleAssetLease
        {
            private readonly bool _isValid;
            private readonly object _asset;

            internal FakeLease(bool isValid, object asset)
            {
                _isValid = isValid;
                _asset = asset;
            }

            public bool IsDone => true;

            public bool IsValid => _isValid && DisposeCount == 0;

            public object AssetObject => _asset;

            internal int DisposeCount { get; private set; }

            public void Dispose()
            {
                if (DisposeCount == 0)
                {
                    DisposeCount = 1;
                }
            }
        }
    }
}
