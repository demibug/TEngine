'use strict';

const CLASS_CACHE = new WeakMap();
const BOUNDS_CACHE = new Map();

/**
 * Recovered from bundle.strings-decoded.js:14170-14280 (original symbol tk).
 * Creates the LayaAir 3.3 Spine2D wrapper used by the original game.
 */
function createLayaSpineAnimationClass(Laya) {
  if (!Laya || !Laya.Sprite || !Laya.Spine2DRenderNode) {
    throw new TypeError('Laya.Sprite and Laya.Spine2DRenderNode are required');
  }
  if (CLASS_CACHE.has(Laya)) return CLASS_CACHE.get(Laya);

  class LayaSpineAnimation extends Laya.Sprite {
    constructor(source) {
      super();
      this.initialPlaybackRate = 1;
      this.spine = this.addComponent(Laya.Spine2DRenderNode);
      this.spine.source = source;
      this.source = source;
      this._syncBoundsWhenReady();
    }

    _syncBoundsWhenReady(retries = 60) {
      if (this.isReady()) {
        const cached = BOUNDS_CACHE.get(this.source);
        const bounds = cached || this.getBounds();
        if (!cached) BOUNDS_CACHE.set(this.source, bounds);
        if (bounds) this.size(bounds.width || 0, bounds.height || 0);
        return;
      }
      if (retries > 0 && !this.destroyed && Laya.timer) {
        Laya.timer.frameOnce(1, this, this._syncBoundsWhenReady, [retries - 1]);
      }
    }

    runWhenReady(callback, retries = 60) {
      if (this.isReady()) callback();
      else if (retries > 0 && !this.destroyed && Laya.timer) {
        Laya.timer.frameOnce(1, this, this.runWhenReady, [callback, retries - 1]);
      }
    }

    isReady() {
      return Boolean(this.spine && (this.spine.templet || (this.spine.getSkeleton && this.spine.getSkeleton())));
    }

    play(name, loop = false, trackIndex, start, end, freshSkin, playAudio) {
      this.runWhenReady(() => this.spine.play(name, loop, trackIndex, start, end, freshSkin, playAudio));
      return this;
    }

    stop() { if (this.spine) this.spine.stop(); return this; }
    Td() { if (Laya.timer) Laya.timer.clearAll(this); return this.stop(); }
    resetForPool() { this.Td(); this.removeSelf(); this.alpha = 1; this.rotation = 0; this.scale(1, 1); return this; }
    setInitPlaybackRate(rate) { this.initialPlaybackRate = Number(rate) || 1; return this; }
    playbackRate(rate) { if (this.spine) this.spine.playbackRate(this.initialPlaybackRate * rate); return this; }
    bm(rate) { return this.playbackRate(rate); }
    offset(x, y) { if (this.spine) this.spine.offset = new Laya.Vector2(x, y); return this; }
    setIsFastMode(fast) { this.runWhenReady(() => fast ? this.spine.changeFast() : this.spine.changeNormal()); return this; }
    setAutoAdjust(value) { if (this.spine) this.spine.autoAdjust = value; return this; }
    showSkinByName(name) { if (this.spine) this.spine.showSkinByName(name); return this; }
    onStop(callback, caller = this) {
      if (typeof this.once === 'function' && Laya.Event) this.once(Laya.Event.STOPPED, caller, callback);
      return this;
    }
    recover() { this.Td(); this.removeSelf(); return this; }
    destroy(destroyChild = true) { if (Laya.timer) Laya.timer.clearAll(this); super.destroy(destroyChild); }
  }

  CLASS_CACHE.set(Laya, LayaSpineAnimation);
  return LayaSpineAnimation;
}

function createLayaSpineAnimation(Laya, source) {
  const Type = createLayaSpineAnimationClass(Laya);
  return new Type(source);
}

module.exports = { createLayaSpineAnimationClass, createLayaSpineAnimation };
