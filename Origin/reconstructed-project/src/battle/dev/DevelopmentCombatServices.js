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
  // Zombie 专属表现 stub（bundle:31970-32131 uB/gB/bubble/tB/dB 表现层）。
  // 开发桩只记录调用 + 创建 mock 节点，不实现真实 Tween/贴图渲染（P2 表现层接入时由 LayaEnemyProduction 承载）。
  createSwampDecal(enemy) {
    this.calls.push(['createSwampDecal', enemy.id]);
    const pB = new this.laya.Image('resources/img/gameObject/enemy/swamp.png');
    pB.name = 'swampDecal'; pB.size(64, 32); pB.pos(8, 47); pB.alpha = 0; pB.zIndex = -1;
    enemy.visual.addChild(pB);
    const yB = new this.laya.Sprite(); yB.name = 'swampMask'; enemy.visual.addChild(yB);
    yB.graphics.drawRect(0, 0, enemy.visual.width, 0, '#fff');
    return { pB, yB };
  }
  createBubbleParticle(enemy) {
    this.calls.push(['createBubbleParticle', enemy.id]);
    const bubble = new this.laya.Sprite(); bubble.size(16, 16); bubble.alpha = 1;
    enemy.visual.addChild(bubble);
    return bubble;
  }
  recoverBubbleParticle(bubble) {
    this.calls.push(['recoverBubbleParticle']);
    if (bubble && typeof bubble.removeSelf === 'function') bubble.removeSelf();
  }
  startZombieBreathing(enemy) { this.calls.push(['startZombieBreathing', enemy.id]); }
  stopZombieBreathing(enemy) {
    this.calls.push(['stopZombieBreathing', enemy && enemy.id]);
    if (this.laya.Tween && typeof this.laya.Tween.killAll === 'function' && enemy && enemy.animation) this.laya.Tween.killAll(enemy.animation);
    if (enemy && enemy.animation && typeof enemy.animation.scale === 'function') enemy.animation.scale(1, 1);
  }
  // Cavalry 专属表现 stub（bundle:32390-32467 init/gameOver/fw/mw/tB 表现层）。
  // 开发桩只记录调用 + 创建/移除 mock 节点，不实现真实 Tween/贴图渲染（P2 表现层接入时由 LayaEnemyProduction 承载）。
  createCavalryAura(enemy, auraResource) {
    this.calls.push(['createCavalryAura', enemy.id, auraResource]);
    let aura = enemy.visual.getChildByName && enemy.visual.getChildByName('cavalryAura');
    if (!aura) {
      aura = new this.laya.Image(auraResource);
      aura.name = 'cavalryAura'; aura.size(80, 30); aura.pos(0, 40); aura.zIndex = -1;
      enemy.visual.addChild(aura);
    }
    return aura;
  }
  removeCavalryAura(enemy) {
    this.calls.push(['removeCavalryAura', enemy && enemy.id]);
    const aura = enemy && enemy.visual && enemy.visual.getChildByName && enemy.visual.getChildByName('cavalryAura');
    if (aura && typeof aura.removeSelf === 'function') aura.removeSelf();
  }
  startCavalryBreathing(enemy) { this.calls.push(['startCavalryBreathing', enemy.id]); }
  stopCavalryBreathing(enemy) {
    this.calls.push(['stopCavalryBreathing', enemy && enemy.id]);
    if (this.laya.Tween && typeof this.laya.Tween.killAll === 'function' && enemy && enemy.animation) this.laya.Tween.killAll(enemy.animation);
    if (enemy && enemy.animation && typeof enemy.animation.scale === 'function') enemy.animation.scale(1, 1);
  }
  // Puppet 专属表现 stub（bundle:31890-31919 rB 表现层）。
  // 开发桩只记录调用 + 创建/回收 mock 节点，不实现真实 Tween/贴图渲染（P2 表现层接入时由 LayaEnemyProduction 承载）。
  createPuppetHeart(enemy) {
    this.calls.push(['createPuppetHeart', enemy.id]);
    const heart = new this.laya.Sprite(); heart.size(16, 16); heart.alpha = 1; heart.scaleX = 0; heart.scaleY = 0;
    return heart;
  }
  updatePuppetHeart(enemy, heart, deltaMs) { this.calls.push(['updatePuppetHeart', enemy && enemy.id]); }
  recoverPuppetHeart(heart) {
    this.calls.push(['recoverPuppetHeart']);
    if (heart && typeof heart.removeSelf === 'function') heart.removeSelf();
    if (heart) heart.alpha = 1;
  }
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
