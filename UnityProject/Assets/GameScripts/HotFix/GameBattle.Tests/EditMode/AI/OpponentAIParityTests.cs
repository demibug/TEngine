using System;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests
{
    [TestFixture]
    public sealed class OpponentAIParityTests
    {
        [Test]
        public void FixedSeedReplay_ProducesStableActionLog()
        {
            string first = RunReplay(20260830);
            string second = RunReplay(20260830);

            Assert.IsNotEmpty(first);
            Assert.AreEqual(first, second);
            StringAssert.Contains("action=", first);
            StringAssert.Contains("result=ok", first);
        }

        private static string RunReplay(int seed)
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            BattleLoadoutDto loadout = new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: seed,
                configVersion: 0,
                configHash: string.Empty,
                opponentMode: BattleOpponentMode.LocalAI,
                opponentAiDifficulty: OpponentAiDifficulty.Expert);
            var replayLog = new OpponentAiReplayLog();
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout,
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot,
                opponentAiReplayLog: replayLog);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);

            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                runtime.OpponentAI.StartGame();
                for (int i = 0; i < 12; i++)
                {
                    runtime.OpponentAI.Update(500);
                }

                return replayLog.ToStableText();
            }
            finally
            {
                runtime.Dispose();
            }
        }
    }
}
