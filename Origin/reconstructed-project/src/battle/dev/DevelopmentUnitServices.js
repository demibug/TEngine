'use strict';

/**
 * DEVELOPMENT_ONLY：为缺失的正式 Spine 兵种表现提供事件等价驱动。
 * 正式单位仍只调用动画对象的 Laya 风格接口，不依赖驱动器类型。
 */
function decorateDevelopmentUnitAnimation(animation, key, owner, animationDriver = null) {
  animation.key = key;
  animation.ownerUnit = owner;
  animation.playCalls = [];
  animation.initialRate = 1;
  animation.rate = 1;
  animation.stopped = false;
  animation.size(80, 80);
  animation.setInitPlaybackRate = function setInitPlaybackRate(value) {
    if (!(value > 0)) throw new RangeError('Initial animation playback rate must be positive');
    this.initialRate = value;
  };
  animation.playbackRate = function playbackRate(value) {
    if (!(value > 0)) throw new RangeError('Animation playback rate must be positive');
    this.rate = value;
  };
  animation.play = function play(name, loop, force = false, startMs = 0, endMs = null) {
    const call = { name, loop: Boolean(loop), force: Boolean(force), startMs, endMs };
    this.playCalls.push(call);
    this.stopped = false;
    if (animationDriver) animationDriver.cancel(this, 'animation-replaced');
    if (!loop) {
      this.lastNonLoopAnimation = name;
      if (Number.isFinite(endMs)) {
        animationDriver.playSegment(this, {
          owner: this.ownerUnit,
          name,
          startMs,
          endMs,
          effectivePlaybackRate: this.initialRate * this.rate,
        });
      }
    }
    return this;
  };
  animation.stop = function stop() {
    this.stopped = true;
    if (animationDriver) animationDriver.cancel(this, 'animation-stopped');
  };
  return animation;
}

class DevelopmentUnitPresentation {
  constructor({ laya, animationDriver = null } = {}) {
    if (!laya) throw new TypeError('DevelopmentUnitPresentation requires Laya');
    this.laya = laya;
    this.animationDriver = animationDriver;
    this.calls = [];
  }

  createSoldierVisual() {
    const root = new this.laya.Sprite();
    root.name = 'soldier';
    root.size(80, 80);
    const level = new this.laya.Text();
    level.name = 'lvl';
    level.value = '1';
    level.text = '1';
    level.visible = true;
    root.addChild(level);
    this.calls.push(['createSoldierVisual']);
    return root;
  }

  resetSoldierVisual(root) {
    this.calls.push(['resetSoldierVisual', root.name]);
    for (const child of root.children.slice()) {
      if (child.name !== 'lvl') child.removeSelf();
    }
    const level = root.getChildByName('lvl');
    if (level) { level.value = '1'; level.text = '1'; level.visible = true; }
    root.name = 'soldier';
    root.pos(0, 0);
    root.scale(1, 1);
    root.rotation = 0;
    root.alpha = 1;
    root.visible = true;
    root.filters = null;
    root.offAll();
  }

  createAnimation(unit, key) {
    if (!['knife', 'bow'].includes(key)) throw new Error(`Development unit animation ${key} is not restored`);
    this.calls.push(['createAnimation', key]);
    return decorateDevelopmentUnitAnimation(new this.laya.Sprite(), key, unit, this.animationDriver);
  }

  resetAnimation(animation) {
    this.calls.push(['resetAnimation', animation.key]);
    animation.offAll();
    animation.stop();
    animation.initialRate = 1;
    animation.rate = 1;
    animation.ownerUnit = null;
    animation.pos(0, 0);
    animation.scale(1, 1);
    animation.rotation = 0;
    animation.visible = false;
  }
}

class DevelopmentUnitAudio {
  constructor() { this.calls = []; }
  play(name) { this.calls.push(name); }
}

class DevelopmentKnifeEffects {
  constructor() { this.calls = []; }
  startKnifeAttack(record, attacker, target) {
    this.calls.push(['startKnifeAttack', record.type, attacker.id, target.id, record.delayMs]);
  }
  showKnifeHit(record, attacker, enemy) {
    this.calls.push(['showKnifeHit', record.type, attacker.id, enemy.id, record.damage]);
  }
}

module.exports = {
  decorateDevelopmentUnitAnimation,
  DevelopmentUnitPresentation,
  DevelopmentUnitAudio,
  DevelopmentKnifeEffects,
};
