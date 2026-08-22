using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using NUnit.Framework;
using UnityEngine;

namespace GameBattle.Tests.EditMode
{
    /// <summary>单场景战斗世界回归测试。</summary>
    [TestFixture]
    internal sealed class BattleWorldLifecycleRegressionTests
    {
        [Test]
        public async Task Exit_HandlerFailure_RemainsFaultedAndCanRetry()
        {
            int exitCalls = 0;
            var module = new BattleModule(
                (loadout, scope) => UniTask.FromResult(
                    BattleOperationResult.Ok(BattleModuleState.Running)),
                scope =>
                {
                    exitCalls++;
                    return exitCalls == 1
                        ? UniTask.FromResult(BattleOperationResult.Fail(
                            BattleErrorCode.UILoadFailed,
                            BattleModuleState.Exiting,
                            "模拟入口恢复失败",
                            BattleFailureStage.Exit))
                        : UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Idle));
                });

            Assert.IsTrue((await module.StartAsync(BattleLoadoutDto.CreateMinimalDefault())).IsSuccess);

            BattleOperationResult firstExit = await module.ExitAsync();
            Assert.IsFalse(firstExit.IsSuccess);
            Assert.AreEqual(BattleModuleState.Faulted, module.State);

            BattleOperationResult retryExit = await module.ExitAsync();
            Assert.IsTrue(retryExit.IsSuccess);
            Assert.AreEqual(BattleModuleState.Idle, module.State);
            Assert.AreEqual(2, exitCalls);
        }

        [Test]
        public async Task EnsureWorld_ReleaseInvalidatesLateLoad_DestroysOrphanAndCanRetry()
        {
            var firstLoadAllowed = new UniTaskCompletionSource();
            int loadCount = 0;
            GameObject lateInstance = null;
            var host = new BattleWorldHost(async parent =>
            {
                if (loadCount++ == 0)
                {
                    await firstLoadAllowed.Task;
                    lateInstance = CreateValidMap();
                    return lateInstance;
                }

                GameObject map = CreateValidMap();
                map.transform.SetParent(parent, false);
                return map;
            });

            try
            {
                UniTask<GameObject> firstLoad = host.EnsureWorldAsync();
                host.Release(destroyImmediate: true);
                firstLoadAllowed.TrySetResult();

                bool invalidated = false;
                try
                {
                    await firstLoad;
                }
                catch (BattleMapLoadException)
                {
                    invalidated = true;
                }

                Assert.IsTrue(invalidated, "Release 后旧加载必须被判定为失效。");
                Assert.IsNull(host.MapInstance, "迟到实例不得提交到 WorldHost。");
                await UniTask.Yield();
                Assert.IsTrue(lateInstance == null, "迟到实例必须被销毁。");

                GameObject map = await host.EnsureWorldAsync();
                Assert.IsNotNull(map);
                Assert.AreEqual(1, host.WorldRoot.childCount, "重试只能创建一张地图。");
            }
            finally
            {
                if (lateInstance != null)
                {
                    UnityEngine.Object.DestroyImmediate(lateInstance);
                }

                host.Release(destroyImmediate: true);
            }
        }

        [Test]
        public async Task EnsureWorld_StaleCompletionDoesNotClearNewerLoad()
        {
            var firstLoadAllowed = new UniTaskCompletionSource();
            var secondLoadAllowed = new UniTaskCompletionSource();
            int loadCount = 0;
            GameObject firstLateInstance = null;
            GameObject secondInstance = null;
            var host = new BattleWorldHost(async parent =>
            {
                int loadIndex = loadCount++;
                if (loadIndex == 0)
                {
                    await firstLoadAllowed.Task;
                    firstLateInstance = CreateValidMap();
                    return firstLateInstance;
                }

                if (loadIndex == 1)
                {
                    await secondLoadAllowed.Task;
                    secondInstance = CreateValidMap();
                    secondInstance.transform.SetParent(parent, false);
                    return secondInstance;
                }

                throw new InvalidOperationException("新一代在途加载被旧加载覆盖，触发了第三次加载。");
            });

            try
            {
                UniTask<GameObject> firstLoad = host.EnsureWorldAsync();
                host.Release(destroyImmediate: true);

                UniTask<GameObject> secondLoad = host.EnsureWorldAsync();
                firstLoadAllowed.TrySetResult();
                Assert.ThrowsAsync<BattleMapLoadException>(async () => await firstLoad);

                // 旧加载收尾后再次请求必须继续复用第二个任务，不能创建第三个加载。
                UniTask<GameObject> sharedSecondLoad = host.EnsureWorldAsync();
                Assert.AreEqual(2, loadCount);

                secondLoadAllowed.TrySetResult();
                GameObject loaded = await secondLoad;
                GameObject sharedLoaded = await sharedSecondLoad;

                Assert.AreSame(loaded, sharedLoaded);
                Assert.AreSame(secondInstance, host.MapInstance);
                Assert.AreEqual(1, host.WorldRoot.childCount);
                await UniTask.Yield();
                Assert.IsTrue(firstLateInstance == null, "旧代迟到实例必须被销毁。");
            }
            finally
            {
                firstLoadAllowed.TrySetResult();
                secondLoadAllowed.TrySetResult();
                if (firstLateInstance != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstLateInstance);
                }

                host.Release(destroyImmediate: true);
            }
        }

        [Test]
        public void Bindings_RejectMisalignedEndpoint_AndUseEndpointPosition()
        {
            GameObject invalidMap = CreateValidMap();
            GameObject validMap = CreateValidMap();
            try
            {
                invalidMap.transform.Find("BoardRoot/SpawnPointRoot/PlayerSpawn").localPosition += Vector3.right;
                BattleMapBindingResult invalid = BattleMapBindings.TryCreate(invalidMap.transform);
                Assert.IsFalse(invalid.IsValid);
                CollectionAssert.Contains(invalid.InvalidPaths, "BoardRoot/SpawnPointRoot/PlayerSpawn");

                BattleMapBindingResult valid = BattleMapBindings.TryCreate(validMap.transform);
                Assert.IsTrue(valid.IsValid, valid.DiagnosticMessage);
                Vector3 position = valid.Bindings.CellToWorld(0, 8);
                Assert.AreEqual(valid.Bindings.PlayerSpawn.position, position);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalidMap);
                UnityEngine.Object.DestroyImmediate(validMap);
            }
        }

        [Test]
        public void Bindings_RejectEndpointMisalignedWithAnchor_AndReturnAnchorPosition()
        {
            GameObject invalidMap = CreateValidMap();
            GameObject validMap = CreateValidMap();
            try
            {
                // 终点偏离路径锚点（可见路径终点）时，绑定必须失败。
                invalidMap.transform.Find("BoardRoot/EndPointRoot/PlayerEnd").localPosition += Vector3.right;
                BattleMapBindingResult invalid = BattleMapBindings.TryCreate(invalidMap.transform);
                Assert.IsFalse(invalid.IsValid);
                CollectionAssert.Contains(invalid.InvalidPaths, "BoardRoot/EndPointRoot/PlayerEnd");

                // 终点与锚点重合时，终点格返回锚点（可见路径尽头）位置。
                BattleMapBindingResult valid = BattleMapBindings.TryCreate(validMap.transform);
                Assert.IsTrue(valid.IsValid, valid.DiagnosticMessage);
                Vector3 playerEnd = valid.Bindings.CellToWorld(7, 9);
                Assert.AreEqual(valid.Bindings.PlayerEndAnchor.position, playerEnd);
                Vector3 opponentEnd = valid.Bindings.CellToWorld(0, 0);
                Assert.AreEqual(valid.Bindings.OpponentEndAnchor.position, opponentEnd);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalidMap);
                UnityEngine.Object.DestroyImmediate(validMap);
            }
        }

        [Test]
        public void CloseEntryAfterHud_CloseFailureReturnsStructuredFailure()
        {
            BattleOperationResult result = BattleModule.CloseEntryAfterHudForTransaction(
                () => true,
                () => throw new InvalidOperationException("模拟关闭失败"));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(BattleErrorCode.UILoadFailed, result.ErrorCode);
            Assert.AreEqual(BattleFailureStage.HudOpen, result.FailureStage);
        }

        private static GameObject CreateValidMap()
        {
            GameObject map = new GameObject("BattleMap0");
            Transform background = AddPath(map.transform, "BackgroundRoot/Background");
            AddPath(background.parent, "ThemeRoot/Mountains");
            AddPath(background.parent, "ThemeRoot/Birds");
            AddPath(background.parent, "ThemeRoot/Deer");
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
            return map;
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
