using System;
using System.Threading;
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
                (loadout, scope, token) => UniTask.FromResult(
                    BattleOperationResult.Ok(BattleModuleState.Running)),
                (scope, token) =>
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
        public async Task EnsureWorld_CanceledAfterInstanceCreated_DestroysOrphanAndCanRetry()
        {
            using var cancellationSource = new CancellationTokenSource();
            int loadCount = 0;
            var host = new BattleWorldHost((parent, token) =>
            {
                GameObject map = CreateValidMap();
                map.transform.SetParent(parent, false);
                if (loadCount++ == 0)
                {
                    cancellationSource.Cancel();
                }

                return UniTask.FromResult(map);
            });

            try
            {
                bool canceled = false;
                try
                {
                    await host.EnsureWorldAsync(cancellationSource.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.IsTrue(canceled);
                Assert.AreEqual(0, host.WorldRoot.childCount, "取消后的地图实例不得遗留在世界根节点下。");

                GameObject map = await host.EnsureWorldAsync();
                Assert.IsNotNull(map);
                Assert.AreEqual(1, host.WorldRoot.childCount, "重试只能创建一张地图。");
            }
            finally
            {
                host.Release();
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
