# Single Game Flow

1. `GameBootstrap` opens LoadScene and MainScene.
2. `MainScene.startGame()` verifies and spends 5 stamina.
3. MatchScene starts `BattleFlowCoordinator.startBattle()`.
4. Runtime starts economy, deck, managers, input and AI.
5. Deck draws five cards per side. Refresh starts at 10 and grows by 2.
6. `PurchaseAndPlace` validates the grid, spends currency and creates via UnitFactory.
7. AI uses the same deck/input/factory path.
8. BattleManager starts WaveManager once both sides placed or delay expires.
9. Normal and Boss waves spawn. Units select targets and attack through weapon/projectile runtimes.
10. A-Dou health reaching zero emits `BATTLE_FINISHED`.
11. BattleFlow creates BattleResult and clears all combat services.
12. GameOver opens; returning reopens MainScene.

The deterministic smoke report is `analysis/smoke/single-game-flow.json`.
