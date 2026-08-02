# Combat Lifecycle

`BattleFlowCoordinator` owns orchestration and listens to `GameEvents.BATTLE_FINISHED`. Start order and cleanup order are also exported from `src/battle/CombatLifecycle.js`. All active collections must be empty after cleanup.
