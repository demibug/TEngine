'use strict';

/** DEVELOPMENT_RENDER_STUB：只实现敌人正式逻辑实际调用到的表现契约。 */
class DevelopmentEnemyAnimation {
  constructor(laya, resourcePath) {
    const node = new laya.Sprite();
    node.resourcePath = resourcePath;
    node.playLog = [];
    node.playbackRate = 1;
    node.recovered = false;
    node.play = function play(name, loop) { this.playLog.push([name, Boolean(loop)]); this.currentAnimation = name; };
    node.bm = function setPlaybackRate(rate) { this.playbackRate = rate; };
    node.recover = function recover() { this.recovered = true; };
    node.onStop = function onStop(callback) { this._onStop = callback; };
    node.DE = function removeAnimationEvent() { this._onStop = null; };
    return node;
  }
}

class DevelopmentEnemyPresentation {
  constructor({ laya, spawnDurationMs = 0, deathDurationMs = 100 } = {}) {
    if (!laya || !laya.Sprite) throw new TypeError('DevelopmentEnemyPresentation requires Laya');
    this.laya = laya;
    this.spawnDurationMs = spawnDurationMs;
    this.deathDurationMs = deathDurationMs;
    this.calls = [];
  }

  createMobVisual() {
    const root = new this.laya.Sprite();
    root.developmentTag = 'DEVELOPMENT_RENDER_STUB';
    root.size(80, 80);
    const hpBg = new this.laya.Sprite(); hpBg.name = 'hpBgImg'; hpBg.size(60, 8);
    const hp1 = new this.laya.Image(); hp1.name = 'hpImg1'; hp1.size(60, 8);
    const hp2 = new this.laya.Image(); hp2.name = 'hpImg2'; hp2.size(60, 8);
    const hpNum = new this.laya.Text(); hpNum.name = 'hpNum'; hpNum.text = '';
    hpBg.addChild(hp1); hpBg.addChild(hp2); hpBg.addChild(hpNum);
    const shadow = new this.laya.Sprite(); shadow.name = 'shadow'; shadow.visible = true;
    const stun = new this.laya.Sprite(); stun.name = 'stun'; stun.visible = false;
    root.addChild(hpBg); root.addChild(shadow); root.addChild(stun);
    return root;
  }

  resetMobVisual(visual) {
    visual.offAll();
    visual.removeSelf();
    visual.visible = true;
    visual.alpha = 1;
    visual.anchorX = 0;
    visual.anchorY = 0;
    visual.rotation = 0;
    visual.scale(1, 1);
    visual.pos(0, 0);
    visual.name = '';
    for (const child of visual.children.slice()) {
      if (child.name === 'sp') child.removeSelf();
    }
    const hpBg = visual.getChildByName('hpBgImg');
    if (hpBg) {
      hpBg.visible = true;
      const hp1 = hpBg.getChildByName('hpImg1');
      const hp2 = hpBg.getChildByName('hpImg2');
      const hpNum = hpBg.getChildByName('hpNum');
      if (hp1) hp1.width = 60;
      if (hp2) hp2.width = 60;
      if (hpNum) { hpNum.text = ''; hpNum.visible = true; }
    }
    const stun = visual.getChildByName('stun'); if (stun) stun.visible = false;
  }

  createAnimation(_enemy, resourcePath) {
    const animation = new DevelopmentEnemyAnimation(this.laya, resourcePath);
    this.calls.push(['createAnimation', resourcePath]);
    return animation;
  }

  playSpawn(enemy, complete) {
    this.calls.push(['playSpawn', enemy.id, this.spawnDurationMs]);
    enemy.visual.visible = true;
    enemy.visual.anchorX = 0.5;
    enemy.visual.anchorY = 1;
    enemy.visual.scale(0, 0);
    enemy.visual.pos(enemy.visual.x + enemy.visual.width / 2, enemy.visual.y + enemy.visual.height / 2);
    this.laya.timer.once(this.spawnDurationMs, enemy, () => {
      enemy.visual.scale(1, 1);
      enemy.visual.anchorX = 0;
      enemy.visual.anchorY = 0;
      enemy.visual.pos(enemy.visual.x - enemy.visual.width / 2, enemy.visual.y - enemy.visual.height);
      complete();
    });
  }

  playDeath(enemy, color, complete) {
    this.calls.push(['playDeath', enemy.id, color, this.deathDurationMs]);
    this.laya.timer.once(this.deathDurationMs, enemy, () => {
      enemy.visual.alpha = 0;
      complete();
    });
  }

  setMovePlaybackRate(enemy, rate) {
    this.calls.push(['setMovePlaybackRate', enemy.id, rate]);
    if (enemy.animation && typeof enemy.animation.bm === 'function') enemy.animation.bm(rate);
  }
  startMoving(enemy) { this.calls.push(['startMoving', enemy.id]); }
  stopMoving(enemy) { this.calls.push(['stopMoving', enemy.id]); }
  startMob0Breathing(enemy) { this.calls.push(['startMob0Breathing', enemy.id]); }
  stopMob0Breathing(enemy) { this.calls.push(['stopMob0Breathing', enemy.id]); if (enemy.animation) enemy.animation.scale(1, 1); }
  playHitReaction(enemy, damage) { this.calls.push(['playHitReaction', enemy.id, damage]); }
  resetAnimation(animation) {
    animation.scale(1, 1);
    animation.skewX = 0;
    animation.alpha = 1;
    animation.rotation = 0;
  }
  resetForPool(enemy) { this.calls.push(['resetForPool', enemy.id]); }
  createFootprint() { return null; }
}

class DevelopmentEnemyAudio {
  constructor() { this.calls = []; }
  playHit(enemy) { this.calls.push(['enemy_hit', enemy.id]); }
  playDeath(enemy) { this.calls.push(['enemy_dead', enemy.id]); }
  playContactAttack(enemy) { this.calls.push(['enemy_knife_attack', enemy.id]); }
}

class DevelopmentEnemyEffects {
  constructor() { this.calls = []; }
  showHit(enemy, damage, attacker) { this.calls.push(['hit', enemy.id, damage, attacker && attacker.id]); }
  showDeath(enemy) { this.calls.push(['death', enemy.id]); }
  showContactAttack(enemy) { this.calls.push(['contactAttack', enemy.id]); }
  showDamageNumber(enemy, damage) { this.calls.push(['damageNumber', enemy.id, damage]); }
}

class DevelopmentEnemyRewardService {
  constructor(battleState, economy = null) { this.battleState = battleState; this.economy = economy; this.calls = []; }
  onEnemyKilled({ enemy, amount, playerLane }) {
    this.calls.push({ enemyId: enemy.id, amount, playerLane, boss: Boolean(enemy && enemy.isBoss) });
    if (this.economy && typeof this.economy.award === 'function') this.economy.award(Boolean(playerLane), amount, 'kill');
    else if (playerLane) this.battleState.gold += amount;
    else this.battleState.opponentGold += amount;
    this.battleState.killCount += 1;
    if (enemy && enemy.isBoss) this.battleState.bossKillCount += 1;
  }
}

module.exports = {
  DevelopmentEnemyAnimation,
  DevelopmentEnemyPresentation,
  DevelopmentEnemyAudio,
  DevelopmentEnemyEffects,
  DevelopmentEnemyRewardService,
};
