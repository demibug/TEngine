'use strict';

/** DEVELOPMENT_ONLY：可观测的暂缓模块调用契约。 */
class DevelopmentLifecycleService {
  constructor(name, options = {}) {
    this.name = name;
    this.calls = [];
    this.originalStartGameWasEmpty = Boolean(options.originalStartGameWasEmpty);
  }
  init() { this.calls.push({ method: 'init' }); }
  startGame() { this.calls.push({ method: 'startGame', originalStartGameWasEmpty: this.originalStartGameWasEmpty }); }
  gameOver(...args) { this.calls.push({ method: 'gameOver', args }); }
}

class DevelopmentSpecialSpawnPolicy {
  constructor({ tutorialActive = false, enableSpecialSpawn = false } = {}) {
    this.tutorialActive = tutorialActive;
    this.enableSpecialSpawn = enableSpecialSpawn;
  }
  isTutorialActive() { return this.tutorialActive; }
  shouldMarkSpecialSpawn() { return this.enableSpecialSpawn; }
}

class DevelopmentMatchPreparation extends DevelopmentLifecycleService {
  constructor() {
    super('MatchPreparation');
    this.playerActiveProps = [];
    this.playerPassiveProps = [];
    this.opponentActiveProps = [];
    this.opponentPassiveProps = [];
    this.battleStarted = false;
  }
  prepareRank() { this.calls.push({ method: 'prepareRank' }); }
  prepareProps() { this.calls.push({ method: 'prepareProps' }); }
  prepareBeforeMatch() { this.prepareRank(); this.prepareProps(); }
  markBattleStarted(value) { this.battleStarted = Boolean(value); this.calls.push({ method: 'markBattleStarted', value: Boolean(value) }); }
  hasAnyDisplayedProps() {
    return this.playerActiveProps.length + this.playerPassiveProps.length + this.opponentActiveProps.length + this.opponentPassiveProps.length > 0;
  }
  gameOver(...args) { super.gameOver(...args); this.battleStarted = false; }
}

class DevelopmentLoadingEffects extends DevelopmentLifecycleService {
  constructor() { super('LoadingAndBattleEffects'); }
  animateLoading(target, frames, intervalMs) {
    this.calls.push({ method: 'animateLoading', targetName: target && target.name, frames: frames.slice(), intervalMs });
  }
}

class DevelopmentTutorialController extends DevelopmentLifecycleService {
  constructor() { super('TutorialController'); this.beforeCount = 0; this.afterCount = 0; }
  beforeBattle() { this.beforeCount += 1; this.calls.push({ method: 'beforeBattle' }); }
  afterBattleSceneCreated() { this.afterCount += 1; this.calls.push({ method: 'afterBattleSceneCreated' }); }
}

class DevelopmentBattleTimingOverride extends DevelopmentLifecycleService {
  constructor(gameData, delayOverrideMs) {
    super('PreBattlePreparation');
    this.gameData = gameData;
    this.delayOverrideMs = delayOverrideMs;
  }
  startGame() {
    super.startGame();
    if (this.delayOverrideMs !== null && this.delayOverrideMs !== undefined) {
      this.gameData.battle.delayTime = this.delayOverrideMs;
      this.calls.push({ method: 'applyDevelopmentDelayOverride', value: this.delayOverrideMs });
    }
  }
}

module.exports = { DevelopmentLifecycleService, DevelopmentSpecialSpawnPolicy, DevelopmentMatchPreparation, DevelopmentLoadingEffects, DevelopmentTutorialController, DevelopmentBattleTimingOverride };
