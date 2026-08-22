using System;
using System.Collections.Generic;
using System.IO;
using GameCommon.Battle;
using NUnit.Framework;
using UnityEngine;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 任务 3.4/3.5/3.7/3.8 + 任务 4.6：BattleRuntimeFactory 敌人接入 EditMode 测试
    // ----------------------------------------------------------------------------
    // 直接通过 BattleRuntimeFactory.Create（注入 golden 快照 + BattlePoolScope）
    // 验证生产装配的敌人链（新链，任务 4.6 起出生由 WaveManager.NormalWaveSpawnHandler 驱动）：
    //   1. 用 configSnapshot.EnemyCatalog 构造类型化 EnemyFactory（封闭注册表 + 四池）。
    //   2. WaveSpawn phase（waveManager.Update）→ normalSpawnHandler 解析当前行 →
    //      构造 EnemySpawnRequest 走新生产链（acquire → ConfiguredInit → Register）；
    //      配置数值实际进入实例；行 enemyKey / 地图默认解析生效（Mob0～Mob3，禁止回退错误类型）。
    //   3. EnemyManager.WaveEntityRemoved → waveManager.OnEntityRemoved 波次所有权接线在 Factory。
    //   4. ReleaseEnemy 按 ConfiguredEnemyBase/实际 key 归还正确池（无 Mob0 强转）。
    //   5. 源码静态检查：生产路径无反射 Configure / Mob0EnemyInitStats / GetInitialHealth /
    //      固定初档血量 / reward fallback / 临时 OnSpawnEnemy 桥 / 旧 UpdateSpawnState 接入。
    //
    // 本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束。
    // ============================================================================

    /// <summary>
    /// BattleRuntimeFactory 敌人接入测试：类型化工厂、WaveSpawn 出生链、配置数值进入实例、池回收。
    /// </summary>
    [TestFixture]
    internal class EnemyRuntimeFactoryIntegrationTests
    {
        /// <summary>组装一份可运行 Runtime 并 StartGame（golden 计划 4 行）。</summary>
        private static BattleRuntimeAssembly CreateBattleRuntime(
            BattlePoolScope poolScope,
            out BattleConfigSnapshot snapshot)
        {
            snapshot = CreateValidatedGoldenSnapshot();
            var loadout = new BattleLoadoutDto(
                mapId: 0, round: 0, randomSeed: 42, configVersion: 0,
                configHash: string.Empty, deckPreset: BattleDeckPreset.Normal);

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope, bindings: null, configSnapshot: snapshot);

            Assert.IsTrue(assembly.IsSuccess, $"组装应成功：{assembly.DiagnosticMessage}");
            Assert.AreEqual(4, snapshot.OrderedWavePlan.Rows.Count, "golden 计划 4 行。");

            // 任务 4.6：StartGame 只做战斗开始协调（MaxRounds=计划行数、CurrentRound=0），
            // 波次由 WaveManager 在 WaveSpawn phase（Update）推进。
            assembly.BattleManager.StartGame(0);
            return assembly;
        }

        /// <summary>
        /// 保留冻结 JSON 的战斗数值/目录/逐行计划，但以最小合法地图替换旧 fixture 中仅具
        /// 表现含义、且不满足当前连续通道契约的 route marker 路径。
        /// </summary>
        private static BattleConfigSnapshot CreateValidatedGoldenSnapshot()
        {
            BattleConfigSnapshot golden = new JsonBattleConfigProvider().GetSnapshot();
            const int width = 3;
            const int height = 3;
            var cells = new GridCell[width * height];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = new GridCell(GridCellKind.Passage, BuildableSide.None);
            }

            var map = new MapData(
                cells,
                width,
                height,
                mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, 2),
                opponentStart: new GridPosition(2, 2),
                opponentEnd: new GridPosition(2, 0),
                playerPath: new[]
                {
                    new GridPosition(0, 0),
                    new GridPosition(0, 1),
                    new GridPosition(0, 2),
                },
                opponentPath: new[]
                {
                    new GridPosition(2, 2),
                    new GridPosition(2, 1),
                    new GridPosition(2, 0),
                });

            return new BattleConfigSnapshot(
                map,
                golden.Enemy,
                golden.Wave,
                golden.Units,
                golden.UnitLevel,
                golden.Economy,
                golden.Deck,
                golden.Projectile,
                golden.MissingFieldNotes,
                "Test",
                golden.EnemyCatalog,
                golden.OrderedWavePlan,
                golden.BuffCatalog,
                golden.SkillCatalog,
                golden.BossCatalog);
        }

        /// <summary>把 golden 目录裁成单行张梁 Boss 计划，供纯逻辑生产装配测试复用。</summary>
        private static BattleConfigSnapshot CreateZhangLiangBossSnapshot(
            BattleConfigSnapshot golden,
            bool spawnPlayerLane = true,
            bool spawnOpponentLane = true)
        {
            var bossPlan = new OrderedWavePlanSnapshot(
                "golden",
                new[]
                {
                    new WavePlanEntry("golden", 1, WavePlanKind.Boss, "", 0, 0, "ZhangLiang",
                        0, 0, 0, spawnPlayerLane, spawnOpponentLane, 0),
                },
                new Dictionary<int, IReadOnlyList<float>> { [0] = new float[] { 1f } });

            return new BattleConfigSnapshot(
                map: golden.Map,
                enemy: golden.Enemy,
                wave: golden.Wave,
                units: golden.Units,
                unitLevel: golden.UnitLevel,
                economy: golden.Economy,
                deck: golden.Deck,
                projectile: golden.Projectile,
                missingFieldNotes: golden.MissingFieldNotes,
                sourceTag: "Test",
                enemyCatalog: golden.EnemyCatalog,
                orderedWavePlan: bossPlan,
                buffCatalog: golden.BuffCatalog,
                skillCatalog: golden.SkillCatalog,
                bossCatalog: golden.BossCatalog);
        }

        /// <summary>把当前行全部活动 handle 从 WaveManager 移除（模拟清场）。</summary>
        private static void ClearAllActiveHandles(BattleRuntimeAssembly assembly)
        {
            foreach (WaveEntityHandle handle in assembly.WaveManager.ActiveHandles)
            {
                assembly.WaveManager.OnEntityRemoved(handle);
            }
        }

        /// <summary>
        /// 通过真实 EnemyManager 移除队列清空当前波；不得直接调用 WaveManager.OnEntityRemoved。
        /// </summary>
        private static void KillAllActiveEnemiesThroughManager(BattleRuntimeAssembly assembly)
        {
            var handles = new List<WaveEntityHandle>(assembly.WaveManager.ActiveHandles);
            Assert.Greater(handles.Count, 0, "当前波应至少有一个活动敌人。");
            foreach (WaveEntityHandle handle in handles)
            {
                assembly.EnemyManager.RequestRemoveEnemy(handle.RuntimeId, EnemyRemovalReason.Killed);
            }

            assembly.EnemyManager.ProcessRemoveQueue();
            Assert.AreEqual(0, assembly.WaveManager.ActiveHandleCount,
                "EnemyManager.WaveEntityRemoved 必须把完整租借身份接回 WaveManager。");
        }

        /// <summary>给首个生产敌人挂一层带到期调度的移动速度 Buff。</summary>
        private static void ApplyDurationBuffToFirstEnemy(BattleRuntimeAssembly assembly)
        {
            IReadOnlyList<int> ids = assembly.EnemyManager.GetOrderedIdsSnapshot();
            Assert.Greater(ids.Count, 0, "应至少存在一个生产敌人。");
            var target = (IBuffTarget)assembly.EnemyManager.GetById(ids[0]);
            assembly.Simulation.ActionScheduler.BeginFrame(1000);

            BuffOperationResult result = assembly.BuffManager.Apply(new BuffApplyRequest(
                3,
                target.Handle,
                new BuffSourceHandle(100),
                10,
                BuffValueMode.Flat,
                BuffTimeMode.DurationMs,
                10000));

            Assert.AreEqual(BuffOperationStatus.Applied, result.Status);
            Assert.AreEqual(1, assembly.BuffManager.ActiveInstanceCount);
            Assert.AreEqual(1, assembly.BuffManager.OwnedScheduleCount);
        }

        private static void AdvanceUntilWaitingForClear(BattleRuntimeAssembly assembly, int order)
        {
            for (int step = 0; step < 64; step++)
            {
                if (assembly.WaveManager.CurrentOrder == order
                    && assembly.WaveManager.State == WaveRuntimeState.WaitingForClear)
                {
                    return;
                }

                assembly.WaveManager.Update(10000);
            }

            Assert.Fail($"未能推进到 order={order} WaitingForClear；" +
                        $"current={assembly.WaveManager.CurrentOrder}, state={assembly.WaveManager.State}");
        }

        [Test]
        [Description("golden order=1 行：Provider 已按地图 EnemyTypeIndex 将空键解析为 Mob0，双路各生成 1 个，" +
                     "配置数值（血量公式/速度/接触伤害/奖励）实际进入实例；Killed 事实携带正确租借身份并归还正确池。")]
        public void WaveSpawn_ResolvesRow_SpawnsConfiguredEnemyWithConfigStats()
        {
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = CreateBattleRuntime(poolScope, out _);

            var waveFacts = new List<(WaveEntityHandle handle, EnemyRemovalReason reason)>();
            assembly.EnemyManager.WaveEntityRemoved += (handle, reason) => waveFacts.Add((handle, reason));

            // 任务 4.6：WaveManager 唯一时间推进（等效 WaveSpawn phase）。
            // golden 行 1：preDelay=1000 → 首次 Update(1000) 出生序号 0（玩家路 + 电脑路各 1 只 Mob0）。
            assembly.WaveManager.Update(1000);

            Assert.AreEqual(2, assembly.EnemyManager.Count, "golden order=1 序号 0 双路各生成 1 个敌人。");
            Assert.AreEqual(1, assembly.BattleState.CurrentRound, "WaveStarted(order=1) 同步 CurrentRound。");

            IEnemyEntity first = assembly.EnemyManager.GetById(1);
            Assert.IsInstanceOf<Mob0Enemy>(first, "Provider 应把地图默认敌人解析为 Mob0。");
            var configured = (ConfiguredEnemyBase)first;
            Assert.AreEqual(1, configured.WaveOrder, "waveOrder 来自计划行 order=1。");
            Assert.AreEqual(6, configured.MaxHealthBaseValue,
                "最大生命 = 10(healthByWave[0]) × 1(profile0[0]) × 0.6(early[0]) → 一次舍入 6。");
            Assert.AreEqual(50, configured.BaseMoveSpeedValue, "速度来自目录定义。");
            Assert.AreEqual(1, configured.Stats.Value.ContactDamage, "接触伤害来自目录定义。");
            Assert.AreEqual(1, configured.Stats.Value.RewardGold, "奖励来自目录定义（非固定 1 fallback，来自 EnemyCatalog）。");
            Assert.IsTrue(assembly.EnemyManager.TryGetLeaseIdentity(1, out EnemyLeaseIdentity lease),
                "登记后可查租借身份。");
            Assert.AreEqual(new EnemyLeaseIdentity(1, 1, 1), lease, "身份 = (runtimeId=1, generation=1, waveOrder=1)。");

            // Killed 内部事实 + 按实际 key 归还 Mob0 池。
            assembly.EnemyManager.RequestRemoveEnemy(1, EnemyRemovalReason.Killed);
            assembly.EnemyManager.ProcessRemoveQueue();
            CollectionAssert.AreEqual(
                new[] { (WaveEntityHandle.FromEnemyLease(new EnemyLeaseIdentity(1, 1, 1)), EnemyRemovalReason.Killed) },
                waveFacts,
                "Killed 内部事实恰好一次且 identity 正确。");

            // 回收对手路敌人后全部租借对称归还。
            assembly.EnemyManager.RequestRemoveEnemy(2, EnemyRemovalReason.Killed);
            assembly.EnemyManager.ProcessRemoveQueue();
            Assert.IsTrue(poolScope.AssertAllActiveReleased(), "全部敌人经 ReleaseEnemy 按实际 key 归还池。");
        }

        [Test]
        [Description("逐行 enemyKey 解析：golden 行 2（enemyKey=Mob1）在行 1 清场后出生 Mob1（waveOrder=2），" +
                     "行覆盖键生效，不因行 1 类型回退。")]
        public void WaveRows_ResolvePerRowEnemyKey()
        {
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = CreateBattleRuntime(poolScope, out _);

            // 行 1（order=1）：双路各 3 只 Mob0，preDelay=1000，spawnInterval=500，postDelay=500。
            assembly.WaveManager.Update(1000); // 序号 0（P+O）
            assembly.WaveManager.Update(500);  // 序号 1（P+O）
            assembly.WaveManager.Update(500);  // 序号 2（P+O）→ WaitingForClear
            Assert.AreEqual(6, assembly.EnemyManager.Count, "行 1 双路各 3 只。");

            // 清场行 1 → postDelay 到期 → 行 1 完成 → 下一次 Update 进入行 2。
            ClearAllActiveHandles(assembly);
            assembly.WaveManager.Update(500); // postDelay 到期，无活动 handle → 行 1 完成
            assembly.WaveManager.Update(1);   // 进入行 2（preDelay 1000）
            assembly.WaveManager.Update(1000);// 行 2 序号 0（玩家路第 1 只 Mob1）
            assembly.WaveManager.Update(600); // 行 2 序号 1（玩家路第 2 只 Mob1）

            Assert.AreEqual(2, assembly.BattleState.CurrentRound, "行 2 开始时同步 CurrentRound=2。");
            Assert.AreEqual(8, assembly.EnemyManager.Count, "行 1 的 6 只已移除 + 行 2 的 2 只。");
            IEnemyEntity row2Enemy = assembly.EnemyManager.GetById(7);
            Assert.IsInstanceOf<Mob1Enemy>(row2Enemy, "行 2 enemyKey=Mob1 解析为 Mob1（行覆盖生效）。");
            Assert.AreEqual(2, ((ConfiguredEnemyBase)row2Enemy).WaveOrder, "行 2 敌人 waveOrder=2。");

            // 归还后池租借对称。
            ClearAllActiveHandles(assembly);
            assembly.EnemyManager.GameOver();
            Assert.IsTrue(poolScope.AssertAllActiveReleased(), "全部敌人归还对应池。");
        }

        [Test]
        [Description("WaveEntityRemoved 接线：EnemyManager 移除事实接到 waveManager.OnEntityRemoved，" +
                     "reason 不改变波次计数；普通敌人/技能外实体只有带有效 waveOrder 的 handle 才属于计划。")]
        public void WaveEntityRemoved_WiresToWaveManager_RemovesOwnedHandle()
        {
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = CreateBattleRuntime(poolScope, out _);

            assembly.WaveManager.Update(1000); // 行 1 序号 0（P+O 两只）
            Assert.AreEqual(2, assembly.WaveManager.ActiveHandleCount, "两只活动 handle 已登记。");

            // Killed：reason 不改变波次计数（HandleSet 只按完整 handle 幂等移除）。
            assembly.EnemyManager.RequestRemoveEnemy(1, EnemyRemovalReason.Killed);
            assembly.EnemyManager.ProcessRemoveQueue();
            Assert.AreEqual(1, assembly.WaveManager.ActiveHandleCount,
                "Killed 移除恰好一只（reason 不改变波次计数，仅移除完整 handle）。");

            // 无有效 waveOrder 的合成身份（非计划实体）不命中：移除是空操作。
            assembly.WaveManager.OnEntityRemoved(new EnemyLeaseIdentity(999, 0, 0));
            Assert.AreEqual(1, assembly.WaveManager.ActiveHandleCount,
                "无有效 waveOrder 的 handle 不属于计划，不改变活动计数。");

            assembly.EnemyManager.GameOver();
            Assert.IsTrue(poolScope.AssertAllActiveReleased(), "清理后池租借对称。");
        }

        [Test]
        [Description("task 6.1：完整生产装配链中最后一波仍有敌人不胜利；对手提前归零不绕过成功闸；" +
                     "全部四行经 EnemyManager 真实清场后只冻结一次胜利。")]
        public void RuntimeAssembly_AllConfiguredWavesCleared_IsOnlySingleWinGate()
        {
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = CreateBattleRuntime(poolScope, out BattleConfigSnapshot snapshot);
            int completionFacts = 0;
            assembly.WaveManager.AllConfiguredWavesCompleted += () => completionFacts++;

            // 对手生命提前归零只保留状态，不得直接成功。
            while (assembly.BattleState.OpponentHealth > 0)
            {
                assembly.BattleState.ApplyDamage(isPlayerSide: false, damage: 1);
            }

            assembly.BattleManager.CheckHealthFreeze(isPlayerSide: false);
            Assert.IsFalse(assembly.ResultBuilder.IsFrozen,
                "对手生命提前归零不得绕过全部配置波清场成功闸。");

            for (int order = 1; order <= snapshot.OrderedWavePlan.Rows.Count; order++)
            {
                AdvanceUntilWaitingForClear(assembly, order);
                Assert.Greater(assembly.WaveManager.ActiveHandleCount, 0,
                    $"order={order} WaitingForClear 时应仍有活动敌人。");
                Assert.IsFalse(assembly.ResultBuilder.IsFrozen,
                    $"order={order} 最后一次出生后、清场前不得冻结胜利。");

                KillAllActiveEnemiesThroughManager(assembly);
                assembly.WaveManager.Update(10000); // postDelay 到期并完成当前行。

                if (order < snapshot.OrderedWavePlan.Rows.Count)
                {
                    Assert.IsFalse(assembly.ResultBuilder.IsFrozen,
                        $"仅完成前 {order} 行不得提前胜利。");
                }
            }

            Assert.IsTrue(assembly.WaveManager.AllWavesCompleted);
            Assert.AreEqual(1, completionFacts, "AllConfiguredWavesCompleted 事实必须只发布一次。");
            Assert.IsTrue(assembly.ResultBuilder.IsFrozen, "全部配置波真实清场后应冻结结果。");
            Assert.IsTrue(assembly.ResultBuilder.FrozenResult.Value.IsWin, "最终结果应为胜利。");
            Assert.AreEqual(4, assembly.ResultBuilder.FrozenResult.Value.Round,
                "结果轮次应保留最后真实 order，而非 legacy MaxRounds。");

            for (int repeat = 0; repeat < 5; repeat++)
            {
                assembly.WaveManager.Update(10000);
            }

            Assert.AreEqual(1, completionFacts, "完成后重复 Update 不得重复发布成功事实。");
            Assert.IsFalse(assembly.BattleManager.TryFreezeResult(playerWin: false),
                "结果已冻结后不得被迟到失败覆盖。");
            Assert.IsTrue(assembly.ResultBuilder.FrozenResult.Value.IsWin);
            Assert.IsTrue(poolScope.AssertAllActiveReleased(), "全部敌人清场后池租借应对称归还。");
        }

        [Test]
        [Description("生产能力仅支持 ZhangLiang；Boss 行会装配真实 BossWavePort 并双路出生。")]
        public void ZhangLiangBossPlan_UsesProductionBossPort()
        {
            BattleConfigSnapshot golden = CreateValidatedGoldenSnapshot();
            BattleConfigSnapshot bossSnapshot = CreateZhangLiangBossSnapshot(golden);

            var loadout = new BattleLoadoutDto(
                mapId: 0, round: 0, randomSeed: 42, configVersion: 0,
                configHash: string.Empty, deckPreset: BattleDeckPreset.Normal);
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope, bindings: null, configSnapshot: bossSnapshot);

            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);
            Assert.IsNotNull(assembly.SkillRunner);

            assembly.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
            AdvanceUntilWaitingForClear(assembly, order: 1);

            Assert.AreEqual(2, assembly.WaveManager.ActiveHandleCount, "双路 Boss 应各出生一次。");
            Assert.IsTrue(golden.EnemyCatalog.TryGetByTypeIndex(0, out EnemyDefinitionSnapshot baseline));
            Assert.IsTrue(golden.BossCatalog.TryGetByKey("ZhangLiang", out BossDefinitionSnapshot bossDefinition));
            ConfiguredEnemyResolvedStats normalBaseline =
                EnemyStatsResolver.Resolve(baseline, 0, new float[] { 1f });
            ConfiguredEnemyResolvedStats expectedStats =
                BossStatsResolver.Resolve(baseline, bossDefinition, 0, new float[] { 1f });
            Assert.AreEqual(
                (int)Math.Round(normalBaseline.MaxHealth * 7d, MidpointRounding.AwayFromZero),
                expectedStats.MaxHealth);
            var bosses = new List<ZhangLiangBoss>();
            foreach (WaveEntityHandle handle in assembly.WaveManager.ActiveHandles)
            {
                Assert.AreEqual(WaveEntityKind.Boss, handle.Kind);
                var boss = (ZhangLiangBoss)assembly.EnemyManager.GetById(handle.RuntimeId);
                bosses.Add(boss);
                Assert.AreEqual(expectedStats.MaxHealth, boss.MaxHealth, "ZhangLiang 最大生命应为 H×7。");
                Assert.AreEqual(10, boss.EffectiveMoveSpeedForTest);
                Assert.AreEqual(84.33f, boss.Width, 0.001f);
                Assert.AreEqual(101.25f, boss.Height, 0.001f);
            }

            UnitConfigSnapshot knifeConfig = golden.Units[0];
            SoldierBase playerNearA = assembly.UnitRegistry.CreateAndPlace(
                SoldierType.Knife, knifeConfig, true, 0, 1, 40f, 40f);
            SoldierBase playerNearB = assembly.UnitRegistry.CreateAndPlace(
                SoldierType.Knife, knifeConfig, true, 1, 0, 40f, 40f);
            SoldierBase playerFar = assembly.UnitRegistry.CreateAndPlace(
                SoldierType.Knife, knifeConfig, true, 2, 2, 40f, 40f);
            SoldierBase opponentNear = assembly.UnitRegistry.CreateAndPlace(
                SoldierType.Knife, knifeConfig, false, 2, 1, 40f, 40f);
            SoldierBase opponentWrongLane = assembly.UnitRegistry.CreateAndPlace(
                SoldierType.Knife, knifeConfig, false, 1, 0, 40f, 40f);

            assembly.Simulation.ActionScheduler.BeginFrame(7999);
            foreach (ZhangLiangBoss boss in bosses)
            {
                boss.Update(1);
                var owner = new SkillOwnerHandle(boss.Id, boss.CurrentLease.Generation);
                Assert.IsTrue(assembly.SkillRunner.TryGetState(owner, "SoulCapture", out SkillStateSnapshot state));
                Assert.IsFalse(state.IsRunning, "首次就绪前不得激活。");
            }

            assembly.Simulation.ActionScheduler.BeginFrame(8000);
            foreach (ZhangLiangBoss boss in bosses)
            {
                boss.Update(1);
                var owner = new SkillOwnerHandle(boss.Id, boss.CurrentLease.Generation);
                Assert.IsTrue(assembly.SkillRunner.TryGetState(owner, "SoulCapture", out SkillStateSnapshot state));
                Assert.IsTrue(state.IsRunning);
                Assert.IsTrue(boss.MovementStoppedForTest, "技能运行时 Boss 应暂停移动。");
            }

            assembly.Simulation.ActionScheduler.BeginFrame(8499);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            foreach (ZhangLiangBoss boss in bosses)
            {
                var owner = new SkillOwnerHandle(boss.Id, boss.CurrentLease.Generation);
                assembly.SkillRunner.TryGetState(owner, "SoulCapture", out SkillStateSnapshot state);
                Assert.IsFalse(state.EffectCommitted, "effectAtMs=500 前不得提交效果。");
            }

            assembly.Simulation.ActionScheduler.BeginFrame(8500);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            foreach (ZhangLiangBoss boss in bosses)
            {
                var owner = new SkillOwnerHandle(boss.Id, boss.CurrentLease.Generation);
                assembly.SkillRunner.TryGetState(owner, "SoulCapture", out SkillStateSnapshot state);
                Assert.IsTrue(state.EffectCommitted, "effectAtMs=500 时提交效果。");
            }
            Assert.AreEqual(3, assembly.BuffManager.ActiveInstanceCount,
                "SoulCapture 只命中同路两格内单位。");
            Assert.AreEqual(1,
                assembly.BuffManager.GetTargetSnapshots(((IBuffTarget)playerNearA).Handle).Count);
            Assert.AreEqual(1,
                assembly.BuffManager.GetTargetSnapshots(((IBuffTarget)playerNearB).Handle).Count);
            Assert.AreEqual(1,
                assembly.BuffManager.GetTargetSnapshots(((IBuffTarget)opponentNear).Handle).Count);
            Assert.AreEqual(0,
                assembly.BuffManager.GetTargetSnapshots(((IBuffTarget)playerFar).Handle).Count,
                "超出两格的同路单位不得命中。");
            Assert.AreEqual(0,
                assembly.BuffManager.GetTargetSnapshots(((IBuffTarget)opponentWrongLane).Handle).Count,
                "靠近另一车道 Boss 的异路单位不得命中。");
            IReadOnlyList<BuffInstanceSnapshot> appliedBuffs = assembly.BuffManager.GetAllSnapshots();
            var lastTargetBySource = new Dictionary<int, int>();
            for (int i = 0; i < appliedBuffs.Count; i++)
            {
                BuffInstanceSnapshot buff = appliedBuffs[i];
                Assert.AreEqual(13, buff.Request.BuffType);
                Assert.AreEqual(2000, buff.Request.DurationMs);
                int sourceId = buff.Request.Source.SourceId;
                if (lastTargetBySource.TryGetValue(sourceId, out int previousTargetId))
                {
                    Assert.Less(previousTargetId,
                        buff.Request.Target.RuntimeId,
                        "每个 SoulCapture 的 Buff 申请顺序必须按目标 runtimeId 稳定升序。");
                }

                lastTargetBySource[sourceId] = buff.Request.Target.RuntimeId;
            }

            assembly.Simulation.ActionScheduler.BeginFrame(9400);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            foreach (ZhangLiangBoss boss in bosses)
            {
                boss.Update(1);
                Assert.IsFalse(boss.MovementStoppedForTest, "completeAtMs=1400 后恢复移动。");
            }


            assembly.Simulation.ActionScheduler.BeginFrame(10499);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            Assert.AreEqual(3, assembly.BuffManager.ActiveInstanceCount);
            assembly.Simulation.ActionScheduler.BeginFrame(10500);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            Assert.AreEqual(0, assembly.BuffManager.ActiveInstanceCount, "Buff13 应在 2000ms 到期。");

            int playerGoldBefore = assembly.BattleState.PlayerGold;
            int opponentGoldBefore = assembly.BattleState.OpponentGold;
            Assert.IsTrue(bosses[0].Hit(bosses[0].MaxHealth, attackerId: 900));
            Assert.IsFalse(bosses[0].Hit(1, attackerId: 900), "重复死亡不得重复提交奖励。");
            assembly.EnemyManager.ProcessRemoveQueue();
            Assert.AreEqual(1, assembly.WaveManager.ActiveHandleCount);
            Assert.IsFalse(assembly.ResultBuilder.IsFrozen,
                "双路 Boss 仅离场一只时仍须 WaitingForClear，不得胜利。");

            Assert.IsTrue(bosses[1].Hit(bosses[1].MaxHealth, attackerId: 900));
            Assert.IsFalse(bosses[1].Hit(1, attackerId: 900), "重复死亡不得重复提交奖励。");
            assembly.EnemyManager.ProcessRemoveQueue();
            Assert.AreEqual(0, assembly.EnemyManager.Count);
            Assert.AreEqual(0, assembly.WaveManager.ActiveHandleCount);
            Assert.IsFalse(assembly.ResultBuilder.IsFrozen,
                "最后 Boss handle 离场后仍须由波次状态机完成 final row。");
            assembly.WaveManager.Update(1);
            Assert.IsTrue(assembly.ResultBuilder.IsFrozen);
            Assert.IsTrue(assembly.ResultBuilder.FrozenResult.Value.IsWin);
            Assert.AreEqual(0, assembly.SkillRunner.OwnerCount);
            Assert.AreEqual(2, assembly.BattleState.KillCount);
            Assert.AreEqual(2, assembly.BattleState.BossKillCount);
            Assert.AreEqual(playerGoldBefore + 10, assembly.BattleState.PlayerGold);
            Assert.AreEqual(opponentGoldBefore + 10, assembly.BattleState.OpponentGold);
            assembly.UnitRegistry.ClearForSettling();
            Assert.IsTrue(poolScope.AssertAllActiveReleased());
        }

        [Test]
        [Description("张梁在 effect 前死亡取消效果；effect 后死亡不清除已提交 Buff13。")]
        public void ZhangLiangDeath_BeforeAndAfterEffect_RespectsCommittedBoundary()
        {
            BattleConfigSnapshot golden = CreateValidatedGoldenSnapshot();
            BattleConfigSnapshot bossSnapshot = CreateZhangLiangBossSnapshot(golden);
            var loadout = new BattleLoadoutDto(
                mapId: 0, round: 0, randomSeed: 42, configVersion: 0,
                configHash: string.Empty, deckPreset: BattleDeckPreset.Normal);
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope, bindings: null, configSnapshot: bossSnapshot);

            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);
            assembly.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
            AdvanceUntilWaitingForClear(assembly, order: 1);

            ZhangLiangBoss playerBoss = null;
            ZhangLiangBoss opponentBoss = null;
            foreach (WaveEntityHandle handle in assembly.WaveManager.ActiveHandles)
            {
                var boss = (ZhangLiangBoss)assembly.EnemyManager.GetById(handle.RuntimeId);
                if (boss.IsPlayerLane)
                {
                    playerBoss = boss;
                }
                else
                {
                    opponentBoss = boss;
                }
            }

            Assert.IsNotNull(playerBoss);
            Assert.IsNotNull(opponentBoss);
            UnitConfigSnapshot knifeConfig = golden.Units[0];
            SoldierBase playerTarget = assembly.UnitRegistry.CreateAndPlace(
                SoldierType.Knife, knifeConfig, true, 0, 1, 40f, 40f);
            SoldierBase opponentTarget = assembly.UnitRegistry.CreateAndPlace(
                SoldierType.Knife, knifeConfig, false, 2, 1, 40f, 40f);

            assembly.Simulation.ActionScheduler.BeginFrame(8000);
            playerBoss.Update(1);
            opponentBoss.Update(1);
            Assert.AreEqual(2, assembly.SkillRunner.RunningActivationCount);

            // 玩家路 Boss 在 500ms effect 前死亡：取消 owner/调度，不得命中玩家路单位。
            Assert.IsTrue(playerBoss.Hit(playerBoss.MaxHealth, attackerId: 901));
            assembly.EnemyManager.ProcessRemoveQueue();
            Assert.AreEqual(1, assembly.SkillRunner.RunningActivationCount);

            assembly.Simulation.ActionScheduler.BeginFrame(8500);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            Assert.AreEqual(0,
                assembly.BuffManager.GetTargetSnapshots(((IBuffTarget)playerTarget).Handle).Count);
            Assert.AreEqual(1,
                assembly.BuffManager.GetTargetSnapshots(((IBuffTarget)opponentTarget).Handle).Count);

            // 电脑路 Boss 在 effect 已提交后死亡：Buff 由自身生命周期继续持有。
            Assert.IsTrue(opponentBoss.Hit(opponentBoss.MaxHealth, attackerId: 902));
            assembly.EnemyManager.ProcessRemoveQueue();
            Assert.AreEqual(0, assembly.SkillRunner.OwnerCount);
            Assert.AreEqual(1, assembly.BuffManager.ActiveInstanceCount);

            assembly.Simulation.ActionScheduler.BeginFrame(10499);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            Assert.AreEqual(1, assembly.BuffManager.ActiveInstanceCount);
            assembly.Simulation.ActionScheduler.BeginFrame(10500);
            assembly.Simulation.ActionScheduler.FlushDueActions(1);
            Assert.AreEqual(0, assembly.BuffManager.ActiveInstanceCount);

            Assert.AreEqual(2, assembly.BattleState.KillCount);
            Assert.AreEqual(2, assembly.BattleState.BossKillCount);
            assembly.UnitRegistry.ClearForSettling();
            Assert.IsTrue(poolScope.AssertAllActiveReleased());
        }

        [Test]
        [Description("技能运行中 Settling 按 Wave→scheduler→Skill→Boss→Buff 顺序幂等清空。")]
        public void ZhangLiangSkillRunning_EnterSettling_ClearsAllOwnersAndLeases()
        {
            BattleConfigSnapshot golden = CreateValidatedGoldenSnapshot();
            BattleConfigSnapshot bossSnapshot = CreateZhangLiangBossSnapshot(golden);
            var loadout = new BattleLoadoutDto(
                mapId: 0, round: 0, randomSeed: 42, configVersion: 0,
                configHash: string.Empty, deckPreset: BattleDeckPreset.Normal);
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope, bindings: null, configSnapshot: bossSnapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);
            var runtime = new BattleRuntime(assembly);

            assembly.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
            AdvanceUntilWaitingForClear(assembly, order: 1);
            assembly.Simulation.ActionScheduler.BeginFrame(8000);
            foreach (WaveEntityHandle handle in assembly.WaveManager.ActiveHandles)
            {
                ((ZhangLiangBoss)assembly.EnemyManager.GetById(handle.RuntimeId)).Update(1);
            }

            Assert.AreEqual(2, assembly.SkillRunner.OwnerCount);
            Assert.AreEqual(2, assembly.SkillRunner.RunningActivationCount);
            runtime.EnterSettling();

            Assert.IsTrue(runtime.IsSettling);
            Assert.IsTrue(assembly.WaveManager.IsStopped);
            Assert.IsTrue(assembly.Simulation.ActionScheduler.IsFrozen);
            Assert.AreEqual(0, assembly.SkillRunner.OwnerCount);
            Assert.AreEqual(0, assembly.SkillRunner.RunningActivationCount);
            Assert.AreEqual(0, assembly.EnemyManager.Count);
            Assert.AreEqual(0, assembly.WaveManager.ActiveHandleCount);
            Assert.AreEqual(0, assembly.BuffManager.ActiveInstanceCount);
            Assert.AreEqual(0, assembly.BuffManager.RegisteredTargetCount);
            Assert.AreEqual(0, assembly.BuffManager.OwnedScheduleCount);
            Assert.AreEqual(0, assembly.BattleState.KillCount);
            Assert.AreEqual(0, assembly.BattleState.BossKillCount);
            Assert.IsTrue(poolScope.AssertAllActiveReleased());

            runtime.Dispose();
            runtime.Dispose();
            Assert.IsTrue(runtime.IsDisposed);
        }

        [Test]
        [Description("RuntimeFactory 生产源码不得包含反射 Configure、Mob0 强转、Mob0EnemyInitStats/" +
                     "GetInitialHealth、固定初档血量、reward fallback、临时 OnSpawnEnemy 桥、旧 " +
                     "UpdateSpawnState 接入或旧 WaveManager 构造（task 3.5/4.6）。")]
        public void RuntimeFactorySource_NoReflectionNoMob0CastNoFallback_NoLegacyBridge()
        {
            string path = Path.Combine(
                Application.dataPath,
                "GameScripts/HotFix/GameBattle/Runtime/BattleRuntimeFactory.cs");
            Assert.IsTrue(File.Exists(path), $"找不到 RuntimeFactory 源码：{path}");
            string source = File.ReadAllText(path);

            string[] forbidden =
            {
                "InvokeMember",               // 反射调用 protected Configure
                "Mob0EnemyInitStats",         // 旧数值快照类型
                "GetInitialHealth()",         // 旧首档血量入口
                "rewardGold: 1",              // 固定奖励 fallback
                "Release((Mob0Enemy)",        // Mob0 强转回收
                "HealthByWave[0]",            // 固定首档血量
                "new EnemyFactory(idAllocator, mob0Pool)", // 旧链工厂构造
                "battleManager.OnSpawnEnemy", // 临时 OnSpawnEnemy 桥（任务 4.6 已删除）
                "battleManager.UpdateSpawnState",         // 旧 spawn state phase 接入
                "waveManager.StartGame",      // 旧第二状态机启动
                "randomSource.NextUnit)",      // 旧 WaveManager 随机源构造
            };

            foreach (string pattern in forbidden)
            {
                Assert.IsFalse(source.Contains(pattern),
                    $"RuntimeFactory 生产源码不得包含 '{pattern}'（task 3.5/4.6 已移除）。");
            }
        }

        [Test]
        [Description("task 6.3：生产协调/组装/清理/校验源码不再读取 legacy Enemy/Wave，" +
                     "也不保留 OnSpawnEnemy、RoundSpawnPrepared 死桥或 WaveManager.GameOver 调用。")]
        public void ProductionSources_NoLegacyAuthorityOrDeadBridges()
        {
            var checks = new Dictionary<string, string[]>
            {
                ["Battle/BattleManager.cs"] = new[] { "OnSpawnEnemy" },
                ["Module/BattleModule.cs"] = new[]
                {
                    "OnSpawnEnemy",
                    "ENABLE_COMPUTER_LANE_ENEMY_SPAWN",
                },
                ["Runtime/BattleRuntimeFactory.cs"] = new[]
                {
                    "waveManager.OnRoundSpawnPrepared =",
                    "battleManager.OnSpawnEnemy",
                },
                ["Runtime/BattleRuntime.cs"] = new[] { "WaveManager?.GameOver" },
                ["Config/BattleConfigValidator.cs"] = new[]
                {
                    "snapshot.Enemy.",
                    "snapshot.Wave.",
                },
            };

            foreach (KeyValuePair<string, string[]> check in checks)
            {
                string path = Path.Combine(
                    Application.dataPath,
                    "GameScripts/HotFix/GameBattle",
                    check.Key.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(path), $"找不到生产源码：{path}");
                string source = File.ReadAllText(path);
                foreach (string forbidden in check.Value)
                {
                    Assert.IsFalse(source.Contains(forbidden),
                        $"{check.Key} 不得包含 legacy 生产依赖/死桥 '{forbidden}'。");
                }
            }
        }

        // ====================================================================
        // task 4.9：EnterSettling / 未 Settling Dispose 都先 Stop WaveManager 再清理敌人
        // ====================================================================
        // 顺序契约可观察点：EnemyManager.GameOver 逐敌人提交 Forced 移除事实时，
        // WaveManager 必须已 Stop（Stop 前置），否则移除事实可能促成波次完成/误判胜利。

        [Test]
        [Description("task 4.9：EnterSettling 先 Stop WaveManager 再 EnemyManager.GameOver，" +
                     "Forced 移除事实在已停止状态下发布，不促成完成/发布 AllCompleted。")]
        public void EnterSettling_StopsWaveManagerBeforeEnemyManagerGameOver()
        {
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = CreateBattleRuntime(poolScope, out _);
            var runtime = new BattleRuntime(assembly);

            // golden order=1：Update(1000) 出生序号 0（玩家路 + 电脑路各 1 只）。
            assembly.WaveManager.Update(1000);
            Assert.AreEqual(2, assembly.WaveManager.ActiveHandleCount, "出生后有活动 handle。");
            Assert.AreEqual(2, assembly.BuffManager.RegisteredTargetCount, "生产 EnemyManager 自动登记 Buff 目标。");
            ApplyDurationBuffToFirstEnemy(assembly);

            int completedCalls = 0;
            assembly.WaveManager.AllConfiguredWavesCompleted += () => completedCalls++;

            var stoppedAtForcedRemoval = new List<bool>();
            assembly.EnemyManager.WaveEntityRemoved += (identity, reason) =>
            {
                if (reason == EnemyRemovalReason.Forced)
                {
                    stoppedAtForcedRemoval.Add(assembly.WaveManager.IsStopped);
                }
            };

            runtime.EnterSettling();

            Assert.IsTrue(runtime.IsSettling, "已进入 Settling。");
            Assert.GreaterOrEqual(stoppedAtForcedRemoval.Count, 1, "Settling 应至少提交一次 Forced 移除事实。");
            foreach (bool stopped in stoppedAtForcedRemoval)
            {
                Assert.IsTrue(stopped, "Forced 移除发生时 WaveManager 必须已 Stop（Stop 先于 EnemyManager.GameOver）。");
            }

            Assert.IsTrue(assembly.WaveManager.IsStopped, "Settling 后 WaveManager 已停止。");
            Assert.AreEqual(0, assembly.WaveManager.ActiveHandleCount, "Settling 后无活动 handle。");
            Assert.AreEqual(0, assembly.EnemyManager.Count, "Settling 清空敌人集合。");
            Assert.AreEqual(0, assembly.BuffManager.ActiveInstanceCount, "Settling 清空 Buff 实例。");
            Assert.AreEqual(0, assembly.BuffManager.RegisteredTargetCount, "Settling 清空目标登记。");
            Assert.AreEqual(0, assembly.BuffManager.OwnedScheduleCount, "Settling 取消 Buff 到期调度。");
            Assert.IsFalse(assembly.WaveManager.AllWavesCompleted, "Settling 不得促成波次完成。");
            Assert.AreEqual(0, completedCalls, "Settling 不发布 AllConfiguredWavesCompleted。");

            runtime.Dispose();
        }

        [Test]
        [Description("task 4.9：未进入 Settling 的 Dispose（退出/取消路径）同样先 Stop WaveManager " +
                     "再 EnemyManager.GameOver，Forced 移除事实在已停止状态下发布。")]
        public void DisposeWithoutSettling_StopsWaveManagerBeforeEnemyManagerGameOver()
        {
            var poolScope = new BattlePoolScope();
            BattleRuntimeAssembly assembly = CreateBattleRuntime(poolScope, out _);
            var runtime = new BattleRuntime(assembly);

            assembly.WaveManager.Update(1000);
            Assert.AreEqual(2, assembly.WaveManager.ActiveHandleCount, "出生后有活动 handle。");
            Assert.AreEqual(2, assembly.BuffManager.RegisteredTargetCount, "生产 EnemyManager 自动登记 Buff 目标。");
            ApplyDurationBuffToFirstEnemy(assembly);

            var stoppedAtForcedRemoval = new List<bool>();
            assembly.EnemyManager.WaveEntityRemoved += (identity, reason) =>
            {
                if (reason == EnemyRemovalReason.Forced)
                {
                    stoppedAtForcedRemoval.Add(assembly.WaveManager.IsStopped);
                }
            };

            runtime.Dispose(); // 未进入 Settling 直接销毁（退出路径）。

            Assert.IsTrue(runtime.IsDisposed, "已销毁。");
            Assert.GreaterOrEqual(stoppedAtForcedRemoval.Count, 1, "Dispose 应至少提交一次 Forced 移除事实。");
            foreach (bool stopped in stoppedAtForcedRemoval)
            {
                Assert.IsTrue(stopped, "Forced 移除发生时 WaveManager 必须已 Stop（Dispose 也先 Stop）。");
            }

            Assert.IsTrue(assembly.WaveManager.IsStopped, "Dispose 后 WaveManager 已停止。");
            Assert.AreEqual(0, assembly.WaveManager.ActiveHandleCount, "Dispose 后无活动 handle。");
            Assert.AreEqual(0, assembly.EnemyManager.Count, "Dispose 清空敌人集合。");
            Assert.AreEqual(0, assembly.BuffManager.ActiveInstanceCount, "Dispose 清空 Buff 实例。");
            Assert.AreEqual(0, assembly.BuffManager.RegisteredTargetCount, "Dispose 清空目标登记。");
            Assert.AreEqual(0, assembly.BuffManager.OwnedScheduleCount, "Dispose 取消 Buff 到期调度。");

            runtime.Dispose(); // 重复 Dispose 幂等。
        }
    }
}
